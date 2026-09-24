"""Renders every PeekSeek sound concept to mp3. Run: python make_sounds.py (needs numpy + ffmpeg)."""
import subprocess, sys, wave
from pathlib import Path
import numpy as np

RATE = 48000
OUT = Path(__file__).parent
rng = np.random.default_rng(7)

# Everything is in D major; the ladders use the D major pentatonic so overlapping tails never clash.
D4, Fs4, A4, Bb4, B4, Cs5, D5, E5, F5, Fs5, G5, A5, B5, Cs6, D6, E6, Fs6, A6, B6, D7 = (
    62, 66, 69, 70, 71, 73, 74, 76, 77, 78, 79, 81, 83, 85, 86, 88, 90, 93, 95, 98)


def hz(midi):
    return 440 * 2 ** ((midi - 69) / 12)


def time_axis(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def envelope(t, attack, decay):
    return np.minimum(t / attack, 1) * np.exp(-t / decay)


# Three voices, one per concept family. Each takes (frequency, seconds, velocity 0..1).
def bell(f, dur, vel):
    """A: FM glass bell. Harmonic 1:2 ratio, brightness falls off as the note rings."""
    t = time_axis(dur + 0.6)
    index = (1.2 + 2.2 * vel) * np.exp(-t / 0.08)
    tone = np.sin(2 * np.pi * f * t + index * np.sin(2 * np.pi * 2 * f * t))
    shimmer = 0.18 * np.sin(2 * np.pi * f * 4.01 * t) * np.exp(-t / 0.05)
    return (tone + shimmer) * envelope(t, 0.002, dur / 3.2)


def tactile(f, dur, vel):
    """B: dry marimba/woodblock. Marimba partials 1 : 3.9 : 9.2, a tiny pitch drop on the strike."""
    t = time_axis(dur + 0.3)
    bend = 1 + 0.025 * np.exp(-t / 0.008)
    phase = 2 * np.pi * f * np.cumsum(bend) / RATE
    out = np.sin(phase) * np.exp(-t / (dur / 3))
    out += 0.35 * vel * np.sin(3.9 * phase) * np.exp(-t / (dur / 9))
    out += 0.12 * vel * np.sin(9.2 * phase) * np.exp(-t / 0.012)
    return out * np.minimum(t / 0.001, 1)


def arcade(f, dur, vel):
    """C: soft pulse synth, two voices detuned by 6 cents, harmonics limited by velocity."""
    t = time_axis(dur + 0.3)
    out = np.zeros_like(t)
    top = int(6 + 10 * vel)
    for detune in (2 ** (6 / 1200), 2 ** (-6 / 1200)):
        for n in range(1, top):
            if f * n * detune > 14000:
                break
            weight = np.sin(np.pi * n * 0.25) / n  # 25% pulse
            out += weight * np.sin(2 * np.pi * f * n * detune * t) * np.exp(-t * n * 0.9)
    return out * envelope(t, 0.003, dur / 2.6)


def click(level):
    """A 4 ms noise snap: the 'contact' that tells the body something landed."""
    noise = np.diff(rng.standard_normal(int(0.004 * RATE) + 1))
    return level * noise * np.linspace(1, 0, noise.size) * 0.3


def thump(level, dur=0.09):
    """A sub drop from 120 to 50 Hz: weight you feel, below where footsteps live."""
    t = time_axis(dur)
    sweep = 50 + 70 * np.exp(-t / 0.025)
    return level * np.sin(2 * np.pi * np.cumsum(sweep) / RATE) * np.exp(-t / (dur / 3))


def punch(level, dur=0.14, top=170, bottom=45, drive=2.5):
    """A saturated body hit: a kick that drops from top to bottom Hz, driven so it bites."""
    t = time_axis(dur)
    sweep = bottom + (top - bottom) * np.exp(-t / 0.02)
    body = np.sin(2 * np.pi * np.cumsum(sweep) / RATE) * np.exp(-t / (dur / 3))
    return level * np.tanh(drive * body) / np.tanh(drive)


def crack(level, dur=0.025, smooth=3):
    """A 25 ms band of noise: the snap of the hit landing."""
    t = time_axis(dur)
    noise = np.convolve(np.diff(rng.standard_normal(t.size + 1)), np.ones(smooth) / smooth, mode="same")
    return level * 0.4 * noise * np.exp(-t / 0.005)


def steel(f, dur, vel):
    """Struck steel plate: inharmonic partials, two copies 3 Hz apart so it shimmers."""
    t = time_axis(dur + 0.3)
    out = np.zeros_like(t)
    for i, (ratio, amp) in enumerate(zip((1, 2.76, 5.40, 8.93), (1, 0.5 * vel, 0.25 * vel, 0.12 * vel))):
        for beat in (0, 3):
            out += amp * np.sin(2 * np.pi * (f * ratio + beat) * t) * np.exp(-t / (dur / (3 * (i + 1))))
    return out * np.minimum(t / 0.001, 1) / 2


def growl(f, dur, vel):
    """A low power chord (root, fifth, octave), overdriven: dark and heavy, no happy third."""
    t = time_axis(dur + 0.1)
    phase = 2 * np.pi * f * np.cumsum(1 + 0.04 * np.exp(-t / 0.01)) / RATE
    chord = np.sin(phase) + 0.7 * np.sin(1.5 * phase) + 0.4 * np.sin(2 * phase)
    out = np.tanh((2 + 2 * vel) * chord) * np.exp(-t / (dur / 3))
    return np.convolve(out, np.ones(8) / 8, mode="same")  # take the fizz off the distortion


def membrane(f, dur, vel):
    """A tuned low drum: the pitch falls onto f, like a floor tom hit hard."""
    t = time_axis(dur + 0.2)
    phase = 2 * np.pi * f * np.cumsum(1 + 0.6 * np.exp(-t / 0.03)) / RATE
    out = np.sin(phase) + 0.3 * vel * np.sin(2.3 * phase) * np.exp(-t / 0.04)
    return out * np.exp(-t / (dur / 3))


def air(level, dur=0.45):
    """Dark noise tail: the room answering the hit."""
    t = time_axis(dur)
    noise = np.convolve(rng.standard_normal(t.size), np.ones(40) / 40, mode="same")
    return level * 3 * noise * np.minimum(t / 0.01, 1) * np.exp(-t / (dur / 4))


def room(signal, wet, decay=0.14):
    t = time_axis(0.7)
    ir = rng.standard_normal(t.size) * np.exp(-t / decay)
    ir = np.convolve(ir, np.ones(12) / 12, mode="same")  # darken the tail
    n = signal.size + ir.size
    tail = np.fft.irfft(np.fft.rfft(signal, n) * np.fft.rfft(ir, n), n)[: signal.size + ir.size - 1]
    tail *= np.max(np.abs(signal)) / (np.max(np.abs(tail)) + 1e-9)
    out = np.zeros(tail.size)
    out[: signal.size] += signal
    return out + wet * tail


def render(voice, notes, layers=(), wet=0.12, echo=0.0):
    """notes: (start, midi, seconds, velocity). layers: (start, pre-rendered array)."""
    parts = [(start, vel * voice(hz(m), dur, vel)) for start, m, dur, vel in notes] + list(layers)
    length = max(start * RATE + part.size for start, part in parts)
    mix = np.zeros(int(length) + 1)
    for start, part in parts:
        i = int(start * RATE)
        mix[i : i + part.size] += part
    if echo:
        gap = int(0.095 * RATE)
        mix = np.concatenate([mix, np.zeros(gap)])
        mix[gap:] += echo * mix[:-gap].copy()
    return room(mix, wet)


def save(signal, folder, name, loudness_db, saturate=False):
    """Scale so the loudest 100 ms sits at loudness_db RMS, cap peaks at -1 dBFS, trim silence.
    saturate rounds the peaks off with tanh instead of turning the whole sound down: denser, a little grit."""
    win = int(0.1 * RATE)
    rms = np.sqrt(np.convolve(signal**2, np.ones(win) / win, mode="valid").max())
    signal = signal * 10 ** (loudness_db / 20) / rms
    ceiling = 10 ** (-1 / 20)
    if saturate:
        signal = ceiling * np.tanh(signal / ceiling)
    signal *= min(1, ceiling / np.max(np.abs(signal)))
    audible = np.nonzero(np.abs(signal) > 10 ** (-60 / 20))[0]
    signal = signal[: audible[-1] + 1]
    fade = min(int(0.02 * RATE), signal.size)
    signal[-fade:] *= np.linspace(1, 0, fade)

    target = OUT / folder
    target.mkdir(exist_ok=True)
    wav = target / f"{name}.wav"
    with wave.open(str(wav), "wb") as f:
        f.setnchannels(1), f.setsampwidth(2), f.setframerate(RATE)
        f.writeframes((signal * 32767).astype(np.int16).tobytes())
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(wav),
                    "-codec:a", "libmp3lame", "-b:a", "192k", str(target / f"{name}.mp3")], check=True)


