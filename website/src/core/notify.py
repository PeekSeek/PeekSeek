import asyncio
import logging
from datetime import datetime, timezone

import requests

from .settings import settings

DISCORD_API = "https://discord.com/api/v10"


def _headers() -> dict | None:
    """Bot auth headers, or None when this environment has no token.

    Every Discord call goes through here so that "no token" means "no Discord" in
    one place. Dev deliberately runs without one, and that is the only thing
    stopping it posting into the real server's channels.
    """
    if not settings.bot_token:
        return None
    return {
        "Authorization": f"Bot {settings.bot_token}",
        "Content-Type": "application/json",
    }


def send_discord_channel(
    channel_id, content: str = None, embed: dict = None, allowed_mentions: dict = None
) -> bool:
    """Post into a channel as the bot. Returns whether it went.

    `allowed_mentions` matters whenever `content` names a role: a mention inside an
    embed renders as one but notifies nobody, and one in content is only delivered
    if it is allowed. Passing it explicitly beats relying on Discord's default.
    """
    headers = _headers()
    if not channel_id or not headers:
        return False

    body = {}
    if content:
        body["content"] = content
    if embed:
        body["embeds"] = [embed]
    if allowed_mentions is not None:
        body["allowed_mentions"] = allowed_mentions

    try:
        response = requests.post(
            f"{DISCORD_API}/channels/{channel_id}/messages", headers=headers, json=body
        )
        response.raise_for_status()
        return True
    except requests.RequestException as error:
        logging.error("Failed to post to channel %s: %s", channel_id, error)
        return False


def send_discord_dm(
    discord_id: int,
    title: str,
    message: str,
    color: int = 0xFF0000,
    footer: str = "Automated message from PeekSeek.net",
    url: str | None = None,
    thumbnail: str | None = None,
    timestamp: datetime | None = None,
    components: list | None = None,
) -> None:
    """`components` is raw Discord component JSON, for link buttons.

    A link button is a URL and needs nothing running to handle it, which is the point:
    the one moment this matters is a player being told their match is ready, and a
    button they press beats a line of text they have to read and act on.
    """
    if not discord_id:
        return

    url = url or settings.base_url
    thumbnail = thumbnail or f"{settings.base_url}/static/img/logo.png"
    timestamp = timestamp or datetime.now(timezone.utc)
    token = settings.bot_token
    if not token:
        return

    headers = {"Authorization": f"Bot {token}", "Content-Type": "application/json"}

    try:
        dm = requests.post(
            f"{DISCORD_API}/users/@me/channels",
            headers=headers,
            json={"recipient_id": discord_id},
        )
        if dm.status_code != 200:
            logging.error("Failed to open DM: %s %s", dm.status_code, dm.text)
            return

        channel_id = dm.json()["id"]

        body = {
            "embeds": [
                {
                    "title": title,
                    "url": url,
                    "description": message,
                    "color": color,
                    "thumbnail": {"url": thumbnail},
                    "footer": {"text": footer},
                    "timestamp": timestamp.isoformat(),
                }
            ]
        }
        if components:
            body["components"] = components

        requests.post(
            f"{DISCORD_API}/channels/{channel_id}/messages",
            headers=headers,
            json=body,
        )
    except Exception as e:
        logging.error("Error sending discord notification: %s", e)


def _counter_name(playing: int, queued: int) -> str:
    """What the sidebar says, in falling order of how good the news is.

    People actually playing is the strongest thing that can be said, so it wins when
    there are any. Failing that a queue is still a reason to log in: somebody is
    waiting for an opponent, and the reader could be that opponent.

    With nothing to report it says neither, rather than announcing a zero. That is a
    real line: a counter that has nothing to say can decline to say a number, but it
    never invents one. Everything above is a count of real people, so somebody who
    joins on it finds them there.
    """
    if playing > 0:
        return f"{playing} player{'' if playing == 1 else 's'} on PeekSeek.net"
    if queued > 0:
        return f"{queued} in queue"
    return "queue is open"


def refresh_player_counter() -> None:
    """Rename the counter channel to the live arena population.

    Skipped when the number has not moved, which matters more than it looks:
    Discord allows two channel renames per ten minutes and answers the third with a
    429, so spending one on "still 4" is spending the one that would have announced
    the fifth player arriving.
    """
    from src.core import models as data

    headers = _headers()
    channel = settings.counter_channel
    if not channel or not headers:
        return

    name = _counter_name(data.LobbyPopulation.playingNow(), data.UserHB.countAllFresh())
    if name == refresh_player_counter.last_name:
        return

    try:
        response = requests.patch(
            f"{DISCORD_API}/channels/{channel}", headers=headers, json={"name": name}
        )
        response.raise_for_status()
        refresh_player_counter.last_name = name
    except requests.RequestException as error:
        logging.error("Failed to rename counter channel: %s", error)


refresh_player_counter.last_name = None


async def keep_player_counter_current() -> None:
    """Runs for the life of the app, renaming the counter on a timer.

    In a thread because the rename is a blocking request and this is sharing an
    event loop with every page the site serves. Any error is swallowed and retried
    on the next pass — a Discord outage must not take the loop down with it, since
    nothing would ever start it again.
    """
    while True:
        try:
            await asyncio.to_thread(refresh_player_counter)
        except Exception as error:
            logging.error("Player counter pass failed: %s", error)
        await asyncio.sleep(settings.counter_interval)


def rank_role_ids() -> set[str]:
    """Every role the ladder hands out.

    Needed as a set because giving somebody a rank means taking the other ranks off
    them, and nothing else they have earned on the server may be touched on the way.
    """
    return {tier["role"] for tier in settings.ranks if tier.get("role")}


def sync_rank_role(steam64) -> bool:
    """Put this player in the Discord role for the badge they now wear, and take the
    other rank roles off them. Returns whether anything actually changed.

    Quiet about every reason it might not happen, because none of them are the match's
    problem: this environment has no bot token, the account has no Discord linked, the
    member has left the server, Discord is having a moment. A ranked match has to finish
    recording either way, so nothing in here raises.
    """
    from src.core.models.stat import Stat
    from src.core.models.user import User
    from src.core.db import open as opendb

    headers = _headers()
    if not headers:
        return False

    with opendb() as db:
        user = db.query(User).where(User.steam64 == int(steam64)).first()
        discordId = user.discord if user else None
    if not discordId:
        return False

    wanted = Stat.rankFor(steam64).role
    if not wanted:
        return False

    guild = settings.discord_guild
    base = f"{DISCORD_API}/guilds/{guild}/members/{discordId}"
    try:
        member = requests.get(base, headers=headers, timeout=10)
        if member.status_code == 404:
            return False
        member.raise_for_status()
        held = set(member.json().get("roles", []))

        # Only the ranks they should not have. A player whose badge has not moved since
        # their last match costs nothing here beyond the lookup above.
        stale = (held & rank_role_ids()) - {wanted}
        changed = False
        for role in stale:
            requests.delete(f"{base}/roles/{role}", headers=headers, timeout=10)
            changed = True
        if wanted not in held:
            requests.put(f"{base}/roles/{wanted}", headers=headers, timeout=10)
            changed = True
        return changed
    except requests.RequestException as error:
        logging.error("Failed to sync rank role for %s: %s", steam64, error)
        return False
