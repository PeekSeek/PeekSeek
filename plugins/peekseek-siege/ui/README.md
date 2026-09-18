# PeekSeek's in-game screens

Two Unity UIs the plugin draws, built into one Workshop item:

- **The option board**, for the pick phase: five tiles with the item's picture, its
  name, one line under it, and a number so `/pick 3` reads as the same board.
- **The match HUD**, along the top of the screen for the whole match: score and clock in
  the middle, a card per player to either side with their face, name, gun and pick, the
  viewer's own team on the left.

Any gamemode can drive them; the plugin's `MatchHud` takes plain data and knows nothing
about Siege.

This folder is a Unity project. Everything in it that matters is text you can diff:

```
Assets/PeekSeek/Bundles/MasterBundle.dat                   the bundle the game loads
Assets/PeekSeek/Bundles/Effects/PickBoard/PickBoard.dat    the board effect, with its GUID
Assets/PeekSeek/Bundles/Effects/PickBoard/Effect.prefab    the board
Assets/PeekSeek/Bundles/Effects/MatchHud/MatchHud.dat      the HUD effect, with its GUID
Assets/PeekSeek/Bundles/Effects/MatchHud/Effect.prefab     the HUD
Assets/PeekSeek/Sprites/                                   rounded corners and the notch fillets
Assets/PeekSeek/Fonts/                                     Inter, Regular and Bold, OFL
Assets/Editor/BuildBundle.cs                               turns the above into a mod
tools/unityyaml.py                                         the Unity objects a screen is made of
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

**The board is a page of the site.** It is something you click, so it is built like the
kit picker: the `.mainElement` panel (`#0a0a0a`, radius 8, padding 20), tiles in the
sub-panel grey (`#1a1a1a`), the same selected treatment (a `#333` fill and a "Selected"
label in `#aae0ff`, not a border), `#fff` text with `#aaa` for what is read second, and
the site's 10px gaps. The timer turns the overlay's jeopardy red for the last five
seconds, because that is the one thing on the board that is jeopardy.

**The HUD is the overlay.** One flat fill (`#0f0f0f` at 90%), `#f2f2f2` ink, `#8a8a8a`
muted, red only for match point and final. The notch is the overlay's notch, inverse
fillets included: `[team][score] ROUND 3 [score][team]`, with the clock under the round
label. The cards are the overlay's cards with the site's avatar chip added: a name line
over a loadout run, gun first, then the pick. Out of the round fades to 30% rather than
disappearing, so the row never reflows mid-round. Sizes are the overlay's `vw` at 1920,
which is what the game draws at UI scale 1.

**The font is Inter** for both. The site asks for the system UI font, which cannot be
shipped in a mod; Inter is the open one that reads the same. The game only replaces a
TextMeshPro font it recognises as TMP's stock one, so ours is left alone.

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
sprite, so `generate.py` writes small PNGs (a rounded square, the notch's bottom corners,
the two fillets) and the `.meta` that imports each as a nine-sliced sprite under a GUID
the prefabs reference. The fillets are the overlay's `M0 0 L10 0 L10 10 A10 10 0 0 0 0 0`
path as pixels: full width at the top edge, nothing at depth r.

**Nothing from the SDK is needed.** `UnturnedCanvasScaler` is added at spawn if the
canvas lacks one, and drives a `CanvasScaler` in ConstantPixelSize mode from the
player's UI scale setting. That is why the whole SDK need not be cloned to build this:
the bundle holds two prefabs, five sprites, two fonts, TMP's SDF shader, and script
references from packages every project has.

**The cursor is the plugin's job.** Unturned only shows a mouse cursor while a plugin has
set `EPluginWidgetFlags.Modal` on the player. `PickBoard` sets it when the board opens
and clears it the moment they pick, or when the board closes.

## What the plugin sends

By child name, via `sendUIEffectText`, `sendUIEffectImageURL` and
`sendUIEffectVisibility`. Children are found by `Transform.Find` and then a recursive
search, so the names only have to be unique and spelt as the plugin spells them.

