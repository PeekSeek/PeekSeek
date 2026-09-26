from datetime import datetime, timezone

import requests
from cachetools import TTLCache, cached
from cachetools.keys import hashkey

from .settings import settings

STEAM_API = "https://api.steampowered.com"
DEFAULT_AVATAR = (
    "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg"
)

# Every page names the signed-in user, so this is how often a page waits on Steam.
_user_cache: TTLCache = TTLCache(maxsize=512, ttl=300)

STEAM_TIMEOUT = 5


def _persist_steam_data(steam64: int, raw: dict) -> None:
    """Write the relevant Steam fields back to the Users table."""
    try:
        from src.core.db import open
        from src.core.models.user import User

        payload = {
            "name": raw.get("personaname"),
            "avatar": raw.get("avatarfull"),
        }
        with open() as db:
            db.query(User).where(User.steam64 == steam64).update(
                {
                    "steam_data": payload,
                    "steam_fetched_at": datetime.now(timezone.utc),
                }
            )
    except Exception:
        pass


@cached(_user_cache, key=lambda steam64: hashkey(steam64))
def get_steam_user_data(steam64: int) -> dict | None:
    """What Steam says about a player, or None if it will not say.

    A failure returns None instead of raising so that it is cached too; otherwise a
    slow Steam costs every page render the full timeout.
    """
    try:
        r = requests.get(
            f"{STEAM_API}/ISteamUser/GetPlayerSummaries/v2",
            {"key": settings.steam_api_key, "format": "json", "steamids": [steam64]},
            timeout=STEAM_TIMEOUT,
        )
        r.raise_for_status()
        data = r.json()["response"]["players"][0]
    except Exception:
        return None

    _persist_steam_data(steam64, data)
    return data


def _get_stored_steam_data(steam64: int) -> dict | None:
    """Fall back to the last DB-persisted Steam data."""
    try:
        from src.core.db import open
        from src.core.models.user import User

        with open() as db:
            user = db.query(User).where(User.steam64 == steam64).first()
            return user.steam_data if user and user.steam_data else None
    except Exception:
        return None


class User:
    def __init__(self, steam64: int):
        try:
            d = get_steam_user_data(steam64)
        except Exception:
            d = None

        if d:
            self.name = d.get("personaname", "Nameless")
            self.avatar = d.get("avatarfull", DEFAULT_AVATAR)
        else:
            stored = _get_stored_steam_data(steam64)
            if stored:
                self.name = stored.get("name", "Nameless")
                self.avatar = stored.get("avatar", DEFAULT_AVATAR)
            else:
                self.name = "Nameless"
                self.avatar = DEFAULT_AVATAR
        self.steam64 = steam64

    def toDict(self) -> dict:
        return {"avatar": self.avatar, "name": self.name}
