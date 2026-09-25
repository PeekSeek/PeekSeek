# RAID powerups: implementation brief

You are implementing the new powerup set for **PeekSeek RAID** (`plugins/peekseek-raid`).
This file is the whole spec. The visual reference is the "RAID Loadouts" artifact:
https://claude.ai/artifact/Umxis4uDgA2pE4Jjz3EaZ3

Read these before writing code:

- `plugins/peekseek-raid/BRIEF.md` for what RAID is and why it deviates from the original brief.
- `plugin/PeekSeekRaidConfiguration.cs` for how options, items and explosive tuning are configured now.
- `plugin/Core/RaidWorld.cs` for `TuneExplosives` and the `OnDamageStructure` / `OnDamageBarricade`,
  deploy and salvage hooks. Most of this spec lives in those hooks.
- `plugin/Core/Raid.cs` for the round flow and the pick board.
- The game's own source and item files, datamined at
  `C:\Users\kiera\Documents\Unturned-Datamining-linux-client-preview`
  (`Assembly-CSharp/SDG.Unturned/*.cs`, `Bundles/Items/**/*.dat`). Check every Unturned API
  there before using it. Don't guess signatures.
- The `unturned-ui` skill before touching the pick board UI.

House rules: match the comment style already in the plugin (`///` prose that explains *why*),
no `utils` files, every number that a designer might tune goes in the configuration and
nowhere else. Commit messages and PR bodies carry no Co-Authored-By or "Generated with" lines.
Nothing here is deployed by CI. Do not deploy.

## What changed since the first draft

If an earlier version of this brief or of the graphic is lying around, this list wins:

- Bulwark is removed. Welder has 3 metal walls instead of 2.
- Anklebreaker and Slip Plate are merged into **Pitfall** (2 slip plates, 2 snares).
- Scaffold is 6 maple pillars, 3 maple walls, 3 maple stairs. No ramp and no floors.
- Insertion is the green Transport Heli, vehicle 93, not the Hummingbird.
- Second Life gives 1 blood bag. No powerup ever gives medkits.
- Courier moves the flag **once**, and the replanted flag takes one extra charge.
- Supplier makes 1 blood bag every 30 s, starts empty and holds 1, with a HUD prompt.
- Each powerup can be taken by only one player per team.
- Defenders get no charges and no detonator.

---

## 1. The six classes

Every powerup belongs to exactly one class. Add a `Category` field to `RaidOption`
(XML attribute, enum) and group the board by it.

| Side | Class | Job |
|---|---|---|
| Attack | **Destruction** | Breaks the base. |
| Attack | **Initializer** | Gets the team in high. |
| Attack | **Support** | Helps the team: suppression, a way in, a second life. |
| Defence | **Special** | A trick for one situation. |
| Defence | **Armorer** | Makes the base itself harder to break. |
| Defence | **Warden** | Heals, sentries and traps. |

## 2. The round loop

1. **Build (once, 5 min).** As today, plus the door rules in section 4.
2. **Pick (20 s).** The board draws **2 powerups per class** for each side (6 tiles), at random
   from that class's pool. A powerup shown last round is excluded this round when the class
   has enough others. Each player takes one tile. **Each powerup can be taken by only one player per
   team**, and **at most 2 players per team may take the same class**; a taken tile or a
   full class greys out on that team's board. A player who doesn't pick gets a random tile
   that is still free.
3. **Setup (30 s).** Defenders place gadgets and the flag. Attackers are at their staging
   spawns and may already place a Second Life bedroll and build a Scaffold.
4. **Action (3 min).** Attackers win by breaking the flag, defenders by holding it or wiping
   the attackers. Off-base damage for defenders still applies.

## 3. Damage rules (apply to every explosive and tool)

