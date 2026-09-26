"""Issuing a ban, and undoing what a cheat did to everyone else's record.

One implementation, three front doors: the admin dashboard, the Discord bot, and the
anti-cheat endpoint game servers already call. A cheating ban that refunds players from
the dashboard but not from Discord would be worse than having only one of them.

The refund reads each tainted match's own stored result line rather than recomputing an
Elo delta, so every player is given back exactly what that match gave them. Two counters
cannot be refunded: `damageDealt` and `damageTaken` are passed to `Stat.append` but never
written into the results blob, so no per-match value survives to subtract.
"""

import logging
from time import time

from src.core.db import open as opendb
from src.core.settings import settings
from src.core import models as data

# Result-blob key -> Stats column. Rows written before hit tracking simply have no key
# for the last two, which is why every lookup below defaults to 0.
COUNTERS = {
    "Kills": "kills",
    "Deaths": "deaths",
    "Headshots": "headshots",
    "Hits": "hits",
    "HeadshotHits": "headshotHits",
}

DAY = 24 * 60 * 60

# What each preset means, in one place, so the dashboard tiles, the bot's choice list
# and the stored reason cannot disagree about how long a Toxicity ban lasts.
PRESETS = {
    "cheating": {
        "label": "Cheating",
        "days": None,
        "voidsHistory": True,
        "summary": "Permanent, voids their match history",
    },
    "matchfixing": {
        "label": "Matchfixing",
        "days": 30,
        "voidsHistory": False,
        "summary": "1 month",
    },
    "toxicity": {
        "label": "Toxicity",
        "days": 7,
        "voidsHistory": False,
        "summary": "1 week",
    },
}


def endTimeFor(preset: str | None, days: int | None) -> int:
    """When a ban with these terms runs out, as a Unix second.

    A preset's duration always wins over a passed-in one. That is the point of a
    preset: an admin who picks Toxicity is choosing not to decide the length, and
    letting a stray `days` override it would quietly produce a ban nobody chose.
    """
    if preset and preset in PRESETS:
        days = PRESETS[preset]["days"]

    if days is None:
        return data.UserBan.PERMANENT
    return int(time()) + int(days) * DAY


def noteWithAuthor(note: str | None, byName: str, bySteam64) -> str:
    """The stored moderator note, always opening with who issued the ban.

    Every existing note in production is hand-typed and starts with some variation of
    "by homeless", which is exactly the thing that should not have been left to whoever
    was typing: three of them spell it differently and one records no author at all.
    """
    prefix = f"by {byName}/{bySteam64}"
    note = (note or "").strip()
    return f"{prefix} {note}" if note else prefix


def taintedMatches(db, steam64) -> list:
    """Every match record this player has a final line in, oldest first.

    Filtered in Python rather than with a JSON path operator, for the same reason
    `MatchResult.latestFor` is: `->`/`->>` reads the row on Postgres and silently
    matches nothing on an older SQLite, so a test suite would pass while production
    refunded nobody.
    """
    key = str(steam64)
    rows = db.query(data.MatchResult).order_by(data.MatchResult.endTime.asc()).all()
    return [row for row in rows if key in (row.data.get("results") or {})]


def _reversals(matches: list, steam64) -> dict:
    """Per-player totals to subtract from Stats, keyed by steam64.

    Both directions. A player who lost Elo to a cheat gets it back; one who beat them
    and gained Elo loses that gain, because the match should never have counted at all.
    """
    totals: dict[int, dict[str, int]] = {}

    for match in matches:
        for other, line in match.data["results"].items():
            if int(other) == int(steam64):
                continue

            delta = totals.setdefault(int(other), {})
            delta["elo"] = delta.get("elo", 0) + line.get("Elo Change", 0)
            for key, column in COUNTERS.items():
                delta[column] = delta.get(column, 0) + line.get(key, 0)

            column = "wins" if line.get("Won") else "losses"
            delta[column] = delta.get(column, 0) + 1

    return totals


def previewBan(steam64) -> dict:
    """What a cheating ban on this player would do, without doing any of it.

    The dashboard drawer and the bot's confirmation both render this, so the numbers an
    admin approves are the numbers that happen rather than an estimate written next to
    the button.
    """
    with opendb() as db:
        matches = taintedMatches(db, steam64)
        totals = _reversals(matches, steam64)
        stats = db.query(data.Stat).where(data.Stat.steam64 == int(steam64)).first()

        refunds = [
            {"steam64": str(other), "elo": -delta.get("elo", 0)}
            for other, delta in totals.items()
        ]
        refunds.sort(key=lambda r: -r["elo"])

        return {
            "steam64": str(steam64),
            "matches": len(matches),
            "since": matches[0].endTime.isoformat() if matches else None,
            "players": len(refunds),
            "eloReturned": sum(r["elo"] for r in refunds),
            "refunds": refunds,
            "stats": (
                {
                    "elo": stats.elo,
                    "kills": stats.kills,
                    "deaths": stats.deaths,
                    "wins": stats.wins,
                    "losses": stats.losses,
                }
                if stats
                else None
            ),
        }


