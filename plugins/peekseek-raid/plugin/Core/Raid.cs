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

    /// What each player took into this round, settled when the pick ends: their own
    /// answer, or a powerup nobody on their team had, at random.
    private readonly Dictionary<ulong, RaidOption> chosen = [];

    /// A round that has been decided but not yet ended: the flag is down or a side is
    /// gone, and the round ends RoundEndDelay later rather than on the same frame, so
    /// nobody is teleported out of the moment they won or lost it.
    private int pendingWinner = -1;
    private float pendingAt;

    /// Kills each player made this round, for the ace line.
    private readonly Dictionary<ulong, int> roundKills = [];

    /// Who last hit the flag, which is who brought it down when it goes.
    private ulong flagBreaker;

    /// Whether this round's attackers have had their half-time charges.
    private bool halfTimeGiven;

    private readonly System.Random random = new();

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

        if (pendingWinner >= 0 && Time.realtimeSinceStartup >= pendingAt) {
            AfterRound();
            return;
        }

        // Everything below is per-second bookkeeping, not per-frame work: clocks,
        // the off-base check and the flag check all read the same once a second as
        // they would sixty times, and the raycast in particular is not free.
        if (Time.realtimeSinceStartup < nextTick) return;
        nextTick = Time.realtimeSinceStartup + 1f;

        if (phase is RaidPhase.Picking or RaidPhase.Playing) Powerups.Tick();

        // Teammates' pings on the bar while a team is choosing or building, never in a
        // fight.
        Host.ShowPings(phase is RaidPhase.Shaping or RaidPhase.Building or RaidPhase.Picking);

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
            case RaidPhase.Playing:
                TickPlaying();
                break;
        }
    }

    public void OnPlayerJoined(Player player) {
        if (match == null) return;

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

        Host.Tag.Hide(player.channel.owner);
        if (phase != RaidPhase.Playing) return;
        if (killer.m_SteamID != 0 && killer.m_SteamID != steam64 && SideOf(killer.m_SteamID) != SideOf(steam64)) {
            roundKills[killer.m_SteamID] = roundKills.TryGetValue(killer.m_SteamID, out int kills) ? kills + 1 : 1;
        }

        // Dead is dead until the round is over: Match remembers, whatever the game
        // does with the body. So the body comes straight back and Place puts it in
        // the map's lobby, where they keep the bar, the feed and the chat for the
        // rest of the round instead of a death screen with nothing on it.
        player.life.ServerRespawn(false);

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
    /// Nothing hangs there before the first round; the footprint is not a pick. The
    /// one defender walking the flag carries a flag there until they have picked, so
    /// the team knows who it is before anybody says.
    public ushort PickOf(ulong steam64) {
        if (phase == RaidPhase.Picking) return Board.HasAnswered(steam64) ? Board.Chosen(steam64)?.IconItemId ?? 0 : (ushort)0;
        if (phase == RaidPhase.Playing) return ChosenOption(steam64)?.IconItemId ?? 0;
        return 0;
    }


    /// The build is the one phase that needs its name over the clock: the clock alone
    /// reads as a round.
    public string PhaseLabel => phase == RaidPhase.Building ? "BUILD" : null;

    /// Whether winning the round in play would end the match, by the same block
    /// arithmetic that ends it.
    public bool IsMatchPoint(int team) {
        if (round == 0) return false;

        int zero = Host.Score(0) + (team == 0 ? 1 : 0);
        int one = Host.Score(1) + (team == 1 ? 1 : 0);
        return RoundOrder.Winner(round, zero, one) == team;
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
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            Board.Open(player.channel.owner, footprints, PeekSeekRaid.Instance.Translate("board_shape"), BoardFace.Shape,
                       _ => StampTeam(steam64));
        }

        Host.RefreshHud();
    }

    /// The first answer on a team counts, so the moment one lands the rest of the
    /// team's boards are answered with it and come down, rather than staying up to
    /// take an answer that would not have counted.
    private void StampTeam(ulong chooser) {
        int index = Board.PickIndex(chooser);
        if (index < 0) return;

        foreach (ulong mate in Roster(SideOf(chooser))) {
            if (mate != chooser) Board.Stamp(mate, index);
        }
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
            budgets[team] = new BuildBudget(TeamSize.ForEnemies(SuppliesFor(ShapeAt(team)), PlayersOn(1 - team), ExtraPerEnemy));
            budgets[team].Changed += () => ShowPanel(captured);
        }

        foreach (int team in BuiltSites()) {
            RaidSite site = sites[team];
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
            RaidWorld.PlaceAnchor(team, site, shapes[team]);
            RaidWorld.PlacePillars(site, GroupOf(team), shapes[team]);
        }

        foreach (Player player in Players()) Place(player);

        Host.Banner("BUILD", Wording.Duration(PeekSeekRaid.Config.BuildSeconds), "Salvage a wall or roof to change its shape");
        Host.RefreshHud();
    }

    /// The team's whole kit: the square kit, or the smaller hexagonal one with its
    /// triangles.
    private static IEnumerable<RaidItem> SuppliesFor(BaseShape shape) {
        PeekSeekRaidConfiguration config = PeekSeekRaid.Config;
        return shape == BaseShape.Square
            ? config.BuildSupplies
            : TrianglePiece.HexagonalKit(config.BuildSupplies, config.TriangleSupplies, config.HexagonSquareShare, config.HexagonTriangleShare);
    }

    /// How much of a piece every raider past the first is worth, off the config: metal,
    /// wood, or a doorway of either.
    private static double ExtraPerEnemy(ushort id) {
        (string Material, string Kind)? piece = Materials.Split((Assets.find(EAssetType.ITEM, id) as ItemAsset)?.itemName);
        if (piece == null) return 0;
        if (piece.Value.Kind == "Doorway") return PeekSeekRaid.Config.DoorwayPerEnemy;

        return piece.Value.Material == "Metal" ? PeekSeekRaid.Config.MetalPerEnemy : PeekSeekRaid.Config.WoodPerEnemy;
    }

    private int PlayersOn(int team) => match.Teams.Count > team ? match.Teams[team].Players.Count : 1;

    private void TickBuilding() {
        Leash();
        ShowBreach();

        if (SecondsLeft > 0) return;
        FinishBuilding();
    }

    /// What it costs to reach the flag from outside, for each team's builders, at the
    /// dearest of their starting tiles, since the flag can go on any of them.
    private void ShowBreach() {
        float limit = PeekSeekRaid.Config.BreachLimit;
        for (int team = 0; team < 2; team++) {
            float cost = BreachCheck.WorstTile(RaidWorld.AnchorTiles(team));
            string line = $"{cost:0} / {limit:0}";
            foreach (ulong steam64 in Roster(team)) {
                if (PlayerTool.getSteamPlayer(new CSteamID(steam64)) is { } builder) Host.Tag.Show(builder, "BREACH", -1, line);
            }
        }
    }

    /// A metal door went up or came down: WallsPerDoor metal walls come with it, and go
    /// with it as far as the pile still has them.
    internal void DoorsChanged(ulong steam64, int doors) {
        if (phase != RaidPhase.Building) return;

        BuildBudget budget = BudgetFor(steam64);
        ushort wall = PeekSeekRaid.Config.DoorWallItemId;
        for (int i = 0; i < Math.Abs(doors) * PeekSeekRaid.Config.WallsPerDoor; i++) {
            if (doors > 0) budget.Refund(wall);
            else budget.Spend(wall);
        }
    }

    /// Freeze what has been built and move on. First every base is brought to the
    /// breach limit, so the attackers can always get in, and its team told what changed.
    internal void FinishBuilding() {
        // A phantom team built nothing, so it gets a plain walled base to be raided.
        for (int team = 0; team < 2; team++) {
            if (IsPhantomTeam(team)) RaidWorld.BuildPhantomBase(team, sites[team], shapes[team], GroupOf(team));
        }

        for (int team = 0; team < 2; team++) {
            List<string> changed = BreachCheck.Enforce(RaidWorld.AnchorTiles(team));
            if (changed.Count == 0) continue;

            string line = PeekSeekRaid.Instance.Translate("breach_downgraded", string.Join(", ", changed));
            foreach (ulong steam64 in Roster(team)) {
                if (PlayerTool.getSteamPlayer(new CSteamID(steam64)) is { } player) Host.Whisper(player, line);
            }
        }

        foreach (Player player in Players()) Host.Tag.Hide(player.channel.owner);
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

    /// A builder salvaged one of their team's pieces; the game's own salvage was
    /// refused and this is what it means instead. With a metal wall in hand, a wood wall
    /// becomes metal, one metal wall off the pile and the wood wall not back on it. A
    /// piece with other forms opens a board of them, free, plus taking it down. Anything
    /// else just comes down and goes back on the pile.
    internal void Salvage(SteamPlayer viewer, StructureDrop drop, int site) {
        if (phase != RaidPhase.Building) return;

        BuildBudget budget = budgets[site];
        ushort was = drop.asset.id;

        if (RaidWorld.IsWoodWall(was) && RaidWorld.HeldMetalWall(viewer.player) is { } metal) {
            if (!budget.Spend(metal.id)) {
                Host.Whisper(viewer, PeekSeekRaid.Instance.Translate("swap_none_left", metal.itemName));
                return;
            }
            RaidWorld.Swap(drop, metal);
            return;
        }

        PieceFamily family = PieceFamily.Of(PeekSeekRaid.Config.PieceFamilies, was);
        if (family == null || family.Forms.Count < 2) {
            TakeDown(drop, budget);
            return;
        }

        List<BoardOption> options = family.Forms
            .Where(form => form != was)
            .Select(form => Assets.find(EAssetType.ITEM, form) as ItemStructureAsset)
            .Where(form => form != null)
            .Select(form => new BoardOption {
                Name = form.itemName,
                Blurb = PeekSeekRaid.Instance.Translate("swap_form", form.health),
                Icon = form.id
            }).ToList();

        options.Add(new BoardOption {
            Name = PeekSeekRaid.Instance.Translate("swap_remove"),
            Blurb = PeekSeekRaid.Instance.Translate("swap_remove_blurb")
        });

        swapping[viewer.playerID.steamID.m_SteamID] = drop.GetServersideData().point;
        Board.Open(viewer, options, drop.asset.itemName, BoardFace.Swap, taken => Swap(viewer, taken));
    }

    /// Carry a choice off the board out. The piece is found again by where it stands,
    /// because the board may have been up for a while and a teammate may have taken it
    /// down meanwhile; a piece that is no longer there is nothing to change.
    private void Swap(SteamPlayer viewer, BoardOption taken) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (phase != RaidPhase.Building || !swapping.TryGetValue(steam64, out Vector3 at)) return;
        swapping.Remove(steam64);

        StructureDrop drop = RaidWorld.StructureAt(at);
        if (drop == null) return;

        if (taken.Icon == 0) {
            TakeDown(drop, BudgetFor(steam64));
            return;
        }

        PieceFamily family = PieceFamily.Of(PeekSeekRaid.Config.PieceFamilies, drop.asset.id);
        if (family == null || !family.Forms.Contains(taken.Icon)) return;
        if (Assets.find(EAssetType.ITEM, taken.Icon) is not ItemStructureAsset to) return;

        RaidWorld.Swap(drop, to);
    }

    /// Take a piece down while building: what it counts as goes back on the pile, if the
    /// pile has it at all. The pillars the site stands are nobody's budget.
    private static void TakeDown(StructureDrop drop, BuildBudget budget) {
        ushort refund = PieceFamily.RefundFor(PeekSeekRaid.Config.PieceFamilies, drop.asset.id);
        RaidWorld.Remove(drop);
        if (budget.Counts.Ids.Contains(refund)) budget.Refund(refund);
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

    /// The group attacking this round, whose scaffolds are fair game to defenders.
    internal ulong AttackingGroup => GroupOf(1 - DefendedSite);

    /// The group holding the base this round.
    internal ulong DefendingGroup => GroupOf(DefendedSite);

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
        // Nobody left at all is not a side that stayed: the match is released, rather
        // than played out round after round to an empty server.
        if (!Host.Players().Any()) {
            Host.Abandon();
            return;
        }
        int alone = Host.TeamLeftAlone();
        if (alone >= 0) {
            Host.Forfeit(alone);
            return;
        }

        round = number;
        chosen.Clear();
        pendingWinner = -1;
        roundKills.Clear();
        flagBreaker = 0;
        halfTimeGiven = false;
        Host.BeginRound();

        // The only two moments the shape of the match changes. Said once each.
        if (number == RoundOrder.RegulationHalf + 1) {
            Host.Banner("SIDES SWAP", $"{TeamName(DefendedSite)} defends");
        } else if (number == RoundOrder.RegulationHalf * 2 + 1) {
            Host.Banner("OVERTIME", "Two rounds each");
        }

        Powerups.RoundOver();
        RestoreBases();

        // The whole map back as it was, the way the game resets an arena between rounds
        // (LevelManager.arenaClear): the loot a death dropped, a smoke still hanging,
        // every tree and object shot down. The bases are re-laid just above.
        ItemManager.askClearAllItems();
        EffectManager.askEffectClearAll();
        ObjectManager.askClearAllObjects();
        ResourceManager.askClearAllResources();

        phase = RaidPhase.Picking;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.PickSeconds;

        // Whoever died last round comes back now, and everybody is put where the round
        // says they belong before the board goes up, so nobody spends the pick phase
        // standing in a base that is being re-laid around them. The bearer is chosen
        // first, because where a defender is put depends on whether it is them.
        FlagBearer = ChooseFlagBearer();
        foreach (Player player in Players()) {
            if (player.life.isDead) {
                player.life.ServerRespawn(false);
                continue;
            }
            Place(player);
        }

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
    /// Unowned is the default and it is the important one: a piece nobody owns cannot be
    /// salvaged by anybody, so the only way through it is to break it. Doors and hatches
    /// go to the team because an unowned door is a door nobody can open. Wood goes to
    /// the team too: a defender can pull a wood wall, roof or doorway mid-round to open
    /// an angle that was not there, but it is gone once pulled. Metal, every floor and
    /// every pillar stay unowned, so a base cannot be dismantled into a better base while
    /// a round is running.
    internal static ulong OwnerGroupFor(ItemAsset asset, ulong teamGroup) {
        if (asset is ItemBarricadeAsset barricade) {
            return barricade.build == EBuild.DOOR || barricade.build == EBuild.HATCH
                ? teamGroup
                : 0;
        }

        return asset is ItemStructureAsset structure && IsSoftPiece(structure) ? teamGroup : 0;
    }

    /// A soft piece is wood, and not a floor or a pillar. Floors and pillars hold the
    /// building up, so letting a team salvage those would let them drop their own roof
    /// on an attacker, or delete the floor an attacker is about to stand on.
    internal static bool IsSoftPiece(ItemStructureAsset asset) {
        if (asset.construct is EConstruct.FLOOR or EConstruct.FLOOR_POLY or EConstruct.PILLAR or EConstruct.POST) return false;

        return Materials.Split(asset.itemName)?.Material is "Birch" or "Pine" or "Maple";
    }

    /// Every powerup a side has, one board each, since the two sides choose from
    /// different lists at the same moment. A powerup has one taker a team: whoever takes
    /// it first, and it is theirs until they take another. A tile a teammate holds is
    /// greyed with their face on it, so a team splits its picks without a word.
    private void OpenBoard(int defender) {
        List<BoardOption> defence = Tiles(PeekSeekRaid.Config.DefenceOptions);
        List<BoardOption> raid = Tiles(PeekSeekRaid.Config.RaidOptions);
        RaidSite attacked = sites[defender];

        Board.Reset();

        string[] labels = attacked.AttackerSpawns.Select(spawn => attacked.Bearing(spawn)).ToArray();
        foreach (Player player in Players()) {
            SteamPlayer viewer = player.channel.owner;
            int side = SideOf(viewer.playerID.steamID.m_SteamID);
            if (side == defender) {
                Board.Open(viewer, defence, PeekSeekRaid.Instance.Translate("board_defend", round, attacked.Name), BoardFace.Raid,
                           _ => {
                               ShowPicks(side);
                               KitDefender(player);
                           });
            } else {
                Board.OpenAttack(viewer, raid, defender, attacked.AttackerSpawns.Count,
                    PeekSeekRaid.Instance.Translate("board_attack", round, attacked.Name),
                    PeekSeekRaid.Instance.Translate("board_attack_line"), attacked.Name, labels, _ => ShowPicks(side));
            }
        }

        ShowPicks(0);
        ShowPicks(1);
    }

    private static List<BoardOption> Tiles(List<RaidOption> pool) {
        return pool.Select(option => {
            BoardOption tile = option.ToBoard();
            tile.Glyph = (int)option.Category;
            tile.Picture = option.Picture;
            return tile;
        }).ToList();
    }

    /// Redraw one team's boards: every tile a teammate holds is greyed with their face.
    private void ShowPicks(int team) {
        List<ulong> roster = Roster(team).Where(steam64 => PlayerTool.getPlayer(new CSteamID(steam64)) != null).ToList();
        int tiles = (team == DefendedSite ? PeekSeekRaid.Config.DefenceOptions : PeekSeekRaid.Config.RaidOptions).Count;

        foreach (ulong viewer in roster) {
            for (int i = 0; i < tiles; i++) {
                Board.Claim(viewer, i, roster.FirstOrDefault(mate => mate != viewer && Board.PickIndex(mate) == i));
            }
        }
    }

    /// The pick runs its whole clock, because it is also the defenders' time to set up:
    /// their traps and the flag go down while the attackers are still choosing.
    private void TickPicking() {
        Board.Countdown(SecondsLeft);
        HoldDefenders();
        if (SecondsLeft > 0) return;

        EndPick();
    }

    /// Whoever has not picked gets a powerup nobody on their team has, a flag still in
    /// somebody's hands goes on a free tile, and the round goes live.
    private void EndPick() {
        Board.Close();
        SettlePicks();

        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (IsDefending(steam64) && !Board.HasAnswered(steam64)) KitDefender(player);
        }

        if (!RaidWorld.MarkerStands(sites[DefendedSite])) {
            if (PlayerTool.getPlayer(new CSteamID(FlagBearer)) is { } bearer) RaidWorld.TakeFlag(bearer);
            StandFlag();
        }

        StartPlaying();
    }

    /// A defender's kit, as soon as they have picked: their loadout, their side's
    /// baseline and the powerup, and the flag in hand for the one who places it.
    private void KitDefender(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        if (phase != RaidPhase.Picking || !IsDefending(steam64) || player.life.isDead) return;

        int index = Board.PickIndex(steam64);
        List<RaidOption> pool = PeekSeekRaid.Config.DefenceOptions;
        if (index >= 0 && index < pool.Count) chosen[steam64] = pool[index];

        RaidWorld.Kit(player, ChosenOption(steam64), BaselineFor(steam64));
        if (steam64 == FlagBearer && !RaidWorld.MarkerStands(sites[DefendedSite])) RaidWorld.HandFlag(player);
    }

    private void SettlePicks() {
        for (int team = 0; team < 2; team++) {
            List<RaidOption> pool = team == DefendedSite ? PeekSeekRaid.Config.DefenceOptions : PeekSeekRaid.Config.RaidOptions;
            List<ulong> roster = Roster(team);
            HashSet<int> taken = [..roster.Select(Board.PickIndex).Where(index => index >= 0)];

            foreach (ulong steam64 in roster) {
                int index = Board.PickIndex(steam64);
                if (index < 0) {
                    index = PickDraw.Fallback(pool.Count, taken, random);
                    if (index >= 0) taken.Add(index);
                }
                chosen[steam64] = index >= 0 && index < pool.Count ? pool[index] : null;
            }
        }
    }

    /// Defenders stay on their base while they set up.
    private void HoldDefenders() {
        RaidSite site = sites[DefendedSite];

        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (player.life.isDead || !IsDefending(steam64) || site.Contains(player.transform.position)) continue;

            player.teleportToLocationUnsafe(RaidWorld.FreeSpot(DefendedSite, site, IndexInTeam(steam64)), site.Yaw);
            Host.Whisper(player.channel.owner, PeekSeekRaid.Instance.Translate("flag_leashed"));
        }
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

    /// The round goes live: attackers come in at the spawn they chose with what they
    /// picked; defenders are already set up and keep what they carry.
    private void StartPlaying() {
        phase = RaidPhase.Playing;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.RoundSeconds;
        offBaseSince.Clear();
        FlagBearer = 0;

        RaidSite target = sites[DefendedSite];
        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (IsDefending(steam64)) {
                RaidWorld.Mark(player, RaidWorld.FlagPoint(target) ?? target.AnchorPoint, "Flag");
                RaidWorld.Freeze(player, false);
                continue;
            }
            Place(player);
        }
        Powerups.RoundLive();

        Host.RefreshHud();
    }

    /// The powerup a player took into this round, or null before the pick settles.
    internal RaidOption ChosenOption(ulong steam64) => chosen.TryGetValue(steam64, out RaidOption option) ? option : null;

    /// What everybody on a side carries whatever they picked.
    private List<RaidItem> BaselineFor(ulong steam64) {
        return IsDefending(steam64) ? PeekSeekRaid.Config.DefenceBaseline : PeekSeekRaid.Config.AttackBaseline;
    }

    private void TickPlaying() {
        PunishOffBase();
        if (pendingWinner >= 0) return;

        int defender = DefendedSite;
        GiveHalfTimeCharges();

        // The flag is the objective. Attackers win by bringing it down; defenders win by
        // still having it up when the clock runs out.
        if (!RaidWorld.MarkerStands(sites[defender])) {
            string breaker = flagBreaker != 0 ? NameOf(flagBreaker) : null;
            Conclude(1 - defender, PeekSeekRaid.Instance.Translate("round_won_flag", breaker ?? TeamName(1 - defender)));
            return;
        }

        if (SecondsLeft <= 0) {
            Conclude(defender, PeekSeekRaid.Instance.Translate("round_won_clock"));
            return;
        }

        if (Living(defender) == 0) {
            Conclude(1 - defender, WipeLine(1 - defender));
            return;
        }
        if (Living(1 - defender) == 0) {
            Conclude(defender, WipeLine(defender));
        }
    }

    /// Half way through the round, every attacker still alive gets HalfTimeCharges
    /// more demolition charges, and everybody is told.
    private void GiveHalfTimeCharges() {
        if (halfTimeGiven || SecondsLeft > PeekSeekRaid.Config.RoundSeconds / 2) return;
        halfTimeGiven = true;

        int charges = PeekSeekRaid.Config.HalfTimeCharges;
        if (charges <= 0) return;

        foreach (Player player in Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (IsDefending(steam64) || player.life.isDead || !Host.IsAlive(steam64)) continue;
            RaidWorld.Give(player, [new RaidItem(PeekSeekRaid.Config.ChargeItemId, (byte)charges)]);
        }
        Host.Banner("HALF TIME", PeekSeekRaid.Instance.Translate("half_time", charges));
    }

    /// How a side was wiped out: by one player alone against more than two, an ace, or
    /// by the team.
    private string WipeLine(int winner) {
        int enemies = Roster(1 - winner).Count;
        ulong ace = Roster(winner).FirstOrDefault(steam64 => roundKills.TryGetValue(steam64, out int kills) && kills >= enemies);
        if (enemies > 2 && ace != 0) return PeekSeekRaid.Instance.Translate("round_won_ace", NameOf(ace), TeamName(1 - winner));

        return PeekSeekRaid.Instance.Translate("round_won_wipe", TeamName(winner), TeamName(1 - winner));
    }

    private static string NameOf(ulong steam64) {
        return PlayerTool.getPlayer(new CSteamID(steam64))?.channel.owner.playerID.characterName ?? "";
    }

    /// The flag took a hit from `instigator`.
    internal void FlagHit(CSteamID instigator) {
        if (phase == RaidPhase.Playing && instigator.m_SteamID != 0) flagBreaker = instigator.m_SteamID;
    }

    /// Award a round from the console: the banner at once, the rest a moment later.
    internal void EndRound(int winner, string reasonKey) {
        Conclude(winner, PeekSeekRaid.Instance.Translate(reasonKey));
    }

    /// The round is decided: the score and the banner now, so everybody sees who won
    /// the moment they did, and the reset RoundEndDelay later.
    private void Conclude(int winner, string line) {
        if (phase != RaidPhase.Playing || pendingWinner >= 0) return;

        pendingWinner = winner;
        pendingAt = Time.realtimeSinceStartup + PeekSeekRaid.Config.RoundEndDelay;
        Host.RoundWon(winner, line);
    }

    /// After the pause: either the next round, or the end of the match, won or level.
    /// The score is Match's; the block arithmetic that says when the match is over is
    /// this gamemode's, because a Raid match is not first to N.
    private void AfterRound() {
        pendingWinner = -1;
        foreach (Player player in Players()) RaidWorld.Mark(player, null, null);
        Powerups.RoundOver();

        int matchWinner = RoundOrder.Winner(round, Host.Score(0), Host.Score(1));
        bool drawn = matchWinner < 0 && RoundOrder.IsDraw(round, Host.Score(0), Host.Score(1));
        if (matchWinner >= 0 || drawn) {
            phase = RaidPhase.Over;
            Board.Close();
            foreach (Player player in Players()) RaidWorld.Freeze(player, false);
            if (drawn) Host.MatchDrawn();
            else Host.MatchWon(matchWinner);
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
            if (!IsDefending(steam64) || player.life.isDead || !Host.IsAlive(steam64)) continue;
            if (Powerups.HasTag(steam64)) {
                offBaseSince.Remove(steam64);
                continue;
            }

            if (RaidWorld.IsStandingOnBuilt(player) || Powerups.OffBaseImmune(steam64)) {
                if (offBaseSince.Remove(steam64)) Host.Tag.Hide(player.channel.owner);
                continue;
            }

            if (!offBaseSince.TryGetValue(steam64, out float since)) {
                since = Time.realtimeSinceStartup;
                offBaseSince[steam64] = since;
            }

            // On screen for as long as it is true: the grace draining in the pips,
            // then the damage in red until they are back on something built.
            float off = Time.realtimeSinceStartup - since;
            float grace = PeekSeekRaid.Config.OffBaseGraceSeconds;
            if (off < grace) {
                Host.Tag.Show(player.channel.owner, "OFF BASE", Mathf.Min(3, Mathf.CeilToInt((grace - off) / grace * 3)), null);
                continue;
            }

            Host.Tag.Show(player.channel.owner, "OFF BASE", -1, $"-{PeekSeekRaid.Config.OffBaseDamagePerSecond} / s");
            player.life.askDamage(
                PeekSeekRaid.Config.OffBaseDamagePerSecond,
                Vector3.up,
                EDeathCause.ARENA,
                ELimb.SPINE,
                CSteamID.Nil,
                out EPlayerKill _);
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
        Powerups.Watch(player);

        // No death screen: a death is straight back to wherever the round says, as in
        // ARENA.
        player.setPluginWidgetFlag(EPluginWidgetFlags.ShowDeathMenu, false);
        int side = SideOf(steam64);
        RaidSite own = sites[side];

        // A salvage is the swap while building, so it is quick; in a round it is the
        // soft wall coming down, which should cost a moment.
        RaidWorld.SalvageTime(player, phase == RaidPhase.Building
            ? PeekSeekRaid.Config.BuildSalvageSeconds
            : PeekSeekRaid.Config.RoundSalvageSeconds);

        // The base being taken on every attacker's map and compass, and the flag
        // itself on every defender's, from the moment they are put down for the round
        // until it is over. The rest of the team was in the lobby while the bearer
        // stood it, so they arrive knowing where it is the same way the attackers
        // know where the base is.
        bool inRound = phase is RaidPhase.Picking or RaidPhase.Playing && Host.IsAlive(steam64);
        RaidSite target = sites[DefendedSite];
        Vector3? mark = !inRound ? null : IsDefending(steam64) ? RaidWorld.FlagPoint(target) ?? target.AnchorPoint : target.AnchorPoint;
        RaidWorld.Mark(player, mark, IsDefending(steam64) ? "Flag" : target.Name);

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

            // The pick is the defenders' setup: they are on their base from its first
            // second, kitted the moment they pick, and one of them holds the flag.
            // Attackers wait it out in the map's lobby, where there is nothing to see and
            // nowhere to be early, and only come in when they can do something.
            case RaidPhase.Picking:
                if (IsDefending(steam64)) {
                    Spawn(player, steam64, side);
                    if (Board.HasAnswered(steam64)) KitDefender(player);
                    else RaidWorld.DressPlain(player);
                    if (steam64 == FlagBearer && !RaidWorld.MarkerStands(sites[DefendedSite])) RaidWorld.HandFlag(player);
                    RaidWorld.Freeze(player, false);
                    return;
                }

                Wait(player, side);
                RaidWorld.DressPlain(player);
                RaidWorld.Freeze(player, true);
                return;

            case RaidPhase.Playing:
                // Out of the round: the map's lobby, with nothing in hand, free to walk
                // and talk, until the next round puts them back.
                if (!Host.IsAlive(steam64)) {
                    Wait(player, side);
                    RaidWorld.DressPlain(player);
                    RaidWorld.Freeze(player, false);
                    return;
                }

                Spawn(player, steam64, side);
                RaidWorld.Kit(player, ChosenOption(steam64), BaselineFor(steam64));
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

        // A phantom in a local test match is alive until the tester moves the round on.
        return match.Teams[team].Players.Count(steam64 => {
            if (Host.IsPhantom(steam64)) return true;
            Player player = PlayerTool.getPlayer(new CSteamID(steam64));
            return player != null && !player.life.isDead && Host.IsAlive(steam64);
        });
    }

    // ---- the test console ---------------------------------------------------------

    /// Move the match on one phase, whatever it is doing: the footprint board, the
    /// build, the pick and the setup end early; a round ends the way its clock would, to
    /// the defenders while the flag stands and to the attackers once it is down.
    internal void NextPhase() {
        switch (phase) {
            case RaidPhase.Shaping:
                FinishShaping();
                return;
            case RaidPhase.Building:
                FinishBuilding();
                return;
            case RaidPhase.Picking:
                EndPick();
                return;
            case RaidPhase.Playing:
                if (pendingWinner >= 0) {
                    AfterRound();
                    return;
                }
                bool standing = RaidWorld.MarkerStands(sites[DefendedSite]);
                Conclude(standing ? DefendedSite : 1 - DefendedSite, PeekSeekRaid.Instance.Translate(
                    standing ? "round_won_clock" : "round_won_flag", TeamName(1 - DefendedSite)));
                return;
        }
    }

    /// Set the clock of whatever phase is running.
    internal void SetSecondsLeft(int seconds) {
        if (phase is RaidPhase.Idle or RaidPhase.Over) return;
        phaseEndsAt = Time.realtimeSinceStartup + Mathf.Max(0, seconds);
        nextTick = 0f;
    }

    /// Whether every player on a team is a phantom, which is a team nobody builds for.
    private bool IsPhantomTeam(int team) {
        return match.Teams.Count > team && match.Teams[team].Players.Count > 0 && match.Teams[team].Players.All(Host.IsPhantom);
    }

    private static IEnumerable<Player> Players() => Host.Players();
}
