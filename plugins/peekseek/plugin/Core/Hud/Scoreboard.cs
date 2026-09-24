using System.Collections.Generic;
using System.Linq;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Hud;

public class ScoreboardSettings {
    public string EffectGUID;
    public short EffectKey;
}

/// One line of the board: a player, or a heading over the players under it.
public class ScoreRow {
    public string Heading;

    public ulong Steam64;
    public string AvatarUrl;
    public string Name;

    /// A word after the name, MVP, or null.
    public string Tag;

    /// The four numbers, as text, in the columns' order.
    public string[] Values = ["", "", "", ""];

    public static ScoreRow Head(string text) => new() { Heading = text };
}

/// Everything the board says: a title, one line under it, the four column names and
/// the rows.
public class ScoreSheet {
    public string Title;
    public string Sub;
    public string[] Columns = ["K", "D", "HS%", "DMG"];
    public List<ScoreRow> Rows = [];
}

/// The scoreboard: a page of the site, up while a player holds the plugin key and up
/// for everybody, unasked, when a match ends or the lobby's arena resets. One prefab
/// draws both a two-team match and a lobby's single list, because a row can be a
/// heading. The viewer's own row is filled, so nothing on the board has to say "you".
public class Scoreboard {
    private const int Rows = 12;

    private readonly ScoreboardSettings settings;
    private EffectAsset effect;
    private readonly HashSet<ulong> up = [];

    public Scoreboard(ScoreboardSettings settings) {
        this.settings = settings;
    }

    public bool IsUp(ulong steam64) => up.Contains(steam64);

    public void Show(SteamPlayer viewer, ScoreSheet sheet) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        ulong me = viewer.playerID.steamID.m_SteamID;
        up.Add(me);
        short key = settings.EffectKey;
        ITransportConnection to = viewer.transportConnection;

        EffectManager.SendUIEffect(asset, key, to, true);
        EffectManager.sendUIEffectText(key, to, true, "Board_Title", sheet.Title ?? "");
        EffectManager.sendUIEffectText(key, to, true, "Board_Sub", sheet.Sub ?? "");
        string[] columns = ["A", "B", "C", "D"];
        for (int c = 0; c < columns.Length; c++) {
            EffectManager.sendUIEffectText(key, to, true, $"Head_{columns[c]}", sheet.Columns.ElementAtOrDefault(c) ?? "");
        }

        for (int i = 0; i < Rows; i++) {
            ScoreRow row = sheet.Rows.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, to, true, $"Row{i}", row != null);
            if (row == null) continue;

            bool heading = row.Heading != null;
            EffectManager.sendUIEffectVisibility(key, to, true, $"Row{i}_Head", heading);
            EffectManager.sendUIEffectVisibility(key, to, true, $"Row{i}_Body", !heading);
            EffectManager.sendUIEffectVisibility(key, to, true, $"Row{i}_Me", !heading && row.Steam64 == me);
            if (heading) {
                EffectManager.sendUIEffectText(key, to, true, $"Row{i}_Head", row.Heading);
                continue;
            }

            EffectManager.sendUIEffectImageURL(key, to, true, $"Row{i}_Avatar", row.AvatarUrl ?? "");
            EffectManager.sendUIEffectText(key, to, true, $"Row{i}_Name", row.Name ?? "");
            EffectManager.sendUIEffectVisibility(key, to, true, $"Row{i}_Tag", !string.IsNullOrEmpty(row.Tag));
            if (!string.IsNullOrEmpty(row.Tag)) EffectManager.sendUIEffectText(key, to, true, $"Row{i}_Tag", row.Tag);
            for (int c = 0; c < columns.Length; c++) {
                EffectManager.sendUIEffectText(key, to, true, $"Row{i}_{columns[c]}", row.Values.ElementAtOrDefault(c) ?? "");
            }
        }
    }

    public void ShowAll(ScoreSheet sheet) {
        foreach (SteamPlayer viewer in Provider.clients.ToList()) Show(viewer, sheet);
    }

    public void Hide(SteamPlayer viewer) {
        if (!up.Remove(viewer.playerID.steamID.m_SteamID)) return;

        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
    }

    public void HideAll() {
        foreach (ulong steam64 in up.ToList()) {
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(steam64));
            if (viewer != null) Hide(viewer);
        }
        up.Clear();
    }

    public void Forget(ulong steam64) => up.Remove(steam64);

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(settings.EffectGUID, "ScoreboardEffectGUID", "No scoreboard.");
        return effect;
    }
}
