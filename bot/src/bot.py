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

import asyncio
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


def cheatingPreviewEmbed(target, preview: dict) -> discord.Embed:
    """What a cheating ban would cost everyone else, before it costs them anything."""
    lines = "\n".join(
        f"`{r['elo']:+d}` <{settings.base_url}/u/{r['steam64']}>"
        for r in preview["refunds"][:10]
    )
    if preview["players"] > 10:
        lines += f"\n... and {preview['players'] - 10} more"

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
    return embed


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
        summary = await asyncio.to_thread(
            moderation.issueBan,
            self.steam64,
            preset="cheating",
            note=self.note,
            byName=self.byName,
            bySteam64=self.bySteam64,
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
    preview = await asyncio.to_thread(moderation.previewBan, target)

    await interaction.followup.send(
        embed=cheatingPreviewEmbed(target, preview),
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


def profileLinks(target) -> discord.ui.View:
    """Their PeekSeek profile and their Steam profile, as link buttons.

    Link buttons need no interaction handling, so these keep working whatever state
    the bot is in, and they save anyone reading a stat line from copying a steam64
    out of it to go and look the player up.
    """
    view = discord.ui.View()
    view.add_item(
        discord.ui.Button(
            label="PeekSeek profile",
            style=discord.ButtonStyle.link,
            url=f"{settings.base_url}/u/{target}",
        )
    )
    view.add_item(
        discord.ui.Button(
            label="Steam profile",
            style=discord.ButtonStyle.link,
            url=f"https://steamcommunity.com/profiles/{target}",
        )
    )
    return view


async def sendStats(interaction: discord.Interaction, target) -> None:
    """One player's record. Deliberately says nothing about bans: that belongs to
    /unban, which is the command that acts on them."""
    with opendb() as db:
        record = db.query(data.Stat).where(data.Stat.steam64 == int(target)).first()

    embed = discord.Embed(
        title=displayName(target),
        url=f"{settings.base_url}/u/{target}",
        colour=GREY,
    )
    embed.add_field(name="Steam64", value=f"`{target}`", inline=True)

    if record:
        embed.add_field(name="Elo", value=str(record.elo), inline=True)
        embed.add_field(
            name="Record",
            value=f"{record.wins}W {record.losses}L - {record.kills}K {record.deaths}D",
            inline=True,
        )
        embed.add_field(name="Headshots", value=f"{record.headshotPercent}%", inline=True)
    else:
        embed.add_field(name="Elo", value="Never played a ranked match", inline=True)

    await interaction.response.send_message(
        embed=embed, view=profileLinks(target), ephemeral=True
    )


@bot.tree.command(name="stats", description="Look up a player's PeekSeek record.")
@app_commands.describe(
    player="The Discord account of the player.", steam64="Or their steam64."
)
async def stats(
    interaction: discord.Interaction,
    player: discord.User | None = None,
    steam64: str | None = None,
):
    """Open to everyone. A player's Elo and K/D are already public on their profile,
    so gating the command that reads them out would only make it less useful."""
    if player is None and steam64 is None:
        caller = linkedAccount(interaction.user.id)
        if caller is None:
            await interaction.response.send_message(
                "Your Discord is not linked to a PeekSeek account, so I do not know "
                "who you are. Name a player or pass a steam64.",
                ephemeral=True,
            )
            return
        target = caller.steam64
    else:
        target, error = resolveTarget(player, steam64)
        if error:
            await interaction.response.send_message(error, ephemeral=True)
            return

    await sendStats(interaction, target)


class BanSelect(discord.ui.Select):
    """The menu itself, as a subclass rather than a Select with its callback reassigned.

    Reassigning `.callback` on an instance works until it does not, and when it fails
    the interaction simply dies with "this interaction failed" and nothing is logged,
    which is exactly how this was failing.
    """

    def __init__(self, bans: list, byName: str, bySteam64):
        self.byName = byName
        self.bySteam64 = bySteam64

        options = []
        for ban in bans[:25]:
            issued = ban.created_at.strftime("%d %b %Y") if ban.created_at else "unknown"
            if ban.endTime == 0:
                state = "overturned"
            elif ban.endTime > int(time()):
                state = "in force"
            else:
                state = "expired"

            options.append(
                discord.SelectOption(
                    label=f"{ban.reason or 'No reason'} - {issued} ({state})"[:100],
                    description=(ban.moderatorNote or "no note")[:100],
                    value=str(ban.id),
                )
            )

        super().__init__(placeholder="Pick the ban to lift", options=options)

    async def callback(self, interaction: discord.Interaction):
        await interaction.response.defer(ephemeral=True)
        banId = int(self.values[0])

        lifted = await asyncio.to_thread(
            moderation.overturnBan, banId, self.byName, self.bySteam64
        )
        if lifted is None:
            await interaction.followup.send("That ban no longer exists.", ephemeral=True)
            return

        await interaction.followup.send(
            f"Lifted the **{lifted['reason']}** ban on "
            f"**{displayName(lifted['steam64'])}**. The record is kept and now says you "
            "lifted it.",
            ephemeral=True,
        )
        self.view.stop()


class PickBanToOverturn(discord.ui.View):
    """Every ban this player has, as a menu, because "unban them" is ambiguous the
    moment somebody has been banned more than once.

    Lifting one does not delete the row. It sets the expiry to zero and writes who
    lifted it into the note, so a repeat offender still reads as one and the fact that
    somebody lifted a ban is part of the record rather than something erased.
    """

    def __init__(self, invoker: int, bans: list, byName: str, bySteam64):
        super().__init__(timeout=180)
        self.invoker = invoker
        self.add_item(BanSelect(bans, byName, bySteam64))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if interaction.user.id != self.invoker:
            await interaction.response.send_message("That menu is not yours.", ephemeral=True)
            return False
        return True

    async def on_error(self, interaction, error, item):
        """Without this a failing menu leaves Discord's "interaction failed" and nothing
        in the log, which is indistinguishable from the bot being down."""
        logging.error("Unban menu failed: %s", "".join(traceback.format_exception(error)))
        message = "That failed. It is logged; nothing was changed."
        if interaction.response.is_done():
            await interaction.followup.send(message, ephemeral=True)
        else:
            await interaction.response.send_message(message, ephemeral=True)


async def sendUnban(interaction: discord.Interaction, admin, target) -> None:
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
            f"Currently banned: **{active.reason}** until {active.expiryLabel}."
            if active
            else "They are not currently banned; these are all expired or already lifted."
        ),
    )

    await interaction.response.send_message(
        embed=embed,
        view=PickBanToOverturn(
            interaction.user.id, history, displayName(admin.steam64), admin.steam64
        ),
        ephemeral=True,
    )


