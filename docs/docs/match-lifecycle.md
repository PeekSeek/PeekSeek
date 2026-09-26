# The match lifecycle

If you read one page to understand the design, read this one. Every awkward-looking
decision in the API comes from a single constraint.

!!! abstract "The constraint"
    A match cannot exist without a game server holding it.

---

## Servers pull, they are never pushed

Matchmaking does not run when a player queues. Queueing only adds the player to the
queue.

When a server reports itself free, **that server's own poll** scans the queue, forms the
best match it can, and takes it, all before answering the request. A matchmade match is
therefore created already assigned to the server that will host it.

The consequences are the whole point:

- No scheduler and no broker to run or supervise.
- No window where a match exists with nowhere to play it.
- A server that dies simply stops polling. Nothing has to notice or reassign.
- To add capacity you start a server. To remove it, you stop one.

Matches that players make for themselves, a custom match or an event pairing, are the
exception. They do start with no server, and are handed to the next server that comes
free. If none does before selection closes plus `host_grace` seconds, they are cancelled.

---

## The one call

A game server runs `GET /server/v1/session` on a loop, sending two things:

| Parameter | Meaning |
|---|---|
| `serverCode` | The code players connect with, the server's own Steam ID. `0` if it cannot report one. |
| `matchId` | The match it considers itself responsible for, including one it has finished but not yet successfully reported. `0` means free. |

Exactly one of four answers comes back. `action` is the only field to switch on.

```jsonc
{"action": "idle"}
{"action": "wait",  "matchId": 42, "waitingOn": ["7656…"], "selectionEndsAt": 1786000060}
{"action": "host",  "match": { /* full payload */ }}
{"action": "abort", "matchId": 42, "reason": "matchOver"}
```

| Answer | Do |
|---|---|
| `idle` | Nothing. Ask again shortly. |
| `wait` | Hold the match, keep sending its `matchId`, let nobody in. The roster has not arrived yet. |
| `host` | Start it. **Sent exactly once per match**, so a slow poll cannot restart a round already being played. |
| `abort` | Drop it. `reason` is `unknownMatch`, `matchOver` or `hostedElsewhere`. |

---

## Why the result report can be retried forever

A server that still names a match is **never** told that match was abandoned.

For as long as a server keeps saying "I am still holding match 42", the site will not
decide match 42 was dropped. That single rule is what makes the result call safe to retry
indefinitely, which matters because it is the only call that scores anything.

| Route | Retry | Why |
|---|---|---|
| `POST /matches/{id}/rounds` | No | Rounds won per team. Each replaces the last, so a dropped one costs nothing. |
| `POST /matches/{id}/result` | **Yes, until acknowledged** | The only thing that scores a match. |

The result answers `{"status": "recorded"}` or `{"status": "alreadyRecorded"}`. Repeats
change nothing, so a reply lost on the way back costs a duplicate request rather than a
doubled Elo swing. It is accepted even for a match already marked finished, because that
is exactly what happens to a result that was slow to arrive.

!!! danger "Report the whole roster"
    `players` must include everyone on the match, including anyone who never joined and
    anyone who left. A player the server omits is a player whose Elo never moves, which
    is what used to make walking out of a losing match free.

    `played: false` scores nothing and just releases the match. That is the honest answer
    when nobody turned up, and it is not the same as a real result that happened to be
    goalless.

## How a result is rated

A ranked result moves two numbers for every player on the roster.

- **Skill** is hidden, and it is what teams are balanced on. A team's rating is its
  players' mean, the expected score is logistic, and each player moves by a Glicko-2 step
  that is bigger the less certain their skill still is. Only the win, loss or draw
  counts, never kills. What one side gains the other loses, and seasons never touch it.
- **Elo** is the ladder: the number on the profile, the badge and the Discord role. A win
  is worth `win_points` and a loss costs `loss_points`. While the ladder sits below skill
  a win is worth more and a loss costs less, which is how a season's soft reset is won
  back; at or above skill it is the plain points, so a player winning half their games
  stays put. A draw is worth nothing, and nobody goes below 100.

`Elo Change` in the result summary is the ladder's move. The tunables are under
`[ranking]` in [`config.toml`](configuration.md).

---

## States

Derived from the match row rather than stored, so each fact lives in one place.

| State | Meaning |
|---|---|
| `pending` | Exists, no game server holds it. |
| `picking` | A server is holding it while its players choose. |
| `live` | Handed over. The server is running it. |
| `finished` | Scored, abandoned or cancelled. Over either way. |

`status` and `serverCode` are separate fields on purpose. A match has a server from the
moment it is made, so "a server exists" is not the same question as "the match has
started".

---

## What decides a roster

Two rules. Failing them is a real outcome: those players stay in the queue rather than
being handed a bad match.

- Teams must be within **`max_team_elo_gap`** average skill of each other.
- Two players are not put against each other again within **`rematch_cooldown`**.

One repeated pairing rejects the whole roster, so `(a+b vs c+d)` blocks `(a+b vs c+e)`.
Swapping one team-mate still leaves `a` and `b` facing `c` a second time.

Parties are never split across teams. Within those constraints, the longest-waiting
players are matched first and teams are balanced on skill. Every number here is in
`[matchmaking]` in [`config.toml`](configuration.md).

---

## Next

- [The full API contract](https://github.com/PeekSeek/PeekSeek/blob/main/website/API.md),
  including players, lobby population and the payload shapes
- [Forking for another game](forking.md), which is mostly a matter of reimplementing this
  loop
