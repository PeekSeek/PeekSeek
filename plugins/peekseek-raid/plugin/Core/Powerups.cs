using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core.Models;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeekRaid.Core;

/// What each powerup does beyond the items it hands out. Everything here lasts a round:
/// RoundOver takes down what the round put up and forgets who had what.
///
/// Most twists are keyed on the item, because the item is what reaches the server: a
/// flashbang being thrown, vitamins being eaten, a bedroll going down. The few that are
/// about the player rather than what they hold (Insertion, Welder) are keyed
/// on their pick.
internal static class Powerups {
    private static Raid raid;

    /// Bumped every round, so a coroutine from a round that is over does nothing.
    private static int round;

    private static readonly Dictionary<ulong, float> stunnedUntil = [];
    private static readonly Dictionary<ulong, float> nomadUntil = [];
    private static readonly Dictionary<ulong, float> launchedAt = [];
    private static readonly Dictionary<ulong, (Vector3 Point, bool Used)> bedrolls = [];
    private static readonly List<InteractableVehicle> helis = [];
    private static readonly List<InteractableStorage> crates = [];
    private static readonly HashSet<ulong> watched = [];
    private static float nextFill;

    /// What an attacker put down this round away from the base itself: scaffolds,
    /// ladders, bedrolls. The base restore only clears the site, so these are cleared
    /// here.
    private static readonly List<Transform> leftBehind = [];

    private static PeekSeekRaidConfiguration Config => PeekSeekRaid.Config;

    internal static void Hook(Raid running) {
        raid = running;
        UseableThrowable.onThrowableSpawned += OnThrown;
        UseableConsumeable.onConsumePerformed += OnConsumed;
        DamageTool.damagePlayerRequested += OnDamagePlayer;
        BarricadeManager.onBarricadeSpawned += OnBarricadeSpawned;
        StructureManager.onStructureSpawned += OnStructureSpawned;
    }

    internal static void Unhook() {
        UseableThrowable.onThrowableSpawned -= OnThrown;
        UseableConsumeable.onConsumePerformed -= OnConsumed;
        DamageTool.damagePlayerRequested -= OnDamagePlayer;
        BarricadeManager.onBarricadeSpawned -= OnBarricadeSpawned;
        StructureManager.onStructureSpawned -= OnStructureSpawned;

        foreach (Player player in Host.Players()) Unwatch(player);
        RoundOver();
        raid = null;
    }

    /// Listen to one player's equipping and falling, which are per-player events.
    internal static void Watch(Player player) {
        if (!watched.Add(player.channel.owner.playerID.steamID.m_SteamID)) return;

        player.equipment.onEquipRequested += OnEquip;
        player.life.OnFallDamageRequested += OnFall;
    }

    private static void Unwatch(Player player) {
        if (!watched.Remove(player.channel.owner.playerID.steamID.m_SteamID)) return;

        player.equipment.onEquipRequested -= OnEquip;
        player.life.OnFallDamageRequested -= OnFall;
    }

    /// The round is over: take down what it put up and forget who had what.
    internal static void RoundOver() {
        round++;

        foreach (InteractableVehicle heli in helis.Where(heli => heli != null && !heli.isExploded)) VehicleManager.askVehicleDestroy(heli);
        helis.Clear();

        foreach (Transform piece in leftBehind.Where(piece => piece != null)) {
            if (StructureManager.FindStructureByRootTransform(piece) is { } structure) RaidWorld.Remove(structure);
            else if (BarricadeManager.FindBarricadeByRootTransform(piece) is { } barricade) RaidWorld.RemoveBarricade(barricade);
        }
        leftBehind.Clear();

        foreach (ulong steam64 in tagged) {
            if (PlayerTool.getSteamPlayer(new CSteamID(steam64)) is { } shown) Host.Tag.Hide(shown);
        }
        tagged.Clear();
        stunnedUntil.Clear();
        nomadUntil.Clear();
        launchedAt.Clear();
        bedrolls.Clear();
        crates.Clear();
    }

