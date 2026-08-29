using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeekArmorSkin.Tests;

public class ConfigurationRoundTripTests {
    [Fact]
    public void Defaults_SurviveAnXmlRoundTrip() {
        var config = new PeekSeekArmorSkinConfiguration();
        config.LoadDefaults();

        var serializer = new XmlSerializer(typeof(PeekSeekArmorSkinConfiguration));
        using var buffer = new StringWriter();
        serializer.Serialize(buffer, config);
        using var reader = new StringReader(buffer.ToString());
        var loaded = (PeekSeekArmorSkinConfiguration)serializer.Deserialize(reader);

        Assert.Equal(config.SkinTonePresets, loaded.SkinTonePresets);
        Assert.Equal(config.AssignSkinTones, loaded.AssignSkinTones);
        Assert.Equal(config.EqualizeArmor, loaded.EqualizeArmor);
        Assert.Equal(config.ArmsArmor, loaded.ArmsArmor);
        Assert.Equal(config.LegsArmor, loaded.LegsArmor);
        Assert.Equal(config.SpineArmor, loaded.SpineArmor);
        Assert.Equal(config.SkullArmor, loaded.SkullArmor);
    }
}
