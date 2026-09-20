# PeekSeek RAID

Build a base. Then take turns cracking each other's.

A gamemode for [PeekSeek Match](../peekseek/), which runs the match, the roster, the
score and the screens; this plugin is the rules. It was built as "Siege", and the
original brief in `BRIEF.md` still calls it that.

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
| Pick | Up to 20 seconds. Attackers get the map: where they come in, and their powerup. Defenders get five powerups, and one of them, once picked, the flag to stand. Everybody but the flag bearer waits rooted in the map's lobby. Over the moment the last person has picked and the flag is up. |
| Ready | 3 seconds, everybody in position and rooted, flag up. |
| Round | 3 minutes. Attackers bring the flag down, defenders keep it up. |

Rounds 1-3 team North defends, rounds 4-6 team South does. The match ends the moment the
rest of the block cannot change who is ahead: 4-0 after four rounds is over, 4-1 after
five is over. Level at 3-3 goes to overtime, two rounds each, over and over, decided the
same way. Never earlier, because half a block is not a fair swap.

Nobody moves until the round is live, except the one defender walking the flag, who
cannot leave the base. Everybody else spends the pick in the map's own lobby, the
building in the corner of Alpha Valley that the game itself uses as one: the regular
player spawns of an Arena map are its lobby (the alt spawns are the arena drop), so the
plugin reads them off the level rather than knowing where they are. A pick phase where
people can run is a head start, which is what the rooting and the distance are for.

## The rules that make it a raid

**Defenders take damage standing on dirt.** Three seconds off your own building and open
ground starts killing you. The grace period matters more than the damage: vaulting a
windowsill, dropping through a hatch, landing a jump short and stepping out to meet
somebody at the door all leave you briefly on the ground while doing exactly what a
defender should be doing. Three seconds covers all of that and nothing longer.

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

**The flag goes on a starting floor, away from any door.** Not on the roof, not on a crate,
not in a doorway to be shut inside the door: on one of the tiles the base began as, on
that tile's own level, with no door within two and a half metres. The bearer who tries
anywhere else is told so and keeps the flag. Everybody spawns on those tiles too, on
whichever of them nothing has been built on, spread out from the middle; the anchor
alone put people inside the walls a hexagonal base grows from its centre.

**The flag is the objective, and the defenders stand it.** Before every round one
defender, at random, picks their powerup and is then handed the flag, with the rest of
the pick to stand it anywhere on the building; step off the base with it and you are
put back on the anchor. The rest of the team, and the attackers, wait in the lobby.
Nobody outside the team sees where. If it is not down when the pick ends it goes on a
random starting tile that has nothing standing on it.

**Attackers choose where they come in.** Each site has three spawn points walked out
by hand, and the attack screen is a map of the base, the game's own GPS picture, with
a dot on each. Click one and your face hangs off it on every teammate's map, so a
team sees its own spread without a word. Somebody who never clicks comes in at the
first. Several on one dot stand a step apart round it, all facing the base. The flag is the PeekSeek Flag, the claim
flag's looks with 600 health and no claim bubble, shipped in the UI workshop item: a
claim flag stops anybody but its own group placing within 32m of it, on the client,
which is exactly the charge an attacker needs to stick on a wall. Attackers win a round by
bringing it down, or by wiping the defence; defenders win by the clock.

**A builder builds from a budget.** The team has one pile of pieces for the whole build
phase, and a builder carries exactly one of each piece the pile still has. So the housing
planner lists what can still go down, the panel beside the screen says how many, and
placing a piece takes one off the pile and puts the next one in your hands. There is no
gun and nothing to top up.

Any piece already standing can be made of something else: salvage it. During the build
the salvage is refused and a board offers the same piece in every other material with
what is left of each, plus taking it down, which puts it back on the pile. Swap a
starting metal floor for birch and the metal floor goes on the pile for use elsewhere.
That is the whole upgrade system, it works on the starting floors too, and holding the
salvage key on a wall is the one gesture nobody has to be taught. The hold is half a
second while building and three in a round, set per player by the server; the game's
own eight is for a world where a wall is worth something.

Plus the leash: both sites are on one map, and during the build phase you cannot leave
your own plot. Walk too far and you are put back. That is what keeps the first round worth
playing.

## The kits

The arithmetic a raider does at the wall, and the plugin writes onto the game's own
items when a match starts (`ChargeStructureDamage`, `RocketStructureDamage`,
`RocketRangeMultiplier`): a maple wall is 400 health, birch 350, metal 1100, the flag
600. A charge does 1000, so one takes a wood wall and two take metal. A rocket does
380, so two take wood and three take metal, and its blast is four fifths of the game's,
a hole in a wall rather than a room. There is no brick: a brick wall is four charges,
and that is not this game. Pillars are maple only, so the frame is the frame and a
wall is what a team chooses to make metal.

