"""The PeekSeek moderation bot: /ban, /stop and /stats.

It imports the website's own `src.core` rather than calling the site over HTTP. The
concept for this proposed an admin API, on the grounds that a second implementation of
"ban" would drift from the first. Importing the module is not a second implementation:
it is the same `moderation.issueBan` the dashboard runs, so the two cannot disagree, and
it costs no new authentication surface on a public endpoint. The bot is a separate
container built from the same repo, so it ships the same code by construction.

Admin is PeekSeek's own permission bit, resolved from the caller's linked Steam account.
Discord's `administrator` only hides the commands from the picker; it is not the gate.
"""

import logging
import os
import sys
import traceback
from time import time

import discord
from discord import app_commands

sys.path.insert(0, os.environ.get("WEBSITE_SRC", "/app/website"))

from src.core import moderation  # noqa: E402
from src.core import models as data  # noqa: E402
from src.core.db import open as opendb  # noqa: E402
from src.core.settings import settings  # noqa: E402

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

RED = 0xF04444
GREY = 0x8A8A8A


class PeekSeekBot(discord.Client):
    def __init__(self):
        # No message content or member intents: every command here is a slash command
        # and reads its arguments from the interaction, so the bot never needs to see
        # what anyone types in a channel.
        super().__init__(intents=discord.Intents.default())
        self.tree = app_commands.CommandTree(self)

    async def setup_hook(self):
        await self.tree.sync()
        logging.info("Slash commands synced")


bot = PeekSeekBot()


def linkedAccount(discordId: int) -> data.User | None:
    """The PeekSeek account this Discord user has linked, if any."""
    with opendb() as db:
        user = db.query(data.User).where(data.User.discord == int(discordId)).first()
        if user is not None:
            db.expunge(user)
        return user


async def requireAdmin(interaction: discord.Interaction) -> data.User | None:
    """The caller's PeekSeek account if they are an admin, else None after replying.

    Answers rather than raising so every command has one shape: a caller who is not an
    admin is told why in a message only they can see, instead of getting Discord's
    generic interaction failure.
    """
    user = linkedAccount(interaction.user.id)
    if user is None:
        await interaction.response.send_message(
            "Your Discord is not linked to a PeekSeek account. Link it at "
            f"{settings.base_url} and try again.",
            ephemeral=True,
        )
        return None

    if not user.getPermission("admin"):
        await interaction.response.send_message(
            "That command is for admins.", ephemeral=True
        )
        return None

    return user


def resolveTarget(player: discord.User | None, steam64: str | None):
    """Who a command is about, as (steam64, error). Exactly one of the two is set.

    A Discord member is resolved through the account they linked. When they have not
    linked one the error says so and names the way round it, because "user not found"
    sends an admin looking for a bug that is not there.
    """
    if steam64:
        cleaned = steam64.strip()
        if not cleaned.isdigit():
            return None, f"`{steam64}` is not a steam64."
        return int(cleaned), None

    if player is None:
        return None, "Give me a player or a steam64."

    user = linkedAccount(player.id)
    if user is None:
        return None, (
            f"{player.mention} has not linked a PeekSeek account, so I cannot tell "
            "who they are in game. Pass their steam64 instead."
        )
    return user.steam64, None


def displayName(steam64) -> str:
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == int(steam64)).first()
        stored = (user.steam_data or {}) if user else {}
    return stored.get("name") or str(steam64)


