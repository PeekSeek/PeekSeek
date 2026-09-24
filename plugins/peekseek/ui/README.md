# PeekSeek's in-game screens

The Unity UIs PeekSeek draws in game, built from one project into two Workshop items:

- **The boards**, one prefab with four panels: the pick board for a round, five tiles
  as the site's match page draws a powerup, a card with the name and a row per item,
  `2x` and its picture, numbered so "I take 2" is something you can say; the footprint
  board before the build, two tiles with the whole
  starting base drawn on each; the swap board while building, the piece you are
  looking at in every other material; and the attack screen, the whole screen dimmed,
  the map of the base being attacked in the middle with a keep on it and a dot on
  every spawn, and the five raid tiles under it. The map is the game's own `Map.png`
  cropped 260 m round the site's anchor and baked in, one per site; the dots are
  baked from the same numbers as the plugin's config, because a position cannot be
  sent to a client. The top-left third of the screen is left empty for the game's
  chat.
- **The match HUD**, along the top of the screen for the whole match: score and clock on
  one block in the middle, drawn over the game's own arena boxes, a face per player to
  either side with their pick on its corner and their health under it, the viewer's own
  team on the left.
- **The build panel**, beside the screen during the build phase: what the team has left
  to lay, a row per kind of piece.
- **The kill feed**, its own item (`Assets/PeekSeekKillFeed/`) so the lobby can carry it
  without the rest: a row per kill at the top right, the weapon a white silhouette.
- **The lobby's bar** (`Assets/PeekSeekKillFeed/Bundles/Effects/LobbyBar/`, in the same item):
  the match bar's block with the queue and the arena's clock on it, and a face per
  player round it by kills, the first on the left, the second on the right, six a side,
  each with its kills on its outer corner; a face at half strength is somebody dead, at
  the spawn or away, and away is marked.
- **The banner, the death card, the scoreboard and the queue screen**, in the same
  item, so both servers draw them: a label over a name a quarter of the way down the
  screen; a card at the bottom saying who killed you and with what, with the respawn
  count in the middle for the lobby; a page of the site with a row per player, the
  PeekSeek mark on its heading, a rank badge on every face and five numbered columns;
  and the site's play page asked in game, gamemode tiles and a region, which is how a
  player queues from the lobby.
- **The tag** (`Assets/PeekSeek/Bundles/Effects/Tag/`): a word, three pips and a word in
  red at the bottom of the screen, which Raid says OFF BASE with.

All of them are PeekSeek Match's (`plugin/Core/Hud/`), driven by whatever gamemode is
running: the bar asks the gamemode for its role markers, its picks and its clock, and
a gamemode asks for a board through `Host.Board`. Nothing here knows which gamemode
that is.

This folder is a Unity project. Everything in it that matters is text you can diff:

```
Assets/PeekSeek/Bundles/MasterBundle.dat                   the bundle the game loads
Assets/PeekSeek/Bundles/Effects/PickBoard/PickBoard.dat    the board effect, with its GUID
Assets/PeekSeek/Bundles/Effects/PickBoard/Effect.prefab    both boards
Assets/PeekSeek/Bundles/Effects/MatchHud/MatchHud.dat      the HUD effect, with its GUID
Assets/PeekSeek/Bundles/Effects/MatchHud/Effect.prefab     the HUD
Assets/PeekSeek/Bundles/Effects/BuildPanel/                the build panel, likewise
Assets/PeekSeek/Bundles/Items/                             the flag and the sentry, as item files
Assets/PeekSeekKillFeed/Bundles/                           the kill feed's bundle and effect
Assets/PeekSeek/Shaders/                                   the silhouette shader and its material
Assets/PeekSeek/Sprites/                                   corners, role icons, the two footprints, the two maps
Assets/PeekSeek/Fonts/                                     Inter and JetBrains Mono, OFL
Assets/Editor/BuildBundle.cs                               turns the above into a mod
tools/unityyaml.py                                         the Unity objects a screen is made of
tools/footprints.py                                        draws a starting base from above
tools/sitemap.py                                           crops the map's own picture round a site
tools/generate.py                                          writes both prefabs and the sprites
tools/WorkshopUpload/                                      publishes the mod
build.ps1                                                  runs the build headless
```

The prefabs and sprites are generated. Change a layout in `generate.py` and run it; the
prefabs are Unity's own YAML, so the Editor opens them like anything else. If a screen is
ever changed inside Unity instead, that edit wins and the script is stale: bring the
change back into the script or delete it, but do not leave the two disagreeing.

