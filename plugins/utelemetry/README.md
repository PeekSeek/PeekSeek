# uTelemetry

[![utelemetry](https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml/badge.svg)](https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml)
[![license](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

A RocketMod plugin for Unturned that extends PvP telemetry beyond simple kill counts — built by Homeless, originally for PeekSeek.

## Features

- Shot offset tracking telemetry.

## Requirements

- Unturned dedicated server with [RocketMod](https://github.com/SmartlyDressedGames/Legally-Distinct-Missile)
- .NET SDK 8.0+ (for building)
- Target framework: `net48`

## Building

```bash
dotnet restore uTelemetry.sln
dotnet build uTelemetry.sln --configuration Release
```

The compiled plugin DLL lands in `plugin/bin/Release/net48/`. Drop it into your server's `Rocket/Plugins/uTelemetry/` directory.

## Testing

```bash
dotnet test
```

## Linting

Formatting and analyzer rules are enforced via `dotnet format`:

```bash
dotnet format uTelemetry.sln
```

CI runs `--verify-no-changes` on every push and PR.

## Project structure

```
plugin/          RocketMod plugin (uTelemetryPlugin.cs, uTelemetryConfiguration.cs, src/)
  src/telemetry  Kill tracker and telemetry collectors
  src/util       Shared helpers (latency, etc.)
tests/           xUnit test project
```

Releases are tagged `utelemetry-v<version>`, since this repo ships more than one plugin.

## License

[PolyForm Noncommercial 1.0.0](LICENSE) — free for non-commercial use.
