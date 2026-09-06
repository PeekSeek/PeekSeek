import requests

from .notify import DISCORD_TIMEOUT
from .settings import settings


class Webhook:
    def __init__(self, name: str = "Website Logs"):
        self.name = name
        self.url = settings.log_webhook

    def _send(
        self, title=None, description=None, color=0xFF0000, msgtype="ERROR"
    ) -> bool:
        if not self.url:
            return False

        embed = {
            "color": color,
            "footer": {
                "text": msgtype,
                "icon_url": f"{settings.base_url}/static/img/logo.png",
            },
        }
        if title:
            embed["title"] = title
        if description:
            embed["description"] = description

        try:
            r = requests.post(
                self.url,
                json={"username": self.name, "embeds": [embed]},
                timeout=DISCORD_TIMEOUT,
            )
            r.raise_for_status()
            return True
        except requests.exceptions.HTTPError:
            return False

    def error(self, title=None, description=None):
        return self._send(title, description, 0xFF0000, "ERROR")

    def info(self, title=None, description=None):
        return self._send(title, description, 0x00FF00, "INFO")
