"""Give every linked player the Discord role for the badge they currently wear.

The site does this by itself after every ranked match (notify.sync_rank_role). This is
for the times that was not running: the roles being created in the first place, the bot
being down for a stretch, or a rank rule changing under everybody at once.

Reads the ladder with plain SQL and resolves badges with the real Rank rules, but
deliberately does not import src.core.db -- importing that bootstraps schema and opens
a season against whatever database DB points at, which is not a thing a role script
should decide to do.

    BOT_TOKEN=... python scripts/sync_rank_roles.py [--apply]

Without --apply it prints what it would change and touches nothing.
"""

import os
import sys
import time
from collections import Counter

import requests
from sqlalchemy import create_engine, text

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from src.core.notify import rank_role_ids  # noqa: E402
from src.core.models.rank import Rank  # noqa: E402
from src.core.settings import settings  # noqa: E402

API = "https://discord.com/api/v10"
APPLY = "--apply" in sys.argv


def _call(method: str, url: str, headers: dict):
    """One Discord call, waiting out a rate limit rather than dropping the member.

    A backfill is the one caller that reliably hits 429: it is the same endpoint, in the
    same guild, a hundred times in a row.
    """
    for _ in range(5):
        response = requests.request(method, url, headers=headers, timeout=15)
        if response.status_code != 429:
            return response
        time.sleep(float(response.json().get("retry_after", 1)) + 0.1)
    return response


def main() -> int:
    token = os.environ.get("BOT_TOKEN") or settings.bot_token
    if not token:
        print("BOT_TOKEN is not set.")
        return 1

    headers = {"Authorization": f"Bot {token}", "Content-Type": "application/json"}
    guild = settings.discord_guild
    ladder = rank_role_ids()

    engine = create_engine(settings.db_url)
    with engine.connect() as connection:
        # Position decides the capped tier, and it is a place on the whole ladder --
        # not on the part of it that happens to have linked Discord.
        top = [
            int(row[0])
            for row in connection.execute(
                text(
                    'SELECT steam64 FROM "Stats" '
                    "ORDER BY elo DESC, wins DESC, kills DESC LIMIT 10"
                )
            )
        ]
        players = connection.execute(
            text(
                'SELECT u.steam64, u.discord, s.elo FROM "Users" u '
                'JOIN "Stats" s ON s.steam64 = u.steam64 WHERE u.discord IS NOT NULL'
            )
        ).fetchall()

    print(f"{len(players)} linked players; {'applying' if APPLY else 'dry run'}")
    tally: Counter = Counter()

    for steam64, discord, elo in players:
        position = top.index(int(steam64)) + 1 if int(steam64) in top else None
        rank = Rank.for_elo(elo, position)
        if not rank.role:
            tally["no role for tier"] += 1
            continue

        member = _call("GET", f"{API}/guilds/{guild}/members/{discord}", headers)
        if member.status_code == 404:
            tally["left the server"] += 1
            continue
        if member.status_code != 200:
            tally[f"lookup failed {member.status_code}"] += 1
            continue

        held = set(member.json().get("roles", []))
        stale = (held & ladder) - {rank.role}
        if rank.role in held and not stale:
            tally["already correct"] += 1
            continue

        if APPLY:
            base = f"{API}/guilds/{guild}/members/{discord}/roles"
            for role in stale:
                _call("DELETE", f"{base}/{role}", headers)
            if rank.role not in held:
                _call("PUT", f"{base}/{rank.role}", headers)
            time.sleep(0.25)
        tally[f"-> {rank.name}"] += 1

    for label, count in sorted(tally.items(), key=lambda kv: -kv[1]):
        print(f"  {label:<24} {count}")
    if not APPLY:
        print("\nNothing was changed. Re-run with --apply.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
