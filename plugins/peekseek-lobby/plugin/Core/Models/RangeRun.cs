namespace PeekSeekLobby.Core.Models;

/// One player's run at the target range: when it started and how many targets are
/// still standing. Deliberately knows nothing about Unturned, putting mannequins
/// in the world and knocking them down needs a live server, but the clock, the
/// layout and what counts as a personal best are the parts worth reading and
/// testing on their own.
internal class RangeRun {
    private readonly float startedAt;
    private int standing;

    internal RangeRun(float now, int targets) {
        startedAt = now;
        standing = targets;
    }

    internal int Standing => standing;
    internal bool IsComplete => standing <= 0;
    internal float Elapsed(float now) => now - startedAt;

    /// Takes one target down and returns how many are left. Floors at zero rather
    /// than going negative: two bullets landing on the last mannequin in the same
    /// frame both reach here, and a negative count would leave IsComplete true but
    /// the finish already fired.
    internal int Knock() {
        if (standing > 0) standing--;
        return standing;
    }

    /// Where each target goes, as yaw offsets from the direction the player is
    /// facing, evenly spread and centred on straight ahead.
    ///
    /// Angles rather than positions so this stays free of UnityEngine: the test
    /// host cannot load Vector3 at all, and the arithmetic that decides whether
    /// the row is even and centred is exactly the part worth checking.
    internal static float[] TargetAngles(int count, float spreadDegrees) {
        if (count <= 0) return [];
        if (count == 1) return [0f];

        float step = spreadDegrees / (count - 1);
        float[] angles = new float[count];
        for (int i = 0; i < count; i++) {
            angles[i] = -spreadDegrees / 2f + (i * step);
        }
        return angles;
    }

    /// Whether a finishing time is a new personal best. A best of zero means there
    /// is no best yet, so a player's first clear always sets one.
    internal static bool BeatsBest(float elapsed, float best) => best <= 0f || elapsed < best;
}
