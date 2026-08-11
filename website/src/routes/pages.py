"""Static-ish pages: home, rulebooks, GDPR, the Discord redirect, and overlays."""

import json

from fastapi import APIRouter, Request
from fastapi.responses import RedirectResponse

from src.core.settings import settings
from src.dependencies import templates

router = APIRouter()


@router.get("/home")
def home(request: Request):
    """New home page with announcements and community features."""
    announcements = [
        {
            "title": "Season 5 Launched",
            "date": "April 15, 2026",
            "content": "The new season is live! Updated maps, balance changes, and new competitive features.",
            "url": "/discord",
        },
        {
            "title": "UCL Spring Tournament",
            "date": "April 10, 2026",
            "content": "Registration is now open for the spring tournament. Prize pool: $5,000. Limited slots available!",
            "url": "/discord",
        },
    ]

    tournaments = [
        {
            "name": "Spring Invitational",
            "date": "March 2026",
            "winner": "Team Apex",
            "prize": "2500",
        },
        {
            "name": "Winter Qualifier",
            "date": "February 2026",
            "winner": "Phoenix Squad",
            "prize": "1500",
        },
    ]

    highlights = [
        {
            "title": "Insane 1v4 Clutch",
            "description": "Watch this incredible 1v4 clutch from the Spring Invitational finals.",
            "likes": "342",
        },
        {
            "title": "Epic Sniper Battle",
            "description": "Two snipers face off in this intense highlander match.",
            "likes": "215",
        },
        {
            "title": "Best Plays of March",
            "description": "Compilation of the most exciting moments from March matches.",
            "likes": "528",
        },
    ]

    return templates.TemplateResponse(
        request=request,
        name="pages/home.html",
        context={
            "announcements": announcements,
            "tournaments": tournaments,
            "highlights": highlights,
            "active_matches": 5,
            "online_players": 127,
            "avg_queue_time": 45,
        },
    )


@router.get("/discord")
def discord():
    return RedirectResponse(settings.discord_invite_url, status_code=302)


@router.get("/overlay")
def overlay(request: Request, id: str | None = None, createdBy: str | None = None):
    if id is None and createdBy is None:
        request.session["lastError"] = "No match ID or createdBy specified for overlay."
        return RedirectResponse("/", status_code=302)
    # json.dumps rather than the raw string: a steam64 is ~17 digits, past
    # Number.MAX_SAFE_INTEGER, so interpolating it unquoted into the page's <script>
    # turns it into a JS number literal that silently loses precision and never matches
    # the createdBy stored on the match. Quoting it keeps it a string end to end.
    return templates.TemplateResponse(
        request=request,
        name="pages/overlay.html",
        context={"matchid": json.dumps(id), "createdBy": json.dumps(createdBy)},
    )


@router.get("/ucl/rulebook")
def rulebook(request: Request):
    return templates.TemplateResponse(
        request=request, name="pages/competitionRulebook.html"
    )


@router.get("/fairplay")
@router.get("/fair-play")
def fairplay(request: Request):
    return templates.TemplateResponse(request=request, name="pages/fairPlay.html")


@router.get("/gdpr")
def gdpr(request: Request):
    return templates.TemplateResponse(request=request, name="pages/gdpr.html")


@router.get("/ucl")
@router.get("/ucl/live")
def ucl_live(request: Request):
    return templates.TemplateResponse(request=request, name="pages/uclLive.html")
