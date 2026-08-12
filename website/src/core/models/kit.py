from __future__ import annotations

from datetime import datetime
from typing import Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, JSON, text
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings

kitsConfig = settings.kits
mmconfig = settings.mmconfig


class Kit(SQLModel, table=True):
    __tablename__ = "Kits"

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False)
    )
    category: str = Field(nullable=False)
    data: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))
    created_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )
    updated_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )

    user: User = Relationship(back_populates="kits")

    @staticmethod
    def addOrUpdateData(steam64, category: str, data: dict) -> None:
        from src.core.db import open

        if isinstance(steam64, Kit):
            steam64 = steam64.steam64
        with open() as db:
            q = db.query(Kit).where(
                (Kit.steam64 == steam64) & (Kit.category == category)
            )
            if q.first():
                q.update({"data": data})
            else:
                db.add(Kit(steam64=steam64, category=category, data=data))

    @staticmethod
    def getItems(steam64, currentMatch: "Match"):  # noqa: F821
        from src.core.db import open
        from src.core import metagun

        kitCategory = (
            currentMatch.meta["userData"][str(steam64)]["kit"]
            or mmconfig["gamemodes"][currentMatch.gamemode]["kitsAllowed"][0]
        )
        powerups = mmconfig["gamemodes"][currentMatch.gamemode]["powerups"]
        defaultPowerup = powerups[0]["name"] if powerups else None
        powerupSelected = (
            currentMatch.meta["userData"][str(steam64)]["powerup"] or defaultPowerup
        )

        with open() as db:
            if isinstance(steam64, (Kit,)):
                steam64 = steam64.steam64

            weaponKit = (
                db.query(Kit)
                .where((Kit.steam64 == steam64) & (Kit.category == kitCategory))
                .first()
            )
            outfitKit = (
                db.query(Kit)
                .where((Kit.steam64 == steam64) & (Kit.category == "outfit"))
                .first()
            )

            if not weaponKit:
                gunId = list(kitsConfig["kits"][kitCategory])[0]
                weaponKit = {
                    n: v[0] for n, v in kitsConfig["kits"][kitCategory][gunId].items()
                }
            else:
                weaponKit = weaponKit.data
                gunId = weaponKit["gun"]
                weaponKit.pop("gun")

            if not outfitKit:
                outfitKit = {x: y[0] for x, y in kitsConfig["outfits"].items()}
            else:
                outfitKit = outfitKit.data

            if str(gunId) in mmconfig["defaultMagAmountFiremodePerGunId"]:
                items = [
                    [gunId, 1, metagun.metaObject(gunId).setAttach(**weaponKit)()],
                    *[[item_id, 1, ""] for _, item_id in outfitKit.items()],
                    *mmconfig["gamemodes"][currentMatch.gamemode]["defaultItems"],
                    [
                        mmconfig["defaultMagAmountFiremodePerGunId"][str(gunId)][0],
                        5,
                        "",
                    ],
                ]
            else:
                items = [
                    [gunId, 1, ""],
                    *[[item_id, 1, ""] for _, item_id in outfitKit.items()],
                    *mmconfig["gamemodes"][currentMatch.gamemode]["defaultItems"],
                ]

            if powerupSelected:
                for p in mmconfig["gamemodes"][currentMatch.gamemode]["powerups"]:
                    if p["name"] != powerupSelected:
                        continue
                    for item in p["items"]:
                        items.append(item)

            return items

    @staticmethod
    def publicData(steam64) -> dict:
        """Every kit category, fully resolved into a ready-to-equip item list — same
        [id, amount, state] shape and the same gun/outfit/bonus-mag logic
        Kit.getItems builds for a live match (minus the gamemode default items and
        powerup a match would add), just without a match to read the selection
        from. Lets a caller with no match context (the lobby plugin, equipping a
        player before they've even queued) hand them their real loadout."""
        from src.core.db import open
        from src.core import metagun

        with open() as db:
            saved = {
                kit.category: kit.data
                for kit in db.query(Kit).where(Kit.steam64 == steam64).all()
            }

        outfitSaved = saved.get("outfit") or {}
        outfitItems = [
            [
                outfitSaved[slot] if outfitSaved.get(slot) in options else options[0],
                1,
                "",
            ]
            for slot, options in kitsConfig["outfits"].items()
        ]

        result = {}
        for category, guns in kitsConfig["kits"].items():
            selection = saved.get(category) or {}
            gunId = str(selection.get("gun", next(iter(guns))))
            if gunId not in guns:
                gunId = next(iter(guns))

            # Not every gun in kits.json has an attachment/ammo profile in
            # defaultMagAmountFiremodePerGunId — Kit.getItems falls back to a bare
            # item (no attachment state, no bonus mags) for those, and this does
            # the same.
            if gunId not in mmconfig["defaultMagAmountFiremodePerGunId"]:
                result[category] = [[int(gunId), 1, ""], *outfitItems]
                continue

            attach = {
                slot: selection[slot] if selection.get(slot) in options else options[0]
                for slot, options in guns[gunId].items()
            }
            gunState = metagun.metaObject(gunId).setAttach(**attach)()
            magId = mmconfig["defaultMagAmountFiremodePerGunId"][gunId][0]
            result[category] = [
                [int(gunId), 1, gunState],
                *outfitItems,
                [magId, 5, ""],
            ]

        return result
