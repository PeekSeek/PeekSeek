using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Core.Patches;

/// Every hit a player landed on another player, counted where it has already happened.
///
/// The obvious place to count is DamageTool.damagePlayerRequested, and that is where
/// this used to be. It is the wrong place twice over. The event is a veto: every
/// subscriber gets the same `shouldAllow` and any one of them can still refuse the hit
/// afterwards, and PeekSeek has subscribers that do (RAID's spawn protection, its
/// Keyhole grenade). So a refused hit was still counted as dealt, as taken and as a
/// head shot, and the round log held damage nobody ever took. The event also fires
/// before armour, before the global multiplier and before the FloorToInt that can throw
/// the hit away entirely, so the number recorded was an estimate of a hit that might
/// not happen rather than the hit that did.
///
/// PlayerLife.askDamage is not far enough either, which is worth writing down because
/// it looks like it is. The overload damagePlayer calls only forwards; the one it
/// forwards to opens with `if (!bypassSafezone &amp;&amp; !InternalCanDamage()) return;`
/// and damagePlayer passes bypassSafezone false. Counting there still counts every hit
/// on somebody in godmode or inside a safezone and lets the game throw it away
/// afterwards: the same defect as the event, one frame deeper.
///
/// doDamage is past all of it. Every refusal that can still happen has happened, and
/// `amount` is the byte of health about to come off. It is private and has one
/// overload, hence the name as a string and no argument types.
///
/// Two checks of its own are left in doDamage, both ahead of us: an already-dead victim
/// and a victim who is not alive. Neither is reachable from a shot, because damagePlayer
/// returns at its first line for a dead player and nothing between there and here can
/// kill them. That is the whole residue, and it is written here rather than claimed
/// away.
///
/// doDamage has two callers besides askDamage, and the filters below are what keep them
/// out rather than an accident:
///   - ReceiveSuicideRequest passes the player's *own* id as the killer, so /suicide
///     would otherwise book 100 damage dealt and a hit against the person who typed it.
///     That is what the self check is for.
///   - SimulateOxygenFrame passes Provider.server, which is not a player, so the
///     "resolves to a player" check drops it. Drowning, bleeding, falls and starving
///     all arrive carrying nobody, which is the same filter: a hit with no player
///     behind it is not somebody's damage.
[HarmonyPatch(typeof(PlayerLife), "doDamage")]
[UsedImplicitly]
internal class DamageStatsPatch {
    [UsedImplicitly]
    private static void Prefix(PlayerLife __instance, byte amount, ELimb newLimb, CSteamID newKiller) {
        if (amount == 0 || newKiller == CSteamID.Nil) return;

        CSteamID victimID = __instance.player.channel.owner.playerID.steamID;
        if (newKiller == victimID) return;
        if (PlayerTool.getPlayer(newKiller) == null) return;

        StatService.AddDamageTaken(victimID, amount);
        StatService.AddDamageDealt(newKiller, amount);
        StatService.RecordHit(newKiller, newLimb == ELimb.SKULL);
        RoundLog.RecordDamage(newKiller, victimID, amount);
    }
}
