using System.Collections.Generic;
using System.Xml.Serialization;
using PeekSeekRaid.Core.Models;
using Rocket.API;

namespace PeekSeekRaid;

[XmlRoot("Config")]
public class PeekSeekRaidConfiguration : IRocketPluginConfiguration {
    /// Handed out on top of the kit, every round, to everyone. These are the
    /// gamemode's default items, the same list the site holds under `defaultItems`
    /// for a competitive gamemode.
    public List<RaidItem> DefaultItems;

    /// How long teams have to build, in seconds. Long enough to put up a real
    /// building, short enough that nobody is standing around at the end.
    public int BuildSeconds;

    /// How long the boards stay up before they pick for you. Attackers choose where they
    /// come in and what they bring; defenders choose what they hold with, and one of
    /// them stands the flag in the same time.
    public int PickSeconds;

    /// The round clock. Running it out with the flag still standing is a defender win,
    /// which is what makes attacking a race rather than a raid. Short while the
    /// gamemode is being tested end to end; a real match wants 180.
    public int RoundSeconds;

    /// The pieces this gamemode places itself. A site starts as the metal floors of the
    /// footprint the team chose, with a pillar on every corner.
    ///
    /// The starting floors are the site: never salvageable, never damaged, by anybody,
    /// in any phase, whatever material a team has swapped them to, refused by position
    /// in RaidWorld. A team that could move its anchor could build a second base beside
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
    public List<RaidItem> TriangleSupplies;

    /// A team's pieces for the whole build phase: one budget, shared by the team. A
    /// builder never carries the kit; they carry one of each piece the team still has,
    /// so the housing planner lists what can still go down, and the panel beside the
    /// screen says how many. Everything is a budget: metal is what survives a charge, so
    /// how much of a building is metal and which parts is the most interesting decision
    /// a team makes, and it stops being one the moment anything is unlimited.
    ///
    /// Floors are in the kit, and any floor placed has to touch a floor that is already
    /// standing (RaidWorld refuses the rest), so the base grows out from the anchor
    /// rather than being laid beside it. A piece already standing can be swapped for
    /// the same piece in another material, which refunds the old one here.
    public List<RaidItem> BuildSupplies;

    /// What a builder is handed on top of their clothes: the housing planner, which is
    /// force-equipped, and nothing else. `/raid find <text>` looks an id up in game.
    public List<RaidItem> BuildTools;

    /// Clothing given only to a player whose own kit did not dress them — because the
    /// site was unreachable, or their loadout has no outfit in it. Anybody with a kit
    /// keeps wearing their own.
    public List<string> FallbackOutfit;

    /// How long holding the salvage key takes, in seconds, while building and in a
    /// round. The game's own is eight. A salvage while building is the swap board,
    /// so it is as quick as the planner's own half second; in a round it is a birch
    /// wall coming down, which should be a decision rather than a reflex.
    public float BuildSalvageSeconds;
    public float RoundSalvageSeconds;

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
    public List<RaidSite> Sites;

    /// What a defender may take into a round, and what an attacker may. Five each,
    /// because the board is built for five and a sixth would not fit on screen.
    public List<RaidOption> DefenceOptions;
    public List<RaidOption> RaidOptions;

    /// How long everybody stays rooted, in position and with the flag up, before the
    /// round goes live. The flag itself is stood during the pick: the defender chosen
    /// for it picks their powerup and then has the rest of the pick to walk the base.
    public int StartSeconds;

    /// What the explosives do to a building, written onto the game's own assets when a
    /// match starts. Structure damage is health taken off a wall: a maple wall is 400,
    /// birch 350, metal 1100, so 1000 on a charge is one wood wall and two metal, and
    /// 380 on a rocket is two wood and three metal. The rocket's blast radius is the
    /// game's own times the multiplier, so a rocket is a hole in a wall rather than a
    /// room.
    public ushort ChargeItemId;
    public float ChargeStructureDamage;
    public ushort RocketItemId;
    public float RocketStructureDamage;
    public float RocketRangeMultiplier;

    /// The gun every sentry goes down with, put in the moment it is placed and never
    /// coming out. One item, with whatever state it needs: the id, and the same
    /// comma-separated state bytes a kit item takes, or none for the game's default,
    /// which for a gun is the standard sight, a full magazine and full quality.
    public RaidItem SentryFirearm;

