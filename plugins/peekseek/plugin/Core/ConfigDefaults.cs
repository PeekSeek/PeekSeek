using System.Collections.Generic;

namespace PeekSeek.Core;

/// What a live configuration.xml lacks because it was written before the field
/// existed.
///
/// Rocket does not merge new fields into an existing file: XmlSerializer simply leaves
/// what it finds no element for, so a field added in a new version reads as null, 0 or
/// false on every server that has been running for a while, and never as its default.
/// That is how a screen ships dark on the live servers and looks, from in game, exactly
/// like a plugin that does nothing, until somebody remembers to hand-edit XML on every
/// box.
///
/// So a plugin fills its own gaps on load and says which it filled. The written file is
/// still the truth for anything it does say: a value only comes from here when the file
/// has nothing to say at all, which for these fields cannot be told apart from being
/// absent, and none of them has a meaningful zero.
///
/// Nothing here writes to the log, because the log is Unturned's and cannot be loaded
/// by a test host. The caller says what came back.
public class ConfigDefaults {
    private readonly List<string> filled = [];

    /// The fields that had nothing in the file, in the order they were asked for.
    public IReadOnlyList<string> Filled => filled;

    public string Or(string value, string fallback, string field) {
        if (!string.IsNullOrWhiteSpace(value)) return value;

        filled.Add(field);
        return fallback;
    }

    public short Or(short value, short fallback, string field) {
        if (value != 0) return value;

        filled.Add(field);
        return fallback;
    }

    public int Or(int value, int fallback, string field) {
        if (value > 0) return value;

        filled.Add(field);
        return fallback;
    }

    public float Or(float value, float fallback, string field) {
        if (value > 0f) return value;

        filled.Add(field);
        return fallback;
    }

    /// What to say about a file that was missing fields, or null for one that was
    /// not. The caller logs it: the log is Unturned's, and this has to stay loadable
    /// by a test host.
    public static string Sentence(IReadOnlyList<string> filled, string file) {
        if (filled.Count == 0) return null;

        return $"{file} predates {filled.Count} field(s), so their defaults are in use: "
            + $"{string.Join(", ", filled)}. Write them into the file to set your own.";
    }
}
