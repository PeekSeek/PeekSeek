"""`Server.describe` answers from cache and never holds a page open on Steam."""

import time

from src.core import steam

SERVER = "90000000000000001"


def _wait_for_fill(key: str, seconds: float = 2.0) -> bool:
    """The fill runs on its own thread, so give it a moment to land."""
    deadline = time.monotonic() + seconds
    while key not in steam._describe_cache and time.monotonic() < deadline:
        time.sleep(0.01)
    return key in steam._describe_cache


def _reset() -> None:
    steam._describe_cache.clear()
    steam._describing.clear()


def test_describe_never_waits_for_steam(monkeypatch):
    """The admin list calls this once per card, one after another, so a miss that waits
    on Steam multiplies: two servers Steam would not answer for used to hold the page
    for two full timeouts. A miss has to answer now and fill in behind."""
    _reset()

    def _slow(steam_id):
        time.sleep(0.3)
        return {
            "name": "Arena",
            "map": "Alpha Valley",
            "players": 4,
            "max_players": 10,
            "addr": "1.2.3.4:27015",
        }

    monkeypatch.setattr(steam.Server, "from_steam_id", staticmethod(_slow))

    began = time.monotonic()
    assert steam.Server.describe(SERVER) is None
    assert time.monotonic() - began < 0.1

    assert _wait_for_fill(SERVER)
    assert steam.Server.describe(SERVER)["name"] == "Arena"


def test_describe_asks_once_for_a_server_steam_will_not_answer_for(monkeypatch):
    """A failure is cached like a success, so a dead server costs one request per TTL
    rather than one per card per poll."""
    _reset()
    calls = []

    def _fails(steam_id):
        calls.append(steam_id)
        raise RuntimeError("Steam is down")

    monkeypatch.setattr(steam.Server, "from_steam_id", staticmethod(_fails))

    assert steam.Server.describe(SERVER) is None
    assert _wait_for_fill(SERVER)

    for _ in range(5):
        assert steam.Server.describe(SERVER) is None
    assert len(calls) == 1


def test_describe_ignores_a_match_no_server_ever_took(monkeypatch):
    """`claimed` is "0" until a server picks the match up. That is not a lookup."""
    _reset()

    def _never(steam_id):
        raise AssertionError("asked Steam about an unclaimed match")

    monkeypatch.setattr(steam.Server, "from_steam_id", staticmethod(_never))

    assert steam.Server.describe("0") is None
    assert steam.Server.describe(None) is None
    assert not steam._describing
