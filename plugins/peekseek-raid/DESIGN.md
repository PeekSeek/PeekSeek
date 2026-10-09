# RAID: the design, and where it departs from it

RAID is Rainbow Six's attack/defend loop in Unturned: build a base, then take turns
cracking each other's.

This is the reasoning, kept because the code cannot hold it. It was a verbatim transcript
of the original brief; a transcript is a record of a conversation, not a document, and it
carried a local filesystem path and a note about which rented server to clear for testing.
The design it described is below, and the places the implementation knowingly departs from
it are below that, those are the valuable half, and the part that is expensive to
rediscover.

## The design

**Two teams, one map, one arena.** Vanilla Alpha Valley, with the arena placed so neither
base is a long walk from the other and neither team can wander off to peek at the other's.

**Five minutes to build.** Each team gets a budget of building supplies and a fixed
starting floor to build off, so a team cannot split into two bases. Defenders take no
damage during this phase.

**Then the base is saved, and it stops being editable.** From here a round is about what
you bring to it, not what you build.

**Three rounds defending, three attacking.** Tie breakers are two and two, repeating.

**A pick before each round.** Five options a side, defensive traps for the defenders and
raiding tools for the attackers. Chosen on a board, in game.

**Defenders have to stand on a structure.** Off it they take arena damage, after a two
second grace, so holding a position means holding a built position.

**Materials decide what can be opened.** Metal doors and hatches for the defenders. Only
birch walls are assigned to the owning group when a base loads, so a soft wall can be
salvaged to make a new opening and a hard one cannot. Floors, pillars and other walls
never are.

## Where it departs from that

Each with the reason, because each was a constraint rather than a preference.

**The Unity UI cannot ship inside the DLL.** Unturned has exactly one way to draw a custom
UI: `EffectManager.SendUIEffect`, which takes an `EffectAsset`. An `EffectAsset` only
exists inside a Unity asset bundle the client has already downloaded. So the board is
written against both: give it an effect GUID in config and it draws the real Unity layout;
leave the GUID empty and it draws the same five options to chat and takes `/pick`.
[`ui/README.md`](../peekseek/ui/README.md) is the spec for the prefab that GUID points at.

The chat form is **not** a stopgap to delete. A player still downloading the bundle has to
be able to answer the board too.

**The building supplies are a team budget, not an inventory.** A builder carries one of
each piece the team still has; the housing planner lists them, the panel beside the screen
counts them, and placing one takes it off the pile and puts the next in hand. That is what
"a large inventory" was for, without a hundred items on a hands page, and it is what makes
swapping a placed piece for another material a matter of counting.

**The flag is not a claim flag.** A claim flag's 32-metre bubble stops anyone outside the
owning group placing anything inside it, *on the client*, which is exactly the demolition
charge an attacker needs to stick on a wall. The marker is the PeekSeek Flag instead: the
claim flag's model with 600 health and no bubble, placed by one defender before every round
rather than by the gamemode. The starting floors are protected by where they stand, not by
what they are.

**Attackers may place charges.** A consequence of the above. The original raid tools were
carried and thrown; with no claim bubble, a demolition charge can go on a wall, so it does.

**The sentry is our own item.** A vanilla sentry only runs within reach of a fuelled
generator, and the server and every client each decide that for themselves, so a server
that lies about it gets a turret that shoots but looks dead. The PeekSeek Sentry is the
Hostile Sentry with `Requires_Power false`, shipped in the UI workshop item beside the flag.

**The arena HUD is drawn over Unturned's own.** Unturned's arena boxes at the top of the
screen cannot be switched off from a server, so the HUD block covers them exactly.

**It ships as a gamemode, not a plugin of its own.** The brief asked for the code to be
"hookable" by a future match plugin. That plugin now has the hooks (`PeekSeek.Gamemodes`),
and RAID is a gamemode on them. `/start` moved to Match with it, as `/start raid`, and the
kits come from the match rather than from this plugin fetching its own.
