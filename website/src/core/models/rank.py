"""The Elo ladder's badges: which one a player wears, and how close the next one is."""

from __future__ import annotations

from src.core.settings import settings


class Rank:
    """The badge a given Elo earns, resolved against that Elo rather than standing on
    its own.

    Every question a page actually asks is about where a player sits *inside* a tier
    ("how full is the bar", "how many points to go"), and a bare tier cannot answer any
    of them, so an Elo is part of the object rather than an argument to each method.
    """

    def __init__(self, tier: dict, elo: int, above: dict | None):
        self._tier = tier
        self.name: str = tier["name"]
        self.icon: str = tier["icon"]
        self.floor: int = tier["elo"]
        self.elo = elo
        self._above = above

    @staticmethod
    def for_elo(elo: int, position: int | None = None) -> "Rank":
        """The rank this Elo earns. `position` is the player's 1-based place on the
        ladder where the caller knows it.

        A tier with `places` is a seat rather than a threshold, so it is only awarded
        inside its cap and a caller who does not know the position does not get it.
        That is deliberate for Apollo: handing it out on Elo alone would make it the
        ninth number on a list instead of the thing five people hold.
        """
        tiers = settings.ranks
        held = 0

        for index, tier in enumerate(tiers):
            if elo < tier["elo"]:
                break
            places = tier.get("places")
            if places is not None and (position is None or position > places):
                break
            held = index

        above = tiers[held + 1] if held + 1 < len(tiers) else None
        return Rank(tiers[held], elo, above)

    @property
    def role(self) -> str | None:
        """The Discord role this badge grants, if the server has one for it."""
        return self._tier.get("role")

    @property
    def next_name(self) -> str | None:
        return self._above["name"] if self._above else None

    @property
    def stands_on_position(self) -> bool:
        """Whether what is left between this player and the tier above is people rather
        than Elo.

        True on the tier under a capped one, where the bar can fill to the end and the
        badge still not change because the seats are taken, and true at the top, where
        there is nothing above to fill towards. Both are cases where a progress bar
        would draw a promotion that earning Elo cannot deliver, so the ladder position
        is shown instead.
        """
        return self._above is None or bool(self._above.get("places"))

    @property
    def next_icon(self) -> str | None:
        return self._above["icon"] if self._above else None

    @property
    def next_floor(self) -> int | None:
        """The Elo the tier above starts at.

        Drawn at the far end of the progress bar instead of the distance to it, so the
        subtraction is the reader's: a bar between 438 and 500 says how far there is to
        go without anybody having to be told.
        """
        return self._above["elo"] if self._above else None

    @property
    def to_next(self) -> int:
        """Elo still to earn before the tier above.

        Zero at the top of the ladder, and zero again for a player who already has a
        capped tier's Elo and is simply not high enough up the ladder to be given it.
        `goal` is what tells those two apart in words.
        """
        if not self._above:
            return 0
        return max(self._above["elo"] - self.elo, 0)

    @property
    def progress(self) -> float:
        """How far through this tier, 0 to 100, ready for the width of a bar."""
        return self.progress_at(self.elo)

    def progress_at(self, elo: int) -> float:
        """Where `elo` sits inside this tier, 0 to 100.

        Clamped, because the Elo a player came into a match with belongs to whichever
        tier they were in *then*: after a promotion it sits below this tier's floor and
        after a demotion above its ceiling, and both have to pin to the end of the bar
        rather than running off it.
        """
        if not self._above:
            return 100.0
        span = self._above["elo"] - self.floor
        # Immune 1 and Apollo share a floor, so the tier below the cap has no Elo range
        # of its own to be somewhere inside. Full, because by Elo there is nothing left
        # to earn -- what is missing is a seat, which no bar can draw.
        if span <= 0:
            return 100.0
        return round(min(max((elo - self.floor) / span * 100, 0.0), 100.0), 1)

    def bar(self, before: int) -> dict:
        """The two widths that draw this rank's bar for a player who arrived here from
        `before` Elo: how full it was going in, and the slice the match moved.

        Kept as one span plus a delta rather than two independent fills so a promotion
        draws as a bar filling from empty and a demotion as one giving ground back,
        which is the whole point of showing the change on the bar instead of beside it.
        """
        low, high = min(before, self.elo), max(before, self.elo)
        held = self.progress_at(low)
        return {
            "held": held,
            "changed": round(self.progress_at(high) - held, 1),
            "direction": "gained" if self.elo >= before else "lost",
        }

    @property
    def goal(self) -> str:
        """The line under a progress bar, saying what it would take to move the badge."""
        if not self._above:
            return "Top of the ladder. Nobody above you."

        places = self._above.get("places")
        if self.to_next == 0 and places:
            return (
                f"You have the Elo for {self._above['name']}. "
                f"It only seats {places}, so take a spot off somebody."
            )
        return f"{self.to_next} Elo to {self._above['name']}"
