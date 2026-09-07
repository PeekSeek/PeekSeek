using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

internal enum SiegePhase {
    Idle,
    Building,
    Picking,
    Playing,
    Over
}

/// Siege: build a base, then take turns attacking each other's.
///
/// The match is one build phase and then a run of rounds. The build phase is the only
/// time the buildings change; after that a base is a fixed thing that gets re-laid
/// before every round exactly as it was left, and the only per-round decision is what
/// each player carries in. That split is the whole design — a team spends five minutes
/// on a building and then has to both hold it and crack the mirror of it, so a base
/// that is only good to defend is a bad base.
///
/// The rules that make it read as a siege rather than a deathmatch on a small map are
/// all about ground. Defenders take damage standing on dirt, so holding means holding
/// a building. Nothing in a base can be salvaged except the doors a team needs to move
/// through and the birch walls it deliberately built soft, so a round cannot be won by
/// dismantling the map.
internal class Siege : IGamemode {
    public string Name => "Siege";

    private GamemodeMatch match;
    private SiegeSites sites;

    private SiegePhase phase = SiegePhase.Idle;
    private float phaseEndsAt;

    private int round;
    private readonly int[] score = [0, 0];

    /// Each team's finished building, taken once at the end of the build phase.
    private readonly BaseSnapshot[] bases = new BaseSnapshot[2];

    private readonly PickBoard board = new();

    /// When each player was last standing on something built, so the grace period
    /// before off-base damage can be measured. Absent means they are on a base now.
    private readonly Dictionary<ulong, float> offBaseSince = [];

    /// Who has already drawn their one metal allowance this build phase. Cleared when a
    /// build phase opens, so solo drawing at the second site is a fresh allowance.
    private readonly HashSet<ulong> rationed = [];

    private float nextSupplyRefill;
    private float nextTick;

    /// Solo only: which side of the one base the lone tester is currently playing. 0 is
    /// holding it, 1 is attacking it. Set from the round order at the start of every
    /// round, and overridable for that round with /siege side. A real match takes this
    /// from the roster instead.
    private int soloSide;

    /// The items this gamemode places itself. They are never captured into a snapshot
    /// and never cleared between rounds, because they are the site rather than the
    /// base — a team that could move its own anchor could build a second base beside
    /// the first and pick whichever one suited the round.
    private HashSet<ushort> Fixtures => [
        PeekSeekSiege.Config.AnchorFloorItemId,
        PeekSeekSiege.Config.AnchorMarkerItemId
    ];

    /// The site being attacked this round, which is the site its owners are defending.
    ///
    /// In a real match this alternates: a team defends its own base for three rounds and
    /// then goes and attacks the other team's for three. Solo has only one base, so every
    /// round is fought over that one and the tester swaps which end of it they are on.
    internal int DefendedSite => RoundOrder.Defender(round);

    /// Whether a player is on the defending side of the round being played.
    internal bool IsDefending(ulong steam64) => SideOf(steam64) == DefendedSite;

    internal SiegePhase Phase => phase;
    internal int Round => round;
    internal PickBoard Board => board;
    internal GamemodeMatch Match => match;

    /// Seconds left in the current phase, floored at zero.
    internal int SecondsLeft => Mathf.Max(0, Mathf.CeilToInt(phaseEndsAt - Time.realtimeSinceStartup));

    public void Begin(GamemodeMatch started) {
        match = started;
        sites = new SiegeSites(PeekSeekSiege.Config.Sites);
        round = 0;
        score[0] = 0;
        score[1] = 0;
        soloSide = 0;

        SiegeWorld.Hook(this);

        Chat.Announce(Chat.Rule);
        Chat.Announce(PeekSeekSiege.Instance.Translate("match_intro",
            RoundOrder.RegulationHalf, PeekSeekSiege.Config.BuildSeconds / 60));

        StartBuilding(0);
    }

    public void End() {
        board.Close();
        SiegeWorld.Unhook();
        phase = SiegePhase.Idle;
        match = null;
    }

