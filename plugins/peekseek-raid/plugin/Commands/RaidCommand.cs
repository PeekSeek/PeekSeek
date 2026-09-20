using System;
using System.Collections.Generic;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;

namespace PeekSeekRaid.Commands;

/// `/raid`, the tester's console: where the match is, and how to move it along.
///
/// The match itself is started with Match's `/start raid` or `/start raid solo`.
///
/// None of this survives contact with a real match — a live gamemode does not let
/// anybody skip a phase or change sides. It exists because a five-minute build phase
/// followed by six three-minute rounds is nearly half an hour of waiting to find out
/// whether round four restores the base correctly, and nobody would find that bug.
public class RaidCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "raid";
    public string Help => "Where the match is, and the test controls.";
    public string Syntax => "[skip def|skip atk|find <text>]";
    public List<string> Aliases => [];
    public List<string> Permissions => ["peekseek.raid.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        // Answering "what is that item called" needs no match running — it is the thing
        // you reach for when a configured gear name turned out not to exist.
        if (command.Length > 0 && command[0].Equals("find", StringComparison.OrdinalIgnoreCase)) {
            Find(caller, command.Length > 1 ? string.Join(" ", command, 1, command.Length - 1) : "");
            return;
        }

        Raid raid = PeekSeekRaid.Raid;
        if (raid?.Match == null) {
            Reply(caller, PeekSeekRaid.Instance.Translate("raid_nothing_running"));
            return;
        }

        if (command.Length == 0) {
            Report(caller, raid);
            return;
        }

        switch (command[0].ToLowerInvariant()) {
            case "skip":
                Skip(raid, command.Length > 1 ? command[1] : "def");
                break;

            default:
                Report(caller, raid);
                break;
        }
    }

    /// End whatever phase is running. A skipped build phase is simply finished early; a
    /// skipped round is awarded, defaulting to the defenders because that is what the
    /// clock running out does.
    private static void Skip(Raid raid, string winner) {
        if (raid.Phase == RaidPhase.Shaping) {
            raid.FinishShaping();
            return;
        }
        if (raid.Phase == RaidPhase.Building) {
            raid.FinishBuilding();
            return;
        }

        int defender = RoundOrder.Defender(raid.Round);
        bool toAttackers = winner.StartsWith("atk", StringComparison.OrdinalIgnoreCase);

        raid.EndRound(toAttackers ? 1 - defender : defender, "round_won_skipped");
    }

    /// Report the items whose name contains some text, with their ids, so a name for
    /// BuildTools or FallbackOutfit can be found without opening the bundles.
    private static void Find(IRocketPlayer caller, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            Reply(caller, PeekSeekRaid.Instance.Translate("find_usage"));
            return;
        }

        List<ItemAsset> found = Core.RaidWorld.Search(text);
        if (found.Count == 0) {
            Reply(caller, PeekSeekRaid.Instance.Translate("find_none", text));
            return;
        }

        foreach (ItemAsset item in found.GetRange(0, Math.Min(found.Count, FindLimit))) {
            Reply(caller, PeekSeekRaid.Instance.Translate("find_hit", item.itemName, item.id));
        }

        if (found.Count > FindLimit) {
            Reply(caller, PeekSeekRaid.Instance.Translate("find_more", found.Count - FindLimit));
        }
    }

    /// Chat is a slow way to read a list, so a broad search says how much it is not
    /// showing rather than filling the screen.
    private const int FindLimit = 12;

    private static void Reply(IRocketPlayer caller, string message) {
        if (caller is UnturnedPlayer player) {
            Host.Whisper(player.Player.channel.owner, message);
            return;
        }

        Rocket.Core.Logging.Logger.Log(message);
    }

    private static void Report(IRocketPlayer caller, Raid raid) {
        Reply(caller, PeekSeekRaid.Instance.Translate("raid_state",
            raid.Phase.ToString(),
            raid.Round,
            raid.SecondsLeft,
            raid.TeamName(RoundOrder.Defender(Math.Max(1, raid.Round)))));
    }
}
