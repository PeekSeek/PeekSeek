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

    /// Unturned heals players passively once every Health_Regen_Ticks ticks — there is
    /// no "off" switch, so this pushes the interval out past anything a match could ever
    /// last. Healing should only ever come from a med kit pick, never from standing still.
    public static void DisablePassiveHealthRegen() {
        Provider.modeConfigData.Players.Health_Regen_Ticks = uint.MaxValue;
    }
}
