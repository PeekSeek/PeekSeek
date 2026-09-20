using System;
using System.Collections.Generic;
using System.Linq;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeek.Gamemodes;

/// The gamemodes installed on this server, by the site's name for each.
///
/// A gamemode plugin registers here when it loads and unregisters when it unloads.
/// Rocket loads plugins in whatever order it finds them, so this is a plain static
/// list that needs nothing from Match to exist: registering before Match has loaded
/// is fine, and so is the other way round.
public static class Gamemodes {
    private static readonly List<IGamemode> registered = [];
    private static IGamemode fallback;

    /// Register a gamemode. The default is the one that takes every match whose
    /// gamemode nothing else is named for, which on a PeekSeek server is the arena.
    public static void Register(IGamemode mode, bool isDefault = false) {
        registered.Add(mode);
        if (isDefault) fallback = mode;
        Logger.Log($"Gamemode {mode.Name} registered{(isDefault ? " as the default" : "")}.");
    }

    public static void Unregister(IGamemode mode) {
        registered.Remove(mode);
        if (fallback == mode) fallback = null;
    }

    /// The gamemode that runs a match the site calls `name`, or null when nothing
    /// installed can.
    internal static IGamemode For(string name) {
        return registered.FirstOrDefault(mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    internal static IEnumerable<string> Names => registered.Select(mode => mode.Name);
}
