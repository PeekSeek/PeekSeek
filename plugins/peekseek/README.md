# PeekSeek Match

[![peekseek-plugin](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml/badge.svg)](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml)

The RocketMod plugin that makes an Unturned server a PeekSeek match server. It holds
the session with the site, takes the match the site hands it, puts the roster in its
groups, keeps the spectators out of the way, waits for everybody to arrive, keeps the
score and the stats, reports the result, and draws every screen. What a match is made
of, where people spawn, what they carry, what a round is and who won it, is a
**gamemode's**, in a plugin of its own that loads beside this one:

| Plugin | What it runs | Site gamemodes |
| --- | --- | --- |
| [peekseek-arena](../peekseek-arena/) | Unturned's arena round, last team standing in a shrinking circle | everything, as the default |
| [peekseek-raid](../peekseek-raid/) | build a base, then take turns cracking each other's | `Raid` |

**A match server needs at least one gamemode plugin installed.** Match alone refuses a
match it has no gamemode for and says so in the log. The live servers run this plugin at
2.6.0, which still has the arena round inside it; 2.7.0 is the first version that needs
ARENA beside it.

## Projects

- [plugin/PeekSeek.csproj](plugin/PeekSeek.csproj), the plugin assembly
- [tests/PeekSeek.Tests.csproj](tests/PeekSeek.Tests.csproj), xUnit unit tests
- [ui/](ui/), the Unity project every in-game screen is built from

## The seam

`PeekSeek.Gamemodes` is the public surface a gamemode plugin is written against, and
nothing else in this assembly is:

- `IGamemode`: what a gamemode is told. Begin with a match, tick, players arriving,
  leaving, dying and respawning, end; and the three things the bar asks it, which team
  holds and which attacks this round, what each player picked, and the clock.
- `Gamemodes.Register(mode, isDefault)`: called when a gamemode plugin loads. Match
  hands a match to the mode whose `Name` matches the site's gamemode name, else to the
  default. Rocket loads plugins in whatever order, so this is a plain static list.
- `Host`: everything a gamemode may ask of the server. Who is playing, who is alive
  this round, the score, a round or the match won, a forfeit, a player's kit, the
  boards and the build panel, a redraw of the bar, chat in the house style.
- `GamemodeMatch`: the match as a gamemode sees it, teams with their Unturned group,
  spectators, and the site's resolved kit per player.

A gamemode plugin references `PeekSeek.csproj` with `Private="false"`, so it compiles
against the DLL without ever shipping a copy of it: two `PeekSeek.dll`s under
`Rocket/Plugins` are two plugins sharing statics.

## A match, start to finish

1. The site answers the session poll with `host` and a match. Everybody on the server
   is kicked, the groups are made, and the wait begins.
2. The wait: `LobbyTimeoutSeconds` from the hand-over, ended early the moment
   everybody on the roster is here. When it runs out, nobody here releases the match,
   one team here takes it, anything else starts short-handed. The bar is up from the
   first join, with the wait on its clock.
3. `Begin`: the gamemode gets the match and everybody present. Whoever joins later
   comes through `OnPlayerJoined`.
4. Rounds are the gamemode's. It calls `Host.RoundWon` when one is over, and
   `Host.MatchWon` when the match is; Match scores, announces, sends the overlay its
   snapshot, and after `DelayKickAfterWin` reports the result and sends everybody
   back to the lobby.

`/start <gamemode> [solo]` runs the same thing without the site: two teams from whoever
is on the server, kits from each player's saved loadout (`LocalKitCategory`), nothing
reported anywhere. `/stop` ends whatever is running. Both need the `peekseek.start`
permission, and an Unturned admin is never funnelled off an empty server, so there is
somebody to type them.

## The screens

Unturned cannot draw a custom UI from plugin code: `EffectManager.SendUIEffect` takes an
`EffectAsset`, and one of those only exists inside a Unity asset bundle the client has
already downloaded. So every screen is a prefab in the **PeekSeek UI** workshop item
(3803930118), built from [ui/](ui/), and a GUID in the config. A server without the
item logs one line per screen and draws nothing; the boards then answer themselves.

- **The bar** along the top, for the whole match: score and clock on one block in the
  middle, a face per player to either side, the viewer's own team on the left. A
  gamemode's role markers hang beside the scores and its pick on each face's corner;
  a gamemode that has neither leaves no gap, because the score line is a layout row.
  Under every face a health bar, green to half and amber after, drawn only while there
  is something to say, and only for the viewer's own team unless they are spectating.