    public void Tick() {
        if (phase == SiegePhase.Idle || phase == SiegePhase.Over) return;

        // Everything below is per-second bookkeeping, not per-frame work: clocks,
        // supply top-ups and the off-base check all read the same once a second as
        // they would sixty times, and the raycast in particular is not free.
        if (Time.realtimeSinceStartup < nextTick) return;
        nextTick = Time.realtimeSinceStartup + 1f;

        switch (phase) {
            case SiegePhase.Building:
                TickBuilding();
                break;
            case SiegePhase.Picking:
                TickPicking();
                break;
            case SiegePhase.Playing:
                TickPlaying();
                break;
        }
    }

    public void OnPlayerJoined(Player player) {
        if (match == null) return;

        Chat.Whisper(player.channel.owner, PeekSeekSiege.Instance.Translate("welcome",
            phase.ToString().ToLowerInvariant(), round, score[0], score[1]));

        Place(player);
    }

    public void OnPlayerLeft(CSteamID steamID) {
        offBaseSince.Remove(steamID.m_SteamID);
    }

    public void OnPlayerDied(Player player, CSteamID killer) {
        offBaseSince.Remove(player.channel.owner.playerID.steamID.m_SteamID);
        if (phase != SiegePhase.Playing) return;

        // A death can end the round, so the check runs on the next tick rather than
        // here: the player is dead but Unturned has not finished moving them yet, and
        // counting the living from inside the death event reads the old numbers.
        nextTick = 0f;
    }

    // ---- build phase ----------------------------------------------------------

    /// Open the build phase.
    ///
    /// Solo runs this twice, once per site, because one person testing has to be able
    /// to build both buildings before there is anything to attack.
    private void StartBuilding(int siteIndex) {
        phase = SiegePhase.Building;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.BuildSeconds;
        nextSupplyRefill = 0f;
        rationed.Clear();

        foreach (int team in BuiltSites()) {
            SiegeSite site = sites[team];
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, Fixtures);
            SiegeWorld.PlaceAnchor(site, GroupOf(team));
        }

        foreach (Player player in Players()) Place(player);

