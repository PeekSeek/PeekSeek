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
}
