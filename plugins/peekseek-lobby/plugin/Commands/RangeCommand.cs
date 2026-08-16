using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekLobby.Commands;

/// /range puts a timed row of targets in front of you when you are the only one
/// here, and running it again clears them. For the player who arrives before
/// anybody else, so an empty arena is something to practise in rather than
/// something to leave.
internal class RangeCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "range";
    public string Help => "Starts a timed target run, or clears the one you are on.";
    public string Syntax => "";
    public List<string> Aliases => ["warmup"];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        TargetRange.Toggle((UnturnedPlayer)caller);
    }
}
