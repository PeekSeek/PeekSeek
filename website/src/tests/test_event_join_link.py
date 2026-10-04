"""The ?join= on an event link.

SDG will not run an Unturned in-game banner that points at a steam:// URL or at a page
that joins a server by itself, so the event brief has to load as a brief and the launch
has to be something the player presses. These tests pin both halves of that: the page
never navigates on arrival, and the button is there when the link asks for one.
"""

from time import time

from sqlmodel import Session

from src.core import models as data
from src.core.db import engine


def event(owner, *, name="fhraid2v2eu", region="paris"):
    """An event with a live queue window, which is what the banner links to."""
    start = int(time())
    with Session(engine) as session:
        row = data.Event(
            ownerSteam64=owner.steam64,
            name=name,
            timeCreated=start,
            gamemode=0,
            data={
                "fullTitle": "FangHosting.com RAID 2v2 EU",
                "title": "RAID 2v2 EU",
                "descriptionLong": "Win three matches to take a trophy skin.",
                "meta": {
                    "region": region,
                    "queueTimes": [[start, start + 3600]],
                    "maxPlayersPerTeam": 2,
                    "maxMatchesPerTeam": 5,
                },
            },
        )
        session.add(row)
        session.commit()
        session.refresh(row)
        return row


LOBBY = "85568392936415589"


def test_join_link_offers_the_launch_without_firing_it(client, user_factory):
    """The exact banner URL. Anonymous on purpose: this link gets handed to players who
    have never seen the site, and making them log in first would defeat the banner."""
    ev = event(user_factory())

    r = client.get(f"/esports/event/{ev.name}?join={LOBBY}")

    assert r.status_code == 200
    # The launch exists, as the href of something the player has to follow.
    assert f'href="steam://run/304930//+connect {LOBBY}"' in r.text
    # And nothing on the page sends the browser there by itself. This is the assertion
    # the banner's approval rests on, so it is spelled out rather than implied.
    assert "window.location.href" not in r.text
    # The brief is still the page: the event's own text survives the extra panel.
    assert "trophy skin" in r.text
    # The bare code too, for Connect Directly and for anyone without the handler.
    assert LOBBY in r.text


def test_event_link_without_a_join_shows_no_server_panel(client, user_factory):
    """Most event links point at no server at all. The brief has to stand alone, with no
    empty panel and no launch left over from the last event that had one."""
    ev = event(user_factory(), name="sa26raid", region="brazil")

    r = client.get(f"/esports/event/{ev.name}")

    assert r.status_code == 200
    assert "joinPanel" not in r.text
    assert "steam://run" not in r.text


def test_a_join_that_is_not_a_server_code_is_ignored(client, user_factory):
    """A mangled link still has to serve the event. The page is what people came for,
    and a 404 over a bad query string would lose them for nothing."""
    ev = event(user_factory())

    r = client.get(f"/esports/event/{ev.name}?join=not a code")

    assert r.status_code == 200
    assert "joinPanel" not in r.text
    assert "steam://run" not in r.text


def test_queue_count_appears_only_once_somebody_is_queued(client, user_factory):
    """A count of nobody argues against joining, so it is absent rather than zero."""
    owner = user_factory()
    ev = event(owner)

    quiet = client.get(f"/esports/event/{ev.name}?join={LOBBY}")
    assert "queueCount" not in quiet.text

    data.User.add_to_queue(owner.steam64, gamemode=1, region="paris", eventId=ev.id)

    busy = client.get(f"/esports/event/{ev.name}?join={LOBBY}")
    assert "queueCount" in busy.text
    assert ">1</b> player queued" in busy.text.replace("\n", "").replace("  ", "")


def test_the_open_queue_is_not_counted_as_the_event_queue(client, user_factory):
    """Somebody queueing for a ranked match is not waiting for this event, and counting
    them would put a number on the page that nobody is behind."""
    owner = user_factory()
    ev = event(owner)
    data.User.add_to_queue(owner.steam64, gamemode=1, region="paris")

    r = client.get(f"/esports/event/{ev.name}?join={LOBBY}")

    assert "queueCount" not in r.text


def test_an_old_event_link_still_reaches_the_brief(client, user_factory):
    """The bare /esports/<slug> shape is out in Discord announcements already, so it has
    to keep working, and keep its ?join=."""
    ev = event(user_factory())

    r = client.get(f"/esports/{ev.name}?join={LOBBY}", follow_redirects=False)
    assert r.status_code == 308
    assert r.headers["location"] == f"/esports/event/{ev.name}?join={LOBBY}"


def test_esports_paths_are_not_shadowed_by_the_slug_catch_all(client):
    """/esports/{event} matches anything, so it is declared last and every real path in
    the router has to stay above it. A route added below would silently disappear; this
    fails the moment one does.
    """
    from src.routes import esports

    catch_all = "/esports/{event}"
    paths = [route.path for route in esports.router.routes]
    assert paths[-1] == catch_all, f"{catch_all} must be declared last, got {paths}"
    assert paths.count(catch_all) == 1
