using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Hud;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

internal enum SiegePhase {
    Idle,
    Shaping,
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

    /// The footprint each team chose to start from. Chosen on a board before the build
    /// phase; Square if nobody on the team answered.
    private readonly BaseShape[] shapes = [BaseShape.Square, BaseShape.Square];

    private readonly PickBoard board = new();
    private readonly MatchHud hud = new();

    /// When each player was last standing on something built, so the grace period
    /// before off-base damage can be measured. Absent means they are on a base now.
    private readonly Dictionary<ulong, float> offBaseSince = [];

    /// Who has been kitted out this build phase. Cleared when a build phase opens, so a
    /// player who dies or rejoins mid-build is dressed again but not resupplied.
    private readonly HashSet<ulong> supplied = [];

    /// Who has died this round. A death is what takes a player out of a round, and it
    /// has to be remembered rather than read off `isDead`, because solo lets the tester
    /// respawn and a real match only respawns everyone when the next round starts.
    private readonly HashSet<ulong> deadThisRound = [];

    private float nextTick;

    /// Solo only: which side of the one base the lone tester is playing, 0 holding it
    /// and 1 attacking it. The round order flips who defends, so leaving this at 0
    /// walks the tester through both halves; /siege side overrides it for a look at the
    /// other half of the same round.
    private int soloSide;