@bot.tree.command(name="unban", description="Lift a ban.")
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

    await sendUnban(interaction, admin, target)


# ---------------------------------------------------------------------------
# Context menus: right-click a member, Apps, and the command runs against them.
#
# A user context menu is a module-level function taking exactly the interaction and
# the member, which is why these cannot simply be the slash commands with a decorator
# added. They share the handlers above rather than the logic being written twice.


class BanNote(discord.ui.Modal):
    """The note, and for a custom ban its length and reason.

    A modal rather than more buttons because a note is free text and this is the only
    Discord component that can ask for any. Discord modals take text inputs only, so
    the preset is chosen from a menu first and this asks for what the preset cannot
    say for itself.
    """

    def __init__(self, preset: str, target, admin):
        super().__init__(title=f"{moderation.PRESETS.get(preset, {}).get('label', 'Custom')} ban")
        self.preset = preset
        self.target = target
        self.admin = admin

        self.note = discord.ui.TextInput(
            label="Moderator note",
            placeholder="Clip, match id, what you saw",
            style=discord.TextStyle.paragraph,
            required=False,
            max_length=900,
        )
        self.add_item(self.note)

        if preset == "custom":
            self.days = discord.ui.TextInput(
                label="Days", placeholder="7", max_length=4, required=True
            )
            self.reason = discord.ui.TextInput(
                label="Reason", placeholder="What it is for", max_length=100, required=True
            )
            self.add_item(self.days)
            self.add_item(self.reason)

    async def on_submit(self, interaction: discord.Interaction):
        byName = displayName(self.admin.steam64)

        if self.preset == "custom":
            if not str(self.days).strip().isdigit():
                await interaction.response.send_message(
                    "Days has to be a number.", ephemeral=True
                )
                return
            await asyncio.to_thread(
                moderation.issueBan,
                self.target,
                None,
                int(str(self.days)),
                str(self.reason),
                str(self.note),
                byName,
                self.admin.steam64,
            )
            await interaction.response.send_message(
                f"**{displayName(self.target)}** is banned for {self.days} days.",
                ephemeral=True,
            )
            return

        if self.preset != "cheating":
            await asyncio.to_thread(
                moderation.issueBan,
                self.target,
                self.preset,
                None,
                None,
                str(self.note),
                byName,
                self.admin.steam64,
            )
            length = moderation.PRESETS[self.preset]["summary"].lower()
            await interaction.response.send_message(
                f"**{displayName(self.target)}** is banned for {length}.", ephemeral=True
            )
            return

        # Cheating shows what it would do before doing any of it, exactly as /ban does.
        await interaction.response.defer(ephemeral=True)
        preview = await asyncio.to_thread(moderation.previewBan, self.target)
        await interaction.followup.send(
            embed=cheatingPreviewEmbed(self.target, preview),
            view=ConfirmBan(
                interaction.user.id, self.target, str(self.note), byName, self.admin.steam64
            ),
            ephemeral=True,
        )


