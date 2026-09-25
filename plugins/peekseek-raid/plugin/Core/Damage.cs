using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
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

    private static Raid raid;

    internal static void Hook(Raid running) {
        raid = running;
        cuts.Clear();
        DamageTool.damagePlayerRequested += OnDamagePlayer;
    }

    internal static void Unhook() {
        DamageTool.damagePlayerRequested -= OnDamagePlayer;
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
        private static void Prefix(InteractableCharge __instance) {
            nextTool = BarricadeManager.FindBarricadeByRootTransform(__instance.transform)?.asset?.id ?? 0;
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

    /// A rocket is 90 on a direct hit and at most 60 otherwise; a Keyhole grenade
    /// never hurts anybody; the Suppressor's bullets are cut to its own number.
    private static void OnDamagePlayer(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        if (raid == null) return;

        if (Current != null) {
            if (Current.Tool == PeekSeekRaid.Config.KeyholeItemId) {
                shouldAllow = false;
                return;
            }

            if (Current.Tool == PeekSeekRaid.Config.LauncherItemId) {
                // Measured to the nearer of feet and chest, so a rocket into somebody's
                // middle and one at their boots both count as direct.
                Vector3 feet = parameters.player.transform.position;
                float gap = Mathf.Min((feet - Current.Point).magnitude, (feet + Vector3.up - Current.Point).magnitude);
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