def _applyRefunds(db, totals: dict) -> None:
    """Subtract each player's tainted totals from their Stats row.

    Clamped so nothing lands somewhere impossible. Elo has a floor of 100 because
    `_ladderDelta` never lets it fall below that, and a counter cannot be negative. In
    practice neither clamp fires on a consistent database; they exist so that one bad
    row cannot write a profile nobody can explain.
    """
    for steam64, delta in totals.items():
        stats = db.query(data.Stat).where(data.Stat.steam64 == steam64).first()
        if stats is None:
            continue

        update = {}
        for column, amount in delta.items():
            floor = 100 if column == "elo" else 0
            update[column] = max((getattr(stats, column, 0) or 0) - amount, floor)

        db.query(data.Stat).where(data.Stat.steam64 == steam64).update(update)


def _voidMatches(db, matches: list) -> None:
    """Move each tainted match's results out of the key the site reads.

    `MatchResult.latestFor` and the profile's Elo graph both key off `results`, so
    renaming it to `voidedResults` makes a voided match vanish from both without a code
    change, while keeping the whole record for an admin to look at later. Leaving it in
    place would show refunded players a match list and a graph that contradict the Elo
    they now have.
    """
    for match in matches:
        voided = {k: v for k, v in match.data.items() if k != "results"}
        voided["voidedResults"] = match.data["results"]
        voided["voided"] = "cheating"
        db.query(data.MatchResult).where(data.MatchResult.id == match.id).update(
            {"data": voided}
        )


def issueBan(
    steam64,
    preset: str | None = None,
    days: int | None = None,
    reason: str | None = None,
    note: str | None = None,
    byName: str = "PeekSeek",
    bySteam64="system",
) -> dict:
    """Ban a player, and for a cheating ban undo what they did to everyone else.

    Returns the same shape `previewBan` does, so a caller can report what happened in
    the same words it warned about. Everything happens in one session: a refund that
    landed while the ban insert failed would leave eleven people short of Elo and the
    cheat still playing.
    """
    preset = (preset or "").lower() or None
    if preset and preset not in PRESETS:
        raise ValueError(f"Unknown preset: {preset}")

    voids = bool(preset and PRESETS[preset]["voidsHistory"])
    reason = PRESETS[preset]["label"] if preset else (reason or "").strip()
    if not reason:
        raise ValueError("A ban needs a reason.")

    summary = previewBan(steam64) if voids else None

    # Before the ban lands, because the answer stops being available once they are
    # no longer anybody's problem: a match already in progress has to be ended, and
    # this is the only moment we still know which one it is.
    live = data.User.getCurrentMatchId(steam64) if voids else None

    with opendb() as db:
        # Most people worth banning have never opened the site: they were reported from
        # a game server, and the site only creates an account when somebody logs in or
        # a result comes in for them. Without a row here the ban lands on an id with no
        # name, no avatar and nothing to hang a profile off, so every list that draws
        # them shows "Nameless" and anything joined to Users refuses them outright.
        #
        # ensure fetches their Steam name and avatar on the way, so the record reads as
        # a person from the moment it exists rather than after their next login.
        data.User.ensure(db, int(steam64))

        if voids:
            matches = taintedMatches(db, steam64)
            _applyRefunds(db, _reversals(matches, steam64))
            _voidMatches(db, matches)
            db.query(data.Stat).where(data.Stat.steam64 == int(steam64)).delete()

        # A player already serving a ban is not banned harder by a second row, and the
        # duplicate only makes the history list harder to read. The exception is a ban
        # that would outlast the one they are already serving.
        active = data.UserBan.activeFor(steam64, db)
        endTime = endTimeFor(preset, days)
        if active is None or endTime > active.endTime:
            db.add(
                data.UserBan(
                    steam64=int(steam64),
                    endTime=endTime,
                    reason=reason,
                    moderatorNote=noteWithAuthor(note, byName, bySteam64),
                )
            )

        # Whatever else happens, they stop being match material now rather than at
        # their next heartbeat.
        db.query(data.UserHB).where(data.UserHB.steam64 == int(steam64)).delete()

        # A cheat's match in progress ends with them. Leaving it running would score a
        # match that has already been decided to be worthless, and the Elo it moved
        # would land after everything else was refunded, which is the one order that
        # puts the numbers back wrong.
        if live:
            db.query(data.Match).where(data.Match.id == live).update({"finished": True})

    if live:
        data.MatchResult.recordUnscored(live, "abandoned")

    if voids:
        summary["cancelledMatch"] = live
        _logRefunds(steam64, reason, byName, summary)

    return summary or {"steam64": str(steam64), "matches": 0, "players": 0}


