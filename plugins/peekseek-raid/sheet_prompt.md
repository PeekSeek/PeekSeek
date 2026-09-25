# Brief: update the RAID powerups sheet

You are updating `plugins/peekseek-raid/raid-powerups-bare.png`, the one-page sheet that
explains PeekSeek RAID's powerups to players. The powerups have changed since it was
made, and the sheet should now also explain how a match and a round run, as a timeline.
Produce a new PNG at the same size (1920x1080) and write it over the old one. Keep the
old one's look; change what it says.

The source of truth for every number below is the plugin: `plugin/PeekSeekRaidConfiguration.cs`
(`LoadDefaults`) and `README.md`. If anything here disagrees with them, they win, and
say so when you hand the sheet back.

## Keep

- The layout: the PeekSeek mark and "POWERUPS" top left, the large "RAID MODE" title,
  the ATTACK block left and DEFENCE block right, each with its one-line win condition,
  the powerups in a grid, the class legend along the bottom.
- The look: PeekSeek's page colour (`#0a0a0a`, or the sheet's current near-black),
  white names, grey lines, red only for "MODE", "ATTACK" and the small counts. Item
  pictures on nothing: no panel, no notch, no coloured border, no well behind an item.
- Each powerup: its item pictures with a small count tag (`6x`), its class glyph inline
  in front of its name, its name in bold caps, and a line under it only where the
  pictures do not say it all.
- The six class glyphs exactly as the current sheet draws them: Destruction (burst),
  Initializer (double chevron), Support (crosshair), Special (four-point star), Armorer
  (shield), Warden (plus). "Backup" is called **Support** now.
- Writing: short, plain sentences. No em dashes, no double hyphens.

Item pictures are on the site at `website/public/unturned/<item id>.png`. Non-item
pictures: `vehicle-93.png` (the transport heli), `43131.png` (sentry), `43132.png`
(bedroll), `43133.png` (decoy flag, the flag's picture).

## The powerups now

Attack, 9 (the grid has room for 10; leave the tenth cell empty or centre the rest):

| Powerup | Class | Items (id x count) | Line |
| --- | --- | --- | --- |
| Demolition | Destruction | detonator 1240 x1, charge 1241 x8 | none |
| Breacher | Destruction | chainsaw 490 x1, impact grenade 1520 x2 | Cuts through wood walls, breaks any door in one throw |
| Elevator | Initializer | bounce charge 1691 x4, ladder 325 x2 | none |
| Scaffold | Initializer | housing planner 1764 x1, pillar 36 x6, wall 33 x3, stairs 316 x3 | none |
| RPG | Support | launcher 519 x1, rocket 520 x3 | none |
| Suppressor | Support | Dragonfang 132 x1, drum 133 x1 | none |
| Insertion | Support | the transport heli (vehicle-93) | The team starts in a transport heli |
| Stun | Support | flashbang 1346 x1 | Stops anyone caught shooting for 5 s |
| Second Life | Support | bedroll 43132 x1 (show the red bedroll) | For 30 seconds one teammate can respawn here once placed |

Defence, 7:

| Powerup | Class | Items (id x count) | Line |
| --- | --- | --- | --- |
| Nomad | Special | vitamins 391 x1, slip plate 1239 x1 | Lets you fight outside for 20 s |
| Faker | Special | stereo 1466 x1, decoy flag 43133 x1, claymore 1102 x2 | Fake gunfire and footsteps, and a decoy flag |
| Striker | Special | red smoke 266 x1 | Drops 9 rockets on your smoke |
| Mirror | Armorer | one-way glass 1299 x2 | none |
| Welder | Armorer | blowtorch 76 x1, metal wall 371 x3 | none |
| Supplier | Warden | care package 1374 x1 | Makes a blood bag every 30 s |
| Sentry | Warden | sentry 43131 x2, snare 1113 x3, barbed wire 386 x4 | Two sentries, stun snares and wire |

Gone since the old sheet: Keyhole and Chainsaw (now Breacher), Courier (now part of
Faker), Minefield (now part of Sentry), Pitfall.

The strapline rules under the title: **1 pick per player, every round**, **1 of each
powerup per team**, **attackers always carry 4 charges**. The old "10 ways in. 10 ways to
hold." no longer counts right: make it "9 ways in. 7 ways to hold." or drop the numbers.

## Add: how a match runs

A timeline, full width, below the grids or replacing the class legend's row (keep the
legend somewhere). Left to right, each stage a label, a duration, and one line. It has
to be readable at a glance: stages, not paragraphs.

Once, at the start:

1. **Footprint**, 20 s. Each team picks square or hexagonal. The first answer on the
   team counts.
2. **Build**, 5 min. Both teams build their own base from a shared budget. A board
   turns a wall into a rampart or window. The breach tag shows how many charges it takes
   to reach the flag; the build ends with any spot over 6 made breakable.

Every round:

3. **Pick**, 20 s. Defenders are already on their base: they pick, get their kit at
   once and set up; one of them has the flag in hand to place (at least 3 m from any
   door). Attackers pick a powerup and where they come in from the lobby.
4. **Raid**, 3 min. Attackers spawn. Half way through, every attacker still alive gets 5
   more charges. Attackers win by destroying the flag or wiping out the defenders;
   defenders win by keeping the flag up until the clock runs out, or wiping out the
   attackers.
5. **Round end.** The banner is instant ("x destroyed the flag", "team x wiped out team
   y", an ace for one player against three or more). The map resets 2.5 s later.

The match:

- Rounds 1 to 3: one team defends. Rounds 4 to 6: the other team defends.
- The match ends as soon as the rest of the half cannot change who is ahead.
- Level at 3-3: overtime, two rounds each way, at most twice. Still level after the
  second overtime: a draw.

## Hand back

The new PNG in place of the old one, and a short list of anything you could not fit or
had to guess.
