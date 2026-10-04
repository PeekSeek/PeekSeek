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

Four deployables, one repository. They share it because the site and the plugins are two
halves of one contract, and a change to both belongs in one commit.

| Component | What it is | Language |
|---|---|---|
| [`website/`](https://github.com/PeekSeek/PeekSeek/tree/main/website) | The whole site and API: pages, the JSON its own scripts call, and the `/server/v1/*` API game servers run on | FastAPI, Python 3.12 |
| [`plugins/peekseek/`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/peekseek) | Runs matches on a game server: match state, lifecycle, ping-based damage scaling | C#, net48 |
| [`plugins/peekseek-lobby/`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/peekseek-lobby) | Runs the waiting arena: queueing from in-game, kits, AFK handling | C#, net48 |
| [`plugins/utelemetry/`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/utelemetry) | PvP telemetry beyond kill counts | C#, net48 |
| [`plugins/peekseek-armor-skin/`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/peekseek-armor-skin) | Gives every player one of four skin colours and a full military set's damage resistance, whatever they wear | C#, net48 |

The RocketMod plugins are the only Unturned-specific code in the system. The website
never imports anything that knows what Unturned is.

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

MIT, per [LICENSE](https://github.com/PeekSeek/PeekSeek/blob/main/website/LICENSE). Fork
it, run it, sell it. Attribution is welcome and not required.

---

## Quick links

- [Getting started](/docs/getting-started/) with a local instance
- [Architecture](/docs/architecture/) and how the halves talk
- [Self-hosting](/docs/self-hosting/) a real deployment
- [Configuration](/docs/configuration/), every knob in one place
- [Forking for another game](/docs/forking/)
- [The game-server API contract](https://github.com/PeekSeek/PeekSeek/blob/main/website/API.md)
