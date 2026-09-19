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
    Starting,
    Playing,
    Over
}

/// Siege: build a base, then take turns attacking each other's.
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

    /// What each team has left to lay, for the build phase only.
    private readonly BuildBudget[] budgets = new BuildBudget[2];

    private readonly PickBoard board = new();
    private readonly MatchHud hud = new();
    private readonly BuildPanel panel = new();

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

        Chat.Announce(PeekSeekSiege.Instance.Translate("match_intro",
            RoundOrder.RegulationHalf, Wording.Duration(PeekSeekSiege.Config.BuildSeconds)));

        StartShaping();
    }

    public void End() {
        board.Close();
        hud.Hide();
        StopBuilding();
        SiegeWorld.Unhook();

        foreach (Player player in Players()) {
            AllowRespawn(player);
            SiegeWorld.Freeze(player, false);
        }

        phase = SiegePhase.Idle;
        match = null;
    }

    public void Tick() {
        if (phase == SiegePhase.Idle || phase == SiegePhase.Over) return;

        // Everything below is per-second bookkeeping, not per-frame work: clocks,
        // the off-base check and the flag check all read the same once a second as
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
            case SiegePhase.Starting:
                if (SecondsLeft <= 0) StartPlaying();
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

    /// Unturned raises this while the leaver is still a client, so the redraw goes to
    /// them too; forgetting them afterwards is what stops the clock following them out.
    public void OnPlayerLeft(CSteamID steamID) {
        offBaseSince.Remove(steamID.m_SteamID);
        swapping.Remove(steamID.m_SteamID);

        Player leaving = PlayerTool.getPlayer(steamID);
        if (leaving != null) BudgetFor(steamID.m_SteamID)?.Unwatch(leaving);

        RefreshHud();
        hud.Forget(steamID.m_SteamID);
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
        RefreshHud();
    }

    // ---- the footprint ----------------------------------------------------------

    /// Before anybody builds, each team says what it is building on: a board with the
    /// two footprints, up for as long as a pick. The first answer on a team is the
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
            board.Open(viewer, footprints, PeekSeekSiege.Instance.Translate("board_shape"), BoardFace.Shape);
        }

        RefreshHud();
    }

    private void TickShaping() {
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

    /// The two footprints as tiles, in BaseShape order, which is the order the board's
    /// pictures are baked in. The first answer on a team decides for the team.
    private static List<SiegeOption> ShapeOptions() {
        return [
            new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("shape_square"),
                Blurb = PeekSeekSiege.Instance.Translate("shape_square_blurb")
            },
            new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("shape_hexagonal"),
                Blurb = PeekSeekSiege.Instance.Translate("shape_hexagonal_blurb")
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

        for (int team = 0; team < 2; team++) {
            int captured = team;
            budgets[team] = new BuildBudget(SuppliesFor(ShapeAt(team)));
            budgets[team].Changed += () => ShowPanel(captured);
        }

        foreach (int team in BuiltSites()) {
            SiegeSite site = sites[team];
            BaseSnapshot.Clear(site.AnchorPoint, site.BuildRadius, []);
            SiegeWorld.PlaceAnchor(team, site, shapes[team]);
            SiegeWorld.PlacePillars(site, GroupOf(team), shapes[team]);
        }

        foreach (Player player in Players()) Place(player);

        RefreshHud();
    }

    /// The team's whole kit: the pieces every base gets, and the triangles a
    /// hexagonal one needs on top.
    private static IEnumerable<SiegeItem> SuppliesFor(BaseShape shape) {
        IEnumerable<SiegeItem> supplies = PeekSeekSiege.Config.BuildSupplies;
        return shape == BaseShape.Square ? supplies : supplies.Concat(PeekSeekSiege.Config.TriangleSupplies);
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
        board.Close();
        swapping.Clear();
        lastPlaced.Clear();

        foreach (BuildBudget budget in budgets) budget?.UnwatchAll();
        foreach (SteamPlayer viewer in Provider.clients.ToList()) panel.Hide(viewer);
    }

    /// The budget a player builds from: their team's. Solo builds as team 0.
    internal BuildBudget BudgetFor(ulong steam64) => budgets[SideOf(steam64)];

    /// A builder put a piece down: one off the team's pile, and the panel picks out
    /// that kind for them from now on.
    internal void Placed(ulong steam64, ushort id) {
        lastPlaced[steam64] = id;
        if (!BudgetFor(steam64).Spend(id)) ShowPanel(SideOf(steam64));
    }

    private void ShowPanel(int team) {
        BuildBudget budget = budgets[team];
        if (budget == null || phase != SiegePhase.Building) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            ulong steam64 = viewer.playerID.steamID.m_SteamID;
            if (SideOf(steam64) != team) continue;

            panel.Show(viewer, budget.Counts.Rows, lastPlaced.TryGetValue(steam64, out ushort laying) ? laying : (ushort)0);
        }
    }

    /// A builder salvaged one of their team's pieces: the salvage was refused, and
    /// this is the board in its place, the same piece in every other material and a
    /// way to take it down.
    internal void OfferSwap(SteamPlayer viewer, StructureDrop drop, int site) {
        if (phase != SiegePhase.Building) return;

        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        BuildBudget budget = budgets[site];
        ItemStructureAsset current = drop.asset;

        List<SiegeOption> options = SiegeWorld.Variants(current).Select(variant => new SiegeOption {
            Name = variant.itemName,
            Blurb = PeekSeekSiege.Instance.Translate(
                budget.Counts.Has(variant.id) ? "swap_left" : "swap_none", budget.Counts.Left(variant.id), variant.health),
            Icon = variant.id
        }).ToList();

        // A starting floor can change material but never go: the site is the floors.
        if (!SiegeWorld.IsAnchorTile(site, drop)) {
            options.Add(new SiegeOption {
                Name = PeekSeekSiege.Instance.Translate("swap_remove"),
                Blurb = PeekSeekSiege.Instance.Translate("swap_remove_blurb")
            });
        }

        swapping[steam64] = drop.GetServersideData().point;
        board.Open(viewer, options, current.itemName, BoardFace.Swap, taken => Swap(viewer, taken));
    }

    /// Carry a swap out. The piece is found again by where it stands, because the
    /// board may have been up for a while and a teammate may have salvaged it
    /// meanwhile; a piece that is no longer there is nothing to swap.
    private void Swap(SteamPlayer viewer, SiegeOption taken) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (phase != SiegePhase.Building || !swapping.TryGetValue(steam64, out Vector3 at)) return;
        swapping.Remove(steam64);

        StructureDrop drop = SiegeWorld.StructureAt(at);
        if (drop == null) return;

        BuildBudget budget = BudgetFor(steam64);
        ushort was = drop.asset.id;

        if (taken.Icon == 0) {
            SiegeWorld.Remove(drop);
            budget.Refund(was);
            return;
        }

        if (Assets.find(EAssetType.ITEM, taken.Icon) is not ItemStructureAsset to) return;
        if (!budget.Spend(to.id)) {
            Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate("swap_none_left", to.itemName));
            return;
        }

        SiegeWorld.Swap(drop, to);
        budget.Refund(was);
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

    /// Everything standing on the plot, starting floors included: they may have
    /// changed material during the build, and a round has to put back what was there.
    private BaseSnapshot Snapshot(int team) {
        SiegeSite site = sites[team];
        BaseSnapshot taken = BaseSnapshot.Capture(site.AnchorPoint, site.BuildRadius, []);
        Logger.Log($"Captured {taken.Count} piece(s) for {site.Name}.");
        return taken;
    }

    // ---- rounds ---------------------------------------------------------------

    private void StartRound(int number) {
        round = number;
        deadThisRound.Clear();
        flagHanded = false;

        RestoreBases();

        // Everything a round left on the ground: the loot a death dropped, the wall
        // somebody never placed. Siege has no world loot, so there is nothing to keep.
        ItemManager.askClearAllItems();

        phase = SiegePhase.Picking;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.PickSeconds;

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
        RefreshHud();
    }

    /// Put both buildings back exactly as they were built, then hand the pieces out
    /// according to the rules that make a round playable. This runs before every round
    /// including the first, so round one and round six start from identical ground.
    /// Last round's flag goes with everything else; this round's is placed fresh.
    private void RestoreBases() {
        for (int team = 0; team < sites.Count; team++) {
            SiegeSite site = sites[team];
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
        List<SiegeOption> defence = PeekSeekSiege.Config.DefenceOptions;
        List<SiegeOption> raid = PeekSeekSiege.Config.RaidOptions;
        SiegeSite attacked = sites[defender];

        board.Reset();

        // One board each, because the two sides are choosing from different lists at the
        // same moment. Solo asks SideOf rather than the roster, so the lone tester gets
        // the board for whichever side they are currently playing.
        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            if (IsDefending(viewer.playerID.steamID.m_SteamID)) {
                board.Open(viewer, defence, PeekSeekSiege.Instance.Translate("board_defend"));
            } else {
                board.OpenAttack(viewer, raid, defender, attacked.AttackerSpawns.Count);
            }
        }
    }

    /// The pick phase ends when the last person has picked and the flag is up, not
    /// when the clock says. When the clock says, whatever is unanswered is answered
    /// for them, and a flag still in somebody's hands goes on a free tile.
    private void TickPicking() {
        board.Countdown(SecondsLeft);
        WalkTheFlag();

        bool everyoneAnswered = Players().All(p => board.HasAnswered(p.channel.owner.playerID.steamID.m_SteamID));
        bool flagUp = SiegeWorld.MarkerStands(sites[DefendedSite]);
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
            if (!board.HasAnswered(FlagBearer)) return;

            flagHanded = true;
            SiegeWorld.HandFlag(bearer);
            SiegeWorld.Freeze(bearer, false);
            Chat.Whisper(bearer.channel.owner, PeekSeekSiege.Instance.Translate("flag_bearer", SecondsLeft));
            return;
        }

        SiegeSite site = sites[DefendedSite];
        if (site.Contains(bearer.transform.position)) return;

        bearer.teleportToLocationUnsafe(SiegeWorld.StandingSpot(site), site.Yaw);
        Chat.Whisper(bearer.channel.owner, PeekSeekSiege.Instance.Translate("flag_leashed"));
    }

    /// One of the defenders present, at random. Random rather than in turn because a
    /// team that knows who is placing next round can plan the placement in the round
    /// before, and the point of the flag is that every round's defence is decided in
    /// its own twenty seconds.
    private ulong ChooseFlagBearer() {
        int defender = DefendedSite;

        if (match.Solo) {
            Player tester = Players().FirstOrDefault();
            return tester != null && IsDefending(tester.channel.owner.playerID.steamID.m_SteamID)
                ? tester.channel.owner.playerID.steamID.m_SteamID
                : 0;
        }

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
        SiegeWorld.PlaceFlag(defender, sites[defender], ShapeAt(defender), GroupOf(defender), TeamName(defender));
    }

    /// Three seconds with the flag up and everybody rooted in position, so the round
    /// starts on a count rather than on whoever noticed first. This is the moment the
    /// lobby empties: defenders onto their building, attackers onto the spawn they
    /// chose, facing it.
    private void StartCountdown() {
        board.Close();
        phase = SiegePhase.Starting;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.StartSeconds;
        FlagBearer = 0;
        flagHanded = false;

        foreach (Player player in Players()) Place(player);

        RefreshHud();
    }

    private void StartPlaying() {
        phase = SiegePhase.Playing;
        phaseEndsAt = Time.realtimeSinceStartup + PeekSeekSiege.Config.RoundSeconds;
        offBaseSince.Clear();

        foreach (Player player in Players()) {
            SiegeWorld.Kit(player, board.Chosen(player.channel.owner.playerID.steamID.m_SteamID));
            SiegeWorld.Freeze(player, false);
        }

        RefreshHud();
    }

    private void TickPlaying() {
        PunishOffBase();

        int defender = DefendedSite;

        // The flag is the objective. Attackers win by bringing it down; defenders win by
        // still having it up when the clock runs out.
        if (!SiegeWorld.MarkerStands(sites[defender])) {
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
        if (phase < SiegePhase.Picking || phase > SiegePhase.Playing) return;

        score[winner]++;
        Chat.Announce(PeekSeekSiege.Instance.Translate(reasonKey,
            TeamName(winner), round, score[0], score[1]));

        int matchWinner = RoundOrder.Winner(round, score[0], score[1]);
        if (matchWinner >= 0) {
            phase = SiegePhase.Over;
            board.Close();
            foreach (Player player in Players()) SiegeWorld.Freeze(player, false);
            Chat.Announce(PeekSeekSiege.Instance.Translate("match_won",
                TeamName(matchWinner), score[0], score[1]));
            RefreshHud();
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

    /// Put one player where the current phase says they belong, kit them for it, and
    /// root them if the phase is one nobody moves in.
    private void Place(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        int side = SideOf(steam64);
        SiegeSite own = sites[side];

        // A salvage is the swap while building, so it is quick; in a round it is the
        // soft wall coming down, which should cost a moment.
        SiegeWorld.SalvageTime(player, phase == SiegePhase.Building
            ? PeekSeekSiege.Config.BuildSalvageSeconds
            : PeekSeekSiege.Config.RoundSalvageSeconds);

        switch (phase) {
            case SiegePhase.Shaping:
                player.teleportToLocationUnsafe(SiegeWorld.OnGround(own.AnchorPoint), own.Yaw);
                SiegeWorld.DressPlain(player);
                SiegeWorld.Freeze(player, true);
                return;

            case SiegePhase.Building:
                player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(own), own.Yaw);
                SiegeWorld.DressForBuilding(player);
                SiegeWorld.Freeze(player, false);
                BudgetFor(steam64).Watch(player);
                panel.Show(player.channel.owner, BudgetFor(steam64).Counts.Rows, 0);
                return;

            // The bearer waits on the base for their flag; everybody else waits in the
            // map's lobby, where there is nothing to see and nowhere to be early.
            case SiegePhase.Picking:
                if (steam64 == FlagBearer) {
                    SiegeSite defended = sites[DefendedSite];
                    player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(defended), defended.Yaw);
                    if (flagHanded) SiegeWorld.HandFlag(player);
                    else SiegeWorld.DressPlain(player);
                    SiegeWorld.Freeze(player, !flagHanded);
                    return;
                }

                Wait(player, side);
                SiegeWorld.DressPlain(player);
                SiegeWorld.Freeze(player, true);
                return;

            case SiegePhase.Starting:
                Spawn(player, steam64, side);
                SiegeWorld.DressPlain(player);
                SiegeWorld.Freeze(player, true);
                return;

            case SiegePhase.Playing:
                Spawn(player, steam64, side);
                SiegeWorld.Kit(player, board.Chosen(steam64));
                SiegeWorld.Freeze(player, false);
                return;

            default:
                SiegeWorld.Freeze(player, false);
                return;
        }
    }

    /// A round's spawn: defenders on their building, attackers on the point they chose,
    /// facing the base, spread a step apart when several chose the same one.
    private void Spawn(Player player, ulong steam64, int side) {
        SiegeSite defended = sites[DefendedSite];

        if (IsDefending(steam64)) {
            player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(defended), defended.Yaw);
            return;
        }

        int spawn = Math.Max(0, board.SpawnChosen(steam64));
        List<ulong> same = Roster(side).Where(mate => Math.Max(0, board.SpawnChosen(mate)) == spawn).ToList();
        int index = Math.Max(0, same.IndexOf(steam64));

        Vector3 at = SiegeWorld.OnGround(defended.AttackerSpawn(spawn, index, Math.Max(1, same.Count)));
        player.teleportToLocationUnsafe(at, defended.YawTowardsAnchor(at.x, at.z));
    }

    /// Put a player in the map's lobby for the length of a pick, each on their own
    /// spawn point of it. A map without a lobby has nowhere else to wait but the plot.
    private void Wait(Player player, int side) {
        int index = Math.Max(0, Players().ToList().IndexOf(player));
        if (SiegeWorld.LobbySpot(index, out Vector3 point, out float angle)) {
            player.teleportToLocationUnsafe(point, angle);
            return;
        }

        SiegeSite own = sites[side];
        player.teleportToLocationUnsafe(SiegeWorld.StandingSpot(own), own.Yaw);
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
        foreach (Player player in Players()) Place(player);
        RefreshHud();
    }

    internal string TeamName(int team) {
        return match.Teams.Count > team ? match.Teams[team].Name : sites[team].Name;
    }

    /// A team's players in roster order, or just the tester in solo, who is a team of
    /// one on whichever side they are playing.
    private List<ulong> Roster(int team) {
        if (match.Teams.Count > team && match.Teams[team].Players.Count > 0) return match.Teams[team].Players;

        return Players().Select(p => p.channel.owner.playerID.steamID.m_SteamID).Take(1).ToList();
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

    // ---- the HUD ----------------------------------------------------------------

    /// Everything the HUD shows, rebuilt from the match on every change.
    private void RefreshHud() {
        if (match == null) return;
        hud.Show(HudState());
    }

    private HudState HudState() {
        HudState state = new() {
            SecondsLeft = SecondsLeft,
            DefendingTeam = phase >= SiegePhase.Picking && phase <= SiegePhase.Playing ? DefendedSite : -1
        };

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

        // The pick hangs off the face once it is made and stays there for the round it
        // was made for: that is the "what did they bring" a teammate glances up for.
        // Nothing hangs there before the first round; the footprint is not a pick.
        bool picked = phase == SiegePhase.Playing
            || (phase >= SiegePhase.Picking && phase <= SiegePhase.Starting && board.HasAnswered(steam64));

        return new HudSeat {
            Steam64 = steam64,
            AvatarUrl = RemoteAPI.AvatarFor(steam64) ?? PeekSeekSiege.Config.DefaultAvatarURL,
            PickItemId = picked ? board.Chosen(steam64)?.IconItemId ?? 0 : (ushort)0,
            Alive = player != null && !player.life.isDead && !deadThisRound.Contains(steam64)
        };
    }
}
