import threading
from datetime import datetime, timezone

import requests
from cachetools import TTLCache, cached
from cachetools.keys import hashkey

from .settings import settings

STEAM_API = "https://api.steampowered.com"
DEFAULT_AVATAR = (
    "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg"
)

# Every page render asks Steam about the signed-in user, so this window is how often a
# blocking third-party call lands on the critical path of an ordinary page. A persona
# name or avatar changing five minutes late costs nobody anything.
_user_cache: TTLCache = TTLCache(maxsize=512, ttl=300)
_server_cache: TTLCache = TTLCache(maxsize=128, ttl=120)
_describe_cache: TTLCache = TTLCache(maxsize=64, ttl=60)

# Servers currently being looked up, so a page that names the same one on ten cards
# starts one thread rather than ten.
# ponytail: the check-then-add is not atomic, so a genuine race costs one duplicate
# request to Steam. A lock here would buy nothing worth having.
_describing: set[str] = set()

# Steam is a third party on the request path of pages that have to render without it.
# Every call here is decoration, so waiting on one is never worth holding a page open.
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

    None rather than a raised exception so that a *failure* is cached too, for the same
    reason Server.describe caches one. A raise is never stored, so while Steam was slow
    every single page render paid the full timeout again: the signed-in user is looked up
    on every render, and with the site's threadpool 40 threads wide that is how a slow
    Steam turned into a slow PeekSeek instead of a page missing its avatars.
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


class Server:
    def __init__(self, ip: str):
        self.addr = ip.rstrip(":").replace(":", "%3A")

    @cached(_server_cache, key=lambda self: hashkey(self.addr))
    def get(self) -> dict | None:
        url = (
            f"{STEAM_API}/IGameServersService/GetServerList/v1/"
            f"?key={settings.steam_api_key}&filter=%5Caddr%5C{self.addr}"
        )
        try:
            r = requests.get(url, timeout=STEAM_TIMEOUT).json()
            return r["response"]["servers"][0]
        except Exception:
            return None

    @staticmethod
    @cached(TTLCache(maxsize=64, ttl=300), key=lambda steam_id: hashkey(steam_id))
    def from_steam_id(steam_id: str) -> dict:
        r = requests.get(
            f"{STEAM_API}/IGameServersService/GetServerList/v1/"
            f"?key={settings.steam_api_key}&filter=%5Csteamid%5C{steam_id}",
            timeout=STEAM_TIMEOUT,
        )
        r.raise_for_status()
        return r.json()["response"]["servers"][0]

    @staticmethod
    def describe(steam_id) -> dict | None:
        """What Steam has already said about the server holding a match, or None.

        A match's `claimed` is the GSLT's own Steam ID, which is what Steam's master
        list is keyed by, so the connect code the site already stores is enough to get
        the server's advertised name, map and player count with no extra bookkeeping.

        None rather than a raised exception because this is decoration on a page that
        has to render regardless: an offline server is missing from the master list
        entirely, and that is the moment an admin most wants the rest of the card.

        **This never calls Steam.** It answers from `_describe_cache` and starts a
        thread to fill it on a miss. Caching the failure too was not enough on its own:
        the admin list asks about every match on it, one after another, so each time the
        sixty-second TTL lapsed the next poll paid one blocking request per *distinct*
        server. Two servers Steam would not answer for, at STEAM_TIMEOUT each, held the
        whole page for ten seconds a minute. The page redraws whole every fifteen
        seconds, so a miss simply shows up on the following pass instead.
        """
        if not steam_id or str(steam_id) == "0":
            return None

        key = str(steam_id)
        if key in _describe_cache:
            return _describe_cache[key]

        if key not in _describing:
            _describing.add(key)
            threading.Thread(
                target=Server._fetch_description, args=(key,), daemon=True
            ).start()
        return None

    @staticmethod
    def _fetch_description(key: str) -> None:
        """Fill `_describe_cache` for one server, off any request path.

        A failure is stored exactly like a success, so a server Steam will not talk
        about is asked about once per TTL rather than once per card per poll.
        """
        try:
            raw = Server.from_steam_id(key)
            described = {
                "name": raw.get("name"),
                "map": raw.get("map"),
                "players": raw.get("players"),
                "maxPlayers": raw.get("max_players"),
                "address": raw.get("addr"),
            }
        except Exception:
            described = None

        _describe_cache[key] = described
        _describing.discard(key)
