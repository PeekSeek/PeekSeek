from .settings import settings

mmconfig = settings.mmconfig

setValues = mmconfig["defaultMagAmountFiremodePerGunId"]


class metaObject:
    def __init__(self, gunid, meta: list | None = None):
        self.gun = str(gunid)

        if self.gun not in setValues:
            raise TypeError(f"Gun id {gunid} not supported.")

        self.sight = 0
        self.tactical = 0
        self.grip = 0
        self.barrel = 0
        self.ammo = setValues[self.gun][0]

        self.ammoCount = setValues[self.gun][1]
        self.firingMode = setValues[self.gun][2]
        self.tacticalMode = 1

        self.sightDurability = 100
        self.tacticalDurability = 100
        self.gripDurability = 100
        self.barrelDurability = 100
        self.ammoDurability = 100

        if meta:
            ids = [meta[i * 2] + meta[i * 2 + 1] * 256 for i in range(4)]
            self.sight, self.tactical, self.grip, self.barrel = ids
            self.barrelDurability = meta[16]

    def setAttach(
        self, sight: int = 0, tactical: int = 0, grip: int = 0, barrel: int = 0
    ):
        self.sight = sight
        self.tactical = tactical
        self.grip = grip
        self.barrel = abs(barrel)
        if barrel < 0:
            self.barrelDurability = 0
        return self

    def __call__(self):
        return ",".join(
            str(v)
            for v in [
                self.sight % 256,
                self.sight // 256,
                self.tactical % 256,
                self.tactical // 256,
                self.grip % 256,
                self.grip // 256,
                self.barrel % 256,
                self.barrel // 256,
                self.ammo % 256,
                self.ammo // 256,
                self.ammoCount,
                self.firingMode,
                self.tacticalMode,
                self.sightDurability,
                self.tacticalDurability,
                self.gripDurability,
                self.barrelDurability,
                self.ammoDurability,
            ]
        )
