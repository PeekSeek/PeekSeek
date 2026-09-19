using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekSeekSiege.Core.Models;

/// What a structure is made of and what it is apart from that, read off its name.
///
/// Unturned names every housing piece the same way, "Birch Wall", "Triangular Metal
/// Floor", "Brick Ramp Wall", with the material as one word somewhere in it, and the
/// pieces of one kind in the five materials are the same shape on the housing grid.
/// That is what makes a piece swappable in place for the same kind in another
/// material, and the name is the only place the game states the family.
public static class Materials {
    public static readonly string[] Names = ["Birch", "Pine", "Maple", "Metal", "Brick"];

    /// ("Metal", "Triangular Floor") for "Triangular Metal Floor"; null for anything
    /// that is not a housing piece in one of the five materials.
    public static (string Material, string Kind)? Split(string itemName) {
        if (string.IsNullOrWhiteSpace(itemName)) return null;

        List<string> words = itemName.Split([' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        int at = words.FindIndex(word => Names.Contains(word));
        if (at < 0) return null;

        string material = words[at];
        words.RemoveAt(at);
        return (material, string.Join(" ", words));
    }

    /// Whether two names are the same kind of piece in any materials.
    public static bool SameKind(string a, string b) {
        return Split(a)?.Kind == Split(b)?.Kind;
    }
}