class PresetSelect(discord.ui.Select):
    def __init__(self, target, admin):
        self.target = target
        self.admin = admin

        options = [
            discord.SelectOption(
                label=preset["label"], description=preset["summary"], value=key
            )
            for key, preset in moderation.PRESETS.items()
        ]
        options.append(
            discord.SelectOption(
                label="Custom", description="Set days + reason", value="custom"
            )
        )
        super().__init__(placeholder="Why are they being banned?", options=options)

    async def callback(self, interaction: discord.Interaction):
        await interaction.response.send_modal(
            BanNote(self.values[0], self.target, self.admin)
        )


class PickPreset(discord.ui.View):
    def __init__(self, invoker: int, target, admin):
        super().__init__(timeout=180)
        self.invoker = invoker
        self.add_item(PresetSelect(target, admin))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if interaction.user.id != self.invoker:
            await interaction.response.send_message("That menu is not yours.", ephemeral=True)
            return False
        return True

    async def on_error(self, interaction, error, item):
        logging.error("Ban menu failed: %s", "".join(traceback.format_exception(error)))
        if not interaction.response.is_done():
            await interaction.response.send_message(
                "That failed. It is logged; nothing was changed.", ephemeral=True
            )


@bot.tree.context_menu(name="PeekSeek stats")
async def statsContext(interaction: discord.Interaction, member: discord.Member):
    """Open to everyone, same as /stats."""
    target, error = resolveTarget(member, None)
    if error:
        await interaction.response.send_message(error, ephemeral=True)
        return
    await sendStats(interaction, target)


@bot.tree.context_menu(name="Ban from PeekSeek")
@app_commands.default_permissions(administrator=True)
async def banContext(interaction: discord.Interaction, member: discord.Member):
    admin = await requireAdmin(interaction)
    if admin is None:
        return

    target, error = resolveTarget(member, None)
    if error:
        await interaction.response.send_message(error, ephemeral=True)
        return

    await interaction.response.send_message(
        f"Banning **{displayName(target)}** (`{target}`).",
        view=PickPreset(interaction.user.id, target, admin),
        ephemeral=True,
    )


@bot.tree.context_menu(name="Lift PeekSeek ban")
@app_commands.default_permissions(administrator=True)
async def unbanContext(interaction: discord.Interaction, member: discord.Member):
    admin = await requireAdmin(interaction)
    if admin is None:
        return

    target, error = resolveTarget(member, None)
    if error:
        await interaction.response.send_message(error, ephemeral=True)
        return

    await sendUnban(interaction, admin, target)


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


