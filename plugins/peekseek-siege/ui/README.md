# The option board

The Unity UI the plugin draws the pick phase with: five tiles, each with the item's
picture, its name, one line under it, and a number so `/pick 3` reads as the same board.

This folder is a Unity project. Everything in it that matters is text you can diff:

```
Assets/PeekSeekSiege/Bundles/MasterBundle.dat                    the bundle the game loads
Assets/PeekSeekSiege/Bundles/Effects/PickBoard/PickBoard.dat     the effect, with its GUID
Assets/PeekSeekSiege/Bundles/Effects/PickBoard/Effect.prefab     the board itself
Assets/Editor/BuildPickBoardBundle.cs                            turns the above into a mod
tools/generate_prefab.py                                         writes Effect.prefab
build.ps1                                                        runs the build headless
```

`Effect.prefab` is generated. Change the layout in `generate_prefab.py` and run it; the
prefab is Unity's own YAML, so the Editor opens it like anything else. If somebody edits
the board inside Unity instead, that edit wins and the script is stale: bring the change
back into the script or delete it, but do not leave the two disagreeing.

## Why it is shaped like this

Everything here was read out of `EffectManager.cs` in the U3-SDK, because the two obvious
guesses about how plugin UIs work are both wrong.

**It is uGUI with TextMeshPro, not UI Toolkit.** `ReceiveUIEffectText` does
`GetComponent<TextMeshProUGUI>()` on the child it finds; a UXML document is never looked
at. And it has to be TMP rather than the legacy `Text`: the game only swaps in its own
font (`LiberationSans SDF with CJK`) for TMP components, and only when the prefab has no
legacy `Text` in it at all. The font the prefab references is TMP's stock one and is
never what a player sees.

**Buttons need nothing but a name.** `createUIEffect` walks every `Button` in the prefab
and attaches a `PluginButtonListener` to it, and the name that reaches the server is the
Button's GameObject name. So the tiles are named `SiegePick0` to `SiegePick4` and carry no
script at all. The prefix is deliberate: `onEffectButtonClicked` is global, and a click on
some other plugin's UI would otherwise land in this board's handler.

**Pictures are `Image`, loaded by URL.** `sendUIEffectImageURL` wants a
`UnityEngine.UI.Image` (a `RawImage` is refused with a log line), adds a `WebImage` to it,
and disables the image until the download lands. The icons come from the site, which
already serves every item at `/static/unturned/{id}.png` for the loadout editor, so the
tile shows the same picture a player chose their kit from. The `Image` starts disabled so
there is no white square before the first frame.

**Nothing from the SDK is needed.** `UnturnedCanvasScaler` is added at spawn if the
canvas lacks one, and it drives a `CanvasScaler` in ConstantPixelSize mode from the
player's UI scale setting. That is the only reason the whole SDK need not be cloned to
build this: the bundle contains a prefab, five script references from packages every
project has, and nothing else.

**The cursor is the plugin's job.** Unturned only shows a mouse cursor while a plugin has
set `EPluginWidgetFlags.Modal` on the player. `PickBoard` sets it when the board opens
and clears it the moment they pick, or when the board closes, whichever is first.

## What the plugin sends

Once, when the board opens: `SendUIEffect(asset, PickUIKey, connection, true)`. Then by
child name, via `sendUIEffectText`, `sendUIEffectImageURL` and `sendUIEffectVisibility`.
Children are found by `Transform.Find` and then a recursive search, so the names only
have to be unique and spelt as `PickBoard.cs` spells them.

| Child | Component | What goes in it |
| --- | --- | --- |
| `Headline` | TMP text | `Round 2. You are defending North.` |
| `Timer` | TMP text | seconds left, rewritten once a second |
| `Option{n}_Icon` | `Image` | the option's picture, from `ItemIconURL` |
| `Option{n}_Name` | TMP text | the option's name |
| `Option{n}_Blurb` | TMP text | one short line under the name |
| `Option{n}_Taken` | GameObject | shown on the picked tile, hidden on the rest |

