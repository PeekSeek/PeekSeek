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
| Pick | 20 seconds. Defenders are on their base from the first second: they pick, are kitted at once, and set up; one of them has the flag in hand to stand. Attackers choose where they come in and their powerup in the map's lobby, and only come in when the round goes live. |
| Round | 3 minutes. Attackers bring the flag down, defenders keep it up. |

Rounds 1-3 team North defends, rounds 4-6 team South does. The match ends the moment the
rest of the block cannot change who is ahead: 4-0 after four rounds is over, 4-1 after
five is over. Level at 3-3 goes to overtime, two rounds each, decided the same way, at
most twice: level after the second overtime is a draw, which the site records as nobody's
win or loss. Never earlier, because half a block is not a fair swap.

Defenders stay on their base through the pick. A round that is decided (the flag down, a
side gone) ends 2.5 seconds later, not on the same frame. The bar says BUILD over its clock for the build and nothing over
it otherwise; a round's end, the swap of sides and overtime are banners. A defender who
dies is put in the map's lobby for the rest of the round, with the bar and the feed
still in front of them, rather than left on a death screen. Everybody else spends the pick in the map's own lobby, the
building in the corner of Alpha Valley that the game itself uses as one: the regular
player spawns of an Arena map are its lobby (the alt spawns are the arena drop), so the
plugin reads them off the level rather than knowing where they are. A pick phase where
people can run is a head start, which is what the rooting and the distance are for.

## The rules that make it a raid

**Defenders take damage standing on dirt.** Three seconds off your own building and open
ground starts killing you, and a tag at the bottom of the screen says OFF BASE with the
grace draining in its pips and the damage in red after. The grace period matters more than the damage: vaulting a
windowsill, dropping through a hatch, landing a jump short and stepping out to meet
somebody at the door all leave you briefly on the ground while doing exactly what a
defender should be doing. Three seconds covers all of that and nothing longer.

**Almost nothing in a base can be taken down.** When a base is re-laid, every piece comes
back owned by nobody, and in Unturned an unowned piece cannot be salvaged by anyone. Two
exceptions:

- *Metal doors and hatches* are handed to the team every round, because an unowned door
  is a door nobody can open.
- *Wood* is handed to the team too, every wall, doorway and roof but no floor or pillar.
  Pull one mid-round to open an angle that was not there before, but it is gone: nothing
  comes back into your hands. Salvage a wood wall holding a metal wall and it becomes
  that metal wall; holding a pane of glass, it becomes a window with the pane in it,
  facing the way you face, so one-way glass sees out from where you stood. A door or a
  pane taken out is gone too. Metal, every floor and every pillar stay unowned, so a
  base cannot be dismantled into a better base while a round is running.

**One base per site.** Before the build phase each team picks a footprint: square (nine
metal floors, three by three) or hexagonal (six triangles round the middle with a square
on every side), from a board that draws the whole starting base on each tile. The
gamemode drops it with a maple pillar on every corner of every floor, on Unturned's own
housing grid to the centimetre, so everything a team attaches links. The starting floors
are the site: never salvaged, damaged or
moved, whatever they are made of. The pillars belong to the team, so a plan that wants a
corner clear can pull one. Teams do get floors, but a floor only goes down beside or
above one that is already standing, so the base grows out of the anchor. Nobody ends up
with three shacks in a field and a choice about which one to defend.

**The flag goes on a starting floor, away from any door.** Not on the roof, not on a crate,
not in a doorway to be shut inside the door: on one of the tiles the base began as, on
that tile's own storey, and not in a doorway: a tenth of a floor clear of the door is enough. The bearer who tries
anywhere else is told so and keeps the flag. Everybody spawns on those tiles too, on
whichever of them nothing has been built on, spread out from the middle; the anchor
alone put people inside the walls a hexagonal base grows from its centre.

**Attackers always know where they are going.** From the moment they are put down for
a round until it ends, the base they are taking is on every attacker's map and compass,
as the game's own marker with the site's name on it, and they spawn facing it. The
defenders get the same marker on their flag, because the rest of the team was in the
lobby while the bearer stood it. The
spawns are a hundred metres out on a map with two bases, and "which way" is not a
question a round should be spent on.

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

The kit is a few doorways and a lot of wall: three metal and three maple doorways, ten
metal walls, sixty-four maple walls, twelve maple floors, thirty-two maple roofs, six
metal roofs, eight maple ramps and forty-eight maple pillars. Every doorway comes with a metal door already in it.

Salvaging while building never hands you anything. A maple wall opens a board to turn it
into a rampart or a window, a maple roof into a hole, free, or take it down. Anything else,
metal included, just comes down and goes back on the pile: there is no metal window or
rampart. A hexagonal base gets 0.7 of all of that and, as triangle floors and
roofs, 0.9 of the maple floors and roofs, rounded down. The kit also grows with the
team that will raid it: every raider past the first adds all of the metal again and half
of the wood, and no doorways. Salvage a wood wall holding a metal wall and it becomes metal, one metal wall off
the pile and the wood one not back on it. The hold is half a second while building and
three in a round, set per player by the server.

