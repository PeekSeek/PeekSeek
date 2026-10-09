"""Writes one effect per sound into the kill feed's bundle, which the lobby and every match
server carry. Run after sound-design/make_sounds.py ship has put the WAVs in
Assets/PeekSeekKillFeed/Sounds.

A sound is an EffectAsset whose prefab is one GameObject with a 2D AudioSource that plays
on awake; the plugin triggers it for one player (Hud/Sounds.cs). Two .dat keys matter,
both read out of EffectManager.internalSpawnEffect in the U3-SDK:

- Static: without it the game sets every AudioSource's pitch to a random 0.9 to 1.1,
  which would put the kill ladder out of tune.
- Lifetime with Lifetime_Spread 0: the default spread is four seconds either way, which
  would cut a sound off or keep a pooled one past its end.
"""

import hashlib
import shutil
import wave
from pathlib import Path

UI = Path(__file__).resolve().parent.parent
SOUNDS = UI / "Assets/PeekSeekKillFeed/Sounds"
EFFECTS = UI / "Assets/PeekSeekKillFeed/Bundles/Effects"
FIRST_ID = 43130  # the kill feed's screens hold 43120 to 43125

# Same names, same order, as the Sound enum in Hud/Sounds.cs: the id is FIRST_ID + index.
NAMES = [
    "kill_1",
    "kill_2",
    "kill_3",
    "kill_4",
    "kill_5",
    "round_win",
    "round_loss",
    "match_point",
    "match_win",
    "match_loss",
    "queue_join",
    "queue_leave",
    "match_found",
    "count_1",
    "count_2",
    "count_3",
    "count_4",
    "count_5",
    "go",
    "time_10s",
    "pick_lock",
]


def guid(*parts):
    return hashlib.md5("/".join(("peekseek-sound",) + parts).encode()).hexdigest()


def seconds(path):
    with wave.open(str(path)) as f:
        return f.getnframes() / f.getframerate()


def folder_name(name):
    return "Sound_" + "".join(word.capitalize() for word in name.split("_"))


# PCM rather than Vorbis: nothing added before the attack, and these are tiny.
AUDIO_META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

PREFAB = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100100
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 100101}}
  - component: {{fileID: 100102}}
  m_Layer: 0
  m_Name: Effect
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &100101
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100100}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: 0}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!82 &100102
AudioSource:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100100}}
  m_Enabled: 1
  serializedVersion: 4
  OutputAudioMixerGroup: {{fileID: 0}}
  m_audioClip: {{fileID: 8300000, guid: {clip}, type: 3}}
  m_PlayOnAwake: 1
  m_Volume: 1
  m_Pitch: 1
  Loop: 0
  Mute: 0
  Spatialize: 0
  SpatializePostEffects: 0
  Priority: 0
  DopplerLevel: 0
  MinDistance: 1
  MaxDistance: 500
  Pan2D: 0
  rolloffMode: 0
  BypassEffects: 1
  BypassListenerEffects: 1
  BypassReverbZones: 1
  panLevelCustomCurve:
    serializedVersion: 2
    m_Curve:
    - serializedVersion: 3
      time: 0
      value: 0
      inSlope: 0
      outSlope: 0
      tangentMode: 0
      weightedMode: 0
      inWeight: 0.33333334
      outWeight: 0.33333334
    m_PreInfinity: 2
    m_PostInfinity: 2
    m_RotationOrder: 4
"""

DAT = """GUID {guid}
Type Effect
ID {id}

// {name}: a 2D sound for one player, triggered by Hud/Sounds.cs. Written by tools/sounds.py.
Static
Lifetime {lifetime:.1f}
Lifetime_Spread 0
Preload 0
"""


def meta(path, file_guid, body=None):
    Path(f"{path}.meta").write_text(
        body or f"fileFormatVersion: 2\nguid: {file_guid}\n", encoding="utf-8"
    )


for index, name in enumerate(NAMES):
    source = SOUNDS / f"{name}.wav"
    folder = EFFECTS / folder_name(name)
    folder.mkdir(exist_ok=True)
    meta(
        folder,
        guid(name, "folder"),
        f"fileFormatVersion: 2\nguid: {guid(name, 'folder')}\nfolderAsset: yes\n",
    )

    clip = folder / f"{name}.wav"
    shutil.copy(source, clip)
    meta(clip, None, AUDIO_META.format(guid=guid(name, "clip")))

    prefab = folder / "Effect.prefab"
    prefab.write_text(PREFAB.format(clip=guid(name, "clip")), encoding="utf-8")
    meta(prefab, guid(name, "prefab"))

    dat = folder / f"{folder_name(name)}.dat"
    dat.write_text(
        DAT.format(
            guid=guid(name, "asset")[:32],
            id=FIRST_ID + index,
            name=name,
            lifetime=seconds(source) + 0.5,
        ),
        encoding="utf-8",
    )
    meta(dat, guid(name, "dat"))
    print(folder.name, FIRST_ID + index, guid(name, "asset"))
