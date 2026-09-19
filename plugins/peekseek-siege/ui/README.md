# PeekSeek's in-game screens

The Unity UIs PeekSeek draws in game, built from one project into two Workshop items:

- **The boards**, one prefab with three panels: the pick board for a round, five tiles
  with the item's picture, its name, one line under it, and a number so `/pick 3` reads
  as the same board; the footprint board before the build, two tiles with the whole
  starting base drawn on each; and the swap board while building, the piece you are
  looking at in every other material.
- **The match HUD**, along the top of the screen for the whole match: score and clock on
  one block in the middle, a face per player to either side with their pick hanging off
  its corner, the viewer's own team on the left.
- **The build panel**, beside the screen during the build phase: what the team has left
  to lay, a row per kind of piece.
- **The kill feed**, its own item (`Assets/PeekSeekKillFeed/`) for its own plugin: a row
  per kill at the top right, the weapon a white silhouette.

Any gamemode can drive them; the plugin's `MatchHud` takes plain data and knows nothing
about Siege.

This folder is a Unity project. Everything in it that matters is text you can diff:

```
Assets/PeekSeek/Bundles/MasterBundle.dat                   the bundle the game loads
Assets/PeekSeek/Bundles/Effects/PickBoard/PickBoard.dat    the board effect, with its GUID
Assets/PeekSeek/Bundles/Effects/PickBoard/Effect.prefab    both boards
Assets/PeekSeek/Bundles/Effects/MatchHud/MatchHud.dat      the HUD effect, with its GUID
Assets/PeekSeek/Bundles/Effects/MatchHud/Effect.prefab     the HUD
Assets/PeekSeek/Bundles/Effects/BuildPanel/                the build panel, likewise
Assets/PeekSeekKillFeed/Bundles/                           the kill feed's bundle and effect
Assets/PeekSeek/Shaders/                                   the silhouette shader and its material
Assets/PeekSeek/Sprites/                                   corners, role icons, the two footprints
Assets/PeekSeek/Fonts/                                     Inter and JetBrains Mono, OFL
Assets/Editor/BuildBundle.cs                               turns the above into a mod
tools/unityyaml.py                                         the Unity objects a screen is made of
tools/footprints.py                                        draws a starting base from above
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
like the kit picker: the `.mainElement` panel (`#0a0a0a`, radius 8, padding 20), tiles in
the sub-panel grey (`#1a1a1a`), the same selected treatment (a `#333` fill and a
"Selected" label in `#aae0ff`, not a border), `#fff` text with `#aaa` for what is read
second, and the site's 10px gaps. The timer turns the overlay's jeopardy red for the
last five seconds, because that is the one thing on the board that is jeopardy. The
footprint tiles show the whole base the plugin will lay, floors, pillars and flag, drawn
by `footprints.py` from the same numbers as `BaseLayout.cs`.

**The HUD is the concept** (`conceptArtOverlay.png` at the repo root) in the overlay's
language: `#f2f2f2` ink, `#8a8a8a` muted, red only for match point and final, one flat
`#0f0f0f` block in the middle reading `team 0 [shield] 0:30 [swords] 1 team2`, the
shield beside whoever holds the flag this round and the swords beside whoever is after
it. Faces sit on nothing to either side, 50px like the site's `.userProfile` chip, the
pick tilted off the outer bottom corner, and a face at half strength is a player out of
the round. No names and no guns. Sizes are px at UI scale 1.

The block is exactly as tall as a face. Unturned draws its arena text and compass on
every Arena map, from the map type the client reads out of its own `Level.dat`, and
nothing a server sends can switch either off; the answer to that is the map (see the
plugin's README), not the HUD, so there is nothing here to cover. The clock is
monospaced because a proportional one shuffled the whole readout every second, and
TextMeshPro cannot ask a font for tabular figures; the small label under it says what
the clock is counting.

**The build panel and the kill feed are the overlay too.** The panel is the block's fill
at 90% with the site's corner, a row per piece: picture, count in ink, name in muted.
Its hint carries `<plugin_0/>`, which the game replaces with the player's own binding of
the first plugin key when the effect is spawned, and only then, which is why the hint is
baked in rather than sent. A feed row is the block's fill with a red hairline round it
when the viewer is in it, done as a red rounded rectangle one pixel proud of the black
one, since uGUI has no border to set. The weapon is drawn with a shader of our own that
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

**Buttons need nothing but a name.** `createUIEffect` walks every `Button` in the prefab
and attaches a `PluginButtonListener` to it, and the name that reaches the server is the
Button's GameObject name. So the tiles are `SiegePick0` to `SiegePick4` and carry no
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
corners, the shield and swords) and the `.meta` that imports each as a nine-sliced
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
| `Siege{P}{n}` | Button | the tile; hidden past the options offered |
| `{P}{n}_Name` | TMP text | the option's name |
| `{P}{n}_Blurb` | TMP text | one short line under the name |
| `{P}{n}_Taken` | GameObject | the selected fill and label, on the picked tile |
| `Pick{n}_Icon`, `Swap{n}_Icon` | `Image` | the option's picture; the footprint tiles carry their own |

