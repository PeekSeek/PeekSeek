using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PeekSeek.Hud;
using PeekSeekLobby.Core.Models;
using PeekSeekLobby.Core.Web;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace PeekSeekLobby.Core;

internal abstract class LobbyManager {
    internal class PlayerState {
        /// Whether they want a match. Nobody does until they say so: the lobby asks,
        /// on a board, once they have been here a while, and /q says it any time.
        internal bool Queueing;
        internal byte Kills;
        internal byte Deaths;
        internal byte Headshots;

        /// What /report sends about somebody. Accuracy and headshot share are the two
        /// numbers that separate a cheat from a good player, and neither means anything
        /// as a raw count, so shots taken is tracked alongside shots landed rather than
        /// hits alone. Reset with the arena, so a report describes the round it was
        /// written in rather than somebody's whole session.
        internal int DamageDealt;
        internal int ShotsFired;
        internal int ShotsHit;
        internal int HeadshotHits;

        internal float SpawnProtectedUntil;

        /// What they have done to each player this life, for the card when it ends:
        /// damage and hits, by the other player.
        internal readonly Dictionary<ulong, (int Damage, int Hits)> DamageTo = [];

        /// When Tick should deploy this player from the standard spawn into the
        /// arena. Zero once they are fighting, which is also what everyone who
        /// joined without dying starts on.
        internal float DeployAt;

        /// Which kits.json category (from AllowedKitCategories) this player is
        /// currently equipped with — set on join, changed with /kit.
        internal string KitCategory;

        /// The magazine EquipRealKit last handed out for the current kit, kept
        /// topped up by Tick's magazine check. Zero means the equipped gun has no
        /// mag profile in mmconfig.json, so there's nothing to keep full.
        internal ushort SpareMagItemId;
        internal byte SpareMagFullStock;

        /// The match the site says this player is on, for as long as it says so. Zero
        /// when they are free to be offered another.
        ///
        /// Cleared by the site's answer rather than by relaying them, because relaying
        /// is not the same as arriving. Two in five matches end with nobody having
        /// turned up, and a player still standing here when that happens used to keep
        /// this set forever: never queued again, never counted in the arena, never put
        /// back into the fight.
        internal int MatchId;

        /// The match server this player has already been sent to. The site keeps
        /// answering with the same server code for as long as the match is live, so
        /// without this every refresh relays them again and says so in chat.
        internal string RelayedTo;

        /// The player's Elo, re-asserted as their reputation on a timer because
        /// Unturned moves reputation itself on kills, and the badge the rank it earns
        /// wears, for the board.
        internal int Elo;
        internal string RankIcon;

        /// Whether the site has actually answered with an Elo yet. Kept separate
        /// from the value because zero is a legitimate Elo, and because a player
        /// whose lookup has not landed has to be asked again rather than silently
        /// left without a rank for the rest of their session.
        internal bool EloKnown;

        /// Kills in a row and the cooldowns on what they have already earned.
        internal readonly KillStreak Streak = new();

        /// The magazine a streak fitted into this player's gun, remembered so it
        /// can be taken back the moment it leaves the gun. Zero when they have none.
        internal ushort StreakMagItemId;

        /// When Tick should pull this player out of the arena and into their match.
        /// Zero when they have no match waiting, or have already been pulled.
        ///
        /// Being yanked out of a firefight mid-burst reads as a crash rather than a
        /// match, so the pull is announced and counted down first: they get to finish
        /// what they were doing and they know why the floor is about to move.
        internal float PullAt;

        /// The last whole second already counted out loud, so each number is said
        /// once however many times Tick runs inside that second.
        internal int LastCountdownSecond;

        /// Whether this player has stepped away. They keep their queue heartbeat and
        /// their stats, they are simply never put back into the arena until they say
        /// so, which is the whole difference between /afk and /noqueue.
        internal bool Afk;

        /// The moment this player's queue heartbeat is allowed to start. Everyone is in
        /// the queue by default, but not from the instant they connect: a match found in
        /// their first second pulls them straight back out of a lobby they have not
        /// finished loading into, and the first thing they see of PeekSeek is being
        /// moved twice. Zero once they are queueing, and zero immediately for anyone who
        /// asked for the queue themselves with /noqueue.
        internal float QueueFrom;

        /// When this player started wanting a match, for the clock on the block.
        internal float QueueingSince;

        /// When the lobby asks whether they want a match, and whether it has. Asked
        /// once, after they have had time to get a feel for the place; /q is the
        /// answer any time after.
        internal float PromptAt;
        internal bool Prompted;

        /// Whether the site should be asked to queue this player on this tick.
        ///
        /// False for someone who opted out, someone already holding a match, and someone
        /// still inside their arrival grace. All three are the same question to the
        /// caller, so they are answered in one place rather than three conditions
        /// repeated at the two call sites that ask.
        internal bool WantsAMatch(float now) {
            return Queueing && MatchId == 0 && now >= QueueFrom;
        }

        /// Whether the match this player is standing here holding has gone away.
        ///
        /// `siteMatchId` is what the site answered for them this refresh, and it is the
        /// only thing that knows: a match ends by being scored, abandoned or cancelled,
        /// and none of those are events this lobby is told about. So the answer simply
        /// stops mentioning a match, and that is the signal.
        internal bool LostTheirMatch(int siteMatchId) => MatchId != 0 && siteMatchId == 0;

        /// Whether this player should be handed over to `serverCode` now.
        ///
        /// Once per server rather than once per refresh. The site keeps answering with
        /// the same code for as long as the match is live, and a relay the client
        /// refuses leaves the player right here — so without the last code remembered
        /// they are invited again every few seconds, and told about it in chat each
        /// time. A countdown still running wins: relaying mid-count would take away the
        /// warning the count exists to give.
        internal bool ShouldRelayTo(string serverCode) {
            return MatchId != 0 && PullAt == 0 && RelayedTo != serverCode;
        }

        /// Whether this player cannot be hurt right now. One timer covers a life's
        /// whole grace — the wait at the standard spawn and the seconds after being
        /// deployed into the arena — so there is one thing to set, one to clear, and
        /// no state in which two of them disagree.
        ///
        /// Being AFK holds it open for as long as it lasts. Somebody parked at spawn
        /// cannot shoot back and cannot walk away, so leaving them killable would make
        /// /afk a way to farm them rather than a way to step out.
        internal bool IsProtectedAt(float now) => Afk || SpawnProtectedUntil > now;

        /// Books this life's deploy and the protection wrapped around it, and says
        /// whether it was this call that did it.
        ///
        /// False when a deploy is already pending, which is the guard that matters:
        /// OnLifeUpdated fires for as long as a player is dead and the still-dead
        /// sweep calls in behind it, so without it every pass pushed the deploy
        /// another respawnDelay into the future — a wait that reset faster than it
        /// ran down — and whispered the countdown again while it did.
        internal bool ScheduleDeploy(float now, float respawnDelay, float protection) {
            if (DeployAt > 0) return false;

            DeployAt = now + respawnDelay;
            SpawnProtectedUntil = now + respawnDelay + protection;
            return true;
        }
    }

    private static readonly Dictionary<ulong, PlayerState> Players = [];
    private static float LastRefreshSent;
    private static float LastMagazineCheck;
    private static float LastReputationSync;

    /// How long after connecting a player starts being offered matches. Not configurable
    /// and not announced: it is not a rule anybody needs to know, it is the beat between
    /// landing in the lobby and the matchmaker being allowed to move you again.
    private const float QueueGraceSeconds = 10f;

    private const float MagazineCheckIntervalSeconds = 2f;
    private const float ReputationSyncIntervalSeconds = 5f;
    private const float ProtectedNoticeIntervalSeconds = 3f;
    private const float StaminaTopUpIntervalSeconds = 0.5f;
    private const float HudIntervalSeconds = 1f;

    private static float LastStaminaTopUp;
    private static float LastHudUpdate;
    private const int LeaderboardPlaces = 5;

    /// Stamina is a byte out of 100. Refilling from just under full would send a
    /// packet for a sliver nobody can see move, so it waits until some is genuinely
    /// gone — still far faster than a sprint can empty it.
    private const byte StaminaTopUpThreshold = 90;

    /// The player count the arena HUD was last told about. -1 so the first tick
    /// always sends, and so a connect can force a resend by putting it back.
    private static int LastLevelNumberSent = -1;

