"""Single source of truth for configuration.

Non-secret values come from config.yaml in the repository root. Each gamemode comes
from its own file in gamemodes/, beside that config. Secrets come from the
environment.

The loader turns those files into the shape the rest of the site already uses, so a
route or a model asks `settings.mmconfig` and does not care that the files changed.
"""

import json
import os
from pathlib import Path

import yaml
from dotenv import load_dotenv

load_dotenv()


# Unturned's EFiremode. The config names them so a gun does not read "2".
FIREMODES = {"safety": 0, "semi": 1, "auto": 2, "burst": 3}


def _items(raw: list[dict]) -> list[list]:
    """A gamemode file writes an item as `id`, `amount` and `state`, because a bare
    triple is unreadable. The site passes triples around, so convert here."""
    return [[item["id"], item["amount"], item.get("state", "")] for item in raw]


class Settings:
    def __init__(self, root: Path | None = None):
        self.root = root or Path(__file__).resolve().parent.parent
        self.public = self.root.parent / "public"
        # The built documentation site, served at /docs. The image has it beside
        # public/; a checkout has it where the docs project builds it. Neither is
        # present until something builds it, and the app still has to start.
        self.docs = next(
            (
                path
                for path in (
                    self.root.parent / "docs",
                    self.root.parent.parent / "docs" / "dist",
                )
                if path.is_dir()
            ),
            None,
        )

        # A checkout keeps the config two levels above src/; the image copies it one
        # level above. The bot installs the site into site-packages, where neither is
        # true, so CONFIG names the file outright. Everything else, the gamemodes and
        # the kits, is found next to whichever file wins.
        candidates = [
            Path(os.environ["CONFIG"]) if os.environ.get("CONFIG") else None,
            self.root.parent.parent / "config.yaml",
            self.root.parent / "config.yaml",
        ]
        self.config_file = next(
            (path for path in candidates if path and path.is_file()), None
        )
        if self.config_file is None:
            tried = ", ".join(str(path) for path in candidates if path)
            raise RuntimeError(
                f"No config.yaml found. Looked in: {tried}. Set CONFIG to its path."
            )
        config = yaml.safe_load(self.config_file.read_text("utf-8"))

        # One image serves every environment, so the deploy picks the hostname.
        self.domain: str = os.environ.get("DOMAIN") or config["app"]["domain"]
        self.protocol: str = os.environ.get("PROTOCOL") or config["app"]["protocol"]
        self.build: str = os.environ.get("BUILD") or config["app"]["build"]
        # Empty on every fork and every staging environment, which is the point: the
        # tag is only rendered when somebody has set their own measurement ID.
        self.analytics_id: str = os.environ.get("ANALYTICS_ID") or config["app"].get(
            "analyticsId", ""
        )

        self.permissions: list[str] = list(config["permissions"])
        self.region_names: dict[str, str] = dict(config["regions"])

        matchmaking = config["matchmaking"]
        self.queue_timeout: int = matchmaking["queueTimeout"]
        self.host_grace: int = matchmaking["hostGrace"]
        self.party_timeout: int = matchmaking["partyTimeout"]
        self.max_team_elo_gap: int = matchmaking["maxTeamEloGap"]
        self.rematch_cooldown: int = matchmaking["rematchCooldown"]
        self.match_cooldown: int = matchmaking["matchCooldown"]

        ranking = config["ranking"]
        self.win_points: int = ranking["winPoints"]
        self.loss_points: int = ranking["lossPoints"]
        self.converge_scale: int = ranking["convergeScale"]
        self.max_boost: float = ranking["maxBoost"]
        self.max_relief: float = ranking["maxRelief"]
        self.new_deviation: int = ranking["newDeviation"]
        self.seed_deviation: int = ranking["seedDeviation"]
        self.min_deviation: int = ranking["minDeviation"]
        self.deviation_drift: int = ranking["deviationDrift"]

        # Keyed by name in the file, because that reads better than a list of
        # records. The ladder walks them in order, so the name comes back in.
        self.ranks: list[dict] = [
            {"name": name} | tier for name, tier in config["ranks"].items()
        ]

        season = config["season"]
        self.season_carry_cap: int = season["carryCap"]
        self.season_carry_drop: int = season["carryDrop"]
        self.season_carry_floor: int = season["carryFloor"]
        self.season_carry_keep: int = season["carryKeep"]

        self.lobby_connect_code: str = config["lobbyConnectCode"]

        discord = config["discord"]
        self.discord_guild: str = discord["guild"]
        self.discord_client_id: str = discord["clientId"]
        self.discord_invite_url: str = discord["inviteUrl"]
        self.counter_channel: str = discord["counterChannel"]
        self.report_channel: str = discord["reportChannel"]
        self.staff_role: str = discord["staffRole"]
        self.overturn_channel: str = discord["overturnChannel"]
        self.sticky_channel: str = discord["stickyChannel"]
        self.counter_interval: int = discord["counterInterval"]
        self.wake_ping_after: int = discord["wakePingAfter"]

        self.mmconfig: dict = self._matchmaking_config(config)
        self.kits: dict = json.loads(
            (self.config_file.parent / config["kitsFile"]).read_text("utf-8")
        )

        self.db_url: str = self._required_env("DB")
        self.steam_api_key: str | None = os.environ.get("STEAM_API_KEY")
        self.bot_token: str | None = os.environ.get("BOT_TOKEN")
        self.log_webhook: str | None = os.environ.get("LOG_WEBHOOK")
        self.queue_log_channel: str | None = os.environ.get("QUEUE_LOG_CHANNEL")
        self.discord_client_secret: str | None = os.environ.get("DISCORD_CLIENT_SECRET")
        self.secret_key: str = os.environ.get("SECRET_KEY", self.domain)

        # Set only where a fresh database needs someone who can reach /admin.
        bootstrap_admin = os.environ.get("BOOTSTRAP_ADMIN_STEAM64")
        self.bootstrap_admin: int | None = (
            int(bootstrap_admin) if bootstrap_admin else None
        )

        # "address=region,address=region", game servers to allow-list on startup.
        self.bootstrap_servers: dict[str, str] = dict(
            pair.split("=", 1)
            for pair in os.environ.get("BOOTSTRAP_SERVERS", "").split(",")
            if "=" in pair
        )

    def _matchmaking_config(self, config: dict) -> dict:
        """Every gamemode file, placed at the queue bit it claims.

        A file's `queueBit` is its position in a queue mask, so the bits are a live
        contract with every mask already sent. The folder decides which gamemodes
        exist; config.yaml only decides which of them the menu shows, and in what
        order.
        """
        folder = self.config_file.parent / "gamemodes"
        shown = config["gamemodes"]

        byBit: dict[int, dict] = {}
        for path in sorted(folder.glob("*.yaml")):
            mode = yaml.safe_load(path.read_text("utf-8"))
            bit = mode.pop("queueBit")
            if bit in byBit:
                raise RuntimeError(
                    f"{path.name} claims queue bit {bit}, which "
                    f"{byBit[bit]['file']} already holds"
                )
            mode["file"] = path.stem
            mode["defaultItems"] = _items(mode["defaultItems"])
            mode["powerups"] = [
                {"name": name, "items": _items(items)}
                for name, items in (mode["powerups"] or {}).items()
            ]
            mode["hideFromMM"] = path.stem not in shown
            if not mode["hideFromMM"]:
                mode["menuOrder"] = shown.index(path.stem)
            byBit[bit] = mode

        missing = set(range(len(byBit))) - set(byBit)
        if missing:
            raise RuntimeError(f"No gamemode claims queue bit(s) {sorted(missing)}")

        unknown = [
            name for name in shown if name not in {m["file"] for m in byBit.values()}
        ]
        if unknown:
            raise RuntimeError(f"config.yaml shows gamemodes with no file: {unknown}")

        return config["match"] | {
            "gamemodes": [byBit[bit] for bit in sorted(byBit)],
            "defaultMagAmountFiremodePerGunId": {
                str(gun): [
                    spec["magazine"],
                    spec["rounds"],
                    FIREMODES[spec["fireMode"]],
                ]
                for gun, spec in config["guns"].items()
            },
        }

    @property
    def base_url(self) -> str:
        return f"{self.protocol}://{self.domain}"

    @property
    def visible_mmconfig(self) -> dict:
        """The gamemodes players can queue for, in the order config.yaml shows them.

        Each keeps its `bit`, its index in the whole list, because that is what the queue
        bitmask means. Positions never move, so the menu's order is a field of its own.
        """
        visible = [
            gamemode | {"bit": bit}
            for bit, gamemode in enumerate(self.mmconfig["gamemodes"])
            if not gamemode.get("hideFromMM")
        ]
        visible.sort(key=lambda gamemode: gamemode.get("menuOrder", gamemode["bit"]))
        return self.mmconfig | {"gamemodes": visible}

    def profile_url(self, steam64) -> str:
        return f"{self.base_url}/u/{steam64}"

    @staticmethod
    def _required_env(name: str) -> str:
        value = os.environ.get(name)
        if not value:
            raise RuntimeError(f"Required environment variable {name} is not set")
        return value


settings = Settings()
