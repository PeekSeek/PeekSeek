# The option board, as a Unity prefab

What has to exist in a workshop bundle before `PickUIEffectGUID` can point at anything.

Unturned draws custom UI from one place only: an `EffectAsset` whose prefab is a UGUI
canvas, living inside an asset bundle the client has downloaded. A plugin cannot create
one at runtime, which is why this is a separate build step and why the plugin ships a chat
board that works without it.

## What the plugin sends

Once, when the board opens:

```
EffectManager.SendUIEffect(asset, PickUIKey, connection, reliable: true)
```

Then, by child name, using `sendUIEffectText` and `sendUIEffectVisibility`:

| Child name | Type | What goes in it |
| --- | --- | --- |
| `Headline` | text | `Round 2. You are defending North.` |
| `Timer` | text | seconds left, rewritten once a second |
| `Option0_Name` … `Option4_Name` | text | the option's name |
| `Option0_Blurb` … `Option4_Blurb` | text | one short line under the name |
| `Option0_Taken` … `Option4_Taken` | any object | shown on the tile the player picked, hidden on the rest |

And back, from `EffectManager.onEffectButtonClicked`:

| Button name | Meaning |
| --- | --- |
| `SiegePick0` … `SiegePick4` | this tile was clicked |

The `Siege` prefix on the buttons is deliberate: `onEffectButtonClicked` is global, so a
click on some other plugin's UI would otherwise land in this board's handler.

Names are matched by `childNameOrPath`, so a child anywhere under the canvas root works as
long as the name is unique. Every `Option{n}_Taken` should start hidden — the plugin only
sends visibility when somebody picks.

## Building it

1. Clone [U3-SDK](https://github.com/SmartlyDressedGames/U3-SDK) and open it in Unity
   **2022.3.62f3**. Any other version writes a bundle the game will not load.
2. Make a canvas prefab with the five tiles and the children named above. Each tile needs
   a `Button` component, named `SiegePick0` through `SiegePick4`.
3. Add an `EffectAsset` `.dat` next to it pointing at the prefab, with `Effect_Type
   UI`. Note its GUID.
4. Put both inside a MasterBundle and build it.
5. Publish it to the Steam Workshop as a content mod.
6. On the server, add the published file id to `File_IDs` in
   `Servers/unturned/WorkshopDownloadConfig.json`, so clients download it on join.
7. Put the `EffectAsset` GUID into `PickUIEffectGUID` in the plugin config and restart.

If the GUID is set but does not resolve, the plugin logs the reason once at load and falls
back to the chat board rather than showing nothing.

## Making it feel like PeekSeek

The site's design language, for whoever builds the prefab:

- Near-black ground, `#111` to `#161616`. Not a translucent grey panel over the game.
- One red accent, `#c8102e`, and use it sparingly: the timer, and the border of the tile
  you have taken. Nothing else.
- Body text off-white `#e6e6e6`; blurbs a step down, `#9a9a9a`.
- Square corners. No drop shadows, no gradients, no icons that do not mean anything.
- The tile number large and to the left, because `/pick 3` has to stay legible as the same
  board.

The board should read at a glance while a twenty-second timer runs. That is the only
requirement that actually matters.