## Two screens, two design languages

The site and the stream overlay are different systems on purpose (see the
`peekseek-design` skill), and each screen follows the one it is closest to.

**The boards are pages of the site.** They are something you click, so they are built
like the kit picker, and none of them is see-through: a menu is read while standing
still, and one you can see the map through is one nobody can read. The `.mainElement`
panel (`#0a0a0a`, radius 8, padding 20), tiles in
the sub-panel grey (`#1a1a1a`), the same selected treatment (a `#333` fill and a
"Selected" label in `#aae0ff`, not a border), `#fff` text with `#aaa` for what is read
second, and the site's 10px gaps. The timer turns the overlay's jeopardy red for the
last five seconds, because that is the one thing on the board that is jeopardy. The
footprint tiles show the whole base the plugin will lay, floors, pillars and flag, drawn
by `footprints.py` from the same numbers as `BaseLayout.cs`.

**The HUD is the concept** (`conceptArtOverlay.png` at the repo root) in the overlay's
language: `#f2f2f2` ink, one flat `#0f0f0f` block in the middle reading
`team 0 [castle] 0:30 [swords] 1 team2`, a blue castle beside whoever holds the flag
this round and red swords beside whoever is after it, both drawn by `unityyaml.py` so
they are nobody else's. Faces sit on nothing to either side, 50px like the site's
`.userProfile` chip, and a face at half strength is a player out of the round. No names
and no guns. Sizes are px at UI scale 1.

**The score line is a layout row.** Not every gamemode has a castle and swords, or a
clock, so the block is not a list of pixel positions: it is uGUI's own
HorizontalLayoutGroup, each field at its own width, and a field that is switched off is
left out of the run, so the rest close up round it. On the arena the line is
`team 0  1 team2`, centred, with no hole where a marker would be.

**Under every face, a health bar**, and on its outer bottom corner, the pick. The bar is
a rounded track, 50 by 6, three under the face, with ten steps of fill inside it in
green and again in amber; the plugin lights the steps and picks the colour, green above
half and amber from half down, because it can switch an image on but not recolour it.
The track is a mask, so the steps come out with its rounded ends. A whole face has no
bar and neither does a dead one, and a player only sees the bars of their own team;
spectators see them all. The pick is a chip of the block's fill, 20px, mostly on the
face and three pixels past its edge, tilted ten degrees outward, with the item's picture
inset; a face with no pick has no chip.

The block is 360 by 50, ten down, because that is the game's rect. Unturned draws two
boxes of its own at the top of every Arena map, from the map type the client reads out
of its own `Level.dat`: the arena message and the round number, together 360 wide at
the centre of the screen, from 10 down to 60, with the compass under them from 70 to
120. Nothing a server sends can switch them off, so the block is exactly their rect and
stops there; the compass is left showing. Its corners are the site's 8px, and the
game's boxes are drawn a pixel inside their rect with a radius of nine, so nothing of
theirs shows past ours. Faces are the block's height and sit beside it on its line. The
clock is monospaced because a proportional one shuffled the whole readout every second,
and TextMeshPro cannot ask a font for tabular figures.

**The build panel and the kill feed are the overlay too.** The panel is the block's fill
at 90% with the site's corner, a row per piece: picture, count in ink, name in muted.
Its one line of text says that salvaging a piece is how you change it, because nothing
else on screen does. A feed row is the block's fill with a red hairline round it when
the viewer is in it, done as a red rounded rectangle one pixel proud of the black one,
since uGUI has no border to set. A death nobody dealt, or one the server cannot put an
item to, is a shorter row: a skull and the name. The weapon is drawn with a shader of our own that
takes only the sprite's alpha, so the site's coloured icons come out as white shapes.

**The font is Inter**, with JetBrains Mono for the clock. The site asks for the system
UI font, which cannot be shipped in a mod; Inter is the open one that reads the same.
The game only replaces a TextMeshPro font it recognises as TMP's stock one, so ours are
left alone.

## Why it is shaped like this

Everything here was read out of `EffectManager.cs` in the U3-SDK, because the obvious
guesses about how plugin UIs work are wrong.

**It is uGUI with TextMeshPro, not UI Toolkit.** `ReceiveUIEffectText` does
`GetComponent<TextMeshProUGUI>()` on the child it finds; a UXML document is never looked
at. It has to be TMP rather than the legacy `Text`, because the font fixup only runs for
TMP and only when the prefab has no legacy `Text` at all.

