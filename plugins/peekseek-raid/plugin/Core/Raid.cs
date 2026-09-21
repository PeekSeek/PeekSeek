using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeek.Gamemodes;
using PeekSeek.Hud;
using PeekSeekRaid.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekRaid.Core;

internal enum RaidPhase {
    Idle,
    Shaping,
    Building,
    Picking,
    Starting,
    Playing,
    Over
}

/// Raid: build a base, then take turns attacking each other's.
///
/// The match is one build phase and then a run of rounds. The build phase is the only
/// time the buildings change; after that a base is a fixed thing that gets re-laid
/// before every round exactly as it was left, and the only per-round decisions are
/// what each player carries in and where the defenders stand their flag. That split
/// is the whole design: a team spends five minutes on a building and then has to both
/// hold it and crack the mirror of it, so a base that is only good to defend is a bad
/// base.
///
/// A round is three moments. Everybody picks: attackers where they come in and what
/// they bring, on a map of the base; defenders what they hold it with, and one of
/// them, once they have picked, is handed the flag and walks the base to stand it
/// where this round's defence wants it. Everybody else waits rooted in the map's own
/// lobby, far from either base. Three seconds of everybody in position, frozen, with
/// the flag up. Then the round. Nobody moves until the last of those, except the one
/// defender with the flag, and they cannot leave the base, which is what stops a pick
/// phase being a head start.
///
/// The rules that make it read as a raid rather than a deathmatch on a small map are
/// all about ground. Defenders take damage standing on dirt, so holding means holding
/// a building. Nothing in a base can be salvaged except the doors a team needs to move
/// through and the birch walls it deliberately built soft, so a round cannot be won by
/// dismantling the map.
///
/// Match keeps the roster, the groups, the score and the stats, draws the bar and
/// reports the result; this asks it for those through Host and answers its questions
/// through IGamemode.
internal class Raid : IGamemode {
    public string Name => "Raid";

    private GamemodeMatch match;
    private RaidSites sites;

    private RaidPhase phase = RaidPhase.Idle;
    private float phaseEndsAt;

    private int round;

    /// Each team's finished building, taken once at the end of the build phase.
    private readonly BaseSnapshot[] bases = new BaseSnapshot[2];

    /// The footprint each team chose to start from. Chosen on a board before the build
    /// phase; Square if nobody on the team answered.
    private readonly BaseShape[] shapes = [BaseShape.Square, BaseShape.Square];

    /// What each team has left to lay, for the build phase only.
    private readonly BuildBudget[] budgets = new BuildBudget[2];

    private static PickBoard Board => Host.Board;

    /// The defender placing this round's flag, or 0 when nobody is.
    internal ulong FlagBearer { get; private set; }

    /// Whether the bearer has been given the flag yet. They get it the moment they
    /// have picked their powerup, so the board is answered before the walk starts.
    private bool flagHanded;

    /// The piece each builder had a swap board open for, by where it stands, so an
    /// answer can find it again even if it has changed hands in the meantime.
    private readonly Dictionary<ulong, Vector3> swapping = [];

    /// The kind of piece each builder last put down, which the panel picks out: the
    /// planner never tells the server what is selected, and what you just laid is what
    /// you are laying.
    private readonly Dictionary<ulong, ushort> lastPlaced = [];

    /// When each player was last standing on something built, so the grace period
    /// before off-base damage can be measured. Absent means they are on a base now.
    private readonly Dictionary<ulong, float> offBaseSince = [];

    private float nextTick;

    /// The site being attacked this round, which is the site its owners are defending:
    /// a team defends its own base for three rounds and then goes and attacks the
    /// other team's for three.
    internal int DefendedSite => RoundOrder.Defender(round);

    /// Whether a player is on the defending side of the round being played.
    internal bool IsDefending(ulong steam64) => SideOf(steam64) == DefendedSite;

    internal RaidPhase Phase => phase;
    internal int Round => round;
    internal GamemodeMatch Match => match;

