using SDG.Unturned;

namespace PeekSeek.Core;

public class ConfigurationManager {
    public static void SetBulletdrop(bool @bool) {
        Provider.modeConfigData.Gameplay.Ballistics = @bool;
    }

    public static void SetOldMovement(bool @bool) {
        if (!@bool) {
            Provider.modeConfigData.Gameplay.AirStrafing_Acceleration_Multiplier = 1f;
            Provider.modeConfigData.Gameplay.AirStrafing_Deceleration_Multiplier = 1f;
        } else {
            Provider.modeConfigData.Gameplay.AirStrafing_Acceleration_Multiplier = 20f;
            Provider.modeConfigData.Gameplay.AirStrafing_Deceleration_Multiplier = 20f;
        }
    }

    /// Clothing in Unturned wears out as it absorbs hits: DamageTool.getPlayerArmor
    /// takes a point of quality off the piece it just read and tells every client, so
    /// a vest is worth less at the end of a round than at the start. A match hands out
    /// a fresh kit anyway, and PeekSeekArmorSkin exists so that a limb is worth the
    /// same number every time, which degrading armour quietly undoes.
    ///
    /// Turning it off also makes getPlayerArmor a plain read with nothing behind it,
    /// which is what lets PSManager.TotalMultiplier ask what a hit is about to be
    /// multiplied by without charging the victim a point of armour for the question.
    /// ShouldClothingTakeDamage has no setter; it is Has_Durability and
    /// Clothing_Has_Durability together, so this is the half of it that is only about
    /// clothing.
    public static void DisableClothingWear() {
        Provider.modeConfigData.Items.Clothing_Has_Durability = false;
    }

    /// Unturned heals players passively once every Health_Regen_Ticks ticks, there is
    /// no "off" switch, so this pushes the interval out past anything a match could ever
    /// last. Healing should only ever come from a med kit pick, never from standing still.
    public static void DisablePassiveHealthRegen() {
        Provider.modeConfigData.Players.Health_Regen_Ticks = uint.MaxValue;
    }
}
