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


# Unturned takes the server code on its launch command line; there is no URL scheme
# that joins by code directly, which is why every join on the site goes through
# `steam://run` rather than linking the server itself.
UNTURNED_APP_ID = 304930


@router.get("/lobby")
def lobby(request: Request):
    """Open the DeathMatch lobby in Unturned.

    A link made to be posted, so it does not redirect straight to `steam://`: a browser
    that refuses to hand a protocol handler a navigation the user did not start would
    leave nothing but a blank page, and this link's whole job is being pasted somewhere
    else. The page fires the launch itself and keeps a button and the bare code for
    when that is refused, or for someone who would rather type it into Connect Directly.
    """
    return templates.TemplateResponse(
        request=request,
        name="pages/lobby.html",
        context={
            "connectCode": settings.lobby_connect_code,
            "connectUrl": (
                f"steam://run/{UNTURNED_APP_ID}//+connect {settings.lobby_connect_code}"
            ),
        },
    )


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
