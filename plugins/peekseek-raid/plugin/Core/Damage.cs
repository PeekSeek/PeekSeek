using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeekRaid.Core;

/// Every rule about how much a hit does, in one place: the hits table for the base,
/// one piece per blast, and what the explosives and the Suppressor do to players.
///
/// The damage hooks are told who and what was hit, and by what kind of thing (a
/// grenade, a charge, a rocket), but never by which item. So the explosion itself is
/// recorded on its way in (DamageTool.explode, patched below) with the item that set it
/// off, and every hook that fires inside it reads the record. The hooks fire inside
/// that one call, nearest candidate first, which is also what makes "the first piece
/// takes the blast" a matter of remembering whether one already has.
internal static class Damage {
    /// The explosion being worked out right now, or null outside one.
    internal sealed class Blast {
        internal Vector3 Point;
        internal CSteamID Killer;
        internal EDamageOrigin Origin;
        internal ushort Tool;
        internal bool PieceTaken;
    }

    internal static Blast Current { get; private set; }

    /// The item that set off the explosion about to happen, set just before it by
    /// whatever knows: a charge's own barricade, a thrown object's tag.
    private static ushort nextTool;

    private static readonly CutTimer cuts = new(0.6f);

    /// When each attacker's spawn protection runs out. Stamped where they are placed,
    /// dropped the moment they fire, aim or throw: untouchable is only fair while it
    /// buys nothing.
    private static readonly Dictionary<ulong, float> protectedUntil = [];

    /// When each shooter was last told their target is protected. An automatic weapon
    /// asks this question once a bullet, and a player does not need forty lines about
    /// it.
    private static readonly Dictionary<ulong, float> lastProtectedNotice = [];

    private const float ProtectedNoticeIntervalSeconds = 3f;

    private static Raid raid;

    internal static void Hook(Raid running) {
        raid = running;
        cuts.Clear();
        lastProtectedNotice.Clear();
        DamageTool.damagePlayerRequested += OnDamagePlayer;
        UseableGun.onBulletSpawned += Fired;
        UseableGun.onProjectileSpawned += Launched;
        UseableGun.OnAimingChanged_Global += Aimed;
        UseableThrowable.onThrowableSpawned += Threw;
    }

    internal static void Unhook() {
        DamageTool.damagePlayerRequested -= OnDamagePlayer;
        UseableGun.onBulletSpawned -= Fired;
        UseableGun.onProjectileSpawned -= Launched;
        UseableGun.OnAimingChanged_Global -= Aimed;
        UseableThrowable.onThrowableSpawned -= Threw;
        lastProtectedNotice.Clear();
        raid = null;
        Current = null;
    }

    // ---- the explosion record ------------------------------------------------------