        Chat.Announce(PeekSeekSiege.Instance.Translate("build_start",
            PeekSeekSiege.Config.BuildSeconds / 60,
            match.Solo ? sites[siteIndex].Name : "your site"));
    }

    private void TickBuilding() {
        if (Time.realtimeSinceStartup >= nextSupplyRefill) {
            nextSupplyRefill = Time.realtimeSinceStartup + PeekSeekSiege.Config.SupplyRefillSeconds;
            foreach (Player player in Players()) Supply(player);
        }

        Leash();
        Countdown("build_countdown");

        if (SecondsLeft > 0) return;
        FinishBuilding();
    }

    /// Freeze what has been built and move on.
    ///
    /// In solo this loops back for the second site instead, because the tester has
    /// only built one of the two buildings so far.
    internal void FinishBuilding() {
        foreach (int team in BuiltSites()) bases[team] = Snapshot(team);

        Chat.Announce(PeekSeekSiege.Instance.Translate("build_done",
            bases[0]?.Count ?? 0, bases[1]?.Count ?? 0));

        StartRound(1);
    }

    /// Which sites get a base built on them. Every team builds its own, except in solo,
    /// where there is one tester, one base, and one place the whole match is fought.
    private IEnumerable<int> BuiltSites() {
        return match.Solo ? [0] : Enumerable.Range(0, sites.Count);
    }

    /// The base to stand at each site.
    ///
    /// Solo builds one base and then has to attack something, so that one base is put up
    /// at both anchors: the tester spends three rounds holding the building they made and
    /// three attacking a copy of it. In a real match each team has its own.
    private BaseSnapshot BaseFor(int team) {
        if (match.Solo) return bases[0];

        return bases.Length > team ? bases[team] : null;
    }

    /// The Unturned group a site's team is in, or 0 before the roster has one.
    private ulong GroupOf(int team) {
        return match != null && match.Teams.Count > team ? match.Teams[team].GroupId : 0;
    }

    private BaseSnapshot Snapshot(int team) {
        SiegeSite site = sites[team];
        BaseSnapshot taken = BaseSnapshot.Capture(site.AnchorPoint, site.BuildRadius, Fixtures);
        Logger.Log($"Captured {taken.Count} piece(s) for {site.Name}.");
        return taken;
    }

    // ---- rounds ---------------------------------------------------------------

    private void StartRound(int number) {
        round = number;

        RestoreBases();
        int defender = DefendedSite;

        phase = SiegePhase.Picking;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.PickSeconds;

        Chat.Announce(Chat.Rule);
        Chat.Announce(PeekSeekSiege.Instance.Translate("round_start",
            round, sites[defender].Name, score[0], score[1]));

        OpenBoard(defender);
    }

    /// Put both buildings back exactly as they were built, then hand the pieces out
    /// according to the rules that make a round playable. This runs before every round
    /// including the first, so round one and round six start from identical ground.
    private void RestoreBases() {
        for (int team = 0; team < sites.Count; team++) {
            SiegeSite site = sites[team];
            ulong group = GroupOf(team);

            // Nothing is skipped here, marker included. A round can end with the flag
            // shot down, so putting a base back means putting its anchor back too.
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
            SiegeWorld.PlaceAnchor(site, group);
            BaseFor(team)?.Restore(site.AnchorPoint, asset => OwnerGroupFor(asset, group));
        }
    }

    /// Who owns a piece of a restored base, which in Unturned is the same question as
    /// who may take it down and who may open it.
    ///
    /// Unowned is the default and it is the important one: a wall nobody owns cannot be
    /// salvaged by anybody, so the only way through it is to break it. The two
    /// exceptions are the ones a team needs to be able to work with. Doors and hatches
    /// are how a team moves through its own building, and an unowned door is a door
    /// nobody can open. Birch walls are the soft walls: a team that deliberately built
    /// in birch gets to pull one down mid-round and open an angle that was not there
    /// before, which is the closest thing this has to Rainbow Six's soft breaching.
    /// Everything else — metal walls, every floor, every pillar — stays unowned, so a
    /// base cannot be dismantled into a better base while a round is running.
    internal static ulong OwnerGroupFor(ItemAsset asset, ulong teamGroup) {
        if (asset is ItemBarricadeAsset barricade) {
            return barricade.build == EBuild.DOOR || barricade.build == EBuild.HATCH
                ? teamGroup
                : 0;
        }

        if (asset is ItemStructureAsset structure) {
            return IsSoftWall(structure) ? teamGroup : 0;
        }

        return 0;
    }

    /// A soft wall is a birch wall, and only a wall. Birch floors and pillars hold the
    /// building up, so letting a team salvage those would let them drop their own roof
    /// on an attacker, or worse, delete the floor an attacker is about to stand on.
    internal static bool IsSoftWall(ItemStructureAsset asset) {
        if (asset.construct != EConstruct.WALL && asset.construct != EConstruct.RAMPART) return false;

        return asset.name.IndexOf("Birch", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OpenBoard(int defender) {
        List<SiegeOption> defence = PeekSeekSiege.Config.DefenceOptions;
        List<SiegeOption> raid = PeekSeekSiege.Config.RaidOptions;

        board.Reset();

        // One board each, because the two sides are choosing from different lists at the
        // same moment. Solo asks SideOf rather than the roster, so the lone tester gets
        // the board for whichever side they are currently playing.
        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            bool defending = IsDefending(viewer.playerID.steamID.m_SteamID);

            board.Open(
                viewer,
                defending ? defence : raid,
                PeekSeekSiege.Instance.Translate(
                    defending ? "board_defend" : "board_attack", round, sites[defender].Name));
        }
    }

    private void TickPicking() {
        board.Countdown(SecondsLeft);
        if (SecondsLeft > 0) return;

        board.Close();
        StartPlaying();
    }

    private void StartPlaying() {
        phase = SiegePhase.Playing;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.RoundSeconds;
        offBaseSince.Clear();

        foreach (Player player in Players()) {
            Place(player);
            SiegeWorld.Kit(player, board.Chosen(player.channel.owner.playerID.steamID.m_SteamID));
        }

        Chat.Announce(PeekSeekSiege.Instance.Translate("round_live", PeekSeekSiege.Config.RoundSeconds));
    }

    private void TickPlaying() {
        PunishOffBase();
        Countdown("round_countdown");

        int defender = DefendedSite;

        // The flag is the objective. Attackers win by bringing it down; defenders win by
        // still having it up when the clock runs out.
        if (!SiegeWorld.MarkerStands(sites[defender].AnchorPoint)) {
            EndRound(1 - defender, "round_won_flag");
            return;
        }

        if (SecondsLeft <= 0) {
            EndRound(defender, "round_won_clock");
            return;
        }

        // Solo has nobody to wipe, so it only ever ends on the clock or on /skip.
        if (match.Solo) return;

        if (Living(defender) == 0) {
            EndRound(1 - defender, "round_won_wipe");
            return;
        }
        if (Living(1 - defender) == 0) {
            EndRound(defender, "round_won_wipe");
        }
    }

    /// Award the round and either start the next one or call the match.
    internal void EndRound(int winner, string reasonKey) {
        if (phase != SiegePhase.Playing && phase != SiegePhase.Picking) return;

        score[winner]++;
        Chat.Announce(PeekSeekSiege.Instance.Translate(reasonKey,
            TeamName(winner), round, score[0], score[1]));

        int matchWinner = RoundOrder.Winner(round, score[0], score[1]);
        if (matchWinner >= 0) {
            phase = SiegePhase.Over;
            Chat.Announce(Chat.Rule);
            Chat.Announce(PeekSeekSiege.Instance.Translate("match_won",
                TeamName(matchWinner), score[0], score[1]));
            return;
        }

        if (RoundOrder.EndsABlock(round)) {
            Chat.Announce(PeekSeekSiege.Instance.Translate("overtime", RoundOrder.OvertimeHalf));
        }

        StartRound(round + 1);
    }

    // ---- the rules that hold a round together ---------------------------------

    /// Hurt defenders who are not standing on their building.
    ///
    /// The grace period matters more than the damage does. A defender vaults a
    /// windowsill, drops through a hatch, or lands a jump slightly short, and in every
    /// one of those they are briefly on dirt while doing exactly what a defender is
    /// meant to do. Two seconds is long enough to cover all of it and short enough that
    /// nobody crosses open ground on it.
    private void PunishOffBase() {
        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (!IsDefending(steam64) || player.life.isDead) continue;

            if (SiegeWorld.IsStandingOnBuilt(player)) {
                offBaseSince.Remove(steam64);
                continue;
            }

            if (!offBaseSince.TryGetValue(steam64, out float since)) {
                offBaseSince[steam64] = Time.realtimeSinceStartup;
                continue;
            }

            if (Time.realtimeSinceStartup - since < PeekSeekSiege.Config.OffBaseGraceSeconds) continue;

            player.life.askDamage(
                PeekSeekSiege.Config.OffBaseDamagePerSecond,
                Vector3.up,
                EDeathCause.ARENA,
                ELimb.SPINE,
                CSteamID.Nil,
                out EPlayerKill _);

            Chat.Whisper(player.channel.owner, PeekSeekSiege.Instance.Translate("off_base"));
        }
    }

    /// Pull anyone who has wandered out of the build phase back onto their plot.
    ///
    /// Both sites are on one map and the other team is building in plain sight of
    /// anyone willing to walk far enough. The leash is what keeps a build phase secret,
    /// which is what makes the first round worth playing.
    private void Leash() {
        foreach (Player player in Players()) {
            SiegeSite site = sites[SideOf(player.channel.owner.playerID.steamID.m_SteamID)];
            if (site.Contains(player.transform.position)) continue;

            player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(site), site.Yaw);
            Chat.Whisper(player.channel.owner, PeekSeekSiege.Instance.Translate("leashed"));
        }
    }

    /// Put one player where the current phase says they belong, and kit them for it.
    private void Place(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        int side = SideOf(steam64);
        SiegeSite own = sites[side];

        if (phase == SiegePhase.Building) {
            player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(own), own.Yaw);
            Supply(player);
            return;
        }

        SiegeSite defended = sites[DefendedSite];

        if (IsDefending(steam64)) {
            player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(defended), defended.Yaw);
            return;
        }

        SiegeSite target = defended;
        int index = IndexInTeam(steam64);
        player.teleportToLocationUnsafe(
            SiegeWorld.OnGround(target.AttackerSpawn(index, Math.Max(1, TeamSize(1 - side)))),
            target.AttackerBearing + 180f);
    }

    /// Kit a builder out: what they wear and hold first, then the materials.
    ///
    /// Order is the whole point. Unturned inventory space comes from clothing, so a
    /// player with no backpack and no vest has almost none — hand them forty walls
    /// before the bag and most of it lands on the floor.
    /// Kit a builder out: what they wear, then their one allowance of metal, then as
    /// much wood as they can carry.
    ///
    /// Order is the whole point. Unturned inventory space comes from clothing, so a
    /// player with no backpack has almost none, and handing them materials first puts
    /// most of it on the floor. The allowance is issued once per build phase and never
    /// again — that is what makes metal a decision — while the wood is topped up on
    /// every refill.
    private void Supply(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;

        // Once per build phase: their own loadout, the tools, and the metal allowance.
        // A builder wears what they always wear rather than a uniform this gamemode
        // invented, so the only thing that marks them out as building is the planner.
        if (rationed.Add(steam64)) {
            SiegeWorld.Outfit(player);
            SiegeWorld.Issue(player, PeekSeekSiege.Config.BuildTools);
            SiegeWorld.Issue(player, PeekSeekSiege.Config.RationedSupplies);
        }

        SiegeWorld.ExpandHands(player, PeekSeekSiege.Config.HandsWidth, PeekSeekSiege.Config.HandsHeight);
        SiegeWorld.TopUp(player, PeekSeekSiege.Config.PlentifulSupplies);
    }

    // ---- who is who -----------------------------------------------------------

    /// Which side a player is on. Solo answers with whichever side the tester last
    /// asked for, so one person can walk both halves of the same round.
    internal int SideOf(ulong steam64) {
        if (match == null) return 0;

        // Solo builds the two sites one after the other, so during the build phase the
        // side a player is on is whichever site is currently open rather than whichever
        // one they last asked to play. Getting this wrong puts the tester back on the
        // finished base and leashes them to it while the clock runs on the other one.
        // Solo is always team 0. The round order does the rest: team 0 defends the first
        // three rounds and attacks the second three, so the tester holds their base and
        // then goes and cracks the other one without touching a command.
        if (match.Solo) return soloSide;

        int index = match.TeamIndexOf(steam64);
        return index < 0 ? 0 : index;
    }

    /// Solo only: swap the tester to the other side of the round they are in.
    internal void SwapSoloSide() {
        soloSide = 1 - soloSide;
        foreach (Player player in Players()) {
            Place(player);
            if (phase == SiegePhase.Playing) {
                SiegeWorld.Kit(player, board.Chosen(player.channel.owner.playerID.steamID.m_SteamID));
            }
        }
    }

    internal string TeamName(int team) {
        return match.Teams.Count > team ? match.Teams[team].Name : sites[team].Name;
    }

    private int TeamSize(int team) {
        return match.Teams.Count > team ? match.Teams[team].Players.Count : 1;
    }

    private int IndexInTeam(ulong steam64) {
        GamemodeTeam team = match.TeamOf(steam64);
        return team == null ? 0 : Math.Max(0, team.Players.IndexOf(steam64));
    }

    private int Living(int team) {
        if (match.Teams.Count <= team) return 0;

        return match.Teams[team].Players.Count(steam64 => {
            Player player = PlayerTool.getPlayer(new CSteamID(steam64));
            return player != null && !player.life.isDead;
        });
    }

    private static IEnumerable<Player> Players() {
        return Provider.clients.ToList()
            .Where(client => client.player != null)
            .Select(client => client.player);
    }

    /// Say the time left, but only at the moments worth saying it at. A clock that
    /// announces every second is noise a player learns to stop reading.
    private static readonly int[] CalledOut = [1, 2, 3, 5, 10, 30, 60, 120, 240];

    private void Countdown(string key) {
        int left = SecondsLeft;
        if (!CalledOut.Contains(left)) return;

        Chat.Announce(PeekSeekSiege.Instance.Translate(key, left));
    }
}
