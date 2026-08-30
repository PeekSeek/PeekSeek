using HarmonyLib;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekArmorSkin;

public class PeekSeekArmorSkin : RocketPlugin<PeekSeekArmorSkinConfiguration> {
    public static PeekSeekArmorSkin Instance { get; private set; }

    public static PeekSeekArmorSkinConfiguration Config { get => Instance.Configuration.Instance; }

    private const string HarmonyId = "peekseek.armorskin";
    private static Harmony Instrument;

    protected override void Load() {
        Instance = this;

        Instrument = new Harmony(HarmonyId);
        Instrument.PatchAll(GetType().Assembly);

        foreach (var patched in Instrument.GetPatchedMethods())
            Logger.Log($"PeekSeekArmorSkin patched {patched.DeclaringType?.Name}.{patched.Name}");

        Logger.Log($"PeekSeekArmorSkin {Assembly.GetName().Version} is now loaded. "
            + $"skin={Config.AssignSkinTones} armor={Config.EqualizeArmor} cosmetics={Config.StripCosmetics}");
    }

    protected override void Unload() {
        Instrument.UnpatchAll(HarmonyId);
        Instance = null;

        Logger.Log("PeekSeekArmorSkin is now unloaded.");
    }
}