**Text goes on the node the plugin names, not on a child of it.**
`ReceiveUIEffectText` finds the child by name and does `GetComponent<TextMeshProUGUI>()`
on it: a container whose text sits one level down takes the message and drops it
silently, and the screen keeps whatever the prefab was saved with. That is how every
face on the lobby's bar showed the placeholder `12` for its kills.

**Buttons need nothing but a name.** `createUIEffect` walks every `Button` in the prefab
and attaches a `PluginButtonListener` to it, and the name that reaches the server is the
Button's GameObject name. So the tiles are `BoardPick0` to `BoardPick4` and carry no
script. The prefix is deliberate: `onEffectButtonClicked` is global, and a click on some
other plugin's UI would otherwise land in this board's handler.

**Pictures are `Image`, loaded by URL.** `sendUIEffectImageURL` wants a
`UnityEngine.UI.Image` (a `RawImage` is refused with a log line), adds a `WebImage` to it
and disables the image until the download lands. Item icons come from the site, which
already serves every item at `/static/unturned/{id}.png`; avatars come from the site's
`GET /server/v1/players/{steam64}`, which carries the name and picture it shows
everywhere else. A `Mask` on the avatar's frame gives a square download the site's
rounded corners. An `Image` that starts disabled shows nothing until its picture arrives.

**Rounded corners are sprites.** uGUI draws a flat rectangle unless an `Image` has a
sprite, so `generate.py` writes small PNGs (a rounded square, the block's bottom
corners, the castle and swords) and the `.meta` that imports each as a nine-sliced
sprite under a GUID the prefabs reference. The two footprints are sprites too, plain
rather than sliced, drawn at twice the size they are shown at.

**Half transparency is two images.** Nothing a server sends can set an alpha, but it
can choose which of two objects is active, so a face is the same download twice, one
image at full strength and one at half, and the plugin shows the half one while that
player is out of the round. The client caches pictures by URL, so the second costs
nothing.

**Nothing from the SDK is needed.** `UnturnedCanvasScaler` is added at spawn if the
canvas lacks one, and drives a `CanvasScaler` in ConstantPixelSize mode from the
player's UI scale setting. That is why the whole SDK need not be cloned to build this:
a bundle holds its prefabs, the sprites and fonts they reference, TMP's SDF shader, and
script references from packages every project has.

**The cursor is the plugin's job.** Unturned only shows a mouse cursor while a plugin has
set `EPluginWidgetFlags.Modal` on the player. `PickBoard` sets it when the board opens
and clears it the moment they pick, or when the board closes.

## What the plugin sends

By child name, via `sendUIEffectText`, `sendUIEffectImageURL` and
`sendUIEffectVisibility`. Children are found by `Transform.Find` and then a recursive
search, so the names only have to be unique and spelt as the plugin spells them.

The boards (`PickBoard.cs`, key `PickUIKey`). `{P}` is the panel, `Pick` for a round's
options, `Shape` for the footprint or `Swap` for a piece's material, and every panel
starts hidden:

| Child | Component | What goes in it |
| --- | --- | --- |
| `{P}` | GameObject | the panel, shown for the board being asked |
| `{P}_Headline` | TMP text | `Round 2. You are defending North.` |
| `{P}_Timer` | TMP text | seconds left, red under six |
| `Board{P}{n}` | Button | the tile; hidden past the options offered |
| `{P}{n}_Name` | TMP text | the option's name |
| `{P}{n}_Blurb` | TMP text | one short line under the name |
| `{P}{n}_Taken` | GameObject | the selected fill and label, on the picked tile |
| `Pick{n}_Icon`, `Swap{n}_Icon`, `Attack{n}_Icon` | `Image` | the option's picture; the footprint tiles carry their own |
| `Attack_Map{s}` | GameObject | the map of site `s`, with its own dots; shown for the site under attack |
| `Spawn{s}{n}_On` | GameObject | the white dot, on the spawn this viewer chose |
| `Spawn{s}{n}_Face{k}` | `Image` | a teammate coming in at that dot, up to five, shown while there is one |

The attack screen has no headline. And back, via `onEffectButtonClicked`: `BoardPick0`
to `BoardPick4`, `BoardShape0` and `BoardShape1`, `BoardSwap0` to `BoardSwap5`,
`BoardAttack0` to `BoardAttack4`, and the dots, `BoardSpawn{s}{n}`. The attack screen
stays up until both its answers are in, lit where it has been answered.