- **The boards** a player answers, and **the build panel**, driven by a gamemode
  through `Host.Board` and `Host.Panel`.
- **The kill feed**, its own workshop item (3804490030) so the lobby can carry it
  without the rest. The same source is compiled into the lobby plugin.

`ui/README.md` has the whole of it, including how a bundle is built and shipped.

## Configuration

On first load, Rocket generates `PeekSeek.configuration.xml` next to the plugin DLL. Root
element is `<Config>`. **Rocket does not merge new fields into an existing file**: a
field missing from a live XML reads as `0`, `false` or null, not its default, so a
server that gets a new version needs the new elements written in by hand.

| Field | Description |
| --- | --- |
| `szAPIURL` | The site, as the bare origin: `https://peekseek.net`. `/server/v1` is appended. |
| `szSpectatorGroup` | Rocket permission group applied to players in spectator mode. |
| `szHexDefaultChatColor` | Default chat color for plugin system messages. |
| `szChatIconURL` | Icon shown alongside plugin chat messages. |
| `UseTestingJSON` | Enables test fixtures instead of live backend data. |
| `PingConfig` | List of `PingPrecentage` entries mapping ping thresholds to damage multiplier percentages. |
| `LobbyTimeoutSeconds` | How long the wait for the roster lasts. **A config without this element reads as 0 and times out instantly.** |
| `LobbyTimeoutBroadcastSecondsRemaining` | Seconds-remaining marks at which the wait is announced in chat. |
| `DelayKickAfterWin` | Seconds between the match-winning round and clearing the server, so players can see the result. |
| `LobbyServerRelaySGSSteamID` | Steam64 of the lobby server players are relayed to when a match ends. `0` disables relaying and just kicks them. |
| `RestartWhenIdle`, `MaxUptimeMinutes`, `DrainSeconds` | Whether and when this server takes itself down once idle, so it comes back on the current build. Needs the host's restart schedule; see the repo README. |
| `MatchHudEffectGUID`, `MatchHudKey` | The bar: the effect in the UI workshop item, and the channel it is sent on. |
| `PickUIEffectGUID`, `PickUIKey` | The boards. |
| `BuildPanelEffectGUID`, `BuildPanelKey` | The build panel. |
| `KillFeedEffectGUID`, `KillFeedKey`, `KillFeedRows`, `KillFeedRowSeconds`, `KillFeedNameLength` | The kill feed: the effect in the Kill Feed workshop item, its channel, how many rows the prefab has, how long a row stays, how many characters of a name fit. |
| `ItemIconURL` | Where an item's picture comes from, `{0}` the item id. |
| `DefaultAvatarURL` | The face for a player the site has no picture for yet. |
| `LocalKitCategory` | Which saved kit a `/start` match uses. |

`Spawns` left this file: the arena's round spawns are ARENA's, in its own config.

## Running the tests

```bash
dotnet test PeekSeek.sln
```

Tests live directly under [tests/](tests/) and cover logic that does not require a
running Unturned/Rocket host: the kit parser, the config and its 1.0 fixture, the stat
service, the round log, the fight tracker, the stats command, the kill feed's
arithmetic, and the death-cause wording. Everything that touches a `UnityEngine.*` type
cannot run under the net48 test host, because it cannot load `UnityEngine.CoreModule`
at all: even a method whose only reference sits in an unreached branch fails, since the
JIT resolves a method's full type references before running any of it.

## Continuous integration

[../../.github/workflows/peekseek-plugin.yml](../../.github/workflows/peekseek-plugin.yml)
lints (`dotnet format --verify-no-changes`), tests, and on `main` with a bumped
`<Version>` tags `peekseek-v<version>` and attaches the built DLL. ARENA's and RAID's
workflows also run on a change under `plugins/peekseek/plugin/`, since they compile
against it.

## Layout

```
plugins/peekseek/
├── PeekSeek.sln
├── plugin/
│   ├── PeekSeek.cs                  # Rocket hook, and every line of copy
│   ├── PeekSeekConfiguration.cs
│   ├── Commands/                    # /start, /stop, /stats, /report, /ping
│   └── Core/
│       ├── PSManager.cs             # the match server
│       ├── Gamemodes/               # the seam: IGamemode, Host, Gamemodes, GamemodeMatch
│       ├── Hud/                     # the bar, the boards, the build panel, the kill feed
│       ├── Patches/                 # the native arena loop switched off
│       ├── Web/RemoteAPI.cs         # the session, results, kills, kits and pictures
│       └── ...                      # stats, the overlay snapshot, the fight tracker
├── tests/
└── ui/                              # the Unity project the workshop items are built from
```
