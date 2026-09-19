# PeekSeek Siege

Build a base. Then take turns cracking each other's.

Two teams get five minutes on opposite ends of Alpha Valley to put up a building. After
that the buildings never change again: they are photographed, and re-laid brick for brick
before every single round. Three rounds each side, then the sides swap. The only things
you choose between rounds are what you carry in and where you stand your flag.

That split is the whole design. A team that spends five minutes on a fortress has to both
hold it and, three rounds later, break into the mirror image of it. A base that is only
good to defend is a bad base.

## The loop

| Phase | What happens |
| --- | --- |
| Footprint | 20 seconds, rooted. Each team picks square or hexagonal; first answer counts. |
| Build | 5 minutes. Both teams build, each locked to their own plot. |
| Pick | Up to 20 seconds, rooted and empty-handed. Five options on the board, defence or raid. Over the moment the last person picks. |
| Flag | 15 seconds. One defender walks the base and stands the flag; everybody else waits where they spawned. |
| Ready | 3 seconds, everybody rooted, flag up. |
| Round | 3 minutes. Attackers bring the flag down, defenders keep it up. |

Rounds 1-3 team North defends, rounds 4-6 team South does. The match ends the moment the
rest of the block cannot change who is ahead: 4-0 after four rounds is over, 4-1 after
five is over. Level at 3-3 goes to overtime, two rounds each, over and over, decided the
same way. Never earlier, because half a block is not a fair swap.

Nobody moves until the round is live. A pick phase where people can run is a head start,
which is what the rooting is for.

## The rules that make it a siege

**Defenders take damage standing on dirt.** Two seconds off your own building and open
ground starts killing you. The grace period matters more than the damage: vaulting a
windowsill, dropping through a hatch and landing a jump short all leave you briefly on
the ground while doing exactly what a defender should be doing. Two seconds covers all of
that and nothing longer.

**Almost nothing in a base can be taken down.** When a base is re-laid, every piece comes
back owned by nobody, and in Unturned an unowned piece cannot be salvaged by anyone. Two
exceptions:

- *Metal doors and hatches* are handed to the team every round, because an unowned door
  is a door nobody can open.
- *Birch walls* are handed to the team too. These are the soft walls: build a wall in
  birch and you can pull it down mid-round to open an angle that was not there before.
  Metal walls, every floor and every pillar stay unowned, so a base cannot be dismantled
  into a better base while a round is running.

**One base per site.** Before the build phase each team picks a footprint: square (nine
metal floors, three by three) or hexagonal (six triangles round the middle with a square
on every side), from a board that draws the whole starting base on each tile. The
gamemode drops it with a maple pillar on every corner of every floor, on Unturned's own
housing grid to the centimetre, so everything a team attaches links. The starting floors
are the site: they can be swapped to another material but never salvaged, damaged or
moved, whatever they are made of. The pillars belong to the team, so a plan that wants a
corner clear can pull one. Teams do get floors, but a floor only goes down beside or
above one that is already standing, so the base grows out of the anchor. Nobody ends up
with three shacks in a field and a choice about which one to defend.

**The flag is the objective, and the defenders stand it.** Before every round one
defender, taking turns, gets fifteen seconds with the flag in hand to stand it anywhere
on the building; the rest of the team, and the attackers, wait rooted at their spawns.
Nobody outside the team sees where. If it is not down in time it goes on a random
starting tile that has nothing standing on it. The flag is the Metal Flag, a sign with
1100 health and the team's name on it, chosen because it has no claim bubble: a claim
flag stops anybody but its own group placing within 32m of it, on the client, which is
exactly the charge an attacker needs to stick on a wall. Attackers win a round by
bringing it down, or by wiping the defence; defenders win by the clock.

**A builder builds from a budget.** The team has one pile of pieces for the whole build
phase, and a builder carries exactly one of each piece the pile still has. So the housing
planner lists what can still go down, the panel beside the screen says how many, and
placing a piece takes one off the pile and puts the next one in your hands. Salvaging a
piece puts it back on the pile. There is no gun and nothing to top up.

Any piece already standing can be made of something else: look at it, press the first
plugin key (`,` unless you rebound it), and a board offers the same piece in every other
material with what is left of each, plus taking it down. Swap a starting metal floor for
birch and the metal floor goes on the pile for use elsewhere. That is the whole upgrade
system, and it works on the starting floors too.

Plus the leash: both sites are on one map, and during the build phase you cannot leave
your own plot. Walk too far and you are put back. That is what keeps the first round worth
playing.

## The kits

Every number is from the game's own item files: a maple wall is 400 health, metal 1100,
brick 1650, the flag 1100.