The HUD (`Hud/MatchHud.cs`, key `MatchHudKey`; `L` is the viewer's team, `R` the other):

| Child | Component | What goes in it |
| --- | --- | --- |
| `Team0_Name`, `Team1_Name` | TMP text | team names, left and right of the score |
| `Score0`, `Score1` | TMP text | rounds won |
| `Castle0`, `Swords0`, `Castle1`, `Swords1` | GameObject | the role beside each score, or nothing |
| `Timer` | TMP text | `m:ss`, on while the gamemode has a clock |
| `L{n}`, `R{n}` | GameObject | a face, shown only while somebody is in that seat |
| `L{n}_Avatar` | `Image` | the player's Steam picture, on while they are in the round |
| `L{n}_Faded` | `Image` | the same picture at half strength, on while they are out |
| `L{n}_Pick` | GameObject | the chip on the face's corner, on while there is a pick |
| `L{n}_Pick_Icon` | `Image` | the item they took this round |
| `L{n}_Health` | GameObject | the bar under the face, on while it has something to say |
| `L{n}_HealthGreen`, `L{n}_HealthAmber` | GameObject | which colour the bar is in |
| `L{n}_Green{k}`, `L{n}_Amber{k}` | GameObject | step `k` of ten, lit up to the health |

The build panel (`Hud/BuildPanel.cs`, key `BuildPanelKey`), sixteen rows:

| Child | Component | What goes in it |
| --- | --- | --- |
| `Row{n}` | GameObject | a row, shown while there is a piece for it |
| `Row{n}_Icon` | `Image` | the piece's picture |
| `Row{n}_Count` | TMP text | how many are left |
| `Row{n}_Name` | TMP text | the piece's name |

The kill feed (`Hud/KillFeed.cs`, key `KillFeedKey`), six rows, newest at the top:

| Child | Component | What goes in it |
| --- | --- | --- |
| `Row{n}` | GameObject | a row, shown while there is a kill for it; as wide as its parts |
| `Row{n}_Killer`, `Row{n}_Victim` | TMP text | names, as wide as they are; the killer off for a death nobody dealt |
| `Row{n}_Streak` | TMP text | `II` to `X`, from the second kill of a life, off otherwise |
| `Row{n}_Weapon` | `Image` | the weapon, drawn as a silhouette, off when the game names no item |
| `Row{n}_Headshot` | GameObject | the skull after the weapon, on for a headshot |
| `Row{n}_Mine` | GameObject | the red hairline, on when the viewer is either name |

The banner (`Hud/Banner.cs`): `Banner_Label`, `Banner_Name`, `Banner_Line`, TMP text.
The death card (`Hud/DeathCard.cs`): `Card` shown or not, `Card_Label`, `Card_Killer`,
`Card_Weapon` (a silhouette, off when the game names no item), `Card_Headshot`,
`Card_Distance`, `Card_Line`; and `Respawn` with `Respawn_Count`. The scoreboard
(`Hud/Scoreboard.cs`): `Board_Title`, `Board_Sub`, `Board_Timer`, `Head_A` to `Head_E`,
and twelve rows of `Row{n}`, `Row{n}_Me`, `Row{n}_Head` for a heading row, `Row{n}_Body`
with `Row{n}_Avatar`, `Row{n}_Rank` and its `Row{n}_Rank_Icon`, `Row{n}_Name`,
`Row{n}_Tag` and `Row{n}_A` to `Row{n}_E`. Its panel is a share of the screen rather
than a fixed width, and everything in a row hangs off the right edge or off the face,
so a wider monitor gets a wider board and nothing runs off it. The queue screen:
`Queue_Headline`, `Queue_Line`, `Queue_Go`, `Queue_No`, `Queue_Hint`, `Mode{n}_Name`,
`Mode{n}_Blurb`, `Mode{n}_On`, `Region{n}_Name`, `Region{n}_On`, and the buttons
`BoardMode{n}`, `BoardRegion{n}`, `BoardQueue` and `BoardQueueNo`. The tag (`Hud/Tag.cs`): `Tag_Text`, `Tag_Pip0` to
`Tag_Pip2`, `Tag_Hurt`. The bar has two more per face, `L{n}_Gone` and `L{n}_Ping`, and
the clock's second shape, `Clock` with `Clock_Phase` and `Clock_Timer`, shown instead of
`Timer` when there is a word to put over it. The lobby's block is `State`,
`State_Timer`, `Gap`, `Arena` and `Arena_Timer`; its faces carry `L{n}_Kills` and
`L{n}_Away`. The attack screen adds `Attack_Headline`, `Attack_Line`, `Attack_Site` and
a `Spawn{s}{n}_Label` under every dot; every powerup tile has a `_Blurb`.

`MatchHud.Show` redraws the whole bar for every viewer on every change; the tick, four
times a second, sends only the clock and any health that moved, against a record of
what each viewer was last sent, and a redraw resets that record. An earlier version
sent only what changed for everything and its record outlived a reconnect, so a player
who came back got patches to a HUD they no longer had. Pictures are cached by URL on
the client, so a redraw downloads nothing.

## Building it

1. Install **Unity 2022.3.62f3** through Unity Hub, with the **Linux Build Support** and
   **Mac Build Support** modules. The server runs Linux and reads its own bundle; without
   a platform's bundle there is nothing for that platform to load. Another Editor version
   builds a bundle too, and the 2021.3 test builds loaded in the 2022.3 game, but the
   game is 2022.3.62f3 and the SDK says to match it.
2. `.\build.ps1`

That runs the Editor headless and writes one folder per mod under `Build\`, one for
every folder under `Assets\` with a `Bundles\MasterBundle.dat`:

```
Build/PeekSeek/
  MasterBundle.dat
  peekseek.masterbundle             Windows
  peekseek_linux.masterbundle
  peekseek_mac.masterbundle
  peekseek.masterbundle.hash        all three hashes, for the server
  Effects/PickBoard/PickBoard.dat
  Effects/MatchHud/MatchHud.dat
  Effects/BuildPanel/BuildPanel.dat
  Items/PeekSeekFlag/PeekSeekFlag.dat        with its English.dat
  Items/PeekSeekSentry/PeekSeekSentry.dat
Build/PeekSeekKillFeed/
  MasterBundle.dat
  peekseek_killfeed.masterbundle    and the same per platform
  Effects/KillFeed/KillFeed.dat
```

`BuildBundle.cs` is `EditorAssetBundleHelper.Build` from the SDK, minus the SDK: the same
`BuildPipeline` call per platform with the same two options, the stray folder-named
bundle removed, the `.hash` written only when all three platforms exist. Before that it
unpacks TMP's Essential Resources itself (`AssetDatabase.ImportPackage` did nothing
headless, and a font asset cannot be made without TMP's shader) and makes the font
assets from the `.ttf` files, pinned to the GUIDs the prefabs reference. Both are build
output and git ignores them.

## Getting it in front of a player

Each folder is a mod. They are Workshop items **3803930118** (PeekSeek UI) and
**3804490030** (PeekSeek Kill Feed), both unlisted. A match server lists both in
`File_IDs` in `Servers/unturned/WorkshopDownloadConfig.json`, the lobby only the kill
feed, so the server fetches them at boot and every client is handed them on join. After
a rebuild:

```
cd tools/WorkshopUpload
dotnet run -c Release -- ../../Build/PeekSeek --update 3803930118 --changes "what changed"
dotnet run -c Release -- ../../Build/PeekSeekKillFeed --update 3804490030 --changes "what changed"
```

That goes through the Steam client logged in on this machine; there is no password, key
or token involved, because Steam takes Workshop uploads no other way. Leave off
`--update` to create a new item, with `--title`. The server sees an update within ten
minutes and restarts itself for it (`Should_Monitor_Updates`).

To test a build before uploading it, copy the folder to
`Servers/unturned/Bundles/PeekSeek/` on the server and to
`Maps/Alpha Valley/Bundles/PeekSeek/` in your own Unturned install, and restart both.
The client path is the map's, not `Sandbox/`: while connected to a server a client only
sees assets from the core game, the map, and the server's workshop files
(`Assets.ApplyServerAssetMapping`), so a `Sandbox/` mod is there in singleplayer and gone
the moment you join. Mod folders are scanned only when the game starts.

The plugin's `PickUIEffectGUID` and `MatchHudEffectGUID` default to the GUIDs in the two
`.dat` files, so a server with the mod draws both screens without a config change, and
one without logs a line and every board answers itself. A server config written before
a default existed has an empty element and needs the value pasted in.

A client without the mod is **kicked** the moment a screen is sent to them:
`missing asset: "PickBoard"`, from the game's asset integrity check, before the plugin
gets a say. That is what the Workshop id in `WorkshopDownloadConfig.json` is for.
