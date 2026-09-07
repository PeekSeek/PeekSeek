# PeekSeek Siege

Build a base. Then take turns cracking each other's.

Two teams get five minutes on opposite ends of Alpha Valley to put up a building. After
that the buildings never change again: they are photographed, and re-laid brick for brick
before every single round. Three rounds each side, then the sides swap. The only thing
you choose between rounds is what you carry in.

That split is the whole design. A team that spends five minutes on a fortress has to both
hold it and, three rounds later, break into the mirror image of it. A base that is only
good to defend is a bad base.

## The loop

| Phase | What happens |
| --- | --- |
| Build | 5 minutes. Both teams build, each locked to their own plot. |
| Pick | 20 seconds. Five options on the board. Defence or raid, depending which side you are. |
| Round | 3 minutes. Attackers breach, defenders hold. |

Rounds 1-3 team North defends, rounds 4-6 team South does. Level at 3-3 goes to overtime:
two rounds each, over and over, and the match only ends when somebody is ahead at the end
of a full block. Never mid-block — half a block is not a fair swap.

## The three rules that make it a siege

**Defenders take damage standing on dirt.** Two seconds off your own building and open
ground starts killing you, the same way the arena boundary does. The grace period matters
more than the damage: vaulting a windowsill, dropping through a hatch and landing a jump
short all leave you briefly on the ground while doing exactly what a defender should be
doing. Two seconds covers all of that and nothing longer.

**Almost nothing in a base can be taken down.** When a base is re-laid, every piece comes
back owned by nobody, and in Unturned an unowned piece cannot be salvaged by anyone. Two
exceptions:

- *Metal doors and hatches* are handed to the team every round, because an unowned door
  is a door nobody can open.
- *Birch walls* are handed to the team too. These are the soft walls: build a wall in
  birch and you can pull it down mid-round to open an angle that was not there before.
  Metal walls, every floor and every pillar stay unowned, so a base cannot be dismantled
  into a better base while a round is running.

**One base per site.** Each plot gets a metal floor with a claim flag on it, dropped by
the gamemode, owned by nobody, and impossible to salvage or move. Teams are given no
floors of their own, so the only place to build is off that anchor. Nobody ends up with
three shacks in a field and a choice about which one to defend.

Plus the leash: both sites are on one map, and during the build phase you cannot leave
your own plot. Walk too far and you are put back. That is what keeps the first round worth
playing.

## Running it

```
/start          two or more players, split into two teams
/start solo     one player, for testing
/stop           end it
/pick 3         answer the option board
/siege          where the match is
```

Solo is how the whole loop gets tested without finding a second person for a five-minute
build phase. It builds both sites in turn — you build North, it saves, you build South —
then runs the rounds normally with you on whichever side you ask for.

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
already downloaded — so a UI cannot ship in a `.dll`, only in a workshop mod.

The board is written against both:

- Set `PickUIEffectGUID` in the config and it draws the real Unity layout, five clickable
  tiles, with the timer ticking down in place.
- Leave it empty and it draws the same five options to chat, answered with `/pick`.

`ui/README.md` is the prefab spec — the child object names the plugin writes into, and
the build steps. The chat form is not a placeholder waiting to be deleted: a player whose
client is still downloading the bundle needs to be able to answer the board too, and a
round is never going to wait for them.

## Where things are

```
plugin/PeekSeekSiege.cs         Rocket entry point, and every line of player-facing copy
plugin/Core/Siege.cs            the gamemode: phases, rounds, and the rules above
plugin/Core/SiegeHost.cs        what starts a match and feeds it Unturned's events
plugin/Core/SiegeWorld.cs       everything Siege does to the world, and the build/salvage gate
plugin/Core/BaseSnapshot.cs     photographing a base and re-laying it
plugin/Core/RoundOrder.cs       who defends round N, and when a match can end
plugin/Core/PickBoard.cs        the five-option board, both ways of drawing it
plugin/Core/IGamemode.cs        the seam the main PeekSeek plugin will drive this through
ui/                             the Unity prefab spec for the real board
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
category), fetched on join and held until the player leaves — a round hands out gear right
now and is not going to wait on an HTTP round trip.

See `BRIEF.md` for the original instructions and the three places this deviates from them.
