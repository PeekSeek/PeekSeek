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

    /// How long the boards stay up before they pick for you. Attackers choose where they
    /// come in and what they bring; defenders choose what they hold with, and one of
    /// them stands the flag in the same time.
    public int PickSeconds;

    /// The round clock. Running it out with the flag still standing is a defender win,
    /// which is what makes attacking a race rather than a siege. Short while the
    /// gamemode is being tested end to end; a real match wants 180.
    public int RoundSeconds;

    /// The pieces this gamemode places itself. A site starts as the metal floors of the
    /// footprint the team chose, with a pillar on every corner.
    ///
    /// The starting floors are the site: never salvageable, never damaged, by anybody,
    /// in any phase, whatever material a team has swapped them to, refused by position
    /// in SiegeWorld. A team that could move its anchor could build a second base beside
    /// the first and pick whichever suited the round. The pillars are not fixtures: they
    /// go down in the team's group so the team can pull one out if the plan needs the
    /// corner, and from then on they are just part of the base.
    ///
    /// The marker is the flag one defender stands somewhere on the base before each
    /// round, and the round's objective. It has to be a barricade with health and
    /// without a claim bubble: a vanilla claim flag keeps everybody but its own group
    /// from placing anything within 32m of it, on the client, which is exactly the
    /// charge an attacker needs to put on the wall. 43130 is the PeekSeek Flag from the
    /// UI workshop item: the claim flag's own model, a plain barricade with 600 health
    /// and no bubble.
    public ushort AnchorFloorItemId;
    public ushort AnchorTriangleItemId;
    public ushort AnchorPillarItemId;
    public ushort AnchorMarkerItemId;

    /// Handed out on top of BuildSupplies to a team that chose a shape with triangles
    /// in it, because a triangular floor needs triangular pieces to build on.
    public List<SiegeItem> TriangleSupplies;

    /// A team's pieces for the whole build phase: one budget, shared by the team. A
    /// builder never carries the kit; they carry one of each piece the team still has,
    /// so the housing planner lists what can still go down, and the panel beside the
    /// screen says how many. Everything is a budget: metal is what survives a charge, so
    /// how much of a building is metal and which parts is the most interesting decision
    /// a team makes, and it stops being one the moment anything is unlimited.
    ///
    /// Floors are in the kit, and any floor placed has to touch a floor that is already
    /// standing (SiegeWorld refuses the rest), so the base grows out from the anchor
    /// rather than being laid beside it. A piece already standing can be swapped for
    /// the same piece in another material, which refunds the old one here.
    public List<SiegeItem> BuildSupplies;

    /// What a builder is handed on top of their clothes: the housing planner, which is
    /// force-equipped, and nothing else. `/siege find <text>` looks an id up in game.
    public List<SiegeItem> BuildTools;

    /// Clothing given only to a player whose own kit did not dress them — because the
    /// site was unreachable, or their loadout has no outfit in it. Anybody with a kit
    /// keeps wearing their own.
    public List<string> FallbackOutfit;

    /// How big a builder's hands page is, in inventory cells.
    ///
    /// Unturned does replicate a page resize — Items.resize tells the owning client the
    /// new size — so this is a real answer to "a backpack does not hold a building"
    /// rather than a workaround. The hands page is the one worth growing: it is always
    /// there, where the backpack and vest pages get resized back to the worn asset every
    /// time somebody changes clothes.
    public byte HandsWidth;
    public byte HandsHeight;

    /// Damage per second to a defender who is not standing on their base, and how
    /// long they may be off it before that starts.
    ///
    /// This is what makes a base a base. A defender who runs out into the field is
    /// playing a different game, so open ground is simply lethal to them. Not
    /// instantly, though: vaulting a gap or dropping through a hatch both leave you
    /// briefly on dirt and neither should cost health.
    public byte OffBaseDamagePerSecond;
    public float OffBaseGraceSeconds;

    /// The two plots. Order is the team order, so team 0 builds at Sites[0]. Each
    /// carries the three points its attackers choose between; those are walked out on
    /// the map by hand, somewhere with cover most of the way in, and the picture the
    /// attackers choose on is baked from the same numbers.
    public List<SiegeSite> Sites;

    /// What a defender may take into a round, and what an attacker may. Five each,
    /// because the board is built for five and a sixth would not fit on screen.
    public List<SiegeOption> DefenceOptions;
    public List<SiegeOption> RaidOptions;

    /// The Unity UI this plugin draws the option board with, as the GUID of an
    /// EffectAsset in a workshop bundle the server pushes to clients.
    ///
    /// Unturned has no way to draw a custom UI from plugin code alone: the layout has
    /// to exist as a prefab inside an asset bundle the client already has. The default
    /// is the GUID in ui/Assets/PeekSeekSiege/Bundles/Effects/PickBoard/PickBoard.dat,
    /// so a server that has the workshop item draws the board and one that does not
    /// logs a line and draws it to chat instead, which is worse to look at but plays
    /// identically. See ui/README.md.
    public string PickUIEffectGUID;

    /// Where the board fetches an option's picture, with `{0}` standing for the item id.
    /// The site already serves every item's icon for its loadout editor, so the tiles
    /// show the same pictures a player picked their kit from.
    public string ItemIconURL;

    /// The channel the UI is sent on. Any small number no other plugin on the server
    /// is using will do; it only has to be stable, so the board can be updated and
    /// cleared after it is sent.
    public short PickUIKey;

    /// The match HUD along the top of the screen, from the same workshop item as the
    /// board: `Effects/MatchHud/MatchHud.dat` in `ui/`. Its own key, so the board and
    /// the HUD can be up at the same time.
    public string MatchHudEffectGUID;
    public short MatchHudKey;

    /// The picture on a HUD card for a player the site has no picture for yet: Steam's
    /// own placeholder, the same one the site falls back to.
    public string DefaultAvatarURL;

    /// The panel beside the screen during the build phase, listing what the team has
    /// left to lay: `Effects/BuildPanel/BuildPanel.dat` in `ui/`, on its own key.
    public string BuildPanelEffectGUID;
    public short BuildPanelKey;

    /// How long everybody stays rooted, in position and with the flag up, before the
    /// round goes live. The flag itself is stood during the pick: the defender chosen
    /// for it picks their powerup and then has the rest of the pick to walk the base.
    public int StartSeconds;

    /// The gun every sentry goes down with, put in the moment it is placed and never
    /// coming out. One item, with whatever state it needs: the id, and the same
    /// comma-separated state bytes a kit item takes, or none for the game's default,
    /// which for a gun is the standard sight, a full magazine and full quality.
    public SiegeItem SentryFirearm;

    public void LoadDefaults() {
        szAPIURL = "https://peekseek.net";
        KitCategory = "meta";
        ChatIconURL = "https://peekseek.net/static/img/logo.png";

        // Melee and a dressing, matching what the site hands out as `defaultItems` on
        // the competitive gamemodes.
        DefaultItems = [
            new SiegeItem(395, 1),
            new SiegeItem(1103, 1)
        ];

        // Short on purpose while the gamemode is being tested end to end; a real match
        // wants 300.
        BuildSeconds = 30;
        PickSeconds = 20;
        RoundSeconds = 30;

        AnchorFloorItemId = 369;
        AnchorTriangleItemId = 1265;
        AnchorPillarItemId = 36;
        AnchorMarkerItemId = 43130;

        // Ids are read off the game's own Bundles/Items rather than guessed. Maple is
        // the standard piece (400 health a wall): 31 floor, 32 doorway, 33 wall, 34
        // window, 35 roof, 36 pillar, 316 stairs, 319 hole, 322 ramp, 442 rampart.
        // Birch is the soft wall a team can pull down mid-round (350): 57 wall, 49
        // doorway, 51 floor. Metal holds a rocket (1100): 369 floor, 370 doorway, 371
        // wall, 373 roof, 374 pillar, 376 hole. Brick holds two (1650): 1215 wall, 1212
        // pillar. Doors are metal only, 378, as the brief asked.
        BuildSupplies = [
            new SiegeItem(36, 80),
            new SiegeItem(33, 40),
            new SiegeItem(35, 24),
            new SiegeItem(31, 16),
            new SiegeItem(32, 8),
            new SiegeItem(34, 8),
            new SiegeItem(319, 4),
            new SiegeItem(316, 4),
            new SiegeItem(322, 4),
            new SiegeItem(442, 8),
            new SiegeItem(57, 12),
            new SiegeItem(49, 4),
            new SiegeItem(51, 4),
            new SiegeItem(371, 8),
            new SiegeItem(370, 4),
            new SiegeItem(369, 4),
            new SiegeItem(374, 8),
            new SiegeItem(373, 4),
            new SiegeItem(376, 2),
            new SiegeItem(1215, 4),
            new SiegeItem(1212, 4),
            new SiegeItem(378, 6)
        ];

        // 1262 maple triangle floor, 1266 maple triangle roof.
        TriangleSupplies = [
            new SiegeItem(1262, 12),
            new SiegeItem(1266, 12)
        ];

        // 1764 is the housing planner: the one tool that says "builder" and makes the
        // placing itself less of a fight. A kitless player gets a bag or their supplies
        // land on the floor; the name is looked up, so it survives an id change.
        BuildTools = [new SiegeItem(1764, 1)];
        FallbackOutfit = ["Alicepack"];

        HandsWidth = 14;
        // Grown to hold one of every piece at once: thirty kinds at two or four cells
        // each is more than a backpack.
        HandsHeight = 12;

        OffBaseDamagePerSecond = 12;
        OffBaseGraceSeconds = 3f;

        Sites = [
            new SiegeSite {
                Name = "North",
                Anchor = new SerializableVector3(74.97f, 65.65f, 311.35f),
                Yaw = 321.4624f,
                BuildRadius = 32f,
                AttackerSpawns = [
                    new SerializableVector3(152.60f, 67.76f, 383.00f),
                    new SerializableVector3(8.21f, 68.47f, 286.77f),
                    new SerializableVector3(91.95f, 62.73f, 232.95f)
                ]
            },
            new SiegeSite {
                Name = "South",
                Anchor = new SerializableVector3(234.63f, 52.23f, -240.41f),
                Yaw = 354.825f,
                BuildRadius = 32f,
                AttackerSpawns = [
                    new SerializableVector3(155.81f, 35.27f, -173.20f),
                    new SerializableVector3(310.10f, 63.16f, -184.88f),
                    new SerializableVector3(305.46f, 79.00f, -321.41f)
                ]
            }
        ];

        // Every number below is off the game's own item files. Traps: 386 barbed wire
        // (40 to a player who walks it), 1101 landmine (91 to a player, 75 to a
        // structure), 43131 the PeekSeek Sentry, the hostile sentry from the UI
        // workshop item with Requires_Power off, armed by SentryFirearm the moment it
        // goes down and shooting anybody not in its group. Its picture is the vanilla
        // sentry's, which is the one the site has.
        DefenceOptions = [
            new SiegeOption {
                Name = "Barbed Wire",
                Blurb = "Four coils. Slows and bleeds anyone who walks it.",
                Items = [new SiegeItem(386, 4)]
            },
            new SiegeOption {
                Name = "Landmines",
                Blurb = "Three, buried. One step and that doorway is yours again.",
                Items = [new SiegeItem(1101, 3)]
            },
            new SiegeOption {
                Name = "Reinforce",
                Blurb = "Spare metal to close a hole mid-round.",
                Items = [new SiegeItem(371, 6), new SiegeItem(374, 4)]
            },
            new SiegeOption {
                Name = "Watchtower",
                Blurb = "Grenades and a scope. See them coming, and greet them.",
                Icon = 1408,
                Items = [new SiegeItem(254, 2), new SiegeItem(1408, 1)]
            },
            new SiegeOption {
                Name = "Sentry",
                Blurb = "A hostile sentry with a shotgun in it. Put it where they have to come through.",
                Icon = 1373,
                Items = [new SiegeItem(43131, 1)]
            }
        ];

        // Raiders are the explosive side, and the numbers are structure damage: a
        // rocket (519 launcher, 520 rocket) and a demolition charge (1241, set off with
        // the 1240 detonator) are 1000 each, a sticky (1100) or an impact grenade
        // (1520) 100, a frag (254) 50. A maple wall is 400, metal 1100, brick 1650, and
        // the flag 1100. The launcher gets fewer shots than the charges because it
        // fires them from across the field; a charge has to be walked to the wall.
        RaidOptions = [
            new SiegeOption {
                Name = "Rocket Launcher",
                Blurb = "Two rockets, from wherever you are standing. One takes any wood wall; two take metal.",
                Items = [new SiegeItem(519, 1), new SiegeItem(520, 2)]
            },
            new SiegeOption {
                Name = "Demolition",
                Blurb = "Three charges and the detonator. Stick them on, walk away, press the button.",
                Icon = 1241,
                Items = [new SiegeItem(1240, 1), new SiegeItem(1241, 3)]
            },
            new SiegeOption {
                Name = "Sticky Grenades",
                Blurb = "Six. Each takes a quarter of a wood wall; they stick where they land.",
                Items = [new SiegeItem(1100, 6)]
            },
            new SiegeOption {
                Name = "Impact Grenades",
                Blurb = "Six, and they go off where they land. Good through a window.",
                Items = [new SiegeItem(1520, 6)]
            },
            new SiegeOption {
                Name = "Frags",
                Blurb = "Six grenades. Clear a room without entering it.",
                Items = [new SiegeItem(254, 6)]
            }
        ];

        PickUIEffectGUID = "69fcf241396b44dbb536cf4fc08abbe3";
        PickUIKey = 27310;
        MatchHudEffectGUID = "5398e1499dcf4fda9e4f8a6c4d73faea";
        MatchHudKey = 27311;
        BuildPanelEffectGUID = "7a3d9c2e5f8b4b1c9d0e1f2a3b4c5d6e";
        BuildPanelKey = 27312;

        StartSeconds = 3;

        // 112 is the Bluntforce; the state is left to the game, which loads it.
        SentryFirearm = new SiegeItem(112, 1);
        ItemIconURL = "https://peekseek.net/static/unturned/{0}.png";
        DefaultAvatarURL = "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";
    }
}