    private static readonly Dictionary<ulong, float> LastProtectedNotice = [];

    private const string HarmonyId = "peekseek.lobby";
    private static Harmony Instrument;

    /// The screens, the match servers' own, drawn here too: the kill feed, the bar
    /// along the top with everybody's face on it, the banner, the death card with the
    /// respawn count in it, the scoreboard, and the one question this lobby asks.
    private static KillFeed Feed;
    private static LobbyBar Bar;
    private static Banner Banner;
    private static DeathCard Card;
    private static Scoreboard Scores;
    private static readonly QueueBoard Queue = new();

    /// When the board everybody is looking at comes down again, after a reset. Zero
    /// while it is nobody's but whoever is holding the key.
    private static float ScoresLockedUntil;

    /// When the arena started counting towards its reset, or zero while it is not:
    /// an arena with one player in it has nothing to reset and no reason to count.
    private static float ArenaCountingSince;

    internal static void Init() {
        Provider.onServerConnected += OnPlayerConnected;
        Provider.onServerDisconnected += OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDead;
        PlayerLife.onPlayerLifeUpdated += OnLifeUpdated;

        // Attribute-based Harmony patches (ArenaNeutralizePatch) are inert until
        // something actually applies them — this is that something. Without it
        // the native Arena round system, shrinking zone included, keeps running
        // completely unpatched.
        Instrument = new Harmony(HarmonyId);
        Instrument.PatchAll(Assembly.GetExecutingAssembly());

        ItemManager.onServerSpawningItemDrop += OnItemDropRequested;
        DamageTool.damagePlayerRequested += OnDamagePlayerRequested;
        UseableGun.onBulletSpawned += OnBulletSpawned;
        UseableGun.onBulletHit += OnBulletHit;

        TargetRange.Init();
        RemoteAPI.Init();

        Feed = new KillFeed(new KillFeedSettings {
            EffectGUID = PeekSeekLobby.Config.KillFeedEffectGUID,
            EffectKey = PeekSeekLobby.Config.KillFeedKey,
            ItemIconURL = PeekSeekLobby.Config.ItemIconURL,
            Rows = PeekSeekLobby.Config.KillFeedRows,
            RowSeconds = PeekSeekLobby.Config.KillFeedRowSeconds,
            NameLength = PeekSeekLobby.Config.KillFeedNameLength
        });
        Feed.Hook();
        Bar = new LobbyBar();
        Banner = new Banner(new BannerSettings {
            EffectGUID = PeekSeekLobby.Config.BannerEffectGUID,
            EffectKey = PeekSeekLobby.Config.BannerKey,
            Seconds = PeekSeekLobby.Config.BannerSeconds
        });
        Card = new DeathCard(new DeathCardSettings {
            EffectGUID = PeekSeekLobby.Config.DeathCardEffectGUID,
            EffectKey = PeekSeekLobby.Config.DeathCardKey,
            ItemIconURL = PeekSeekLobby.Config.ItemIconURL,
            Seconds = PeekSeekLobby.Config.DeathCardSeconds
        });
        Scores = new Scoreboard(new ScoreboardSettings {
            EffectGUID = PeekSeekLobby.Config.ScoreboardEffectGUID,
            EffectKey = PeekSeekLobby.Config.ScoreboardKey
        });
        PlayerInput.onPluginKeyTick += OnPluginKey;
        EffectManager.onEffectButtonClicked += OnButtonClicked;
    }

    /// Refusing the drop outright is cheaper and cleaner than spawning it and
    /// deleting it a frame later, and it covers every source at once — death
    /// piles, manual drops, and inventory overflow from a re-equip.
    private static void OnItemDropRequested(Item item, ref Vector3 location, ref bool shouldAllow) {
        if (PeekSeekLobby.Config.RemoveDroppedItems) shouldAllow = false;
    }

    /// Refusing the damage outright *is* the spawn protection.
    ///
    /// It used to be Rocket's Features.GodMode, which heals the player to full on
    /// every FixedUpdate — fifty replicated health, food, water and virus updates a
    /// second, for every protected player, in a lobby where a ten second respawn
    /// wait and five seconds of cover mean somebody nearly always is one. It also
    /// had to be switched back off by a call that returned false whenever the
    /// player object could not be resolved that frame, which is how people ended up
    /// invincible for an entire session. A bool on a damage request costs nothing,
    /// cannot half-fail, and ends the instant the timer says so.
    ///
    /// Environmental damage is refused too: teleporting someone to a spawn point
    /// with teleportToLocationUnsafe can land them hard enough to hurt, and dying
    /// to the deploy that was meant to save you is not a lobby anyone enjoys.
    /// Every shot leaving a gun, so accuracy has a denominator.
    ///
    /// Counted here rather than from hits alone because "412 hits" says nothing about
    /// whether somebody is cheating; 412 hits out of 430 shots does.
    private static void OnBulletSpawned(UseableGun gun, BulletInfo bullet) {
        if (gun?.player == null) return;
        if (Players.TryGetValue(gun.player.channel.owner.playerID.steamID.m_SteamID, out PlayerState state))
            state.ShotsFired++;
    }

    /// Shots that connected with a player, and how many of those hit the head.
    ///
    /// Only hits on people are counted. A bullet that lands in a wall is not a miss
    /// worth reporting on and a bullet that lands in a warm-up target is not a hit,
    /// so both are left out of the ratio entirely.
    private static void OnBulletHit(UseableGun gun, BulletInfo bullet, InputInfo hit, ref bool shouldAllow) {
        if (gun?.player == null || hit == null || hit.type != ERaycastInfoType.PLAYER) return;
        if (!Players.TryGetValue(gun.player.channel.owner.playerID.steamID.m_SteamID, out PlayerState state)) return;

        state.ShotsHit++;
        if (hit.limb == ELimb.SKULL) state.HeadshotHits++;
    }

    private static void OnDamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        CSteamID victimID = parameters.player.channel.owner.playerID.steamID;
        CSteamID attacker = parameters.killer;
        float now = Time.realtimeSinceStartup;

        // Throwing a punch is giving up your own protection. Shooting out from
        // behind it while nothing can touch you is the one thing it must never buy.
        if (attacker != CSteamID.Nil && attacker != victimID
                && Players.TryGetValue(attacker.m_SteamID, out PlayerState attackerState)
                && attackerState.IsProtectedAt(now)) {
            EndSpawnProtection(attacker, attackerState);
        }

        if (!Players.TryGetValue(victimID.m_SteamID, out PlayerState victimState)) return;
        if (!victimState.IsProtectedAt(now)) {
            // Counted only once the hit is going to land, and only against another
            // player: damage a spawn-protected target absorbs never happened, and
            // reporting it would credit a shooter for damage nobody took.
            if (attacker != CSteamID.Nil && attacker != victimID
                    && Players.TryGetValue(attacker.m_SteamID, out PlayerState dealer)) {
                int dealt = Mathf.RoundToInt(parameters.damage * parameters.times);
                dealer.DamageDealt += dealt;
                dealer.DamageTo.TryGetValue(victimID.m_SteamID, out (int Damage, int Hits) sofar);
                dealer.DamageTo[victimID.m_SteamID] = (sofar.Damage + dealt, sofar.Hits + 1);
            }
            return;
        }

        shouldAllow = false;

        // Without a word for it the shooter just sees their hits do nothing and
        // assumes the server is broken. Throttled per shooter — this runs once per
        // bullet, and an automatic weapon would otherwise fill their chat.
        if (attacker == CSteamID.Nil || attacker == victimID) return;

        if (LastProtectedNotice.TryGetValue(attacker.m_SteamID, out float last)
                && now - last < ProtectedNoticeIntervalSeconds) return;
        LastProtectedNotice[attacker.m_SteamID] = now;

        Player attackerPlayer = PlayerTool.getPlayer(attacker);
        if (attackerPlayer == null) return;