The board (`PickBoard.cs`, key `PickUIKey`):

| Child | Component | What goes in it |
| --- | --- | --- |
| `Headline` | TMP text | `Round 2. You are defending North.` |
| `Timer` | TMP text | seconds left, red under six |
| `Option{n}_Icon` | `Image` | the option's picture |
| `Option{n}_Name` | TMP text | the option's name |
| `Option{n}_Blurb` | TMP text | one short line under the name |
| `Option{n}_Taken` | GameObject | the selected fill and label, on the picked tile |

And back, via `onEffectButtonClicked`: `SiegePick0` to `SiegePick4`.

The HUD (`Hud/MatchHud.cs`, key `MatchHudKey`; `L` is the viewer's team, `R` the other):

| Child | Component | What goes in it |
| --- | --- | --- |
| `Team0_Name`, `Team1_Name` | TMP text | team names, left and right of the score |
| `Score0`, `Score1` | TMP text | rounds won |
| `Mid` | TMP text | `Round 3`, `Match point` (red), `Final` (red) |
| `Timer` | TMP text | `m:ss`, once a second |
| `L{n}`, `R{n}` | GameObject | a card, shown only while somebody is in that seat |
| `L{n}_Avatar` | `Image` | the player's Steam picture |
| `L{n}_Name` | TMP text | the player's name |
| `L{n}_Gun` | `Image` | the gun in their hands |
| `L{n}_Pick` | `Image` | the option they took this round |
| `L{n}_Out` | GameObject | the fade, on while they are out of the round |

`MatchHud.Show` sends a whole state once per viewer and only the fields that changed
after that, the same mount-versus-patch split the overlay uses; a full resend every
second would reload every avatar every second.

## Building it

1. Install **Unity 2022.3.62f3** through Unity Hub, with the **Linux Build Support** and
   **Mac Build Support** modules. The server runs Linux and reads its own bundle; without
   a platform's bundle there is nothing for that platform to load. Another Editor version
   builds a bundle too, and the 2021.3 test builds loaded in the 2022.3 game, but the
   game is 2022.3.62f3 and the SDK says to match it.
2. `.\build.ps1`

That runs the Editor headless and writes `Build\PeekSeekUI\`:

```
MasterBundle.dat
peekseek.masterbundle             Windows
peekseek_linux.masterbundle
peekseek_mac.masterbundle
peekseek.masterbundle.hash        all three hashes, for the server
Effects/PickBoard/PickBoard.dat
Effects/MatchHud/MatchHud.dat
```

`BuildBundle.cs` is `EditorAssetBundleHelper.Build` from the SDK, minus the SDK: the same
`BuildPipeline` call per platform with the same two options, the stray folder-named
bundle removed, the `.hash` written only when all three platforms exist. Before that it
unpacks TMP's Essential Resources itself (`AssetDatabase.ImportPackage` did nothing
headless, and a font asset cannot be made without TMP's shader) and makes the two Inter
font assets from the `.ttf` files, pinned to the GUIDs the prefabs reference. Both are
build output and git ignores them.

## Getting it in front of a player

That folder is the mod. It is Workshop item **3803930118** (unlisted), and the debug
server lists it in `File_IDs` in `Servers/unturned/WorkshopDownloadConfig.json`, so the
server fetches it at boot and every client is handed it on join. After a rebuild:

```
cd tools/WorkshopUpload
dotnet run -c Release -- ../../Build/PeekSeekUI --update 3803930118 --changes "what changed"
```

That goes through the Steam client logged in on this machine; there is no password, key
or token involved, because Steam takes Workshop uploads no other way. Leave off
`--update` to create a new item. The server sees the update within ten minutes and
restarts itself for it (`Should_Monitor_Updates`).

To test a build before uploading it, copy the folder to
`Servers/unturned/Bundles/PeekSeekUI/` on the server and to
`Maps/Alpha Valley/Bundles/PeekSeekUI/` in your own Unturned install, and restart both.
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
