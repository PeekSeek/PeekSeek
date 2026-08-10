from datetime import datetime, timezone

import requests
from cachetools import TTLCache, cached
from cachetools.keys import hashkey

from .settings import settings

STEAM_API = "https://api.steampowered.com"
DEFAULT_AVATAR = (
    "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg"
)

_user_cache: TTLCache = TTLCache(maxsize=512, ttl=60)
_server_cache: TTLCache = TTLCache(maxsize=128, ttl=120)


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
def get_steam_user_data(steam64: int) -> dict:
    r = requests.get(
        f"{STEAM_API}/ISteamUser/GetPlayerSummaries/v2",
        {"key": settings.steam_api_key, "format": "json", "steamids": [steam64]},
    )
    r.raise_for_status()
    data = r.json()["response"]["players"][0]
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
            self.name = d["personaname"]
            self.avatar = d["avatarfull"]
        except Exception:
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


class Server:
    def __init__(self, ip: str):
        self.addr = ip.rstrip(":").replace(":", "%3A")

    @cached(_server_cache, key=lambda self: hashkey(self.addr))
    def get(self) -> dict | None:
        url = (
            f"{STEAM_API}/IGameServersService/GetServerList/v1/"
            f"?key={settings.steam_api_key}&filter=%5Caddr%5C{self.addr}"
        )
        r = requests.get(url).json()
        try:
            return r["response"]["servers"][0]
        except Exception:
            return None

    @staticmethod
    @cached(TTLCache(maxsize=64, ttl=300), key=lambda steam_id: hashkey(steam_id))
    def from_steam_id(steam_id: str) -> dict:
        r = requests.get(
            f"{STEAM_API}/IGameServersService/GetServerList/v1/"
            f"?key={settings.steam_api_key}&filter=%5Csteamid%5C{steam_id}"
        )
        r.raise_for_status()
        return r.json()["response"]["servers"][0]
