# The option board, as a Unity prefab

What has to exist in a workshop bundle before `PickUIEffectGUID` can point at anything.

Everything below is read out of the game's own code (`EffectManager`,
`PluginButtonListener`) rather than inferred, because the two obvious guesses are both
wrong.

## It is uGUI, not UI Toolkit

`EffectManager.ReceiveUIEffectText` resolves the child and then does exactly this:

```
GetComponent<UnityEngine.UI.Text>()          -> set_text
GetComponent<TMPro.TextMeshProUGUI>()        -> set_text
```

So the board is a **uGUI hierarchy** — Canvas, RectTransform, `Text` or
`TextMeshProUGUI`, `Image`, `Button`. Unity's UI Builder (UXML/USS) authors **UI
Toolkit**, which this path never touches: Unturned has UI Toolkit classes of its own
(`GlazierButton_UIToolkit`) for its internal menus, but a plugin effect is not one of
them. A UXML document will silently do nothing here.

Children are resolved by `Transform.Find(childNameOrPath)` first, falling back to
`TransformRecursiveFind.FindChildRecursive`. So a plain unique name anywhere under the
root works, and a slash path works too.

## Buttons need a listener component

A `Button` on its own sends nothing. `SDG.Unturned.PluginButtonListener` is what bridges
it:

```
Start()                  -> targetButton.onClick.AddListener(onTargetButtonClicked)
onTargetButtonClicked()  -> EffectManager.sendEffectClicked(targetButton.name)
```

So each tile needs **a `Button`, plus a `PluginButtonListener` whose `targetButton` field
points at that same Button**, and the name that reaches the server is the *Button
GameObject's* name — not the listener's, not the parent's.

## What the plugin sends

Once, when the board opens:

```
EffectManager.SendUIEffect(asset, PickUIKey, connection, reliable: true)
```

Then, by child name, via `sendUIEffectText` and `sendUIEffectVisibility`:

| Child name | Needs | What goes in it |
| --- | --- | --- |
| `Headline` | `Text` or `TextMeshProUGUI` | `Round 2. You are defending North.` |
| `Timer` | `Text` or `TextMeshProUGUI` | seconds left, rewritten once a second |
| `Option0_Name` … `Option4_Name` | `Text` or `TextMeshProUGUI` | the option's name |
| `Option0_Blurb` … `Option4_Blurb` | `Text` or `TextMeshProUGUI` | one short line under the name |
| `Option0_Taken` … `Option4_Taken` | any GameObject | shown on the picked tile, hidden on the rest |

And back, via `EffectManager.onEffectButtonClicked`:

| Button GameObject name | Meaning |
| --- | --- |
| `SiegePick0` … `SiegePick4` | this tile was clicked |

The `SiegePick` prefix is deliberate: `onEffectButtonClicked` is global, so a click on
another plugin's UI would otherwise land in this board's handler.

Every `Option{n}_Taken` should start inactive — the plugin only sends visibility when
somebody picks.

## Building it

1. Clone [U3-SDK](https://github.com/SmartlyDressedGames/U3-SDK) and open it in Unity
   **2022.3.62f3**. Another version writes a bundle the game will not load.
2. Build the canvas prefab with the children named above. Five tiles, each with a
   `Button` named `SiegePick0`…`SiegePick4` and a `PluginButtonListener` pointing at it.
3. Add an `EffectAsset` `.dat` beside it pointing at the prefab, with `Effect_Type UI`.
   Note its GUID.
4. Put both in a MasterBundle and build it.
5. Publish it to the Steam Workshop as a content mod.
6. On the server, add the published file id to `File_IDs` in
   `Servers/unturned/WorkshopDownloadConfig.json` so clients download it on join.
7. Put the `EffectAsset` GUID into `PickUIEffectGUID` in the plugin config and restart.

A GUID that is set but does not resolve is logged once at load and the board falls back
to chat, rather than showing nothing.

## Making it feel like PeekSeek

- Near-black ground, `#111` to `#161616`. Not a translucent grey panel over the game.
- One red accent, `#c8102e`, used sparingly: the timer, and the border of the taken tile.
- Body text off-white `#e6e6e6`; blurbs a step down, `#9a9a9a`.
- Square corners. No drop shadows, no gradients, no icons that mean nothing.
- Tile number large and to the left, so `/pick 3` reads as the same board.

It has to be legible at a glance while a twenty-second timer runs. That is the only
requirement that really matters.