    /// Seconds left in the current phase, floored at zero.
    public int SecondsLeft => phase is RaidPhase.Idle or RaidPhase.Over
        ? -1
        : Mathf.Max(0, Mathf.CeilToInt(phaseEndsAt - Time.realtimeSinceStartup));

    public void Begin(GamemodeMatch started) {
        match = started;
        sites = new RaidSites(PeekSeekRaid.Config.Sites);
        round = 0;

        RaidWorld.Hook(this);

        Host.Announce(PeekSeekRaid.Instance.Translate("match_intro",
            RoundOrder.RegulationHalf, Wording.Duration(PeekSeekRaid.Config.BuildSeconds)));

        StartShaping();
    }

    public void End() {
        Board.Close();
        StopBuilding();
        RaidWorld.Unhook();

        foreach (Player player in Players()) {
            AllowRespawn(player);
            RaidWorld.Freeze(player, false);
            RaidWorld.Mark(player, null, null);
        }

        phase = RaidPhase.Idle;
        match = null;
    }

    public void Tick() {
        if (phase == RaidPhase.Idle || phase == RaidPhase.Over) return;

        // Everything below is per-second bookkeeping, not per-frame work: clocks,
        // the off-base check and the flag check all read the same once a second as
        // they would sixty times, and the raycast in particular is not free.
        if (Time.realtimeSinceStartup < nextTick) return;
        nextTick = Time.realtimeSinceStartup + 1f;

        switch (phase) {
            case RaidPhase.Shaping:
                TickShaping();
                break;
            case RaidPhase.Building:
                TickBuilding();
                break;
            case RaidPhase.Picking:
                TickPicking();
                break;
            case RaidPhase.Starting:
                if (SecondsLeft <= 0) StartPlaying();
                break;
            case RaidPhase.Playing:
                TickPlaying();
                break;
        }
    }

    public void OnPlayerJoined(Player player) {
        if (match == null) return;

        Host.Whisper(player.channel.owner, PeekSeekRaid.Instance.Translate("welcome",
            phase.ToString().ToLowerInvariant(), round, Host.Score(0), Host.Score(1)));

        Place(player);
    }

    public void OnPlayerLeft(CSteamID steamID) {
        offBaseSince.Remove(steamID.m_SteamID);
        swapping.Remove(steamID.m_SteamID);

        Player leaving = PlayerTool.getPlayer(steamID);
        if (leaving != null) BudgetFor(steamID.m_SteamID)?.Unwatch(leaving);
    }

    public void OnPlayerDied(Player player, CSteamID killer) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        offBaseSince.Remove(steam64);

        if (phase != RaidPhase.Playing) return;

        // Dead is dead until the round is over. Unturned would otherwise offer the
        // respawn button after its own timer and put them back on a map spawn, alive
        // and counted.
        player.setPluginWidgetFlag(EPluginWidgetFlags.ShowDeathMenu, false);