# ---- kills: an impact first, the pitch of the ring is the streak count ----

KILL_LADDER = [D5, E5, Fs5, A5, B5]


def kill_layers(style, step, start=0.0):
    """Kill n of a round in one style, as (start, array) layers. Every hit lands at sample zero: no pickup."""
    land = KILL_LADDER[step - 1]
    grow = 0.85 + 0.06 * step
    ace = step == 5
    if style == "A_steel":
        parts = [(0, punch(0.8 * grow)), (0, crack(0.9)), (0, grow * steel(hz(land), 0.45 + 0.05 * step, grow))]
        if step >= 3:
            parts.append((0, 0.35 * steel(hz(land + 12), 0.3, 0.6)))
        if ace:
            parts += [(0.12, 0.6 * steel(hz(land + 7), 0.9, 1)), (0.12, 0.4 * steel(hz(land + 12), 0.9, 1))]
    elif style == "B_punch":
        parts = [(0, punch(1.1 * grow, drive=4)), (0, crack(1.1, smooth=2)),
                 (0, 0.55 * grow * growl(hz(land - 24), 0.28, grow)),
                 (0, 0.6 * tactile(hz(land + 12), 0.24, 1))]  # the high tick carries the ladder
        if step >= 3:
            parts.append((0, punch(0.5, dur=0.25, top=90, bottom=35)))
        if ace:
            parts += [(0.12, punch(1.0, drive=4)), (0.12, 0.5 * growl(hz(land - 17), 0.6, 1))]
    else:  # C_boom
        parts = [(0, punch(0.7 * grow, dur=0.25, bottom=35)), (0, crack(0.6, smooth=5)),
                 (0, 0.8 * grow * membrane(hz(land - 24), 0.6, grow)), (0, air(0.4 * grow)),
                 (0, 0.3 * steel(hz(land + 12), 0.5, 0.7))]
        if ace:
            parts += [(0.14, 0.7 * membrane(hz(land - 17), 1.0, 1)), (0.14, air(0.5, 0.9)),
                      (0.14, 0.35 * steel(hz(land + 19), 1.0, 1))]
    return [(start + s, part) for s, part in parts]


