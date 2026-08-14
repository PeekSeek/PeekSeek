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
    internal static PSUpdateState Build(PSMatch data, PSUpdateEvent latestEvent, List<PSUpdateKill> feed) {
        int roundsPlayed = StatService.GetTotalPlayedRounds();

        PSUpdateState state = new() {
            Round = roundsPlayed + 1,
            RoundsToWin = data.RoundsToWin,
            Event = latestEvent,
            Feed = [.. feed]
        };

        foreach (PSTeam team in data.Teams) {
            byte wins = StatService.GetStatWins(new CSteamID(team.Players.First()));

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
        int totalAmmo = TotalAmmo(player);
        stats.Equipped = IsEmptyBow(player, totalAmmo) ? 0 : player.equipment.itemID;
        stats.Inventory = ReadInventory(player);
        stats.TotalAmmo = totalAmmo;

        return stats;
    }

    /// A bow that has fired its last arrow — spares in the pack and the one already
    /// nocked included — is just dead weight; there is no way to reload it, so the
    /// overlay should stop showing it rather than a bow icon nobody can use.
    private static bool IsEmptyBow(Player player, int totalAmmo) {
        return totalAmmo == 0 && player.equipment.asset is ItemGunAsset gun && gun.action == EAction.String;
    }

    /// Every magazine's `amount` byte is repurposed by Unturned to mean its own current
    /// round count, not a stack size (see UseableGun.ReceivePlayReload, which reads a
    /// spare magazine's amount straight into the gun's live ammo) — so this is spare
    /// rounds across every magazine/arrow the player is carrying, plus whatever is
    /// already chambered in the gun itself (state[10], the same byte UseableGun syncs
    /// its own live ammo count through).
    private static int TotalAmmo(Player player) {
        int total = 0;

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items container = player.inventory.items[page];
            if (container?.items == null) continue;

            foreach (ItemJar jar in container.items) {
                if (jar.item.GetAsset() is ItemMagazineAsset) total += jar.item.amount;
            }
        }

        if (player.equipment.asset is ItemGunAsset && player.equipment.state?.Length > 10) {
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

    /// One entry per distinct item id with how many of it the player is carrying, over
    /// every page but storage — the same pages a kit is handed out across. Magazines and
    /// arrows are left out entirely: they are folded into TotalAmmo instead, since a
    /// magazine's own "amount" means its current round count, not how many the player
    /// is carrying — counting it here would just be wrong.
    private static List<PSUpdateItem> ReadInventory(Player player) {
        Dictionary<ushort, int> counts = [];
        List<ushort> firstSeen = [];

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items container = player.inventory.items[page];
            if (container?.items == null) continue;

            foreach (ItemJar jar in container.items) {
                if (jar.item.GetAsset() is ItemMagazineAsset) continue;

                ushort id = jar.item.id;

                if (counts.ContainsKey(id)) {
                    counts[id]++;
                    continue;
                }

                counts[id] = 1;
                firstSeen.Add(id);
            }
        }

        return firstSeen.Select(id => Describe(id, counts[id])).ToList();
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