    /// The round goes live: the one helicopter a team may have. It stands on a platform
    /// InsertionHeight up and InsertionDistance further out than the pilot's spawn,
    /// facing away from the base, and the team starts inside it: the pilot at the
    /// controls, as many teammates as there are seats, and the rest where they were.
    internal static void RoundLive() {
        Player pilot = Host.Players().FirstOrDefault(player => {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            return !raid.IsDefending(steam64) && !player.life.isDead && raid.ChosenOption(steam64)?.Twist == Twist.Insertion;
        });
        if (pilot == null) return;

        Vector3 anchor = Config.Sites[raid.DefendedSite].AnchorPoint;
        Vector3 away = pilot.transform.position - anchor;
        away.y = 0f;
        away = away.sqrMagnitude < 1f ? Vector3.forward : away.normalized;
        float yaw = Quaternion.LookRotation(away).eulerAngles.y;

        Vector3 pad = RaidWorld.OnGround(pilot.transform.position + away * Config.InsertionDistance) + Vector3.up * Config.InsertionHeight;
        leftBehind.AddRange(RaidWorld.Platform(pad, yaw, Config.InsertionPlatformItemId));

        InteractableVehicle heli = VehicleManager.spawnVehicleV2(Config.InsertionVehicleId, pad + Vector3.up * 2.5f, Quaternion.Euler(0f, yaw, 0f));
        if (heli == null) return;

        VehicleManager.ServerSetVehicleLock(heli, pilot.channel.owner.playerID.steamID, new CSteamID(raid.AttackingGroup), true);
        helis.Add(heli);
        ushort fuel = (ushort)Mathf.Clamp(Mathf.CeilToInt(heli.asset.fuelBurnRate * Config.InsertionSeconds), 1, ushort.MaxValue);
        VehicleManager.sendVehicleFuel(heli, fuel);

        Run(Board(heli, pilot));
    }

    /// Seat the team: the pilot first, which is the pilot's seat, then everybody else
    /// on the team until it is full. A player still in an equip animation cannot be
    /// seated, so hands are emptied first and the whole thing is tried a few times.
    private static IEnumerator Board(InteractableVehicle heli, Player pilot) {
        int token = round;
        List<Player> team = [
            pilot,
            .. Host.Players().Where(player =>
                player != pilot && !player.life.isDead && !raid.IsDefending(player.channel.owner.playerID.steamID.m_SteamID))
        ];

        for (int attempt = 0; attempt < 6; attempt++) {
            yield return new WaitForSeconds(0.35f);
            if (token != round || heli == null || heli.isExploded) yield break;

            foreach (Player player in team.Where(player => player != null && player.movement.getVehicle() == null)) {
                if (player.equipment.HasValidUseable) player.equipment.dequip();
                if (!VehicleManager.ServerForcePassengerIntoVehicle(player, heli) && heli.passengers.All(seat => seat.player != null)) yield break;
            }
        }
    }

    // ---- once a second -----------------------------------------------------------

    internal static void Tick() {
        if (raid == null) return;
        float now = Time.realtimeSinceStartup;

        ShowCountdowns(now);
        WearBedrolls();

        if (now >= nextFill) {
            nextFill = now + Config.SupplierSeconds;
            FillCrates();
        }
    }

    /// The tag under the crosshair, for whatever is counting down on a player: a stun,
    /// a Nomad's vitamins. Off base is Raid's own tag and
    /// gives way to these.
    internal static bool HasTag(ulong steam64) => TagFor(steam64, Time.realtimeSinceStartup) != null;

    private static (string Label, float Left)? TagFor(ulong steam64, float now) {
        if (stunnedUntil.TryGetValue(steam64, out float stun) && stun > now) return ("STUNNED", stun - now);
        if (nomadUntil.TryGetValue(steam64, out float nomad) && nomad > now) return ("NOMAD", nomad - now);
        return null;
    }

    /// Players with one of these tags up, so it can be taken down when it runs out
    /// instead of staying on screen at "1 s".
    private static readonly HashSet<ulong> tagged = [];

    private static void ShowCountdowns(float now) {
        foreach (Player player in Host.Players()) {
            ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
            if (TagFor(steam64, now) is not { } tag) {
                if (tagged.Remove(steam64)) Host.Tag.Hide(player.channel.owner);
                continue;
            }
            tagged.Add(steam64);

            Host.Tag.Show(player.channel.owner, tag.Label, -1, $"{Mathf.Max(0, Mathf.CeilToInt(tag.Left))} s");
        }
    }

    /// Whether off-base damage is off for a defender right now: a Nomad's thirty
    /// seconds, from the moment they ate.
    internal static bool OffBaseImmune(ulong steam64) {
        return nomadUntil.TryGetValue(steam64, out float until) && until > Time.realtimeSinceStartup;
    }

    // ---- throwables ------------------------------------------------------------------