Plus the leash: both sites are on one map, and during the build phase you cannot leave
your own plot. Walk too far and you are put back. That is what keeps the first round worth
playing.

## The powerups

Every player takes one powerup a round, and a powerup has one taker a team: whoever
takes it first. The board shows every powerup a side has, each tile its class glyph in
front of its name, its items, and a line where the pictures do not say it all. Your own
pick is lit; a teammate's is greyed out with their face on it. Whoever has not picked
when the clock runs out gets one nobody on their team has. Attackers always carry a
detonator and 4 charges, and every attacker still alive gets 5 more at half time;
defenders carry nothing extra. Once the round is live, attackers may build anything
anywhere; it all comes down when the round ends.

| Attack | Class | What it is |
| --- | --- | --- |
| Demolition | Destruction | 8 charges and the detonator |
| Breacher | Destruction | a chainsaw (birch in 1 s, maple in 3 s, nothing on metal) and 2 impact grenades that each break any door in one throw |
| Elevator | Initializer | 4 bounce charges and 2 ladders; no fall damage for 3 s after a bounce |
| Scaffold | Initializer | a housing planner, 6 pillars, 3 maple walls and 3 maple stairs |
| RPG | Support | the launcher and 3 rockets: 90 on a direct hit, 60 at most from splash, a 4 m blast |
| Suppressor | Support | the meta Dragonfang and a spare drum: 9 a hit to players, double to glass |
| Insertion | Support | a Forest Transport Heli on a platform 50 m up and 100 m further out than the pilot's spawn, facing away from the base; the pilot starts at the controls and the team in the seats; the engine dies after 60 s |
| Stun | Support | a flashbang: 25 damage, and nobody it blinds can fire a gun for 5 s |
| Second Life | Support | a bedroll (the PeekSeek Bedroll: an Arena map refuses real beds); for 30 s one teammate who dies comes back there, once, on 50 health |

| Defence | Class | What it is |
| --- | --- | --- |
| Nomad | Special | vitamins, 20 s outside with no off-base damage, and a slip plate |
| Faker | Special | a stereo that fakes gunfire and footsteps, a decoy flag that looks exactly like the real one and wins nothing, and 2 claymores only attackers set off |
| Striker | Special | red smoke: a plane flies over, trees within 16 m fall, and 9 rockets land within 16 m a ragged moment apart, never on your own base |
| Mirror | Armorer | 2 one-way panes, bulletproof from outside, a metal wall to explosives |
| Welder | Armorer | a blowtorch and 3 metal walls you may place mid-round |
| Supplier | Warden | a crate that makes a blood bag every 30 s |
| Sentry | Warden | 2 PeekSeek Sentries, armed the moment they go down, 3 snares that root an attacker for 3 s, and 4 coils of wire |

**Hits to break.** Whatever the game worked out, a tool on the table takes a piece down
in exactly its number of hits, and only the nearest piece takes a blast. Floors and
pillars keep the game's own damage; the starting floors take none.

| Tool | Metal door | Birch wall | Maple wall | Metal wall | Anything else |
| --- | --- | --- | --- | --- | --- |
| Demolition charge | 1 | 1 | 1.5 | 3 | 3 |
| Impact grenade | 1 | none | none | none | none |
| Rocket | 2 | 2 | 2 | 4 | 4 |
| Chainsaw | none | 1 s | 3 s | none | none |

**Attackers can always get in.** While building, a `BREACH n / 6` tag says how many
charges it takes to reach the dearest starting tile from outside, along the cheapest way
in. At the end of the build any tile over 6 has the outermost metal on its cheapest way
in turned to maple, and the team is told what changed. The flag goes at least 3 m from
any door, so it cannot be shut inside one, and never where it would cost more than 6; a
Welder's wall that would seal it in comes back into their hands. Every doorway comes
with a metal door.

**A round's end.** The banner goes up the moment the round is decided, and says how:
who destroyed the flag, which team wiped out the other, or who aced a team of three or
more. The round resets 2.5 seconds later. Between rounds the whole map is reset as the
game resets an arena: items, smoke, trees and objects. There is no death screen; a dead
player is straight back in the lobby for the rest of the round.

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
plugin because the match names it. To test one alone, queue it on dev with a second
player and run `/ssolo` on the match server once it is held.

```
/ssolo               play the match this server holds now: everybody on its roster who
                     is not here counts as present and alive, gets a random powerup, and
                     a team with nobody here gets a plain walled base
/nextphase, /np      end the current phase; a round goes to the defenders while the flag
                     stands and to the attackers once it is down
/raidtime <seconds>  set the clock of the current phase
/raidwin <atk|def>   give the round to one side
/raidend             end the match with no winner
/raid                where the match is
/raid find <text>    look an item up by name
```

All of them need `peekseek.raid.start`.

## The screens

Every screen Raid uses is Match's, in the PeekSeek UI workshop item built from
`plugins/peekseek/ui`: the bar along the top, the footprint board, the swap board, the
defenders' powerup board, the attackers' map beside theirs, and the build panel. The same item carries
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
