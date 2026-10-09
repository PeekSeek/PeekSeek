from __future__ import annotations

from datetime import datetime
from typing import Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, Index, JSON, text
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings

kitsConfig = settings.kits
mmconfig = settings.mmconfig


class Kit(SQLModel, table=True):
    __tablename__ = "Kits"
    __table_args__ = (Index("ix_Kits_steam64_category", "steam64", "category"),)

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
    def add_or_update_data(steam64, category: str, data: dict) -> None:
        from src.core.db import session

        if isinstance(steam64, Kit):
            steam64 = steam64.steam64
        with session() as db:
            q = db.query(Kit).where(
                (Kit.steam64 == steam64) & (Kit.category == category)
            )
            if q.first():
                q.update({"data": data})
            else:
                db.add(Kit(steam64=steam64, category=category, data=data))

    @staticmethod
    def kit_category(steam64, currentMatch: "Match") -> str:  # noqa: F821
        """The kit a player is going into this match with: their pick, or the
        gamemode's first allowed kit while that pick is still unmade."""
        return (
            currentMatch.meta["userData"][str(steam64)]["kit"]
            or mmconfig["gamemodes"][currentMatch.gamemode]["kitsAllowed"][0]
        )

    @staticmethod
    def resolve_outfit(saved: dict) -> dict:
        """The outfit a player actually wears: their pick per slot, or that slot's
        first option wherever they have no pick or the one they saved is no longer
        offered. Retiring an item from kits.json is enough to move everyone off it,
        so a saved row can never hand out a piece the site has stopped listing."""
        return {
            slot: saved[slot] if saved.get(slot) in options else options[0]
            for slot, options in kitsConfig["outfits"].items()
        }

    @staticmethod
    def items_for(steam64, currentMatch: "Match"):  # noqa: F821
        from src.core.db import session
        from src.core import metagun

        kit_category = Kit.kit_category(steam64, currentMatch)
        powerups = mmconfig["gamemodes"][currentMatch.gamemode]["powerups"]
        defaultPowerup = powerups[0]["name"] if powerups else None
        powerupSelected = (
            currentMatch.meta["userData"][str(steam64)]["powerup"] or defaultPowerup
        )

        with session() as db:
            if isinstance(steam64, (Kit,)):
                steam64 = steam64.steam64

            weaponKit = (
                db.query(Kit)
                .where((Kit.steam64 == steam64) & (Kit.category == kit_category))
                .first()
            )
            outfitKit = (
                db.query(Kit)
                .where((Kit.steam64 == steam64) & (Kit.category == "outfit"))
                .first()
            )

            if not weaponKit:
                gunId = list(kitsConfig["kits"][kit_category])[0]
                weaponKit = {
                    n: v[0] for n, v in kitsConfig["kits"][kit_category][gunId].items()
                }
            else:
                weaponKit = weaponKit.data
                gunId = weaponKit["gun"]
                weaponKit.pop("gun")

            outfitKit = Kit.resolve_outfit(outfitKit.data if outfitKit else {})

            if str(gunId) in mmconfig["defaultMagAmountFiremodePerGunId"]:
                items = [
                    [gunId, 1, metagun.GunMeta(gunId).set_attach(**weaponKit)()],
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
    def public_data(steam64) -> dict:
        """Every kit category, fully resolved, same gun/outfit/bonus-mag logic
        Kit.items_for builds for a live match (minus the gamemode default items and
        powerup a match would add), just without a match to read the selection
        from. Lets a caller with no match context (the lobby plugin, equipping a
        player before they've even queued) hand them their real loadout.

        Each category is {"items": [[id, amount, state], ...], "magazine": [id,
        amount, state] | null}, the magazine is broken out on its own rather than
        appended to items so a caller can keep restocking it (a spare mag that's
        always full) without having to guess which item in the list is the gun,
        which are outfit pieces, and which is the magazine."""
        from src.core.db import session
        from src.core import metagun

        with session() as db:
            saved = {
                kit.category: kit.data
                for kit in db.query(Kit).where(Kit.steam64 == steam64).all()
            }

        outfitItems = [
            [item_id, 1, ""]
            for item_id in Kit.resolve_outfit(saved.get("outfit") or {}).values()
        ]

        result = {}
        for category, guns in kitsConfig["kits"].items():
            selection = saved.get(category) or {}
            gunId = str(selection.get("gun", next(iter(guns))))
            if gunId not in guns:
                gunId = next(iter(guns))

            # Not every gun in kits.json has an attachment/ammo profile in
            # defaultMagAmountFiremodePerGunId, Kit.items_for falls back to a bare
            # item (no attachment state, no bonus mags) for those, and this does
            # the same.
            if gunId not in mmconfig["defaultMagAmountFiremodePerGunId"]:
                result[category] = {
                    "items": [[int(gunId), 1, ""], *outfitItems],
                    "magazine": None,
                }
                continue

            attach = {
                slot: selection[slot] if selection.get(slot) in options else options[0]
                for slot, options in guns[gunId].items()
            }
            gunState = metagun.GunMeta(gunId).set_attach(**attach)()
            magId = mmconfig["defaultMagAmountFiremodePerGunId"][gunId][0]
            result[category] = {
                "items": [[int(gunId), 1, gunState], *outfitItems],
                "magazine": [magId, 5, ""],
            }

        return result