class ConfirmBan(discord.ui.View):
    """The confirmation a cheating ban goes through, and nothing else does.

    A cheating ban voids matches and moves other people's Elo. That should never happen
    on one keystroke, and the numbers on the button are the ones `previewBan` actually
    computed rather than an estimate.

    Sixty seconds, then the buttons stop working: a live Ban button sitting in old
    scrollback is its own hazard.
    """

    def __init__(self, invoker: int, steam64: int, note: str, byName: str, bySteam64):
        super().__init__(timeout=60)
        self.invoker = invoker
        self.steam64 = steam64
        self.note = note
        self.byName = byName
        self.bySteam64 = bySteam64

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if interaction.user.id != self.invoker:
            await interaction.response.send_message(
                "That confirmation is not yours.", ephemeral=True
            )
            return False
        return True

    @discord.ui.button(label="Ban and refund", style=discord.ButtonStyle.danger)
    async def confirm(self, interaction: discord.Interaction, _: discord.ui.Button):
        await interaction.response.defer(ephemeral=True)
        summary = await bot.loop.run_in_executor(
            None,
            lambda: moderation.issueBan(
                self.steam64,
                preset="cheating",
                note=self.note,
                byName=self.byName,
                bySteam64=self.bySteam64,
            ),
        )
        await interaction.followup.send(
            f"**{displayName(self.steam64)}** is permanently banned. "
            f"{summary['matches']} matches voided, {summary['players']} players "
            f"refunded {summary['eloReturned']:+d} Elo. The full list is in the mod log.",
            ephemeral=True,
        )
        self.stop()

    @discord.ui.button(label="Cancel", style=discord.ButtonStyle.secondary)
    async def cancel(self, interaction: discord.Interaction, _: discord.ui.Button):
        await interaction.response.edit_message(
            content="Cancelled. Nothing was changed.", embed=None, view=None
        )
        self.stop()


@bot.tree.command(name="ban", description="Ban a player from PeekSeek.")
@app_commands.default_permissions(administrator=True)
@app_commands.describe(
    reason="Cheating is permanent and voids their match history.",
    player="The Discord account of the player, if they have linked one.",
    steam64="Their steam64, if they have not.",
    days="Custom bans only: how many days.",
    note="Evidence or context. Your name is recorded automatically.",
)
@app_commands.choices(
    reason=[
        app_commands.Choice(name="Cheating (permanent, voids history)", value="cheating"),
        app_commands.Choice(name="Matchfixing (1 month)", value="matchfixing"),
        app_commands.Choice(name="Toxicity (1 week)", value="toxicity"),
        app_commands.Choice(name="Custom", value="custom"),
    ]
)
async def ban(
    interaction: discord.Interaction,
    reason: app_commands.Choice[str],
    player: discord.User | None = None,
    steam64: str | None = None,
    days: int | None = None,
    note: str | None = None,
):
    """`reason` is required rather than optional, which is the one deviation from the
    original spec. Discord cannot make one option's requirement depend on another's
    value, so a fully optional set of five fields has a lot of ways to be wrong. A
    required choice deletes the most likely of them before it can happen."""
    admin = await requireAdmin(interaction)
    if admin is None:
        return

    target, error = resolveTarget(player, steam64)
    if error:
        await interaction.response.send_message(error, ephemeral=True)
        return

    byName = displayName(admin.steam64)
    preset = reason.value

    if preset == "custom":
        if not days or days < 1:
            await interaction.response.send_message(
                "A custom ban needs `days`.", ephemeral=True
            )
            return
        if not note:
            await interaction.response.send_message(
                "A custom ban needs a `note` saying what it is for, since there is no "
                "preset reason to stand in for one.",
                ephemeral=True,
            )
            return

        summary = moderation.issueBan(
            target,
            days=days,
            reason=note[:100],
            note=note,
            byName=byName,
            bySteam64=admin.steam64,
        )
        await interaction.response.send_message(
            f"**{displayName(target)}** is banned for {days} days.", ephemeral=True
        )
        logging.info("Custom ban on %s by %s: %s", target, byName, summary)
        return

    if preset != "cheating":
        moderation.issueBan(
            target,
            preset=preset,
            note=note,
            byName=byName,
            bySteam64=admin.steam64,
        )
        length = moderation.PRESETS[preset]["summary"]
        await interaction.response.send_message(
            f"**{displayName(target)}** is banned for {length.lower()} "
            f"({moderation.PRESETS[preset]['label']}).",
            ephemeral=True,
        )
        return

    # Cheating: show what it would do before doing any of it.
    await interaction.response.defer(ephemeral=True)
    preview = await bot.loop.run_in_executor(None, moderation.previewBan, target)

    lines = "\n".join(
        f"`{r['elo']:+d}` <{settings.base_url}/u/{r['steam64']}>"
        for r in preview["refunds"][:10]
    )
    if preview["players"] > 10:
        lines += f"\n… and {preview['players'] - 10} more"

    stats = preview["stats"]
    embed = discord.Embed(
        title=f"Confirm permanent ban: {displayName(target)}",
        url=f"{settings.base_url}/u/{target}",
        colour=RED,
        description=lines or "No matches to refund.",
    )
    embed.add_field(name="Steam64", value=f"`{target}`", inline=True)
    embed.add_field(
        name="Their record",
        value=(
            f"{stats['elo']} Elo, {stats['kills']}K/{stats['deaths']}D"
            if stats
            else "No stats row"
        ),
        inline=True,
    )
    embed.add_field(
        name="Would void",
        value=f"{preview['matches']} matches, refunding {preview['players']} players "
        f"{preview['eloReturned']:+d} Elo",
        inline=False,
    )

    await interaction.followup.send(
        embed=embed,
        view=ConfirmBan(interaction.user.id, target, note, byName, admin.steam64),
        ephemeral=True,
    )


