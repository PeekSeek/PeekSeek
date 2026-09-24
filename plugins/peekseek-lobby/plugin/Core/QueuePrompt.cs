using System.Collections.Generic;
using PeekSeek.Hud;
using SDG.Unturned;

namespace PeekSeekLobby.Core;

/// The question the lobby asks a player who has been here a while: queue for a
/// match, or stay in the arena. Two tiles on the boards' own panel, with a line under
/// the headline that says what queueing from here means, and what it costs at their
/// ping. Answered by a click; `BoardChoice0` is yes and `BoardChoice1` is no.
internal class QueuePrompt {
    private const string Button = "BoardChoice";

    private readonly HashSet<ulong> up = [];
    private EffectAsset effect;
    private bool effectLookedUp;

    internal bool IsUp(ulong steam64) => up.Contains(steam64);

    internal void Open(SteamPlayer viewer, string line) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        short key = PeekSeekLobby.Config.ChoiceKey;
        up.Add(viewer.playerID.steamID.m_SteamID);
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        Send(viewer, "Choice_Headline", PeekSeekLobby.Instance.Translate("prompt_headline"));
        Send(viewer, "Choice_Line", line);
        Send(viewer, "Choice0_Name", PeekSeekLobby.Instance.Translate("prompt_yes"));
        Send(viewer, "Choice0_Blurb", PeekSeekLobby.Instance.Translate("prompt_yes_blurb"));
        Send(viewer, "Choice1_Name", PeekSeekLobby.Instance.Translate("prompt_no"));
        Send(viewer, "Choice1_Blurb", PeekSeekLobby.Instance.Translate("prompt_no_blurb"));

        // A tile cannot be clicked without a cursor, and the game only shows one while
        // a plugin says it is modal.
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, true);
    }

    internal void Close(SteamPlayer viewer) {
        if (!up.Remove(viewer.playerID.steamID.m_SteamID)) return;

        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, false);
    }

    internal void Forget(ulong steam64) => up.Remove(steam64);

    /// True for yes, false for no, null for a click that was not on this board.
    internal bool? Answer(SteamPlayer viewer, string button) {
        if (button == null || !button.StartsWith(Button) || !up.Contains(viewer.playerID.steamID.m_SteamID)) return null;
        if (!int.TryParse(button.Substring(Button.Length), out int index)) return null;

        Close(viewer);
        return index == 0;
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeekLobby.Config.ChoiceKey, viewer.transportConnection, true, child, text ?? "");
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(PeekSeekLobby.Config.ChoiceEffectGUID, "ChoiceEffectGUID", "Nobody is asked to queue.");
        return effect;
    }
}
