import logging
from datetime import datetime, timezone

import requests

from .settings import settings

DISCORD_API = "https://discord.com/api/v10"


def send_discord_dm(
    discord_id: int,
    title: str,
    message: str,
    color: int = 0xFF0000,
    footer: str = "Automated message from PeekSeek.net",
    url: str | None = None,
    thumbnail: str | None = None,
    timestamp: datetime | None = None,
) -> None:
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

        requests.post(
            f"{DISCORD_API}/channels/{channel_id}/messages",
            headers=headers,
            json={
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
            },
        )
    except Exception as e:
        logging.error("Error sending discord notification: %s", e)