def _logRefunds(steam64, reason: str, byName: str, summary: dict) -> None:
    """Post the full refund list to the moderation channel.

    Every refunded player is named, because the people whose Elo moved are the ones
    who have to be able to check that it moved by the right amount, and a summary
    that only says "11 players" is not something anyone can check.
    """
    if not summary or not summary["refunds"]:
        return

    lines = "\n".join(
        f"[{r['steam64']}]({settings.base_url}/u/{r['steam64']}) `{r['elo']:+d}` Elo"
        for r in summary["refunds"]
    )
    embed = {
        "title": f"{reason} ban: {summary['players']} players refunded",
        "url": f"{settings.base_url}/u/{steam64}",
        "color": 0xF04444,
        "description": (
            f"**{steam64}** banned by {byName}.\n"
            f"{summary['matches']} matches voided, "
            f"**{summary['eloReturned']:+d}** Elo returned in total.\n\n{lines}"
        )[:4000],
    }

    from src.core.notify import send_discord_channel

    if not send_discord_channel(settings.report_channel, embed=embed):
        logging.warning("Refund list for %s was not posted to Discord", steam64)


def overturnBan(banId: int, byName: str, bySteam64) -> dict | None:
    """Lift one ban, and record in its own note that it was lifted and by whom.

    The row is kept with `endTime` set to 0 rather than deleted. A ban that was wrong is
    part of a player's history in exactly the way a ban that stood is: deleting it would
    make an admin looking at a repeat offender see a clean record, and would quietly
    erase the fact that somebody overturned it.

    Zero rather than "now" because `activeFor` compares against the current time, and a
    value in the past is unambiguous however clocks drift. Returns None when there is no
    such ban.
    """
    from datetime import datetime, timezone

    stamp = datetime.now(timezone.utc).strftime("%d %b %Y")

    with opendb() as db:
        ban = db.query(data.UserBan).where(data.UserBan.id == banId).first()
        if ban is None:
            return None

        note = (ban.moderatorNote or "").strip()
        overturn = f"[overturned by {byName}/{bySteam64} on {stamp}]"
        db.query(data.UserBan).where(data.UserBan.id == banId).update(
            {
                "endTime": 0,
                "moderatorNote": f"{note} {overturn}".strip(),
            }
        )

        lifted = {
            "id": ban.id,
            "steam64": str(ban.steam64),
            "reason": ban.reason,
            "wasPermanent": ban.permanent,
        }

    _logOverturn(lifted, byName, bySteam64, note)
    return lifted


def _logOverturn(lifted: dict, byName: str, bySteam64, previousNote: str) -> None:
    """Announce a lifted ban to staff, pinging the role.

    Louder than issuing one on purpose. A ban is one person's call and stands until
    somebody argues with it; undoing one reverses a colleague's decision, and the rest
    of staff finding out by accident is how the same player gets banned again next week.

    The ping goes in the content, never the embed: a mention inside an embed renders
    as a mention and notifies nobody.
    """
    from src.core.notify import send_discord_channel

    role = settings.staff_role
    embed = {
        "title": f"Ban lifted: {lifted['reason']}",
        "url": f"{settings.base_url}/u/{lifted['steam64']}",
        "color": 0x6CC66C,
        "description": (
            f"**{lifted['steam64']}** is no longer banned.\n"
            f"Lifted by **{byName}** (`{bySteam64}`).\n\n"
            f"The ban was {'permanent' if lifted['wasPermanent'] else 'temporary'}."
        ),
        "fields": [{"name": "Original note", "value": (previousNote or "none")[:1000]}],
    }

    if not send_discord_channel(
        settings.overturn_channel,
        content=f"<@&{role}>" if role else None,
        embed=embed,
        allowed_mentions={"roles": [role] if role else []},
    ):
        logging.warning("Overturn of ban %s was not posted to Discord", lifted["id"])
