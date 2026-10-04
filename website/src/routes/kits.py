"""Kit & outfit editors. Pure HTMX: every click POSTs the selection, the server
validates and saves it, then returns the re-rendered editor to swap in. No client
state, no bespoke JS — the saved kit in the database is the single source of truth.
"""

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import RedirectResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.dependencies import LoggedInSteam64, RequestValues, templates

# The editors are HTMX fragments rather than JSON, so they stay out of the schema; the
# tag is there for the one browser endpoint that is not.
router = APIRouter(prefix="/me", tags=["kits"], include_in_schema=False)

kitsConfig = settings.kits

WEAPON_SLOTS = ("sight", "tactical", "grip", "barrel")


def _saved_kit(steam64, category):
    with opendb() as db:
        kit = (
            db.query(data.Kit)
            .where((data.Kit.steam64 == steam64) & (data.Kit.category == category))
            .first()
        )
        return dict(kit.data) if kit else None


def _weapon_selection(steam64, category, submitted=None):
    """Resolve a valid weapon selection from a submission or the saved kit,
    falling back to the first allowed option for every slot."""
    guns = kitsConfig["kits"][category]
    source = submitted or _saved_kit(steam64, category) or {}

    gun = str(source.get("gun", next(iter(guns))))
    if gun not in guns:
        gun = next(iter(guns))

    selection = {"gun": int(gun)}
    for slot, options in guns[gun].items():
        value = source.get(slot)
        selection[slot] = value if value in options else options[0]
    return selection


def _adjustable_slots(category, selection):
    """The attachment slots the selected gun actually offers a choice in.

    A slot with a single allowed item is not a choice: melee kits take no
    attachments at all and snipers take no barrel, so those rows would sit there
    offering one tile that can never change anything. Leaving them out of the form
    is safe because _weapon_selection falls back to the slot's only option when the
    POST carries nothing for it.
    """
    gun = kitsConfig["kits"][category][str(selection["gun"])]
    return [(slot, gun[slot]) for slot in WEAPON_SLOTS if len(gun[slot]) > 1]


def _outfit_selection(steam64, submitted=None):
    outfits = kitsConfig["outfits"]
    source = submitted or _saved_kit(steam64, "outfit") or {}
    return {
        slot: (source[slot] if source.get(slot) in options else options[0])
        for slot, options in outfits.items()
    }


def _submitted_ints(values: dict, slots) -> dict:
    return {slot: _as_int(values.get(slot)) for slot in slots}


def _as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


@router.get("/kit/{category}")
def kit(request: Request, category: str, steam64: LoggedInSteam64):
    if category not in kitsConfig["kits"]:
        request.session["lastError"] = "Invalid kit name."
        return RedirectResponse("/", status_code=302)
    selection = _weapon_selection(steam64, category)
    return templates.TemplateResponse(
        request=request,
        name="app/kits.html",
        context={
            "category": category,
            "guns": kitsConfig["kits"][category],
            "selection": selection,
            "slots": _adjustable_slots(category, selection),
        },
    )


@router.post("/kit/{category}")
def save_kit(
    request: Request, category: str, steam64: LoggedInSteam64, values: RequestValues
):
    if category not in kitsConfig["kits"]:
        raise HTTPException(status_code=400, detail="Invalid kit name.")
    selection = _weapon_selection(
        steam64, category, _submitted_ints(values, ("gun", *WEAPON_SLOTS))
    )
    data.Kit.addOrUpdateData(steam64, category, selection)
    return templates.TemplateResponse(
        request=request,
        name="app/_kit_editor.html",
        context={
            "category": category,
            "guns": kitsConfig["kits"][category],
            "selection": selection,
            "slots": _adjustable_slots(category, selection),
        },
    )


@router.get("/outfit")
def outfit(request: Request, steam64: LoggedInSteam64):
    return templates.TemplateResponse(
        request=request,
        name="app/outfit.html",
        context={
            "outfits": kitsConfig["outfits"],
            "selection": _outfit_selection(steam64),
        },
    )


@router.post("/outfit")
def save_outfit(request: Request, steam64: LoggedInSteam64, values: RequestValues):
    selection = _outfit_selection(
        steam64, _submitted_ints(values, kitsConfig["outfits"].keys())
    )
    data.Kit.addOrUpdateData(steam64, "outfit", selection)
    return templates.TemplateResponse(
        request=request,
        name="app/_outfit_editor.html",
        context={"outfits": kitsConfig["outfits"], "selection": selection},
    )
