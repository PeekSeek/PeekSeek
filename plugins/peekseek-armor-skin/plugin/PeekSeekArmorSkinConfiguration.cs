using System.Collections.Generic;
using System.Xml.Serialization;
using Rocket.API;

namespace PeekSeekArmorSkin;

[XmlRoot("Config")]
public class PeekSeekArmorSkinConfiguration : IRocketPluginConfiguration {
    public bool AssignSkinTones;
    public bool EqualizeArmor;
    public bool StripCosmetics;

    public List<int> SkinTonePresets;

    public float ArmsArmor;
    public float LegsArmor;
    public float SpineArmor;
    public float SkullArmor;
    public float ExplosionArmor;

    public void LoadDefaults() {
        AssignSkinTones = true;
        EqualizeArmor = true;
        StripCosmetics = true;
        SkinTonePresets = [0, 2, 4, 7];
        ArmsArmor = 0.9f;
        LegsArmor = 0.9f;
        SpineArmor = 0.63f;
        SkullArmor = 0.85f;
        ExplosionArmor = 0.8375f;
    }
}
