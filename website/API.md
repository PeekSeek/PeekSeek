# The game-server API

`/server/v1/*` is everything a game server needs to run PeekSeek matches, and nothing
else. It is deliberately not Unturned-specific: a server for a different game implements
the same calls and speaks the same JSON.

The reference implementation is [src/routes/gameserver.py](src/routes/gameserver.py).
Live, browsable docs for every route are at `/docs`.

## Conventions

Every route here follows all of these.

- **Auth is the caller's source IP**, allow-listed in `AuthorizedServers`. An unlisted
  address gets a `401` from everything under `/server/v1`.
- **Field names are camelCase.**
- **Steam64s are strings.** They are 64-bit and JSON numbers are not, so anything
  parsing one as a number loses the bottom digits.
- **Every response is a JSON object**, never a bare string or array, so a field can be
  added later without changing the shape.
- **Durations are seconds; timestamps are Unix seconds.**
- **Writes that can be retried are idempotent**, and say which of the two happened.

## The idea

A match cannot exist without a game server holding it.

Matchmaking does not run when a player queues. A player queueing only adds themselves to
the queue. The moment a server in their region reports itself free, *that server's own
poll* is what scans the queue, forms the best match it can, and takes it — all before
answering the request. A matchmade match is therefore created already assigned to the
server that will host it.

Matches players make for themselves (a custom match, an event pairing) do start with no
server. They are handed to the next server that comes free, and cancelled if none does
before selection closes plus `host_grace` seconds.

## `GET /session`

The one call a game server makes on a loop. Everything else here is a detail.

| Parameter | Meaning |
|---|---|
| `serverCode` | The code players connect with — the server's own Steam ID. `0` from a server that cannot report one. |
| `matchId` | The match this server considers itself responsible for, including one it has finished playing but not yet successfully reported. `0` means free. |

Exactly one of four answers comes back, and `action` is the only field to switch on.

```jsonc
{"action": "idle"}

{"action": "wait", "matchId": 42, "waitingOn": ["7656…"], "selectionEndsAt": 1786000060}

{"action": "host", "match": { /* see below */ }}

{"action": "abort", "matchId": 42, "reason": "matchOver"}
```

- **idle** — nothing to do. Ask again shortly.
- **wait** — this match is yours, but its players are still choosing. Hold it, keep
  sending this `matchId`, and do not let anyone in: the roster that says who is even on
  it has not arrived yet.
- **host** — start it. **Sent exactly once per match.** Ask again while hosting and the
  answer is `idle`, so a slow poll cannot restart a round already being played.
- **abort** — drop it. `reason` is `unknownMatch`, `matchOver` or `hostedElsewhere`.

A server that still names a match is never told that match was abandoned. That is what
makes the result report safe to retry forever: for as long as the server keeps saying "I
am still holding match 42", the site will not decide match 42 was dropped.

### The match payload

```jsonc
{
  "id": 42,
  "region": "paris",
  "gamemode": {"id": 0, "name": "Competitive"},
  "roundsToWin": 3,
  "settings": {"map": "Alpha Valley", "ballistics": true, "movement": false},
  "teams": [{"name": "team_alice", "players": ["7656…"]}],
  "players": [
    {"steam64": "7656…", "kit": [{"itemId": 363, "amount": 1, "state": [1, 2, 3]}]}
  ],
  "spectators": ["7656…"]
}
```

`players` carries each player's resolved loadout, so a server never has to know which
item in a list was the gun and which was an outfit piece. `state` is the attachment
bytes to stamp onto the item.

## Reporting a match

| Route | Retry? | Why |
|---|---|---|
| `POST /matches/{id}/state` | **No** | The overlay snapshot. Each one replaces the last, so a dropped one costs nothing. |
| `POST /matches/{id}/kills` | **No** | One kill, sent as it happens. Batching them to the end would lose the whole log with the server. |
| `POST /matches/{id}/result` | **Yes, until acknowledged** | The only thing that scores a match. |

The result is the one call that must land.

```jsonc
POST /server/v1/matches/42/result
{
  "played": true,
  "winningTeam": "team_alice",
  "players": [
    {"steam64": "7656…", "won": true, "kills": 5, "deaths": 2,
     "headshotKills": 2, "hits": 30, "headshotHits": 7,
     "damageDealt": 640, "damageTaken": 410}
  ]
}
```

- `played: false` scores nothing and just releases the match — nobody turned up.
- `players` must be the **whole roster**, including anyone who never joined and anyone
  who left. A player the server omits is a player whose Elo never moves, which is what
  used to make walking out of a losing match free.

Answers `{"status": "recorded"}` or `{"status": "alreadyRecorded"}`. Repeats change
nothing, so a reply lost on the way back costs a duplicate request rather than a doubled
Elo swing. Accepted even for a match already marked finished, because that is exactly
what happens to a match whose result was slow to arrive.

## Players

| Route | What it does |
|---|---|
| `GET /players/{steam64}` | Rank and whereabouts. **No side effects.** |
| `PUT /players/{steam64}/queue` | Queue them, and answer the same thing. |
| `DELETE /players/{steam64}/queue` | Take them out of the queue. |
| `GET /players/{steam64}/kits` | Every saved kit, resolved into ready-to-equip items. |
| `POST /players/{steam64}/bans` | A ban issued by anti-cheat. Empty body bans for "Cheating". |

Both reads answer the same shape:

```jsonc
{"steam64": "7656…", "elo": 300, "matchId": 42, "relayTo": null}
```

`relayTo` is the whole decision for anything holding players. **Null means keep them;
non-null is the site's promise that the match is being hosted right now and this player
will be let in.** It stays null while the match is still in selection, so nobody is ever
sent to a server that would refuse them.

The side-effect-free `GET` exists so a lobby can keep asking about a player who used
`/noqueue`: a match made on the site or by their party still has to pull them out of the
arena, and asking with `PUT` would quietly put them back in the queue.

`GET /matches/{id}` answers the same `status` and `relayTo` for one match, for anything
watching a match it is not hosting.

## Matchmaking rules

Two rules decide whether a roster is allowed to become a match. Failing them is a real
outcome: those players stay in the queue rather than being handed a bad match.

- **Teams must be within `max_team_elo_gap` average Elo of each other** (200).
- **Two players are not put against each other again within `rematch_cooldown`** (300s).
  One repeated pairing rejects the whole roster, so `(a+b vs c+d)` blocks `(a+b vs c+e)`
  — swapping one team-mate still leaves a and b facing c for a second time.

Parties are never split across teams. Within those constraints the longest-waiting
players are matched first, and teams are balanced by Elo.

All three numbers, plus `host_grace`, live in
[src/config.toml](src/config.toml) under `[matchmaking]`.

## Match states

Derived from the match row rather than stored, so there is only one place each fact
lives.

| State | Meaning |
|---|---|
| `pending` | Exists, no game server holds it. |
| `picking` | A server is holding it while its players choose. |
| `live` | Handed over; the server is running it. |
| `finished` | Scored, abandoned or cancelled — over either way. |

The site's own pages read `status` and `serverCode` from `/match/current` and
`/match/overlay`. They are separate fields on purpose: a match has a server from the
moment it is made, so "a server exists" stopped being the same question as "the match has
started".
