"""The DM that fires when a match actually has somewhere to go.

More than a quarter of every match ever made was never played. Matchmaking worked, a
server was assigned, and nobody turned up. The existing DM goes out when the match is
created, which is minutes before there is anywhere to join.
"""

from sqlmodel import Session

from src.core import models as data
from src.core.db import engine


def liveMatch(players: list, claimed="85568392936415589") -> data.Match:
    from time import time

    with Session(engine) as session:
        row = data.Match(
            time=int(time()),
            region="paris",
            gamemode=0,
            teams=[{"name": "a", "users": [str(players[0])]},
                   {"name": "b", "users": [str(players[1])]}],
            meta={"map": "Alpha Valley", "userData": {}, "hostedAt": time()},
            claimed=claimed,
            finished=False,
        )
        session.add(row)
        session.commit()
        session.refresh(row)
        session.expunge(row)
        return row


def test_everyone_on_the_match_is_told(user_factory, monkeypatch):
    sent = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_dm",
        lambda discordId, title, message, **kw: sent.append((discordId, title, kw)),
    )

    a = user_factory(discord=111)
    b = user_factory(discord=222)
    match = liveMatch([a.steam64, b.steam64])

    sent.clear()
    match._tellPlayersToJoin()

    assert len(sent) == 2
    assert {s[0] for s in sent} == {111, 222}
    assert all(s[1] == "Your match is ready" for s in sent)


def test_the_dm_carries_a_button_that_joins_the_server(user_factory, monkeypatch):
    """A button they press beats a line of text they have to read and act on."""
    sent = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_dm",
        lambda discordId, title, message, **kw: sent.append(kw),
    )

    player = user_factory(discord=333)
    other = user_factory()
    match = liveMatch([player.steam64, other.steam64], claimed="12345")

    sent.clear()
    match._tellPlayersToJoin()

    button = sent[0]["components"][0]["components"][0]
    assert button["style"] == 5
    assert button["url"].endswith("/join/12345")


def test_nothing_is_sent_before_there_is_anywhere_to_go(user_factory, monkeypatch):
    """A match a server has not taken yet has no code, and a button to nowhere is
    worse than no message."""
    sent = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_dm",
        lambda *a, **kw: sent.append(a),
    )

    a = user_factory(discord=444)
    b = user_factory(discord=555)
    match = liveMatch([a.steam64, b.steam64], claimed="0")

    sent.clear()
    match._tellPlayersToJoin()

    assert sent == []


def test_discord_being_down_does_not_stop_the_match(user_factory, monkeypatch):
    """This runs inside the session poll that hands a server its match."""
    def explode(*a, **kw):
        raise RuntimeError("discord is down")

    a = user_factory(discord=666)
    b = user_factory(discord=777)
    match = liveMatch([a.steam64, b.steam64])

    # Patched only now, so building the match above is not what raises.
    monkeypatch.setattr("src.core.notify.send_discord_dm", explode)
    match._tellPlayersToJoin()


def test_unlinked_players_are_simply_skipped(user_factory, monkeypatch):
    sent = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_dm",
        lambda discordId, *a, **kw: sent.append(discordId),
    )

    linked = user_factory(discord=888)
    unlinked = user_factory()
    match = liveMatch([linked.steam64, unlinked.steam64])

    sent.clear()
    match._tellPlayersToJoin()

    assert sent == [888]
