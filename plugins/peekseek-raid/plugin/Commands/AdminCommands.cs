using System;
using System.Collections.Generic;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekRaid.Commands;

/// The tester's controls, one command each so they are quick to type mid-round. All of
/// them need `peekseek.raid.start`, like `/raid`.
internal static class Admin {
    internal const string Permission = "peekseek.raid.start";

    internal static void Reply(IRocketPlayer caller, string message) {
        if (caller is UnturnedPlayer player) {
            Host.Whisper(player.Player.channel.owner, message);
            return;
        }

        Rocket.Core.Logging.Logger.Log(message);
    }

    /// The running match, or null after telling the caller there is none.
    internal static Raid Running(IRocketPlayer caller) {
        Raid raid = PeekSeekRaid.Raid;
        if (raid?.Match != null) return raid;

        Reply(caller, PeekSeekRaid.Instance.Translate("raid_nothing_running"));
        return null;
    }
}

/// `/ssolo`: play the match this server holds with whoever is here. Everybody on the
/// roster who has not connected counts as present and alive from now on: a team with
/// nobody here gets a plain walled base and a random powerup every round. The match is
/// the site's and is reported as usual. Move it along with `/nextphase`.
public class SoloCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "ssolo";
    public string Help => "Play the held match now, standing in for everybody who is not here.";
    public string Syntax => "";
    public List<string> Aliases => [];
    public List<string> Permissions => [Admin.Permission];

    public void Execute(IRocketPlayer caller, string[] command) {
        string refused = Host.PlaySolo();
        Admin.Reply(caller, refused ?? PeekSeekRaid.Instance.Translate("solo_started"));
    }
}

/// `/nextphase`: end whatever phase is running and go to the next. In a round the
/// defenders win if the flag still stands, the attackers if it is down.
public class NextPhaseCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "nextphase";
    public string Help => "Move the Raid match on one phase.";
    public string Syntax => "";
    public List<string> Aliases => ["np"];
    public List<string> Permissions => [Admin.Permission];

    public void Execute(IRocketPlayer caller, string[] command) {
        Admin.Running(caller)?.NextPhase();
    }
}

/// `/raidtime <seconds>`: set the clock of the phase that is running.
public class RaidTimeCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "raidtime";
    public string Help => "Set the seconds left in the current Raid phase.";
    public string Syntax => "<seconds>";
    public List<string> Aliases => [];
    public List<string> Permissions => [Admin.Permission];

    public void Execute(IRocketPlayer caller, string[] command) {
        Raid raid = Admin.Running(caller);
        if (raid == null) return;

        if (command.Length == 0 || !int.TryParse(command[0], out int seconds)) {
            Admin.Reply(caller, PeekSeekRaid.Instance.Translate("raidtime_usage"));
            return;
        }
        raid.SetSecondsLeft(seconds);
    }
}

/// `/raidwin <atk|def>`: give the round being played to one side.
public class RaidWinCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "raidwin";
    public string Help => "Give the current Raid round to the attackers or the defenders.";
    public string Syntax => "<atk|def>";
    public List<string> Aliases => [];
    public List<string> Permissions => [Admin.Permission];

    public void Execute(IRocketPlayer caller, string[] command) {
        Raid raid = Admin.Running(caller);
        if (raid == null) return;

        bool attackers = command.Length > 0 && command[0].StartsWith("atk", StringComparison.OrdinalIgnoreCase);
        int defender = raid.DefendedSite;
        raid.EndRound(attackers ? 1 - defender : defender, "round_won_skipped");
    }
}

/// `/raidend`: end the match with no winner.
public class RaidEndCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "raidend";
    public string Help => "End the Raid match with no winner.";
    public string Syntax => "";
    public List<string> Aliases => [];
    public List<string> Permissions => [Admin.Permission];

    public void Execute(IRocketPlayer caller, string[] command) {
        if (Admin.Running(caller) != null) Host.EndMatch();
    }
}