    public void LoadDefaults() {
        // Melee and a dressing, matching what the site hands out as `defaultItems` on
        // the competitive gamemodes.
        DefaultItems = [
            new RaidItem(395, 1),
            new RaidItem(1103, 1)
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
        // doorway, 51 floor. Metal holds two charges (1100): 369 floor, 370 doorway, 371
        // wall, 373 roof, 376 hole. There is no brick, and the only pillar is maple:
        // the frame is the frame, and a wall is what a team chooses to make metal.
        // Doors are metal only, 378, as the brief asked.
        BuildSupplies = [
            new RaidItem(36, 80),
            new RaidItem(33, 40),
            new RaidItem(35, 24),
            new RaidItem(31, 16),
            new RaidItem(32, 8),
            new RaidItem(34, 8),
            new RaidItem(319, 4),
            new RaidItem(316, 4),
            new RaidItem(322, 4),
            new RaidItem(442, 8),
            new RaidItem(57, 12),
            new RaidItem(49, 4),
            new RaidItem(51, 4),
            new RaidItem(371, 8),
            new RaidItem(370, 4),
            new RaidItem(369, 4),
            new RaidItem(373, 4),
            new RaidItem(376, 2),
            new RaidItem(378, 6)
        ];

        // 1262 maple triangle floor, 1266 maple triangle roof.
        TriangleSupplies = [
            new RaidItem(1262, 12),
            new RaidItem(1266, 12)
        ];

        // 1764 is the housing planner: the one tool that says "builder" and makes the
        // placing itself less of a fight. A kitless player gets a bag or their supplies
        // land on the floor; the name is looked up, so it survives an id change.
        BuildTools = [new RaidItem(1764, 1)];
        FallbackOutfit = ["Alicepack"];

        BuildSalvageSeconds = 0.5f;
        RoundSalvageSeconds = 3f;

        HandsWidth = 14;
        // Grown to hold one of every piece at once: thirty kinds at two or four cells
        // each is more than a backpack.
        HandsHeight = 12;

        OffBaseDamagePerSecond = 12;
        OffBaseGraceSeconds = 3f;

        Sites = [
            new RaidSite {
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
            new RaidSite {
                Name = "South",
                Anchor = new SerializableVector3(224.10f, 81.41f, -379.60f),
                Yaw = 354.825f,
                BuildRadius = 32f,
                AttackerSpawns = [
                    new SerializableVector3(150.36f, 85.01f, -364.48f),
                    new SerializableVector3(207.59f, 72.66f, -306.13f),
                    new SerializableVector3(311.91f, 103.34f, -369.68f)
                ]
            }
        ];

        // Every number below is off the game's own item files, or set on them by
        // RaidWorld.TuneExplosives from the fields above: a charge is two to a metal
        // wall and one to wood, a rocket three and two.
        //
        // Defence. 43131 is the PeekSeek Sentry, the hostile sentry from the UI workshop
        // item with Requires_Power off, armed by SentryFirearm the moment it goes down
        // and shooting anybody not in its group; its picture is the vanilla sentry's,
        // which is the one the site has. 76 is the blowtorch, which repairs anything
        // built. 386 barbed wire (40 to a player who walks it), 1101 landmine (91 to a
        // player, 75 to a structure).
        DefenceOptions = [
            new RaidOption {
                Name = "Sentry",
                Blurb = "A hostile sentry with a shotgun in it. Put it where they have to come through.",
                Icon = 1373,
                Items = [new RaidItem(43131, 1)]
            },
            new RaidOption {
                Name = "Welder",
                Blurb = "A blowtorch to mend what they crack, and two metal walls to close a hole.",
                Icon = 76,
                Items = [new RaidItem(76, 1), new RaidItem(371, 2)]
            },
            new RaidOption {
                Name = "Trapper",
                Blurb = "Three landmines and four coils of wire. The doorways are yours.",
                Icon = 1101,
                Items = [new RaidItem(1101, 3), new RaidItem(386, 4)]
            },
            new RaidOption {
                Name = "Reinforce",
                Blurb = "Six metal walls, a door and two pillars. Rebuild the base they are about to meet.",
                Icon = 371,
                Items = [new RaidItem(371, 6), new RaidItem(378, 1), new RaidItem(36, 2)]
            },
            new RaidOption {
                Name = "Watchtower",
                Blurb = "A scope and three grenades. See them coming, and greet them.",
                Icon = 1408,
                Items = [new RaidItem(1408, 1), new RaidItem(254, 3)]
            }
        ];

        // Attack. Every kit has a way through a wall; what changes is how many walls,
        // from how far, and what is left over for the people inside. In metal walls: a
        // charge (1241, set off with the 1240 detonator) is half of one, a rocket (520,
        // from the 519 launcher) a third, a sticky (1100) a twelfth. The more of it
        // comes from range, the less of it there is: twelve charges are six walls at
        // the wall, nine rockets are three from across the field. A base that is metal
        // all the way round is four walls deep at most, and the flag is somewhere
        // inside it.
        RaidOptions = [
            new RaidOption {
                Name = "Demolition",
                Blurb = "Twelve charges and the detonator. Six metal walls, if you can reach them.",
                Icon = 1241,
                Items = [new RaidItem(1240, 1), new RaidItem(1241, 12)]
            },
            new RaidOption {
                Name = "Rockets",
                Blurb = "Nine rockets. Three metal walls, from wherever you are standing.",
                Icon = 519,
                Items = [new RaidItem(519, 1), new RaidItem(520, 9)]
            },
            new RaidOption {
                Name = "Breacher",
                Blurb = "Three rockets to open it from range, five charges to finish the job up close.",
                Icon = 1241,
                Items = [new RaidItem(519, 1), new RaidItem(520, 3), new RaidItem(1240, 1), new RaidItem(1241, 5)]
            },
            new RaidOption {
                Name = "Grenadier",
                Blurb = "Twelve stickies, four impacts, two frags. A metal wall, or three wood, and the room behind it.",
                Icon = 1100,
                Items = [new RaidItem(1100, 12), new RaidItem(1520, 4), new RaidItem(254, 2)]
            },
            new RaidOption {
                Name = "Sapper",
                Blurb = "Four charges, six frags. Two metal walls, then clear what is behind them without going in.",
                Icon = 254,
                Items = [new RaidItem(1240, 1), new RaidItem(1241, 4), new RaidItem(254, 6)]
            }
        ];

        StartSeconds = 3;

        ChargeItemId = 1241;
        ChargeStructureDamage = 1000f;
        RocketItemId = 520;
        RocketStructureDamage = 380f;
        RocketRangeMultiplier = 0.8f;

        // 112 is the Bluntforce; the state is left to the game, which loads it.
        SentryFirearm = new RaidItem(112, 1);
    }
}