### 3.1 One piece per blast
`DamageTool.explode` sorts its candidates nearest first (`explosionRangeComparator`) and
calls `ApplyExplosionDamage` on each, which ends in `StructureManager.damage` /
`BarricadeManager.damage` and so in RAID's `onDamage*Requested` hooks. In the hooks, let the
**first** structure or barricade hit of an explosion through and set `shouldAllow = false` for
every later one of that explosion. Identify an explosion by instigator + damage origin +
`Time.frameCount`. Space multi-rocket effects (Striker) at least one frame apart, 0.3 s in
practice, so each rocket counts on its own.

### 3.2 Hits to break
Ignore the damage the game calculated. Set `pendingTotalDamage` to
`ceil(maxHealth / hits)` from this table, keyed by the tool (asset id or damage origin) and
the target's material or kind. Put the table in configuration.

| Tool | Metal door / hatch / shutter | Birch wall | Maple wall | Metal wall | Everything else |
|---|---|---|---|---|---|
| Demolition Charge 1241 | 1 | 1 | 2 | 3 | 3 |
| Keyhole, Impact Grenade 1520 | 1 | 0 | 0 | 0 | 0 |
| Rocket, launcher 519 | 2 | 2 | 3 | 4 | 4 |
| Chainsaw 490 (held) | 0 | 1 s | 3 s | 0 | 0 |

"0" means the hook refuses the damage. Metal is high armor tier (×0.5) in vanilla; the table
replaces that, so don't apply both. Pillars, floors and the anchor floors keep today's rules
(anchor floors are never damaged).

### 3.3 Player damage
- Rocket: 90 on a direct hit (player within ~0.5 m of the explosion point), otherwise capped at 60.
- Keyhole: 0 to players.
- Stun flashbang: 25.

### 3.4 The rocket asset
The vanilla rocket 520 has no projectile override, so `UseableGun` takes the rocket's damage
and blast radius from the **launcher (519)**, not the magazine (`UseableGun.cs`, around line
617). Today `TuneExplosives` writes to 520 and does nothing. Tune 519's `structureDamage`,
`barricadeDamage` and `range` (the blast radius for a launcher) instead. Blast radius 4 m.

## 4. Attackers must always be able to get in

This is a hard guarantee, not a balance goal. Implement all three parts.

1. **Every attacker, whatever they pick**, spawns each round with a **Detonator (1240) and 2
   Demolition Charges (1241)**, on top of their powerup. Defenders get no charges and no
   detonator.
2. **The flag must be reachable.** At the end of the build phase, and whenever the flag is
   placed or moved, compute the **breach cost**: cast horizontal rays from the flag every
   22.5° plus one straight up, walk the structures and barricades each ray crosses until it
   leaves the base, and sum each piece's Demolition Charge hits from 3.2. The breach cost is
   the cheapest ray. It must be **at most 6**. Show it to defenders during the build
   ("Breach cost 5 / 6"). If it's over 6 when the build ends, replace the outermost metal
   pieces on the cheapest ray with the same piece in maple (`Materials.SameKind`) until it's
   6 or less, and tell the team which pieces were downgraded. Don't let a Courier replant or a
   Welder wall push it over 6 either: refuse the placement and say why.
3. **Doors.** The flag can only be placed within 6 m of a metal door, and each metal door
   placed in the build unlocks 3 metal walls in the budget (`BuildBudget`).

With the minimum team of attackers the baseline alone is 2 charges each, and every Destruction
pick adds more. Put a unit test on the breach-cost walk (pure logic, no Unturned types) and on
the hits table.

## 5. The powerups

Ids are Unturned item ids, taken from the game files. Quantities are what the player
receives. "Twist" is what the plugin adds on top of vanilla behavior.

### Attack · Destruction

**Demolition**: Detonator 1240, Demolition Charge 1241 ×6.
Vanilla behavior with the hits table.

**Keyhole**: Impact Grenade 1520 ×3.
Twist: breaks one metal door, hatch or shutter per grenade (including the vault door if one
is ever added). Zero damage to walls and to players.

