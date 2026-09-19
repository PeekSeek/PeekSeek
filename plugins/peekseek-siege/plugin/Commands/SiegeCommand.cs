using System;
using System.Collections.Generic;
using PeekSeekSiege.Core;
using Rocket.API;
using SDG.Unturned;

namespace PeekSeekSiege.Commands;

/// `/siege`, the tester's console: where the match is, and how to move it along.
///
/// None of this survives contact with a real match — a live gamemode does not let
/// anybody skip a phase or change sides. It exists because a five-minute build phase
/// followed by six three-minute rounds is nearly half an hour of waiting to find out
/// whether round four restores the base correctly, and nobody would find that bug.
public class SiegeCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "siege";
    public string Help => "Where the match is, and the test controls.";
    public string Syntax => "[skip def|skip atk|side|find <text>]";
    public List<string> Aliases => [];
    public List<string> Permissions => ["peekseek.siege.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        // Answering "what is that item called" needs no match running — it is the thing
        // you reach for when a configured gear name turned out not to exist.
        if (command.Length > 0 && command[0].Equals("find", StringComparison.OrdinalIgnoreCase)) {
            Find(caller, command.Length > 1 ? string.Join(" ", command, 1, command.Length - 1) : "");
            return;
        }

        Siege siege = SiegeHost.Current;
        if (siege == null) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("start_nothing_running"));
            return;
        }

        if (command.Length == 0) {
            Report(caller, siege);
            return;
        }

        switch (command[0].ToLowerInvariant()) {
            case "skip":
                Skip(siege, command.Length > 1 ? command[1] : "def");
                break;

            case "side":
                if (!siege.Match.Solo) {
                    StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("side_solo_only"));
                    return;
                }
                siege.SwapSoloSide();
                break;

            default:
                Report(caller, siege);
                break;
        }
    }

    /// End whatever phase is running. A skipped build phase is simply finished early; a
    /// skipped round is awarded, defaulting to the defenders because that is what the
    /// clock running out does.
    private static void Skip(Siege siege, string winner) {
        if (siege.Phase == SiegePhase.Shaping) {
            siege.FinishShaping();
            return;
        }
        if (siege.Phase == SiegePhase.Building) {
            siege.FinishBuilding();
            return;
        }

        int defender = RoundOrder.Defender(siege.Round);
        bool toAttackers = winner.StartsWith("atk", StringComparison.OrdinalIgnoreCase);

        siege.EndRound(toAttackers ? 1 - defender : defender, "round_won_skipped");
    }

    /// Report the items whose name contains some text, with their ids, so a name for
    /// BuildTools or FallbackOutfit can be found without opening the bundles.
    private static void Find(IRocketPlayer caller, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("find_usage"));
            return;
        }

        List<ItemAsset> found = Core.SiegeWorld.Search(text);
        if (found.Count == 0) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("find_none", text));
            return;
        }

        foreach (ItemAsset item in found.GetRange(0, Math.Min(found.Count, FindLimit))) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("find_hit", item.itemName, item.id));
        }

        if (found.Count > FindLimit) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("find_more", found.Count - FindLimit));
        }
    }

    /// Chat is a slow way to read a list, so a broad search says how much it is not
    /// showing rather than filling the screen.
    private const int FindLimit = 12;

    private static void Report(IRocketPlayer caller, Siege siege) {
        StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("siege_state",
            siege.Phase.ToString(),
            siege.Round,
            siege.SecondsLeft,
            siege.TeamName(RoundOrder.Defender(Math.Max(1, siege.Round)))));
    }
}
