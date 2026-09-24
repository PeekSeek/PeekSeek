using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using PeekSeek.Core.Models;
using PeekSeek.Core.Models.Enums;
using PeekSeek.Core.Web;
using PeekSeek.Gamemodes;
using PeekSeek.Hud;
using Rocket.Core;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeek.Core;

/// The match server.
///
/// Everything a match is apart from its rules lives here: the session with the site,
/// who is on the roster and in which Unturned group, who is spectating, the wait for
/// everybody to arrive, the score, the stats, the result the site is owed, and the
/// screens. The rules are a gamemode's (PeekSeek.Gamemodes.IGamemode), handed the
/// match once the wait is over and told about players from then on.
internal abstract class PSManager {
    internal static EPSStatus Status;

    /// The match being played, once the site has handed it over. Null while this server
    /// is free or still waiting on its players to finish choosing.
    internal static PSMatch Data;

    /// The match this server has told the site it is responsible for. Set the moment a
    /// match is assigned — before its data arrives — and only cleared once the site has
    /// acknowledged the result, so a match is never treated as abandoned while this
    /// server still has something to say about it.
    internal static int HeldMatchId { get; private set; }

    /// The roster of the match being held while its players choose, what they have to
    /// choose from, what each has chosen so far, and when the site stops waiting. The
    /// players are on their way here from the lobby the moment the site names this
    /// server, so whoever arrives with a pick still to make gets the board.
    private static PSMatch Held;
    private static PSOptions Options;
    private static long SelectionEndsAt;
    private static readonly Dictionary<ulong, PSPicked> Picked = [];
    private static float LastPickCountdown;

    internal static float MatchReady;
    private static List<GroupInfoEx> GroupInfos;

    /// The match as the gamemode sees it, and the gamemode running it. Running is null
    /// from the hand-over until everybody who is coming has arrived; Chosen is the mode
    /// that will get it then.
    internal static GamemodeMatch Match { get; private set; }
    internal static IGamemode Running { get; private set; }
    private static IGamemode Chosen;
    private static bool Waiting;

    private static readonly MatchHud Hud = new();
    internal static readonly PickBoard Board = new();
    internal static readonly BuildPanel Panel = new();
    internal static readonly Tag Tag = new();
    internal static Banner Banner;
    private static KillFeed Feed;
    private static DeathCard Card;
    private static Scoreboard Scores;

    /// Whether the scoreboard is up for everybody and stays up: the end of a match.
    /// Otherwise it is up for whoever is holding the key.
    private static bool ScoresLocked;

    /// When this server came up, and whether it has decided it wants to go down again.
    /// Restarting is how a server picks up a new game build and new plugins, and the
    /// only safe time to do it is while it is holding nothing.
    private static float StartedAt;
    private static bool WantsRestart;

    /// When it stopped asking the site for work. Zero while it is still asking.
    private static float DrainingSince;

    private const float UpdateIntervalSeconds = 1f;
    private static float LastUpdateSent;

    private const float VisibilityIntervalSeconds = 0.15f;
    private static float LastVisibilityCheck;

    /// The bar's clock and health move between redraws on this cadence. Short, because
    /// a bar that shows a teammate at half health two seconds after the hit is a bar
    /// nobody trusts.
    private const float HudIntervalSeconds = 0.25f;
    private static float LastHudTick;

    /// Separate from UpdateIntervalSeconds: this is a console log for debugging alive/dead
    /// state solo, not the payload sent to the overlay, so it can run on its own slower
    /// cadence without touching what the overlay actually receives.
    private const float DebugStateLogIntervalSeconds = 10f;
    private static float LastDebugStateLogged;

    private static PSUpdateEvent LatestEvent;
    private static int EventsRecorded;

    private const int FeedLength = 5;
    private static readonly List<PSUpdateKill> RecentKills = [];

    /// Separate from EventsRecorded: this ids the durable kill log sent to the site,
    /// not the ephemeral overlay feed, and the two have no reason to share a sequence.
    private static int KillEventsRecorded;

    private static CSteamID RelaySteamID => new(PeekSeek.Config.LobbyServerRelaySGSSteamID);

    private static Harmony Instrument;

    internal static void Init() {
        StartedAt = Time.realtimeSinceStartup;

        Provider.onCheckValidWithExplanation += OnCheckValidWithExplanation;
        Provider.onServerConnected += OnClientConnected;
        Provider.onServerDisconnected += OnClientDisconnected;

        UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDead;
        UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;
        PlayerLife.onPlayerLifeUpdated += OnLifeUpdated;
        EffectManager.onEffectButtonClicked += OnButtonClicked;

        Instrument = new Harmony("blaze.ps.main");
        Instrument.PatchAll(Assembly.GetExecutingAssembly());

        Level.onPostLevelLoaded += OnPostLevelLoaded;

        DamageTool.damagePlayerRequested += OnDamageRequested;
        UseableGun.onBulletSpawned += OnBulletSpawned;
        RemoteAPI.Init();

        Feed = new KillFeed(new KillFeedSettings {
            EffectGUID = PeekSeek.Config.KillFeedEffectGUID,
            EffectKey = PeekSeek.Config.KillFeedKey,
            ItemIconURL = PeekSeek.Config.ItemIconURL,
            Rows = PeekSeek.Config.KillFeedRows,
            RowSeconds = PeekSeek.Config.KillFeedRowSeconds,
            NameLength = PeekSeek.Config.KillFeedNameLength
        });
        Feed.Hook();

        Banner = new Banner(new BannerSettings {
            EffectGUID = PeekSeek.Config.BannerEffectGUID,
            EffectKey = PeekSeek.Config.BannerKey,
            Seconds = PeekSeek.Config.BannerSeconds
        });
        Card = new DeathCard(new DeathCardSettings {
            EffectGUID = PeekSeek.Config.DeathCardEffectGUID,
            EffectKey = PeekSeek.Config.DeathCardKey,
            ItemIconURL = PeekSeek.Config.ItemIconURL,
            Seconds = PeekSeek.Config.DeathCardSeconds
        });
        Scores = new Scoreboard(new ScoreboardSettings {
            EffectGUID = PeekSeek.Config.ScoreboardEffectGUID,
            EffectKey = PeekSeek.Config.ScoreboardKey
        });
        PlayerInput.onPluginKeyTick += OnPluginKey;

        ConfigurationManager.DisablePassiveHealthRegen();
    }

    internal static void Shutdown() {
        if (Data != null) ResetMatchState();

        Provider.onCheckValidWithExplanation -= OnCheckValidWithExplanation;
        Provider.onServerConnected -= OnClientConnected;
        Provider.onServerDisconnected -= OnClientDisconnected;

        UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDead;
        UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
        PlayerLife.onPlayerLifeUpdated -= OnLifeUpdated;
        EffectManager.onEffectButtonClicked -= OnButtonClicked;

        Instrument.UnpatchAll("blaze.ps.main");

        Level.onPostLevelLoaded -= OnPostLevelLoaded;

        DamageTool.damagePlayerRequested -= OnDamageRequested;
        UseableGun.onBulletSpawned -= OnBulletSpawned;

        Feed.Unhook();
        Feed.Clear();
        PlayerInput.onPluginKeyTick -= OnPluginKey;
        Banner.Clear();
        Card.Clear();
        Scores.HideAll();
        RemoteAPI.Shutdown();
    }

