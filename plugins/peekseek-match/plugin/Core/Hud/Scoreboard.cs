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

    /// The rank badge on the face's corner, or null for a board with no ranks to show
    /// and for a player the site has not answered a rank for yet.
    public string RankIconUrl;

    /// A word after the name, MVP, or null.
    public string Tag;

    /// The four numbers, as text, in the columns' order.
    public string[] Values = ["", "", "", ""];

    /// Milliseconds to this server, or null for a player who is not here. Drawn after
    /// the columns as "23 ms", with no column name over it.
    public int? Ping;

    public static ScoreRow Head(string text) => new() { Heading = text };
}

/// Everything the board says: what it is, the line under that, a clock when one
/// applies, the four column names and the rows.
public class ScoreSheet {
    public string Title;
    public string Sub;

    /// m:ss, or null for a board with no clock.
    public string Timer;

    public string[] Columns = ["K", "D", "HS%", "DMG"];
    public List<ScoreRow> Rows = [];
}

/// The scoreboard: a page of the site, up while a player holds the plugin key and up
/// for everybody, unasked, when a match ends or the lobby's arena resets. One prefab
/// draws both a two-team match and a lobby's single list, because a row can be a
/// heading. The viewer's own row is filled, so nothing on the board has to say "you".
///
/// A board that is up is redrawn by `Refresh` as the numbers move. Each viewer's last
/// sent value for every child is kept, so a redraw only sends what actually changed:
/// a whole board is a few hundred messages, and a clock ticking is one.
public class Scoreboard {
    private const int Rows = 12;
    private static readonly string[] Columns = ["A", "B", "C", "D"];

    private readonly ScoreboardSettings settings;
    private EffectAsset effect;
    private readonly Dictionary<ulong, Dictionary<string, string>> sent = [];

    public Scoreboard(ScoreboardSettings settings) {
        this.settings = settings;
    }

    public bool IsUp(ulong steam64) => sent.ContainsKey(steam64);

    public bool AnyUp => sent.Count > 0;

    public void Show(SteamPlayer viewer, ScoreSheet sheet) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        ulong me = viewer.playerID.steamID.m_SteamID;
        sent[me] = [];
        EffectManager.SendUIEffect(asset, settings.EffectKey, viewer.transportConnection, true);
        Fill(viewer, sheet);
    }

    public void ShowAll(ScoreSheet sheet) {
        foreach (SteamPlayer viewer in Provider.clients.ToList()) Show(viewer, sheet);
    }

    public void Refresh(ScoreSheet sheet) {
        foreach (ulong steam64 in sent.Keys.ToList()) {
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(steam64));
            if (viewer == null) {
                sent.Remove(steam64);
                continue;
            }
            Fill(viewer, sheet);
        }
    }

    public void Hide(SteamPlayer viewer) {
        if (!sent.Remove(viewer.playerID.steamID.m_SteamID)) return;

        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
    }

    public void HideAll() {
        foreach (ulong steam64 in sent.Keys.ToList()) {
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(steam64));
            if (viewer != null) Hide(viewer);
        }
        sent.Clear();
    }

    public void Forget(ulong steam64) => sent.Remove(steam64);

    private void Fill(SteamPlayer viewer, ScoreSheet sheet) {
        ulong me = viewer.playerID.steamID.m_SteamID;
        Dictionary<string, string> seen = sent[me];
        short key = settings.EffectKey;
        ITransportConnection to = viewer.transportConnection;

        void Text(string child, string value) {
            value ??= "";
            if (seen.TryGetValue(child, out string was) && was == value) return;
            seen[child] = value;
            EffectManager.sendUIEffectText(key, to, true, child, value);
        }

        void Visible(string child, bool visible) {
            string value = visible ? "on" : "off";
            if (seen.TryGetValue("visible:" + child, out string was) && was == value) return;
            seen["visible:" + child] = value;
            EffectManager.sendUIEffectVisibility(key, to, true, child, visible);
        }

        void Picture(string child, string url) {
            url ??= "";
            if (seen.TryGetValue("picture:" + child, out string was) && was == url) return;
            seen["picture:" + child] = url;
            EffectManager.sendUIEffectImageURL(key, to, true, child, url);
        }

        Text("Board_Title", sheet.Title);
        Visible("Board_Sub", !string.IsNullOrEmpty(sheet.Sub));
        if (!string.IsNullOrEmpty(sheet.Sub)) Text("Board_Sub", sheet.Sub);
        Visible("Board_Timer", sheet.Timer != null);
        if (sheet.Timer != null) Text("Board_Timer", sheet.Timer);

        for (int c = 0; c < Columns.Length; c++) {
            Text($"Head_{Columns[c]}", sheet.Columns.ElementAtOrDefault(c));
        }

        for (int i = 0; i < Rows; i++) {
            ScoreRow row = sheet.Rows.ElementAtOrDefault(i);
            Visible($"Row{i}", row != null);
            if (row == null) continue;

            bool heading = row.Heading != null;
            Visible($"Row{i}_Head", heading);
            Visible($"Row{i}_Body", !heading);
            Visible($"Row{i}_Me", !heading && row.Steam64 == me);
            if (heading) {
                Text($"Row{i}_Head", row.Heading);
                continue;
            }

            Picture($"Row{i}_Avatar", row.AvatarUrl);
            Text($"Row{i}_Name", row.Name);
            Visible($"Row{i}_Rank", row.RankIconUrl != null);
            if (row.RankIconUrl != null) Picture($"Row{i}_Rank_Icon", row.RankIconUrl);
            Visible($"Row{i}_Tag", !string.IsNullOrEmpty(row.Tag));
            if (!string.IsNullOrEmpty(row.Tag)) Text($"Row{i}_Tag", row.Tag);
            for (int c = 0; c < Columns.Length; c++) {
                Text($"Row{i}_{Columns[c]}", row.Values.ElementAtOrDefault(c));
            }
            Text($"Row{i}_Ping", row.Ping is int ms ? $"{ms} ms" : "");
        }
    }

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(settings.EffectGUID, "ScoreboardEffectGUID", "No scoreboard.");
        return effect;
    }
}
