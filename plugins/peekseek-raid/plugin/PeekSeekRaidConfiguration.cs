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
    /// come in and what they bring; defenders choose what they hold with.
    public int PickSeconds;

    /// The round clock. Running it out with the flag still standing is a defender win,
    /// which is what makes attacking a race rather than a raid.
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

    /// What a team that chose the hexagon builds with instead of BuildSupplies: every
    /// square piece at HexagonSquareShare of its count, and each triangle piece at
    /// HexagonTriangleShare of the square piece it stands in for, rounded down.
    public List<TrianglePiece> TriangleSupplies;
    public float HexagonSquareShare;
    public float HexagonTriangleShare;

    /// How much more of a piece a team gets for every player past the first on the
    /// team that will raid it, rounded down: metal walls and roofs by MetalPerEnemy, the
    /// wood by WoodPerEnemy, and doorways, metal or maple, by DoorwayPerEnemy. A 2v2 is
    /// 2x metal, 1.5x wood and the same doorways.
    public float MetalPerEnemy;
    public float WoodPerEnemy;
    public float DoorwayPerEnemy;

    /// A team's pieces for the whole build phase: one budget, shared by the team. A
    /// builder never carries the kit; they carry one of each piece the team still has,
    /// so the housing planner lists what can still go down, and the panel beside the
    /// screen says how many. Everything is a budget: metal is what survives a charge, so
    /// how much of a building is metal and which parts is the most interesting decision
    /// a team makes, and it stops being one the moment anything is unlimited.
    ///
    /// Floors are in the kit, and any floor placed has to touch a floor that is already
    /// standing (RaidWorld refuses the rest), so the base grows out from the anchor
    /// rather than being laid beside it.
    public List<RaidItem> BuildSupplies;

    /// What a standing piece can be turned into by salvaging it while building, without
    /// spending anything: a maple wall is a wall, a rampart or a window, a maple roof is
    /// a roof or a hole. Every other piece is only itself, so salvaging it takes it
    /// down and puts it back on the pile, and there is no metal window or rampart.
    public List<PieceFamily> PieceFamilies;

    /// The door every doorway is given the moment it goes down, so a door is never a
    /// piece anybody lays, and the frame glass goes into when a wall is salvaged with
    /// a pane in hand.
    public ushort DoorItemId;
    public ushort WindowItemId;

    /// What a builder is handed on top of their clothes: the housing planner, which is
    /// force-equipped, and nothing else. `/raid find <text>` looks an id up in game.
    public List<RaidItem> BuildTools;

    /// Clothing given only to a player whose own kit did not dress them, because the
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
    /// Unturned does replicate a page resize, Items.resize tells the owning client the
    /// new size, so this is a real answer to "a backpack does not hold a building"
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

    /// Every powerup a defender or an attacker may take. The board shows all of them,
    /// five by two, and a powerup has one taker a team: the only rule.
    public List<RaidOption> DefenceOptions;
    public List<RaidOption> RaidOptions;

    /// What every attacker and every defender carries whatever they picked: the floor
    /// under "attackers can always get in". Attackers get a way through a wall each;
    /// defenders get nothing extra.
    public List<RaidItem> AttackBaseline;
    public List<RaidItem> DefenceBaseline;

    /// How long an attacker cannot be hurt after they are placed for a live round, so a
    /// round does not open with somebody shot where they land. It ends the moment that
    /// attacker fires, aims or throws anything: protection that buys free breaching time
    /// is worse than none. Defenders get none, having stood on their base all pick.
    public float SpawnProtectSeconds;

    /// How long a decided round stays on screen before it ends: the flag down, or a
    /// side gone, and a moment to see it before everybody is moved.
    public float RoundEndDelay;

    /// Half way through a round, every attacker still alive gets this many more
    /// demolition charges.
    public int HalfTimeCharges;

    /// The hits table: how many of each tool break each kind of piece, whatever the game
    /// worked out. 0 is nothing. The chainsaw's row is seconds of cutting, and a cut
    /// that stops for longer than CutGapSeconds starts again.
    public List<BreakRule> BreakRules;
    public float CutGapSeconds;

    /// The breach cost: charges (ChargeItemId's row) from outside to the flag along the cheapest of
    /// BreachRays horizontal rays and one straight up. Never more than BreachLimit, and
    /// a flag stands at least FlagDoorClearance from any door. Every metal door put up in
    /// the build brings WallsPerDoor more metal walls into the budget.
    public ushort ChargeItemId;
    public float BreachLimit;
    public int BreachRays;
    public float BreachReach;
    public float FlagDoorClearance;
    public int WallsPerDoor;
    public ushort DoorWallItemId;

    /// The rocket, tuned on the launcher: a launcher without a projectile of its own
    /// takes the damage and blast from the gun, not the rocket (UseableGun). A player
    /// within RocketDirectRadius of the blast takes RocketDirectDamage, anybody else at
    /// most RocketSplashDamage.
    public ushort LauncherItemId;
    public float RocketBlastRadius;
    public float RocketDirectRadius;
    public float RocketDirectDamage;
    public float RocketSplashDamage;

    /// The Keyhole grenade: a door a throw, nothing to walls or players.
    public ushort KeyholeItemId;

    /// The Suppressor: the gun whose player damage is cut, and whose damage to glass is
    /// multiplied.
    public ushort SuppressorGunId;
    public float SuppressorPlayerDamage;
    public float SuppressorGlassMultiplier;

    /// The Stun flashbang: damage to everyone within StunDamageRadius, and how long
    /// anybody it blinds cannot fire a gun.
    public ushort FlashbangItemId;
    public float StunDamage;
    public float StunDamageRadius;
    public float StunSeconds;

    /// Elevator: whoever a bounce charge launches takes no fall damage for
    /// ElevatorFallSeconds. Attackers may build anything anywhere once the round is live,
    /// and what they put down comes down when it ends.
    public ushort BounceChargeItemId;
    public float ElevatorFallSeconds;

    /// Insertion: one transport helicopter per attacking team, fuelled for about
    /// InsertionSeconds, on a platform of InsertionPlatformItemId floors
    /// InsertionHeight up and InsertionDistance further from the base than the pilot's
    /// spawn, with the team already inside it.
    public ushort InsertionVehicleId;
    public float InsertionSeconds;
    public float InsertionHeight;
    public float InsertionDistance;
    public ushort InsertionPlatformItemId;

    /// Second Life: a bedroll that lasts SecondLifeSeconds and brings a teammate back
    /// once on SecondLifeHealth. It is the PeekSeek Bedroll from the UI workshop item, a
    /// barricade that looks like the red bedroll: an Arena map refuses every real bed.
    public ushort BedrollItemId;
    public float SecondLifeSeconds;
    public byte SecondLifeHealth;
    public ushort MedkitItemId;

    /// Nomad: eat the vitamins and off-base damage is off for NomadSeconds.
    public ushort NomadItemId;
    public float NomadSeconds;

    /// Faker: the stereo's fake fight. Every FakerMinSeconds to FakerMaxSeconds it plays
    /// a burst of FakerEffects, a random one each time, from where it stands. Claymores
    /// only go off for attackers.
    public ushort StereoItemId;
    public List<ushort> FakerEffects;
    public float FakerMinSeconds;
    public float FakerMaxSeconds;
    public ushort ClaymoreItemId;

    /// Striker: a plane flies over the red smoke StrikerDelay after it lands, at
    /// StrikerPlaneHeight above it and StrikerPlaneSpeed, and StrikerRockets rockets fall
    /// StrikerSpacing apart within StrikerRadius of it, never within StrikerSafeRadius of
    /// the defenders' own base. The plane is the game's airdrop plane with no drop.
    public ushort RedSmokeItemId;
    public ushort SirenEffect;
    public float StrikerDelay;
    public int StrikerRockets;
    public float StrikerSpacing;
    public float StrikerSafeRadius;
    public float StrikerHeight;
    public float StrikerRadius;
    public float StrikerPlaneSpeed;
    public float StrikerPlaneHeight;

    /// Mirror: the one-way glass bullets from outside the base cannot touch.
    public ushort MirrorItemId;

    /// Supplier: the crate that fills every SupplierSeconds with a medkit and a magazine
    /// for each defender's gun, up to SupplierCap of each.
    public ushort SupplierItemId;
    public float SupplierSeconds;
    public int SupplierCap;
    public ushort SupplyItemId;

    /// Pitfall: a slip plate placeable on the base floor and never salvaged by an
    /// attacker, and a snare that also roots the attacker it catches.
    public ushort SlipPlateItemId;

    public ushort SnareItemId;
    public float SnareRootSeconds;

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

        // Five minutes to build, twenty seconds to pick, thirty to set up, three to raid.
        BuildSeconds = 300;
        PickSeconds = 20;
        RoundSeconds = 180;

        AnchorFloorItemId = 369;
        AnchorTriangleItemId = 1265;
        AnchorPillarItemId = 36;
        AnchorMarkerItemId = 43130;

        // Ids are read off the game's own Bundles/Items rather than guessed. Maple is
        // the standard piece (400 health a wall): 31 floor, 32 doorway, 33 wall, 34
        // window, 35 roof, 319 hole, 322 ramp, 442 rampart. Metal holds two charges
        // (1100): 370 doorway, 371 wall, 373 roof, and 36 the maple pillar. A few doorways and a lot of wall:
        // where the way in is matters more than how many there are. Pillars on top of the
        // ones the site stands, and no doors: every doorway comes with a metal one.
        BuildSupplies = [
            new RaidItem(370, 3),
            new RaidItem(32, 3),
            new RaidItem(371, 10),
            new RaidItem(33, 64),
            new RaidItem(31, 12),
            new RaidItem(35, 32),
            new RaidItem(373, 6),
            new RaidItem(322, 8),
            new RaidItem(36, 48)
        ];

        PieceFamilies = [
            new PieceFamily(33, 33, 442, 34),
            new PieceFamily(35, 35, 319)
        ];

        DoorItemId = 378;
        WindowItemId = 34;

        // 1262 maple triangle floor off the maple floor, 1266 maple triangle roof off
        // the maple roof.
        TriangleSupplies = [
            new TrianglePiece(1262, 31),
            new TrianglePiece(1266, 35)
        ];
        HexagonSquareShare = 0.7f;
        HexagonTriangleShare = 0.9f;

        // Metal is what a charge is counted in, so it keeps pace with the raiders; wood
        // is layout, and a doubled defence is already bringing its own powerups.
        MetalPerEnemy = 1f;
        WoodPerEnemy = 0.5f;
        // More ways in never made a base harder to take.
        DoorwayPerEnemy = 0f;

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
                    new SerializableVector3(215.40f, 72.66f, -304.40f),
                    new SerializableVector3(311.91f, 103.34f, -369.68f)
                ]
            }
        ];

        // Ten a side, as the powerups sheet has them (raid-powerups.png). Every id is off
        // the game's own item files.

        DefenceOptions = [
            // Special: a trick for one situation.
            Option("Nomad", PowerupClass.Special, "Lets you fight outside for 20 s", 391,
                   [new RaidItem(391, 1), new RaidItem(1239, 1)]),
            Option("Faker", PowerupClass.Special, "Fake gunfire and footsteps, and a decoy flag", 1466,
                   [new RaidItem(1466, 1), new RaidItem(43133, 1), new RaidItem(1102, 2)]),
            Option("Striker", PowerupClass.Special, "Drops 9 rockets on your smoke", 266,
                   [new RaidItem(266, 1)]),

            // Armorer: makes the base itself harder to break.
            Option("Mirror", PowerupClass.Armorer, "", 1299,
                   [new RaidItem(1299, 2)]),
            Option("Welder", PowerupClass.Armorer, "", 76,
                   [new RaidItem(76, 1), new RaidItem(371, 3)], Twist.Welder),

            // Warden: heals, sentries and traps. 43131 is the PeekSeek Sentry, the hostile
            // sentry from the UI workshop item with Requires_Power off, armed by
            // SentryFirearm the moment it goes down; its picture is the vanilla sentry's.
            Option("Supplier", PowerupClass.Warden, "Makes a blood bag every 30 s", 1374,
                   [new RaidItem(1374, 1)]),
            Option("Sentry", PowerupClass.Warden, "Two sentries, stun snares and wire", 1373,
                   [new RaidItem(43131, 2), new RaidItem(1113, 3), new RaidItem(386, 4)])
        ];

        RaidOptions = [
            // Destruction: breaks the base.
            Option("Demolition", PowerupClass.Destruction, "", 1241,
                   [new RaidItem(1240, 1), new RaidItem(1241, 8)]),
            Option("Breacher", PowerupClass.Destruction, "Cuts through wood walls, breaks any door in one throw", 490,
                   [new RaidItem(490, 1), new RaidItem(1520, 2)]),

            // Initializer: gets the team in high. The housing planner is what lays a
            // scaffold's pieces.
            Option("Elevator", PowerupClass.Initializer, "", 1691,
                   [new RaidItem(1691, 4), new RaidItem(325, 2)]),
            Option("Scaffold", PowerupClass.Initializer, "", 316,
                   [new RaidItem(1764, 1), new RaidItem(36, 6), new RaidItem(33, 3), new RaidItem(316, 3), new RaidItem(31, 2)]),

            // Support: suppression, a way in, a second life. The Suppressor is the meta
            // kit's Dragonfang (website/src/kits.json): red chevron, tactical laser,
            // vertical grip, military muzzle and a full drum.
            Option("RPG", PowerupClass.Support, "", 519,
                   [new RaidItem(519, 1), new RaidItem(520, 3)]),
            Option("Suppressor", PowerupClass.Support, "", 132,
                   [new RaidItem(132, 1) { Sight = 148, Tactical = 151, Grip = 8, Barrel = 150, Magazine = 133 }, new RaidItem(133, 1)]),
            new RaidOption {
                Name = "Insertion", Category = PowerupClass.Support, Blurb = "The team starts in a transport heli",
                Picture = "vehicle-93", Twist = Twist.Insertion
            },
            Option("Stun", PowerupClass.Support, "Stops anyone caught shooting for 5 s", 1346,
                   [new RaidItem(1346, 1)]),
            Option("Second Life", PowerupClass.Support, "For 30 seconds one teammate can respawn here once placed", 293,
                   [new RaidItem(43132, 1)])
        ];

        AttackBaseline = [new RaidItem(1240, 1), new RaidItem(1241, 4)];
        DefenceBaseline = [];

        RoundEndDelay = 2.5f;
        SpawnProtectSeconds = 3f;
        HalfTimeCharges = 5;

        // Charge, Keyhole, rocket (keyed by the launcher) and chainsaw (seconds).
        BreakRules = [
            new BreakRule("Demolition Charge", 1241, 1, 1, 1, 3, 3),
            new BreakRule("Keyhole", 1520, 1, 0, 0, 0, 0),
            new BreakRule("Rocket", 519, 2, 1, 1, 3, 4),
            new BreakRule("Chainsaw", 490, 0, 1, 3, 0, 0)
        ];
        CutGapSeconds = 0.6f;

        ChargeItemId = 1241;
        BreachLimit = 6f;
        BreachRays = 16;
        BreachReach = 40f;
        FlagDoorClearance = 3f;
        // Off: extra walls turning up in the middle of a build read as a bug, not a reward.
        WallsPerDoor = 0;
        DoorWallItemId = 371;

        LauncherItemId = 519;
        RocketBlastRadius = 4f;
        RocketDirectRadius = 0.5f;
        RocketDirectDamage = 90f;
        RocketSplashDamage = 60f;

        KeyholeItemId = 1520;

        SuppressorGunId = 132;
        SuppressorPlayerDamage = 18f;
        SuppressorGlassMultiplier = 2f;

        FlashbangItemId = 1346;
        StunDamage = 25f;
        StunDamageRadius = 4f;
        StunSeconds = 5f;

        BounceChargeItemId = 1691;
        ElevatorFallSeconds = 3f;


        // 93 is the Forest Transport Heli: seats for a team.
        InsertionVehicleId = 93;
        InsertionSeconds = 60f;
        InsertionHeight = 50f;
        InsertionDistance = 100f;
        InsertionPlatformItemId = 369;

        BedrollItemId = 43132;
        SecondLifeSeconds = 30f;
        SecondLifeHealth = 50;
        MedkitItemId = 15;

        NomadItemId = 391;
        NomadSeconds = 20f;

        // Vanilla impact effects with their own audio: concrete, metal, wood, gravel and
        // flesh. PeekSeek's sound bundle has no footsteps or doors yet; their effect ids
        // go on this list when it does.
        StereoItemId = 1466;
        FakerEffects = [38, 18, 17, 44, 5];
        FakerMinSeconds = 2f;
        FakerMaxSeconds = 6f;
        ClaymoreItemId = 1102;

        // No vanilla effect is a siren; 0 plays nothing until the bundle has one.
        RedSmokeItemId = 266;
        SirenEffect = 0;
        StrikerDelay = 1.2f;
        StrikerRockets = 9;
        StrikerSpacing = 0.15f;
        StrikerRadius = 16f;
        StrikerPlaneSpeed = 180f;
        StrikerPlaneHeight = 70f;
        StrikerSafeRadius = 20f;
        StrikerHeight = 40f;

        // No vanilla effect is a flare over a head either; 0 until the bundle has one.

        MirrorItemId = 1299;

        SupplierItemId = 1374;
        SupplierSeconds = 30f;
        SupplierCap = 3;
        SupplyItemId = 395;

        SlipPlateItemId = 1239;

        SnareItemId = 1113;
        SnareRootSeconds = 3f;

        // 112 is the Bluntforce; the state is left to the game, which loads it.
        SentryFirearm = new RaidItem(112, 1);
    }

    private static RaidOption Option(string name, PowerupClass kind, string blurb, ushort icon, List<RaidItem> items,
                                     Twist twist = Twist.None) {
        return new RaidOption { Name = name, Category = kind, Blurb = blurb, Icon = icon, Items = items, Twist = twist };
    }
}