KILL_STYLES = {"A_steel": 0.08, "B_punch": 0.04, "C_boom": 0.12}  # style: reverb

# ---- everything else: note scores shared by all three families ----

SCORES = {
    "round_win": (-14, [[(0, D5, 0.2, 0.8), (0.07, Fs5, 0.2, 0.85), (0.14, A5, 0.25, 0.9),
                         (0.21, D6, 0.9, 1), (0.21, D4, 0.9, 0.5), (0.21, A5, 0.7, 0.4)]]),
    "round_loss": (-20, [[(0, A5, 0.25, 0.5), (0.13, F5, 0.3, 0.45), (0.26, D5, 0.8, 0.45),
                          (0.26, D4, 0.8, 0.3)]]),
    "match_point": (-17, [[(t, m, 0.35, 0.7) for t in (0, 0.28) for m in (D5, G5, A5)]
                          + [(0.56, D6, 0.6, 0.6), (0.56, G5, 0.6, 0.5)]]),
    "match_win": (-13, [[(0, A4, 0.3, 0.7), (0, Cs5, 0.3, 0.7), (0, E5, 0.3, 0.7),
                         (0.3, A5, 0.07, 0.7), (0.345, D6, 0.4, 1),
                         (0.45, Fs6, 0.3, 0.8), (0.52, A6, 0.3, 0.8),
                         (0.62, D5, 1.6, 0.8), (0.62, Fs5, 1.6, 0.7), (0.62, A5, 1.6, 0.7),
                         (0.62, D6, 1.8, 0.9), (0.62, D4, 1.8, 0.6)]]),
    "match_loss": (-19, [[(0, D6, 0.3, 0.5), (0.18, A5, 0.3, 0.45), (0.36, F5, 0.35, 0.4),
                          (0.54, D5, 1.2, 0.45), (0.54, A4, 1.2, 0.35), (0.54, D4, 1.2, 0.35)]]),
    "queue_join": (-19, [[(0, D5, 0.15, 0.55), (0.09, A5, 0.35, 0.6)]]),
    "queue_leave": (-22, [[(0, A5, 0.15, 0.45), (0.09, D5, 0.35, 0.45)]]),
    "match_found": (-11, [[(0, A5, 0.07, 0.9), (0.05, D6, 0.18, 1), (0.28, A5, 0.07, 0.9), (0.33, D6, 0.18, 1),
                           (0.6, D6, 0.2, 1), (0.66, Fs6, 0.2, 1), (0.72, A6, 0.2, 1), (0.78, D7, 0.7, 1),
                           (0.78, D6, 0.6, 0.6)]]),
    # 5 4 3 2 1 climb the kill ladder, so higher always means closer; 3 2 1 get heavier; GO is the only one with a bass hit.
    "round_start": (-15, [[(i, m, 0.1, v) for i, (m, v) in enumerate(zip(KILL_LADDER, (0.45, 0.5, 0.75, 0.85, 0.95)))]
                          + [(5, D6, 0.7, 1), (5, A5, 0.7, 0.6), (5, D5, 0.8, 0.7), (5, D4, 0.8, 0.6)]]),
    # Two pulses with a semitone rub (A against B flat): the only note outside D major in the whole set, so it means danger.
    "time_10s": (-15, [[(t, m, 0.18, 0.75) for t in (0, 0.2) for m in (A4, Bb4)] + [(0, D4, 0.4, 0.5)]]),
    "pick_lock": (-17, [[(0, D5, 0.12, 0.8), (0, A5, 0.12, 0.8), (0.06, D6, 0.2, 0.7)]]),
}

