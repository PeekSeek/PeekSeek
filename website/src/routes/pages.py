"""Static-ish pages: rulebooks, GDPR, the Discord redirect, and overlays."""

import json

from fastapi import APIRouter, Request
from fastapi.responses import RedirectResponse

from src.core.settings import settings
from src.dependencies import templates

router = APIRouter()


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