    /// The items this gamemode places itself. They are never captured into a snapshot
    /// and never cleared between rounds, because they are the site rather than the
    /// base — a team that could move its own anchor could build a second base beside
    /// the first and pick whichever one suited the round.
    private HashSet<ushort> Fixtures => [
        PeekSeekSiege.Config.AnchorFloorItemId,
        PeekSeekSiege.Config.AnchorTriangleItemId,
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
            RoundOrder.RegulationHalf, Wording.Duration(PeekSeekSiege.Config.BuildSeconds)));

        StartShaping();
    }

    public void End() {
        board.Close();
        hud.Hide();
        SiegeWorld.Unhook();

        foreach (Player player in Players()) AllowRespawn(player);

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

        hud.Countdown(SecondsLeft);

        switch (phase) {
            case SiegePhase.Shaping:
                TickShaping();
                break;
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
        RefreshHud();
    }

    public void OnPlayerLeft(CSteamID steamID) {
        offBaseSince.Remove(steamID.m_SteamID);
        hud.Forget(steamID.m_SteamID);
        RefreshHud();
    }

    public void OnPlayerDied(Player player, CSteamID killer) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        offBaseSince.Remove(steam64);

        if (phase == SiegePhase.Playing) {
            deadThisRound.Add(steam64);

            // Dead is dead until the round is over. Unturned would otherwise offer the
            // respawn button after its own timer and put them back on a map spawn, alive
            // and counted. Solo has nobody to be unfair to, so the tester respawns.
            if (!match.Solo) player.setPluginWidgetFlag(EPluginWidgetFlags.ShowDeathMenu, false);

            // A death can end the round, so the check runs on the next tick rather than
            // here: the player is dead but Unturned has not finished moving them yet, and
            // counting the living from inside the death event reads the old numbers.
            nextTick = 0f;
        }

        RefreshHud();
    }

    /// Back from the dead, on Unturned's own respawn: put them where the phase says and
    /// dress them for it. Without this a respawn lands on a random map spawn with an
    /// empty inventory, which is what "the gamemode broke" looked like from the inside.
    public void OnPlayerRevived(Player player) {
        if (match == null) return;

        Place(player);
        if (phase != SiegePhase.Building) SiegeWorld.Outfit(player);
        RefreshHud();
    }

    /// Show or hide the HUD for one player. It covers the game's own compass and arena
    /// text on purpose, so turning it off is also how a player gets those back.
    internal bool ToggleHud(ulong steam64) {
        bool shown = hud.Toggle(steam64);
        RefreshHud();
        return shown;
    }

    // ---- the footprint ----------------------------------------------------------

    /// Before anybody builds, each team says what it is building on: a board with the
    /// three footprints, up for as long as a pick. The first answer on a team is the
    /// team's; a team that never answers gets the square.
    private void StartShaping() {
        phase = SiegePhase.Shaping;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.PickSeconds;

        foreach (int team in BuiltSites()) {
            BaseSnapshot.Clear(sites[team].AnchorPoint, sites[team].BuildRadius, []);
        }
        foreach (Player player in Players()) Place(player);

        board.Reset();
        List<SiegeOption> footprints = ShapeOptions();
        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            board.Open(viewer, footprints, PeekSeekSiege.Instance.Translate("board_shape"));
        }

        RefreshHud();
    }

    private void TickShaping() {
        Leash();
        board.Countdown(SecondsLeft);

        bool everyoneAnswered = Players().All(p => board.HasAnswered(p.channel.owner.playerID.steamID.m_SteamID));
        if (SecondsLeft > 0 && !everyoneAnswered) return;

        FinishShaping();
    }

    internal void FinishShaping() {
        for (int team = 0; team < 2; team++) shapes[team] = ShapeChosenBy(team);
        board.Close();
        StartBuilding();
    }

    /// The three footprints as tiles: the first answer on a team decides for the team.
    private static List<SiegeOption> ShapeOptions() {
        return [
            new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("shape_square"),
                Blurb = PeekSeekSiege.Instance.Translate("shape_square_blurb"),
                Icon = PeekSeekSiege.Config.AnchorFloorItemId
            },
            new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("shape_hexagonal"),
                Blurb = PeekSeekSiege.Instance.Translate("shape_hexagonal_blurb"),
                Icon = PeekSeekSiege.Config.AnchorTriangleItemId
            },
            new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("shape_round"),
                Blurb = PeekSeekSiege.Instance.Translate("shape_round_blurb"),
                Icon = PeekSeekSiege.Config.TriangleSupplies.Count > 0 ? PeekSeekSiege.Config.TriangleSupplies[0].ItemId : (ushort)0
            }
        ];
    }

    private BaseShape ShapeChosenBy(int team) {
        if (match.Teams.Count <= team) return BaseShape.Square;

        foreach (ulong steam64 in match.Teams[team].Players) {
            int pick = board.PickIndex(steam64);
            if (pick >= 0) return (BaseShape)pick;
        }

        return BaseShape.Square;
    }

    // ---- build phase ----------------------------------------------------------

    private void StartBuilding() {
        phase = SiegePhase.Building;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.BuildSeconds;
        supplied.Clear();

        foreach (int team in BuiltSites()) {
            SiegeSite site = sites[team];
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
            SiegeWorld.PlaceAnchor(site, GroupOf(team), shapes[team]);
            SiegeWorld.PlacePillars(site, GroupOf(team), shapes[team]);
        }

        foreach (Player player in Players()) Place(player);

        Chat.Announce(PeekSeekSiege.Instance.Translate("build_start",
            Wording.Duration(PeekSeekSiege.Config.BuildSeconds),
            match.Solo ? sites[0].Name : "your site"));
        for (int team = 0; team < (match.Solo ? 1 : 2); team++) {
            Chat.Announce(PeekSeekSiege.Instance.Translate("build_shape", TeamName(team),
                PeekSeekSiege.Instance.Translate("shape_" + shapes[team].ToString().ToLowerInvariant())));
        }

        RefreshHud();
    }

    private void TickBuilding() {
        Leash();
        Countdown("build_countdown");

        if (SecondsLeft > 0) return;
        FinishBuilding();
    }

    /// Freeze what has been built and move on.
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

    /// The footprint standing at a site. Solo stands its one base at both anchors, so
    /// both get the tester's shape.
    private BaseShape ShapeAt(int team) {
        return match.Solo ? shapes[0] : shapes[team];
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
        deadThisRound.Clear();

        RestoreBases();

        // Everything a round left on the ground: the loot a death dropped, the wall
        // somebody never placed. Siege has no world loot, so there is nothing to keep.
        ItemManager.askClearAllItems();

        int defender = DefendedSite;

        phase = SiegePhase.Picking;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.PickSeconds;

        // Whoever died last round comes back now, and everybody is put where the round
        // says they belong before the board goes up, so nobody spends the pick phase
        // standing in a base that is being re-laid around them.
        foreach (Player player in Players()) {
            if (player.life.isDead) {
                AllowRespawn(player);
                player.life.ServerRespawn(false);
                continue;
            }
            Place(player);
        }

        Chat.Announce(Chat.Rule);
        Chat.Announce(PeekSeekSiege.Instance.Translate("round_start",
            round, sites[defender].Name, score[0], score[1]));

        OpenBoard(defender);
        RefreshHud();
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
            SiegeWorld.PlaceAnchor(site, group, ShapeAt(team));
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
        RefreshHud();
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
            board.Close();
            Chat.Announce(Chat.Rule);
            Chat.Announce(PeekSeekSiege.Instance.Translate("match_won",
                TeamName(matchWinner), score[0], score[1]));
            RefreshHud();
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

            float off = Time.realtimeSinceStartup - since;
            if (off < PeekSeekSiege.Config.OffBaseGraceSeconds) continue;

            player.life.askDamage(
                PeekSeekSiege.Config.OffBaseDamagePerSecond,
                Vector3.up,
                EDeathCause.ARENA,
                ELimb.SPINE,
                CSteamID.Nil,
                out EPlayerKill _);

            // Said once, as the damage starts. Every second after that the health bar
            // is saying it, and a line of chat per second is noise nobody reads.
            if (off < PeekSeekSiege.Config.OffBaseGraceSeconds + 1f) {
                Chat.Whisper(player.channel.owner, PeekSeekSiege.Instance.Translate("off_base"));
            }
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

        if (phase == SiegePhase.Shaping) {
            player.teleportToLocationUnsafe(SiegeWorld.OnGround(own.AnchorPoint), own.Yaw);
            return;
        }

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

        int index = IndexInTeam(steam64);
        player.teleportToLocationUnsafe(
            SiegeWorld.OnGround(defended.AttackerSpawn(index, Math.Max(1, TeamSize(side)))),
            defended.AttackerBearing + 180f);
    }

    /// Kit a builder out, once per build phase: their clothes, the planner in hand, and
    /// the whole budget of materials. Nothing is topped up; the amounts in BuildSupplies
    /// are the base.
    ///
    /// A player placed again mid-build (a respawn, a rejoin) is dressed again but not
    /// resupplied, or dying would be a way to double the kit.
    private void Supply(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        bool first = supplied.Add(steam64);

        SiegeWorld.DressForBuilding(player);
        if (!first) return;

        SiegeWorld.Issue(player, PeekSeekSiege.Config.BuildSupplies);
        if (ShapeAt(SideOf(steam64)) != BaseShape.Square) {
            SiegeWorld.Issue(player, PeekSeekSiege.Config.TriangleSupplies);
        }
    }

    private static void AllowRespawn(Player player) {
        player.setPluginWidgetFlag(EPluginWidgetFlags.ShowDeathMenu, true);
    }

    // ---- who is who -----------------------------------------------------------

    /// Which side a player is on. Solo answers with whichever side the tester is
    /// playing, so one person can walk both halves of the same round.
    internal int SideOf(ulong steam64) {
        if (match == null) return 0;
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
        RefreshHud();
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
            return player != null && !player.life.isDead && !deadThisRound.Contains(steam64);
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

    // ---- the HUD ----------------------------------------------------------------

    /// Everything the HUD shows, rebuilt from the match on every change. The HUD only
    /// sends what differs from what each viewer last saw, so this can be called freely.
    private void RefreshHud() {
        if (match == null) return;
        hud.Show(HudState());
    }

    private HudState HudState() {
        HudState state = new() {
            SecondsLeft = SecondsLeft,
            DefendingTeam = phase == SiegePhase.Picking || phase == SiegePhase.Playing ? DefendedSite : -1,
            Label = PeekSeekSiege.Instance.Translate("hud_round", Math.Max(1, round)),
        };

        if (phase == SiegePhase.Shaping) {
            state.Label = PeekSeekSiege.Instance.Translate("hud_shaping");
        } else if (phase == SiegePhase.Building) {
            state.Label = PeekSeekSiege.Instance.Translate("hud_building");
        } else if (phase == SiegePhase.Over) {
            state.Label = PeekSeekSiege.Instance.Translate("hud_final");
            state.Jeopardy = true;
        } else if (round > RoundOrder.RegulationHalf * 2) {
            state.Label = PeekSeekSiege.Instance.Translate("hud_overtime");
        } else if (RoundOrder.EndsABlock(round) && score[0] != score[1]) {
            // The last round of a block with one side ahead: this round can end it.
            state.Label = PeekSeekSiege.Instance.Translate("hud_match_point");
            state.Jeopardy = true;
        }

        for (int team = 0; team < 2; team++) {
            state.Teams[team].Name = TeamName(team);
            state.Teams[team].Score = score[team];

            if (match.Teams.Count <= team) continue;
            foreach (ulong steam64 in match.Teams[team].Players) {
                state.Teams[team].Seats.Add(Seat(steam64));
            }
        }

        return state;
    }

    private HudSeat Seat(ulong steam64) {
        Player player = PlayerTool.getPlayer(new CSteamID(steam64));
        RemoteAPI.Profile profile = RemoteAPI.ProfileFor(steam64);

        // The pick shows once it is made, and stays on the card for the round it was
        // made for: that is the "what did they bring" a teammate glances up for.
        bool picked = phase == SiegePhase.Playing || board.HasAnswered(steam64);

        return new HudSeat {
            Steam64 = steam64,
            Name = profile?.Name ?? player?.channel.owner.playerID.characterName ?? steam64.ToString(),
            AvatarUrl = profile?.Avatar ?? PeekSeekSiege.Config.DefaultAvatarURL,
            GunItemId = player == null ? (ushort)0 : SiegeWorld.PrimaryWeapon(player),
            PickItemId = picked ? board.Chosen(steam64)?.IconItemId ?? 0 : (ushort)0,
            Alive = player != null && !player.life.isDead && !deadThisRound.Contains(steam64)
        };
    }
}