@bot.tree.command(name="stop", description="Force a match to end.")
@app_commands.default_permissions(administrator=True)
@app_commands.describe(matchid="The match id, as shown on the admin page.")
async def stop(interaction: discord.Interaction, matchid: int):
    """Nobody's Elo moves and the match's players are freed, exactly as the dashboard's
    Force stop does."""
    if await requireAdmin(interaction) is None:
        return

    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == matchid).first()
        if match is None:
            await interaction.response.send_message(
                f"No match {matchid}.", ephemeral=True
            )
            return
        if match.finished:
            await interaction.response.send_message(
                f"Match {matchid} was already over.", ephemeral=True
            )
            return
        db.query(data.Match).where(data.Match.id == matchid).update({"finished": True})

    data.MatchResult.recordUnscored(matchid, "abandoned")
    await interaction.response.send_message(
        f"Match {matchid} stopped. Nobody's Elo moved and its players are free.",
        ephemeral=True,
    )


@bot.tree.command(name="stats", description="Look up a player's record and ban history.")
@app_commands.default_permissions(administrator=True)
@app_commands.describe(
    player="The Discord account of the player.", steam64="Or their steam64."
)
async def stats(
    interaction: discord.Interaction,
    player: discord.User | None = None,
    steam64: str | None = None,
):
    admin = await requireAdmin(interaction)
    if admin is None:
        return

    # Defaults to the caller, so an admin checking their own record does not have to
    # type their own steam64 at themselves.
    if player is None and steam64 is None:
        target = admin.steam64
    else:
        target, error = resolveTarget(player, steam64)
        if error:
            await interaction.response.send_message(error, ephemeral=True)
            return

    with opendb() as db:
        record = db.query(data.Stat).where(data.Stat.steam64 == int(target)).first()

    ban = data.UserBan.activeFor(target)
    history = data.UserBan.historyFor(target)

    embed = discord.Embed(
        title=displayName(target),
        url=f"{settings.base_url}/u/{target}",
        colour=RED if ban else GREY,
    )
    embed.add_field(name="Steam64", value=f"`{target}`", inline=True)

    if record:
        embed.add_field(name="Elo", value=str(record.elo), inline=True)
        embed.add_field(
            name="Record",
            value=f"{record.wins}W {record.losses}L · {record.kills}K {record.deaths}D",
            inline=True,
        )
        embed.add_field(
            name="Headshots", value=f"{record.headshotPercent}%", inline=True
        )
    else:
        embed.add_field(name="Elo", value="Never played a ranked match", inline=True)

    if ban:
        embed.add_field(
            name="Banned",
            value=f"{ban.reason} · {ban.expiryLabel}\n{ban.moderatorNote or ''}"[:1024],
            inline=False,
        )

    past = [b for b in history if b is not ban]
    if past:
        embed.add_field(
            name=f"{len(past)} previous ban{'' if len(past) == 1 else 's'}",
            value="\n".join(
                f"{b.reason} · {b.expiryLabel} · {b.moderatorNote or ''}" for b in past[:5]
            )[:1024],
            inline=False,
        )

    await interaction.response.send_message(embed=embed, ephemeral=True)


