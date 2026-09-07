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

    /// The pieces this gamemode places itself and must never treat as part of a
    /// team's base: the anchor floor, and the flag standing on it that marks the middle
    /// of the site.
    ///
    /// The marker is a claim flag, and it is placed in the site team's group so that the
    /// team can build inside its own 32m bubble. That is the only whitelist a claim flag
    /// has: ClaimManager.checkCanBuild admits you if you match the bubble's owner or its
    /// group, and the bubble is built on the client from the replicated barricade, so
    /// there is nothing a server-side patch can do about it. Being in the group does not
    /// make it theirs to take down — salvage and damage are refused by item id in
    /// SiegeWorld, for everybody, in every phase.
    public ushort AnchorFloorItemId;
    public ushort AnchorMarkerItemId;

    /// The scarce half of a team's materials: metal, and floors of any kind.
    ///
    /// Issued once per player at the start of the build phase and never topped up, so
    /// the amounts here are a budget for the whole five minutes. Metal is what survives
    /// a breach charge, so how much of a building is metal and which parts is the most
    /// interesting decision a team makes — and it stops being a decision the moment the
    /// metal is unlimited. Floors are rationed for a second reason: a team with plenty
    /// of them could lay a second base beside the anchor and pick whichever suited the
    /// round.
    public List<SiegeItem> RationedSupplies;

    /// The plentiful half: birch. Topped back up continuously, so nobody ever stands
    /// still waiting for wood.
    ///
    /// `Amount` here means "how many you keep on you", not "how many you are given" —
    /// what a player holds is counted and only the difference is handed over. That is
    /// what makes a small inventory behave like an unlimited one. Unturned decides
    /// inventory size from the clothing a player wears and a plugin cannot change it
    /// (the client sizes each page from the backpack asset it was told to wear, so a
    /// server-side resize only desyncs), so the supply has to come to the player a
    /// little at a time instead.
    public List<SiegeItem> PlentifulSupplies;

    /// What a builder is handed on top of their own loadout. `/siege find <text>` looks
    /// an id up in game if one of these ever needs swapping.
    public List<SiegeItem> BuildTools;

    /// Clothing given only to a player whose own kit did not dress them — because the
    /// site was unreachable, or their loadout has no outfit in it. Anybody with a kit
    /// keeps wearing their own.
    public List<string> FallbackOutfit;

    /// How often, in seconds, the plentiful supplies are topped back up.
    public int SupplyRefillSeconds;

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
        szAPIURL = "https://peekseek.net";
        KitCategory = "meta";
        ChatIconURL = "https://peekseek.net/public/img/logo.png/";

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
        AnchorMarkerItemId = 1158;

        // Ids are read off the server's own Bundles/Items rather than guessed, and none
        // of the three materials is a contiguous block in the order its names suggest.
        //
        // Brick: 1210 doorway, 1212 pillar, 1215 wall, 1216 window, 1984 roof.
        // Birch: 49 doorway, 51 floor, 53 pillar, 55 roof, 57 wall, 59 window, 318 stairs.
        // Metal: 378 door, 1332 hatch.
        //
        // Brick is the scarce material rather than metal: at 1650 health it takes real
        // explosives to open, and it is what a team spends its one allowance on. Doors
        // and hatches stay metal because they are the ways a team moves through its own
        // building and should not be the soft spot.
        RationedSupplies = [
            new SiegeItem(1215, 6),
            new SiegeItem(1984, 4),
            new SiegeItem(1212, 4),
            new SiegeItem(1210, 2),
            new SiegeItem(1216, 2),
            new SiegeItem(378, 2),
            new SiegeItem(1332, 2)
        ];

        PlentifulSupplies = [
            new SiegeItem(57, 30),
            new SiegeItem(49, 12),
            new SiegeItem(59, 12),
            new SiegeItem(53, 24),
            new SiegeItem(55, 20),
            new SiegeItem(318, 8)
        ];

        SupplyRefillSeconds = 5;
        HandsWidth = 14;
        HandsHeight = 20;

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

        PickUIEffectGUID = "";
        PickUIKey = 27310;
    }
}