**Attack.** Every kit has a way through a wall; what changes is how many walls, from
how far, and what is left for the people inside. The more of it comes from range, the
less of it there is. A base that is metal all the way round is four walls deep at most.

| Kit | What you carry | In metal walls |
| --- | --- | --- |
| Demolition | twelve charges, the detonator | six, at the wall |
| Rockets | the launcher, nine rockets | three, from anywhere |
| Breacher | three rockets, five charges | one from range, then two and a half up close |
| Grenadier | twelve stickies, four impacts, two frags | one, or three wood, and the room behind it |
| Sapper | four charges, six frags | two, then clear what is behind them without going in |

**Defence.** A sentry, a hostile one that goes down with a Bluntforce in it, loaded, that
nobody can take out (salvage it and put it back and it is armed again; which gun is
`SentryFirearm`). A welder: a blowtorch to mend what they crack and two metal walls to
close a hole. A trapper: three landmines and four coils of wire. Reinforce: six metal
walls, a door and two pillars. A watchtower: a scope and three grenades. The sentry is
the PeekSeek Sentry, item 43131 in the UI workshop item: the Hostile Sentry with
`Requires_Power false`, because a vanilla sentry only runs within reach of a fuelled
generator, and both the server and every client decide that for themselves.

## The map

Raid runs on vanilla **Alpha Valley**, an Arena map. Unturned draws its arena message
and round number on every Arena map, from the client's own copy of the map, and no server
setting or plugin can switch them off, so the bar's middle block is drawn exactly over
them: 360 wide at the centre of the screen, down to where they end. The compass under
them stays, on purpose. The game's own arena round system is switched off by Match for
every gamemode, and `Items.Spawn_Chance` in the server config sets the loot to nothing.

## Running it

A match comes from the site: the `Raid` gamemode in `website/src/mmconfig.json`, queued
for like any other, handed to a match server by `/server/v1/session`, and run by this
plugin because the match names it. There is no other way to start one.

```
/raid                where the match is
/raid skip           end the current phase (a round goes to the defenders, as the clock would)
/raid skip atk       end the round, give it to the attackers
/raid find <text>    look an item up by name
```

`/raid skip` is what makes a bug in round four findable in under a minute instead of
half an hour.

## The screens

Every screen Raid uses is Match's, in the PeekSeek UI workshop item built from
`plugins/peekseek/ui`: the bar along the top, the footprint board, the swap board, the
defenders' powerup board, the attackers' map, and the build panel. The same item carries
the flag and the sentry as items. Raid asks for a board through `Host.Board` and tells
the bar what it needs to know through `IGamemode`: which team holds and which attacks
this round, what each player picked, and the clock. See `../peekseek/ui/README.md`.

## Where things are

```
plugin/PeekSeekRaid.cs          Rocket entry point, registers the gamemode, every line of copy
plugin/Core/Raid.cs             the gamemode: phases, rounds, the flag, and the rules above
plugin/Core/RaidWorld.cs        everything Raid does to the world, and the build/salvage gate
plugin/Core/BuildBudget.cs      the team's pile of pieces, and the inventories that mirror it
plugin/Core/BaseSnapshot.cs     photographing a base and re-laying it
plugin/Core/RoundOrder.cs       who defends round N, and when a match is over
plugin/Core/Models/             the housing grid, the budget arithmetic, material families
plugin/Commands/RaidCommand.cs  the test console
```

## The seam

`Raid` implements `PeekSeek.Gamemodes.IGamemode` and is registered with
`Gamemodes.Register` when the plugin loads. Match hands it every match the site calls
`Raid`, ticks it, tells it about players arriving, leaving, dying and coming back, and
ends it. Everything Raid needs from the server it asks `PeekSeek.Gamemodes.Host` for:
who is playing, who is still alive this round, the score, a round or the match won, a
player's kit, the boards, the panel, and chat in the house style.

Match owns the score. Raid tells it a round was won and with what line to say so, and
then asks `RoundOrder` whether that ends the match; a Raid match is blocks of rounds, not
first to N, so that arithmetic is Raid's.

The plugin is compiled against `PeekSeek.dll` and loaded beside it; the DLL is never
shipped with it, since a second `PeekSeek.dll` under `Rocket/Plugins` loads as a second
plugin.

See `BRIEF.md` for the original instructions and the places this deviates from them.
