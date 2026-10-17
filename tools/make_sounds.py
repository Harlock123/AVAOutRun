#!/usr/bin/env python3
"""Synthesises AVAOutRun's sound effects and tunes into ../src/AVAOutRun/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The announcer speaks through the formant voice in speech.py, the way the
talking arcade games of the day spoke, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAOutRun", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def echoed(dry, taps=((0.03, 0.15),)):
    """A little ring of the loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.3)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


def computer(words):
    """The announcer: a bright voice with a fifth humming under it, a little metallic."""
    voice = say(words, rate=RATE, pitch=142, speed=1.05, size=0.9)
    hum = say(words, rate=RATE, pitch=213, speed=1.05, size=0.9, seed=11)
    return echoed([a + 0.15 * b for a, b in zip(voice, hum)])


# The announcer's lines, written out as phonemes.
LINES = {
    "SayReady": "G EH T , R EH1 D IY !",
    "SayExtended": "EH K S T EH1 N D IH D , P L EY1 !",
    "SayGoal": "G OW1 L !",
}


def engine(freq, seconds=1.0):
    """An engine note that loops cleanly: a whole number of cycles of a rasping saw over a throbbing square."""
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        ph = freq * t
        s = voice("saw", ph) * 0.55 + voice("square", ph / 2) * 0.35 + voice("tri", ph * 2) * 0.2
        s *= 0.85 + 0.15 * math.sin(2 * math.pi * 8 * t)
        out.append(s + RNG.uniform(-1, 1) * 0.06)
    return out


def arp(chord, beats, step=0.25):
    notes = []
    for i in range(int(beats / step)):
        notes.append((chord[i % len(chord)], step))
    return notes