        Whisper(attackerPlayer.channel.owner, PeekSeekLobby.Instance.Translate(
            "target_protected", parameters.player.channel.owner.playerID.characterName));
    }

    internal static void Shutdown() {
        Provider.onServerConnected -= OnPlayerConnected;
        Provider.onServerDisconnected -= OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDead;
        PlayerLife.onPlayerLifeUpdated -= OnLifeUpdated;

        ItemManager.onServerSpawningItemDrop -= OnItemDropRequested;
        DamageTool.damagePlayerRequested -= OnDamagePlayerRequested;
        UseableGun.onBulletSpawned -= OnBulletSpawned;
        UseableGun.onBulletHit -= OnBulletHit;
        Instrument.UnpatchAll(HarmonyId);

        TargetRange.Shutdown();
        RemoteAPI.Shutdown();
        Feed.Unhook();
        Feed.Clear();
        Bar.Hide();
        Banner.Clear();
        Card.Clear();
        Scores.HideAll();
        PlayerInput.onPluginKeyTick -= OnPluginKey;
        EffectManager.onEffectButtonClicked -= OnButtonClicked;
    }

    private static void OnPlayerConnected(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        PlayerState state = new() {
            KitCategory = DefaultKitCategory(),
            QueueFrom = Time.realtimeSinceStartup + QueueGraceSeconds,
            PromptAt = Time.realtimeSinceStartup + PeekSeekLobby.Config.QueuePromptSeconds,
        };
        Players[steamID.m_SteamID] = state;
        Bar.Redraw();
        RemoteAPI.FetchAvatar(steamID.m_SteamID, url => Bar.Picture(steamID.m_SteamID, url));

        // Someone arriving as someone else leaves keeps the count identical, and the
        // new player would never be sent a number they have not seen before.
        LastLevelNumberSent = -1;

        // There is a real fight to be had now, so nobody's warm-up targets are left
        // standing in the middle of it as cover.
        if (Provider.clients.Count > 1) TargetRange.StopAll("range_ended_players");

        ForceSolo(player);
        SpawnPlayer(steamID, state);
        // Asks about them without queueing them. The queue starts on the tick their
        // grace runs out, which is Tick's job rather than this one's.
        RemoteAPI.Refresh(steamID.m_SteamID);

        SendWelcome(player);
    }

    /// One line: what this server is and the commands, and the range when it applies.
    /// Somebody who walked into a full arena does not need to be told about shooting
    /// mannequins, and somebody who walked into an empty one needs it more than
    /// anything else.
    private static void SendWelcome(Player player) {
        PeekSeekLobbyConfiguration config = PeekSeekLobby.Config;

        string kits = string.Join("|", config.AllowedKitCategories ?? []);
        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("welcome", kits));

        if (config.RangeEnabled && Provider.clients.Count <= 1) {
            Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("range_hint"));
        }
    }

    private static void OnPlayerDisconnected(CSteamID steamID) {
        Bar.Left(steamID.m_SteamID);
        Card.Forget(steamID.m_SteamID);
        Scores.Forget(steamID.m_SteamID);
        Queue.Forget(steamID.m_SteamID);
        TargetRange.Forget(steamID.m_SteamID);
        OfferRangeToWhoeverIsLeft();

        if (!PeekSeekLobby.Config.EnableQueue) {
            Players.Remove(steamID.m_SteamID);
            return;
        }
        if (Players.Remove(steamID.m_SteamID)) {
            RemoteAPI.LeaveQueue(steamID.m_SteamID);
        }
    }

    /// The player whose opponent just left is in exactly the position the range is
    /// for, and is the one most likely to leave next. Told once, when it becomes
    /// true, rather than repeated at them while they stand there alone.
    private static void OfferRangeToWhoeverIsLeft() {
        if (!PeekSeekLobby.Config.RangeEnabled) return;
        if (Provider.clients.Count != 1) return;

        SteamPlayer alone = Provider.clients[0];
        if (alone != null) Whisper(alone, PeekSeekLobby.Instance.Translate("range_hint"));
    }

    /// The feed carries the kill for everybody. What the one who died gets is the
    /// card: who, with what, from how far, what they had left and what they did to
    /// them, while it still matters.
    private static void OnPlayerDead(UnturnedPlayer victim, EDeathCause cause, ELimb limb, CSteamID murderer) {
        if (cause == EDeathCause.ARENA) return;
        if (!Players.TryGetValue(victim.CSteamID.m_SteamID, out PlayerState victimState)) return;

        victimState.Deaths++;
        // Dying ends a streak outright, however recent the last kill was.
        victimState.Streak.End();
        Card.Show(victim.Player.channel.owner, DeathOf(victim, victimState, cause, limb, murderer));
        victimState.DamageTo.Clear();

        // Putting them back on their feet is OnLifeUpdated's job, not this
        // event's: a ServerRespawn from in here is ignored, because Unturned has
        // not finished applying the death at the point this fires.

        bool isPlayerKill = murderer != CSteamID.Nil
            && murderer != SteamGameServer.GetSteamID()
            && murderer != victim.CSteamID;
        if (!isPlayerKill) return;

        UnturnedPlayer killer = UnturnedPlayer.FromCSteamID(murderer);
        if (killer == null) return;

        if (Players.TryGetValue(murderer.m_SteamID, out PlayerState killerState)) {
            killerState.Kills++;
            if (limb == ELimb.SKULL) killerState.Headshots++;
        }

        GrantRewards(killer.Player, spawning: false);

        StreakReward earned = killerState == null ? null : AdvanceStreak(killerState);
        if (earned != null) PayOutStreak(killer, killerState, earned);
        Bar.Redraw();
    }

    /// The card for one death. With a killer: who, what they held, whether it was
    /// the head, how far, what they had left and what the victim had done to them
    /// this life. Without one: the cause, in the few words the game can name.
    private static DeathInfo DeathOf(UnturnedPlayer victim, PlayerState victimState, EDeathCause cause, ELimb limb, CSteamID murderer) {
        UnturnedPlayer killer = murderer == CSteamID.Nil || murderer == victim.CSteamID ? null : UnturnedPlayer.FromCSteamID(murderer);
        if (killer?.Player == null) return new DeathInfo { Cause = CauseLabel(cause) };

        string line = $"They had {killer.Player.life.health} HP left";
        if (victimState.DamageTo.TryGetValue(murderer.m_SteamID, out (int Damage, int Hits) given)) {
            line += $"    You dealt {given.Damage} in {given.Hits} hit{(given.Hits == 1 ? "" : "s")}";
        }

        return new DeathInfo {
            Killer = killer.CharacterName,
            Weapon = KillFeed.Weapon(cause, killer.Player),
            Headshot = limb == ELimb.SKULL,
            Distance = Mathf.RoundToInt(Vector3.Distance(killer.Player.transform.position, victim.Player.transform.position)),
            Line = line
        };
    }

    private static string CauseLabel(EDeathCause cause) {
        return cause switch {
            EDeathCause.BONES => "FELL",
            EDeathCause.WATER => "DROWNED",
            EDeathCause.BLEEDING => "BLED OUT",
            EDeathCause.SUICIDE => "SUICIDE",
            EDeathCause.GRENADE or EDeathCause.LANDMINE or EDeathCause.MISSILE or EDeathCause.CHARGE => "BLOWN UP",
            _ => "DIED"
        };
    }

    /// A streak is kills in a row with no death and no long gap between them. At
    /// most one reward lands per kill — the one whose exact kill count this kill
    /// just reached — so climbing to five collects the two, three and five rewards
    /// on the way up rather than all of them at once.
    private static StreakReward AdvanceStreak(PlayerState state) {
        return state.Streak.Advance(
            Time.realtimeSinceStartup,
            PeekSeekLobby.Config.StreakWindowSeconds,
            PeekSeekLobby.Config.StreakRewards);
    }

    private static void PayOutStreak(UnturnedPlayer killer, PlayerState state, StreakReward reward) {
        GiveStreakItems(killer.Player, state, reward);

        if (string.IsNullOrEmpty(reward.szChatMessage)) return;
        Announce(string.Format(reward.szChatMessage, killer.CharacterName, reward.KillsRequired));
    }

    /// Magazines are fitted into the gun the player is already holding rather than
    /// dropped in their pack — a drum is only a reward if it's the one they are
    /// shooting — and are skipped when that gun can't chamber them. Everything else
    /// goes into the inventory the ordinary way.
    private static void GiveStreakItems(Player player, PlayerState state, StreakReward reward) {
        if (reward.Items is not { Count: > 0 }) return;

        foreach (StreakItem item in reward.Items) {
            if (Assets.find(EAssetType.ITEM, item.ItemID) is ItemMagazineAsset magazine) {
                AttachMagazine(player, state, item.ItemID, magazine);
                continue;
            }

            for (byte i = 0; i < item.Amount; i++) {
                player.inventory.forceAddItem(new Item(item.ItemID, true), true);
            }
        }
    }

    /// Swaps the magazine fitted to the equipped gun for this one, full. Bytes 8
    /// and 9 of a gun's state are the fitted magazine's id and byte 10 its round
    /// count — the same layout the site writes its kit states in. Does nothing
    /// unless the player is holding a gun that shares a caliber with it, which is
    /// what makes one "drum" reward give the ranger drum to ranger guns, the
    /// military drum to military guns, and a sniper nothing at all.
    private static void AttachMagazine(Player player, PlayerState state, ushort itemId, ItemMagazineAsset magazine) {
        ItemGunAsset gun = player.equipment.asset as ItemGunAsset;
        if (gun == null) return;

        byte[] gunState = player.equipment.state;
        if (gunState == null || gunState.Length <= 10) return;
        if (!gun.magazineCalibers.Intersect(magazine.calibers).Any()) return;

        gunState[8] = (byte)itemId;
        gunState[9] = (byte)(itemId >> 8);
        gunState[10] = magazine.amount;
        player.equipment.sendUpdateState();

        state.StreakMagItemId = itemId;
    }

    /// The magazine currently fitted to the gun the player is holding, or zero when
    /// they aren't holding a gun.
    private static ushort FittedMagazine(Player player) {
        if (player.equipment.asset is not ItemGunAsset) return 0;

        byte[] gunState = player.equipment.state;
        if (gunState == null || gunState.Length <= 10) return 0;
        return (ushort)(gunState[8] | (gunState[9] << 8));
    }

    /// A streak magazine only ever exists in the gun. The moment it leaves — the
    /// player reloaded it away, dropped it or switched weapons — it is taken back
    /// wherever it ended up, so a spent drum can't be reloaded a second time or
    /// carried out of the fight it was earned in. Nothing else a streak hands out
    /// is touched: grenades and bounces consume themselves when thrown, and the
    /// umbrella is meant to be kept.
    private static void TakeBackSpentStreakMagazine(CSteamID steamID, PlayerState state) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        // Only a gun in their hands can say the magazine has left it. Zero means
        // they are holding something else — the umbrella the last streak gave
        // them, say — and the magazine is still in the gun in their pack rather
        // than spent, so there is nothing to take yet.
        ushort fitted = FittedMagazine(player);
        if (fitted == 0 || fitted == state.StreakMagItemId) return;

        RemoveItem(player, state.StreakMagItemId);
        state.StreakMagItemId = 0;
    }

    /// Every copy of an item, wherever it sits. Walks each page backwards because
    /// removing an item shifts everything after it down an index.
    private static void RemoveItem(Player player, ushort itemId) {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            for (int index = player.inventory.getItemCount(page) - 1; index >= 0; index--) {
                ItemJar jar = player.inventory.getItem(page, (byte)index);
                if (jar?.item != null && jar.item.id == itemId) player.inventory.removeItem(page, (byte)index);
            }
        }
    }

    /// Tops each reward back up to its cap rather than adding one blindly, so a
    /// player who never spent theirs doesn't accumulate a stack across a killstreak.
    /// A spawn only hands out what GrantOnSpawn allows; a kill restocks everything.
    private static void GrantRewards(Player player, bool spawning) {
        List<RewardItem> rewards = PeekSeekLobby.Config.KillRewards;
        if (rewards is not { Count: > 0 }) return;

        foreach (RewardItem reward in rewards) {
            if (spawning && !reward.GrantOnSpawn) continue;

            int missing = reward.MaxCarried - CountItem(player, reward.ItemID);
            for (int i = 0; i < missing; i++) {
                player.inventory.forceAddItem(new Item(reward.ItemID, true), true);
            }
        }
    }

    private static int CountItem(Player player, ushort itemId) {
        int count = 0;
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            byte itemCount = player.inventory.getItemCount(page);
            for (byte i = 0; i < itemCount; i++) {
                ItemJar jar = player.inventory.getItem(page, i);
                if (jar?.item != null && jar.item.id == itemId) count++;
            }
        }
        return count;
    }

    /// Puts a player into the arena, kitted and briefly protected.
    ///
    /// The AFK guard lives here rather than at the call sites because there are several
    /// and they do not look alike: a deploy coming due, an arena reset putting everyone
    /// back in, a player connecting. Every one of them is "spawn this player", and an
    /// AFK player must come out of all of them the same way, which is the bug that
    /// shipped in 1.5.0: the deploy path checked and the arena reset did not, so anyone
    /// AFK was dropped back into the fight the next time the arena cleared.
    private static void SpawnPlayer(CSteamID steamID, PlayerState state) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        if (state.Afk) {
            TeleportToSafeSpawn(player);
            return;
        }

        TeleportToArena(player);
        Card.HideRespawn(player.channel.owner);

        state.SpawnProtectedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.SpawnProtectionSeconds;

        EquipRealKit(player, steamID, state);
    }

    /// One of the configured arena spawns, which is where the fighting is. Named for
    /// what it does rather than "spawn", because the level's own spawn point is a
    /// different place with the opposite meaning and the two were being confused.
    private static void TeleportToArena(Player player) {
        List<LobbySpawn> spawns = PeekSeekLobby.Config.Spawns;
        if (spawns is not { Count: > 0 }) return;

        LobbySpawn spawn = spawns[Random.Range(0, spawns.Count)];
        player.teleportToLocationUnsafe(spawn.Position.ToVector3(), player.look.yaw);
    }

    /// The level's own spawn point: the safezone players wait at between lives, and the
    /// only place somebody who is not fighting belongs. This is where a respawn drops a
    /// dead player, so an AFK player parked here is standing exactly where the lobby
    /// already puts anyone who is out of the fight.
    private static void TeleportToSafeSpawn(Player player) {
        PlayerSpawnpoint spawn = LevelPlayers.getSpawn(false);
        if (spawn == null) return;

        player.teleportToLocationUnsafe(spawn.point, spawn.angle);
    }

    /// Respawning the moment the life updates is what keeps a death screen off the
    /// player's display, but it puts them on Unturned's own spawn point rather than
    /// back in the fight. Deploying to a lobby spawn is left to Tick, so the wait
    /// between lives is spent standing somewhere safe instead of staring at a
    /// death screen.
    private static void Respawn(CSteamID steamID, PlayerState state) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        // The one part worth doing on every pass — a respawn that did not land is
        // simply retried. Everything after it happens once a life.
        player.life.ServerRespawn(false);

        state.ScheduleDeploy(
            Time.realtimeSinceStartup,
            PeekSeekLobby.Config.RespawnDelaySeconds,
            PeekSeekLobby.Config.SpawnProtectionSeconds);
    }

    /// The one thing that actually puts people back on their feet, copied from the
    /// main plugin because that is the version proven to work. Unturned raises this
    /// once a life has really changed, which is after the death has been applied —
    /// the death event fires too early for a respawn to take. It also keeps firing
    /// while someone is dead, so a respawn that does not land is simply retried
    /// instead of leaving them stuck on a death screen.
    private static void OnLifeUpdated(Player player) {
        if (player?.channel?.owner == null || !player.life.isDead) return;

        CSteamID steamID = player.channel.owner.playerID.steamID;
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return;

        Respawn(steamID, state);
    }

    /// The sweep behind OnLifeUpdated. Nobody stays dead in a warm-up lobby, so if
    /// a life update is ever missed this still catches them within the second
    /// rather than stranding them for the rest of the round.
    private static void RespawnAnyoneStillDead() {
        foreach (SteamPlayer client in Provider.clients) {
            if (client?.player == null || !client.player.life.isDead) continue;
            if (!Players.TryGetValue(client.playerID.steamID.m_SteamID, out PlayerState state)) continue;

            Respawn(client.playerID.steamID, state);
        }
    }

    /// Clearing the timer is the whole of it, so this cannot half-succeed the way
    /// switching god mode off could. A pending deploy is deliberately left alone:
    /// someone who shot their way out of protection still has to be put into the
    /// arena, they just arrive able to be shot back at.
    private static void EndSpawnProtection(CSteamID steamID, PlayerState state) {
        if (state.SpawnProtectedUntil == 0) return;
        state.SpawnProtectedUntil = 0;

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("spawn_protection_over"));
    }

    private static string DefaultKitCategory() {
        List<string> allowed = PeekSeekLobby.Config.AllowedKitCategories;
        return allowed is { Count: > 0 } ? allowed[0] : "meta";
    }

    /// The configured stand-in for a category when the website can't be reached.
    /// Falls back to the first configured kit of any category rather than
    /// nothing, since an armed player in the wrong kit still beats an unarmed one.
    private static KitLoadout FallbackLoadout(string category) {
        List<FallbackKit> kits = PeekSeekLobby.Config.FallbackKits;
        if (kits is not { Count: > 0 }) return null;

        FallbackKit match = kits.FirstOrDefault(kit => kit.Category == category) ?? kits[0];
        return match.ToLoadout();
    }

    /// Re-fetched and re-applied on every spawn (join and respawn alike) — each
    /// life starts with the player's real, currently-saved kit, same as a ranked
    /// match would give them, rather than whatever they picked up mid-fight.
    private static void EquipRealKit(Player player, CSteamID steamID, PlayerState state) {
        string category = state.KitCategory;
        System.Threading.Tasks.Task.Run(async () => {
            KitLoadout loadout = await RemoteAPI.FetchKit(steamID.m_SteamID, category) ?? FallbackLoadout(category);
            if (loadout == null) {
                Logger.LogWarning($"No kit for {steamID} ({category}) and no fallback configured — they spawn empty-handed.");
                return;
            }

            TaskDispatcher.QueueOnMainThread(() => {
                if (player == null) return;
                ClearInventory(player);
                GiveItems(player, loadout.Items);

                // The kit that was just cleared took any streak magazine with it,
                // so there is nothing left to take back.
                state.StreakMagItemId = 0;

                // How many spares the kit asks for is ignored in favour of the
                // configured count: a lobby life is short and constantly re-kitted,
                // so a match's worth of magazines is just inventory clutter.
                byte spares = PeekSeekLobby.Config.SpareMagazines;
                state.SpareMagItemId = loadout.Magazine?.ItemID ?? 0;
                state.SpareMagFullStock = loadout.Magazine == null ? (byte)0 : spares;
                if (loadout.Magazine != null) GiveItem(player, loadout.Magazine, spares);

                // After the kit, never before: EquipRealKit clears the inventory
                // first, so rewards granted earlier would be thrown away.
                GrantRewards(player, spawning: true);
            });
        });
    }

    private static void GiveItems(Player player, List<PSItem> items) {
        foreach (PSItem item in items) GiveItem(player, item);
    }

    private static void GiveItem(Player player, PSItem item) {
        GiveItem(player, item, item.Amount);
    }

    /// The same item, but however many of it the caller wants rather than what the
    /// kit says — the lobby caps magazines at its own count.
    private static void GiveItem(Player player, PSItem item, byte amount) {
        for (byte i = 0; i < amount; i++) {
            player.inventory.forceAddItem(new Item(item.ItemID, true) { state = item.State }, true);
        }
    }

    private static void ClearInventory(Player player) {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            byte count = player.inventory.getItemCount(page);
            for (byte j = 0; j < count; j++) {
                player.inventory.removeItem(page, 0);
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
    }

    internal static bool QueueEnabled => PeekSeekLobby.Config.EnableQueue;

    internal static bool SetQueueing(CSteamID steamID, bool queueing) {
        if (!QueueEnabled) return false;
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;

        state.Queueing = queueing;
        state.Prompted = true;
        if (state.Queueing) {
            // Asking for the queue by name skips the arrival grace. That grace exists to
            // stop a match landing on somebody who has not finished arriving, and
            // somebody typing the command has plainly arrived.
            state.QueueFrom = 0;
            QueueChoice choice = Queue.ChoiceOf(steamID.m_SteamID);
            RemoteAPI.RefreshQueued(steamID.m_SteamID,
                choice?.Bitmask ?? PeekSeekLobby.Config.GamemodeBitmask,
                choice?.RegionCode ?? PeekSeekLobby.Config.szRegion);
        } else {
            RemoteAPI.LeaveQueue(steamID.m_SteamID);
        }
        return state.Queueing;
    }

    /// Steps this player out of the arena, or puts them back in. Returns whether they
    /// are now AFK.
    ///
    /// Going AFK parks them at a spawn point rather than leaving them standing where
    /// they were: protection lasts as long as the AFK does, and an untouchable body in
    /// the middle of a firefight is cover nobody agreed to. Coming back books the same
    /// deploy a respawn books, so the wait to be let into the arena is the wait
    /// everybody else serves.
    ///
    /// The queue is deliberately untouched. Stepping away from the keyboard is not
    /// leaving matchmaking, and a match found while AFK still pulls them into it.
    internal static bool ToggleAfk(CSteamID steamID) {
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;

        state.Afk = !state.Afk;

        if (state.Afk) {
            // The safezone, not an arena spawn. Sending them to one of the configured
            // arena points is what 1.5.0 did, which put an untouchable player in the
            // middle of the fighting and looked from the outside like /afk doing nothing.
            state.DeployAt = 0;
            Player player = PlayerTool.getPlayer(steamID);
            if (player != null) TeleportToSafeSpawn(player);
            return true;
        }

        state.SpawnProtectedUntil = 0;
        state.ScheduleDeploy(
            Time.realtimeSinceStartup,
            PeekSeekLobby.Config.RespawnDelaySeconds,
            PeekSeekLobby.Config.SpawnProtectionSeconds);
        return false;
    }

    internal static bool TryGetStats(CSteamID steamID, out PlayerState state) {
        return Players.TryGetValue(steamID.m_SteamID, out state);
    }

    /// Rocket's UnturnedPlayer.FromName cannot be JIT-compiled by the Mono build
    /// the live server runs: calling it throws BadImageFormatException("Method has
    /// zero rva"), which Rocket catches and logs, so the player just gets silence.
    /// Only the commands that took a name argument ever reached it, which is why
    /// /stats and /ms worked bare and died the moment you named someone.
    ///
    /// Resolving here costs one pass over the client list and owes Rocket nothing.
    /// Steam IDs are matched before names so an exact ID never loses to somebody
    /// whose name happens to contain those digits.
    internal static SteamPlayer FindPlayer(string query) {
        if (string.IsNullOrEmpty(query)) return null;

        if (ulong.TryParse(query, out ulong steam64)) {
            foreach (SteamPlayer client in Provider.clients) {
                if (client?.playerID.steamID.m_SteamID == steam64) return client;
            }
        }

        foreach (SteamPlayer client in Provider.clients) {
            if (client == null) continue;
            if (NameMatches(client.playerID.characterName, query)) return client;
            if (NameMatches(client.playerID.playerName, query)) return client;
        }
        return null;
    }

    /// The same forgiving substring rule Unturned's own NameTool applies, so /stats
    /// rio still finds RioDash. Ordinal because a player name is a label rather than
    /// prose, and matching it has no business depending on the server's locale.
    ///
    /// Pure, and kept that way so it can be tested without a live Unturned host.
    internal static bool NameMatches(string name, string query) {
        return name != null && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) != -1;
    }

    /// Everything the site said about one player, acted on in one place.
    ///
    /// Reads top to bottom as the player's journey: rank first, then "you have a
    /// match", then either "your match is ready, go" or "that match is gone, back to
    /// the fight". Each step only fires once because the step before it moves the
    /// player's own state on.
    internal static void ApplyPlayerState(ulong steam64, RemoteAPI.PlayerState remote, bool eloKnown) {
        if (!Players.TryGetValue(steam64, out PlayerState state)) return;

        // Before anything else this method might do with them. A banned player has no
        // business holding a slot in the arena, and the site has already refused to
        // queue them, so leaving them stood here would only be a lobby full of people
        // who can never be matched.
        //
        // The kick lands a moment after they connect rather than at the door: the
        // answer comes from an HTTP call and Unturned's connection check is
        // synchronous, so there is nothing to wait on at the point they are let in.
        if (remote.Ban != null) {
            KickBanned(steam64, remote.Ban);
            return;
        }

        if (eloKnown) {
            state.Elo = remote.Elo;
            state.EloKnown = true;
        }
        if (remote.RankIcon != null) state.RankIcon = remote.RankIcon;

        if (remote.MatchId != 0 && state.MatchId == 0) SendToMatch(steam64, state, remote.MatchId);
        if (state.LostTheirMatch(remote.MatchId)) ReturnToArena(steam64, state);
        if (remote.RelayTo != null) RelayToMatch(steam64, state, remote.RelayTo);
    }

    /// Ask the reported player's client for a screenshot, then send it to staff with
    /// their numbers for this round attached.
    ///
    /// `CSteamID.Nil` as the destination is what makes this a server-side capture: the
    /// built-in /spy passes the requesting admin so the image is relayed to their
    /// client, and there is no admin here to relay it to. The callback is where the
    /// bytes actually arrive.
    ///
    /// The counters are read here, on the game thread, rather than inside the callback.
    /// A screenshot can take seconds to come back, and by then an arena reset may have
    /// cleared them, and the report would then describe a round nobody was complaining
    /// about.
    internal static void Report(CSteamID reporter, SteamPlayer target, string reason) {
        ulong reported = target.playerID.steamID.m_SteamID;
        Players.TryGetValue(reported, out PlayerState state);

        RemoteAPI.Report payload = new() {
            Reported = reported,
            Reporter = reporter.m_SteamID,
            Reason = reason,
            Where = "the warm-up lobby",
            DamageDealt = state?.DamageDealt ?? 0,
            ShotsFired = state?.ShotsFired ?? 0,
            ShotsHit = state?.ShotsHit ?? 0,
            HeadshotHits = state?.HeadshotHits ?? 0
        };

        target.player.sendScreenshot(CSteamID.Nil, (_, image) => {
            payload.Screenshot = image;
            RemoteAPI.SendReport(payload);
        });
    }

    /// Remove a banned player, telling them why rather than dropping them silently.
    ///
    /// Their lobby state goes with them so a reconnect is a fresh arrival rather than a
    /// resumption, and so nothing left behind keeps counting them as present.
    private static void KickBanned(ulong steam64, RemoteAPI.BanInfo ban) {
        string reason = string.IsNullOrWhiteSpace(ban.Reason) ? "Banned" : ban.Reason;
        string until = ban.Permanent ? "permanently" : "temporarily";

        Players.Remove(steam64);
        TargetRange.Stop(steam64);
        Provider.kick(new CSteamID(steam64), $"You are {until} banned from PeekSeek: {reason}. Appeal at peekseek.net/discord");
    }

    /// A matched player stops fighting: back to a spawn point, and handed the link that
    /// logs them in and lands them on their match page. Hitting /match while logged out
    /// is itself what records it as the page to return to, so the site sends them back
    /// there after login without needing a redirect parameter.
    ///
    /// Their own queue choice is deliberately left alone. Holding a match already keeps
    /// them out of the queue — that is what WantsAMatch checks — so switching Queueing
    /// off as well changed nothing while the match lasted and threw away the one thing
    /// needed to put them back when it did not.
    ///
    /// Having a match is what matters here, not being in this lobby's queue — a match
    /// made on the site or in a party has to pull someone out of the arena just the
    /// same, or they fight on unaware their match is waiting.
    private static void SendToMatch(ulong steam64, PlayerState state, int matchId) {
        state.MatchId = matchId;
        state.RelayedTo = null;
        // The range was only ever there to fill the wait, and the wait is over.
        TargetRange.Stop(steam64);
        // AFK is a lobby state and this player is leaving the lobby. Left set it would
        // outlive the reason for it: the countdown below is about to move them anyway,
        // and anything asking whether they can be hurt would keep saying no.
        state.Afk = false;

        CSteamID steamID = new(steam64);
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        // Warned and counted down rather than moved on the spot: being teleported out
        // of a firefight the instant a match lands reads as the server breaking. This
        // gives them the end of the fight they are already in, and tells them why the
        // ground is about to move.
        state.PullAt = Time.realtimeSinceStartup + PeekSeekLobby.Config.MatchWarningSeconds;
        Queue.Close(player.channel.owner);
        Banner.ShowTo(player.channel.owner, "MATCH FOUND", PeekSeekLobby.Instance.Translate("match_found_name"),
            PeekSeekLobby.Instance.Translate("match_found_line", Mathf.RoundToInt(PeekSeekLobby.Config.MatchWarningSeconds)));
    }

    /// Pulls them in when the count on the block runs out. The count is what makes
    /// somebody stop pushing a fight they are about to be taken out of; it is the
    /// difference between being moved and being interrupted.
    private static void CountDownToMatch(CSteamID steamID, PlayerState state, float now) {
        if (now < state.PullAt) return;

        state.PullAt = 0;
        PullIntoMatch(steamID, state);
    }

    /// The end of the countdown: out of the arena, onto a spawn point, until the site
    /// says which server to go to, which is usually the next refresh.
    private static void PullIntoMatch(CSteamID steamID, PlayerState state) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        // Being pulled out for a match ends the wait between lives — a deploy still
        // pending would otherwise drop them back into the arena moments later.
        state.DeployAt = 0;
        // Out of the arena, which is what "pulled out for a match" means. This used to
        // teleport them to an arena spawn, so being given a match dropped them into the
        // middle of a fight to read the link from.
        TeleportToSafeSpawn(player);
        state.SpawnProtectedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.SpawnProtectionSeconds;
    }

    /// The site fills in a server code the moment a server holds the match, and that
    /// server takes the picks on a board of its own, so this is the moment the player
    /// is handed over: the same relay the match plugin uses to send everyone back here
    /// when a match ends.
    ///
    /// Sent once per match server rather than once per match: a relay the client refuses
    /// leaves the player standing here, and the site goes on answering with the same
    /// code until the match is over.
    private static void RelayToMatch(ulong steam64, PlayerState state, string serverCode) {
        if (!state.ShouldRelayTo(serverCode)) return;
        if (!ulong.TryParse(serverCode, out ulong serverId) || serverId == 0) return;

        Player player = PlayerTool.getPlayer(new CSteamID(steam64));
        if (player == null) return;

        state.RelayedTo = serverCode;
        player.sendRelayToServer(new CSteamID(serverId), string.Empty, true);
    }

    /// The match this player was taken out of the arena for is over and they are still
    /// standing here, so they are put back into the fight and back into the queue.
    ///
    /// This is the ordinary ending, not an edge case: two in five matches finish with
    /// nobody having turned up, and the players they were built out of are usually still
    /// in this lobby when that happens. Without this they keep a match id the site
    /// stopped believing in — parked on the spawn point PullIntoMatch left them on, never
    /// offered another match, and left out of the arena count for the rest of the session,
    /// which is a lobby quietly emptying itself of exactly the people who wanted to play.
    private static void ReturnToArena(ulong steam64, PlayerState state) {
        state.MatchId = 0;
        state.RelayedTo = null;
        // A match that died mid-countdown takes its countdown with it, or Tick would go
        // on pulling them out for somewhere that no longer exists.
        state.PullAt = 0;

        CSteamID steamID = new(steam64);
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        Banner.ShowTo(player.channel.owner, "MATCH OVER", PeekSeekLobby.Instance.Translate("match_over_name"),
            PeekSeekLobby.Instance.Translate("match_over_line"));
        SpawnPlayer(steamID, state);
    }

    /// Switches the caller's kit category and re-equips them immediately (no
    /// teleport, no fresh spawn protection — just what they're holding). False for
    /// an unrecognised category or a player Tick doesn't know about.
    internal static bool SwitchKit(CSteamID steamID, string category) {
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;
        if (!PeekSeekLobby.Config.AllowedKitCategories.Contains(category)) return false;

        state.KitCategory = category;
        Player player = PlayerTool.getPlayer(steamID);
        if (player != null) EquipRealKit(player, steamID, state);
        return true;
    }

    internal static void Announce(string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, null, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    internal static void Whisper(SteamPlayer to, string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, to, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    /// Everyone still queueing gets pinged on a timer, spawn protection expires on
    /// its own clock, and anyone waiting out a respawn is deployed into the arena
    /// once their timer is up. Nobody is ever dead here — the respawn itself
    /// happens the moment the life updates.
    internal static void Tick() {
        float now = Time.realtimeSinceStartup;
        Feed.Tick();
        Banner.Tick();
        Card.Tick();
        TickArenaClock(now);
        if (ScoresLockedUntil > 0 && now >= ScoresLockedUntil) {
            ScoresLockedUntil = 0;
            Scores.HideAll();
        }

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            CSteamID steamID = new(entry.Key);
            PlayerState state = entry.Value;

            // Checked every tick rather than on the magazine timer: the gap between
            // reloading a drum away and having it taken back is the window in which
            // it could be reloaded straight back in.
            if (state.StreakMagItemId != 0) TakeBackSpentStreakMagazine(steamID, state);

            if (state.PullAt > 0) CountDownToMatch(steamID, state, now);
            if (!state.Prompted && state.MatchId == 0 && !state.Afk && now >= state.PromptAt) AskToQueue(steamID, state);
            if (state.DeployAt > 0 && !state.Afk) ShowRespawnCount(steamID, state, now);

            // An AFK player's deploy stays booked rather than being cancelled, so
            // coming back puts them into the arena on the next tick instead of making
            // them die once more to get there.
            if (state.DeployAt > 0 && now >= state.DeployAt && !state.Afk) {
                state.DeployAt = 0;
                SpawnPlayer(steamID, state);
            }

            // Only to say so — the protection itself stopped applying the moment
            // the timer passed, because that is the one thing anything checks.
            if (!state.Afk && state.SpawnProtectedUntil > 0 && now >= state.SpawnProtectedUntil) {
                EndSpawnProtection(steamID, state);
            }
        }

        if (PeekSeekLobby.Config.UnlimitedStamina && now - LastStaminaTopUp >= StaminaTopUpIntervalSeconds) {
            LastStaminaTopUp = now;
            RefillStamina();
        }

        if (now - LastHudUpdate >= HudIntervalSeconds) {
            LastHudUpdate = now;
            UpdateArenaHud();
            ForceEveryoneSolo();
            RespawnAnyoneStillDead();
        }

        if (now - LastBarUpdate >= BarIntervalSeconds) {
            LastBarUpdate = now;
            Bar.Show(View(now));
        }

        if (now - LastReputationSync >= ReputationSyncIntervalSeconds) {
            LastReputationSync = now;
            SyncReputations();
        }

        if (ArenaCountingSince > 0 && now - ArenaCountingSince >= PeekSeekLobby.Config.ResetIntervalSeconds) {
            ArenaCountingSince = now;
            ResetArena();
        }

        if (now - LastMagazineCheck >= MagazineCheckIntervalSeconds) {
            LastMagazineCheck = now;
            TopUpMagazines();
        }

        if (now - LastRefreshSent < PeekSeekLobby.Config.HeartbeatIntervalSeconds) return;
        LastRefreshSent = now;
        RefreshPlayers();
    }

    private static void TopUpMagazines() {
        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            PlayerState state = entry.Value;
            if (state.SpareMagItemId == 0) continue;

            Player player = PlayerTool.getPlayer(new CSteamID(entry.Key));
            if (player == null) continue;

            RestockMagazines(player, state);
        }
    }

    /// Reloading doesn't consume the spare — it swaps the spent magazine back into
    /// the inventory — so the magazine *count* never drops and counting alone sees
    /// nothing wrong while every spare sits empty. Partly-used magazines are
    /// dropped and replaced instead, which also self-heals a spare that was
    /// thrown away rather than fired.
    private static void RestockMagazines(Player player, PlayerState state) {
        byte capacity = MagazineCapacity(state.SpareMagItemId);
        int full = 0;

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            // Backwards: removing an item shifts everything after it down an index.
            for (int index = player.inventory.getItemCount(page) - 1; index >= 0; index--) {
                ItemJar jar = player.inventory.getItem(page, (byte)index);
                if (jar?.item == null || jar.item.id != state.SpareMagItemId) continue;

                if (capacity > 0 && jar.item.amount < capacity) {
                    player.inventory.removeItem(page, (byte)index);
                    continue;
                }
                full++;
            }
        }

        for (int i = full; i < state.SpareMagFullStock; i++) {
            player.inventory.forceAddItem(new Item(state.SpareMagItemId, true), true);
        }
    }

    /// Rounds the magazine holds when full. Zero when the id isn't a magazine
    /// asset, which makes RestockMagazines keep every one it finds rather than
    /// deleting items it can't reason about.
    private static byte MagazineCapacity(ushort itemId) {
        return Assets.find(EAssetType.ITEM, itemId) is ItemMagazineAsset asset ? asset.amount : (byte)0;
    }

    /// Steam groups are applied on connect without the player choosing to, and
    /// group mates cannot damage each other, so anyone sharing a group would be
    /// unkillable to their friends in what is meant to be a free-for-all.
    private static void ForceSolo(Player player) {
        if (!PeekSeekLobby.Config.ForceSoloPlayers) return;
        if (player.quests.groupID == CSteamID.Nil) return;

        player.quests.leaveGroup(true);
        GroupManager.alertGroupmatesLeft(player);
    }

    /// Swept rather than only handled on join: a group can be re-applied at any
    /// point by Steam or by the player, and the cost of checking is a field read.
    private static void ForceEveryoneSolo() {
        if (!PeekSeekLobby.Config.ForceSoloPlayers) return;

        foreach (SteamPlayer client in Provider.clients) {
            if (client?.player != null) ForceSolo(client.player);
        }
    }

    /// Topped back up rather than disabled outright: there is no switch for
    /// infinite stamina, and refilling faster than it can visibly drain is
    /// indistinguishable from having none.
    ///
    /// Only for whoever has actually spent some. serverModifyStamina replicates, so
    /// topping up a bar that is already full is a packet per player, twice a second,
    /// for the entire life of the server, that changes nothing on the receiving end.
    private static void RefillStamina() {
        foreach (SteamPlayer client in Provider.clients) {
            if (client?.player == null || client.player.life.isDead) continue;
            if (client.player.life.stamina >= StaminaTopUpThreshold) continue;

            client.player.life.serverModifyStamina(byte.MaxValue);
        }
    }

    /// The arena HUD draws the level number under the server name, so it is made to
    /// carry how many people are actually here. The server name itself is left
    /// exactly as the server set it — it is the server's name, not a scoreboard.
    ///
    /// Sent only when the number changes. It used to go out to every client every
    /// second for the life of the server, gathering a fresh connection list each
    /// time, to say what it had already said — for a figure that moves when somebody
    /// joins or leaves and at no other time.
    private static void UpdateArenaHud() {
        int here = Mathf.Min(Provider.clients.Count, byte.MaxValue);
        if (here == LastLevelNumberSent) return;
        LastLevelNumberSent = here;

        LevelManager.SendLevelNumber.InvokeAndLoopback(
            ENetReliability.Unreliable,
            Provider.GatherRemoteClientConnections(),
            (byte)here);
    }

    /// Unturned adjusts reputation itself when players kill each other, so this keeps
    /// putting it back — the number over someone's head is their Elo and nothing else.
    /// A player the site has not answered for yet is skipped rather than stamped with a
    /// zero; every refresh carries their Elo, so one blip no longer costs somebody their
    /// rank for a whole session the way a single lookup on connect did.
    private static void SyncReputations() {
        if (!PeekSeekLobby.Config.ShowEloAsReputation) return;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            PlayerState state = entry.Value;
            if (!state.EloKnown) continue;

            Player player = PlayerTool.getPlayer(new CSteamID(entry.Key));
            if (player == null) continue;

            // Asserted negative: a 300-Elo player carries -300 reputation in game.
            int drift = -state.Elo - player.skills.reputation;
            if (drift != 0) player.skills.askRep(drift);
        }
    }

    /// Wipes the arena back to how it started: nothing left on the ground,
    /// nothing built, every session score back to zero and everyone respawned
    /// with a fresh kit. Elo is deliberately untouched — it isn't a session stat.
    private static void ResetArena() {
        // The wipe clears every barricade on the map, which is where a running
        // range keeps its targets. Skipping the whole cycle rather than sparing
        // them is the honest version: the only player here is the one on the
        // range, so there is nothing else the wipe would have tidied up.
        if (TargetRange.AnyRunning) return;

        ScoresLockedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.ResetBoardSeconds;
        Scores.ShowAll(Sheet());

        ItemManager.askClearAllItems();
        BarricadeManager.askClearAllBarricades();
        StructureManager.askClearAllStructures();

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            PlayerState state = entry.Value;
            state.Kills = 0;
            state.Deaths = 0;
            state.Headshots = 0;
            state.DamageDealt = 0;
            state.ShotsFired = 0;
            state.ShotsHit = 0;
            state.HeadshotHits = 0;
            state.Streak.EndRound();

            CSteamID steamID = new(entry.Key);
            Player player = PlayerTool.getPlayer(steamID);
            if (player == null) continue;

            // A reset puts everyone straight back in, so anyone still waiting out a
            // respawn skips the rest of it rather than being deployed a second time.
            state.DeployAt = 0;
            if (player.life.isDead) player.life.ServerRespawn(false);
            SpawnPlayer(steamID, state);
        }
        Bar.Redraw();
    }

    /// One request per player, every tick. Whether it queues them or only asks about
    /// them is the only difference — both come back with the same answer, so there is no
    /// second round trip between "you have a match" and "here is where it is".
    ///
    /// The population report goes out from here rather than from Tick because this is
    /// where all three of its numbers are already known, and a count taken anywhere else
    /// would be a second place deciding who is in the arena and who is waiting.
    private static void RefreshPlayers() {
        float now = Time.realtimeSinceStartup;
        int inArena = 0;
        int wanting = 0;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            if (entry.Value.MatchId == 0) inArena++;

            // Queueing has a side effect, so it is only ever used for someone who
            // actually wants a match: asking with it would quietly undo a /noqueue, and
            // would put a player who already has a match back in the queue for another.
            bool wantsAMatch = PeekSeekLobby.Config.EnableQueue
                && entry.Value.WantsAMatch(now);
            if (wantsAMatch) {
                wanting++;
                if (entry.Value.QueueingSince == 0) entry.Value.QueueingSince = now;
                QueueChoice choice = Queue.ChoiceOf(entry.Key);
                RemoteAPI.RefreshQueued(entry.Key,
                    choice?.Bitmask ?? PeekSeekLobby.Config.GamemodeBitmask,
                    choice?.RegionCode ?? PeekSeekLobby.Config.szRegion);
                continue;
            }

            entry.Value.QueueingSince = 0;
            RemoteAPI.Refresh(entry.Key);
        }

        RemoteAPI.ReportPopulation(inArena, wanting);
    }

    // ---- the screens ---------------------------------------------------------------

    private const float BarIntervalSeconds = 0.5f;
    private static float LastBarUpdate;

    /// The bar as it stands: faces by kills, the queue cell for the viewer's own
    /// state, which is the same for everybody since the block is one block, and the
    /// arena's clock while it counts.
    ///
    /// The block is shared, so the queue cell shows the state most worth showing:
    /// a match found for anybody counts down for everybody to see, else the queue
    /// with the longest wait on it, else nothing.
    private static LobbyView View(float now) {
        LobbyView view = new();

        foreach (SteamPlayer client in Provider.clients) {
            if (client.player == null || !Players.TryGetValue(client.playerID.steamID.m_SteamID, out PlayerState state)) continue;
            view.Faces.Add(new LobbyFace {
                Steam64 = client.playerID.steamID.m_SteamID,
                Playing = !state.Afk && !client.player.life.isDead && !client.player.movement.isSafe,
                Away = state.Afk,
                Kills = state.Kills
            });
        }
        view.Faces = view.Faces.OrderByDescending(face => face.Kills).ToList();

        PlayerState pulled = Players.Values.Where(state => state.PullAt > 0).OrderBy(state => state.PullAt).FirstOrDefault();
        PlayerState queued = Players.Values.Where(state => state.WantsAMatch(now) && state.QueueingSince > 0)
            .OrderBy(state => state.QueueingSince).FirstOrDefault();
        if (pulled != null) {
            view.State = "MATCH FOUND";
            view.StateTimer = Mathf.CeilToInt(pulled.PullAt - now).ToString();
        } else if (queued != null) {
            view.State = "QUEUE";
            view.StateTimer = LobbyBar.Clock(now - queued.QueueingSince);
        }

        if (ArenaCountingSince > 0) {
            view.ArenaTimer = LobbyBar.Clock(PeekSeekLobby.Config.ResetIntervalSeconds - (now - ArenaCountingSince));
        }
        return view;
    }

    /// The arena counts towards a reset from the moment there are two people in it,
    /// and stops when there are not: one player has nobody to be reset against.
    private static void TickArenaClock(float now) {
        int here = Players.Values.Count(state => state.MatchId == 0);
        if (here >= 2) {
            if (ArenaCountingSince == 0) ArenaCountingSince = now;
            return;
        }
        ArenaCountingSince = 0;
    }

    /// The count in the middle of a dead player's screen, once a second.
    private static void ShowRespawnCount(CSteamID steamID, PlayerState state, float now) {
        int seconds = Mathf.CeilToInt(state.DeployAt - now);
        if (seconds == state.LastCountdownSecond) return;
        state.LastCountdownSecond = seconds;

        Player player = PlayerTool.getPlayer(steamID);
        if (player != null) Card.ShowRespawn(player.channel.owner, Mathf.Max(0, seconds));
    }

    /// The queue screen, put in front of them once. It says what their ping costs
    /// here, because a match on this region cuts a shooter's damage above a number
    /// and finding that out mid-match means being sent somewhere nobody warned you
    /// about.
    private static void AskToQueue(CSteamID steamID, PlayerState state) {
        state.Prompted = true;
        if (!QueueEnabled) return;

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        int ping = Ping.Milliseconds(player.channel.owner);
        string line = ping >= PeekSeekLobby.Config.HighPingMs
            ? PeekSeekLobby.Instance.Translate("queue_high_ping", ping, PeekSeekLobby.Config.HighPingMs)
            : null;
        Queue.Open(player.channel.owner, line);
    }

    /// `/q` opens the same screen rather than toggling silently: what they are
    /// queueing for is a choice, and the command is how they change it.
    internal static void OpenQueueBoard(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null || !QueueEnabled) return;

        AskToQueue(steamID, Players.TryGetValue(steamID.m_SteamID, out PlayerState state) ? state : new PlayerState());
    }

    private static void OnButtonClicked(Player player, string button) {
        bool? answer = Queue.Answer(player.channel.owner, button);
        if (answer == null) return;

        SetQueueing(player.channel.owner.playerID.steamID, answer.Value);
    }

    /// The board as it stands: everybody here by kills, their rank as the badge on
    /// their face, and the arena's clock in the corner when it is running.
    private static ScoreSheet Sheet() {
        ScoreSheet sheet = new() {
            Title = PeekSeekLobby.Instance.Translate("board_title"),
            Columns = ["K", "D", "HS%", "MS", "ELO"],
            Timer = ArenaCountingSince > 0
                ? LobbyBar.Clock(PeekSeekLobby.Config.ResetIntervalSeconds - (Time.realtimeSinceStartup - ArenaCountingSince))
                : null
        };

        foreach (KeyValuePair<ulong, PlayerState> entry in Players.OrderByDescending(e => e.Value.Kills).ThenBy(e => e.Value.Deaths)) {
            PlayerState state = entry.Value;
            SteamPlayer client = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (client == null) continue;

            int hs = state.Kills > 0 ? state.Headshots * 100 / state.Kills : 0;
            sheet.Rows.Add(new ScoreRow {
                Steam64 = entry.Key,
                AvatarUrl = Bar.AvatarOf(entry.Key),
                Name = client.playerID.characterName,
                RankIconUrl = state.RankIcon == null ? null : string.Format(PeekSeekLobby.Config.RankIconURL, state.RankIcon),
                Values = [
                    state.Kills.ToString(),
                    state.Deaths.ToString(),
                    hs.ToString(),
                    Ping.Milliseconds(client).ToString(),
                    state.EloKnown ? state.Elo.ToString() : ""
                ]
            });
        }
        return sheet;
    }

    /// The board on a held plugin key: up while the first one is down, gone when it
    /// comes up. While the reset's board is everybody's it stays.
    private static void OnPluginKey(Player player, uint simulation, byte key, bool state) {
        if (key != 0 || ScoresLockedUntil > 0) return;

        SteamPlayer viewer = player.channel.owner;
        bool up = Scores.IsUp(viewer.playerID.steamID.m_SteamID);
        if (state && !up) Scores.Show(viewer, Sheet());
        else if (!state && up) Scores.Hide(viewer);
    }
}