# ---------------------------------------------------------------------------
# The sticky notice telling players how to report a cheater.

# The title identifies the bot's own sticky when clearing up after a restart. It used
# to be a footer, which put a line of housekeeping in front of readers to solve a
# problem only the bot has.
STICKY_TITLE = "Reporting a cheater"

# How long to let a burst of messages settle before reposting. Without it, five
# messages arriving together would delete and repost the sticky five times, which is
# both rate-limit bait and visible flicker for anyone reading.
STICKY_SETTLE = 3

stickyId: int | None = None
stickyLock = asyncio.Lock()
stickyPending = False


def stickyEmbed() -> discord.Embed:
    """Two situations, two answers, in the order somebody arrives in.

    Anybody opening this channel has just watched something they think was cheating.
    The first thing they need to know is that there is a better option than typing it
    out, and that it takes seconds, so that goes first and short. The fallback for
    people who have already quit the server goes second, because the people who need
    it will keep reading and the people who do not are already gone.
    """
    return discord.Embed(
        title=STICKY_TITLE,
        colour=0xFFA34E,
        description=(
            "**Still in the server? Use `/report` in game.**\n"
            "Type `/report <player> <what they did>`. Staff instantly get a picture of "
            "their screen and their damage, accuracy and headshot rate for the round. "
            "It takes about five seconds and it is the strongest evidence you can "
            "give us.\n\n"
            "**Already left? Post here with a clip.**\n"
            "Show the whole fight if you can. A short video tells us more than a "
            "screenshot of the killfeed or a description ever will."
        ),
    )


async def clearOldStickies(channel) -> None:
    """Delete any sticky left over from a previous run.

    A restart otherwise strands the old one part way up the channel, and after a few
    restarts the channel is mostly stickies.
    """
    async for message in channel.history(limit=50):
        if message.author.id != bot.user.id or not message.embeds:
            continue
        if message.embeds[0].title == STICKY_TITLE:
            try:
                await message.delete()
            except discord.HTTPException:
                pass


async def postSticky(channel) -> None:
    global stickyId

    if stickyId is not None:
        try:
            await channel.get_partial_message(stickyId).delete()
        except discord.HTTPException:
            # Already gone, or somebody deleted it by hand. Either way it is not in the
            # way any more, which is all this delete was for.
            pass

    stickyId = (await channel.send(embed=stickyEmbed())).id


@bot.event
async def on_message(message: discord.Message):
    """Keep the notice at the bottom of its channel.

    Its own message rather than a pinned one: a pin is a click away and nobody clicks
    it, and the whole point is that somebody about to type out a description of a cheat
    reads that there is a better way first.

    Only a message from somebody else moves it. Comparing the id against the sticky's
    own was not enough and could not be: the gateway delivers MESSAGE_CREATE before
    channel.send returns, so the sticky's id is still unset when its own arrival is
    handled, and it retriggered on itself forever. Author is known at that moment and
    the id is not.
    """
    global stickyPending

    if message.author.id == bot.user.id:
        return
    if not settings.sticky_channel or message.channel.id != int(settings.sticky_channel):
        return

    if stickyPending:
        return
    stickyPending = True

    try:
        await asyncio.sleep(STICKY_SETTLE)
        async with stickyLock:
            stickyPending = False
            await postSticky(message.channel)
    except discord.HTTPException as error:
        stickyPending = False
        logging.warning("Could not move the sticky: %s", error)


@bot.event
async def on_ready():
    logging.info("Logged in as %s", bot.user)

    if not settings.sticky_channel:
        return
    channel = bot.get_channel(int(settings.sticky_channel))
    if channel is None:
        logging.warning("Sticky channel %s not visible", settings.sticky_channel)
        return

    async with stickyLock:
        await clearOldStickies(channel)
        await postSticky(channel)


if __name__ == "__main__":
    token = settings.bot_token
    if not token:
        raise SystemExit("BOT_TOKEN is not set.")
    bot.run(token)
