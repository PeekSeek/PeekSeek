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
    internal static PSUpdateState Build(PSData data, PSUpdateEvent latestEvent, List<PSUpdateKill> feed) {
        int roundsPlayed = StatService.GetTotalPlayedRounds();

        PSUpdateState state = new() {
            Round = roundsPlayed + 1,
            RoundsToWin = data.Meta.Rounds,
            Event = latestEvent,
            Feed = [.. feed]
        };

        foreach (PSTeam team in data.Teams) {
            byte wins = StatService.GetStatWins(new CSteamID(team.Users.First()));

            PSUpdateTeam updateTeam = new() {
                TeamName = team.Name,
                Wins = wins,
                Losses = roundsPlayed - wins
            };

            foreach (ulong user in team.Users) {
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

        return stats;
    }

    /// Death wins over the safezone: a killed player respawns straight back into the
    /// lobby, and the overlay should still say they lost the round rather than that
    /// they are waiting for the next one.
    private static string RoundState(CSteamID steamID, Player player) {
        if (!StatService.IsAliveThisRound(steamID)) return "dead";
        if (player.movement.isSafe) return "waiting";

        return "alive";
    }

    /// One entry per distinct item id with how many of it the player is carrying, over
    /// every page but storage — the same pages a kit is handed out across.
    private static List<PSUpdateItem> ReadInventory(Player player) {
        Dictionary<ushort, int> counts = [];
        List<ushort> firstSeen = [];

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items container = player.inventory.items[page];
            if (container?.items == null) continue;

            foreach (ItemJar jar in container.items) {
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
