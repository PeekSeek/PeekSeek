# PeekSeek Armor & Skin

[![peekseek-armor-skin](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml/badge.svg)](https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml)

Makes every player on a server the same kind of target. Three parts, each of
which can be switched off on its own:

- **Skin**, a player whose skin colour is outside the band the four tones span
  is moved to one of them, picked from their Steam64.
- **Armor**, everyone takes damage as though wearing a full military set,
  whatever they actually have on.
- **Cosmetics**, shirt, pants, vest and backpack cosmetics stop rendering. Hats,
  masks and glasses keep theirs.

Running on the warm-up lobby first.

## Skin

The colours are Unturned's own character presets, taken straight out of
`Customization.SKINS` at indices `0`, `2`, `4` and `7`:

| Index | Hex | |
| --- | --- | --- |
| 0 | `#F4E6D2` | lightest preset |
| 2 | `#BEA582` | |
| 4 | `#94764B` | |
| 7 | `#4B3D31` | darkest of the four |

A player is only moved if their own colour falls outside the band those four
span, every preset from the lightest of them down to the darkest is a face they
keep, which is all ten of Unturned's minus the two near-black ones. The four are
a floor and a ceiling on how visible a player can make themselves, not a uniform.

Anyone outside gets `presets[steam64 % 4]`, so it is the same colour on every
join and on every server running this, and no two consecutive Steam IDs land on
the same one. In practice the players this touches are Gold accounts, who get RGB
sliders in the character menu and can mix a colour no preset check would pass.

## Cosmetics

A cosmetic is a Steam itemdef the client renders over the real clothing item, so
a player in a workshop shirt is a shape nobody can predict from what they picked
up. Clearing the itemdef is how Unturned itself says "no cosmetic here", and it
drops the player back to the actual item.

Shirt, pants, vest and backpack are cleared. Hats, masks and glasses keep theirs:
they sit above the silhouette rather than changing it, and they are most of what
people spend money on.

## Armor

Every hit is resolved as if the player were wearing an undamaged Military Top,
Bottom, Vest and Helmet. The numbers come from Unturned's own item files, where
every camo variant carries the same ones:

| Hit location | Multiplier | From |
| --- | --- | --- |
| Skull | `0.85` | Military Helmet |
| Spine | `0.63` | Military Vest `0.7` x Military Top `0.9` |
| Arms, hands | `0.9` | Military Top |
| Legs, feet | `0.9` | Military Bottom |
| Torso front and back | `1.0` | vanilla armours neither, so neither does this |
| Explosions | `0.8375` | the set's average: `(0.9 + 0.9 + 0.7 + 0.85) / 4` |

Explosions are a separate path in Unturned and needed their own number.
`DamageTool.getPlayerExplosionArmor` averages the four worn pieces rather than
picking one by limb, and a piece's explosion armor falls back to its ordinary
armor when the item never sets one, which every military piece does. So a
landmine was the last thing that could still tell a naked player from a dressed
one.

Lower is tougher: these multiply incoming damage rather than absorbing a
percentage of it. Only shirt, pants, vest and hat ever mattered, vanilla never
reads a mask, backpack or glasses for damage, so nothing is being taken away
that used to count.

Clothing durability is left alone as a side effect. Vanilla docks a point off
whatever slot was hit and tells the client about it; since what a player wears no
longer decides anything, wearing it out would be damage nobody can feel.

## Configuration

`Rocket/Plugins/PeekSeekArmorSkin/PeekSeekArmorSkin.configuration.xml`:

| Setting | Default | |
| --- | --- | --- |
| `AssignSkinTones` | `true` | the skin part |
| `EqualizeArmor` | `true` | the armor part |
| `StripCosmetics` | `true` | the cosmetics part |
| `SkinTonePresets` | `0, 2, 4, 7` | indices into `Customization.SKINS`; any length works |
| `ArmsArmor` | `0.9` | |
| `LegsArmor` | `0.9` | |
| `SpineArmor` | `0.63` | |
| `SkullArmor` | `0.85` | |
| `ExplosionArmor` | `0.8375` | one number, not per-limb: the game averages instead |

## How it works

All three are Harmony prefixes, and none keeps state that outlives a connect:

- `Provider.accept(SteamPending)` for the colour and the cosmetics. Unturned
  copies both out of `SteamPending` once, when the `SteamPlayer` is built, and
  writes that same copy into the packet every other client is told about the new
  player with. This is the last moment both still read from `SteamPending`, set
  it any later and the server would be the only machine that agrees.
- `DamageTool.getPlayerArmor` for bullets and melee, and
  `DamageTool.getPlayerExplosionArmor` for blasts. Both replaced outright rather
  than adjusted, which is also what leaves durability alone.

It shares nothing with [peekseek-lobby](../peekseek-lobby/) and can be dropped
onto any PeekSeek server by itself.

## Building

```bash
dotnet build PeekSeekArmorSkin.sln --configuration Release
dotnet test PeekSeekArmorSkin.sln
dotnet format PeekSeekArmorSkin.sln
```

The compiled DLL lands in `plugin/bin/Release/net48/`. Drop it into the server's
`Rocket/Plugins/PeekSeekArmorSkin/`.