    /// Every throw, tagged with what it is and who threw it; a flashbang and a red smoke
    /// are followed to where they land.
    private static void OnThrown(UseableThrowable useable, GameObject thrown) {
        if (raid == null || useable?.equippedThrowableAsset == null) return;

        Damage.Unprotect(useable.player);

        ushort id = useable.equippedThrowableAsset.id;
        CSteamID thrower = useable.player.channel.owner.playerID.steamID;
        Thrown tag = thrown.AddComponent<Thrown>();
        tag.ItemId = id;
        tag.Thrower = thrower;

        if (id == Config.FlashbangItemId) Run(Flash(thrown, useable.equippedThrowableAsset.fuseLength, thrower));
        if (id == Config.RedSmokeItemId && raid.IsDefending(thrower.m_SteamID)) Run(Strike(thrown, thrower));
    }

    /// The flashbang blinds on each client, which the server never sees, so the same
    /// test runs here: within 32 m, looking no further than a little behind it, and with
    /// nothing in the way (Flashbang.Explode). Anybody it would blind cannot fire a gun
    /// for StunSeconds; anybody within StunDamageRadius takes StunDamage as well.
    private static IEnumerator Flash(GameObject thrown, float fuse, CSteamID thrower) {
        int token = round;
        yield return new WaitForSeconds(Mathf.Max(0f, fuse - 0.05f));
        if (token != round || thrown == null) yield break;

        Vector3 bang = thrown.transform.position;
        foreach (Player player in Host.Players().ToList()) {
            if (player.life.isDead) continue;

            Vector3 eye = player.look.aim.position;
            Vector3 toBang = bang - eye;
            if (toBang.sqrMagnitude < 1024f && Vector3.Dot(toBang.normalized, player.look.aim.forward) > -0.25f
                && !Physics.Raycast(eye, toBang.normalized, Mathf.Max(0f, toBang.magnitude - 0.5f), RayMasks.DAMAGE_SERVER, QueryTriggerInteraction.Ignore)) {
                Stun(player);
            }

            if ((player.transform.position - bang).sqrMagnitude <= Config.StunDamageRadius * Config.StunDamageRadius) {
                DamageTool.damage(player, EDeathCause.GRENADE, ELimb.SPINE, thrower, Vector3.up, Config.StunDamage, 1f, out EPlayerKill _,
                                  applyGlobalArmorMultiplier: false, trackKill: true);
            }
        }
    }

    private static void Stun(Player player) {
        stunnedUntil[player.channel.owner.playerID.steamID.m_SteamID] = Time.realtimeSinceStartup + Config.StunSeconds;
        if (player.equipment.asset is ItemGunAsset) player.equipment.dequip();
    }

    /// Red smoke: once it has come to rest, a siren, then rockets straight down on it in
    /// a small triangle, a moment apart so each is its own blast. Never on the thrower's
    /// own base: that smoke is refused and handed back.
    private static IEnumerator Strike(GameObject thrown, CSteamID thrower) {
        int token = round;
        Rigidbody body = thrown.GetComponent<Rigidbody>();
        for (float waited = 0f; waited < 6f && thrown != null && body != null && !body.IsSleeping(); waited += 0.25f) {
            yield return new WaitForSeconds(0.25f);
        }
        if (token != round || thrown == null) yield break;

        Vector3 landed = thrown.transform.position;
        Vector3 home = Config.Sites[raid.DefendedSite].AnchorPoint;
        if ((landed - home).sqrMagnitude < Config.StrikerSafeRadius * Config.StrikerSafeRadius) {

            if (PlayerTool.getPlayer(thrower) is { } striker) {
                striker.inventory.forceAddItemAuto(new Item(Config.RedSmokeItemId, true), false, false, false);
                Host.Whisper(striker.channel.owner, PeekSeekRaid.Instance.Translate("striker_too_close", Config.StrikerSafeRadius));
            }
            yield break;
        }

        FlyOver(landed);
        if (Config.SirenEffect != 0) Effect(Config.SirenEffect, landed);
        yield return new WaitForSeconds(Config.StrikerDelay);
        if (token != round) yield break;

        // Every tree under the strike comes down with it.
        DamageTool.explode(new ExplosionParameters(landed, Config.StrikerRadius, EDeathCause.MISSILE, thrower) {
            resourceDamage = 100000f,
            objectDamage = 0f,
            damageOrigin = EDamageOrigin.Rocket_Explosion
        }, out List<EPlayerKill> _);

        ItemGunAsset launcher = Assets.find(EAssetType.ITEM, Config.LauncherItemId) as ItemGunAsset;
        for (int i = 0; i < Config.StrikerRockets; i++) {
            Vector2 spread = Random.insideUnitCircle * Config.StrikerRadius;
            Vector3 point = Ground(landed + new Vector3(spread.x, 0f, spread.y));
            if (launcher != null) Effect(launcher.explosion, point);

            ExplosionParameters blast = new(point, Config.RocketBlastRadius, EDeathCause.MISSILE, thrower) {
                playerDamage = launcher?.playerDamageMultiplier.damage ?? 200f,
                barricadeDamage = launcher?.barricadeDamage ?? 1000f,
                structureDamage = launcher?.structureDamage ?? 1000f,
                damageOrigin = EDamageOrigin.Rocket_Explosion
            };
            DamageTool.explode(blast, out List<EPlayerKill> _);

            // Not a metronome: a strike that lands on a beat sounds scripted.
            yield return new WaitForSeconds(Config.StrikerSpacing * Random.Range(0.5f, 1.8f));
            if (token != round) yield break;
        }
    }

