"""Write the option board as a Unity prefab, in the YAML Unity itself saves.

Unity only reads a UI out of an asset bundle, and a bundle only comes out of the Unity
Editor. What the Editor needs in order to build one is a prefab, and a prefab is a text
file. This writes that file, so the board's layout lives in Python you can diff rather
than in clicks somebody made in an inspector once.

Run it from anywhere:

    python generate_prefab.py

It overwrites Assets/PeekSeekSiege/Bundles/Effects/PickBoard/Effect.prefab. If the
board is ever changed inside Unity instead, that edit wins and this file is stale; bring
the change back here or delete this script, but do not leave the two disagreeing.

Everything the game does to this prefab was read out of EffectManager.cs in the U3-SDK:

- Children are found by name (Transform.Find, then a recursive search), so every name
  the plugin writes to has to be unique and spelt exactly as PickBoard.cs sends it.
- Every UnityEngine.UI.Button gets a PluginButtonListener attached at spawn, and the
  Button's GameObject name is what reaches the server. No listener component is needed.
- sendUIEffectImageURL wants a UnityEngine.UI.Image, not a RawImage, and disables it
  until the picture has loaded, so the icon can start disabled without a flash.
- The game replaces the TextMeshPro font with its own, so the font referenced here
  only has to be TMP's default; it is never what the player sees.
- A CanvasScaler in ConstantPixelSize mode is driven by the player's UI scale setting.
"""

from pathlib import Path

# The com.unity.ugui and com.unity.textmeshpro script GUIDs. These are the package's own
# and are the same in every project that has the package, which is how a prefab written
# outside Unity can still point at the right components.
IMAGE = "fe87c0e1cc204ed48ad3b37840f39efc"
BUTTON = "4e29b1a8efbd4b44bb3f3716e73f07ff"
CANVAS_SCALER = "0cd44c1031e13a943bb63640046fad76"
GRAPHIC_RAYCASTER = "dc42784cf147c0c48a680349fa168899"
TEXT_MESH_PRO_UGUI = "f4688fdb7df04437aeb418b961361dc5"

# TMP's stock font, from its Essential Resources. The game swaps it for LiberationSans
# with CJK at spawn, by name, so this is a stand-in that renders in the Editor.
FONT = "{fileID: 11400000, guid: 8f586378b4e144a9851e7b34d9b748ee, type: 2}"
FONT_MATERIAL = "{fileID: 2180264, guid: 8f586378b4e144a9851e7b34d9b748ee, type: 2}"

# PeekSeek's palette: near-black ground, one red, off-white text, grey for what is read
# second. Square corners, no gradients.
GROUND = "#111111"
TILE = "#1b1b1b"
TILE_HOVER = "#262626"
TILE_PRESSED = "#303030"
TEXT = "#e6e6e6"
MUTED = "#9a9a9a"
RED = "#c8102e"

OPTIONS = 5

OUT = Path(__file__).resolve().parent.parent / "Assets/PeekSeekSiege/Bundles/Effects/PickBoard/Effect.prefab"