class PickBanToOverturn(discord.ui.View):
    """Every ban this player has, as a menu, because "unban them" is ambiguous the
    moment somebody has been banned more than once.

    Overturning does not delete the row. It sets the expiry to zero and writes who
    lifted it into the note, so a repeat offender still reads as one and the fact that
    somebody overturned a ban is itself part of the record.
    """

    def __init__(self, invoker: int, bans: list, byName: str, bySteam64):
        super().__init__(timeout=120)
        self.invoker = invoker
        self.byName = byName
        self.bySteam64 = bySteam64

        options = []
        for ban in bans[:25]:
            issued = ban.created_at.strftime("%d %b %Y") if ban.created_at else "unknown"
            state = "in force" if ban.endTime > int(time()) else "already over"
            note = (ban.moderatorNote or "no note")[:90]
            options.append(
                discord.SelectOption(
                    label=f"{ban.reason} - {issued} ({state})"[:100],
                    description=note,
                    value=str(ban.id),
                )
            )

        self.menu = discord.ui.Select(placeholder="Which ban?", options=options)
        self.menu.callback = self.chosen
        self.add_item(self.menu)

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if interaction.user.id != self.invoker:
            await interaction.response.send_message("That menu is not yours.", ephemeral=True)
            return False
        return True

    async def chosen(self, interaction: discord.Interaction):
        await interaction.response.defer(ephemeral=True)
        banId = int(self.menu.values[0])

        lifted = await bot.loop.run_in_executor(
            None,
            lambda: moderation.overturnBan(banId, self.byName, self.bySteam64),
        )
        if lifted is None:
            await interaction.followup.send("That ban no longer exists.", ephemeral=True)
            return

        await interaction.followup.send(
            f"Overturned the **{lifted['reason']}** ban on "
            f"**{displayName(lifted['steam64'])}**. The record is kept and now says you "
            "lifted it.",
            ephemeral=True,
        )
        self.stop()



@bot.tree.command(name="unban", description="Overturn a ban.")
@app_commands.default_permissions(administrator=True)
@app_commands.describe(
    player="The Discord account of the player.", steam64="Or their steam64."
)
async def unban(
    interaction: discord.Interaction,
    player: discord.User | None = None,
    steam64: str | None = None,
):
    admin = await requireAdmin(interaction)
    if admin is None:
        return

    target, error = resolveTarget(player, steam64)
    if error:
        await interaction.response.send_message(error, ephemeral=True)
        return

    history = data.UserBan.historyFor(target)
    if not history:
        await interaction.response.send_message(
            f"**{displayName(target)}** has never been banned.", ephemeral=True
        )
        return

    active = data.UserBan.activeFor(target)
    embed = discord.Embed(
        title=f"Bans on {displayName(target)}",
        url=f"{settings.base_url}/u/{target}",
        colour=RED if active else GREY,
        description=(
            "They are not currently banned; these are all expired or already overturned."
            if not active
            else f"Currently banned: **{active.reason}** until {active.expiryLabel}."
        ),
    )

    await interaction.response.send_message(
        embed=embed,
        view=PickBanToOverturn(
            interaction.user.id, history, displayName(admin.steam64), admin.steam64
        ),
        ephemeral=True,
    )

@bot.tree.error
async def onCommandError(interaction: discord.Interaction, error: Exception):
    """A command that raises must still say something.

    Without this an unhandled error leaves the admin looking at Discord's "the
    application did not respond", which is indistinguishable from the bot being down.
    """
    logging.error("Command failed: %s", "".join(traceback.format_exception(error)))
    message = "That failed. It is logged; nothing was changed unless the reply said so."
    if interaction.response.is_done():
        await interaction.followup.send(message, ephemeral=True)
    else:
        await interaction.response.send_message(message, ephemeral=True)


@bot.event
async def on_ready():
    logging.info("Logged in as %s", bot.user)


if __name__ == "__main__":
    token = settings.bot_token
    if not token:
        raise SystemExit("BOT_TOKEN is not set.")
    bot.run(token)
