"""Request and response shapes for the `/server/v1/*` API.

This is the one contract in the project with external callers compiled against it, so it
is the one worth writing down as types rather than as prose. Two things follow from
having them here:

- A write route validates its body into a model instead of reaching into a bare dict and
  hand-checking fields, so a malformed report is refused with a reason rather than
  reaching the scoring code.
- `/api` can state what every one of these routes answers, which it could not do while
  every handler returned an untyped dict.

Field names are camelCase, matching the convention the rest of the API documents, and
Steam64s are strings in and out, because JSON numbers lose their bottom digits.

Request models stay lenient on purpose: `extra="ignore"` so a newer plugin build sending
a field this version does not know about is not refused, and every counter defaults so an
older build that has never heard of a stat reports what it has.
"""

from pydantic import BaseModel, ConfigDict, Field


class ServerRequest(BaseModel):
    """Base for bodies a game server sends. Unknown fields are ignored, never refused:
    a plugin is deployed independently of the site and may be ahead of it."""

    model_config = ConfigDict(extra="ignore")


class PlayerLine(ServerRequest):
    """One player's line on a match result. Everything but the id is optional."""

    steam64: int
    won: bool = False
    kills: int = Field(0, ge=0)
    deaths: int = Field(0, ge=0)
    headshotKills: int = Field(0, ge=0)
    hits: int = Field(0, ge=0)
    headshotHits: int = Field(0, ge=0)
    damageDealt: int = Field(0, ge=0)
    damageTaken: int = Field(0, ge=0)


class RoundWinsReport(ServerRequest):
    """`POST /matches/{matchId}/rounds`. Each report replaces the last, so a lost one is
    covered by the next."""

    wins: dict[str, int] = Field(description="team name -> rounds won")

    def checked(self) -> dict[str, int]:
        if any(count < 0 for count in self.wins.values()):
            raise ValueError("rounds won cannot be negative")
        return {str(name): count for name, count in self.wins.items()}


class MatchResultReport(ServerRequest):
    """`POST /matches/{matchId}/result`. The match's final per-player line."""

    played: bool = Field(
        True, description="false when nobody turned up: scores nothing"
    )
    winningTeam: str | None = None
    draw: bool = False
    players: list[PlayerLine] = Field(
        default_factory=list,
        description="every player on the roster, including no-shows and leavers",
    )


class LobbyPopulationReport(ServerRequest):
    """`POST /lobby/population`, once per lobby heartbeat.

    `queueing` and `held` stay None when the lobby does not report them, rather than
    becoming zero: an older lobby that has never sent them must not read as nobody
    wanting a match.
    """

    players: int = Field(0, ge=0)
    queueing: int | None = Field(None, ge=0)
    held: int | None = Field(None, ge=0)


class GuardBan(ServerRequest):
    """`POST /players/{steam64}/bans`. An empty body bans for "Cheating"."""

    reason: str | None = None


class Status(BaseModel):
    """What a write answers. `status` says which of the two things happened, so a retried
    write can be told apart from a first one."""

    status: str
    matchId: int | None = None


class ReportAccepted(BaseModel):
    """`POST /reports`. `delivered` is false when Discord was down, which is not an
    error the game server could do anything about."""

    status: str
    delivered: bool


class MatchState(BaseModel):
    """`GET /matches/{matchId}`. `relayTo` is the server code to send this match's
    players to, and stays null until there is a server to send them to."""

    id: int
    region: str
    status: str
    relayTo: str | None


class Ban(BaseModel):
    reason: str
    expires: int | None
    permanent: bool


class Choices(BaseModel):
    """What a player still gets to decide, and what they have decided so far. A list with
    one entry is not a choice and comes back empty."""

    kits: list[str]
    powerups: list[str]
    kit: str | None
    powerup: str | None


class PlayerState(BaseModel):
    """One player as every player-facing route here reports them. A `matchId` with no
    `relayTo` means matched and still choosing."""

    steam64: str
    name: str
    avatar: str | None
    elo: int
    rankIcon: str
    matchId: int | None
    relayTo: str | None
    choose: Choices | None
    ban: Ban | None


class Decision(BaseModel):
    """`POST /players/{steam64}/decide`."""

    matchId: int
    choose: Choices | None


class Gamemode(BaseModel):
    """`bit` is the gamemode's position in the whole list, which is what a queue mask
    means. Positions never move."""

    bit: int
    name: str
    blurb: str
    usesElo: bool


class Gamemodes(BaseModel):
    gamemodes: list[Gamemode]


class Item(BaseModel):
    """A kit item, resolved from the stored `[itemId, amount, "1,2,3"]` tuple."""

    itemId: int
    amount: int
    state: list[int]


class SavedKit(BaseModel):
    """One of a player's saved kits, with the magazine broken out so a caller can keep
    the spare topped up."""

    items: list[Item]
    magazine: Item | None


class SessionAnswer(BaseModel):
    """`GET /session`, the one call a game server makes on a loop.

    Documented rather than enforced: this is the only answer here whose shape depends on
    `action`, and a response model would have to list every field as optional, which
    would add a null for each one a given answer does not carry. The contract is that a
    field is absent, not null, so the model describes the union and the route returns
    exactly what it built.

    `region` and `queuedInRegion` are on every answer, so a region mismatch reads off the
    console instead of looking like an empty queue.
    """

    action: str = Field(description="idle | wait | host | abort")
    region: str
    queuedInRegion: int
    matchId: int | None = Field(None, description="on wait and abort")
    reason: str | None = Field(
        None, description="on abort: unknownMatch | matchOver | hostedElsewhere"
    )
    waiting_on: list[str] | None = Field(None, description="on wait")
    selection_ends_at: int | None = Field(None, description="on wait, Unix seconds")
    match: dict | None = Field(
        None, description="the roster on wait, the whole match on host"
    )
    options: dict | None = Field(
        None, description="on wait: the kits and powerups to choose between"
    )
    picked: dict | None = Field(
        None, description="on wait: what each player has locked so far"
    )