    /// The game's own airdrop plane over a point, arriving as the rockets start, with no
    /// drop: only the plane's flight is sent to clients (LevelManager.ReceiveAirdropState);
    /// the server never tracks it, so nothing lands.
    private static void FlyOver(Vector3 over) {
        Vector3 heading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        Vector3 start = over - heading * Config.StrikerPlaneSpeed * Config.StrikerDelay;
        start.y = over.y + Config.StrikerPlaneHeight;
        LevelManager.SendAirdropState.Invoke(ENetReliability.Reliable, Provider.GatherRemoteClientConnections(),
            start, heading * Config.StrikerPlaneSpeed);
    }

    private static Vector3 Ground(Vector3 point) {
        return Physics.Raycast(point + Vector3.up * Config.StrikerHeight, Vector3.down, out RaycastHit hit, Config.StrikerHeight * 2f,
                               RayMasks.BLOCK_EXPLOSION, QueryTriggerInteraction.Ignore)
            ? hit.point
            : point;
    }

    // ---- eating, equipping, falling --------------------------------------------------

    private static void OnConsumed(Player player, ItemConsumeableAsset asset) {
        if (raid == null || asset.id != Config.NomadItemId) return;

        nomadUntil[player.channel.owner.playerID.steamID.m_SteamID] = Time.realtimeSinceStartup + Config.NomadSeconds;
    }

    /// No gun for somebody stunned.
    private static void OnEquip(PlayerEquipment equipment, ItemJar jar, ItemAsset asset, ref bool shouldAllow) {
        if (asset is not ItemGunAsset gun) return;
        ulong steam64 = equipment.player.channel.owner.playerID.steamID.m_SteamID;
        float now = Time.realtimeSinceStartup;

        if (stunnedUntil.TryGetValue(steam64, out float until) && until > now) shouldAllow = false;
    }

    /// Somebody a bounce charge threw lands without hurting.
    private static void OnFall(PlayerLife life, float velocity, ref float damage, ref bool breakLegs) {
        ulong steam64 = life.player.channel.owner.playerID.steamID.m_SteamID;
        if (!launchedAt.TryGetValue(steam64, out float at) || Time.realtimeSinceStartup - at > Config.ElevatorFallSeconds) return;

        damage = 0f;
        breakLegs = false;
    }

    // ---- players getting hurt -------------------------------------------------------

    /// A bounce charge's blast marks whoever it throws; a hit that would kill somebody
    /// with a Second Life bedroll standing sends them back to it instead.
    private static void OnDamagePlayer(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        if (raid == null || !shouldAllow || parameters.player == null) return;
        Player player = parameters.player;
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;

        if (Damage.Current?.Tool == Config.BounceChargeItemId) launchedAt[steam64] = Time.realtimeSinceStartup;

        if (raid.Phase != RaidPhase.Playing || parameters.damage * parameters.times < player.life.health) return;

        // The first of the team's standing bedrolls brings back whoever dies first.
        bool defending = raid.IsDefending(steam64);
        ulong placer = bedrolls.Where(bed => !bed.Value.Used && raid.IsDefending(bed.Key) == defending && BedrollAt(bed.Value.Point) != null)
            .Select(bed => bed.Key).FirstOrDefault();
        if (placer == 0) return;
        (Vector3 Point, bool Used) bed = bedrolls[placer];
        if (BedrollAt(bed.Point) is not { } drop) return;

        shouldAllow = false;
        bedrolls[placer] = (bed.Point, true);
        RaidWorld.RemoveBarricade(drop);
        SecondLife(player, bed.Point);
    }

