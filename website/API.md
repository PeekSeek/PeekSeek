# The game-server API

`/server/v1/*` is everything a game server needs to run PeekSeek matches, and nothing
else. It is deliberately not Unturned-specific: a server for a different game implements
the same calls and speaks the same JSON.

**This file is the reasoning. It is not the reference.** Every route's parameters and
response shapes are generated from the app itself into
[the server API reference](https://peekseek.net/docs/reference/server-api/), with the raw
schema at [`/openapi.json`](https://peekseek.net/openapi.json) and an interactive explorer
at `/api`. Those are always right; a table copied in here would not stay that way, and for
a while did not.

So what is here is the part a schema cannot state: why matchmaking runs inside the
server's own poll, why the result report is safe to retry forever, why `relayTo` is the
whole decision, and what each field is *for*. The types are one click away.

The models are in [src/routes/contracts.py](src/routes/contracts.py) and the handlers in
[src/routes/gameserver.py](src/routes/gameserver.py).

### What may change inside `v1`

`v1` is in the path because plugins are compiled against it and deployed independently of
the site, so the site can never assume which build is calling. Inside `v1`:

- **A field may be added** to any response at any time. That is why every response is a
  JSON object: parse loosely and ignore what you do not know.
- **A request field may become optional**, never required. A route that gains a field
  keeps working for a caller that has never heard of it.
- **A field will not be removed, renamed, or change type.** Neither will a path or a
  method. Those are what a new version number would be for.
- **A new `action`, `reason` or `status` value may appear.** Switch on the ones you know
  and treat the rest as "do nothing" rather than as an error.

There is no `v2` and no timetable for one. If there ever is, `v1` keeps answering until
every server we know of has moved off it.

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
poll* is what scans the queue, forms the best match it can, and takes it, all before
answering the request. A matchmade match is therefore created already assigned to the
server that will host it.

Matches players make for themselves (a custom match, an event pairing) do start with no
server. They are handed to the next server that comes free, and cancelled if none does
before selection closes plus `host_grace` seconds.

## `GET /session`

The one call a game server makes on a loop. Everything else here is a detail.

Two query parameters, and what they mean matters more than their types:
`serverCode` is the code players connect with, the server's own Steam ID, or `0` from a
server that cannot report one. `matchId` is the match this server considers itself
responsible for, **including one it has finished playing but not yet successfully
reported**, and `0` means free.

Exactly one of four answers comes back, and `action` is the only field to switch on.

```jsonc
{"action": "idle"}

{"action": "wait", "matchId": 42, "waitingOn": ["7656…"], "selectionEndsAt": 1786000060,
 "match": {"id": 42, "gamemode": {…}, "teams": [{"name": "North", "players": ["7656…"]}], "spectators": []},
 "options": {"kits": ["meta", "sniper"], "powerups": [{"name": "Boom", "items": [{"itemId": 519, "amount": 1, "state": []}]}]},
 "picked": {"7656…": {"kit": "meta", "powerup": null}}}

{"action": "host", "match": { /* see below */ }}

{"action": "abort", "matchId": 42, "reason": "matchOver"}
```

- **idle**: nothing to do. Ask again shortly.
- **wait**: this match is yours, but its players are still choosing. Hold it, keep
  sending this `matchId`, and let the roster in: `match` is who is on it, without the
  settings and kits that are still being decided, and the players are already on their
  way, because a match has a connect code from the moment a server holds it. `options`
  is what there is to choose, the same lists `choose` gives per player, with each
  powerup's items so a board can picture them; `picked` is what each player has chosen
  so far. Put a board in front of whoever is here with something unpicked and lock
  each answer with `POST /players/{steam64}/decide`; every poll carries the site's
  view of who has picked, which is what closes a board answered on the site instead.
- **host**: start it. **Sent exactly once per match.** Ask again while hosting and the
  answer is `idle`, so a slow poll cannot restart a round already being played.
- **abort**: drop it. `reason` is `unknownMatch`, `matchOver` or `hostedElsewhere`.

A server that still names a match is never told that match was abandoned. That is what
makes the result report safe to retry forever: for as long as the server keeps saying "I
am still holding match 42", the site will not decide match 42 was dropped.

### The match payload

The whole match, as the server hosting it needs to see it: its id and region, the
gamemode, the rounds to win, the settings, the teams, the players and the spectators.
The [reference](https://peekseek.net/docs/reference/server-api/) has the shape.

The one thing worth saying about it: `players` carries each player's **resolved** loadout,
so a server never has to know which item in a list was the gun and which was an outfit
piece, and `state` is the attachment bytes to stamp onto the item.

## Reporting a match

Two writes, and they are not the same kind of call.

`POST /matches/{id}/rounds` reports rounds won per team each time a round is decided.
**Do not retry it.** Each report replaces the last, so a lost one is covered by the next
one anyway.

`POST /matches/{id}/result` is the only thing that scores a match, and the one call that
must land. **Retry it until it is acknowledged.**

- `played: false` scores nothing and just releases the match, nobody turned up.
- `players` must be the **whole roster**, including anyone who never joined and anyone
  who left. A player the server omits is a player whose Elo never moves, which is what
  used to make walking out of a losing match free.

A ranked result moves two numbers per player. **Skill** is hidden and is what teams are
balanced on: a Glicko-2 style team rating moved by the win or loss alone, zero-sum within
the match, and never reset by a season. **Elo** is the ladder players see, rank and
Discord roles included: a flat `win_points`/`loss_points` per result, with bigger wins and
smaller losses while it sits below the player's skill. A draw moves no ladder, and kills
move neither. `Elo Change` in the summary is the ladder's move. Tunables are under
`[ranking]` in [src/config.toml](src/config.toml).

Answers `{"status": "recorded"}` or `{"status": "alreadyRecorded"}`. Repeats change
nothing, so a reply lost on the way back costs a duplicate request rather than a doubled
Elo swing. Accepted even for a match already marked finished, because that is exactly
what happens to a match whose result was slow to arrive.

## Players

`GET /players/{steam64}` and `PUT /players/{steam64}/queue` answer the same shape, and the
difference between them is the whole point: **the `GET` has no side effects and the `PUT`
queues them.** There is also a `DELETE` on the same path to take them out, a `kits` read,
a `decide` write for locking a pick, a `bans` write for anti-cheat, and `POST /reports`.

`ban` is `null` for a player who may play. The ban may be on another account: any active ban on an account seen on the same IP, on the site or
through `ip`, bars every account on that IP from queues and matches. **A caller holding
players must remove anyone it is non-null for.** `PUT .../queue` refuses to queue them,
so a lobby that ignores it ends up full of people who can never be matched.

It is on both reads, not just the queue call, because a ban can land while somebody is
already stood in an arena: nothing would otherwise reconsider them until they next tried
to queue. For the same reason the site drops a banned player's queue heartbeat rather
than skipping it, so population counts do not include somebody who cannot be matched.

`relayTo` is the whole decision for anything holding players. **Null means keep them;
non-null is the site's promise that a server holds the match and this player will be
let in.** It is set from the moment a server takes the match, during selection, because
the picks are made on that server as well as here: a player relayed from a lobby gets
the board there, and one who queued on the site can pick on either. `rankIcon` is the
badge the Elo earns, served at `/static/ranks/<icon>.png`, which a server draws on the
player's face.

`choose` is what this player still gets to decide, and null when there is nothing: no
match, or a gamemode that offers them one kit and one powerup. **A list with one entry in
it never appears.** The site resolves those itself, so an empty list means "do not put
this in front of them", and a caller can prompt without knowing the gamemode.

`POST /players/{steam64}/decide?kit=sniper&powerup=Boom` locks either or both and answers
with the same `choose` block, so a caller can re-prompt off the reply. Both values are
checked against the gamemode; an unknown one is a `400` rather than a match nobody can be
equipped for.

This is what keeps a lobby from having to wait out selection. `readyToHost` needs every
player to have chosen, or `selectKitsTime` to run out, so a player with no way to choose
from where they are standing costs their opponent the whole timer. Once every player has
locked both, the outstanding map/ballistics/movement picks are collapsed to their
defaults as well: nobody who chose from in-game is at a browser to make them.

The side-effect-free `GET` exists so a lobby can keep asking about a player who used
`/noqueue`: a match made on the site or by their party still has to pull them out of the
arena, and asking with `PUT` would quietly put them back in the queue.

`GET /matches/{id}` answers the same `status` and `relayTo` for one match, for anything
watching a match it is not hosting.

## Reports

`POST /reports` is **multipart/form-data**, not JSON, because the screenshot is the
point of it. Fields: `reported`, `reporter`, `reason`, and optionally `serverCode`,
`where`, `damageDealt`, `shotsFired`, `shotsHit`, `headshotHits`, and a `screenshot`
file part.

The screenshot is captured by asking the reported player's own client, which only a
game server can do. In Unturned that is `Player.sendScreenshot(CSteamID, PlayerSpyReady)`,
the same call the built-in `/spy` command makes. It has to arrive with the report rather
than as a second call, or a failure leaves a report with no evidence attached.

The counters are sent, not derived here: a lobby server and a match server disagree
about what "this round" means, and only they know which they are. Accuracy and headshot
share are computed from them for display.

Answers `{"status": "ok", "delivered": true|false}`. **`delivered: false` is not an
error**. The report reached the site and Discord did not take it, which is logged here
and is nothing a game server can act on.

## Lobby population

`POST /lobby/population` says what this server's arena looks like right now: `players` in
the arena and not on a match, and optionally `queueing` (how many of those want a match)
and `held` (how many you are keeping out of the queue).

Sent once per heartbeat for the whole server, not once per player: every number here is
a fact about the server rather than about anybody in it. Nothing reads the reply.

`queueing` and `held` are optional, and are stored as absent rather than as zero when
they are left out, a server that does not report them has not said nobody wanted a
match. Send them if you hold players back for any reason. Without them a lobby with
three people in it looks the same whether they are happily fighting or all three are
waiting on a match you have not offered up, and those are the two cases anything trying
to raise matches per day has to tell apart.

**Report only the players who are not on a match.** The site counts every roster it
handed out by itself, so a player standing in the arena picking their kit is already
counted there. Sending them here too counts them twice. This is what lets the two
halves add up: lobbies report themselves, match servers are never asked at all.

**The queue is not this number and cannot stand in for it.** A lobby may hold players
back from queueing to keep its own browser listing worth joining, so a busy arena can
honestly report nobody queueing at all. Anything that wants to say how alive the game is
(the Discord counter today) has to ask this, or it will announce an empty server with
six people fighting in it.

A server that stops reporting drops out after 30 seconds rather than being trusted,
so a lobby that crashed with eleven people in it stops advertising them.

Each reading is also kept as history, but only when it says something the reading before
it did not, so keep sending on your heartbeat and do not try to send only on change.
The site knows what you last said; you would only be guessing.

## Matchmaking rules

Two rules decide whether a roster is allowed to become a match. Failing them is a real
outcome: those players stay in the queue rather than being handed a bad match.

- **Teams must be within `max_team_elo_gap` average skill of each other** (200).
- **Two players are not put against each other again within `rematch_cooldown`** (300s).
  One repeated pairing rejects the whole roster, so `(a+b vs c+d)` blocks
  `(a+b vs c+e)`: swapping one team-mate still leaves a and b facing c for a second
  time.

Parties are never split across teams. Within those constraints the longest-waiting
players are matched first, and teams are balanced by skill: the hidden rating described
under the result, not the ladder Elo players see.

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
| `finished` | Scored, abandoned or cancelled: over either way. |

The match page reads `status` and `relayTo` from `/match/current`. They are separate
fields on purpose: a match has a server from the moment it is made, so "a server exists"
stopped being the same question as "the match has started".
