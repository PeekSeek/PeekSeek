namespace PeekSeekRaid.Core.Models;

/// The six kinds of powerup, three a side. Every powerup is exactly one; the class is
/// the glyph in the tile's corner, which says what a powerup is for at a glance.
public enum PowerupClass {
    /// Attack: breaks the base.
    Destruction,

    /// Attack: gets the team in high.
    Initializer,

    /// Attack: suppression, a way in, a second life.
    Support,

    /// Defence: a trick for one situation.
    Special,

    /// Defence: makes the base itself harder to break.
    Armorer,

    /// Defence: heals, sentries and traps.
    Warden
}

/// What a powerup does that its items alone do not, where the plugin has to know who
/// picked it rather than what they are holding. Everything an item does wherever it
/// goes (a Keyhole grenade, a chainsaw, a bedroll) is keyed on the item instead.
public enum Twist {
    None,
    Insertion,
    Welder
}
