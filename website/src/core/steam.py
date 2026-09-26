from datetime import datetime, timezone

import requests
from cachetools import TTLCache, cached
from cachetools.keys import hashkey

from .settings import settings

STEAM_API = "https://api.steampowered.com"
DEFAULT_AVATAR = (
    "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg"
)

# Every page names the signed-in user, so this is how often a page waits on Steam, or
# on the database when Steam will not answer.
_profile_cache: TTLCache = TTLCache(maxsize=4096, ttl=300)

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


def get_steam_user_data(steam64: int) -> dict | None:
    """What Steam says about a player, or None if it will not say."""
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


@cached(_profile_cache, key=lambda steam64: hashkey(int(steam64)))
def _profile(steam64) -> dict:
    """A player's name and avatar: Steam's, else the last copy stored, else defaults.
    Cached either way, so a Steam outage costs one lookup per player, not one per page.
    """
    try:
        fresh = get_steam_user_data(steam64)
    except Exception:
        fresh = None
    if fresh:
        return {"name": fresh.get("personaname"), "avatar": fresh.get("avatarfull")}
    return _get_stored_steam_data(steam64) or {}


class User:
    def __init__(self, steam64: int):
        profile = _profile(steam64)
        self.name = profile.get("name") or "Nameless"
        self.avatar = profile.get("avatar") or DEFAULT_AVATAR
        self.steam64 = steam64

    def toDict(self) -> dict:
        return {"avatar": self.avatar, "name": self.name}
