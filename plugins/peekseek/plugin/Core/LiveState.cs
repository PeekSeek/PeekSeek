using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core.Models;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Core;

/// Builds the snapshot the stream overlay renders. Everything here is re-read from the
/// live server each time rather than accumulated, so the overlay cannot drift out of
/// sync with the match no matter how many updates are lost on the way to the backend.
internal static class LiveState {
    internal static PSUpdateState Build(PSMatch data, int roundsPlayed, PSUpdateEvent latestEvent, List<PSUpdateKill> feed) {
        PSUpdateState state = new() {
            Round = roundsPlayed + 1,
            RoundsToWin = data.RoundsToWin,
            Event = latestEvent,
            Feed = [.. feed]
        };

        foreach (PSTeam team in data.Teams) {
            int wins = PSManager.Score(team);

            PSUpdateTeam updateTeam = new() {
                TeamName = team.Name,
                Wins = wins,
                Losses = roundsPlayed - wins
            };

            foreach (ulong user in team.Players) {
                updateTeam.Users.Add(user.ToString(), BuildUser(new CSteamID(user)));
            }

            state.Teams.Add(updateTeam);
        }

        return state;
    }

    private static UserStats BuildUser(CSteamID steamID) {
        UserStats stats = new() {
            Kills = StatService.GetStatKills(steamID),
            Deaths = StatService.GetStatDeaths(steamID),
            Headshots = StatService.GetStatHeadshots(steamID),
            State = "offline"
        };

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return stats;

        stats.State = RoundState(steamID, player);
        stats.Name = player.channel.owner.playerID.characterName;
        stats.Health = player.life.health;
        stats.IsBleeding = player.life.isBleeding;
        stats.IsBroken = player.life.isBroken;
        stats.Equipped = player.equipment.itemID;
        stats.Inventory = ReadInventory(player);
        stats.TotalAmmo = TotalAmmo(player);

        return stats;
    }

    /// A bow or crossbow. PeekSeek hands the crossbow out as a movement tool rather than a
    /// weapon, since nocking an explosive arrow is how a player launches themselves, so it
    /// is kept out of the loadout row entirely: it is only ever held for the moment it
    /// takes to jump, and the overlay's first slot belongs to the gun they fight with.
    private static bool IsBow(ItemAsset asset) {
        return asset is ItemGunAsset gun && gun.action == EAction.String;
    }

    /// Caliber 15 is Unturned's bow caliber, carried by every arrow and by nothing else.
    private const ushort BowCaliber = 15;

    /// An arrow, which is a magazine by asset type but not by any other measure. One arrow
    /// is one shot a player still has, so the overlay counts them as items rather than
    /// folding them into a rounds total that is about the gun in their hands.
    private static bool IsArrow(ItemAsset asset) {
        return asset is ItemMagazineAsset magazine && magazine.calibers?.Contains(BowCaliber) == true;
    }

    /// Every magazine's `amount` byte is repurposed by Unturned to mean its own current
    /// round count, not a stack size (see UseableGun.ReceivePlayReload, which reads a
    /// spare magazine's amount straight into the gun's live ammo), so this is spare rounds
    /// across every magazine the player is carrying, plus whatever is already chambered in
    /// the gun itself (state[10], the same byte UseableGun syncs its own live ammo count
    /// through).
    ///
    /// Arrows are left out at both ends. They are a magazine by asset type, but adding
    /// them here would inflate the count that is meant to answer "how much longer can they
    /// keep shooting", so ReadInventory shows them as items instead.
    private static int TotalAmmo(Player player) {
        int total = 0;

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items container = player.inventory.items[page];
            if (container?.items == null) continue;

            foreach (ItemJar jar in container.items) {
                ItemAsset asset = jar.item.GetAsset();
                if (asset is ItemMagazineAsset && !IsArrow(asset)) total += jar.item.amount;
            }
        }

        if (player.equipment.asset is ItemGunAsset && !IsBow(player.equipment.asset)
            && player.equipment.state?.Length > 10) {
            total += player.equipment.state[10];
        }

        return total;
    }

    /// Death wins over the safezone: a killed player respawns straight back into the
    /// lobby, and the overlay should still say they lost the round rather than that
    /// they are waiting for the next one. Internal rather than private so PSManager's
    /// debug heartbeat can log the exact same value the overlay is being sent, instead
    /// of a second copy of this logic that could quietly drift from it.
    internal static string RoundState(CSteamID steamID, Player player) {
        if (!StatService.IsAliveThisRound(steamID)) return "dead";
        if (player.movement.isSafe) return "waiting";

        return "alive";
    }

    /// One entry per distinct item id with how many of it the player is carrying, in the
    /// order they were first found, over every page but storage: the same pages a kit is
    /// handed out across.
    private static List<PSUpdateItem> ReadInventory(Player player) {
        List<ushort> carried = [];

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items container = player.inventory.items[page];
            if (container?.items == null) continue;

            foreach (ItemJar jar in container.items) {
                ushort id = ShownId(jar.item);
                if (id != 0) carried.Add(id);
            }
        }

        return carried
            .Distinct()
            .Select(id => Describe(id, carried.Count(other => other == id)))
            .ToList();
    }

    /// The item id one carried item puts in the loadout row, or 0 for one the overlay does
    /// not show.
    ///
    /// Magazines are not shown and are folded into TotalAmmo instead, since a magazine's
    /// own "amount" means its current round count rather than how many the player is
    /// carrying, so counting it as an item would just be wrong. Arrows are the exception,
    /// because one arrow is one shot. The bow is left out in their favour, and whatever it
    /// has nocked is counted as if the player were carrying it loose, since the weapon is
    /// the only place that arrow exists.
    private static ushort ShownId(Item item) {
        ItemAsset asset = item.GetAsset();

        if (IsBow(asset)) return NockedArrow(item.state);
        if (asset is ItemMagazineAsset && !IsArrow(asset)) return 0;

        return item.id;
    }

    /// What a bow has nocked, or 0 for an empty one. A gun's state holds the id of the
    /// magazine loaded into it across bytes 8 and 9, and the rounds left in byte 10.
    /// Internal rather than private so that byte layout can be pinned by a test without a
    /// running Unturned host.
    internal static ushort NockedArrow(byte[] state) {
        if (state == null || state.Length <= 10 || state[10] == 0) return 0;

        return (ushort)(state[8] | state[9] << 8);
    }

    private static PSUpdateItem Describe(ushort id, int amount) {
        ItemAsset asset = Assets.find(EAssetType.ITEM, id) as ItemAsset;

        return new PSUpdateItem {
            Id = id,
            Amount = amount,
            Name = asset?.itemName ?? $"Item {id}",
            Kind = asset == null ? "unknown" : asset.type.ToString().ToLowerInvariant()
        };
    }
}
