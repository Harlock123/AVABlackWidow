#!/usr/bin/env python3
"""Synthesises AVABlackWidow's sound effects and tunes into ../src/AVABlackWidow/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVABlackWidow", "Sounds")
RNG = random.Random(1982)


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


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The spider's gun and what it hits.
    write("Fire.wav", mix(tone(0.07, 2400, 900, "square", release=1.5), tone(0.05, 3600, 1500, "tri", release=1.5, gain=0.4)), 0.22)
    write("Hit.wav", mix(tone(0.16, 1400, 200, "noise", release=0.9), tone(0.12, 700, 180, "square", release=1.0, gain=0.4)), 0.4)
    write("Thud.wav", mix(tone(0.1, 220, 120, "square", release=1.0), tone(0.08, 600, 200, "noise", release=1.2, gain=0.4)), 0.35)
    write("Blast.wav", mix(tone(0.9, 500, 40, "noise", release=0.6), tone(0.7, 130, 30, "sine", release=0.6, gain=0.8)), 0.6)
    write("Coin.wav", cat(tone(0.06, hz("B5"), kind="pulse", release=0.3), tone(0.16, hz("E6"), kind="pulse", release=0.6)), 0.35)
    write("Lay.wav", tone(0.12, 500, 260, "sine", release=0.6), 0.35)
    write("Hatch.wav", cat(tone(0.05, 1800, 1400, "noise", release=0.8), tone(0.12, 600, 1200, "tri", release=0.6)), 0.35)
    write("Eat.wav", cat(tone(0.06, 300, 200, "square", release=0.6), tone(0.06, 260, 180, "square", release=0.6)), 0.35)
    write("Slayer.wav", mix(tone(0.8, 180, 900, "saw", release=0.4, vibrato=0.1, vib_rate=12), tone(0.8, 120, 600, "square", release=0.4, gain=0.3)), 0.4)
    write("Death.wav", cat(*[tone(0.09, f, f * 0.7, "square", release=0.4) for f in [900, 760, 640, 540, 450, 380, 320]], tone(0.6, 250, 60, "noise", release=0.5)), 0.5)
    write("WaveStart.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.4) for n in ["D5", "F5", "A5"]], tone(0.4, hz("D6"), kind="pulse", release=0.7)), 0.38)
    write("WaveClear.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["D5", "F#5", "A5", "D6", "F#6", "A6"]], tone(0.5, hz("D7"), kind="pulse", release=0.6)), 0.4)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["A5", "D6", "F#6", "A6", "D7"]]), 0.4)

    # The web: a creeping minor bass line under a skittering arpeggio (new). Loops.
    bass_notes = []
    for root in ["D2", "D2", "F2", "E2", "D2", "D2", "Bb1", "A1"] * 2:
        bass_notes += [(root, 0.5), ("-", 0.25), (root, 0.25), (root, 0.5), ("-", 0.5)]
    arp = []
    for chord in [["D4", "F4", "A4"], ["D4", "F4", "A4"], ["F4", "A4", "C5"], ["E4", "G4", "Bb4"], ["D4", "F4", "A4"], ["D4", "F4", "A4"], ["Bb3", "D4", "F4"], ["A3", "C#4", "E4"]] * 2:
        arp += [(chord[0], 0.25), (chord[1], 0.25), (chord[2], 0.25), (chord[1], 0.25)] * 2
    bass = seq(132, bass_notes, kind="tri", gain=0.55, release=0.5)
    top = seq(132, arp, kind="pulse", gain=0.16, release=0.8)
    write("Music.wav", mix(bass, top), 0.4)

    write("TitleTune.wav", tune(108,
                                [("D5", 1), ("A4", 0.5), ("D5", 0.5), ("F5", 1), ("E5", 1), ("D5", 0.5), ("C#5", 0.5), ("A4", 1), ("D5", 2),
                                 ("F5", 1), ("G5", 0.5), ("A5", 0.5), ("Bb5", 1), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("C#5", 1), ("D5", 2)],
                                [("D3", 2), ("Bb2", 2), ("A2", 2), ("D3", 2), ("Bb2", 2), ("G2", 2), ("A2", 2), ("D3", 2)], kind="tri"), 0.45)
    write("StartTune.wav", tune(150, [("D5", 0.5), ("F5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 2)], [("D3", 2), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("A5", 1), ("G5", 1), ("F5", 1), ("E5", 1), ("D5", 3)], [("D4", 2), ("Bb3", 2), ("A3", 1), ("D3", 3)], kind="tri"), 0.45)

if __name__ == "__main__":
    main()
