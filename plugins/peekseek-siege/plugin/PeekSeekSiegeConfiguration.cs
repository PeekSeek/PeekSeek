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

    /// The round clock. Running it out with the flag still standing is a defender win,
    /// which is what makes attacking a race rather than a siege. Short while the
    /// gamemode is being tested end to end; a real match wants 180.
    public int RoundSeconds;

    /// The pieces this gamemode places itself. A site starts as a three-by-three grid of
    /// metal floors with a pillar on each outer corner and the claim flag in the middle.
    ///
    /// The floors and the flag are fixtures: never part of a snapshot, never salvageable,
    /// never damaged, by anybody, in any phase, refused by item id in SiegeWorld. A team
    /// that could move its anchor could build a second base beside the first and pick
    /// whichever suited the round. The pillars are not fixtures: they go down in the
    /// team's group so the team can pull one out if the plan needs the corner, and from
    /// then on they are just part of the base.
    ///
    /// The flag is placed in the site team's group so that the team can build inside its
    /// own 32m bubble. That is the only whitelist a claim flag has: ClaimManager.
    /// checkCanBuild admits you if you match the bubble's owner or its group, and the
    /// bubble is built on the client from the replicated barricade, so there is nothing
    /// a server-side patch can do about it.
    public ushort AnchorFloorItemId;
    public ushort AnchorTriangleItemId;
    public ushort AnchorPillarItemId;
    public ushort AnchorMarkerItemId;

    /// Handed out on top of BuildSupplies to a team that chose a shape with triangles
    /// in it, because a triangular floor needs triangular pieces to build on.
    public List<SiegeItem> TriangleSupplies;

    /// A team's materials for the whole build phase, issued once. Everything is a
    /// budget: metal is what survives a charge, so how much of a building is metal and
    /// which parts is the most interesting decision a team makes, and it stops being
    /// one the moment anything is unlimited.
    ///
    /// Floors are in the kit, and any floor placed has to touch a floor that is already
    /// standing (SiegeWorld refuses the rest), so the base grows out from the anchor
    /// rather than being laid beside it.
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
        AnchorMarkerItemId = 1158;

        // Ids are read off the server's own Bundles/Items rather than guessed. Maple:
        // 31 floor, 32 doorway, 33 wall, 35 roof, 36 pillar, 322 ramp, 1262 triangle
        // floor, 1266 triangle roof. Metal: 369 floor, 373 roof, 378 door, 1265
        // triangle floor.
        BuildSupplies = [
            new SiegeItem(36, 50),
            new SiegeItem(33, 20),
            new SiegeItem(35, 20),
            new SiegeItem(32, 5),
            new SiegeItem(31, 5),
            new SiegeItem(378, 5),
            new SiegeItem(322, 3),
            new SiegeItem(373, 3)
        ];

        TriangleSupplies = [
            new SiegeItem(1262, 6),
            new SiegeItem(1266, 12)
        ];

        // 1764 is the housing planner: the one tool that says "builder" and makes the
        // placing itself less of a fight. A kitless player gets a bag or their supplies
        // land on the floor; the name is looked up, so it survives an id change.
        BuildTools = [new SiegeItem(1764, 1)];
        FallbackOutfit = ["Alicepack"];

        HandsWidth = 14;
        // Grown to hold the whole kit at once: a hundred and ten pieces at two or four
        // cells each is more than a backpack.
        HandsHeight = 32;

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
                Icon = 1408,
                Items = [new SiegeItem(254, 2), new SiegeItem(1408, 1)]
            },
            new SiegeOption {
                Name = "Medic",
                Blurb = "Two medkits and a dressing. Outlast the clock.",
                Items = [new SiegeItem(95, 2), new SiegeItem(1103, 1)]
            }
        ];

        // Raiders are the explosive side. Nothing here is a placed barricade: attackers
        // are in a different group from the site they are attacking, so the defenders'
        // claim bubble refuses them any placement inside it, which is the whole base.
        // C4 is a sticky throwable rather than a placed charge, so it works.
        //
        // 254 fragmentation grenade, 519 rocket launcher, 520 rocket, 1100 C4,
        // 1520 impact grenade. The launcher does 1000 structure damage, so it comes
        // with less of everything else.
        RaidOptions = [
            new SiegeOption {
                Name = "Rocket Launcher",
                Blurb = "Two rockets. Opens brick, and nothing else you carry will.",
                Items = [new SiegeItem(519, 1), new SiegeItem(520, 2)]
            },
            new SiegeOption {
                Name = "C4",
                Blurb = "Four sticky charges. Put them on the wall you want gone.",
                Items = [new SiegeItem(1100, 4)]
            },
            new SiegeOption {
                Name = "Impact Grenades",
                Blurb = "Six, and they go off where they land.",
                Items = [new SiegeItem(1520, 6)]
            },
            new SiegeOption {
                Name = "Frags",
                Blurb = "Six grenades. Clear a room without entering it.",
                Items = [new SiegeItem(254, 6)]
            },
            new SiegeOption {
                Name = "Field Medic",
                Blurb = "Two medkits and a dressing. Survive the first room.",
                Items = [new SiegeItem(95, 2), new SiegeItem(1103, 1)]
            }
        ];

        PickUIEffectGUID = "69fcf241396b44dbb536cf4fc08abbe3";
        PickUIKey = 27310;
        MatchHudEffectGUID = "5398e1499dcf4fda9e4f8a6c4d73faea";
        MatchHudKey = 27311;
        ItemIconURL = "https://peekseek.net/static/unturned/{0}.png";
        DefaultAvatarURL = "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";
    }
}
