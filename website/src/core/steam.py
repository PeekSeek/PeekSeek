from datetime import datetime, timedelta, timezone

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

# How old a stored name and avatar may be before Steam is asked again.
PROFILE_MAX_AGE = timedelta(days=1)


def _persist_steam_data(steam64: int, raw: dict) -> None:
    """Write the relevant Steam fields back to the Users table."""
    try:
        from src.core.db import session
        from src.core.models.user import User

        payload = {
            "name": raw.get("personaname"),
            "avatar": raw.get("avatarfull"),
        }
        with session() as db:
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


def _stored(steam64: int) -> tuple[dict | None, datetime | None]:
    """The last Steam profile written to the Users table, and when it was fetched."""
    from src.core.db import session
    from src.core.models.user import User

    with session() as db:
        row = (
            db.query(User.steam_data, User.steam_fetched_at)
            .where(User.steam64 == int(steam64))
            .first()
        )
    return (row[0], row[1]) if row else (None, None)


@cached(_profile_cache, key=lambda steam64: hashkey(int(steam64)))
def _profile(steam64) -> dict:
    """A player's name and avatar: the stored copy while it is under a day old, else
    Steam's, else whatever copy there is. Cached, so a page never waits on Steam for a
    player it has already named in the last five minutes."""
    stored, fetchedAt = _stored(steam64)
    if fetchedAt is not None and fetchedAt.tzinfo is None:
        fetchedAt = fetchedAt.replace(tzinfo=timezone.utc)
    if (
        stored
        and fetchedAt
        and datetime.now(timezone.utc) - fetchedAt < PROFILE_MAX_AGE
    ):
        return stored

    try:
        fresh = get_steam_user_data(steam64)
    except Exception:
        fresh = None
    if fresh:
        return {"name": fresh.get("personaname"), "avatar": fresh.get("avatarfull")}
    return stored or {}


class User:
    def __init__(self, steam64: int):
        profile = _profile(steam64)
        self.name = profile.get("name") or "Nameless"
        self.avatar = profile.get("avatar") or DEFAULT_AVATAR
        self.steam64 = steam64

    def to_dict(self) -> dict:
        return {"avatar": self.avatar, "name": self.name}
