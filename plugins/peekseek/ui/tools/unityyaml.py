"""The handful of Unity objects a uGUI screen is made of, written as the YAML Unity saves.

A prefab is a flat list of objects, each `--- !u!<class> &<id>` followed by its fields,
and Unity fills in any field a document leaves out. That is what makes it practical to
write one from Python: every component here carries only the fields that matter, and
the component GUIDs are the packages' own, the same in every project that has the
package. Everything below was checked against prefabs Unity 2021.3 saved.

Images and sprites: an Image with no sprite draws a flat rectangle in its colour. A
rounded one needs a sprite, so `sprite()` writes a PNG and the .meta that imports it as
a nine-sliced sprite under a GUID this file chooses, and `image()` can point at it.

Fonts: the game replaces a TextMeshPro font at spawn only when the prefab has none or
has TMP's stock one, so a font of our own is left alone. `text()` references the two
Selawik assets the build script creates under fixed GUIDs.
"""

import math
import struct
import zlib
from pathlib import Path

# com.unity.ugui and com.unity.textmeshpro script GUIDs.
IMAGE = "fe87c0e1cc204ed48ad3b37840f39efc"
BUTTON = "4e29b1a8efbd4b44bb3f3716e73f07ff"
MASK = "31a19414c41e5ae4aae2af33fee712f6"
ASPECT_RATIO_FITTER = "86710e43de46f6f4bac7c8e50813a599"
HORIZONTAL_LAYOUT_GROUP = "30649d3a9faa99c48a7b1166b86bf2a0"
VERTICAL_LAYOUT_GROUP = "59f8146938fff824cb5fd77236b75775"
CONTENT_SIZE_FITTER = "3245ec927659c4140ac4f8d17403cc18"
LAYOUT_ELEMENT = "306cc8c2b49d7114eaa3623786fc2126"
CANVAS_SCALER = "0cd44c1031e13a943bb63640046fad76"
GRAPHIC_RAYCASTER = "dc42784cf147c0c48a680349fa168899"
TEXT_MESH_PRO_UGUI = "f4688fdb7df04437aeb418b961361dc5"

# The two font assets BuildBundle.cs creates from Assets/PeekSeek/Fonts and pins to these
# GUIDs, so a prefab written here and a font made there agree without either knowing the
# other exists. The shared material is left null on purpose: TextMeshPro then takes the
# font's own, which saves this file having to know a sub-asset id Unity generates.
#
# Selawik, because the site asks for the system UI font and on Windows that is Segoe UI,
# which may not be shipped; Selawik is Microsoft's open stand-in for it, drawn to the
# same metrics. Inter, the earlier choice, read visibly wider than the site.
FONT_REGULAR = "a1c4e7f0b3d6a9c2e5f8b1d4a7c0e3f6"
FONT_BOLD = "b2d5f8a1c4e7b0d3f6a9c2e5b8d1f4a7"
# JetBrains Mono Bold, for a clock: every digit the same width, so 0:59 to 1:00 does
# not shift the text. TextMeshPro cannot ask a proportional font for tabular figures.
FONT_MONO = "9f3c5e1a7d4b6c8e0a2f3b4c5d6e7f8a"

# How tall a line of each font is, as a multiple of its size: ascent to descent, off
# the font assets BuildBundle.cs makes.
LINE_HEIGHT = {FONT_REGULAR: 1.2, FONT_BOLD: 1.2, FONT_MONO: 1.32}

LEFT, CENTER, RIGHT = 1, 2, 4
TOP, MIDDLE, BOTTOM = 256, 512, 1024
# The middle of the capitals on the middle of the box, rather than the middle of the
# line's ascent and descent: what puts a word level with a picture beside it.
CAPLINE = 8192

HEADER = [
    "  m_ObjectHideFlags: 0",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
]

CLASS_NAMES = {114: "MonoBehaviour", 222: "CanvasRenderer", 223: "Canvas"}