        // A death can end the round, so the check runs on the next tick rather than
        // here: the player is dead but Unturned has not finished moving them yet, and
        // counting the living from inside the death event reads the old numbers.
        nextTick = 0f;
    }

    /// Back from the dead, on Unturned's own respawn: put them where the phase says and
    /// dress them for it. Without this a respawn lands on a random map spawn with an
    /// empty inventory, which is what "the gamemode broke" looked like from the inside.
    public void OnPlayerRevived(Player player) {
        if (match == null) return;

        Place(player);
    }

    // ---- the bar ------------------------------------------------------------------

    public HudRole RoleOf(int team) {
        if (phase < RaidPhase.Picking || phase > RaidPhase.Playing) return HudRole.None;

        return team == DefendedSite ? HudRole.Holding : HudRole.Attacking;
    }

    /// The pick hangs off the face once it is made and stays there for the round it
    /// was made for: that is the "what did they bring" a teammate glances up for.
    /// Nothing hangs there before the first round; the footprint is not a pick.
    public ushort PickOf(ulong steam64) {
        bool picked = phase == RaidPhase.Playing
            || (phase >= RaidPhase.Picking && phase <= RaidPhase.Starting && Board.HasAnswered(steam64));

        return picked ? Board.Chosen(steam64)?.IconItemId ?? 0 : (ushort)0;
    }

    // ---- the footprint ----------------------------------------------------------

    /// Before anybody builds, each team says what it is building on: a board with the
    /// two footprints, up for as long as a pick. The first answer on a team is the
    /// team's; a team that never answers gets the square.
    private void StartShaping() {
        phase = RaidPhase.Shaping;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.PickSeconds;

        foreach (int team in BuiltSites()) {
            BaseSnapshot.Clear(sites[team].AnchorPoint, sites[team].BuildRadius, []);
        }
        foreach (Player player in Players()) Place(player);

        Board.Reset();
        List<BoardOption> footprints = ShapeOptions();
        foreach (Player player in Players()) {
            Board.Open(player.channel.owner, footprints, PeekSeekRaid.Instance.Translate("board_shape"), BoardFace.Shape);
        }

        Host.RefreshHud();
    }

    private void TickShaping() {
        Board.Countdown(SecondsLeft);

        bool everyoneAnswered = Players().All(p => Board.HasAnswered(p.channel.owner.playerID.steamID.m_SteamID));
        if (SecondsLeft > 0 && !everyoneAnswered) return;

        FinishShaping();
    }

    internal void FinishShaping() {
        for (int team = 0; team < 2; team++) shapes[team] = ShapeChosenBy(team);
        Board.Close();
        StartBuilding();
    }

    /// The two footprints as tiles, in BaseShape order, which is the order the board's
    /// pictures are baked in. The first answer on a team decides for the team.
    private static List<BoardOption> ShapeOptions() {
        return [
            new BoardOption {
                Name = PeekSeekRaid.Instance.Translate("shape_square"),
                Blurb = PeekSeekRaid.Instance.Translate("shape_square_blurb")
            },
            new BoardOption {
                Name = PeekSeekRaid.Instance.Translate("shape_hexagonal"),
                Blurb = PeekSeekRaid.Instance.Translate("shape_hexagonal_blurb")
            }
        ];
    }

    private BaseShape ShapeChosenBy(int team) {
        if (match.Teams.Count <= team) return BaseShape.Square;

        foreach (ulong steam64 in match.Teams[team].Players) {
            int pick = Board.PickIndex(steam64);
            if (pick >= 0) return (BaseShape)pick;
        }

        return BaseShape.Square;
    }

    // ---- build phase ----------------------------------------------------------

    private void StartBuilding() {
        phase = RaidPhase.Building;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.BuildSeconds;

        for (int team = 0; team < 2; team++) {
            int captured = team;
            budgets[team] = new BuildBudget(SuppliesFor(ShapeAt(team)));
            budgets[team].Changed += () => ShowPanel(captured);
        }

        foreach (int team in BuiltSites()) {
            RaidSite site = sites[team];
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
            RaidWorld.PlaceAnchor(team, site, shapes[team]);
            RaidWorld.PlacePillars(site, GroupOf(team), shapes[team]);
        }

        foreach (Player player in Players()) Place(player);

        Host.RefreshHud();
    }

    /// The team's whole kit: the pieces every base gets, and the triangles a
    /// hexagonal one needs on top.
    private static IEnumerable<RaidItem> SuppliesFor(BaseShape shape) {
        IEnumerable<RaidItem> supplies = PeekSeekRaid.Config.BuildSupplies;
        return shape == BaseShape.Square ? supplies : supplies.Concat(PeekSeekRaid.Config.TriangleSupplies);
    }

    private void TickBuilding() {
        Leash();

        if (SecondsLeft > 0) return;
        FinishBuilding();
    }

    /// Freeze what has been built and move on.
    internal void FinishBuilding() {
        foreach (int team in BuiltSites()) bases[team] = Snapshot(team);

        StopBuilding();
        StartRound(1);
    }

    private void StopBuilding() {
        Board.Close();
        swapping.Clear();
        lastPlaced.Clear();

        foreach (BuildBudget budget in budgets) budget?.UnwatchAll();
        foreach (Player player in Players()) Host.Panel.Hide(player.channel.owner);
    }

    /// The budget a player builds from: their team's.
    internal BuildBudget BudgetFor(ulong steam64) => budgets[SideOf(steam64)];

    /// A builder put a piece down: one off the team's pile, and the panel picks out
    /// that kind for them from now on.
    internal void Placed(ulong steam64, ushort id) {
        lastPlaced[steam64] = id;
        if (!BudgetFor(steam64).Spend(id)) ShowPanel(SideOf(steam64));
    }

    private void ShowPanel(int team) {
        BuildBudget budget = budgets[team];
        if (budget == null || phase != RaidPhase.Building) return;

        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (SideOf(steam64) != team) continue;

            Host.Panel.Show(player.channel.owner, budget.Counts.Rows, lastPlaced.TryGetValue(steam64, out ushort laying) ? laying : (ushort)0);
        }
    }

    /// A builder salvaged one of their team's pieces: the salvage was refused, and
    /// this is the board in its place, the same piece in every other material and a
    /// way to take it down.
    internal void OfferSwap(SteamPlayer viewer, StructureDrop drop, int site) {
        if (phase != RaidPhase.Building) return;

        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        BuildBudget budget = budgets[site];
        ItemStructureAsset current = drop.asset;

        List<BoardOption> options = RaidWorld.Variants(current).Select(variant => new BoardOption {
            Name = variant.itemName,
            Blurb = PeekSeekRaid.Instance.Translate(
                budget.Counts.Has(variant.id) ? "swap_left" : "swap_none", budget.Counts.Left(variant.id), variant.health),
            Icon = variant.id
        }).ToList();

        // A starting floor can change material but never go: the site is the floors.
        if (!RaidWorld.IsAnchorTile(site, drop)) {
            options.Add(new BoardOption {
                Name = PeekSeekRaid.Instance.Translate("swap_remove"),
                Blurb = PeekSeekRaid.Instance.Translate("swap_remove_blurb")
            });
        }

        swapping[steam64] = drop.GetServersideData().point;
        Board.Open(viewer, options, current.itemName, BoardFace.Swap, taken => Swap(viewer, taken));
    }

    /// Carry a swap out. The piece is found again by where it stands, because the
    /// board may have been up for a while and a teammate may have salvaged it
    /// meanwhile; a piece that is no longer there is nothing to swap.
    private void Swap(SteamPlayer viewer, BoardOption taken) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (phase != RaidPhase.Building || !swapping.TryGetValue(steam64, out Vector3 at)) return;
        swapping.Remove(steam64);

        StructureDrop drop = RaidWorld.StructureAt(at);
        if (drop == null) return;

        BuildBudget budget = BudgetFor(steam64);
        ushort was = drop.asset.id;

        if (taken.Icon == 0) {
            RaidWorld.Remove(drop);
            budget.Refund(was);
            return;
        }

        if (Assets.find(EAssetType.ITEM, taken.Icon) is not ItemStructureAsset to) return;
        if (!budget.Spend(to.id)) {
            Host.Whisper(viewer, PeekSeekRaid.Instance.Translate("swap_none_left", to.itemName));
            return;
        }

        RaidWorld.Swap(drop, to);
        budget.Refund(was);
    }

    /// Every team builds its own base, on its own site.
    private IEnumerable<int> BuiltSites() {
        return Enumerable.Range(0, sites.Count);
    }

    private BaseSnapshot BaseFor(int team) {
        return bases.Length > team ? bases[team] : null;
    }

    private BaseShape ShapeAt(int team) {
        return shapes[team];
    }

    /// The Unturned group a site's team is in, or 0 before the roster has one.
    private ulong GroupOf(int team) {
        return match != null && match.Teams.Count > team ? match.Teams[team].GroupId : 0;
    }

    /// Everything standing on the plot, starting floors included: they may have
    /// changed material during the build, and a round has to put back what was there.
    private BaseSnapshot Snapshot(int team) {
        RaidSite site = sites[team];
        BaseSnapshot taken = BaseSnapshot.Capture(site.AnchorPoint, site.BuildRadius, []);
        Logger.Log($"Captured {taken.Count} piece(s) for {site.Name}.");
        return taken;
    }

    // ---- rounds ---------------------------------------------------------------

    private void StartRound(int number) {
        // A side that has gone home between rounds has forfeited; a wipe a second
        // into every round until the score says so would be the same result, slower.
        int alone = Host.TeamLeftAlone();
        if (alone >= 0) {
            Host.Forfeit(alone);
            return;
        }

        round = number;
        flagHanded = false;
        Host.BeginRound();

        RestoreBases();

        // Everything a round left on the ground: the loot a death dropped, the wall
        // somebody never placed. Raid has no world loot, so there is nothing to keep.
        ItemManager.askClearAllItems();

        phase = RaidPhase.Picking;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.PickSeconds;

        // Whoever died last round comes back now, and everybody is put where the round
        // says they belong before the board goes up, so nobody spends the pick phase
        // standing in a base that is being re-laid around them. The bearer is chosen
        // first, because where a defender is put depends on whether it is them.
        FlagBearer = ChooseFlagBearer();
        foreach (Player player in Players()) {
            if (player.life.isDead) {
                AllowRespawn(player);
                player.life.ServerRespawn(false);
                continue;
            }
            Place(player);
        }

        // With nobody to walk it, the flag goes on a free tile now rather than at the
        // end of a pick nobody is spending on it.
        if (FlagBearer == 0) StandFlag();

        OpenBoard(DefendedSite);
        Host.RefreshHud();
    }

    /// Put both buildings back exactly as they were built, then hand the pieces out
    /// according to the rules that make a round playable. This runs before every round
    /// including the first, so round one and round six start from identical ground.
    /// Last round's flag goes with everything else; this round's is placed fresh.
    private void RestoreBases() {
        for (int team = 0; team < sites.Count; team++) {
            RaidSite site = sites[team];
            ulong group = GroupOf(team);

            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
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
    /// Everything else, metal walls, every floor, every pillar, stays unowned, so a
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
        List<BoardOption> defence = PeekSeekRaid.Config.DefenceOptions.Select(option => option.ToBoard()).ToList();
        List<BoardOption> raid = PeekSeekRaid.Config.RaidOptions.Select(option => option.ToBoard()).ToList();
        RaidSite attacked = sites[defender];

        Board.Reset();

        // One board each, because the two sides are choosing from different lists at the
        // same moment.
        foreach (Player player in Players()) {
            SteamPlayer viewer = player.channel.owner;
            if (IsDefending(viewer.playerID.steamID.m_SteamID)) {
                Board.Open(viewer, defence, PeekSeekRaid.Instance.Translate("board_defend"));
            } else {
                Board.OpenAttack(viewer, raid, defender, attacked.AttackerSpawns.Count);
            }
        }
    }

    /// The pick phase ends when the last person has picked and the flag is up, not
    /// when the clock says. When the clock says, whatever is unanswered is answered
    /// for them, and a flag still in somebody's hands goes on a free tile.
    private void TickPicking() {
        Board.Countdown(SecondsLeft);
        WalkTheFlag();

        bool everyoneAnswered = Players().All(p => Board.HasAnswered(p.channel.owner.playerID.steamID.m_SteamID));
        bool flagUp = RaidWorld.MarkerStands(sites[DefendedSite]);
        if (SecondsLeft > 0 && !(everyoneAnswered && flagUp)) return;

        if (!flagUp) StandFlag();
        StartCountdown();
    }

    /// The bearer's part of the pick. Once they have answered their board they are
    /// handed the flag and let go, and from then on they are kept on the base: step
    /// off it and they are back on the anchor. The other team is rooted in the lobby,
    /// so the leash is not about peeking; it is that a bearer who wanders off with the
    /// flag has taken the round's objective for a walk.
    private void WalkTheFlag() {
        if (FlagBearer == 0) return;

        Player bearer = PlayerTool.getPlayer(new CSteamID(FlagBearer));
        if (bearer == null || bearer.life.isDead) return;

        if (!flagHanded) {
            if (!Board.HasAnswered(FlagBearer)) return;

            flagHanded = true;
            RaidWorld.HandFlag(bearer);
            RaidWorld.Freeze(bearer, false);
            Host.Whisper(bearer.channel.owner, PeekSeekRaid.Instance.Translate("flag_bearer", SecondsLeft));
            Host.RefreshHud();
            return;
        }

        RaidSite site = sites[DefendedSite];
        if (site.Contains(bearer.transform.position)) return;

        bearer.teleportToLocationUnsafe(RaidWorld.FreeSpot(DefendedSite, site, 0), site.Yaw);
        Host.Whisper(bearer.channel.owner, PeekSeekRaid.Instance.Translate("flag_leashed"));
    }

    /// One of the defenders present, at random. Random rather than in turn because a
    /// team that knows who is placing next round can plan the placement in the round
    /// before, and the point of the flag is that every round's defence is decided in
    /// its own twenty seconds.
    private ulong ChooseFlagBearer() {
        int defender = DefendedSite;
        if (match.Teams.Count <= defender) return 0;

        // Whoever is on the server counts, dead or not: the dead are respawned before
        // anybody is placed for the pick.
        List<ulong> present = match.Teams[defender].Players
            .Where(steam64 => PlayerTool.getPlayer(new CSteamID(steam64)) != null)
            .ToList();
        if (present.Count == 0) return 0;

        return present[UnityEngine.Random.Range(0, present.Count)];
    }

    private void StandFlag() {
        int defender = DefendedSite;
        RaidWorld.PlaceFlag(defender, sites[defender], ShapeAt(defender), GroupOf(defender), TeamName(defender));
    }

    /// Three seconds with the flag up and everybody rooted in position, so the round
    /// starts on a count rather than on whoever noticed first. This is the moment the
    /// lobby empties: defenders onto their building, attackers onto the spawn they
    /// chose, facing it.
    private void StartCountdown() {
        Board.Close();
        phase = RaidPhase.Starting;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.StartSeconds;
        FlagBearer = 0;
        flagHanded = false;

        foreach (Player player in Players()) Place(player);

        Host.RefreshHud();
    }

    private void StartPlaying() {
        phase = RaidPhase.Playing;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.RoundSeconds;
        offBaseSince.Clear();

        foreach (Player player in Players()) {
            RaidWorld.Kit(player, ChosenOption(player.channel.owner.playerID.steamID.m_SteamID));
            RaidWorld.Freeze(player, false);
        }

        Host.RefreshHud();
    }

    /// The option a player took into this round, from the list their side was shown.
    /// The board defaults an unanswered player to the first tile, so this does too.
    private RaidOption ChosenOption(ulong steam64) {
        List<RaidOption> offered = IsDefending(steam64) ? PeekSeekRaid.Config.DefenceOptions : PeekSeekRaid.Config.RaidOptions;
        if (offered.Count == 0) return null;

        return offered[Math.Min(Math.Max(0, Board.PickIndex(steam64)), offered.Count - 1)];
    }

    private void TickPlaying() {
        PunishOffBase();

        int defender = DefendedSite;

        // The flag is the objective. Attackers win by bringing it down; defenders win by
        // still having it up when the clock runs out.
        if (!RaidWorld.MarkerStands(sites[defender])) {
            EndRound(1 - defender, "round_won_flag");
            return;
        }

        if (SecondsLeft <= 0) {
            EndRound(defender, "round_won_clock");
            return;
        }

        if (Living(defender) == 0) {
            EndRound(1 - defender, "round_won_wipe");
            return;
        }
        if (Living(1 - defender) == 0) {
            EndRound(defender, "round_won_wipe");
        }
    }

    /// Award the round and either start the next one or call the match. The score is
    /// Match's; the block arithmetic that says when the match is over is this
    /// gamemode's, because a Raid match is not first to N.
    internal void EndRound(int winner, string reasonKey) {
        if (phase < RaidPhase.Picking || phase > RaidPhase.Playing) return;

        foreach (Player player in Players()) RaidWorld.Mark(player, null, null);

        Host.RoundWon(winner, PeekSeekRaid.Instance.Translate(reasonKey, TeamName(winner), round));

        int matchWinner = RoundOrder.Winner(round, Host.Score(0), Host.Score(1));
        if (matchWinner >= 0) {
            phase = RaidPhase.Over;
            Board.Close();
            foreach (Player player in Players()) RaidWorld.Freeze(player, false);
            Host.MatchWon(matchWinner);
            return;
        }

        StartRound(round + 1);
    }

    // ---- the rules that hold a round together ---------------------------------

    /// Hurt defenders who are not standing on their building.
    ///
    /// The grace period matters more than the damage does. A defender vaults a
    /// windowsill, drops through a hatch, lands a jump slightly short, or steps out to
    /// meet somebody at the door, and in every one of those they are briefly on dirt
    /// while doing exactly what a defender is meant to do. Three seconds is long
    /// enough to cover all of it and short enough that nobody crosses open ground on
    /// it.
    private void PunishOffBase() {
        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (!IsDefending(steam64) || player.life.isDead) continue;

            if (RaidWorld.IsStandingOnBuilt(player)) {
                offBaseSince.Remove(steam64);
                continue;
            }

            if (!offBaseSince.TryGetValue(steam64, out float since)) {
                offBaseSince[steam64] = Time.realtimeSinceStartup;
                continue;
            }

            float off = Time.realtimeSinceStartup - since;
            if (off < PeekSeekRaid.Config.OffBaseGraceSeconds) continue;

            player.life.askDamage(
                PeekSeekRaid.Config.OffBaseDamagePerSecond,
                Vector3.up,
                EDeathCause.ARENA,
                ELimb.SPINE,
                CSteamID.Nil,
                out EPlayerKill _);

            // Said once, as the damage starts. Every second after that the health bar
            // is saying it, and a line of chat per second is noise nobody reads.
            if (off < PeekSeekRaid.Config.OffBaseGraceSeconds + 1f) {
                Host.Whisper(player.channel.owner, PeekSeekRaid.Instance.Translate("off_base"));
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
            RaidSite site = sites[SideOf(player.channel.owner.playerID.steamID.m_SteamID)];
            if (site.Contains(player.transform.position)) continue;

            player.teleportToLocationUnsafe(RaidWorld.FreeSpot(SideOf(player.channel.owner.playerID.steamID.m_SteamID), site, 0), site.Yaw);
            Host.Whisper(player.channel.owner, PeekSeekRaid.Instance.Translate("leashed"));
        }
    }

    /// Put one player where the current phase says they belong, kit them for it, and
    /// root them if the phase is one nobody moves in.
    private void Place(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        int side = SideOf(steam64);
        RaidSite own = sites[side];

        // A salvage is the swap while building, so it is quick; in a round it is the
        // soft wall coming down, which should cost a moment.
        RaidWorld.SalvageTime(player, phase == RaidPhase.Building
            ? PeekSeekRaid.Config.BuildSalvageSeconds
            : PeekSeekRaid.Config.RoundSalvageSeconds);

        // The base being taken, on every attacker's map and compass from the moment
        // they are put down for the round until it is over; nobody else has a marker.
        bool attacking = phase is RaidPhase.Starting or RaidPhase.Playing && !IsDefending(steam64);
        RaidWorld.Mark(player, attacking ? sites[DefendedSite].AnchorPoint : null, sites[DefendedSite].Name);

        switch (phase) {
            case RaidPhase.Shaping:
                player.teleportToLocationUnsafe(RaidWorld.OnGround(own.AnchorPoint), own.Yaw);
                RaidWorld.DressPlain(player);
                RaidWorld.Freeze(player, true);
                return;

            case RaidPhase.Building:
                player.teleportToLocationUnsafe(RaidWorld.FreeSpot(side, own, IndexInTeam(steam64)), own.Yaw);
                RaidWorld.DressForBuilding(player);
                RaidWorld.Freeze(player, false);
                BudgetFor(steam64).Watch(player);
                Host.Panel.Show(player.channel.owner, BudgetFor(steam64).Counts.Rows, 0);
                return;

            // The bearer waits on the base for their flag; everybody else waits in the
            // map's lobby, where there is nothing to see and nowhere to be early.
            case RaidPhase.Picking:
                if (steam64 == FlagBearer) {
                    RaidSite defended = sites[DefendedSite];
                    player.teleportToLocationUnsafe(RaidWorld.FreeSpot(DefendedSite, defended, 0), defended.Yaw);
                    if (flagHanded) RaidWorld.HandFlag(player);
                    else RaidWorld.DressPlain(player);
                    RaidWorld.Freeze(player, !flagHanded);
                    return;
                }

                Wait(player, side);
                RaidWorld.DressPlain(player);
                RaidWorld.Freeze(player, true);
                return;

            case RaidPhase.Starting:
                Spawn(player, steam64, side);
                RaidWorld.DressPlain(player);
                RaidWorld.Freeze(player, true);
                return;

            case RaidPhase.Playing:
                Spawn(player, steam64, side);
                RaidWorld.Kit(player, ChosenOption(steam64));
                RaidWorld.Freeze(player, false);
                return;

            default:
                RaidWorld.Freeze(player, false);
                return;
        }
    }

    /// A round's spawn: defenders on their building, attackers on the point they chose,
    /// facing the base, spread a step apart when several chose the same one.
    private void Spawn(Player player, ulong steam64, int side) {
        RaidSite defended = sites[DefendedSite];

        if (IsDefending(steam64)) {
            player.teleportToLocationUnsafe(RaidWorld.FreeSpot(DefendedSite, defended, IndexInTeam(steam64)), defended.Yaw);
            return;
        }

        int spawn = Math.Max(0, Board.SpawnChosen(steam64));
        List<ulong> same = Roster(side).Where(mate => Math.Max(0, Board.SpawnChosen(mate)) == spawn).ToList();
        int index = Math.Max(0, same.IndexOf(steam64));

        Vector3 at = RaidWorld.OnGround(defended.AttackerSpawn(spawn, index, Math.Max(1, same.Count)));
        player.teleportToLocationUnsafe(at, defended.YawTowardsAnchor(at.x, at.z));
    }

    /// Put a player in the map's lobby for the length of a pick, each on their own
    /// spawn point of it. A map without a lobby has nowhere else to wait but the plot.
    private void Wait(Player player, int side) {
        int index = Math.Max(0, Players().ToList().IndexOf(player));
        if (RaidWorld.LobbySpot(index, out Vector3 point, out float angle)) {
            player.teleportToLocationUnsafe(point, angle);
            return;
        }

        RaidSite own = sites[side];
        player.teleportToLocationUnsafe(RaidWorld.FreeSpot(side, own, 0), own.Yaw);
    }

    private static void AllowRespawn(Player player) {
        player.setPluginWidgetFlag(EPluginWidgetFlags.ShowDeathMenu, true);
    }

    // ---- who is who -----------------------------------------------------------

    /// Which side a player is on.
    internal int SideOf(ulong steam64) {
        if (match == null) return 0;

        int index = match.TeamIndexOf(steam64);
        return index < 0 ? 0 : index;
    }

    internal string TeamName(int team) {
        return match.Teams.Count > team ? match.Teams[team].Name : sites[team].Name;
    }

    private List<ulong> Roster(int team) {
        return match.Teams.Count > team ? match.Teams[team].Players : [];
    }

    private int IndexInTeam(ulong steam64) {
        return Math.Max(0, Roster(SideOf(steam64)).IndexOf(steam64));
    }

    private int Living(int team) {
        if (match.Teams.Count <= team) return 0;

        return match.Teams[team].Players.Count(steam64 => {
            Player player = PlayerTool.getPlayer(new CSteamID(steam64));
            return player != null && !player.life.isDead && Host.IsAlive(steam64);
        });
    }

    private static IEnumerable<Player> Players() => Host.Players();
}
