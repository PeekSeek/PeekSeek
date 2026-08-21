using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PeekSeek.Core;

/// The crash dumps Unturned leaves in the Rocket folder, and getting rid of them.
///
/// One dump is roughly 1.8 GB against a 30 GB disk, so a handful of them is most of the
/// disk. That was survivable while a server went down once a day. It is not survivable
/// now that finishing a match takes one down: a server that dumps on even a small share
/// of its exits fills its disk in days, and a full disk is a server that cannot write
/// its logs, its config, or its save. EU3 was carrying 12.5 GB of them.
///
/// Swept on load rather than on shutdown because the dump is written *as* the process
/// dies. The next start is the only moment anything is around to clear it.
internal static class CoreDumps {
    /// `core.` and then the pid that produced it, which is the whole of the name Linux
    /// gives them. Anchored at both ends so nothing else in the folder can match.
    private static readonly Regex Named = new Regex(@"^core\.\d+$", RegexOptions.Compiled);

    /// What a sweep got rid of, for the caller to report. Reporting is deliberately not
    /// done here: this runs during plugin load and in tests, and Rocket's logger only
    /// exists in one of those.
    internal readonly struct Swept {
        internal readonly int Cleared;
        internal readonly long Freed;

        internal Swept(int cleared, long freed) {
            Cleared = cleared;
            Freed = freed;
        }

        internal long FreedMegabytes => Freed / 1024 / 1024;
    }

    /// Clears every dump sitting next to Rocket's own files.
    ///
    /// `pluginDirectory` is where this plugin keeps its config, which is somewhere under
    /// the Rocket folder but not at a fixed depth: the match plugin lives in
    /// `Rocket/Plugins/PeekSeek/` and the lobby's DLL sits directly in `Rocket/Plugins/`.
    /// Walking up to the folder actually called `Rocket` is what makes this survive
    /// either layout, and do nothing at all rather than guess if it is neither.
    internal static Swept Sweep(string pluginDirectory) {
        string rocket = RocketFolderAbove(pluginDirectory);
        if (rocket == null) return new Swept(0, 0);

        long freed = 0;
        int cleared = 0;
        foreach (string path in Directory.GetFiles(rocket)) {
            if (!Named.IsMatch(Path.GetFileName(path))) continue;

            try {
                long size = new FileInfo(path).Length;
                File.Delete(path);
                freed += size;
                cleared++;
            } catch (Exception) {
                // Never worth failing a load over. A dump that will not delete costs
                // disk; a plugin that will not start costs the whole server.
            }
        }

        return new Swept(cleared, freed);
    }

    private static string RocketFolderAbove(string directory) {
        try {
            for (DirectoryInfo at = new DirectoryInfo(directory); at != null; at = at.Parent) {
                if (at.Name.Equals("Rocket", StringComparison.OrdinalIgnoreCase)) return at.FullName;
            }
        } catch (Exception) {
            // An unreadable path is the same answer as no Rocket folder: do nothing.
        }
        return null;
    }
}