    private static void OnDamageRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        Player attacker = PlayerTool.getPlayer(parameters.killer);

        if (attacker != null && parameters.damage > 5) {
            int ping = Ping.Milliseconds(attacker.channel.owner);

            parameters.damage *= PingPercentage(ping) / 100f;
        }

        // Recorded after the ping scaling above, so damage stats and the fight history
        // both reflect what the victim actually took rather than the pre-reduction number.
        CSteamID victimID = parameters.player.channel.owner.playerID.steamID;
        StatService.AddDamageTaken(victimID, parameters.damage);

        float? reactionTime = null;
        if (attacker != null) {
            StatService.AddDamageDealt(parameters.killer, parameters.damage);
            StatService.RecordHit(parameters.killer, parameters.limb == ELimb.SKULL);
            reactionTime = VisibilityTracker.ConsumeReactionTime(parameters.killer);
        }

        FightTracker.RecordHit(
            victimID,
            parameters.killer,
            parameters.damage,
            parameters.limb,
            attacker?.transform.position ?? default,
            parameters.player.transform.position,
            reactionTime
        );

        RoundLog.RecordDamage(parameters.killer, victimID, parameters.damage);
    }

    /// What a shooter's damage is scaled to at this ping, as a percentage: 100 for
    /// nothing taken off. The same number colours the ping under their own face, so
    /// a player who is being cut sees it every time they look up rather than once
    /// in a chat line.
    internal static int PingPercentage(int ping) {
        foreach (var config in PeekSeek.Config.PingConfig.OrderByDescending(c => c.PingOrHigher)) {
            if (ping >= config.PingOrHigher) return config.Precentage;
        }
        return 100;
    }

    /// Only worth sampling in bullet-drop mode: instant-hit mode has no equivalent
    /// per-shot event, and Data is null outside a live match, so both are guarded here
    /// rather than asking every caller to remember the condition. Third-person is also
    /// excluded — PeekSeek is played first-person, but if a player ever ends up in
    /// third-person the aim direction no longer matches where their reticle actually was,
    /// so a spray-offset sample from that shot would be quietly wrong rather than missing.
    private static void OnBulletSpawned(UseableGun gun, BulletInfo bullet) {
        // Counted before the ballistics guard below, which exists for the shot-trace
        // telemetry. A shot is a shot whether or not this match records trajectories,
        // and accuracy is wrong if half of them are not counted.
        if (gun?.player != null)
            StatService.RecordShotFired(gun.player.channel.owner.playerID.steamID);

        if (Data?.Settings?.Ballistics != true) return;
        if (gun.player.look.perspective != EPlayerPerspective.FIRST) return;

        FightTracker.RecordShot(
            gun.player.channel.owner.playerID.steamID,
            bullet.origin,
            bullet.ApproximatePlayerAimDirection
        );
    }

    private static void OnPostLevelLoaded(int level) {
        RemoteAPI.StartSession();
        Status = EPSStatus.READY;
    }

    /// A click on a tile of a board. The board works out whose it was and what for.
    private static void OnButtonClicked(Player player, string buttonName) {
        Board.AnswerButton(player.channel.owner, buttonName);
    }

    // ---- the session --------------------------------------------------------------

    /// Acts on one answer from the session poll. Every state this server can be in is
    /// reached from here, and nowhere else.
    internal static void Apply(PSSessionAnswer answer) {
        if (answer == null) return;

        switch (answer.Action) {
            case PSSessionAnswer.Wait:
                HoldForMatch(answer);
                break;
            case PSSessionAnswer.Host:
                StartMatch(answer.Match);
                break;
            case PSSessionAnswer.Abort:
                AbortMatch(answer.Reason);
                break;
        }
    }

    /// This server has been given a match whose players are still choosing. Hold it,
    /// let its players in as they arrive, and put the board in front of whoever has
    /// something left to choose. Every poll brings the site's view of who has picked,
    /// which is what closes a board answered on the site instead of here.
    private static void HoldForMatch(PSSessionAnswer answer) {
        bool fresh = HeldMatchId != answer.MatchId || Status != EPSStatus.WAITING;
        HeldMatchId = answer.MatchId;
        UpdateStatus(EPSStatus.WAITING);
        Held = answer.Match ?? Held;
        Options = answer.Options ?? Options;
        SelectionEndsAt = answer.SelectionEndsAt;

        foreach (KeyValuePair<string, PSPicked> entry in answer.Picked ?? []) {
            if (!ulong.TryParse(entry.Key, out ulong steam64)) continue;
            PSPicked known = Picked.TryGetValue(steam64, out PSPicked local) ? local : new PSPicked();
            // The site's word beats a local answer it has not seen yet; a local
            // answer beats the site's silence.
            known.Kit = entry.Value?.Kit ?? known.Kit;
            known.Powerup = entry.Value?.Powerup ?? known.Powerup;
            Picked[steam64] = known;
        }

        if (fresh) {
            Board.Reset();
            Logger.Log($"Holding match {answer.MatchId}, waiting on {answer.WaitingOn.Count} player(s) to finish choosing.");
        }

        foreach (SteamPlayer client in Provider.clients.ToList()) {
            if (client.player != null && Held != null && Held.HasPlayer(client.playerID.steamID.m_SteamID)) OfferPicks(client);
        }
    }

    /// The board for whatever this player still has to choose: the kit first, then
    /// the powerup, each on the five-tile board a round's options go on. A player
    /// with nothing left to choose gets no board, and a board they answer here is
    /// sent to the site the same way the match page sends one.
    private static void OfferPicks(SteamPlayer viewer) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (!Picked.TryGetValue(steam64, out PSPicked picked)) Picked[steam64] = picked = new PSPicked();
        PSOptions options = Options ?? new PSOptions();

        if (options.Kits.Count > 1 && picked.Kit == null) {
            if (Board.IsShowing(viewer, BoardFace.Pick) && Board.PickIndex(steam64) < 0) return;
            List<BoardOption> kits = options.Kits.Select(kit => new BoardOption { Name = Title(kit) }).ToList();
            Board.Open(viewer, kits, "Kit", BoardFace.Pick, taken => {
                picked.Kit = options.Kits[kits.IndexOf(taken)];
                RemoteAPI.Decide(steam64, picked.Kit, null);
                OfferPicks(viewer);
            });
            return;
        }

        if (options.Powerups.Count > 1 && picked.Powerup == null) {
            if (Board.IsShowing(viewer, BoardFace.Pick) && Board.PickIndex(steam64) < 0) return;
            List<BoardOption> powerups = options.Powerups.Select(powerup => new BoardOption {
                Name = powerup.Name,
                Items = powerup.Items.Select(Item).ToList()
            }).ToList();
            Board.Open(viewer, powerups, "Powerup", BoardFace.Pick, taken => {
                picked.Powerup = options.Powerups[powerups.IndexOf(taken)].Name;
                RemoteAPI.Decide(steam64, null, picked.Powerup);
                OfferPicks(viewer);
            });
            return;
        }

        Board.CloseFor(viewer);
    }

    private static string Title(string word) {
        return string.IsNullOrEmpty(word) ? "" : char.ToUpperInvariant(word[0]) + word.Substring(1);
    }

    /// The seconds the site is still willing to wait, on every board that is up.
    private static void TickPicking(float now) {
        if (now - LastPickCountdown < 1f) return;
        LastPickCountdown = now;

        long left = SelectionEndsAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Board.Countdown((int)Math.Max(0, left));
    }

    private static void StartMatch(PSMatch match) {
        if (match == null) {
            Logger.LogError("Told to host a match with no match data!");
            return;
        }
        // The site only ever hands a match over once, so reaching here twice would mean
        // a duplicate answer — restarting the round would kick everyone already playing.
        if (Status == EPSStatus.IN_PROGRESS && Data?.Id == match.Id) return;

        IGamemode mode = Gamemodes.Gamemodes.For(match.Gamemode?.Name);
        if (mode == null) {
            Logger.LogError($"No gamemode plugin is installed to run \"{match.Gamemode?.Name}\". "
                + "Install PeekSeek ARENA or another gamemode. The match is not started.");
            return;
        }

        HeldMatchId = match.Id;
        UpdateStatus(EPSStatus.IN_PROGRESS);
        Held = null;
        Options = null;
        Picked.Clear();
        Board.Close();
        Board.Reset();

        ConfigurationManager.SetBulletdrop(match.Settings.Ballistics);
        ConfigurationManager.SetOldMovement(match.Settings.Movement);

        MatchReady = Time.realtimeSinceStartup;

        // Whoever is here for this match stays; they arrived while it was being
        // chosen and picked on the board. Everybody else goes.
        foreach (SteamPlayer client in Provider.clients.ToList()) {
            ulong steam64 = client.playerID.steamID.m_SteamID;
            if (match.HasPlayer(steam64) || (match.Spectators?.Contains(steam64) ?? false)) continue;
            Provider.kick(client.playerID.steamID, "This server is starting a new match.");
        }

        Take(match, mode);

        foreach (SteamPlayer client in Provider.clients.ToList()) {
            if (client.player != null) Admit(client.player);
        }

        Logger.Log($"Hosting match {match.Id} ({match.Gamemode?.Name}, run by {mode.Name}) on {match.Settings.Map}, "
            + $"first to {match.RoundsToWin} rounds. Waiting for {Data.Teams.Sum(team => team.Players.Count)} player(s).");
        SendUpdateState();
    }

    /// Hold a match and make it the gamemode's: the groups, and the match as the mode
    /// will see it. The mode itself is not begun until everybody is here.
    private static void Take(PSMatch match, IGamemode mode) {
        GroupInfos = CreateGroups(match.Teams);
        Data = match;
        Chosen = mode;
        Waiting = true;

        Match = new GamemodeMatch {
            Id = match.Id,
            Spectators = [.. match.Spectators ?? []],
            Teams = match.Teams.Select(team => new GamemodeTeam {
                Name = team.Name,
                GroupId = GetTeamGroupID(team).m_SteamID,
                Players = [.. team.Players]
            }).ToList()
        };
        foreach (PSPlayer player in match.Players ?? []) {
            Match.Kits[player.Steam64] = player.Kit.Select(Item).ToList();
        }
    }

    private static GamemodeItem Item(PSItem item) {
        return new GamemodeItem { ItemId = item.ItemID, Amount = item.Amount, State = item.State ?? [] };
    }

    /// The wait for the roster, measured from the hand-over. It ends early the moment
    /// everybody is here, and when it runs out: nobody here releases the match, one
    /// team here takes it, and anything else starts short-handed.
    private static void TickWait(float now) {
        if (!Waiting) return;

        if (HaveAllPlayersJoined()) {
            Begin();
            return;
        }

        if (PeekSeek.Config.LobbyTimeoutSeconds - (now - MatchReady) > 0) return;

        if (!HasAnyPlayerJoined()) {
            AbandonMatch();
            return;
        }

        PSTeam onlyTeam = OnlyTeamOnline();
        if (onlyTeam != null) {
            NotifyTeamForcedWin(onlyTeam);
            return;
        }

        Begin();
    }

    /// Seconds of the wait left, for the bar's clock.
    private static int WaitSecondsLeft => Mathf.Max(0, Mathf.CeilToInt(PeekSeek.Config.LobbyTimeoutSeconds - (Time.realtimeSinceStartup - MatchReady)));

    private static void Begin() {
        Waiting = false;
        Running = Chosen;
        Running.Begin(Match);
        RefreshHud();
    }

    /// The site says this match is no longer a match. Nobody is coming, so everything
    /// about it is dropped and this server goes back to asking for work.
    private static void AbortMatch(string reason) {
        Logger.LogWarning($"Match {HeldMatchId} aborted by the site ({reason ?? "no reason given"}).");
        KickAllClients("This match was cancelled. Head back to the lobby.");
        Held = null;
        Options = null;
        Picked.Clear();
        ResetMatchState();
        RelayAllClients();
        HeldMatchId = 0;
        UpdateStatus(EPSStatus.READY);

        // A cancelled match empties this server exactly as a finished one does, so it is
        // the same safe moment to go down and come back on the current build. Left out,
        // a server that was handed nothing but cancelled matches would keep an old build
        // until its uptime ceiling, which on a quiet region can be the whole day.
        WantRestart("a match was cancelled");
    }

    /// The site has the result, so this server owes it nothing more and is free again.
    internal static void OnResultAcknowledged(int matchId) {
        if (HeldMatchId == matchId) HeldMatchId = 0;
    }

    // ---- players --------------------------------------------------------------

    private static void OnClientConnected(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        RemoteAPI.FetchPlayer(steamID.m_SteamID);

        if (!BelongsHere(steamID)) {
            RelayToLobby(player);
            return;
        }

        // Here for a match that is still being chosen: nothing to run yet, only a
        // board for whatever they have left to choose.
        if (Status == EPSStatus.WAITING) {
            if (Held.HasPlayer(steamID.m_SteamID)) OfferPicks(player.channel.owner);
            return;
        }

        Admit(player);
    }

    /// Take one of the match's own into it: a spectator into the spectator group and
    /// out of the way, a player into their team's group and the gamemode's hands.
    private static void Admit(Player player) {
        CSteamID steamID = player.channel.owner.playerID.steamID;

        if (IsSpectator(steamID)) {
            UnturnedPlayer unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            R.Permissions.GetGroups(unturnedPlayer, true).ForEach(r => R.Permissions.RemovePlayerFromGroup(r.Id, unturnedPlayer));
            R.Permissions.AddPlayerToGroup(PeekSeek.Config.szSpectatorGroup, unturnedPlayer);

            Vector3 originalPosition = player.transform.position;
            float yaw = player.look.yaw;
            player.teleportToLocationUnsafe(new Vector3(-100, -500, -100), yaw);

            unturnedPlayer.Features.VanishMode = true;
            unturnedPlayer.Player.look.sendFreecamAllowed(true);
            unturnedPlayer.Player.look.sendSpecStatsAllowed(true);

            player.teleportToLocationUnsafe(originalPosition, yaw);

            Whisper(player.channel.owner, PeekSeek.Instance.Translate("welcome_spectator"));
            SendWelcome(player.channel.owner);
            RefreshHud();
            return;
        }

        if (player.quests.groupID != GetPlayerTeamGroupID(player)) AssignPlayerGroup(player);

        SendWelcome(player.channel.owner);
        Running?.OnPlayerJoined(player);
        RefreshHud();
    }

    /// Two lines: what this match is, then what is happening right now. Someone who
    /// reads only the first still knows the mode, the map and how long it runs for.
    private static void SendWelcome(SteamPlayer to) {
        Whisper(to, PeekSeek.Instance.Translate("welcome_match",
            Data.Gamemode?.Name ?? "PeekSeek",
            Data.Settings.Map,
            Data.RoundsToWin,
            Data.Settings.Ballistics ? "on" : "off",
            Data.Settings.Movement ? "old" : "new"));

        if (!Waiting || HaveAllPlayersJoined()) return;
        Whisper(to, PeekSeek.Instance.Translate("welcome_waiting"));
    }

    private static void OnClientDisconnected(CSteamID steamid) {
        RemoteAPI.Forget(steamid.m_SteamID);

        Player player = PlayerTool.getPlayer(steamid);
        if (player == null || Data == null) return;

        if (IsSpectator(steamid)) {
            UnturnedPlayer unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            R.Permissions.RemovePlayerFromGroup(PeekSeek.Config.szSpectatorGroup, unturnedPlayer);
        }

        Running?.OnPlayerLeft(steamid);
        RefreshHud();
        Hud.Forget(steamid.m_SteamID);
        Card.Forget(steamid.m_SteamID);
        Scores.Forget(steamid.m_SteamID);
        Tag.Forget(steamid.m_SteamID);
    }

    /// Whether this connection is one of the people this server's current match is
    /// for, in play or still being chosen. Everyone else is a stranger who typed the
    /// connect code, or a player whose match has since ended, and the funnel sends
    /// them to the lobby instead.
    private static bool BelongsHere(CSteamID steamID) {
        ulong steam64 = steamID.m_SteamID;
        if (Status == EPSStatus.WAITING) {
            return Held != null && (Held.HasPlayer(steam64) || (Held.Spectators?.Contains(steam64) ?? false));
        }

        return Data != null
            && Status == EPSStatus.IN_PROGRESS
            && (Data.HasPlayer(steam64) || IsSpectator(steam64));
    }

    /// Bounce one player straight back out to the warm-up lobby.
    ///
    /// A match server with nothing on it used to refuse the connection outright, which
    /// left whoever tried it staring at "This server is not hosting a match!" in the
    /// server browser with nowhere to go. Every one of these servers is now a funnel:
    /// join one at the wrong moment and you land in the lobby, which is where matches
    /// are made anyway. The kick is what actually moves them, the relay is what tells
    /// their client where to reconnect, so the two go together.
    private static void RelayToLobby(Player player) {
        if (RelaySteamID == CSteamID.Nil) {
            Provider.kick(player.channel.owner.playerID.steamID, "This server is not hosting a match!");
            return;
        }

        player.sendRelayToServer(RelaySteamID, string.Empty, true);
        Provider.kick(player.channel.owner.playerID.steamID, "Relay");
    }

    private static void OnCheckValidWithExplanation(ValidateAuthTicketResponse_t callback, ref bool isValid, ref string explanation) {
        ulong ulSteamID = callback.m_SteamID.m_SteamID;

        if (BelongsHere(callback.m_SteamID)) return;

        // Someone on this server's match who has arrived before this server has heard
        // of it is the one case that stays a refusal. The lobby relays them the moment
        // the site names this server, and this server only agrees a beat later, so
        // funnelling them back would bounce them between the two for that beat.
        if (Data != null && Data.HasPlayer(ulSteamID)) {
            isValid = false;
            explanation = "The match has not started!";
            return;
        }

        // Everyone else is let in and immediately relayed out by OnClientConnected.
        // Letting them connect is the only way to send them anywhere: sendRelayToServer
        // needs a Player, and a rejected connection never becomes one.
    }

    private static List<GroupInfoEx> CreateGroups(List<PSTeam> teams) {
        List<GroupInfoEx> infos = [];

        GroupManager.knownGroups.Clear();

        foreach (var team in teams) {
            GroupInfo sdgInfo = GroupManager.addGroup(
                GroupManager.generateUniqueGroupID(),
                team.Name
            );

            if (sdgInfo == null) {
                Logger.LogError($"Failed to create group for {team.Name}");
                continue;
            }

            infos.Add(new GroupInfoEx() {
                TeamName = team.Name,
                GroupInfo = sdgInfo
            });
        }

        return infos;
    }

    /// Strip a player and dress them in the kit the match holds for them, at full
    /// health.
    ///
    /// Full health with the kit, because passive regen is off here on purpose and
    /// nothing else would ever give it back. Players arrive relayed from the warm-up
    /// lobby on whatever health they left it with, and would otherwise start a ranked
    /// round already down and with no way up. Kitting someone is the one moment that
    /// means "you are about to fight", so it is the moment worth topping them up.
    internal static void AssignPlayerKit(Player player) {
        List<PSItem> kit = Data?.KitFor(player.channel.owner.playerID.steamID.m_SteamID) ?? [];

        for (byte i = 0; i < PlayerInventory.PAGES - 1; i++) {
            byte count = player.inventory.getItemCount(i);

            for (byte j = 0; j < count; j++) {
                player.inventory.removeItem(i, 0);
            }
        }

        player.clothing.updateClothes(
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, []
        );

        foreach (PSItem item in kit) {
            for (byte i = 0; i < item.Amount; i++) {
                player.inventory.forceAddItem(new Item(item.ItemID, true) { state = item.State }, true);
            }
        }

        player.life.serverModifyHealth(100);
    }

    private static void AssignPlayerGroup(Player player) {
        player.quests.leaveGroup(true);
        GroupManager.alertGroupmatesLeft(player);

        player.quests.ServerAssignToGroup(
            GroupInfos.First(
                x => x.TeamName == GetPlayerTeam(player.channel.owner.playerID.steamID.m_SteamID).Name
            ).GroupInfo.groupID,
            EPlayerGroupRank.MEMBER,
            true
        );
    }

    private static void KickAllClients(string msg = "You have been kicked!") {
        foreach (var client in Provider.clients.ToList()) {
            Provider.kick(
                client.playerID.steamID,
                msg
            );
        }
    }

    /// Hands every client over to the lobby server before dropping them, when one is configured.
    private static void RelayAllClients() {
        foreach (var client in Provider.clients.ToList()) {
            if (RelaySteamID == CSteamID.Nil || client.player == null) continue;
            client.player.sendRelayToServer(RelaySteamID, string.Empty, true);
        }

        KickAllClients("Relay");
    }

    internal static void Announce(string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            null,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL,
            true
        );
    }

    /// Ask the reported player's client for a screenshot, then send it to staff with
    /// their numbers for this match attached.
    ///
    /// `CSteamID.Nil` as the destination is what makes this a server-side capture: the
    /// built-in /spy passes the requesting admin so the image is relayed to their
    /// client, and there is no admin here to relay it to. The callback is where the
    /// bytes actually arrive.
    ///
    /// The counters are read here, on the game thread, rather than inside the callback.
    /// A screenshot can take seconds to come back, and StatService is being written to
    /// by every shot fired in the meantime.
    internal static void Report(CSteamID reporter, SteamPlayer target, string reason) {
        CSteamID reported = target.playerID.steamID;

        RemoteAPI.Report payload = new() {
            Reported = reported.m_SteamID,
            Reporter = reporter.m_SteamID,
            Reason = reason,
            DamageDealt = Mathf.RoundToInt(StatService.GetStatDamageDealt(reported)),
            ShotsFired = StatService.GetStatShotsFired(reported),
            ShotsHit = StatService.GetStatHits(reported),
            HeadshotHits = StatService.GetStatHeadshotHits(reported)
        };

        target.player.sendScreenshot(CSteamID.Nil, (_, image) => {
            payload.Screenshot = image;
            RemoteAPI.SendReport(payload);
        });
    }

    /// Same as Announce but to one player only.
    internal static void Whisper(SteamPlayer to, string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            to,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL,
            true
        );
    }

    private static PSTeam GetPlayerTeam(ulong steamID) {
        return Data.Teams.First(
            x => x.Players.Contains(steamID)
        );
    }

    private static CSteamID GetPlayerTeamGroupID(Player player) {
        return GetPlayerTeamGroupID(player.channel.owner.playerID.steamID);
    }

    internal static CSteamID GetPlayerTeamGroupID(CSteamID steamID) {
        return GetTeamGroupID(GetPlayerTeam(steamID.m_SteamID));
    }

    internal static CSteamID GetTeamGroupID(PSTeam team) {
        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.TeamName == team.Name
        );
        return info != null ? info.GroupInfo.groupID : CSteamID.Nil;
    }

    internal static string GetTeamGroupName(CSteamID groupID) {
        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.GroupInfo.groupID == groupID
        );

        return info != null ? info.TeamName : "NONE_TEAM_NULL";
    }

    private static bool IsOnline(ulong steam64) {
        return Provider.clients.Any(client => client.playerID.steamID.m_SteamID == steam64);
    }

    internal static bool HaveAllPlayersJoined() {
        return Data.Teams.SelectMany(team => team.Players).All(IsOnline);
    }

    internal static bool HasAnyPlayerJoined() {
        return Data.Teams.SelectMany(team => team.Players).Any(IsOnline);
    }

    /// The single team that still has players on the server, or null when it is not exactly one.
    internal static PSTeam OnlyTeamOnline() {
        List<PSTeam> online = Data.Teams.Where(team => team.Players.Any(IsOnline)).ToList();
        return online.Count == 1 ? online[0] : null;
    }

    /// Everyone on the server who is playing this match: no spectators, no strangers.
    internal static IEnumerable<Player> Players() {
        return Provider.clients.ToList()
            .Where(client => client.player != null && Match != null && Match.HasPlayer(client.playerID.steamID.m_SteamID))
            .Select(client => client.player);
    }

    private static void UpdateStatus(EPSStatus status) {
        Status = status;
    }

    // ---- the score ------------------------------------------------------------

    /// Rounds a team has won. Wins are kept per player, and every player on a team is
    /// credited with the team's rounds, so any of them says it; a team with nobody on
    /// it has none.
    internal static int Score(PSTeam team) {
        return team.Players.Count == 0 ? 0 : StatService.GetStatWins(new CSteamID(team.Players[0]));
    }

    internal static int Score(int team) {
        return Data != null && Data.Teams.Count > team ? Score(Data.Teams[team]) : 0;
    }

    /// Rounds played so far, which is every team's wins added up. Not a sum over
    /// players: a team of two winning one round is one round, not two.
    internal static int RoundsPlayed => Data?.Teams.Sum(Score) ?? 0;

    /// The round goes to a team. Scored for every player on it, announced, and sent
    /// to the overlay. `announcement` replaces the plain line for a gamemode with
    /// something more to say about how it was won.
    internal static void RoundWon(int team, string announcement = null) {
        if (Status != EPSStatus.IN_PROGRESS || Data == null || Data.Teams.Count <= team) return;
        PSTeam winner = Data.Teams[team];

        foreach (ulong user in winner.Players) {
            StatService.UpdateStatWins(new(user));
        }

        Banner.Show($"TAKES ROUND {RoundsPlayed}", winner.Name, announcement ?? AceLine(winner));
        RecordEvent("roundWon", winner.Name);
        SendUpdateState();
        RoundLog.Clear();
        RefreshHud();
    }

    /// The gamemode calls the match. Everybody gets a moment to see the result, then
    /// the site gets it and everybody goes home.
    internal static void MatchWon(int team) {
        if (Status != EPSStatus.IN_PROGRESS || Data == null || Data.Teams.Count <= team) return;
        PSTeam winner = Data.Teams[team];

        UpdateStatus(EPSStatus.DELAY);
        Banner.Show($"WINS THE MATCH {ScoreLine()}", $"<color=#ff4f4f>{winner.Name}</color>", MvpLine());
        RecordEvent("matchWon", winner.Name);
        SendUpdateState();
        RoundLog.Clear();
        RefreshHud();

        // The match is over the moment it is won: everybody stands where they are and
        // looks at the numbers until the server sends them home.
        foreach (SteamPlayer client in Provider.clients.ToList()) {
            if (client.player != null) Freeze(client.player, true);
        }
        ScoresLocked = true;
        Scores.ShowAll(Sheet());
        Task.Run(() => DelayEnd(PeekSeek.Config.DelayKickAfterWin, winner.Name));
    }

    /// Root a player to the spot, or let them go. Speed and jump both, because a
    /// standing jump is a way to move.
    internal static void Freeze(Player player, bool frozen) {
        player.movement.sendPluginSpeedMultiplier(frozen ? 0f : 1f);
        player.movement.sendPluginJumpMultiplier(frozen ? 0f : 1f);
    }

    /// The score, in team order, as "2-1" — or with names when there are more than two
    /// teams and a bare pair of numbers would not say whose is whose.
    internal static string ScoreLine() {
        if (Data?.Teams == null || Data.Teams.Count == 0) return string.Empty;

        IEnumerable<string> parts = Data.Teams.Select(team => Score(team).ToString());
        if (Data.Teams.Count > 2) {
            parts = Data.Teams.Select(team => $"{team.Name} {Score(team)}");
        }
        return string.Join(Data.Teams.Count > 2 ? "    " : "-", parts);
    }

    /// The round banner's line when one player took the whole other side, or null.
    /// A solo ace only means something in a team format, a 1v1 win is just a win, and
    /// only generalises cleanly to two teams.
    private static string AceLine(PSTeam winningTeam) {
        if (Data.Teams.Count != 2) return null;

        PSTeam losingTeam = Data.Teams.First(t => t.Name != winningTeam.Name);
        if (losingTeam.Players.Count < 2) return null;

        foreach (ulong user in winningTeam.Players) {
            if (RoundLog.KillsBy(new CSteamID(user)) < losingTeam.Players.Count) continue;
            return $"Ace by {NameOf(user)}";
        }
        return null;
    }

    /// The match banner's line: most kills, headshots as the tiebreak. Null for a
    /// match that ended without a single kill rather than naming an MVP who did
    /// nothing.
    private static string MvpLine() {
        PSPlayerStat mvp = Mvp();
        if (mvp == null) return null;

        return $"MVP {NameOf(mvp.SteamID)}    {mvp.Kills} kills    {mvp.Headshots * 100 / mvp.Kills}% HS";
    }

    private static PSPlayerStat Mvp() {
        if (StatService.InstanceStats.Count == 0) return null;

        PSPlayerStat mvp = StatService.InstanceStats
            .OrderByDescending(s => s.Kills)
            .ThenByDescending(s => s.Headshots)
            .First();
        return mvp.Kills == 0 ? null : mvp;
    }

    internal static string NameOf(ulong steam64) {
        return PlayerTool.getSteamPlayer(new CSteamID(steam64))?.playerID.characterName ?? steam64.ToString();
    }

    /// Awards the whole match to the only team that showed up, or the only one left.
    internal static void NotifyTeamForcedWin(PSTeam team) {
        if (Status != EPSStatus.IN_PROGRESS) return;

        for (byte round = 0; round < Data.RoundsToWin; round++) {
            foreach (ulong user in team.Players) {
                StatService.UpdateStatWins(new(user));
            }
        }

        Banner.Show("TAKES THE MATCH", team.Name, "Nobody from the other side turned up");
        EndMatch(team.Name);
    }

    // ---- the scoreboard ---------------------------------------------------------

    /// The board as it stands: the gamemode over whether the Elo moves and the map, a
    /// heading per team with its rounds, a row per player on the roster with their
    /// numbers this match, the MVP tagged.
    private static ScoreSheet Sheet() {
        ScoreSheet sheet = new() {
            Title = Data?.Gamemode?.Name ?? "",
            Sub = MatchLine(),
            Timer = Running?.SecondsLeft is int left and >= 0 ? $"{left / 60}:{left % 60:00}" : null
        };
        if (Data == null) return sheet;

        PSPlayerStat mvp = Mvp();
        foreach (PSTeam team in Data.Teams) {
            sheet.Rows.Add(ScoreRow.Head($"{team.Name.ToUpperInvariant()}    {Score(team)}"));
            foreach (ulong steam64 in team.Players) {
                CSteamID id = new(steam64);
                byte kills = StatService.GetStatKills(id);
                int hs = kills > 0 ? StatService.GetStatHeadshots(id) * 100 / kills : 0;
                sheet.Rows.Add(new ScoreRow {
                    Steam64 = steam64,
                    AvatarUrl = RemoteAPI.AvatarFor(steam64),
                    Name = NameOf(steam64),
                    Tag = mvp != null && mvp.SteamID == steam64 ? "MVP" : null,
                    Values = [
                        kills.ToString(),
                        StatService.GetStatDeaths(id).ToString(),
                        hs.ToString(),
                        Mathf.RoundToInt(StatService.GetStatDamageDealt(id)).ToString(),
                        PingOf(id)
                    ]
                });
            }
        }
        return sheet;
    }

    /// A player's ping, or nothing at all for one who is not here: the roster is on
    /// the board whether or not everybody turned up.
    private static string PingOf(CSteamID steamID) {
        SteamPlayer client = PlayerTool.getSteamPlayer(steamID);
        return client == null ? "" : Ping.Milliseconds(client).ToString();
    }

    /// Whether the Elo moves, and the map, which is the line every match board
    /// carries. A site too old to say whether the gamemode is ranked leaves the word
    /// off rather than guessing at somebody's Elo.
    private static string MatchLine() {
        string map = Data?.Settings?.Map ?? "";
        return Data?.Gamemode?.UsesElo switch {
            true => $"Ranked   {map}",
            false => $"Unrated   {map}",
            _ => map
        };
    }

    /// The board on a held plugin key: up while the first one is down, gone when it
    /// comes up. Once a match is over the board is everybody's and stays.
    private static void OnPluginKey(Player player, uint simulation, byte key, bool state) {
        if (key != 0 || Data == null || ScoresLocked) return;

        SteamPlayer viewer = player.channel.owner;
        bool up = Scores.IsUp(viewer.playerID.steamID.m_SteamID);
        if (state && !up) Scores.Show(viewer, Sheet());
        else if (!state && up) Scores.Hide(viewer);
    }

    /// Reports a match nobody turned up for, so the backend can release it.
    internal static void AbandonMatch() {
        if (Status != EPSStatus.IN_PROGRESS) return;

        SendResults(string.Empty, isPlayed: false);
        ClearMatch();
    }

    private static async Task DelayEnd(float seconds, string winningTeam) {
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TaskDispatcher.QueueOnMainThread(() => {
            if (Status != EPSStatus.DELAY) {
                Logger.LogError($"DelayEnd() ran with status {Status}, expected {EPSStatus.DELAY}");
                return;
            }

            EndMatch(winningTeam);
        });
    }

    /// Notes that this server would like to go down. Nothing happens immediately —
    /// TickRestart decides when it is actually safe.
    private static void WantRestart(string why) {
        if (WantsRestart) return;

        WantsRestart = true;
        Logger.Log($"Restart wanted ({why}). Going down once this server is holding nothing.");
    }

    /// Takes the server down for updates, but only ever while it is holding nothing.
    ///
    /// Two conditions have to hold and keep holding. The status must be READY with no
    /// HeldMatchId — a held match, including a finished one whose result the site has
    /// not accepted yet, means somebody is still waiting on this server. And it must
    /// have spent DrainSeconds not asking for work, because a poll already in flight
    /// when the decision was made can still come back holding a match; if one does,
    /// the restart is abandoned and the server goes back to asking.
    private static void TickRestart(float now) {
        if (!PeekSeek.Config.RestartWhenIdle) return;

        if (!WantsRestart && now - StartedAt >= PeekSeek.Config.MaxUptimeMinutes * 60f) {
            WantRestart($"it has been up {PeekSeek.Config.MaxUptimeMinutes} minutes");
        }
        if (!WantsRestart) return;

        bool idle = Status == EPSStatus.READY && HeldMatchId == 0;
        if (!idle) {
            // Took one after all, so this is no longer a moment to go.
            if (DrainingSince != 0) {
                DrainingSince = 0;
                RemoteAPI.ResumeAskingForWork();
                Logger.Log("Restart put off — this server was handed a match while draining.");
            }
            return;
        }

        if (DrainingSince == 0) {
            DrainingSince = now;
            RemoteAPI.StopAskingForWork();
            Logger.Log($"Draining: no longer asking for matches, going down in {PeekSeek.Config.DrainSeconds}s.");
            return;
        }

        if (now - DrainingSince < PeekSeek.Config.DrainSeconds) return;

        Logger.Log("Shutting down to come back on the current build.");
        Provider.shutdown();
    }

    private static void EndMatch(string winningTeam) {
        SendResults(winningTeam);
        ClearMatch();
    }

    private static void ClearMatch() {
        ResetMatchState();
        UpdateStatus(EPSStatus.READY);
        RelayAllClients();

        // The gap after a match is the only moment this server is both empty and
        // certain to be empty, which makes it the one safe moment to go down and
        // come back on whatever the current build is.
        WantRestart("a match finished");
    }

    /// Everything a finished match leaves behind. HeldMatchId deliberately survives:
    /// this server still owes the site a result, and the site keeps the match open for
    /// exactly as long as this server keeps naming it.
    private static void ResetMatchState() {
        Running?.End();
        Running = null;
        Chosen = null;
        Waiting = false;
        Board.Close();
        Board.Reset();
        Hud.Hide();
        Banner.Clear();
        Card.Clear();
        Scores.HideAll();
        ScoresLocked = false;
        foreach (SteamPlayer client in Provider.clients.ToList()) {
            if (client.player != null) Freeze(client.player, false);
        }

        StatService.InstanceStats.Clear();
        LatestEvent = null;
        EventsRecorded = 0;
        KillEventsRecorded = 0;
        RecentKills.Clear();
        FightTracker.Clear();
        RoundLog.Clear();
        VisibilityTracker.Clear();
        GroupManager.knownGroups.Clear();
        Data = null;
        Match = null;
    }

    /// Reports every player on the roster, not only the ones who turned up: a player the
    /// server leaves out is a player whose Elo never moves, which is what used to make
    /// walking out of a losing match free.
    internal static void SendResults(string winningTeam, bool isPlayed = true) {
        var payload = new {
            played = isPlayed,
            winningTeam,
            players = isPlayed ? RosterResults(winningTeam) : []
        };

        RemoteAPI.QueueResult(Data.Id, JsonConvert.SerializeObject(payload));
    }

    private static object[] RosterResults(string winningTeam) {
        return Data.Teams
            .SelectMany(team => team.Players.Select(steam64 => new { team, steam64 }))
            .Select(entry => {
                CSteamID steamID = new(entry.steam64);
                return (object)new {
                    steam64 = entry.steam64.ToString(),
                    won = entry.team.Name == winningTeam,
                    kills = StatService.GetStatKills(steamID),
                    deaths = StatService.GetStatDeaths(steamID),
                    headshotKills = StatService.GetStatHeadshots(steamID),
                    hits = StatService.GetStatHits(steamID),
                    headshotHits = StatService.GetStatHeadshotHits(steamID),
                    damageDealt = (int)Math.Round(StatService.GetStatDamageDealt(steamID)),
                    damageTaken = (int)Math.Round(StatService.GetStatDamageTaken(steamID))
                };
            })
            .ToArray();
    }

    // ---- deaths ---------------------------------------------------------------

    /// Ordinary kills say nothing publicly; the kill feed carries them. What the one
    /// who died gets is the card: who, with what, from how far, what they had left and
    /// what they did to them, which is the record the site keeps, shown while it still
    /// matters.
    ///
    /// A death to the arena, which is also what a gamemode's own ground damage is, is
    /// not a death for the stats or the site's kill log, as it never was; it still takes
    /// the player out of the round and is still the gamemode's to answer.
    private static void OnPlayerDead(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        if (Data == null) return;

        StatService.MarkDeadThisRound(player.CSteamID);
        if (cause != EDeathCause.ARENA) RecordDeath(player, cause, limb, murderer);
        else Card.Show(player.Player.channel.owner, new DeathInfo { Cause = "OUT OF BOUNDS" });

        Running?.OnPlayerDied(player.Player, murderer);
        RefreshHud();
        SendUpdateState();
    }

    private static void RecordDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        StatService.UpdateStatDeaths(player.CSteamID);
        Logger.Log($"[alive-check] {player.CharacterName} ({player.CSteamID}) died (cause={cause}, limb={limb}) — marked dead this round.");

        UnturnedPlayer unturnedInstigator = null;
        if (murderer != CSteamID.Nil && murderer != SteamGameServer.GetSteamID() && murderer != player.CSteamID) {
            unturnedInstigator = UnturnedPlayer.FromCSteamID(murderer);
            StatService.UpdateStatKills(murderer);
            if (limb == ELimb.SKULL) StatService.UpdateStatHeadshots(murderer);
            RoundLog.RecordKill(murderer);
        }

        Card.Show(player.Player.channel.owner, DeathOf(player, cause, limb, unturnedInstigator));
        RecordKill(player, unturnedInstigator, limb);
        SendKillEvent(player, unturnedInstigator, cause, limb, murderer);
    }

    /// The card for one death. With a killer: who, what they held, whether it was the
    /// head, how far, what they had left and what the victim had done to them this
    /// round. Without one: the cause, in the few words the game can name.
    private static DeathInfo DeathOf(UnturnedPlayer victim, EDeathCause cause, ELimb limb, UnturnedPlayer killer) {
        if (killer == null) return new DeathInfo { Cause = DeathCauseLabel(cause) };

        RoundDamageEntry given = RoundLog.DamageBy(victim.CSteamID)
            .FirstOrDefault(entry => entry.Victim == killer.CSteamID.m_SteamID);
        string line = $"They had {killer.Player.life.health} HP left";
        if (given != null) line += $"    You dealt {Mathf.RoundToInt(given.Damage)} in {given.Hits} hit{(given.Hits == 1 ? "" : "s")}";

        return new DeathInfo {
            Killer = killer.CharacterName,
            Weapon = KillFeed.Weapon(cause, killer.Player),
            Headshot = limb == ELimb.SKULL,
            Distance = Mathf.RoundToInt(Vector3.Distance(killer.Player.transform.position, victim.Player.transform.position)),
            Line = line
        };
    }

    /// Only the causes realistic in a match get a word of their own; the rest fall back
    /// to the plain one rather than growing this list for causes that never fire.
    internal static string DeathCauseLabel(EDeathCause cause) {
        return cause switch {
            EDeathCause.BONES => "FELL",
            EDeathCause.WATER => "DROWNED",
            EDeathCause.BURNING => "BURNED",
            EDeathCause.FREEZING => "FROZE",
            EDeathCause.BLEEDING => "BLED OUT",
            EDeathCause.SUICIDE => "SUICIDE",
            EDeathCause.GRENADE or EDeathCause.LANDMINE or EDeathCause.MISSILE or EDeathCause.CHARGE => "BLOWN UP",
            _ => "DIED"
        };
    }

    /// Builds the durable kill record for the site from whatever FightTracker buffered,
    /// separate from RecordKill's ephemeral overlay feed entry.
    private static void SendKillEvent(UnturnedPlayer victim, UnturnedPlayer killer, EDeathCause cause, ELimb limb, CSteamID murderer) {
        bool ballistics = Data.Settings.Ballistics;

        RemoteAPI.SendKill(Data.Id, new PSKillEvent {
            Id = ++KillEventsRecorded,
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Map = Data.Settings.Map,
            Round = RoundsPlayed + 1,
            Killer = killer?.CSteamID.ToString(),
            KillerName = killer?.CharacterName,
            Victim = victim.CSteamID.ToString(),
            VictimName = victim.CharacterName,
            Cause = cause.ToString().ToLowerInvariant(),
            Limb = limb.ToString().ToLowerInvariant(),
            Weapon = killer?.Player.equipment.itemID ?? 0,
            KillerPosition = killer != null ? PSVector3.From(killer.Player.transform.position) : null,
            VictimPosition = PSVector3.From(victim.Player.transform.position),
            Hits = FightTracker.TakeFight(victim.CSteamID),
            ReciprocalHits = murderer != CSteamID.Nil
                ? FightTracker.TakeHitsFrom(murderer, victim.CSteamID)
                : [],
            Shots = murderer != CSteamID.Nil && ballistics ? FightTracker.RecentShotsBy(murderer) : []
        });
    }

    private static void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle) {
        if (Data == null) return;

        Running?.OnPlayerRevived(player.Player);
        RefreshHud();
    }

    /// Safety net: if a death is ever resolved without UnturnedPlayerEvents.OnPlayerDeath
    /// firing, this still marks the player out of the round, so the overlay and the bar
    /// do not keep showing them alive after they respawn.
    private static void OnLifeUpdated(Player player) {
        if (Data == null || !player.life.isDead) return;

        CSteamID steamID = player.channel.owner.playerID.steamID;
        if (!StatService.IsAliveThisRound(steamID)) return;

        Logger.LogWarning($"[alive-check] {player.channel.owner.playerID.characterName} ({steamID}) was dead without OnPlayerDead firing — marking dead now so the overlay does not show them as alive.");
        StatService.MarkDeadThisRound(steamID);
        RefreshHud();
    }

    // ---- the heartbeat --------------------------------------------------------------

    /// Pushes a snapshot on a timer as well as on events, so health, held weapon and
    /// remaining powerup items move on the overlay between kills rather than freezing
    /// until somebody dies. Also drives the visibility scan reaction time reads from, on
    /// its own shorter interval — waiting a full second to notice someone came into view
    /// would make the numbers meaningless.
    internal static void Tick() {
        float now = Time.realtimeSinceStartup;

        // Ahead of the Data guard: a server with no match is exactly the one that
        // should be thinking about going down for updates.
        TickRestart(now);
        Feed.Tick();
        Banner.Tick();
        Card.Tick();

        if (Status == EPSStatus.WAITING) TickPicking(now);
        if (Data == null) return;

        TickWait(now);
        if (Data == null) return;

        Running?.Tick();

        if (now - LastHudTick >= HudIntervalSeconds) {
            LastHudTick = now;
            Hud.Tick(HudState());
        }

        if (now - LastVisibilityCheck >= VisibilityIntervalSeconds) {
            LastVisibilityCheck = now;
            VisibilityTracker.Tick(Data.Teams);
        }

        if (now - LastDebugStateLogged >= DebugStateLogIntervalSeconds) {
            LastDebugStateLogged = now;
            LogPlayerStates();
        }

        if (now - LastUpdateSent < UpdateIntervalSeconds) return;

        SendUpdateState();
    }

    /// Prints exactly what LiveState.RoundState would tell the overlay for every roster
    /// player, on its own 10s cadence, so alive/dead can be watched from the server console
    /// alone — no browser, no OBS, no waiting for the next kill/round event to see a change.
    private static void LogPlayerStates() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Players) {
                CSteamID steamID = new(user);
                Player player = PlayerTool.getPlayer(steamID);

                if (player == null) {
                    Logger.Log($"[alive-check] {steamID} => offline");
                    continue;
                }

                string state = LiveState.RoundState(steamID, player);
                string name = player.channel.owner.playerID.characterName;
                Logger.Log($"[alive-check] {name} ({steamID}) => {state} "
                    + $"(deadThisRound={!StatService.IsAliveThisRound(steamID)}, isDead={player.life.isDead}, isSafe={player.movement.isSafe})");
            }
        }
    }

    /// Appends to the rolling kill feed, keeping only what the overlay draws. The killer
    /// is null for a death nobody caused, which the overlay renders without an attacker.
    private static void RecordKill(UnturnedPlayer victim, UnturnedPlayer killer, ELimb limb) {
        RecentKills.Add(new PSUpdateKill {
            Id = ++EventsRecorded,
            Killer = killer?.CharacterName,
            Victim = victim.CharacterName,
            Weapon = killer?.Player.equipment.itemID ?? 0,
            Limb = limb.ToString().ToLowerInvariant()
        });

        if (RecentKills.Count > FeedLength) RecentKills.RemoveAt(0);
    }

    /// Records a result for the overlay to announce. The id increases so the overlay can
    /// tell a new result from the same one being repeated by every later tick.
    private static void RecordEvent(string kind, string teamName) {
        LatestEvent = new PSUpdateEvent {
            Id = ++EventsRecorded,
            Kind = kind,
            Team = teamName,
            Round = RoundsPlayed
        };
    }

    private static void SendUpdateState() {
        if (Data?.Teams == null) {
            Logger.LogError("SendUpdateState() has no match data to report!");
            return;
        }

        LastUpdateSent = Time.realtimeSinceStartup;
        RemoteAPI.SendState(Data.Id, LiveState.Build(Data, RoundsPlayed, LatestEvent, RecentKills));
    }

    // ---- the bar ------------------------------------------------------------------

    /// Redraw the bar for everybody, from what the match and the gamemode say now.
    internal static void RefreshHud() {
        if (Data == null) return;
        Hud.Show(HudState());
    }

    private static HudState HudState() {
        HudState state = new() {
            SecondsLeft = Running?.SecondsLeft ?? (Waiting ? WaitSecondsLeft : -1),
            Phase = Running?.PhaseLabel,
            Spectators = [.. Data.Spectators ?? []]
        };

        for (int team = 0; team < 2 && team < Data.Teams.Count; team++) {
            state.Teams[team].Name = Data.Teams[team].Name;
            state.Teams[team].Score = Score(team);
            state.Teams[team].Role = Running?.RoleOf(team) ?? HudRole.None;
            state.Teams[team].MatchPoint = Running?.IsMatchPoint(team) ?? false;
            state.Teams[team].Seats = Data.Teams[team].Players.Select(Seat).ToList();
        }

        return state;
    }

    private static HudSeat Seat(ulong steam64) {
        Player player = PlayerTool.getPlayer(new CSteamID(steam64));
        int ping = player == null ? 0 : Ping.Milliseconds(player.channel.owner);
        int percentage = PingPercentage(ping);

        return new HudSeat {
            Steam64 = steam64,
            AvatarUrl = RemoteAPI.AvatarFor(steam64),
            PickItemId = Running?.PickOf(steam64) ?? 0,
            Online = player != null,
            Alive = player != null && !player.life.isDead && StatService.IsAliveThisRound(new CSteamID(steam64)),
            Health = player?.life.health ?? 0,
            PingMs = ping,
            PingLevel = percentage >= 100 ? 0 : percentage > 50 ? 1 : 2
        };
    }

    // ---- spectators --------------------------------------------------------------

    public static bool IsSpectator(ulong steamID) {
        return Data?.Spectators != null && Data.Spectators.Contains(steamID);
    }

    public static bool IsSpectator(CSteamID steamID) {
        return IsSpectator(steamID.m_SteamID);
    }

    public static bool IsSpectator(Player player) {
        return player != null && IsSpectator(player.channel.owner.playerID.steamID.m_SteamID);
    }

    public static bool IsSpectator(SteamPlayer player) {
        return player != null && IsSpectator(player.playerID.steamID.m_SteamID);
    }
}