**Chainsaw**: Chainsaw 490.
Twist: while it is hitting a wood wall, count held time per piece: birch breaks after 1 s,
maple after 3 s. It does nothing to metal, doors or players beyond vanilla melee damage.

### Attack · Initializer

**Elevator**: Bounce Charge 1691 ×2, Maple Ladder 325 ×2.
The vanilla bounce charge does no damage and launches whoever is on it
(`Explosion_Launch_Speed 50`). Twist: attackers may place both items on and against the
enemy base (bypass RAID's deploy blocks for these ids), the ladders stay for the round, and a
player launched by a bounce charge takes no fall damage for 3 s after.

**Scaffold**: Maple Pillar 36 ×6, Maple Wall 33 ×3, Maple Stairs 316 ×3.
Twist: attackers may place these structures within **40 m** of the enemy base anchor during
setup and action. They're owned by the attacking group. Defenders can shoot or melee them
down (maple, 400 health). They're removed at round end.

### Attack · Support

**RPG**: Rocket Launcher 519 with a Rocket 520 loaded, plus 1 spare Rocket 520. Two rockets
in total: the loaded one counts. Damage per 3.2 and 3.3.

**Suppressor**: Dragonfang 132 with Red Chevron Scope 148, Tactical Laser 151, Vertical Grip
8, Military Muzzle 150 and a full Dragonfang drum 133 attached, plus 1 spare drum 133.
The attachments are the meta kit's (`website/src/kits.json`); build the gun's state bytes
the same way the kit code does. Twist: player damage cut from 17 to 9. Damage to glass
barricades (One-Way Glass 1299) doubled.

**Insertion**: Transport Heli, the green Huey (vehicle 93, `Bundles/Vehicles/Huey_Forest`).
Twist: one per attacking team per round (the one-player-per-powerup rule already ensures it). Spawned at the pilot's staging point when the round
goes live, locked to the attacking group, fuel set so the engine dies around 60 s, then, who picks this gets a heli where they spawned, they spawn as the driver and everyone else who spawns at that location is added as a passenger
removed at round end. Test inside Alpha Valley's arena bounds before promising it.

**Stun**: Flashbang 1346 ×1.
Twist: 25 damage in the blast. Anyone the game blinds with it can't shoot for 5 s: refuse gun
use (not melee) for that player and show the countdown on the RAID HUD.

**Second Life**: Bedroll 293, Blood Bag 395 ×1.
Twist: placeable only within 64 m of the enemy base anchor. The bedroll loses health on its
own so it lasts about 30 s from placement (vanilla health 50). The first attacker (any
teammate, the placer included) who dies while it stands respawns at it, once, on 50 health,
with **only** their gun, its ammo and their armor (clothes with armor), plus the kit's blood
bag if the placer hasn't used it. Everything else is removed. The bedroll is removed on use,
so it gives exactly one respawn.

### Defence · Special

**Nomad**: Vitamins 391 ×1.
Twist: from the moment they're eaten, 30 s with no off-base damage (skip the grace timer
entirely). Show the countdown.

**Faker**: Stereo 1466, Claymore 1102 ×2.
Twist: the stereo plays fake fight sounds from where it stands: vanilla impact effects
(`Bundles/Effects/Impacts/*_Dynamic`, with audio) in short bursts, plus footstep and door
sounds from PeekSeek's own sound bundle (`plugins/peekseek/ui`, see the Sounds commits),
spawned with `EffectManager` at its position on a randomized timer so it sounds like a
player. It has 50 health. Claymores behave as vanilla but trigger only on attackers.
Test that it sounds convincing before shipping.

**Striker**: Red Smoke 266 ×1.
Twist: where the smoke lands, after 3 s a siren plays, then 3 rockets spawn above that point
and fall straight down, 0.3 s apart, in a small triangle. Refuse (and refund) if the landing
point is within 20 m of the defenders' own base.