And back, via `onEffectButtonClicked`: `SiegePick0` to `SiegePick4`.

Which item a tile pictures is the option's first item unless the option sets `Icon`,
because a grenade is a poor picture of a watchtower.

## Building it

1. Install **Unity 2022.3.62f3** through Unity Hub, with the **Linux Build Support** and
   **Mac Build Support** modules. The server runs Linux and reads its own bundle; without
   a platform's bundle there is nothing for that platform to load. Another Editor version
   builds a bundle too, but the game is 2022.3.62f3 and the SDK says to match it.
2. `.\build.ps1`

That runs the Editor headless and writes `Build\PeekSeekSiegeUI\`:

```
MasterBundle.dat
peekseeksiege.masterbundle          Windows
peekseeksiege_linux.masterbundle
peekseeksiege_mac.masterbundle
peekseeksiege.masterbundle.hash     all three hashes, for the server
Effects/PickBoard/PickBoard.dat
```

The build script is `EditorAssetBundleHelper.Build` from the SDK, minus the SDK: the same
`BuildPipeline` call per platform with the same two options, the stray folder-named
bundle removed, the `.hash` written only when all three platforms exist. It does not
import TMP's Essential Resources; opening the project in the Editor prompts for them, and
they only affect what you see in the Editor, never what the game draws.

## Getting it in front of a player

That folder is the mod. Two ways to load it:

- **To test**, copy it to `Servers/unturned/Bundles/PeekSeekSiegeUI/` on the server and
  to `Maps/Alpha Valley/Bundles/PeekSeekSiegeUI/` in your own Unturned install. Restart
  both. The client path is the map's, not `Sandbox/`, for a reason worth knowing: while
  connected to a server a client only sees assets from the core game, the map, and the
  server's workshop files (`Assets.ApplyServerAssetMapping`), so a `Sandbox/` mod is
  there in singleplayer and gone the moment you join. A map's `Bundles/` folder is part
  of the map's origin and not part of its hash, so it rides along.
- **For players**, it is Workshop item **3803930118** (unlisted), and the debug server
  lists it in `File_IDs` in `Servers/unturned/WorkshopDownloadConfig.json`, so the
  server fetches it at boot and every client is handed it on join. After a rebuild:

  ```
  cd tools/WorkshopUpload
  dotnet run -c Release -- ../../Build/PeekSeekSiegeUI --update 3803930118 --changes "what changed"
  ```

  That goes through the Steam client logged in on this machine; there is no password,
  key or token involved, because Steam takes Workshop uploads no other way. Leave off
  `--update` to create a new item. The server sees the update within ten minutes and
  restarts itself for it (`Should_Monitor_Updates`).

The plugin's `PickUIEffectGUID` defaults to the GUID in `PickBoard.dat`, so a server
that has the mod draws the board without a config change, and one that does not logs a
line at the first pick phase and draws the board to chat. A server config written before
this default existed has an empty `<PickUIEffectGUID />` and needs the value pasted in.

The chat fallback covers a *server* without the mod, and nothing else. Once the server
has it, a client that does not is **kicked** the moment the board is sent to them:
`missing asset: "PickBoard"`, from the game's asset integrity check, before the plugin
gets a say. That is what the Workshop id in `WorkshopDownloadConfig.json` is for: a
client is made to download it on join. Mod folders are also only scanned when the game
starts, so a copy dropped in while Unturned is running is not there until it restarts.

## Making it look like PeekSeek

- Near-black ground, `#111` at 94%. Not a translucent grey panel over the game.
- One red, `#c8102e`, used twice: the timer, and the border of the taken tile.
- Body text off-white `#e6e6e6`; blurbs and numbers a step down, `#9a9a9a`.
- Square corners. No drop shadows, no gradients, no icons that mean nothing.
- Centred, a little above the middle, under the game's own canvas so the pause menu
  covers it and not the other way round.

It has to be legible at a glance while a twenty-second timer runs. That is the only
requirement that really matters.