def rgba(hex_colour, alpha=1.0):
    r, g, b = (int(hex_colour[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return f"{{r: {r:.4g}, g: {g:.4g}, b: {b:.4g}, a: {alpha:g}}}"


def yaml_string(text):
    return "'" + text.replace("'", "''") + "'"


class Node:
    """One GameObject, its RectTransform, and whatever components go on it."""

    ids = iter(range(100000, 10_000_000))

    def __init__(self, name, *, active=True, anchor_min=(0, 0), anchor_max=(1, 1),
                 pivot=(0.5, 0.5), position=(0, 0), size=(0, 0)):
        self.name = name
        self.active = active
        self.anchor_min, self.anchor_max, self.pivot = anchor_min, anchor_max, pivot
        self.position, self.size = position, size
        self.children = []
        self.components = []  # (class id, body lines) in the order they sit on the object
        self.id = next(Node.ids)
        self.rect_id = next(Node.ids)

    def add(self, child):
        self.children.append(child)
        return child

    def component(self, class_id, body):
        component_id = next(Node.ids)
        self.components.append((class_id, component_id, body))
        return component_id

    def mono(self, guid, body, enabled=True):
        return self.component(114, [
            f"m_Enabled: {int(enabled)}",
            "m_EditorHideFlags: 0",
            f"m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}",
            "m_Name: ",
            "m_EditorClassIdentifier: ",
            *body,
        ])

    def emit(self, parent_rect_id, out):
        component_refs = [f"  - component: {{fileID: {self.rect_id}}}"]
        component_refs += [f"  - component: {{fileID: {cid}}}" for _, cid, _ in self.components]

        out.append(f"--- !u!1 &{self.id}")
        out.append("GameObject:")
        out += HEADER
        out.append("  serializedVersion: 6")
        out.append("  m_Component:")
        out += component_refs
        out.append("  m_Layer: 5")
        out.append(f"  m_Name: {self.name}")
        out.append("  m_TagString: Untagged")
        out.append("  m_Icon: {fileID: 0}")
        out.append("  m_NavMeshLayer: 0")
        out.append("  m_StaticEditorFlags: 0")
        out.append(f"  m_IsActive: {int(self.active)}")

        out.append(f"--- !u!224 &{self.rect_id}")
        out.append("RectTransform:")
        out += HEADER
        out.append(f"  m_GameObject: {{fileID: {self.id}}}")
        out.append("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}")
        out.append("  m_LocalPosition: {x: 0, y: 0, z: 0}")
        out.append("  m_LocalScale: {x: 1, y: 1, z: 1}")
        out.append("  m_ConstrainProportionsScale: 0")
        out.append("  m_Children:" if self.children else "  m_Children: []")
        out += [f"  - {{fileID: {child.rect_id}}}" for child in self.children]
        out.append(f"  m_Father: {{fileID: {parent_rect_id}}}")
        out.append("  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}")
        out.append(f"  m_AnchorMin: {{x: {self.anchor_min[0]:g}, y: {self.anchor_min[1]:g}}}")
        out.append(f"  m_AnchorMax: {{x: {self.anchor_max[0]:g}, y: {self.anchor_max[1]:g}}}")
        out.append(f"  m_AnchoredPosition: {{x: {self.position[0]:g}, y: {self.position[1]:g}}}")
        out.append(f"  m_SizeDelta: {{x: {self.size[0]:g}, y: {self.size[1]:g}}}")
        out.append(f"  m_Pivot: {{x: {self.pivot[0]:g}, y: {self.pivot[1]:g}}}")

        for class_id, component_id, body in self.components:
            out.append(f"--- !u!{class_id} &{component_id}")
            out.append(CLASS_NAMES[class_id] + ":")
            out += HEADER
            out.append(f"  m_GameObject: {{fileID: {self.id}}}")
            out += ["  " + line for line in body]

        for child in self.children:
            child.emit(self.rect_id, out)


HEADER = [
    "  m_ObjectHideFlags: 0",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
]

CLASS_NAMES = {114: "MonoBehaviour", 222: "CanvasRenderer", 223: "Canvas"}


def canvas_renderer(node):
    node.component(222, ["m_CullTransparentMesh: 1"])


def image(node, colour, alpha=1.0, *, enabled=True, preserve_aspect=False, raycast=True):
    """A flat rectangle. With no sprite, uGUI draws the colour and nothing else."""
    canvas_renderer(node)
    return node.mono(IMAGE, [
        "m_Material: {fileID: 0}",
        f"m_Color: {rgba(colour, alpha)}",
        f"m_RaycastTarget: {int(raycast)}",
        "m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}",
        "m_Maskable: 1",
        "m_OnCullStateChanged:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
        "m_Sprite: {fileID: 0}",
        "m_Type: 0",
        f"m_PreserveAspect: {int(preserve_aspect)}",
        "m_FillCenter: 1",
        "m_FillMethod: 4",
        "m_FillAmount: 1",
        "m_FillClockwise: 1",
        "m_FillOrigin: 0",
        "m_UseSpriteMesh: 0",
        "m_PixelsPerUnitMultiplier: 1",
    ], enabled=enabled)


def button(node, target_image_id):
    """The tile itself is the button. Its Image is white and these tints are the real
    tile colours, which is how uGUI's colour transition is meant to be used."""
    node.mono(BUTTON, [
        "m_Navigation:",
        "  m_Mode: 0",
        "  m_WrapAround: 0",
        "  m_SelectOnUp: {fileID: 0}",
        "  m_SelectOnDown: {fileID: 0}",
        "  m_SelectOnLeft: {fileID: 0}",
        "  m_SelectOnRight: {fileID: 0}",
        "m_Transition: 1",
        "m_Colors:",
        f"  m_NormalColor: {rgba(TILE)}",
        f"  m_HighlightedColor: {rgba(TILE_HOVER)}",
        f"  m_PressedColor: {rgba(TILE_PRESSED)}",
        f"  m_SelectedColor: {rgba(TILE)}",
        f"  m_DisabledColor: {rgba(TILE)}",
        "  m_ColorMultiplier: 1",
        "  m_FadeDuration: 0.08",
        "m_Interactable: 1",
        f"m_TargetGraphic: {{fileID: {target_image_id}}}",
        "m_OnClick:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
    ])


LEFT, CENTER, RIGHT = 1, 2, 4
TOP, MIDDLE = 256, 512


def text(node, content, *, size, colour, halign=LEFT, valign=MIDDLE, bold=False, wrap=False):
    canvas_renderer(node)
    node.mono(TEXT_MESH_PRO_UGUI, [
        "m_Material: {fileID: 0}",
        f"m_Color: {rgba(colour)}",
        "m_RaycastTarget: 0",
        "m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}",
        "m_Maskable: 1",
        "m_OnCullStateChanged:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
        f"m_text: {yaml_string(content)}",
        "m_isRightToLeft: 0",
        f"m_fontAsset: {FONT}",
        f"m_sharedMaterial: {FONT_MATERIAL}",
        "m_fontSharedMaterials: []",
        "m_fontMaterial: {fileID: 0}",
        "m_fontMaterials: []",
        "m_fontColor32:",
        "  serializedVersion: 2",
        "  rgba: 4294967295",
        f"m_fontColor: {rgba(colour)}",
        "m_enableVertexGradient: 0",
        "m_colorMode: 3",
        "m_fontColorGradient:",
        "  topLeft: {r: 1, g: 1, b: 1, a: 1}",
        "  topRight: {r: 1, g: 1, b: 1, a: 1}",
        "  bottomLeft: {r: 1, g: 1, b: 1, a: 1}",
        "  bottomRight: {r: 1, g: 1, b: 1, a: 1}",
        "m_fontColorGradientPreset: {fileID: 0}",
        "m_spriteAsset: {fileID: 0}",
        "m_tintAllSprites: 0",
        "m_StyleSheet: {fileID: 0}",
        "m_TextStyleHashCode: -1183493901",
        "m_overrideHtmlColors: 0",
        "m_faceColor:",
        "  serializedVersion: 2",
        "  rgba: 4294967295",
        f"m_fontSize: {size}",
        f"m_fontSizeBase: {size}",
        "m_fontWeight: 400",
        "m_enableAutoSizing: 0",
        "m_fontSizeMin: 8",
        "m_fontSizeMax: 72",
        f"m_fontStyle: {1 if bold else 0}",
        f"m_HorizontalAlignment: {halign}",
        f"m_VerticalAlignment: {valign}",
        "m_textAlignment: 65535",
        "m_characterSpacing: 0",
        "m_wordSpacing: 0",
        "m_lineSpacing: 0",
        "m_lineSpacingMax: 0",
        "m_paragraphSpacing: 0",
        "m_charWidthMaxAdj: 0",
        f"m_enableWordWrapping: {int(wrap)}",
        "m_wordWrappingRatios: 0.4",
        "m_overflowMode: 1",
        "m_linkedTextComponent: {fileID: 0}",
        "parentLinkedComponent: {fileID: 0}",
        "m_enableKerning: 1",
        "m_enableExtraPadding: 0",
        "checkPaddingRequired: 0",
        "m_isRichText: 1",
        "m_parseCtrlCharacters: 1",
        "m_isOrthographic: 1",
        "m_isCullingEnabled: 0",
        "m_horizontalMapping: 0",
        "m_verticalMapping: 0",
        "m_uvLineOffset: 0",
        "m_geometrySortingOrder: 0",
        "m_IsTextObjectScaleStatic: 0",
        "m_VertexBufferAutoSizeReduction: 0",
        "m_useMaxVisibleDescender: 1",
        "m_pageToDisplay: 1",
        "m_margin: {x: 0, y: 0, z: 0, w: 0}",
        "m_isUsingLegacyAnimationComponent: 0",
        "m_isVolumetricText: 0",
        "m_hasFontAssetChanged: 0",
        "m_baseMaterial: {fileID: 0}",
        "m_maskOffset: {x: 0, y: 0, z: 0, w: 0}",
    ])


def top_left(name, x, y, w, h, **kw):
    return Node(name, anchor_min=(0, 1), anchor_max=(0, 1), pivot=(0, 1), position=(x, -y), size=(w, h), **kw)


def top_stretch(name, y, h, inset, **kw):
    """Full width of the parent minus `inset` each side, `h` tall, `y` down from the top."""
    return Node(name, anchor_min=(0, 1), anchor_max=(1, 1), pivot=(0.5, 1), position=(0, -y), size=(-2 * inset, h), **kw)


def edge(name, side, thickness):
    """A hairline along one edge of the parent."""
    anchors = {
        "top": ((0, 1), (1, 1), (0.5, 1), (0, thickness)),
        "bottom": ((0, 0), (1, 0), (0.5, 0), (0, thickness)),
        "left": ((0, 0), (0, 1), (0, 0.5), (thickness, 0)),
        "right": ((1, 0), (1, 1), (1, 0.5), (thickness, 0)),
    }[side]
    return Node(name, anchor_min=anchors[0], anchor_max=anchors[1], pivot=anchors[2], size=anchors[3])


# ---- the board -------------------------------------------------------------------

PANEL_W, PANEL_H = 1096, 300
PAD = 24
TILE_W, TILE_H, GAP = 200, 200, 12
TILES_TOP = 62  # below the headline row


def build():
    root = Node("Effect")
    root.component(223, [
        "m_Enabled: 1",
        "serializedVersion: 3",
        "m_RenderMode: 0",
        "m_Camera: {fileID: 0}",
        "m_PlaneDistance: 100",
        "m_PixelPerfect: 0",
        "m_ReceivesEvents: 1",
        "m_OverrideSorting: 0",
        "m_OverridePixelPerfect: 0",
        "m_SortingBucketNormalizedSize: 0",
        "m_VertexColorAlwaysGammaSpace: 0",
        "m_AdditionalShaderChannelsFlag: 25",
        "m_UpdateRectTransformForStandalone: 0",
        "m_SortingLayerID: 0",
        # Under the game's own canvas (15), so the pause menu covers the board rather
        # than the other way round. The cursor is at 30000 and always on top.
        "m_SortingOrder: 0",
        "m_TargetDisplay: 0",
    ])
    root.mono(CANVAS_SCALER, [
        "m_UiScaleMode: 0",
        "m_ReferencePixelsPerUnit: 100",
        "m_ScaleFactor: 1",
        "m_ReferenceResolution: {x: 1920, y: 1080}",
        "m_ScreenMatchMode: 0",
        "m_MatchWidthOrHeight: 0",
        "m_PhysicalUnit: 3",
        "m_FallbackScreenDPI: 96",
        "m_DefaultSpriteDPI: 96",
        "m_DynamicPixelsPerUnit: 1",
        "m_PresetInfoIsWorld: 0",
    ])
    root.mono(GRAPHIC_RAYCASTER, [
        "m_IgnoreReversedGraphics: 1",
        "m_BlockingObjects: 0",
        "m_BlockingMask:",
        "  serializedVersion: 2",
        "  m_Bits: 4294967295",
    ])

    # Centred, a little above the middle so the crosshair is not on the tiles.
    board = root.add(Node("Board", anchor_min=(0.5, 0.5), anchor_max=(0.5, 0.5),
                          position=(0, 40), size=(PANEL_W, PANEL_H)))
    image(board, GROUND, 0.94)

    headline = board.add(top_left("Headline", PAD, 18, 880, 30))
    text(headline, "Round 2. You are defending North.", size=20, colour=TEXT)

    timer = board.add(Node("Timer", anchor_min=(1, 1), anchor_max=(1, 1), pivot=(1, 1),
                           position=(-PAD, -12), size=(120, 40)))
    text(timer, "20", size=32, colour=RED, halign=RIGHT, bold=True)

    hint = board.add(Node("Hint", anchor_min=(0, 0), anchor_max=(0, 0), pivot=(0, 0),
                          position=(PAD, 12), size=(880, 18)))
    text(hint, "Click a tile, or type /pick and its number.", size=12, colour=MUTED)

    for i in range(OPTIONS):
        tile = board.add(top_left(f"SiegePick{i}", PAD + i * (TILE_W + GAP), TILES_TOP, TILE_W, TILE_H))
        button(tile, image(tile, "#ffffff"))

        number = tile.add(top_left(f"Option{i}_Number", 10, 8, 36, 36))
        text(number, str(i + 1), size=30, colour=MUTED, bold=True)

        icon = tile.add(Node(f"Option{i}_Icon", anchor_min=(0.5, 1), anchor_max=(0.5, 1), pivot=(0.5, 1),
                             position=(0, -14), size=(96, 88)))
        image(icon, "#ffffff", enabled=False, preserve_aspect=True, raycast=False)

        name = tile.add(top_stretch(f"Option{i}_Name", 110, 22, 12))
        text(name, "Rocket Launcher", size=16, colour=TEXT, halign=CENTER, bold=True)

        blurb = tile.add(top_stretch(f"Option{i}_Blurb", 136, 52, 12))
        text(blurb, "Two rockets. Opens brick, and nothing else you carry will.",
             size=12, colour=MUTED, halign=CENTER, valign=TOP, wrap=True)

        # Off until the player picks this tile; the plugin turns exactly one on.
        taken = tile.add(Node(f"Option{i}_Taken", active=False))
        for side in ("top", "bottom", "left", "right"):
            image(taken.add(edge(side.capitalize(), side, 2)), RED, raycast=False)
        label = taken.add(Node("Label", anchor_min=(0, 0), anchor_max=(1, 0), pivot=(0.5, 0),
                               position=(0, 6), size=(0, 14)))
        text(label, "LOCKED IN", size=10, colour=RED, halign=CENTER, bold=True)

    out = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
    root.emit(0, out)
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(build(), encoding="utf-8", newline="\n")
    print(f"wrote {OUT}")
