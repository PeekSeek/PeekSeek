using System.Collections.Generic;
using System.Xml.Serialization;
using PeekSeekSiege.Core.Models;
using Rocket.API;

namespace PeekSeekSiege;

[XmlRoot("Config")]
public class PeekSeekSiegeConfiguration : IRocketPluginConfiguration {
    /// Where player kits come from. Only `GET /players/{steam64}/kits` is used, and
    /// only the category named below, so this plugin works against the live site
    /// without needing a match to exist on it.
    public string szAPIURL;

    /// Which saved kit each player fights with. Siege has no kit choice of its own on
    /// purpose: the per-round choice is the option board, and adding a second one
    /// would put two decisions in front of a player at the same moment.
    public string KitCategory;

    /// The icon shown next to every line this server sends, so Siege chat is visibly
    /// the same product as the rest of PeekSeek. Blank for no icon.
    public string ChatIconURL;

    /// Handed out on top of the kit, every round, to everyone. These are the
    /// gamemode's default items, the same list the site holds under `defaultItems`
    /// for a competitive gamemode.
    public List<SiegeItem> DefaultItems;

    /// How long teams have to build, in seconds. Long enough to put up a real
    /// building, short enough that nobody is standing around at the end.
    public int BuildSeconds;

    /// How long the option board stays up before it picks for you.
    public int PickSeconds;

    /// The round clock. Running it out is a defender win, which is what makes
    /// attacking a race rather than a siege.
    public int RoundSeconds;

    /// The pieces this gamemode places itself and must never treat as part of a
    /// team's base: the anchor floor, and the claim flag standing on it.
    public ushort AnchorFloorItemId;
    public ushort AnchorClaimFlagItemId;

    /// What each team is given to build with. No floors in here on purpose: a team
    /// that cannot make a second floor has to build off the anchor, which is what
    /// keeps one base to a site instead of three shacks in a field.
    public List<SiegeItem> BuildSupplies;

    /// What a builder wears and holds, by the name shown on the item.
    ///
    /// Named rather than numbered because these are incidental to the gamemode: a
    /// backpack is "whatever holds the most", not a specific id worth maintaining. The
    /// bag and the vest are what make the supplies below fit at all, and the planner is
    /// what makes five minutes enough to put up a real building.
    public List<string> BuildGear;

    /// How often, in seconds, build supplies are topped back up during the build
    /// phase. Running out of metal with two minutes left is not an interesting
    /// constraint, it is just a player standing still.
    public int SupplyRefillSeconds;

    /// Damage per second to a defender who is not standing on their base, and how
    /// long they may be off it before that starts.
    ///
    /// This is what makes a base a base. A defender who runs out into the field is
    /// playing a different game, so open ground is simply lethal to them. Not
    /// instantly, though: vaulting a gap or dropping through a hatch both leave you
    /// briefly on dirt and neither should cost health.
    public byte OffBaseDamagePerSecond;
    public float OffBaseGraceSeconds;

    /// The two plots. Order is the team order, so team 0 builds at Sites[0].
    public List<SiegeSite> Sites;

    /// What a defender may take into a round, and what an attacker may. Five each,
    /// because the board is built for five and a sixth would not fit on screen.
    public List<SiegeOption> DefenceOptions;
    public List<SiegeOption> RaidOptions;

    /// The Unity UI this plugin draws the option board with, as the GUID of an
    /// EffectAsset in a workshop bundle the server pushes to clients.
    ///
    /// Unturned has no way to draw a custom UI from plugin code alone: the layout has
    /// to exist as a prefab inside an asset bundle the client already has. Until that
    /// bundle is published this stays empty and the board is drawn to chat instead,
    /// which is worse to look at but plays identically. See the plugin README for the
    /// prefab this GUID has to point at.
    public string PickUIEffectGUID;

    /// The channel the UI is sent on. Any small number no other plugin on the server
    /// is using will do; it only has to be stable, so the board can be updated and
    /// cleared after it is sent.
    public short PickUIKey;

