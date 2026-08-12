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
    gunEntry = result[category][0]
    assert gunEntry[0] == firstGun
    assert gunEntry[1] == 1


def test_public_data_reflects_a_saved_selection(user_factory):
    player = user_factory()
    category = next(iter(kitsConfig["kits"]))
    guns = kitsConfig["kits"][category]
    gunId = next(iter(guns))
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": int(gunId)})

    result = data.Kit.publicData(player.steam64)
    assert result[category][0][0] == int(gunId)


def test_public_data_includes_every_outfit_slot(user_factory):
    player = user_factory()
    category = next(iter(kitsConfig["kits"]))
    result = data.Kit.publicData(player.steam64)

    outfitIds = {entry[0] for entry in result[category][1:-1]}
    expected = {options[0] for options in kitsConfig["outfits"].values()}
    assert outfitIds == expected


def test_public_data_appends_bonus_magazines_for_a_supported_gun(user_factory):
    player = user_factory()
    supportedGun = next(iter(mmconfig["defaultMagAmountFiremodePerGunId"]))
    category = next(
        cat for cat, guns in kitsConfig["kits"].items() if supportedGun in guns
    )
    data.Kit.addOrUpdateData(player.steam64, category, {"gun": int(supportedGun)})

    result = data.Kit.publicData(player.steam64)
    magId, amount, state = result[category][-1]
    assert magId == mmconfig["defaultMagAmountFiremodePerGunId"][supportedGun][0]
    assert amount == 5
    assert state == ""
