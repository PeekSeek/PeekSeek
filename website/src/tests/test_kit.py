"""Kit.publicData — the fully-resolved, no-match-context loadout export."""

from src.core import models as data
from src.core.settings import settings

kitsConfig = settings.kits
mmconfig = settings.mmconfig


def test_public_data_covers_every_kit_category(user_factory):
    player = user_factory()
    result = data.Kit.publicData(player.steam64)
    assert set(result) == set(kitsConfig["kits"])


def test_public_data_falls_back_to_the_first_gun_when_nothing_is_saved(user_factory):
    player = user_factory()
    result = data.Kit.publicData(player.steam64)

    category = next(iter(kitsConfig["kits"]))
    firstGun = int(next(iter(kitsConfig["kits"][category])))
    gunEntry = result[category]["items"][0]
    assert gunEntry[0] == firstGun
    assert gunEntry[1] == 1


def test_public_data_reflects_a_saved_selection(user_factory):
    player = user_factory()
    category = next(iter(kitsConfig["kits"]))
    guns = kitsConfig["kits"][category]
    gunId = next(iter(guns))
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": int(gunId)})

    result = data.Kit.publicData(player.steam64)
    assert result[category]["items"][0][0] == int(gunId)


def test_public_data_includes_every_outfit_slot(user_factory):
    player = user_factory()
    category = next(iter(kitsConfig["kits"]))
    result = data.Kit.publicData(player.steam64)

    outfitIds = {entry[0] for entry in result[category]["items"][1:]}
    expected = {options[0] for options in kitsConfig["outfits"].values()}
    assert outfitIds == expected


def test_public_data_breaks_out_the_magazine_for_a_supported_gun(user_factory):
    player = user_factory()
    supportedGun = next(iter(mmconfig["defaultMagAmountFiremodePerGunId"]))
    category = next(
        cat for cat, guns in kitsConfig["kits"].items() if supportedGun in guns
    )
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": int(supportedGun)})

    result = data.Kit.publicData(player.steam64)
    magId, amount, state = result[category]["magazine"]
    assert magId == mmconfig["defaultMagAmountFiremodePerGunId"][supportedGun][0]
    assert amount == 5
    assert state == ""


def test_public_data_magazine_is_null_for_an_unsupported_gun(user_factory):
    player = user_factory()
    unsupportedGun = next(
        gunId
        for guns in kitsConfig["kits"].values()
        for gunId in guns
        if gunId not in mmconfig["defaultMagAmountFiremodePerGunId"]
    )
    category = next(
        cat for cat, guns in kitsConfig["kits"].items() if unsupportedGun in guns
    )
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": int(unsupportedGun)})

    result = data.Kit.publicData(player.steam64)
    assert result[category]["magazine"] is None


def test_gun_for_falls_back_to_the_first_gun_when_nothing_is_saved(user_factory):
    """The overlay names a player's gun before they have ever opened the kit editor,
    so an unsaved kit has to resolve to the same gun the game server would hand out."""
    player = user_factory()
    match = data.Match(
        gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
    )

    category = mmconfig["gamemodes"][0]["kitsAllowed"][0]
    expected = int(next(iter(kitsConfig["kits"][category])))
    assert data.Kit.gunFor(player.steam64, match) == expected


def test_gun_for_reads_the_saved_gun_for_the_picked_kit(user_factory):
    player = user_factory()
    match = data.Match(
        gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
    )
    category = mmconfig["gamemodes"][0]["kitsAllowed"][0]
    match.meta["userData"][str(player.steam64)]["kit"] = category

    guns = list(kitsConfig["kits"][category])
    chosen = int(guns[-1])
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": chosen})

    assert data.Kit.gunFor(player.steam64, match) == chosen


def test_gun_for_agrees_with_the_full_loadout_get_items_builds(user_factory):
    """gunFor exists only to skip the work getItems does. The moment the two disagree
    the overlay is naming a weapon the player will not spawn with."""
    player = user_factory()
    match = data.Match(
        gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
    )
    category = mmconfig["gamemodes"][0]["kitsAllowed"][0]
    chosen = int(list(kitsConfig["kits"][category])[-1])
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": chosen})

    items = data.Kit.getItems(player.steam64, match)
    assert data.Kit.gunFor(player.steam64, match) == items[0][0]


def test_resolve_outfit_keeps_a_pick_that_is_still_offered():
    slot, options = next(iter(kitsConfig["outfits"].items()))
    chosen = options[-1]
    assert data.Kit.resolveOutfit({slot: chosen})[slot] == chosen


def test_resolve_outfit_replaces_a_retired_item_with_the_slot_default():
    """307 and its Forest Military siblings were dropped from kits.json with 347
    saved outfits still naming them, and Kit.getItems used to hand a saved row
    straight to the server."""
    resolved = data.Kit.resolveOutfit({slot: 307 for slot in kitsConfig["outfits"]})
    assert resolved == {
        slot: options[0] for slot, options in kitsConfig["outfits"].items()
    }


def test_resolve_outfit_fills_in_a_slot_that_was_never_saved():
    assert data.Kit.resolveOutfit({}) == {
        slot: options[0] for slot, options in kitsConfig["outfits"].items()
    }
