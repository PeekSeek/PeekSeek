from .settings import settings

mmconfig = settings.mmconfig

# Per gun id: the magazine item, how many rounds it holds, and the firing mode a gun
# comes out of a kit set to.
DEFAULT_MAGAZINES = mmconfig["defaultMagAmountFiremodePerGunId"]


class GunMeta:
    """One gun's state string, as Unturned stores it on an item.

    Calling the instance renders the eighteen bytes in the order the game reads them,
    which is why `__call__` and not a method with a name: the object *is* that string.
    """

    def __init__(self, gun_id, meta: list | None = None):
        self.gun = str(gun_id)

        if self.gun not in DEFAULT_MAGAZINES:
            raise TypeError(f"Gun id {gun_id} not supported.")

        self.sight = 0
        self.tactical = 0
        self.grip = 0
        self.barrel = 0
        self.ammo = DEFAULT_MAGAZINES[self.gun][0]

        self.ammoCount = DEFAULT_MAGAZINES[self.gun][1]
        self.firingMode = DEFAULT_MAGAZINES[self.gun][2]
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

    def set_attach(
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