CONCEPTS = {
    "A_bell": dict(voice=bell, wet=0.14, echo=0.0, click=0.5, thump=0.25),
    "B_tactile": dict(voice=tactile, wet=0.06, echo=0.0, click=1.0, thump=0.6),
    "C_arcade": dict(voice=arcade, wet=0.1, echo=0.28, click=0.3, thump=0.2),
}

# Sounds with a physical hit under the tone: (start, strength) for the click and thump.
HITS = {"pick_lock": [(0, 1)], "round_start": [(2, 0.5), (3, 0.7), (4, 0.9), (5, 1.8)], "time_10s": [(0, 0.8), (0.2, 0.8)]}

only = set(sys.argv[1:])  # render just these sounds, so a sound you already like never changes
for name, (loudness, (notes,)) in SCORES.items():
    if only and name not in only:
        continue
    for concept, c in CONCEPTS.items():
        layers = [(s, click(c["click"] * k)) for s, k in HITS.get(name, [])]
        layers += [(s, thump(c["thump"] * k)) for s, k in HITS.get(name, [])]
        save(render(c["voice"], notes, layers, c["wet"], c["echo"]), name, concept, loudness)
    print("made", name)


def mix(layers):
    out = np.zeros(int(max(s * RATE + part.size for s, part in layers)) + 1)
    for s, part in layers:
        out[int(s * RATE) : int(s * RATE) + part.size] += part
    return out


for style, wet in KILL_STYLES.items():
    if not only or "kill" in only:
        for step in range(1, 6):  # one file per rung of the ladder, for the game
            save(room(mix(kill_layers(style, step)), wet), "kill", style if step == 1 else f"{style}_{step}", -9, saturate=True)
    if not only or "kill_streak" in only:
        streak = sum((kill_layers(style, k, (k - 1) * 0.7) for k in range(1, 6)), [])
        save(room(mix(streak), wet), "kill_streak", style, -9, saturate=True)
print("made kills")


# ---- ship: copy the chosen concepts to where the game and the site read them ----
# Steel for kills, tactile for everything else. The game takes WAV (MP3 pads its start),
# the site MP3. The Unity folders and their prefabs are written by the ui's tools/sounds.py.
CHOSEN = {f"kill_{k}": ("kill", "A_steel" if k == 1 else f"A_steel_{k}") for k in range(1, 6)} | {
    name: (name, "B_tactile") for name in SCORES if name != "pick_lock"} | {"pick_lock": ("pick_lock", "B_tactile")}

if "ship" in only:
    import shutil
    repo = OUT.parent
    game = repo / "plugins/peekseek/ui/Assets/PeekSeekKillFeed/Sounds"
    site = repo / "website/public/sounds"
    game.mkdir(exist_ok=True), site.mkdir(exist_ok=True)
    for name, (folder, concept) in CHOSEN.items():
        shutil.copy(OUT / folder / f"{concept}.wav", game / f"{name}.wav")
        shutil.copy(OUT / folder / f"{concept}.mp3", site / f"{name}.mp3")
    # The countdown ships as one clip per second, so a gamemode with any length of
    # start phase plays 3 2 1 GO or 5 4 3 2 1 GO on its own clock.
    with wave.open(str(game / "round_start.wav")) as f:
        params, frames = f.getparams(), f.readframes(f.getnframes())
    second = RATE * 2
    for i in range(6):
        name = "go" if i == 5 else f"count_{5 - i}"
        with wave.open(str(game / f"{name}.wav"), "wb") as f:
            f.setparams(params)
            f.writeframes(frames[i * second :] if i == 5 else frames[i * second : (i + 1) * second])
    (game / "round_start.wav").unlink()
    print("shipped", len(CHOSEN))