    public void LoadDefaults() {
        szAPIURL = "https://api.peekseek.com";
        KitCategory = "meta";
        ChatIconURL = "";

        // Melee and a dressing, matching what the site hands out as `defaultItems` on
        // the competitive gamemodes.
        DefaultItems = [
            new SiegeItem(395, 1),
            new SiegeItem(1103, 1)
        ];

        BuildSeconds = 300;
        PickSeconds = 20;
        RoundSeconds = 180;

        AnchorFloorItemId = 369;
        AnchorClaimFlagItemId = 1158;

        // Metal for everything that should stay up, birch walls as the soft walls a
        // team can cut new openings through, and metal doors and hatches for the ways
        // in the team itself needs to use.
        //
        // Ids are read off the server's own Bundles/Items rather than guessed, because
        // the metal structure block is not in the order its names suggest: 370 is the
        // doorway and 373 the roof, not the other way round.
        BuildSupplies = [
            new SiegeItem(371, 40),   // Metal Wall
            new SiegeItem(370, 10),   // Metal Doorway
            new SiegeItem(372, 10),   // Metal Window
            new SiegeItem(374, 30),   // Metal Pillar
            new SiegeItem(373, 20),   // Metal Roof
            new SiegeItem(375, 8),    // Metal Stairs
            new SiegeItem(57, 20),    // Birch Wall, the soft one
            new SiegeItem(378, 8),    // Metal Door
            new SiegeItem(1332, 8)    // Metal Hatch
        ];
        BuildGear = ["Alicepack", "Military Vest", "Housing Planner"];
        SupplyRefillSeconds = 30;

        OffBaseDamagePerSecond = 12;
        OffBaseGraceSeconds = 2f;

        Sites = [
            new SiegeSite {
                Name = "North",
                Anchor = new SerializableVector3(74.97f, 65.65f, 311.35f),
                Yaw = 321.4624f,
                BuildRadius = 32f,
                AttackerSpawnDistance = 70f,
                AttackerBearing = 141f
            },
            new SiegeSite {
                Name = "South",
                Anchor = new SerializableVector3(234.63f, 52.23f, -240.41f),
                Yaw = 354.825f,
                BuildRadius = 32f,
                AttackerSpawnDistance = 70f,
                AttackerBearing = 175f
            }
        ];

        DefenceOptions = [
            new SiegeOption {
                Name = "Barbed Wire",
                Blurb = "Slows and bleeds anyone who walks it.",
                Items = [new SiegeItem(1101, 4)]
            },
            new SiegeOption {
                Name = "Landmines",
                Blurb = "Buried. One step and that doorway is yours again.",
                Items = [new SiegeItem(1102, 3)]
            },
            new SiegeOption {
                Name = "Reinforce",
                Blurb = "Spare metal to close a hole mid-round.",
                Items = [new SiegeItem(371, 6), new SiegeItem(374, 4)]
            },
            new SiegeOption {
                Name = "Watchtower",
                Blurb = "Grenades and a scope. See them coming, and greet them.",
                Items = [new SiegeItem(254, 2), new SiegeItem(1408, 1)]
            },
            new SiegeOption {
                Name = "Medic",
                Blurb = "Two medkits and a dressing. Outlast the clock.",
                Items = [new SiegeItem(95, 2), new SiegeItem(1103, 1)]
            }
        ];

        RaidOptions = [
            new SiegeOption {
                Name = "Breach Charge",
                Blurb = "Puts a hole where there was a wall.",
                Items = [new SiegeItem(519, 2), new SiegeItem(1838, 2)]
            },
            new SiegeOption {
                Name = "Frags",
                Blurb = "Three grenades. Clear a room without entering it.",
                Items = [new SiegeItem(254, 3)]
            },
            new SiegeOption {
                Name = "Sledge",
                Blurb = "Quiet entry, straight through the soft walls.",
                Items = [new SiegeItem(17, 1), new SiegeItem(1838, 1)]
            },
            new SiegeOption {
                Name = "Smoke",
                Blurb = "Cover the approach. They cannot shoot what they cannot see.",
                Items = [new SiegeItem(1408, 3)]
            },
            new SiegeOption {
                Name = "Ammo",
                Blurb = "Spare magazines and a vest. Win the long fight.",
                Items = [new SiegeItem(1838, 4), new SiegeItem(125, 1)]
            }
        ];

        PickUIEffectGUID = "";
        PickUIKey = 27310;
    }
}
