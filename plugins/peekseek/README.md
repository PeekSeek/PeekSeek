# PeekSeek

[![peekseek-plugin](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml/badge.svg)](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml)

RocketMod plugin for Unturned that powers game servers connected to the PeekSeek.net match backend. Handles lobby lifecycle, match state sync, ping-based damage scaling, and player stats.

## Projects

- [plugin/PeekSeek.csproj](plugin/PeekSeek.csproj) — the plugin assembly
- [tests/PeekSeek.Tests.csproj](tests/PeekSeek.Tests.csproj) — xUnit unit tests

## Building

Dependencies on Rocket / Unturned / Unity assemblies are pulled in via the [RestoreMonarchy.RocketRedist](https://www.nuget.org/packages/RestoreMonarchy.RocketRedist) NuGet package, so no `lib/` folder with hand-copied DLLs is required.

```bash
dotnet restore PeekSeek.sln
dotnet build PeekSeek.sln -c Release
```

The output assembly lands in [plugin/bin/Release/net48/](plugin/bin/Release/net48/). Drop `PeekSeek.dll` into your server's `Rocket/Plugins/PeekSeek/` directory.

`ExcludeAssets="runtime"` is set on the RocketRedist reference in the plugin project so the Rocket/Unturned DLLs are used only for compilation and are not copied next to the plugin output — the server already ships them.

## Configuration

On first load, Rocket generates `PeekSeek.configuration.xml` next to the plugin DLL. Root element is `<Config>`.

| Field | Description |
| --- | --- |
| `szAPIURL` | PeekSeek backend endpoint the plugin connects to. |
| `szSpectatorGroup` | Rocket permission group applied to players in spectator mode. |
| `szHexDefaultChatColor` | Default chat color for plugin system messages. |
| `szChatIconURL` | Icon shown alongside plugin chat messages. |
| `UseTestingJSON` | Enables test fixtures instead of live backend data. |
| `Spawns` | List of `HomelessSpawn` positions (fallback spawn points). |
| `PingConfig` | List of `PingPrecentage` entries mapping ping thresholds to damage multiplier percentages. |

## Running the tests

```bash
dotnet test PeekSeek.sln
```

Tests live directly under [tests/](tests/) and cover logic that does not require a running Unturned/Rocket host.

## Test cases

### Covered by automated tests

| Area | Case | Expectation |
| --- | --- | --- |
| [PSItemConverter](plugin/Core/Models/PSData.cs) | Single kit item `[363, 1, "100,0,255"]` | Parses into `PSItem { ItemID=363, Amount=1, State=[100,0,255] }` |
| [PSItemConverter](plugin/Core/Models/PSData.cs) | Empty state string `""` | `State` is an empty byte array (no `byte.Parse` on empty segments) |
| [PSItemConverter](plugin/Core/Models/PSData.cs) | Multiple items | All items returned in source order with correct IDs and amounts |
| [PSItemConverter](plugin/Core/Models/PSData.cs) | `WriteJson` invoked | Throws `NotImplementedException` (one-way converter contract) |
| [Ex.ToFemaleSalary](plugin/Core/Ex.cs) | `100`, `5`, `0`, `1`, `-10`, `1000` | Integer truncation of `salary * 0.8` (`80`, `4`, `0`, `0`, `-8`, `800`) |
| [PeekSeekConfiguration](plugin/PeekSeekConfiguration.cs) | After `LoadDefaults()` | `Spawns` and `PingConfig` are non-null and non-empty |
| [PeekSeekConfiguration](plugin/PeekSeekConfiguration.cs) | After `LoadDefaults()` | Known defaults set (`sJoinTimeout=120`, `szSpectatorGroup="spectator"`, `szHexDefaultChatColor="#FFFFFF"`, `UseTestingJSON=false`) |
| [PeekSeekConfiguration](plugin/PeekSeekConfiguration.cs) | `[XmlRoot]` attribute | Root element is `Config` so pre-2.0 config files still deserialize |
| [StatService](plugin/Core/StatService.cs) | `UpdateStatWins` then `GetStatWins` for new player | Returns `1` |
| [StatService](plugin/Core/StatService.cs) | `UpdateStatKills` called three times | `GetStatKills` returns `3` |
| [StatService](plugin/Core/StatService.cs) | `GetStatDeaths` for unknown player | Returns `0` (null-safe path) |
| [StatService](plugin/Core/StatService.cs) | Two players with 2 + 3 wins | `GetTotalPlayedRounds` returns `5` |

### Not covered (require running Unturned/Rocket host)

- `PSManager` event handlers (`OnDamageRequested`, `OnClientConnected`, `OnPlayerDead`, etc.)
- `RemoteAPI` WebSocket I/O
- `LevelManagerPatch` Harmony patches
- `ConfigurationManager` SDG gameplay-flag mutations
- `SerializableVector3.ToVector3()` (constructs `UnityEngine.Vector3`)

The ping damage multiplier resolution in `PSManager.OnDamageRequested` is the most interesting untested bit; testing it without a live server would need the pure selection logic extracted into a helper.

## Continuous integration

[../../.github/workflows/peekseek-plugin.yml](../../.github/workflows/peekseek-plugin.yml) runs both jobs, and only when something under `plugins/peekseek/` changes:

- `lint` — `dotnet format --verify-no-changes`. Run `dotnet format PeekSeek.sln` locally to fix violations.
- `test` — `dotnet test` on push and PR to `main`.

## Layout

```
plugins/peekseek/
├── PeekSeek.sln
├── plugin/
│   ├── PeekSeek.csproj
│   ├── PeekSeek.cs                  # Rocket hook
│   ├── PeekSeekConfiguration.cs
│   └── Core/                        # PSManager, RemoteAPI, patches, models
└── tests/
    ├── PeekSeek.Tests.csproj
    ├── PSItemConverterTests.cs
    ├── ExTests.cs
    ├── PeekSeekConfigurationTests.cs
    └── StatServiceTests.cs
```