    [HarmonyPatch(typeof(DamageTool), nameof(DamageTool.explode),
        new[] { typeof(ExplosionParameters), typeof(List<EPlayerKill>) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    [UsedImplicitly]
    internal class ExplodePatch {
        [UsedImplicitly]
        private static void Prefix(ExplosionParameters parameters) {
            ushort tool = nextTool;
            nextTool = 0;
            if (tool == 0 && parameters.damageOrigin == EDamageOrigin.Rocket_Explosion) tool = PeekSeekRaid.Config.LauncherItemId;

            Current = new Blast {
                Point = parameters.point, Killer = parameters.killer, Origin = parameters.damageOrigin, Tool = tool
            };
        }

        [UsedImplicitly]
        private static void Postfix() => Current = null;
    }

    /// A charge knows which barricade it is; the explosion it sets off does not.
    [HarmonyPatch(typeof(InteractableCharge), nameof(InteractableCharge.Detonate))]
    [UsedImplicitly]
    internal class DetonatePatch {
        [UsedImplicitly]
        private static void Prefix(InteractableCharge __instance, Player instigatingPlayer) {
            nextTool = BarricadeManager.FindBarricadeByRootTransform(__instance.transform)?.asset?.id ?? 0;

            // Setting a charge off is offensive, so it costs the protection too. A
            // charge is the attackers' main tool, and an attacker who could blow one
            // while still untouchable would get the whole opening exchange for free.
            //
            // The person who pressed the detonator, not the barricade's owner. Both
            // ways in hand us the instigator: detonate(CSteamID) resolves the id and
            // calls this, and UseableDetonator calls it directly. Charging the owner
            // instead would take protection off whoever placed the charge while
            // leaving it on whoever actually set it off.
            if (instigatingPlayer != null) Unprotect(instigatingPlayer);
        }
    }

    /// A melee swing is offensive, and UseableMelee has no event for it. This is the
    /// method that does the swing's damage, so a prefix on it is the swing.
    [HarmonyPatch(typeof(UseableMelee), "fire")]
    [UsedImplicitly]
    internal class MeleeSwingPatch {
        [UsedImplicitly]
        private static void Prefix(UseableMelee __instance) {
            if (__instance.player != null) Unprotect(__instance.player);
        }
    }

    /// A thrown grenade carries the tag Throws put on it when it was thrown.
    [HarmonyPatch(typeof(Grenade), nameof(Grenade.Explode))]
    [UsedImplicitly]
    internal class GrenadePatch {
        [UsedImplicitly]
        private static void Prefix(Grenade __instance) {
            nextTool = __instance.GetComponent<Thrown>()?.ItemId ?? 0;
        }
    }

    // ---- the base ----------------------------------------------------------------

    /// What a structure is for the table. A piece of the attackers' own group is a
    /// scaffold, maple however it is built, so a defender's charges break it in two.
    internal static TargetKind KindOf(ItemStructureAsset asset, ulong group) {
        if (asset.construct is EConstruct.FLOOR or EConstruct.FLOOR_POLY or EConstruct.PILLAR or EConstruct.POST) {
            return group != 0 && raid != null && group == raid.AttackingGroup ? TargetKind.MapleWall : TargetKind.Untabled;
        }
        if (group != 0 && raid != null && group == raid.AttackingGroup) return TargetKind.MapleWall;
        if (asset.construct is not (EConstruct.WALL or EConstruct.RAMPART)) return TargetKind.Other;

        return Materials.Split(asset.itemName)?.Material switch {
            "Metal" => TargetKind.MetalWall,
            "Birch" or "Pine" => TargetKind.BirchWall,
            _ => TargetKind.MapleWall
        };
    }

    /// What a barricade is for the table: a door, glass (which is a metal wall to an
    /// explosive), or anything else. The charge doing the damage is not a target.
    internal static TargetKind KindOf(ItemBarricadeAsset asset) {
        return asset.build switch {
            EBuild.DOOR or EBuild.HATCH or EBuild.SHUTTER or EBuild.GATE => TargetKind.Door,
            EBuild.GLASS => TargetKind.MetalWall,
            EBuild.CHARGE => TargetKind.Untabled,
            _ => TargetKind.Other
        };
    }

    /// Settle a hit on a piece of the base: the table's damage, or the game's, or none.
    /// `piece` identifies the piece for a held tool's timer, `onDefendedBase` is
    /// whether it stands on the base under attack.
    internal static void Piece(CSteamID instigator, TargetKind kind, ushort maxHealth, int piece, bool onDefendedBase,
                               EDamageOrigin origin, ref ushort damage, ref bool shouldAllow) {
        if (raid == null || kind == TargetKind.Untabled) return;

        // A defender's own charges never touch their own base: they carry them for the
        // attackers' scaffolds and ladders.
        if (onDefendedBase && Current != null && raid.IsDefending(Current.Killer.m_SteamID)) {
            shouldAllow = false;
            return;
        }

        if (Current != null) {
            if (Current.PieceTaken) {
                shouldAllow = false;
                return;
            }
            Current.PieceTaken = true;

            BreakRule rule = BreakTable.For(PeekSeekRaid.Config.BreakRules, Current.Tool);
            if (rule == null) return;

            damage = BreakTable.DamagePerHit(maxHealth, rule.For(kind));
            if (damage == 0) shouldAllow = false;
            return;
        }

        if (origin != EDamageOrigin.Useable_Melee) return;

        // A held tool on the table: seconds of cutting, not hits.
        Player cutter = PlayerTool.getPlayer(instigator);
        BreakRule cut = cutter == null ? null : BreakTable.For(PeekSeekRaid.Config.BreakRules, cutter.equipment.itemID);
        if (cut == null) return;

        float needed = cut.For(kind);
        if (needed <= 0f) {
            shouldAllow = false;
            return;
        }

        float held = cuts.Hit(instigator.m_SteamID, piece, Time.realtimeSinceStartup);
        if (held + 0.001f < needed) {
            shouldAllow = false;
            return;
        }
        damage = maxHealth;
    }

    /// Bullets on glass: nothing from an attacker on a Mirror pane, which is bulletproof
    /// from outside, and double from the Suppressor on any other.
    internal static void Glass(CSteamID instigator, ushort glassId, EDamageOrigin origin, ref ushort damage, ref bool shouldAllow) {
        if (raid == null || origin != EDamageOrigin.Useable_Gun || Current != null) return;

        if (glassId == PeekSeekRaid.Config.MirrorItemId && !raid.IsDefending(instigator.m_SteamID)) {
            shouldAllow = false;
            return;
        }

        Player shooter = PlayerTool.getPlayer(instigator);
        if (shooter != null && shooter.equipment.itemID == PeekSeekRaid.Config.SuppressorGunId) {
            damage = (ushort)Mathf.Min(ushort.MaxValue, damage * PeekSeekRaid.Config.SuppressorGlassMultiplier);
        }
    }

    // ---- players -----------------------------------------------------------------

    /// An attacker is untouchable for SpawnProtectSeconds from where they come in.
    internal static void Protect(ulong steam64) {
        protectedUntil[steam64] = Time.realtimeSinceStartup + PeekSeekRaid.Config.SpawnProtectSeconds;
    }

    /// Anything offensive gives the protection up, and shooting is not the only way to
    /// spend it: a charge is thrown, a rocket is aimed.
    internal static void Unprotect(Player player) {
        protectedUntil.Remove(player.channel.owner.playerID.steamID.m_SteamID);
    }

    private static void Fired(UseableGun gun, BulletInfo bullet) => Unprotect(gun.player);
    private static void Launched(UseableGun gun, GameObject projectile) => Unprotect(gun.player);
    private static void Aimed(UseableGun gun) => Unprotect(gun.player);
    private static void Threw(UseableThrowable useable, GameObject thrown) => Unprotect(useable.player);

    /// Say why the hit did nothing.
    ///
    /// A refusal here is completely silent in game: the shooter gets their client-side
    /// hitmarker and the victim loses nothing, which is indistinguishable from the
    /// server not registering the shot. That is the same silence that had South
    /// American players reporting broken hit detection, and the lobby already learned
    /// this lesson. A rule nobody is told about is a bug as far as the player is
    /// concerned.
    private static void ExplainRefusal(CSteamID shooter, Player victim, string key) {
        if (shooter == CSteamID.Nil || shooter == victim.channel.owner.playerID.steamID) return;

        // Resolved before the throttle is stamped, so a shooter who has already left
        // does not spend somebody's three seconds on a line nobody can be sent.
        Player player = PlayerTool.getPlayer(shooter);
        if (player == null) return;

        float now = Time.realtimeSinceStartup;
        if (lastProtectedNotice.TryGetValue(shooter.m_SteamID, out float last)
                && now - last < ProtectedNoticeIntervalSeconds) return;
        lastProtectedNotice[shooter.m_SteamID] = now;

        Host.Whisper(player.channel.owner, PeekSeekRaid.Instance.Translate(
            key, victim.channel.owner.playerID.characterName));
    }

    /// A rocket is 90 on a direct hit and at most 60 otherwise; a Keyhole grenade
    /// never hurts anybody; the Suppressor's bullets are cut to its own number.
    private static void OnDamagePlayer(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        if (raid == null) return;

        ulong hurt = parameters.player.channel.owner.playerID.steamID.m_SteamID;
        if (protectedUntil.TryGetValue(hurt, out float until) && Time.realtimeSinceStartup < until) {
            shouldAllow = false;
            ExplainRefusal(parameters.killer, parameters.player, "target_protected");
            return;
        }

        if (Current != null) {
            if (Current.Tool == PeekSeekRaid.Config.KeyholeItemId) {
                shouldAllow = false;
                ExplainRefusal(Current.Killer, parameters.player, "keyhole_harmless");
                return;
            }

            if (Current.Tool == PeekSeekRaid.Config.LauncherItemId) {
                // Measured to the nearer of feet and chest, so a rocket into somebody's
                // middle and one at their boots both count as direct.
                Vector3 feet = parameters.player.transform.position;
                float gap = Mathf.Min((feet - Current.Point).magnitude, (feet + Vector3.up - Current.Point).magnitude);

                // No radius check here on purpose. RaidWorld puts RocketBlastRadius on
                // the launcher, so the game has already decided who is in reach and
                // nobody outside it gets this far. Refusing the hit a second time here
                // would only risk refusing one on the boundary that the game allowed,
                // and a refusal this late is silent.
                //
                // What follows overwrites `damage` outright, so a rocket is the same 90 or 60
                // whoever fired it: the match plugin's ping handicap ran a moment ago
                // and is discarded here, along with the limb and the armour. That is
                // deliberate for a breaching tool, whose job is to open a wall for a
                // known number of charges rather than to reward aim, but it does mean a
                // high-ping attacker is handicapped on their bullets and not on their
                // rockets. If that ever needs to change, scale these two numbers by
                // PSManager.PingPercentage here rather than relying on the handicap
                // upstream, which cannot survive being overwritten.
                bool onTarget = gap <= PeekSeekRaid.Config.RocketDirectRadius;
                parameters.damage = onTarget
                    ? PeekSeekRaid.Config.RocketDirectDamage
                    : Mathf.Min(parameters.damage * parameters.times, PeekSeekRaid.Config.RocketSplashDamage);
                parameters.times = 1f;
                parameters.respectArmor = false;
            }
            return;
        }

        if (parameters.cause != EDeathCause.GUN) return;

        Player shooter = PlayerTool.getPlayer(parameters.killer);
        if (shooter == null || shooter.equipment.itemID != PeekSeekRaid.Config.SuppressorGunId) return;
        if (shooter.equipment.asset is not ItemGunAsset gun || gun.playerDamageMultiplier.damage <= 0f) return;

        parameters.damage *= PeekSeekRaid.Config.SuppressorPlayerDamage / gun.playerDamageMultiplier.damage;
    }
}

/// What a thrown object was and who threw it, stuck on it the moment the server spawns
/// it (UseableThrowable.onThrowableSpawned), because the grenade the game makes of it
/// keeps neither.
internal class Thrown : MonoBehaviour {
    internal ushort ItemId;
    internal CSteamID Thrower;
}
