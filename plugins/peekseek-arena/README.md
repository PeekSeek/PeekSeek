# PeekSeek ARENA

The gamemode every ranked PeekSeek match has been so far: Unturned's own arena round,
last team standing inside a shrinking circle, driven by the match server's roster
rather than by whoever walked in. A gamemode for [PeekSeek Match](../peekseek/), which
runs the match, the roster, the score, the stats, the result and the screens; this
plugin is the round.

It is registered as the **default** gamemode, so every match the site hands over runs on
it unless another gamemode plugin is named for that match's gamemode. A match server
without this plugin hosts nothing: Match refuses a match it has no gamemode for and says
so in the log.

## What it does

Unturned's `LevelManager` runs the round: CLEAR wipes the map, WARMUP counts down, SPAWN
drops everybody in and PLAY shrinks the circle until one group is left. Match switches
that loop off for every gamemode by patching its four methods to do nothing;
`Patches/LevelManagerPatch.cs` patches the same four from this plugin with what they
should do instead, and only while the arena is the gamemode in play. Harmony runs every
prefix on a method and skips the original if any of them says so, so the two never
fight.

What those patches make PeekSeek's:

- Teams come down on the configured spawns, rotating one step per round so nobody
  keeps the good end of the map.
- Spectators are on the server but not in the round, so they are not counted.
- A round ends through Match, so it is scored, announced and reported; the match is
  first to the site's `roundsToWin`.
- Between rounds, a side that has gone home has forfeited, and nobody at all releases
  the match.

The wait for the roster before the first round is Match's, and so is the kit: this
plugin hands every player their kit when the match begins, when they join, and when
they respawn, which in the arena is the moment they die.

The bar along the top shows no castle, no swords and no clock for this gamemode; the
score line closes up round them.

## Configuration

`PeekSeekArena.configuration.xml`:

| Field | Description |
| --- | --- |
| `Spawns` | Where the teams come down each round, as `x`/`y`/`z`. Team 0 takes the first in round one, team 1 the second, and both step along one per round. **This used to be `Spawns` in the PeekSeek config**, and it does not move over on its own: a server that gets this plugin needs the list written into this file. |

## Building

```bash
dotnet build PeekSeekArena.sln -c Release
dotnet test PeekSeekArena.sln
```

The plugin is compiled against `PeekSeek.dll` from `../peekseek` and loaded beside it.
The output folder holds only `PeekSeekArena.dll`; the Match DLL is never copied next
to it, because a second `PeekSeek.dll` under `Rocket/Plugins` loads as a second plugin.

## Where things are

```
plugin/PeekSeekArena.cs               Rocket entry point, registers the gamemode as the default
plugin/Arena.cs                       the gamemode: kits, respawns, and the round win through Match
plugin/Patches/LevelManagerPatch.cs   the arena loop, made PeekSeek's
plugin/PeekSeekArenaConfiguration.cs  the spawns
```