def rgba(hex_colour, alpha=1.0):
    r, g, b = (int(hex_colour[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return f"{{r: {r:.4g}, g: {g:.4g}, b: {b:.4g}, a: {alpha:g}}}"


def quoted(text):
    return "'" + text.replace("'", "''") + "'"


class Node:
    """One GameObject with its RectTransform, and whatever components sit on it.

    Anchors, pivot, position and size are uGUI's own RectTransform fields; the
    constructors below (`top_left`, `stretch`, ...) are the few shapes a screen needs.
    """

    ids = iter(range(100000, 10_000_000))

    def __init__(self, name, *, active=True, anchor_min=(0, 0), anchor_max=(1, 1),
                 pivot=(0.5, 0.5), position=(0, 0), size=(0, 0), rotation=0):
        self.name = name
        self.active = active
        self.anchor_min, self.anchor_max, self.pivot = anchor_min, anchor_max, pivot
        self.position, self.size = position, size
        # Degrees anticlockwise about the pivot, the one axis a flat UI can turn on.
        self.rotation = rotation
        self.children = []
        self.components = []
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
        out.append(f"--- !u!1 &{self.id}")
        out.append("GameObject:")
        out += HEADER
        out.append("  serializedVersion: 6")
        out.append("  m_Component:")
        out.append(f"  - component: {{fileID: {self.rect_id}}}")
        out += [f"  - component: {{fileID: {cid}}}" for _, cid, _ in self.components]
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
        half = math.radians(self.rotation) / 2
        out.append(f"  m_LocalRotation: {{x: 0, y: 0, z: {math.sin(half):.6g}, w: {math.cos(half):.6g}}}")
        out.append("  m_LocalPosition: {x: 0, y: 0, z: 0}")
        out.append("  m_LocalScale: {x: 1, y: 1, z: 1}")
        out.append("  m_ConstrainProportionsScale: 0")
        out.append("  m_Children:" if self.children else "  m_Children: []")
        out += [f"  - {{fileID: {child.rect_id}}}" for child in self.children]
        out.append(f"  m_Father: {{fileID: {parent_rect_id}}}")
        out.append(f"  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: {self.rotation:g}}}")
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


# ---- placement -----------------------------------------------------------------------

def top_left(name, x, y, w, h, **kw):
    return Node(name, anchor_min=(0, 1), anchor_max=(0, 1), pivot=(0, 1), position=(x, -y), size=(w, h), **kw)


def top_right(name, x, y, w, h, **kw):
    """`x` in from the right edge."""
    return Node(name, anchor_min=(1, 1), anchor_max=(1, 1), pivot=(1, 1), position=(-x, -y), size=(w, h), **kw)


def top_center(name, x, y, w, h, **kw):
    """`x` is the offset of the centre from the parent's centre."""
    return Node(name, anchor_min=(0.5, 1), anchor_max=(0.5, 1), pivot=(0.5, 1), position=(x, -y), size=(w, h), **kw)


def bottom_center(name, x, y, w, h, **kw):
    """`x` is the offset of the centre from the parent's centre, `y` up from the bottom."""
    return Node(name, anchor_min=(0.5, 0), anchor_max=(0.5, 0), pivot=(0.5, 0), position=(x, y), size=(w, h), **kw)


def bottom_left(name, x, y, w, h, **kw):
    return Node(name, anchor_min=(0, 0), anchor_max=(0, 0), pivot=(0, 0), position=(x, y), size=(w, h), **kw)


def bottom_right(name, x, y, w, h, **kw):
    """`x` and `y` in from the bottom-right corner."""
    return Node(name, anchor_min=(1, 0), anchor_max=(1, 0), pivot=(1, 0), position=(-x, y), size=(w, h), **kw)


def centered(name, x, y, w, h, **kw):
    return Node(name, anchor_min=(0.5, 0.5), anchor_max=(0.5, 0.5), position=(x, y), size=(w, h), **kw)


def top_stretch(name, y, h, inset, **kw):
    """Full parent width minus `inset` each side, `h` tall, `y` down from the top."""
    return Node(name, anchor_min=(0, 1), anchor_max=(1, 1), pivot=(0.5, 1), position=(0, -y), size=(-2 * inset, h), **kw)


def stretch(name, **kw):
    return Node(name, anchor_min=(0, 0), anchor_max=(1, 1), size=(0, 0), **kw)


# ---- components ----------------------------------------------------------------------

def canvas(node, sorting_order=0):
    node.component(223, [
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
        f"m_SortingOrder: {sorting_order}",
        "m_TargetDisplay: 0",
    ])
    # ConstantPixelSize: the game's UnturnedCanvasScaler multiplies this by the player's
    # UI scale setting, so sizes here are pixels at scale 1.
    node.mono(CANVAS_SCALER, [
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
    node.mono(GRAPHIC_RAYCASTER, [
        "m_IgnoreReversedGraphics: 1",
        "m_BlockingObjects: 0",
        "m_BlockingMask:",
        "  serializedVersion: 2",
        "  m_Bits: 4294967295",
    ])


def image(node, colour, alpha=1.0, *, sprite=None, sliced=True, enabled=True, preserve_aspect=False, raycast=False,
          material=None):
    """A rectangle in a colour. With `sprite` set it is that sprite, tinted: nine-sliced
    for a corner or a border that must not stretch, plain for a picture. `material` is
    the GUID of a material asset to draw it with instead of uGUI's own."""
    node.component(222, ["m_CullTransparentMesh: 1"])
    return node.mono(IMAGE, [
        f"m_Material: {{fileID: 2100000, guid: {material}, type: 2}}" if material else "m_Material: {fileID: 0}",
        f"m_Color: {rgba(colour, alpha)}",
        f"m_RaycastTarget: {int(raycast)}",
        "m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}",
        "m_Maskable: 1",
        "m_OnCullStateChanged:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
        f"m_Sprite: {{fileID: 21300000, guid: {sprite}, type: 3}}" if sprite else "m_Sprite: {fileID: 0}",
        f"m_Type: {1 if sprite and sliced else 0}",
        f"m_PreserveAspect: {int(preserve_aspect)}",
        "m_FillCenter: 1",
        "m_FillMethod: 4",
        "m_FillAmount: 1",
        "m_FillClockwise: 1",
        "m_FillOrigin: 0",
        "m_UseSpriteMesh: 0",
        "m_PixelsPerUnitMultiplier: 1",
    ], enabled=enabled)


def aspect_fit(node, ratio=1.0):
    """uGUI's AspectRatioFitter in FitInParent: the node becomes the largest rectangle
    of this ratio that fits its parent, whatever the screen is."""
    node.mono(ASPECT_RATIO_FITTER, [
        "m_AspectMode: 3",
        f"m_AspectRatio: {ratio:g}",
    ])


def mask(node, show=False):
    """Clips the node's children to the alpha of its own Image, which is how an avatar
    that arrives as a square gets the site's rounded corners. The Image itself is
    hidden unless `show`, for a mask that is also the thing behind its children."""
    node.mono(MASK, [f"m_ShowMaskGraphic: {int(show)}"])


# TextAnchor, the uGUI enum a layout group aligns its children by.
MIDDLE_CENTER = 4


def row(node, spacing=0, align=MIDDLE_CENTER, padding=0, control_width=False):
    """uGUI's HorizontalLayoutGroup: the node's active children laid out left to right,
    `spacing` apart, the whole run aligned in the node. A child that is switched off
    is left out of the run, so the rest close up round it: that is what makes a marker
    or a clock a screen does not have cost no space. With `control_width` each child is
    as wide as it asks to be, a text as wide as its text and an image whatever its
    `layout_size` says, which with `fit_width` on the node makes a row as wide as what
    is in it."""
    node.mono(HORIZONTAL_LAYOUT_GROUP, [
        "m_Padding:",
        f"  m_Left: {padding}",
        f"  m_Right: {padding}",
        "  m_Top: 0",
        "  m_Bottom: 0",
        f"m_ChildAlignment: {align}",
        f"m_Spacing: {spacing:g}",
        "m_ChildForceExpandWidth: 0",
        "m_ChildForceExpandHeight: 0",
        f"m_ChildControlWidth: {int(control_width)}",
        "m_ChildControlHeight: 0",
        "m_ChildScaleWidth: 0",
        "m_ChildScaleHeight: 0",
        "m_ReverseArrangement: 0",
    ])


def column(node, spacing=0, padding=0, top=0):
    """uGUI's VerticalLayoutGroup: the node's active children stacked, each as wide as
    the node and as tall as it already is. A child that is switched off is left out,
    so a board of twelve row slots with three players in it is three rows tall rather
    than nine rows of empty panel."""
    node.mono(VERTICAL_LAYOUT_GROUP, [
        "m_Padding:",
        f"  m_Left: {padding}",
        f"  m_Right: {padding}",
        f"  m_Top: {top}",
        f"  m_Bottom: {padding}",
        "m_ChildAlignment: 0",
        f"m_Spacing: {spacing:g}",
        "m_ChildForceExpandWidth: 1",
        "m_ChildForceExpandHeight: 0",
        "m_ChildControlWidth: 1",
        "m_ChildControlHeight: 0",
        "m_ChildScaleWidth: 0",
        "m_ChildScaleHeight: 0",
        "m_ReverseArrangement: 0",
    ])


def fit_height(node):
    """uGUI's ContentSizeFitter, vertical only: the node becomes as tall as its layout
    says it wants to be. Its pivot decides which way it grows."""
    node.mono(CONTENT_SIZE_FITTER, [
        "m_HorizontalFit: 0",
        "m_VerticalFit: 2",
    ])


def fit_width(node):
    """uGUI's ContentSizeFitter, horizontal only: the node becomes as wide as its layout
    says it wants to be. Its pivot decides which way it grows."""
    node.mono(CONTENT_SIZE_FITTER, [
        "m_HorizontalFit: 2",
        "m_VerticalFit: 0",
    ])


def layout_size(node, width, height):
    """What a node asks a layout for, when it has no text to measure: a picture is this
    big in the run whatever picture arrives."""
    node.mono(LAYOUT_ELEMENT, [
        "m_IgnoreLayout: 0",
        "m_MinWidth: -1",
        "m_MinHeight: -1",
        f"m_PreferredWidth: {width:g}",
        f"m_PreferredHeight: {height:g}",
        "m_FlexibleWidth: -1",
        "m_FlexibleHeight: -1",
        "m_LayoutPriority: 1",
    ])


def ignore_layout(node):
    """A child a layout leaves alone: a hairline drawn round the row is not a part of
    the row."""
    node.mono(LAYOUT_ELEMENT, [
        "m_IgnoreLayout: 1",
        "m_MinWidth: -1",
        "m_MinHeight: -1",
        "m_PreferredWidth: -1",
        "m_PreferredHeight: -1",
        "m_FlexibleWidth: -1",
        "m_FlexibleHeight: -1",
        "m_LayoutPriority: 1",
    ])


def button(node, target_image_id, normal, hover, pressed):
    """The node's own Image is the button. It is white and these tints are the real
    colours, which is how uGUI's colour transition is meant to be used."""
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
        f"  m_NormalColor: {rgba(normal)}",
        f"  m_HighlightedColor: {rgba(hover)}",
        f"  m_PressedColor: {rgba(pressed)}",
        f"  m_SelectedColor: {rgba(normal)}",
        f"  m_DisabledColor: {rgba(normal)}",
        "  m_ColorMultiplier: 1",
        "  m_FadeDuration: 0.08",
        "m_Interactable: 1",
        f"m_TargetGraphic: {{fileID: {target_image_id}}}",
        "m_OnClick:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
    ])


def fits_line(node, line, wrap):
    """Refuse a box shorter than one line of its text. The overflow mode is Ellipsis,
    and TextMeshPro draws nothing at all when even the first line does not fit, so a
    box a few pixels short is a blank one in game rather than a clipped one."""
    fixed_height = node.anchor_min[1] == node.anchor_max[1]
    if wrap or not fixed_height or node.size[1] >= line:
        return
    raise ValueError(f"{node.name} is {node.size[1]:g}px tall, its text needs {line:.1f}")


def text(node, content, *, size, colour, halign=LEFT, valign=MIDDLE, bold=False,
         wrap=False, spacing=0, uppercase=False, mono=False):
    """TextMeshPro text. `spacing` is letter spacing in hundredths of an em, the unit
    TMP uses, so CSS's 0.1em is 10."""
    font = FONT_MONO if mono else FONT_BOLD if bold else FONT_REGULAR
    fits_line(node, size * LINE_HEIGHT[font], wrap)
    node.component(222, ["m_CullTransparentMesh: 1"])
    node.mono(TEXT_MESH_PRO_UGUI, [
        "m_Material: {fileID: 0}",
        f"m_Color: {rgba(colour)}",
        "m_RaycastTarget: 0",
        "m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}",
        "m_Maskable: 1",
        "m_OnCullStateChanged:",
        "  m_PersistentCalls:",
        "    m_Calls: []",
        f"m_text: {quoted(content)}",
        "m_isRightToLeft: 0",
        f"m_fontAsset: {{fileID: 11400000, guid: {font}, type: 2}}",
        "m_sharedMaterial: {fileID: 0}",
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
        f"m_fontSize: {size:g}",
        f"m_fontSizeBase: {size:g}",
        "m_fontWeight: 400",
        "m_enableAutoSizing: 0",
        "m_fontSizeMin: 8",
        "m_fontSizeMax: 72",
        f"m_fontStyle: {16 if uppercase else 0}",
        f"m_HorizontalAlignment: {halign}",
        f"m_VerticalAlignment: {valign}",
        "m_textAlignment: 65535",
        f"m_characterSpacing: {spacing:g}",
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


def prefab(root):
    out = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
    root.emit(0, out)
    return "\n".join(out) + "\n"


# ---- sprites -------------------------------------------------------------------------

def png(width, height, alpha_at):
    """An all-white RGBA PNG whose alpha is `alpha_at(x, y)` in 0..1, y downward."""
    rows = []
    for y in range(height):
        row = bytearray([0])
        for x in range(width):
            row += bytes((255, 255, 255, round(255 * alpha_at(x, y))))
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def coverage(inside, x, y, samples=4):
    """How much of pixel (x, y) is inside a shape, by sampling."""
    hits = 0
    for i in range(samples):
        for j in range(samples):
            hits += inside(x + (i + 0.5) / samples, y + (j + 0.5) / samples)
    return hits / (samples * samples)


def rounded_alpha(width, height, radius, corners=("tl", "tr", "bl", "br")):
    """A rectangle with the named corners rounded."""
    def inside(px, py):
        for name, (cx, cy) in {
            "tl": (radius, radius), "tr": (width - radius, radius),
            "bl": (radius, height - radius), "br": (width - radius, height - radius),
        }.items():
            if name not in corners:
                continue
            dx, dy = px - cx, py - cy
            in_corner_box = (
                (px < cx if name in ("tl", "bl") else px > cx)
                and (py < cy if name in ("tl", "tr") else py > cy)
            )
            if in_corner_box and dx * dx + dy * dy > radius * radius:
                return 0
        return 1

    return lambda x, y: coverage(inside, x, y)


def ring_alpha(width, height, radius, thickness=1):
    """A rounded rectangle's outline, `thickness` wide: the rectangle less the same one
    inset. A hairline round a thing that sizes itself, since a border cannot be set on
    an Image but a ring behind it with nothing in the middle draws the same."""
    outer = rounded_alpha(width, height, radius)

    def inside_inner(px, py):
        t = thickness
        if px < t or py < t or px > width - t or py > height - t:
            return 0
        r = radius - t
        for cx, cy in ((t + r, t + r), (width - t - r, t + r), (t + r, height - t - r), (width - t - r, height - t - r)):
            in_corner = (px < cx if cx < width / 2 else px > cx) and (py < cy if cy < height / 2 else py > cy)
            if in_corner and (px - cx) ** 2 + (py - cy) ** 2 > r * r:
                return 0
        return 1

    return lambda x, y: max(0.0, outer(x, y) - coverage(inside_inner, x, y))


def flare_alpha(size, outer_x):
    """The fillet beside a notch: filled everywhere except a quarter disc of radius
    `size` centred at depth `size` on the flare's outer edge, which is the overlay's
    `M0 0 L10 0 L10 10 A10 10 0 0 0 0 0` path. Full width at the top edge, nothing at
    depth `size`: that is what makes a notch flare into a straight line instead of
    just rounding away from it."""
    def inside(px, py):
        dx, dy = px - outer_x, py - size
        return 0 if dx * dx + dy * dy < size * size else 1

    return lambda x, y: coverage(inside, x, y)


def skull_alpha(size):
    """A skull: a round head with two eyes, a jaw under it with two gaps for teeth.
    The kill feed's mark for a death nobody dealt, and for one the server cannot put
    an item to."""
    def inside(px, py):
        x, y = px / size, py / size
        for ex in (0.37, 0.63):
            if (x - ex) ** 2 + (y - 0.42) ** 2 <= 0.085 ** 2:
                return 0
        if 0.30 <= x <= 0.70 and 0.70 <= y <= 0.88 and (abs(x - 0.43) < 0.02 or abs(x - 0.57) < 0.02):
            return 0
        if (x - 0.5) ** 2 + (y - 0.42) ** 2 <= 0.34 ** 2:
            return 1
        if 0.30 <= x <= 0.70 and 0.60 <= y <= 0.88:
            return 1
        return 0

    return lambda x, y: coverage(inside, x, y)


def disc_alpha(size):
    """A filled circle: a dot on a map, and the ground under the keep."""
    def inside(px, py):
        return 1 if (px - size / 2) ** 2 + (py - size / 2) ** 2 <= (size / 2) ** 2 else 0

    return lambda x, y: coverage(inside, x, y)


def castle_alpha(size):
    """A keep: a wall with a merlon at each end, a taller tower in the middle with two
    of its own, and an arched gate. The role marker for the team holding the flag.
    Drawn here rather than taken from an icon set, so nothing about it is anybody
    else's."""
    def box(x, y, x0, y0, x1, y1):
        return x0 <= x <= x1 and y0 <= y <= y1

    def inside(px, py):
        x, y = px / size, py / size
        # The gate: an arch with a straight-sided opening under it, cut out of the wall.
        if 0.41 <= x <= 0.59 and (y >= 0.77 or (x - 0.5) ** 2 + (y - 0.77) ** 2 <= 0.09 ** 2):
            return 0
        if box(x, y, 0.08, 0.46, 0.92, 0.92):
            return 1
        if box(x, y, 0.08, 0.32, 0.22, 0.46) or box(x, y, 0.78, 0.32, 0.92, 0.46):
            return 1
        if box(x, y, 0.35, 0.22, 0.65, 0.46):
            return 1
        if box(x, y, 0.35, 0.10, 0.46, 0.22) or box(x, y, 0.54, 0.10, 0.65, 0.22):
            return 1
        return 0

    return lambda x, y: coverage(inside, x, y)


def swords_alpha(size):
    """Two crossed blades with a short guard each. The role marker for the team
    attacking. Drawn here for the same reason the keep is."""
    def near(px, py, ax, ay, bx, by, radius):
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
        cx, cy = ax + t * dx, ay + t * dy
        return (px - cx) ** 2 + (py - cy) ** 2 <= radius * radius

    lo, hi, thick = size * 0.12, size * 0.88, size * 0.06
    guard = size * 0.11

    def inside(px, py):
        # Blades from each top corner down to the opposite bottom corner.
        if near(px, py, lo, lo, hi, hi, thick) or near(px, py, hi, lo, lo, hi, thick):
            return 1
        # A guard across each blade near its grip, which is the bottom end.
        for (gx, gy, ux, uy) in ((lo + (hi - lo) * 0.8, lo + (hi - lo) * 0.8, 1, -1),
                                 (lo + (hi - lo) * 0.2, lo + (hi - lo) * 0.8, 1, 1)):
            if near(px, py, gx - ux * guard, gy - uy * guard, gx + ux * guard, gy + uy * guard, thick):
                return 1
        return 0

    return lambda x, y: coverage(inside, x, y)


def sprite(folder, name, guid, width, height, alpha_at, border):
    """Write `name`.png, white with the alpha `alpha_at` gives, and the .meta that imports
    it as a sprite under `guid`. `border` is the nine-slice inset on every side."""
    return sprite_file(folder, name, guid, png(width, height, alpha_at), border)


def sprite_file(folder, name, guid, png_bytes, border=0):
    """Write a finished PNG and the .meta that imports it as a sprite under `guid`."""
    Path(folder, name + ".png").write_bytes(png_bytes)
    Path(folder, name + ".png.meta").write_text(f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: {border}, y: {border}, z: {border}, w: {border}}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8", newline="\n")
    return guid


# ---- the silhouette material -----------------------------------------------------------

SILHOUETTE_SHADER = "a1b2c3d4e5f60718293a4b5c6d7e8f90"
SILHOUETTE_MATERIAL = "b2c3d4e5f60718293a4b5c6d7e8f90a1"


def silhouette(folder):
    """A material that draws an image as a flat shape: the sprite's alpha in the tint
    colour, and nothing of its own colours. uGUI's default material multiplies the
    tint into the picture, which leaves a gun the colour of a gun; a kill feed wants
    it white. Writes the shader, its material and their metas, and returns the
    material's GUID for `image(material=...)`."""
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)

    Path(folder, "Silhouette.shader").write_text("""Shader "PeekSeek/UI Silhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                return fixed4(i.color.rgb, a * i.color.a);
            }
            ENDCG
        }
    }
}
""", encoding="utf-8", newline="\n")
    Path(folder, "Silhouette.shader.meta").write_text(f"""fileFormatVersion: 2
guid: {SILHOUETTE_SHADER}
ShaderImporter:
  externalObjects: {{}}
  defaultTextures: []
  nonModifiableTextures: []
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8", newline="\n")

    Path(folder, "Silhouette.mat").write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Silhouette
  m_Shader: {{fileID: 4800000, guid: {SILHOUETTE_SHADER}, type: 3}}
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats: []
    m_Colors:
    - _Color: {{r: 1, g: 1, b: 1, a: 1}}
  m_BuildTextureStacks: []
""", encoding="utf-8", newline="\n")
    Path(folder, "Silhouette.mat.meta").write_text(f"""fileFormatVersion: 2
guid: {SILHOUETTE_MATERIAL}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 2100000
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8", newline="\n")

    return SILHOUETTE_MATERIAL
