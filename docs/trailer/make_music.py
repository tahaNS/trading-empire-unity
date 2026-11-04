#!/usr/bin/env python3
"""Writes a short original soundtrack (soft pad, plucked arpeggio, bass, light percussion) to a WAV file.

Usage: python3 make_music.py out.wav [seconds]
"""
import array, math, random, struct, sys, wave

OUT = sys.argv[1] if len(sys.argv) > 1 else "music.wav"
SECONDS = float(sys.argv[2]) if len(sys.argv) > 2 else 80.0
SR = 44100
BPM = 96
BEAT = 60.0 / BPM
N = int(SR * SECONDS)
buf = [0.0] * N
random.seed(3)

def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12.0)

# Am - F - C - G, one chord per bar (4 beats)
CHORDS = [(57, [57, 60, 64]), (53, [53, 57, 60]), (48, [55, 60, 64]), (55, [55, 59, 62])]

def add_tone(start, dur, freq, amp, attack, release, harmonics):
    i0 = int(start * SR)
    n = int(dur * SR)
    for k in range(n):
        i = i0 + k
        if i >= N:
            break
        t = k / SR
        env = min(1.0, t / attack) * min(1.0, (dur - t) / release) if dur - t < release else min(1.0, t / attack)
        v = 0.0
        for h, a in harmonics:
            v += a * math.sin(2 * math.pi * freq * h * t)
        buf[i] += v * env * amp

def pluck(start, freq, amp):
    i0 = int(start * SR)
    n = int(0.55 * SR)
    for k in range(n):
        i = i0 + k
        if i >= N:
            break
        t = k / SR
        env = math.exp(-t * 7.0)
        v = math.sin(2 * math.pi * freq * t) + 0.35 * math.sin(2 * math.pi * freq * 2 * t) * math.exp(-t * 12)
        buf[i] += v * env * amp

def kick(start, amp):
    i0 = int(start * SR)
    n = int(0.22 * SR)
    for k in range(n):
        i = i0 + k
        if i >= N:
            break
        t = k / SR
        f = 120 * math.exp(-t * 18) + 42
        buf[i] += math.sin(2 * math.pi * f * t) * math.exp(-t * 14) * amp

def hat(start, amp):
    i0 = int(start * SR)
    n = int(0.05 * SR)
    for k in range(n):
        i = i0 + k
        if i >= N:
            break
        buf[i] += (random.random() * 2 - 1) * math.exp(-k / SR * 90) * amp

bars = int(SECONDS / (4 * BEAT)) + 1
for b in range(bars):
    root, notes = CHORDS[b % 4]
    t0 = b * 4 * BEAT
    intense = 0.0 if t0 < 6 else 1.0
    # pad
    for m in notes:
        add_tone(t0, 4 * BEAT + 0.6, hz(m), 0.045, 0.9, 1.2, [(1, 1.0), (2.003, 0.18)])
        add_tone(t0, 4 * BEAT + 0.6, hz(m) * 1.004, 0.03, 0.9, 1.2, [(1, 1.0)])
    # bass
    add_tone(t0, 2 * BEAT, hz(root - 12), 0.16, 0.02, 0.35, [(1, 1.0), (2, 0.25)])
    add_tone(t0 + 2 * BEAT, 2 * BEAT, hz(root - 12), 0.13, 0.02, 0.35, [(1, 1.0), (2, 0.25)])
    # arpeggio, eighth notes
    pattern = [0, 1, 2, 1, 2, 1, 0, 1] if b % 2 == 0 else [2, 1, 0, 1, 2, 1, 2, 1]
    for s in range(8):
        m = notes[pattern[s]] + 12
        pluck(t0 + s * BEAT / 2, hz(m), 0.11 * (0.4 + 0.6 * intense))
    # light drums once the intro is over
    if intense:
        for beat in range(4):
            kick(t0 + beat * BEAT, 0.28 if beat % 2 == 0 else 0.0)
            hat(t0 + beat * BEAT + BEAT / 2, 0.05)

# fade in and out, soft limiter, write 16 bit mono
peak = max(abs(x) for x in buf) or 1.0
out = array.array("h")
for i, x in enumerate(buf):
    t = i / SR
    f = min(1.0, t / 2.5) * min(1.0, (SECONDS - t) / 4.0)
    y = math.tanh(x / peak * 1.6) * f * 0.55
    out.append(int(y * 32767))
with wave.open(OUT, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes(out.tobytes())
print("wrote", OUT)