**Courier**: uses the flag itself (43130), no extra item.
Twist: **once** per round the Courier can pick the flag up (salvage it into their hands) and
must replant it on the base within 10 s, or it drops where they stand. While carrying they
can't shoot, and a flare effect burns over their head. The buff: once replanted, the flag
takes **one extra Demolition Charge** to break (4 instead of 3, and the other tools scaled the
same way on the hits table) for the rest of the round. The breach cost rule (section 4)
applies to the new spot.

### Defence · Armorer

**Mirror**: One-Way Glass 1299 ×2.
Placed in place of a wall piece. Vanilla glass is already one-way. Twist: bullet damage from
outside the base is refused; explosives still break it by the hits table (counts as a metal
wall).

**Welder**: Blowtorch 76, Metal Wall 371 ×3.
Twist: this player may place their three walls during the round (everyone else can't).
Placements must respect the breach cost rule.

### Defence · Warden

**Supplier**: Maple Crate 366, a storage barricade.
Twist: placed in the base, locked so only defenders can open it. It starts **empty**. 30 s
after it's placed, it gains 1 **Blood Bag 395**. It holds at most 1: while the blood bag is
in it, the timer is stopped. When someone takes the blood bag out, the 30 s timer starts
again from zero. Show a prompt while a defender within 4 m is looking at it, on the RAID HUD
(Unturned's own hover text for a storage can't be changed): "Supplier: blood bag ready" or
"Supplier: next blood bag in 12 s".

No powerup ever hands out medkits. Healing in RAID is blood bags only.

**Minefield**: Landmine 1101 ×3, Barbed Wire 386 ×4. Vanilla behavior (already live in RAID).

**Sentry**: PeekSeek Sentry 43131 ×3. The existing powerless hostile sentry, armed with
`SentryFirearm` on placement (already live in RAID).

**Pitfall**: Slip Plate 1239 ×2, Snare 1113 ×2. (Slip Plate and Anklebreaker merged.)
The slip plate is vanilla and very slippery; it launches whoever runs onto it. Place it so the
slide ends on a snare. Twist: slip plates are placeable as a floor cover in the base, and
attackers can't salvage them. The snare stuns: it breaks legs as vanilla and also roots the
attacker for 3 s (speed multiplier 0). Defenders can use the plates too: with Nomad, a slip plate at a
doorway throws you out of the base fast.

### Board text

What each tile shows under its name, exactly. The graphic
(`raid-powerups-bare.png`) is the reference. Blank means the tile shows only the name, its
class symbol (left of the name) and the items.

| Powerup | Board text |
|---|---|
| Demolition | |
| Keyhole | Breaks any door in one throw |
| Chainsaw | Cuts through wood walls |
| Elevator | |
| Scaffold | |
| RPG | |
| Suppressor | |
| Insertion | |
| Stun | Stops anyone caught shooting for 5 s |
| Second Life | For 30 seconds one teammate can respawn once placed |
| Nomad | Lets you fight outside for 30 s |
| Faker | Fakes gunfire and footsteps |
| Striker | Drops 3 rockets on your smoke |
| Courier | Moves the flag once. Then it takes 1 more charge |
| Mirror | |
| Welder | |
| Supplier | Makes a blood bag every 30 s |
| Minefield | |
| Sentry | |
| Pitfall | A slippery plate and a snare that stuns |

## 6. What to test on the debug server

The debug server runs Match + ARENA + RAID (see the memory notes on the siege debug server).
Before calling a powerup done, check it in game:

- Hits table and one-piece-per-blast on every row of 3.2.
- Rocket numbers after moving the tuning to 519.
- Breach cost: build a sealed metal box around the flag and confirm the downgrade.
- Insertion within the arena, Faker's sound, Pitfall's slide into a snare, Courier pickup, replant and the extra charge, and the Supplier's timer and prompt.

Report anything that couldn't be verified instead of marking it done.
