import pytest

from src.core import metagun
from src.core.settings import settings

mmconfig = settings.mmconfig


def test_unsupported_gun_raises():
    with pytest.raises(TypeError):
        metagun.metaObject("-1")


def test_encodes_to_csv_of_18_ints():
    gun_id = next(iter(mmconfig["defaultMagAmountFiremodePerGunId"]))
    out = metagun.metaObject(gun_id)()
    parts = out.split(",")
    assert len(parts) == 18
    for p in parts:
        int(p)  # every field must be an int


def test_set_attach_applies_values():
    gun_id = next(iter(mmconfig["defaultMagAmountFiremodePerGunId"]))
    obj = metagun.metaObject(gun_id).setAttach(sight=5, tactical=2, grip=1, barrel=3)
    assert obj.sight == 5 and obj.tactical == 2 and obj.grip == 1 and obj.barrel == 3


def test_negative_barrel_zeros_durability():
    gun_id = next(iter(mmconfig["defaultMagAmountFiremodePerGunId"]))
    obj = metagun.metaObject(gun_id).setAttach(barrel=-7)
    assert obj.barrel == 7
    assert obj.barrelDurability == 0
