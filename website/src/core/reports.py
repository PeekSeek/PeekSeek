"""In-game /report: the spy screenshot, the numbers, and the two buttons staff need.

The game server captures the screenshot because only it can: `Player.sendScreenshot`
asks the reported player's own client for it. Everything after that happens here so the
bot token, the embed shape and the staff role live in one place rather than being
copied into both plugins.
"""

import logging

import requests

from src.core.notify import DISCORD_API
from src.core.settings import settings
from src.core import models as data


def _percent(part: int, whole: int) -> str:
    """`part` of `whole` to one decimal, or "n/a" when there is nothing to divide.

    Not 0%: a player who has not fired a shot has no accuracy, and printing zero
    would read as someone who missed every shot they took.
    """
    if not whole:
        return "n/a"
    return f"{part / whole * 100:.1f}%"


def _statLines(stats: dict) -> str:
    """The numbers a moderator judges a report on, and nothing else.

    Accuracy and headshot share are the two that separate a cheat from a good player,
    and neither means anything as a raw count, which is why both are shown as a
    percentage of the shots actually taken rather than as "412 hits".
    """
    fired = int(stats.get("shotsFired") or 0)
    hits = int(stats.get("shotsHit") or 0)
    headshots = int(stats.get("headshotHits") or 0)
    damage = int(stats.get("damageDealt") or 0)

    return (
        f"**{damage}** damage this round\n"
        f"**{_percent(hits, fired)}** of {fired} shots hit\n"
        f"**{_percent(headshots, fired)}** of shots were headshots"
    )


def _buttons(reported, serverCode: str | None) -> list:
    """Join the server, and ban the player. Both link buttons, so neither needs the
    bot to be running to work, because a link button is a URL, not an interaction.

    The ban link opens the admin drawer with the player already loaded, which is where
    the preset reasons and the moderator note field already live. Rebuilding that as a
    Discord modal would be a second implementation of the same form.
    """
    row = [
        {
            "type": 2,
            "style": 5,
            "label": "Ban this player",
            "url": f"{settings.base_url}/admin?ban={reported}",
        }
    ]
    if serverCode:
        row.insert(
            0,
            {
                "type": 2,
                "style": 5,
                "label": "Join the server",
                "url": f"{settings.base_url}/join/{serverCode}",
            },
        )
    return [{"type": 1, "components": row}]


def _name(steam64) -> str:
    """Their stored Steam name, falling back to the id itself.

    Read off the Users row rather than asking Steam: a report is written while
    something is happening in a live server, and a blocking lookup that Steam is
    refusing would hold the whole thing up to add a name nobody needs.
    """
    from src.core.db import open as opendb

    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == int(steam64)).first()
        stored = (user.steam_data or {}) if user else {}
    return stored.get("name") or str(steam64)


def send(
    reported,
    reporter,
    reason: str,
    stats: dict,
    serverCode: str | None = None,
    where: str = "a PeekSeek server",
    screenshot: bytes | None = None,
    channel: str | None = None,
    staffRole: str | None = None,
) -> bool:
    """Post one report to the staff channel. Returns whether Discord took it.

    The screenshot rides along as a real attachment rather than a link, because the
    thing it is evidence of is usually over by the time anyone reads the report, and an
    attachment is what survives in the channel afterwards.
    """
    channel = channel or settings.report_channel
    staffRole = staffRole or settings.staff_role
    if not settings.bot_token or not channel:
        logging.info(
            "Report from %s about %s not sent: no Discord configured",
            reporter,
            reported,
        )
        return False

    reportedName = _name(reported)
    embed = {
        "title": f"Report: {reportedName}",
        "url": f"{settings.base_url}/u/{reported}",
        "color": 0xFFA34E,
        # The reason is whatever the reporter typed, so it is quoted rather than
        # interpolated into the surrounding sentence: a report reading "> he is walling"
        # cannot be mistaken for the site saying so.
        "description": f"> {reason}",
        "fields": [
            {
                "name": "Reported",
                "value": f"`{reported}`\n{reportedName}",
                "inline": True,
            },
            {"name": "By", "value": f"`{reporter}`\n{_name(reporter)}", "inline": True},
            {"name": "Where", "value": where, "inline": True},
            {"name": "This round", "value": _statLines(stats), "inline": False},
        ],
    }
    if screenshot:
        embed["image"] = {"url": "attachment://spy.jpg"}

    payload = {
        "content": f"<@&{staffRole}>" if staffRole else None,
        "embeds": [embed],
        "components": _buttons(reported, serverCode),
        # Without this a report naming a role would ping it from inside the embed
        # description too, if a reporter typed one into their reason.
        "allowed_mentions": {"roles": [staffRole] if staffRole else []},
    }

    return _post(channel, payload, screenshot)


def _post(channel: str, payload: dict, screenshot: bytes | None) -> bool:
    import json

    url = f"{DISCORD_API}/channels/{channel}/messages"
    headers = {"Authorization": f"Bot {settings.bot_token}"}

    try:
        if screenshot:
            response = requests.post(
                url,
                headers=headers,
                data={"payload_json": json.dumps(payload)},
                files={"files[0]": ("spy.jpg", screenshot, "image/jpeg")},
                timeout=20,
            )
        else:
            response = requests.post(
                url,
                headers={**headers, "Content-Type": "application/json"},
                json=payload,
                timeout=20,
            )
        response.raise_for_status()
        return True
    except requests.RequestException as error:
        logging.error("Failed to post report: %s", error)
        return False