And back, via `onEffectButtonClicked`: `SiegePick0` to `SiegePick4`, `SiegeShape0` and
`SiegeShape1`, or `SiegeSwap0` to `SiegeSwap5`.

The HUD (`Hud/MatchHud.cs`, key `MatchHudKey`; `L` is the viewer's team, `R` the other):

| Child | Component | What goes in it |
| --- | --- | --- |
| `Team0_Name`, `Team1_Name` | TMP text | team names, left and right of the score |
| `Score0`, `Score1` | TMP text | rounds won |
| `Shield0`, `Swords0`, `Shield1`, `Swords1` | GameObject | the role beside each score |
| `Timer` | TMP text | `m:ss`, once a second |
| `Mid` | TMP text | `Round 3`, `Match point` (red), `Final` (red) |
| `L{n}`, `R{n}` | GameObject | a face, shown only while somebody is in that seat |
| `L{n}_Avatar` | `Image` | the player's Steam picture, on while they are in the round |
| `L{n}_Faded` | `Image` | the same picture at half strength, on while they are out |
| `L{n}_Pick` | `Image` | the option they took this round |

The build panel (`Hud/BuildPanel.cs`, key `BuildPanelKey`), sixteen rows:

| Child | Component | What goes in it |
| --- | --- | --- |
| `Row{n}` | GameObject | a row, shown while there is a piece for it |
| `Row{n}_Icon` | `Image` | the piece's picture |
| `Row{n}_Count` | TMP text | how many are left |
| `Row{n}_Name` | TMP text | the piece's name |

The kill feed (`plugins/peekseek-killfeed`, key `EffectKey`), six rows, newest at the top:

| Child | Component | What goes in it |
| --- | --- | --- |
| `Row{n}` | GameObject | a row, shown while there is a kill for it |
| `Row{n}_Killer`, `Row{n}_Victim` | TMP text | names, cut with an ellipsis |
| `Row{n}_Streak` | TMP text | `II` to `X`, from the second kill of a life |
| `Row{n}_Weapon` | `Image` | the weapon, drawn as a silhouette |
| `Row{n}_Mine` | GameObject | the red hairline, on when the viewer is either name |

`MatchHud.Show` redraws the whole bar for every viewer on every change, and only the
clock is sent on its own once a second. It used to send just what changed and keep a
record per viewer, and the record outlived a reconnect, so a player who came back got
patches to a HUD they no longer had. Pictures are cached by URL on the client, so a
redraw downloads nothing.

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
**3804490030** (PeekSeek Kill Feed), both unlisted, and the debug server lists them in
`File_IDs` in `Servers/unturned/WorkshopDownloadConfig.json`, so the server fetches them
at boot and every client is handed them on join. After a rebuild:

```
cd tools/WorkshopUpload
dotnet run -c Release -- ../../Build/PeekSeek --update 3803930118 --changes "what changed"
dotnet run -c Release -- ../../Build/PeekSeekKillFeed --update 3804490030 --changes "what changed"
```

That goes through the Steam client logged in on this machine; there is no password, key
or token involved, because Steam takes Workshop uploads no other way. Leave off
`--update` to create a new item, with `--title`. The same tool uploads the map,
`--type Map` (see `../map/make.py`); that is item **3804490205**. The server sees an
update within ten minutes and restarts itself for it (`Should_Monitor_Updates`).

To test a build before uploading it, copy the folder to
`Servers/unturned/Bundles/PeekSeek/` on the server and to
`Maps/Alpha Valley Siege/Bundles/PeekSeek/` in your own Unturned install, and restart both.
The client path is the map's, not `Sandbox/`: while connected to a server a client only
sees assets from the core game, the map, and the server's workshop files
(`Assets.ApplyServerAssetMapping`), so a `Sandbox/` mod is there in singleplayer and gone
the moment you join. Mod folders are scanned only when the game starts.

The plugin's `PickUIEffectGUID` and `MatchHudEffectGUID` default to the GUIDs in the two
`.dat` files, so a server with the mod draws both screens without a config change, and
one without logs a line and draws the board to chat. A server config written before a
default existed has an empty element and needs the value pasted in.

The chat fallback covers a *server* without the mod, and nothing else. Once the server
has it, a client that does not is **kicked** the moment a screen is sent to them:
`missing asset: "PickBoard"`, from the game's asset integrity check, before the plugin
gets a say. That is what the Workshop id in `WorkshopDownloadConfig.json` is for.
