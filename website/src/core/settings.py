"""Single source of truth for configuration.

Non-secret values come from config.toml, secrets from the environment, and the
big JSON datasets (kits, matchmaking) from the files config.toml points at.
"""

import json
import os
import tomllib
from pathlib import Path

from dotenv import load_dotenv

load_dotenv()


class Settings:
    def __init__(self, root: Path | None = None):
        self.root = root or Path(__file__).resolve().parent.parent
        self.public = self.root.parent / "public"
        toml = tomllib.loads((self.root / "config.toml").read_text("utf-8"))

        # One image serves every environment, so the deploy picks the hostname.
        self.domain: str = os.environ.get("DOMAIN") or toml["app"]["domain"]
        self.protocol: str = os.environ.get("PROTOCOL") or toml["app"]["protocol"]
        self.build: str = os.environ.get("BUILD") or toml["app"]["build"]

        self.permissions: list[str] = list(toml["permissions"]["list"])
        self.region_names: dict[str, str] = dict(toml["regions"]["names"])

        self.queue_timeout: int = toml["matchmaking"]["queue_timeout"]
        self.host_grace: int = toml["matchmaking"]["host_grace"]
        self.party_timeout: int = toml["matchmaking"]["party_timeout"]
        self.max_team_elo_gap: int = toml["matchmaking"]["max_team_elo_gap"]
        self.rematch_cooldown: int = toml["matchmaking"]["rematch_cooldown"]

        self.lobby_connect_code: str = toml["lobby"]["connect_code"]

        self.discord_client_id: str = toml["discord"]["client_id"]
        self.discord_invite_url: str = toml["discord"]["invite_url"]
        self.counter_channel: str = toml["discord"]["counter_channel"]
        self.report_channel: str = toml["discord"]["report_channel"]
        self.staff_role: str = toml["discord"]["staff_role"]
        self.counter_interval: int = toml["discord"]["counter_interval"]
        self.wake_ping_after: int = toml["discord"]["wake_ping_after"]

        self.mmconfig: dict = self._data_file(toml, "mmconfig")
        self.kits: dict = self._data_file(toml, "kits")

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

        # "address=region,address=region" — game servers to allow-list on startup.
        self.bootstrap_servers: dict[str, str] = dict(
            pair.split("=", 1)
            for pair in os.environ.get("BOOTSTRAP_SERVERS", "").split(",")
            if "=" in pair
        )

    @property
    def base_url(self) -> str:
        return f"{self.protocol}://{self.domain}"

    @property
    def visible_mmconfig(self) -> dict:
        """mmconfig with event-only gamemodes (hideFromMM) filtered out."""
        return self.mmconfig | {
            "gamemodes": [
                gamemode
                for gamemode in self.mmconfig["gamemodes"]
                if not gamemode.get("hideFromMM")
            ]
        }

    def profile_url(self, steam64) -> str:
        return f"{self.base_url}/u/{steam64}"

    def _data_file(self, toml: dict, name: str) -> dict:
        return json.loads((self.root / toml["data_files"][name]).read_text("utf-8"))

    @staticmethod
    def _required_env(name: str) -> str:
        value = os.environ.get(name)
        if not value:
            raise RuntimeError(f"Required environment variable {name} is not set")
        return value


settings = Settings()
