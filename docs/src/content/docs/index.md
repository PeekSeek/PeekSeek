---
title: PeekSeek
description: An open-source matchmaking and esports platform
sidebar:
  order: 0
---
PeekSeek is an open-source matchmaking and esports platform. It runs
[peekseek.net](https://peekseek.net) for Unturned, and everything that makes it work is
in [one repository](https://github.com/PeekSeek/PeekSeek).

These docs are for three kinds of people:

- **You want to run your own instance.** Start at [Getting Started](/docs/getting-started/).
- **You want to understand how it works** before trusting it. Start at
  [Architecture](/docs/architecture/).
- **You want it for a different game.** Start at [Forking](/docs/forking/). The part of the
  system that talks to game servers was built to not care what game it is.

---

## What it does

| | |
|---|---|
| **Matchmaking** | Players queue, servers pull. Teams are balanced on Elo with a configurable gap, and a rematch cooldown stops the same two people meeting twice in a row. |
| **Match hosting** | A pool of game servers claims matches, runs them, and reports results back. Servers that go quiet are noticed. |
| **Lobbies** | A persistent arena players wait in, which queues them without leaving the game. |
| **Ranking** | Elo per player, adjusted from reported match results. |
| **Parties** | Queue together, stay together. |
| **Events** | Tournament pairings and signups on top of the same match machinery. |
| **Discord** | A live player counter, queue logging, and a ping when a dead arena wakes up. |

---

## The pieces

One repository, because the site and the plugins are two halves of one contract and a
change to both belongs in one commit.

**The [repository README](https://github.com/PeekSeek/PeekSeek#readme) lists every
component, what it is, and whether it is maintained.** That table lives there and only
there — this page used to carry a copy of it, which said "four deployables", listed five,
and had never heard of two of the gamemodes.

What is worth knowing from here is the shape rather than the inventory:

- **The website is the backend.** It holds the queue, the matches, the ratings and the
  moderation state, and it is the only thing with a database.
- **The plugins are clients of it.** Each one polls `/server/v1/*` and does what it is
  told; none of them talks to another, and none of them has state worth keeping.
- **Gamemodes plug into one plugin.** ARENA and RAID are gamemodes for PeekSeek Match,
  loaded beside it, not servers of their own.
- **The Discord bot imports the site's own code** rather than calling it over HTTP, so a
  ban issued in Discord and a ban issued on the dashboard are the same function.

The RocketMod plugins are the only Unturned-specific code in the system. The website never
imports anything that knows what Unturned is, which is the thing that makes
[forking it for another game](/docs/forking/) possible at all.

---

## The one idea worth reading first

**A match cannot exist without a game server holding it.**

Matchmaking does not run when a player queues. Queueing only puts you in the queue. When
a server reports itself free, *that server's own poll* scans the queue, forms the best
match it can, and takes it, all before answering the request.

So a matchmade match is created already assigned to the server that will host it. There
is no scheduler, no broker, and no window where a match exists with nowhere to play it.
[The match lifecycle](/docs/match-lifecycle/) walks through what that means in practice.

---

## Licence

MIT, per [LICENSE](https://github.com/PeekSeek/PeekSeek/blob/main/LICENSE). Fork it, run
it, sell it. Attribution is welcome and not required.

One exception, and it is a real one: `plugins/utelemetry/` is under
[PolyForm Noncommercial 1.0.0](https://github.com/PeekSeek/PeekSeek/blob/main/plugins/utelemetry/LICENSE),
which forbids commercial use. Selling a distribution that includes it breaches those
terms. It is an optional telemetry plugin nothing else depends on, so a commercial fork
drops the directory and the rest is MIT.
[NOTICE](https://github.com/PeekSeek/PeekSeek/blob/main/NOTICE) lists that and the
third-party fonts and assemblies.

---

## Quick links

- [Getting started](/docs/getting-started/) with a local instance
- [Architecture](/docs/architecture/) and how the halves talk
- [Self-hosting](/docs/self-hosting/) a real deployment
- [Configuration](/docs/configuration/), every knob in one place
- [Forking for another game](/docs/forking/)
- [The game-server API contract](https://github.com/PeekSeek/PeekSeek/blob/main/website/API.md)
