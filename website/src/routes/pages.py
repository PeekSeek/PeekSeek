"""Static-ish pages: rulebooks, GDPR, the Discord redirect, and join links."""

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import (
    FileResponse,
    PlainTextResponse,
    RedirectResponse,
    Response,
)

from html import escape
from urllib.parse import quote

from src.core import models as data
from src.core.db import session
from src.core.settings import settings
from src.dependencies import templates

# Server-rendered pages, every one of them. Kept out of the OpenAPI schema so /api
# documents the JSON API rather than listing the site's own HTML as operations.
router = APIRouter(tags=["pages"], include_in_schema=False)

UNTURNED_APP_ID = 304930


# Crawlers and AI assistants only look for these at the root, never under /static.
@router.get("/llms.txt")
def llms_text():
    return FileResponse(settings.public / "llms.txt", media_type="text/plain")


# Only the pages a visitor can read without signing in. Everything else is behind
# Steam, so listing it would only send crawlers to the sign-in redirect.
PUBLIC_PAGES = [
    "/",
    "/about",
    "/fairplay",
    "/ucl/rulebook",
    "/ucl/live",
    "/esports/",
    "/gdpr",
    "/docs/",
]

# Enough profiles for the ladder people search for, without handing crawlers every
# account that ever played one match.
SITEMAP_PROFILES = 500


@router.get("/robots.txt")
def robots_text():
    return PlainTextResponse(
        f"User-agent: *\nAllow: /\nSitemap: {settings.base_url}/sitemap.xml\n"
    )


@router.get("/sitemap.xml")
def sitemap():
    with session() as db:
        events = db.query(data.Event.name).where(data.Event.name.is_not(None)).all()
        players = (
            db.query(data.Stat.steam64)
            .order_by(data.Stat.elo.desc())
            .limit(SITEMAP_PROFILES)
            .all()
        )
    pages = (
        PUBLIC_PAGES
        + [f"/esports/event/{quote(name)}" for (name,) in events]
        + [f"/u/{steam64}" for (steam64,) in players]
    )
    urls = "".join(
        f"<url><loc>{escape(settings.base_url + page)}</loc></url>" for page in pages
    )
    return Response(
        '<?xml version="1.0" encoding="UTF-8"?>'
        f'<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{urls}</urlset>',
        media_type="application/xml",
    )


@router.get("/about")
def about(request: Request):
    return templates.TemplateResponse(request=request, name="pages/about.html")


@router.get("/discord")
def discord():
    return RedirectResponse(settings.discord_invite_url, status_code=302)


@router.get("/lobby")
def lobby(request: Request):
    """Open the DeathMatch lobby in Unturned.

    A page rather than a redirect to `steam://`, because a browser may refuse a
    protocol handler it did not see the visitor start, and the page keeps a button and
    the bare code for that case.
    """
    return _join_page(
        request,
        settings.lobby_connect_code,
        heading="Opening the lobby",
        embedTitle="Join the PeekSeek lobby",
        embedDescription=(
            "One click into the DeathMatch arena. Queue for a ranked match from inside it."
        ),
    )


def connect_url(serverCode: str) -> str | None:
    """The steam:// launch for a server code, or None if that is not a code.

    Shared with the esports event page's ?join=, so both spell the launch the same way
    and agree on which codes are worth handing to Steam.
    """
    if not serverCode.isalnum() or len(serverCode) > 32:
        return None
    return f"steam://run/{UNTURNED_APP_ID}//+connect {serverCode}"


def _join_page(
    request: Request,
    serverCode: str,
    heading: str,
    embedTitle: str,
    embedDescription: str,
):
    return templates.TemplateResponse(
        request=request,
        name="pages/lobby.html",
        context={
            "connect_code": serverCode,
            "connect_url": connect_url(serverCode),
            "heading": heading,
            "embedTitle": embedTitle,
            "embedDescription": embedDescription,
        },
    )


@router.get("/join/{serverCode}")
def join(request: Request, serverCode: str):
    """Open any server by its code. The code comes from the URL and is reflected into
    the page's script, so only the shape Unturned accepts gets through."""
    if connect_url(serverCode) is None:
        raise HTTPException(status_code=404, detail="Not a server code.")

    return _join_page(
        request,
        serverCode,
        heading="Opening the server",
        embedTitle=f"Join PeekSeek server {serverCode}",
        embedDescription="One click into the match.",
    )


@router.get("/ucl/rulebook")
def rulebook(request: Request):
    return templates.TemplateResponse(
        request=request, name="pages/competitionRulebook.html"
    )


@router.get("/fairplay")
def fairplay(request: Request):
    return templates.TemplateResponse(request=request, name="pages/fairPlay.html")


# One page, one URL. The spellings that were once served beside the canonical ones stay
# reachable as permanent redirects, so old links and bookmarks keep working without the
# site answering the same content at two addresses.
@router.get("/fair-play")
def fairplay_alias():
    return RedirectResponse("/fairplay", status_code=308)


@router.get("/gdpr")
def gdpr(request: Request):
    return templates.TemplateResponse(request=request, name="pages/gdpr.html")


@router.get("/ucl/live")
def ucl_live(request: Request):
    return templates.TemplateResponse(request=request, name="pages/uclLive.html")


@router.get("/ucl")
def ucl_alias():
    return RedirectResponse("/ucl/live", status_code=308)