    /// Back at a teammate's bedroll on SecondLifeHealth, carrying their guns, the
    /// ammunition for them, their medkits and whatever they wear, and nothing else.
    private static void SecondLife(Player player, Vector3 at) {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items items = player.inventory.items[page];
            if (items == null) continue;

            for (int i = items.getItemCount() - 1; i >= 0; i--) {
                ItemAsset asset = items.getItem((byte)i)?.item?.GetAsset();
                bool keep = asset is ItemGunAsset or ItemMagazineAsset || asset?.id == Config.MedkitItemId;
                if (!keep) player.inventory.removeItem(page, (byte)i);
            }
        }

        player.teleportToLocationUnsafe(at + Vector3.up, player.look.yaw);
        player.life.serverModifyHealth(Config.SecondLifeHealth - player.life.health);
        player.life.serverSetBleeding(false);
        player.life.serverSetLegsBroken(false);
    }

    private static BarricadeDrop BedrollAt(Vector3 point) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(point, 1f, around);
        foreach (RegionCoordinate coordinate in around) {
            foreach (BarricadeDrop drop in BarricadeManager.regions[coordinate.x, coordinate.y].drops) {
                if (drop.asset?.id == Config.BedrollItemId && (drop.GetServersideData().point - point).sqrMagnitude < 0.25f) return drop;
            }
        }
        return null;
    }

    /// A bedroll loses its health over SecondLifeSeconds, so it stands about that long.
    private static void WearBedrolls() {
        foreach ((Vector3 point, bool used) in bedrolls.Values.ToList()) {
            if (used || BedrollAt(point) is not { } drop) continue;

            float perSecond = drop.asset.health / Mathf.Max(1f, Config.SecondLifeSeconds);
            BarricadeManager.damage(drop.model, perSecond, 1f, armor: false, default, EDamageOrigin.Trap_Wear_And_Tear);
        }
    }

    // ---- placing -------------------------------------------------------------------

    /// Whether an attacker may put this down here: anything, anywhere, once the round is
    /// live. What they build comes down when it ends.
    internal static bool AttackerMayPlace(ulong owner, ushort id, Vector3 point) {
        return raid != null && raid.Phase == RaidPhase.Playing && !raid.IsDefending(owner) && id != Config.AnchorMarkerItemId;
    }

    /// Whether a defender may put a structure down mid-round: only a Welder's walls.
    internal static bool DefenderMayBuild(ulong owner, ushort id) {
        return raid != null && raid.IsDefending(owner) && raid.ChosenOption(owner)?.Twist == Twist.Welder
               && raid.ChosenOption(owner).Items.Any(item => item.ItemId == id);
    }

    private static void OnStructureSpawned(StructureRegion region, StructureDrop drop) {
        if (raid == null || raid.Phase is not (RaidPhase.Picking or RaidPhase.Playing)) return;
        StructureData data = drop.GetServersideData();
        if (data.owner == 0) return;

        if (!raid.IsDefending(data.owner)) {
            leftBehind.Add(drop.model);
            return;
        }

        // A Welder's wall that seals the flag in comes back down, into their hands.
        if (RaidWorld.FlagPoint(Config.Sites[raid.DefendedSite]) is { } flag && BreachCheck.Cost(flag) > Config.BreachLimit) {
            RaidWorld.Remove(drop);
            if (PlayerTool.getPlayer(new CSteamID(data.owner)) is { } welder) {
                welder.inventory.forceAddItemAuto(new Item(drop.asset.id, true), false, false, false);
                Host.Whisper(welder.channel.owner, PeekSeekRaid.Instance.Translate("wall_breach", Config.BreachLimit));
            }
        }
    }

    private static void OnBarricadeSpawned(BarricadeRegion region, BarricadeDrop drop) {
        if (raid == null || raid.Phase is not (RaidPhase.Picking or RaidPhase.Playing) || drop.asset == null) return;
        BarricadeData data = drop.GetServersideData();

        if (data.owner == 0) return;

        if (!raid.IsDefending(data.owner)) {
            leftBehind.Add(drop.model);
            if (drop.asset.id == Config.BedrollItemId) bedrolls[data.owner] = (data.point, false);
            return;
        }

        if (drop.asset.id == Config.StereoItemId) Run(Fake(drop));
        if (drop.asset.id == Config.SupplierItemId && drop.interactable is InteractableStorage crate) crates.Add(crate);
    }

    // ---- Faker -------------------------------------------------------------------

    /// A stereo that sounds like a fight: every few seconds, a burst of impacts from
    /// where it stands, a random one each time, for as long as it stands.
    private static IEnumerator Fake(BarricadeDrop stereo) {
        int token = round;
        while (token == round && stereo?.model != null) {
            yield return new WaitForSeconds(Random.Range(Config.FakerMinSeconds, Config.FakerMaxSeconds));
            if (token != round || stereo?.model == null || Config.FakerEffects.Count == 0) yield break;

            int shots = Random.Range(3, 7);
            for (int i = 0; i < shots && stereo?.model != null; i++) {
                Effect(Config.FakerEffects[Random.Range(0, Config.FakerEffects.Count)], stereo.model.position + Random.insideUnitSphere * 1.5f);
                yield return new WaitForSeconds(Random.Range(0.08f, 0.25f));
            }
        }
    }

    // ---- traps -------------------------------------------------------------------

    /// Everybody is in the arena while Raid runs. The game refuses a vehicle to anybody
    /// who is not (VehicleManager.ReceiveEnterVehicleRequest), and Match switches the
    /// arena round off, so without this nobody could get into the Insertion heli.
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.isPlayerInArena))]
    [UsedImplicitly]
    internal class ArenaPatch {
        [UsedImplicitly]
        private static bool Prefix(ref bool __result) {
            if (raid == null) return true;

            __result = true;
            return false;
        }
    }

    /// A claymore only goes off for an attacker; a snare that catches an attacker roots
    /// them as well as breaking their legs.
    [HarmonyPatch(typeof(InteractableTrap), nameof(InteractableTrap.NotifyTrapEntered))]
    [UsedImplicitly]
    internal class TrapPatch {
        [UsedImplicitly]
        private static bool Prefix(InteractableTrap __instance, Collider other) {
            if (raid == null) return true;
            ushort id = BarricadeManager.FindBarricadeByRootTransform(__instance.transform.root)?.asset?.id ?? 0;
            if (id != Config.ClaymoreItemId) return true;

            Player victim = DamageTool.getPlayer(other.transform);
            return victim != null && !raid.IsDefending(victim.channel.owner.playerID.steamID.m_SteamID);
        }

        [UsedImplicitly]
        private static void Postfix(InteractableTrap __instance, Collider other) {
            if (raid == null) return;
            ushort id = BarricadeManager.FindBarricadeByRootTransform(__instance.transform.root)?.asset?.id ?? 0;
            if (id != Config.SnareItemId) return;

            Player victim = DamageTool.getPlayer(other.transform);
            if (victim == null || raid.IsDefending(victim.channel.owner.playerID.steamID.m_SteamID)) return;
            Run(Root(victim));
        }
    }

    private static IEnumerator Root(Player victim) {
        RaidWorld.Freeze(victim, true);
        yield return new WaitForSeconds(Config.SnareRootSeconds);
        if (victim != null) RaidWorld.Freeze(victim, false);
    }

    // ---- Supplier ------------------------------------------------------------------

    /// Every SupplierSeconds each crate makes a blood bag, as long as it holds fewer
    /// than SupplierCap.
    private static void FillCrates() {
        crates.RemoveAll(crate => crate == null);

        foreach (InteractableStorage crate in crates) {
            int held = 0;
            for (byte i = 0; i < crate.items.getItemCount(); i++) {
                if (crate.items.getItem(i).item.id == Config.SupplyItemId) held++;
            }
            if (held < Config.SupplierCap) crate.items.tryAddItem(new Item(Config.SupplyItemId, true));
        }
    }

    // ---- helpers --------------------------------------------------------------------

    private static void Run(IEnumerator work) => PeekSeekRaid.Instance.StartCoroutine(work);

    private static void Effect(ushort id, Vector3 at) {
        if (id == 0 || Assets.find(EAssetType.EFFECT, id) is not EffectAsset asset) return;

        TriggerEffectParameters effect = new(asset) { position = at, relevantDistance = EffectManager.MEDIUM, reliable = true };
        EffectManager.triggerEffect(effect);
    }
}