def drumbeat(bpm, bars, swing=False):
    """Kick on one and three, a snare on two and four, hats on the eighths."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            kick = tone(beat / 2, 120, 50, "sine", attack=0.002, release=1.5, gain=0.9) if b % 2 == 0 else silence(beat / 2)
            snare = tone(beat / 2, 2000, 900, "noise", attack=0.001, release=2.0, gain=0.5) if b % 2 == 1 else silence(beat / 2)
            hat = cat(tone(beat / 4, 7000, 6000, "noise", attack=0.001, release=3, gain=0.18), silence(beat / 4))
            half = mix(kick, snare, hat)
            out.extend(half[: int(RATE * beat / 2)])
            out.extend(cat(tone(beat / 4, 7000, 6000, "noise", attack=0.001, release=3, gain=0.18), silence(beat / 4))[: int(RATE * beat / 2)])
    return out


def song(bpm, lead, bass, chords, bars, lead_kind="pulse"):
    """A lead, a bass, rippling chords and the drums, all a whole number of bars so it loops."""
    beat = 60.0 / bpm
    total = int(RATE * beat * 4 * bars)
    parts = [seq(bpm, lead, kind=lead_kind, gain=0.5), seq(bpm, bass, kind="tri", gain=0.6, release=0.3), seq(bpm, chords, kind="square", gain=0.16, release=0.5), drumbeat(bpm, bars)]
    out = mix(*parts)
    return (out + [0.0] * total)[:total]


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The engine, at four bands of revs.
    for i, f in enumerate([60, 95, 140, 190]):
        write(f"Engine{i}.wav", engine(f), 0.35)
    write("Gear.wav", mix(tone(0.12, 300, 120, "square", attack=0.001, release=1.2, gain=0.5), tone(0.08, 1200, 400, "noise", release=1.5, gain=0.3)), 0.24)
    write("Bump.wav", mix(tone(0.2, 160, 60, "sine", attack=0.002, release=1.0), tone(0.15, 900, 200, "noise", release=1.2, gain=0.5)), 0.36)
    write("Crash.wav", cat(mix(tone(0.9, 2400, 100, "noise", attack=0.001, release=0.5), tone(0.8, 140, 40, "saw", vibrato=0.25, vib_rate=14, release=0.5, gain=0.6)),
                          tone(0.5, 600, 200, "noise", attack=0.02, release=0.8, gain=0.4)), 0.46)
    write("Skid.wav", tone(0.5, 2600, 2200, "noise", attack=0.02, vibrato=0.1, vib_rate=30, release=0.6), 0.16)
    write("Checkpoint.wav", cat(*[tone(0.12, hz(n), kind="sine", release=0.5) for n in ["E6", "G#6", "B6", "E7"]], tone(0.6, hz("E7"), kind="sine", vibrato=0.01, release=0.6)), 0.32)
    write("Beep.wav", tone(0.25, hz("A5"), kind="square", attack=0.002, release=0.3, gain=0.5), 0.24)
    write("StartBeep.wav", tone(0.25, hz("A5"), kind="square", attack=0.002, release=0.3, gain=0.5), 0.24)
    write("GoBeep.wav", tone(0.6, hz("A6"), kind="square", attack=0.002, release=0.4, gain=0.5), 0.26)
    write("Tick.wav", tone(0.04, 2000, kind="square", attack=0.001, release=1.0, gain=0.5), 0.14)
    write("ExtraTime.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7"]]), 0.28)
    for name, words in LINES.items():
        write(f"{name}.wav", computer(words), 0.75)

    # The radio (all original compositions).
    # Coastline Cruise: a breezy major-key tune, bright and sunny.
    lead = [("E5", 1), ("G#5", 0.5), ("B5", 1), ("A5", 0.5), ("G#5", 0.5), ("F#5", 0.5), ("E5", 1.5), ("C#5", 0.5), ("E5", 2),
            ("F#5", 1), ("A5", 0.5), ("C#6", 1), ("B5", 0.5), ("A5", 0.5), ("G#5", 0.5), ("F#5", 2), ("-", 2),
            ("E5", 1), ("G#5", 0.5), ("B5", 1), ("E6", 0.5), ("D#6", 0.5), ("C#6", 0.5), ("B5", 1.5), ("G#5", 0.5), ("A5", 2),
            ("F#5", 1), ("G#5", 0.5), ("A5", 1), ("B5", 0.5), ("G#5", 0.5), ("F#5", 0.5), ("E5", 2), ("-", 2)]
    bass = [(n, 0.5) for n in ["E2", "E3"] * 4 + ["C#2", "C#3"] * 4 + ["F#2", "F#3"] * 4 + ["B1", "B2"] * 4 + ["E2", "E3"] * 4 + ["A1", "A2"] * 4 + ["F#2", "F#3"] * 2 + ["B1", "B2"] * 2 + ["E2", "E3"] * 4]
    chords = arp(["E4", "G#4", "B4"], 4) + arp(["C#4", "E4", "G#4"], 4) + arp(["F#4", "A4", "C#5"], 4) + arp(["B3", "D#4", "F#4"], 4) + arp(["E4", "G#4", "B4"], 4) + arp(["A3", "C#4", "E4"], 4) + arp(["F#4", "A4", "B4"], 4) + arp(["E4", "G#4", "B4"], 4)
    write("Station0.wav", song(138, lead, bass, chords, 8), 0.3)
    # Silver Breeze: a lilting, latin-flavoured line in A minor.
    lead = [("A4", 0.75), ("C5", 0.75), ("E5", 0.5), ("D5", 1), ("C5", 1), ("B4", 0.75), ("D5", 0.75), ("G5", 0.5), ("F5", 1), ("E5", 1),
            ("F5", 0.75), ("E5", 0.75), ("D5", 0.5), ("C5", 1), ("B4", 1), ("C5", 0.75), ("B4", 0.75), ("G#4", 0.5), ("A4", 2),
            ("E5", 0.75), ("F5", 0.75), ("G5", 0.5), ("A5", 1), ("G5", 1), ("F5", 0.75), ("E5", 0.75), ("D5", 0.5), ("E5", 2),
            ("C5", 0.75), ("D5", 0.75), ("E5", 0.5), ("D5", 1), ("B4", 1), ("A4", 4)]
    bass = [(n, 0.75 if i % 3 != 2 else 0.5) for i, n in enumerate(["A2", "E2", "A2"] * 2 + ["G2", "D2", "G2"] * 2 + ["F2", "C2", "F2"] * 2 + ["E2", "B1", "E2"] * 2 + ["A2", "E2", "A2"] * 2 + ["C3", "G2", "C3"] * 2 + ["F2", "C2", "F2"] * 2 + ["E2", "B1", "E2"] * 2)]
    chords = arp(["A3", "C4", "E4"], 4, 0.5) + arp(["G3", "B3", "D4"], 4, 0.5) + arp(["F3", "A3", "C4"], 4, 0.5) + arp(["E3", "G#3", "B3"], 4, 0.5) + arp(["A3", "C4", "E4"], 4, 0.5) + arp(["C4", "E4", "G4"], 4, 0.5) + arp(["F3", "A3", "C4"], 4, 0.5) + arp(["E3", "G#3", "B3"], 4, 0.5)
    write("Station1.wav", song(128, lead, bass, chords, 8, lead_kind="tri"), 0.3)
    # Night Highway: cool and driving in D minor.
    lead = [("-", 1), ("D5", 0.5), ("F5", 0.5), ("A5", 1), ("G5", 1), ("F5", 1), ("E5", 1), ("D5", 2),
            ("-", 1), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("F5", 1), ("E5", 1), ("C5", 1), ("D5", 2),
            ("-", 1), ("D5", 0.5), ("F5", 0.5), ("A5", 1), ("C6", 1), ("A#5", 1), ("A5", 1), ("G5", 2),
            ("F5", 1), ("E5", 1), ("D5", 1), ("C#5", 1), ("D5", 4)]
    bass = [(n, 0.5) for n in ["D2", "D2", "D3", "D2"] * 2 + ["C2", "C2", "C3", "C2"] * 2 + ["A#1", "A#1", "A#2", "A#1"] * 2 + ["A1", "A1", "A2", "A1"] * 2]
    chords = arp(["D4", "F4", "A4"], 8) + arp(["C4", "E4", "G4"], 8) + arp(["A#3", "D4", "F4"], 8) + arp(["A3", "C#4", "E4"], 8)
    write("Station2.wav", song(132, lead, bass, chords, 8, lead_kind="saw"), 0.28)

    title_lead = [("E5", 0.5), ("G#5", 0.5), ("B5", 1), ("C#6", 0.5), ("B5", 0.5), ("G#5", 1), ("A5", 0.5), ("B5", 0.5), ("C#6", 1), ("E6", 2),
                  ("D#6", 0.5), ("C#6", 0.5), ("B5", 1), ("G#5", 1), ("A5", 0.5), ("G#5", 0.5), ("F#5", 1), ("E5", 2)]
    title_bass = [(n, 0.5) for n in ["E2", "E3"] * 4 + ["A1", "A2"] * 4 + ["B1", "B2"] * 4 + ["E2", "E3"] * 4]
    write("TitleTune.wav", cat(tune(124, title_lead, title_bass, kind="pulse"), silence(0.8)), 0.36)
    write("Victory.wav", tune(120, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G#6", 3)],
                              [("E2", 1), ("B1", 1), ("C#2", 1), ("A1", 1), ("B1", 1), ("E2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E2", 2), ("C2", 2), ("B1", 2), ("E1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
