using System.Reflection;
using HarmonyLib;
using PeekSeek.Gamemodes;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using SDG.Unturned;

namespace PeekSeekArena;

/// PeekSeek ARENA: the gamemode every ranked PeekSeek match has been so far. Unturned's
/// own arena round, last team standing inside a shrinking circle, driven by the match
/// server's roster rather than by whoever walked in.
///
/// Registered as the default gamemode, so every match the site hands over runs on
/// this unless another gamemode plugin is named for it.
public class PeekSeekArena : RocketPlugin<PeekSeekArenaConfiguration> {
    public static PeekSeekArena Instance { get; private set; }

    public static PeekSeekArenaConfiguration Config { get => Instance.Configuration.Instance; }

    internal static Arena Arena { get; private set; }

    private Harmony instrument;

    /// Rocket loads every DLL under Plugins/, subfolders included, so a stale copy left
    /// beside the current one comes up as a second plugin sharing this one's statics.
    /// Whoever loads first owns the server.
    protected override void Load() {
        if (Instance != null) {
            Logger.LogWarning("PeekSeekArena is already loaded; ignoring this copy. Look for a leftover PeekSeekArena.dll under Rocket/Plugins.");
            return;
        }

        Instance = this;
        Arena = new Arena();
        instrument = new Harmony("blaze.ps.arena");
        instrument.PatchAll(Assembly.GetExecutingAssembly());
        PlayerLife.onPlayerLifeUpdated += OnLifeUpdated;

        Gamemodes.Register(Arena, isDefault: true);
        Logger.Log($"PeekSeekArena {Assembly.GetName().Version} is now loaded.");
    }

    protected override void Unload() {
        if (Instance != this) return;

        Gamemodes.Unregister(Arena);
        PlayerLife.onPlayerLifeUpdated -= OnLifeUpdated;
        instrument?.UnpatchAll(instrument.Id);
        Arena = null;
        Instance = null;
    }

    /// Safety net: if a death is ever resolved without the death event firing, this
    /// still puts them back, so nobody sits on a death screen in a mode where the
    /// dead respawn straight away.
    private static void OnLifeUpdated(Player player) {
        if (Arena is { InPlay: true } && player.life.isDead) player.life.ServerRespawn(false);
    }

    public override TranslationList DefaultTranslations => new TranslationList() {
        // Said as the round spawns everyone in, so the first thing a player sees on
        // landing is which round this is and where the match stands.
        { "round_start", "<color=white>Round {0}.</color> {1}" }
    };
}
