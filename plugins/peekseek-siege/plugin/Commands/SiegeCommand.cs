using System;
using System.Collections.Generic;
using PeekSeekSiege.Core;
using Rocket.API;
using Rocket.Unturned.Player;

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
    public string Syntax => "[skip def|skip atk|side]";
    public List<string> Aliases => [];
    public List<string> Permissions => ["peekseek.siege.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
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
        if (siege.Phase == SiegePhase.Building) {
            siege.FinishBuilding();
            return;
        }

        int defender = RoundOrder.Defender(siege.Round);
        bool toAttackers = winner.StartsWith("atk", StringComparison.OrdinalIgnoreCase);

        siege.EndRound(toAttackers ? 1 - defender : defender, "round_won_skipped");
    }

    private static void Report(IRocketPlayer caller, Siege siege) {
        StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("siege_state",
            siege.Phase.ToString(),
            siege.Round,
            siege.SecondsLeft,
            siege.TeamName(RoundOrder.Defender(Math.Max(1, siege.Round)))));
    }
}
