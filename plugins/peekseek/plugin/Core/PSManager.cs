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
    private static KillFeed Feed;

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

        ConfigurationManager.DisablePassiveHealthRegen();
    }

    internal static void Shutdown() {
        if (Data != null) StopMatch();

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
        RemoteAPI.Shutdown();
    }

    private static void OnDamageRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        Player attacker = PlayerTool.getPlayer(parameters.killer);

        if (attacker != null && parameters.damage > 5) {
            int ping = Ping.Milliseconds(attacker.channel.owner);

            foreach (var config in PeekSeek.Config.PingConfig.OrderByDescending(c => c.PingOrHigher)) {
                if (!(ping >= config.PingOrHigher)) continue;
                parameters.damage *= (config.Precentage / 100f);
                TellAboutPingReduction(attacker, config.Precentage, ping);
                break;
            }
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

    private const float PingNoticeIntervalSeconds = 20f;
    private static readonly Dictionary<ulong, float> LastPingNotice = [];

    /// Ping scaling fires once per bullet and is otherwise completely invisible: the
    /// shooter sees their hits land for less than they should and concludes the server
    /// is broken. Throttled per shooter for the same reason it is worth saying at all —
    /// once is information, every bullet is noise.
    private static void TellAboutPingReduction(Player attacker, int percentage, int ping) {
        ulong steam64 = attacker.channel.owner.playerID.steamID.m_SteamID;
        float now = Time.realtimeSinceStartup;

        if (LastPingNotice.TryGetValue(steam64, out float last)
                && now - last < PingNoticeIntervalSeconds) return;
        LastPingNotice[steam64] = now;

        Whisper(attacker.channel.owner,
            PeekSeek.Instance.Translate("damage_reduced", percentage, ping));
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

        // A match started here with /start is not the site's to interrupt. The site
        // does not know it exists, so it may well hand one over; it is refused and
        // logged, and the site's own housekeeping releases it.
        if (Data != null && Match.IsLocal && answer.Action != PSSessionAnswer.Idle) {
            Logger.LogWarning($"Ignoring the site's \"{answer.Action}\" while a local match is running.");
            return;
        }

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

    /// This server has been given a match whose players are still choosing their
    /// loadouts. Nothing to run yet — just hold it, and refuse anyone who tries to join
    /// early rather than letting them in to a match that has not been set up.
    private static void HoldForMatch(PSSessionAnswer answer) {
        if (HeldMatchId == answer.MatchId && Status == EPSStatus.WAITING) return;

        HeldMatchId = answer.MatchId;
        UpdateStatus(EPSStatus.WAITING);
        Logger.Log($"Holding match {answer.MatchId} — waiting on {answer.WaitingOn.Count} player(s) to finish choosing.");
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

        ConfigurationManager.SetBulletdrop(match.Settings.Ballistics);
        ConfigurationManager.SetOldMovement(match.Settings.Movement);

        MatchReady = Time.realtimeSinceStartup;
        KickAllClients("This server is starting a new match.");

        Take(match, mode);

        Logger.Log($"Hosting match {match.Id} ({match.Gamemode?.Name}, run by {mode.Name}) on {match.Settings.Map} — "
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
            Solo = match.Solo,
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
            if (!Match.IsLocal) Announce(PeekSeek.Instance.Translate("lobby_everyone_here"));
            Begin();
            return;
        }

        float secondsLeft = PeekSeek.Config.LobbyTimeoutSeconds - (now - MatchReady);
        if (secondsLeft > 0) {
            BroadcastCountdown(secondsLeft);
            return;
        }

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

    private static float LastCountdownBroadcast;

    private static void BroadcastCountdown(float secondsLeft) {
        if (Time.realtimeSinceStartup - LastCountdownBroadcast < 1f) return;
        LastCountdownBroadcast = Time.realtimeSinceStartup;

        // Null when the config file predates this field: XmlSerializer leaves it unset rather than defaulted.
        List<ushort> broadcastAt = PeekSeek.Config.LobbyTimeoutBroadcastSecondsRemaining;

        ushort rounded = (ushort)Mathf.Round(secondsLeft);
        if (broadcastAt == null || !broadcastAt.Contains(rounded)) return;

        Announce(PeekSeek.Instance.Translate("lobby_countdown", rounded));
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

    // ---- local matches --------------------------------------------------------

    /// Start a match from whoever is on the server right now, for testing a gamemode
    /// without the site. Two teams from the people present, kits from each player's
    /// saved loadout. `solo` puts everybody on one team, for a mode that can be walked
    /// through alone. Returns the reason it could not start, or null when it did.
    internal static string StartLocal(string gamemodeName, bool solo) {
        if (Data != null) return "already_running";

        IGamemode mode = Gamemodes.Gamemodes.For(gamemodeName);
        if (mode == null || !string.Equals(mode.Name, gamemodeName, StringComparison.OrdinalIgnoreCase)) return "no_such_gamemode";

        List<SteamPlayer> present = Provider.clients.Where(client => client.player != null).ToList();
        if (present.Count == 0) return "nobody_here";
        if (!solo && present.Count < 2) return "need_two";

        PSMatch match = new() {
            Id = 0,
            Solo = solo,
            Gamemode = new PSGamemode { Name = mode.Name },
            RoundsToWin = 3,
            Settings = new PSSettings {
                Map = Level.info?.name ?? "",
                Ballistics = Provider.modeConfigData.Gameplay.Ballistics,
                Movement = Provider.modeConfigData.Gameplay.AirStrafing_Acceleration_Multiplier > 1f
            },
            Teams = [new PSTeam { Name = "North" }, new PSTeam { Name = "South" }],
            Players = [],
            Spectators = []
        };

        for (int i = 0; i < present.Count; i++) {
            ulong steam64 = present[i].playerID.steamID.m_SteamID;
            match.Teams[solo ? 0 : i % 2].Players.Add(steam64);
            match.Players.Add(new PSPlayer { Steam64 = steam64, Kit = RemoteAPI.KitFor(steam64) });
        }

        HeldMatchId = 0;
        UpdateStatus(EPSStatus.IN_PROGRESS);
        MatchReady = Time.realtimeSinceStartup;
        Take(match, mode);

        foreach (SteamPlayer player in present) AssignPlayerGroup(player.player);

        Logger.Log($"Local {mode.Name} match started{(solo ? " in solo" : "")} with {present.Count} player(s).");
        Begin();
        return null;
    }

    /// End whatever is running without a result: a test that has told you what you
    /// needed, or the plugin unloading.
    internal static void StopMatch() {
        if (Data == null) return;

        bool local = Match.IsLocal;
        Announce(PeekSeek.Instance.Translate("match_stopped"));
        ResetMatchState();
        if (!local) RelayAllClients();
        UpdateStatus(EPSStatus.READY);
    }

    /// Somebody arriving in a local match joins the side with fewer people, with
    /// whatever kit the site has for them. Without a seat they would be on nobody's
    /// roster: counted by nobody, and nowhere on the bar.
    private static void Seat(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        int team = Match.Solo ? 0 : Data.Teams.Select((t, i) => (t.Players.Count, i)).Min().i;

        Data.Teams[team].Players.Add(steam64);
        Data.Players.Add(new PSPlayer { Steam64 = steam64, Kit = RemoteAPI.KitFor(steam64) });
        Match.Teams[team].Players.Add(steam64);
        Match.Kits[steam64] = Data.KitFor(steam64).Select(Item).ToList();
        Logger.Log($"{player.channel.owner.playerID.characterName} joined {Data.Teams[team].Name} mid-match.");
    }

    // ---- players --------------------------------------------------------------

    private static void OnClientConnected(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        RemoteAPI.FetchPlayer(steamID.m_SteamID);

        if (!BelongsHere(steamID)) {
            // Admins are never funnelled out of a server with nothing on it: they are
            // the ones who start a local match, and there is nobody here to bother.
            if (player.channel.owner.isAdmin && Data == null) return;

            RelayToLobby(player);
            return;
        }

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

        if (!Data.HasPlayer(steamID.m_SteamID)) Seat(player);
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
    }

    /// Whether this connection is one of the people this server's current match is for.
    /// Everyone else is a stranger who typed the connect code, or a player whose match
    /// has since ended, and the funnel sends them to the lobby instead. A local match
    /// is for whoever turns up.
    private static bool BelongsHere(CSteamID steamID) {
        return Data != null
            && Status == EPSStatus.IN_PROGRESS
            && (Match.IsLocal || Data.HasPlayer(steamID.m_SteamID) || IsSpectator(steamID.m_SteamID));
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

        // Someone on this server's match who has arrived before it is ready is the one
        // case that stays a refusal. The lobby relays them here the moment the site
        // calls the match live, and this server only agrees a beat later, so funnelling
        // them back would bounce them between the two servers for that beat.
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

        AnnounceAceIfAny(winner);
        WhisperCloseCalls();
        WhisperRoundDamage();

        Announce(announcement ?? PeekSeek.Instance.Translate("round_won", winner.Name, RoundsPlayed, ScoreLine()));
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
        Announce(PeekSeek.Instance.Translate("match_won", winner.Name, ScoreLine()));
        AnnounceMvp();
        RecordEvent("matchWon", winner.Name);
        SendUpdateState();
        RoundLog.Clear();
        RefreshHud();
        Task.Run(() => DelayEnd(PeekSeek.Config.DelayKickAfterWin, winner.Name));
    }

    /// The score, in team order, as "2-1" — or with names when there are more than two
    /// teams and a bare pair of numbers would not say whose is whose.
    internal static string ScoreLine() {
        if (Data?.Teams == null || Data.Teams.Count == 0) return string.Empty;

        IEnumerable<string> parts = Data.Teams.Select(team => Score(team).ToString());
        if (Data.Teams.Count > 2) {
            parts = Data.Teams.Select(team => $"{team.Name} {Score(team)}");
        }
        return string.Join(Data.Teams.Count > 2 ? " · " : "-", parts);
    }

    /// A solo ace only means something in a team format — a 1v1 win is just a win — and
    /// only generalises cleanly to two teams, so FFA/multi-team matches skip it rather
    /// than guessing at what "the opposing team" means there.
    private static void AnnounceAceIfAny(PSTeam winningTeam) {
        if (Data.Teams.Count != 2) return;

        PSTeam losingTeam = Data.Teams.First(t => t.Name != winningTeam.Name);
        if (losingTeam.Players.Count < 2) return;

        foreach (ulong user in winningTeam.Players) {
            if (RoundLog.KillsBy(new CSteamID(user)) < losingTeam.Players.Count) continue;

            string name = UnturnedPlayer.FromCSteamID(new CSteamID(user))?.CharacterName ?? user.ToString();
            Announce(PeekSeek.Instance.Translate("ace", name));
            return;
        }
    }

    private const byte CloseCallHealthThreshold = 15;

    /// Private, not public — celebrates surviving the round to whoever actually did it,
    /// without turning "I nearly died" into public information for everyone else.
    private static void WhisperCloseCalls() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Players) {
                Player p = PlayerTool.getPlayer(new CSteamID(user));
                if (p == null || p.life.isDead || p.life.health > CloseCallHealthThreshold) continue;

                Whisper(p.channel.owner, PeekSeek.Instance.Translate("close_call", p.life.health));
            }
        }
    }

    /// One whisper per player who dealt damage this round, broken down by who they were
    /// fighting — built from RoundLog rather than FightTracker, since this covers the
    /// whole round rather than only the fight that ended in a kill.
    private static void WhisperRoundDamage() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Players) {
                List<RoundDamageEntry> entries = RoundLog.DamageBy(new CSteamID(user));
                if (entries.Count == 0) continue;

                Player p = PlayerTool.getPlayer(new CSteamID(user));
                if (p == null) continue;

                string breakdown = string.Join(" · ", entries.Select(entry => {
                    string name = UnturnedPlayer.FromCSteamID(new CSteamID(entry.Victim))?.CharacterName
                        ?? entry.Victim.ToString();
                    return $"{name}: {Mathf.Round(entry.Damage)} dmg ({entry.Hits} hits)";
                }));

                Whisper(p.channel.owner, PeekSeek.Instance.Translate("round_damage", breakdown));
            }
        }
    }

    /// Most kills, headshots as the tiebreak. Says nothing for a round that ended without
    /// a single kill rather than naming an arbitrary "MVP" who did nothing.
    private static void AnnounceMvp() {
        if (StatService.InstanceStats.Count == 0) return;

        PSPlayerStat mvp = StatService.InstanceStats
            .OrderByDescending(s => s.Kills)
            .ThenByDescending(s => s.Headshots)
            .First();
        if (mvp.Kills == 0) return;

        string name = UnturnedPlayer.FromCSteamID(new CSteamID(mvp.SteamID))?.CharacterName ?? mvp.SteamID.ToString();
        int hsPercent = mvp.Headshots * 100 / mvp.Kills;
        Announce(PeekSeek.Instance.Translate("mvp", name, mvp.Kills, hsPercent));
    }

    /// Awards the whole match to the only team that showed up, or the only one left.
    internal static void NotifyTeamForcedWin(PSTeam team) {
        if (Status != EPSStatus.IN_PROGRESS) return;

        for (byte round = 0; round < Data.RoundsToWin; round++) {
            foreach (ulong user in team.Players) {
                StatService.UpdateStatWins(new(user));
            }
        }

        Announce(PeekSeek.Instance.Translate("match_forfeit", team.Name));
        EndMatch(team.Name);
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
        if (!Match.IsLocal) SendResults(winningTeam);
        ClearMatch();
    }

    private static void ClearMatch() {
        bool local = Match.IsLocal;
        Announce(PeekSeek.Instance.Translate("match_over"));
        ResetMatchState();
        UpdateStatus(EPSStatus.READY);

        // A local match ends where it began, with everybody still here to run another.
        if (local) return;

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

        StatService.InstanceStats.Clear();
        LatestEvent = null;
        EventsRecorded = 0;
        KillEventsRecorded = 0;
        RecentKills.Clear();
        FightTracker.Clear();
        RoundLog.Clear();
        LastKilledBy.Clear();
        LastPingNotice.Clear();
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

    /// Who last killed each player this match, for the revenge whisper. Steam64 to steam64,
    /// cleared with the rest of the match's state.
    private static readonly Dictionary<ulong, ulong> LastKilledBy = [];

    /// Ordinary kills say nothing publicly — the kill feed carries them — and the
    /// round/match win announcements still mark the moments that actually matter. Two
    /// things replace the old public line: a revenge whisper to the killer, and a
    /// flavoured private line to anyone who died without a killer at all.
    ///
    /// A death to the arena, which is also what a gamemode's own ground damage is, is
    /// not a death for the stats or the site's kill log, as it never was; it still takes
    /// the player out of the round and is still the gamemode's to answer.
    private static void OnPlayerDead(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        if (Data == null) return;

        StatService.MarkDeadThisRound(player.CSteamID);
        if (cause != EDeathCause.ARENA) RecordDeath(player, cause, limb, murderer);

        Running?.OnPlayerDied(player.Player, murderer);
        RefreshHud();
        SendUpdateState();
    }

    private static void RecordDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        StatService.UpdateStatDeaths(player.CSteamID);
        Logger.Log($"[alive-check] {player.CharacterName} ({player.CSteamID}) died (cause={cause}, limb={limb}) — marked dead this round.");

        UnturnedPlayer unturnedInstigator = null;
        if (murderer != CSteamID.Nil && murderer != SteamGameServer.GetSteamID()) {
            unturnedInstigator = UnturnedPlayer.FromCSteamID(murderer);
            StatService.UpdateStatKills(murderer);
            if (limb == ELimb.SKULL) StatService.UpdateStatHeadshots(murderer);
            RoundLog.RecordKill(murderer);

            if (LastKilledBy.TryGetValue(murderer.m_SteamID, out ulong murderersLastKiller)
                    && murderersLastKiller == player.CSteamID.m_SteamID) {
                Whisper(unturnedInstigator.Player.channel.owner,
                    PeekSeek.Instance.Translate("revenge", player.CharacterName));
            }

            LastKilledBy[player.CSteamID.m_SteamID] = murderer.m_SteamID;
        } else {
            string key = DeathCauseTranslationKey(cause);
            if (key != null) Whisper(player.Player.channel.owner, PeekSeek.Instance.Translate(key));
        }

        RecordKill(player, unturnedInstigator, limb);
        if (!Match.IsLocal) SendKillEvent(player, unturnedInstigator, cause, limb, murderer);
    }

    /// Only the causes realistic in an arena match get a line — the rest (zombies,
    /// animals, infection, ...) can't happen here, so they fall back to the generic one
    /// rather than growing this list for causes that will never fire.
    internal static string DeathCauseTranslationKey(EDeathCause cause) {
        return cause switch {
            EDeathCause.BONES => "death_fall",
            EDeathCause.WATER => "death_drown",
            EDeathCause.BURNING => "death_burn",
            EDeathCause.FREEZING => "death_freeze",
            EDeathCause.BLEEDING => "death_bleed",
            EDeathCause.SUICIDE => "death_suicide",
            EDeathCause.GRENADE or EDeathCause.LANDMINE or EDeathCause.MISSILE or EDeathCause.CHARGE => "death_explosive",
            _ => "death_generic"
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
        if (Match.IsLocal) return;

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
            Spectators = [.. Data.Spectators ?? []]
        };

        for (int team = 0; team < 2 && team < Data.Teams.Count; team++) {
            state.Teams[team].Name = Data.Teams[team].Name;
            state.Teams[team].Score = Score(team);
            state.Teams[team].Role = Running?.RoleOf(team) ?? HudRole.None;
            state.Teams[team].Seats = Data.Teams[team].Players.Select(Seat).ToList();
        }

        return state;
    }

    private static HudSeat Seat(ulong steam64) {
        Player player = PlayerTool.getPlayer(new CSteamID(steam64));

        return new HudSeat {
            Steam64 = steam64,
            AvatarUrl = RemoteAPI.AvatarFor(steam64),
            PickItemId = Running?.PickOf(steam64) ?? 0,
            Alive = player != null && !player.life.isDead && StatService.IsAliveThisRound(new CSteamID(steam64)),
            Health = player?.life.health ?? 0
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