Defence: barbed wire (four coils), landmines (three), reinforce (spare metal), a
watchtower kit (grenades and a scope), and a sentry, a hostile one that goes down with a
Bluntforce in it, loaded, that nobody can take out. Salvage the sentry and put it back and
it is armed again; the gun never hits the floor. Which gun is `SentryFirearm` in the
config, one item with whatever state it needs.

Raid: a rocket launcher with two rockets (1000 each, from across the field), three
demolition charges with the detonator (1000 each, but you have to reach the wall), six
sticky grenades (100 each), six impact grenades (100), six frags (50, mostly for people).

## The map

Siege runs on **Alpha Valley Siege**, a Workshop copy of Alpha Valley with one byte
changed: the map type, from Arena to Survival. Unturned draws its arena text and compass
on every Arena map, from the client's own copy of the map, and no server setting or
plugin can switch them off. A Survival copy under another name has neither, and the
compass then follows `Gameplay.Compass` in the server's `Config.json`, which is off. The
copy also lets a base go down near the map's player spawns, which only Arena maps allow
by default. `map/make.py` makes it from a local Unturned install; it is never committed.

With the type comes hunger, thirst and infection on the HUD, which the plugin hides, and
loot, which `Items.Spawn_Chance` in the server config sets to nothing.

## Running it

```
/start          two or more players, split into two teams
/start solo     one player, for testing
/stop           end it
/pick 3         answer the option board
/siege          where the match is
```

Solo is how the whole loop gets tested without finding a second person for a five-minute
build phase. It builds one base and stands it at both sites, then runs the rounds
normally with you on whichever side you ask for. With nobody to defend, the flag goes on
a free tile by itself.

```
/siege skip           end the current phase (a round goes to the defenders, as the clock would)
/siege skip atk       end the round, give it to the attackers
/siege side           swap which side you are playing, solo only
```

`/siege skip` is what makes a bug in round four findable in under a minute instead of
half an hour.

## The UI

Unturned cannot draw a custom UI from plugin code. `EffectManager.SendUIEffect` takes an
`EffectAsset`, and an `EffectAsset` only exists inside a Unity asset bundle the client has
already downloaded, so a UI cannot ship in a `.dll`, only in a workshop mod.

The boards are written against both:

- A server that has the `ui/` mod loaded draws the real Unity layouts: the boards, the
  HUD along the top for everybody all match, and the build panel.
- One that does not logs a line at the first pick phase and draws the same options to
  chat, answered with `/pick`.

`ui/` is the Unity project that mod is built from, and `ui/README.md` says how it works
and how to build it. The chat form is not a placeholder waiting to be deleted: a player
whose client is still downloading the bundle needs to be able to answer the board too,
and a round is never going to wait for them.

The kill feed is not part of this plugin. It is `plugins/peekseek-killfeed`, its own
plugin and its own workshop item, because every PeekSeek server wants one.

## Where things are

```
plugin/PeekSeekSiege.cs         Rocket entry point, and every line of player-facing copy
plugin/Core/Siege.cs            the gamemode: phases, rounds, the flag, and the rules above
plugin/Core/SiegeHost.cs        what starts a match and feeds it Unturned's events
plugin/Core/SiegeWorld.cs       everything Siege does to the world, and the build/salvage gate
plugin/Core/BuildBudget.cs      the team's pile of pieces, and the inventories that mirror it
plugin/Core/BaseSnapshot.cs     photographing a base and re-laying it
plugin/Core/RoundOrder.cs       who defends round N, and when a match is over
plugin/Core/PickBoard.cs        the option, footprint and swap boards, both ways of drawing them
plugin/Core/Hud/MatchHud.cs     the score, clock and faces along the top; any gamemode can drive it
plugin/Core/Hud/BuildPanel.cs   what is left to lay, beside the screen
plugin/Core/Models/             the housing grid, the budget arithmetic, material families
plugin/Core/IGamemode.cs        the seam the main PeekSeek plugin will drive this through
map/                            the script that makes the Survival copy of Alpha Valley
ui/                             the Unity project the screens are built from
```

## The gamemode seam

`IGamemode` is what the main PeekSeek plugin will eventually drive this through: hand it a
match, tick it, tell it about players arriving, leaving and dying, end it. `SiegeHost` is
the temporary host that fills that role today, from a `/start` command on a server with no
matchmaking on it.

Nothing in `Siege` reaches for `PSManager`, and nothing outside `SiegeHost` knows how the
match was started. When PeekSeek's match plugin grows gamemode hooks, `SiegeHost` is
deleted and replaced by a registration line; `Siege` does not change.

Kits already come from the live site (`GET /server/v1/players/{steam64}/kits`, the `meta`
category), fetched on join and held until the player leaves; a round hands out gear right
now and is not going to wait on an HTTP round trip.

See `BRIEF.md` for the original instructions and the places this deviates from them.
