# The game's sound effects, synthesized (numpy only): no recorded samples to
# license, every one tunable and reproducible from a seed. Writes 48 kHz WAVs
# (16-bit) and, for checking by eye, a waveform + spectrogram sheet.
# Run with Blender's Python (it has numpy):  python -I sfx.py OUT_DIR [PREVIEW_DIR]
import os, sys, wave, struct
import numpy as np

SR = 48000
OUT = sys.argv[1]
PREV = sys.argv[2] if len(sys.argv) > 2 else None
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(0x1A77)


# ---- building blocks --------------------------------------------------------

def t_axis(secs):
    return np.arange(int(secs * SR)) / SR


def noise(secs, seed=None):
    r = np.random.default_rng(seed) if seed is not None else rng
    return r.standard_normal(int(secs * SR))


def env_exp(secs, tau, attack=0.0005):
    t = t_axis(secs)
    a = np.clip(t / max(attack, 1e-6), 0, 1)
    return a * np.exp(-t / tau)


def band(x, lo, hi, order=2):
    """Zero-phase band-pass in the frequency domain, with smooth (Butterworth-like) edges."""
    n = len(x)
    N = 1 << int(np.ceil(np.log2(n * 2)))
    X = np.fft.rfft(x, N)
    f = np.fft.rfftfreq(N, 1 / SR)
    g = np.ones_like(f)
    if lo > 0:
        g *= 1 / np.sqrt(1 + (lo / np.maximum(f, 1e-3)) ** (2 * order))
    if hi < SR / 2:
        g *= 1 / np.sqrt(1 + (f / hi) ** (2 * order))
    return np.fft.irfft(X * g, N)[:n]


def lowpass(x, hi, order=2):
    return band(x, 0, hi, order)


def highpass(x, lo, order=2):
    return band(x, lo, SR / 2, order)


def tilt(x, db_per_oct):
    """A spectral tilt (pinkish when negative), zero-phase."""
    n = len(x)
    N = 1 << int(np.ceil(np.log2(n * 2)))
    X = np.fft.rfft(x, N)
    f = np.maximum(np.fft.rfftfreq(N, 1 / SR), 20.0)
    g = (f / 1000.0) ** (db_per_oct / 6.02)
    return np.fft.irfft(X * g, N)[:n]


def sweep(secs, f0, f1, curve=4.0, shape="sine"):
    """A pitch glide from f0 to f1 (exponential-ish)."""
    t = t_axis(secs)
    k = np.exp(-t / (secs / curve))
    f = f1 + (f0 - f1) * k
    ph = 2 * np.pi * np.cumsum(f) / SR
    if shape == "saw":
        return 2 * ((ph / (2 * np.pi)) % 1.0) - 1
    if shape == "square":
        return np.sign(np.sin(ph))
    return np.sin(ph)


def ring(secs, freqs, taus, amps):
    t = t_axis(secs)
    out = np.zeros_like(t)
    for f, tau, a in zip(freqs, taus, amps):
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / tau)
    return out


def pad(x, secs):
    n = int(secs * SR)
    return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n] if n > len(x) else x[:n]


def place(dst, src, at, gain=1.0):
    i = int(at * SR)
    n = min(len(src), len(dst) - i)
    if n > 0:
        dst[i:i + n] += src[:n] * gain
    return dst


def convolve(x, ir):
    n = len(x) + len(ir) - 1
    N = 1 << int(np.ceil(np.log2(n)))
    return np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(ir, N), N)[:n]


def outdoor_ir(secs, seed, ground=0.006, ground_gain=0.45, slaps=((0.09, -20.0), (0.19, -24.0)),
               tail_db=-18.0, bright=6000.0, decay=0.35):
    """An open field's response: the direct sound, the ground's bounce, a couple
    of distant slaps (tree lines, slopes; each a short diffuse burst, given in dB
    below the direct sound), then a sparse tail that darkens as it decays, its
    energy `tail_db` below the direct sound's."""
    r = np.random.default_rng(seed)
    n = int(secs * SR)
    ir = np.zeros(n)
    ir[0] = 1.0
    ir[int(ground * SR)] += ground_gain
    t = np.arange(n) / SR
    for at, db in slaps:
        k = int(at * SR * r.uniform(0.9, 1.1))
        if k < n - 480:
            burst = lowpass(r.standard_normal(480) * np.exp(-np.arange(480) / 90.0), bright * 0.6)
            burst *= 10 ** (db / 20) / np.sqrt(np.sum(burst ** 2) + 1e-12)
            ir[k:k + 480] += burst
    dens = np.clip(0.03 + t * 3.0, 0, 1)
    raw = r.standard_normal(n) * (r.random(n) < dens) * np.clip((t - 0.015) / 0.04, 0, 1)
    tail = lowpass(raw * np.exp(-t / (decay * 0.45)), bright) + lowpass(raw * np.exp(-t / decay), 1200) * 0.8
    tail *= 10 ** (tail_db / 20) / np.sqrt(np.sum(tail ** 2) + 1e-12)
    return ir + tail


def normalize(x, peak_db=-1.0):
    p = np.max(np.abs(x)) + 1e-9
    return x / p * 10 ** (peak_db / 20)


def fade(x, fin=0.0005, fout=0.01):
    n = len(x)
    a = np.ones(n)
    i = int(fin * SR)
    o = int(fout * SR)
    if i > 0:
        a[:i] = np.linspace(0, 1, i)
    if o > 0:
        a[-o:] *= np.linspace(1, 0, o) ** 2
    return x * a


def softclip(x, drive=1.0):
    return np.tanh(x * drive) / np.tanh(drive)


def write(name, x, stereo=False):
    if ONLY and not any(name.startswith(o) for o in ONLY):
        return
    path = os.path.join(OUT, name + ".wav")
    if stereo:
        L, R = x
        data = np.stack([L, R], axis=1)
    else:
        data = x[:, None]
    pcm = np.clip(data, -1, 1)
    pcm = (pcm * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2 if stereo else 1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    made.append((name, x if not stereo else (x[0] + x[1]) * 0.5))
    print("wrote", name, "%.2f s" % ((len(x[0]) if stereo else len(x)) / SR))


made = []
ONLY = [o for o in os.environ.get("SFX_ONLY", "").split(",") if o]


# ---- the rifle --------------------------------------------------------------

def rifle_dry(seed, close=1.0):
    """One shot, dry, at the shooter: crack, body, a short energy zap, the action."""
    r = np.random.default_rng(seed)
    L = 0.35
    out = np.zeros(int(L * SR))
    # The muzzle report's front: hard, broadband, gone in a few milliseconds.
    crack = highpass(noise(0.03, seed), 700) * env_exp(0.03, 0.0032 * r.uniform(0.9, 1.15), 0.00006)
    place(out, crack, 0, 1.0)
    # The blast: the body you hear (low mids), and a short low thump under it.
    body = tilt(band(noise(0.16, seed + 1), 180, 3200), -1.5) * env_exp(0.16, 0.026 * r.uniform(0.9, 1.1), 0.0002)
    place(out, body, 0.0, 1.0)
    thump = sweep(0.12, 175 * r.uniform(0.92, 1.08), 62, 5.0) * env_exp(0.12, 0.024, 0.0005)
    place(out, thump, 0.0003, 0.42 * close)
    # The rifle's own voice: a short electric zap dropping in pitch (it's a blaster
    # that fires slugs), under the report.
    zap = band(sweep(0.07, 2600 * r.uniform(0.95, 1.05), 520, 3.0, "saw"), 400, 7000) * env_exp(0.07, 0.016, 0.0004)
    place(out, zap, 0.001, 0.13)
    # The action cycling: a small metallic clack ~40 ms on.
    clack = (band(noise(0.04, seed + 2), 2500, 9000) * env_exp(0.04, 0.003, 0.0001) * 0.6
             + ring(0.04, [3150 * r.uniform(0.97, 1.03), 4720, 6900], [0.008, 0.006, 0.004], [0.35, 0.25, 0.12]))
    place(out, clack, 0.038 * r.uniform(0.92, 1.1), 0.2 * close)
    return softclip(out * 1.5, 1.8)


# ---- the classes' weapons (U6) ----------------------------------------------
# Each its own voice on the rifle's recipe: how hard the front cracks, the
# blast's band and length, the thump under it, the zap, and the action
# (a shotgun racks its pump twice, a marksman rifle's bolt is heavier).
VOICES = {
    "Carbine": dict(crack=0.85, body=(230, 3800, 0.019), thump=(195, 70, 0.28), zap=(3100, 640, 0.15), clacks=((0.034, 3500, 0.18),), length=0.3),
    "Shotgun": dict(crack=0.7, body=(90, 2600, 0.036), thump=(130, 42, 0.95), zap=(1500, 260, 0.09), clacks=((0.30, 1900, 0.32), (0.43, 2300, 0.36)), length=0.62),
    "Marksman": dict(crack=1.35, body=(150, 3000, 0.034), thump=(150, 48, 0.62), zap=(2300, 380, 0.2), clacks=((0.075, 2700, 0.26),), length=0.42),
    "Smg": dict(crack=0.75, body=(280, 4400, 0.014), thump=(230, 85, 0.2), zap=(3600, 820, 0.11), clacks=((0.026, 4200, 0.15),), length=0.26),
}


def gun_dry(voice, seed, close=1.0):
    """One shot of a class's weapon, dry, at the shooter (`rifle_dry`'s parts, in its voice)."""
    v = VOICES[voice]
    r = np.random.default_rng(seed)
    out = np.zeros(int(v["length"] * SR))
    crack = highpass(noise(0.03, seed), 700) * env_exp(0.03, 0.0032 * r.uniform(0.9, 1.15), 0.00006)
    place(out, crack, 0, v["crack"])
    lo, hi, decay = v["body"]
    body = tilt(band(noise(decay * 6, seed + 1), lo, hi), -1.5) * env_exp(decay * 6, decay * r.uniform(0.9, 1.1), 0.0002)
    place(out, body, 0.0, 1.0)
    f0, f1, amp = v["thump"]
    thump = sweep(0.14, f0 * r.uniform(0.92, 1.08), f1, 5.0) * env_exp(0.14, 0.024 + 0.02 * (amp > 0.8), 0.0005)
    place(out, thump, 0.0003, amp * close)
    z0, z1, za = v["zap"]
    zap = band(sweep(0.07, z0 * r.uniform(0.95, 1.05), z1, 3.0, "saw"), 300, 7000) * env_exp(0.07, 0.016, 0.0004)
    place(out, zap, 0.001, za)
    for k, (at, f, a) in enumerate(v["clacks"]):
        clack = (band(noise(0.05, seed + 2 + k), 1800, 9000) * env_exp(0.05, 0.004, 0.0001) * 0.6
                 + ring(0.05, [f * r.uniform(0.97, 1.03), f * 1.5, f * 2.19], [0.01, 0.007, 0.005], [0.35, 0.25, 0.12]))
        place(out, clack, at * r.uniform(0.94, 1.06), a * close)
    return softclip(out * 1.5, 1.8)


def gun_own(voice, variant):
    seed = 1000 + 37 * list(VOICES).index(voice) + variant
    dry = gun_dry(voice, seed, close=1.0)
    secs = 1.4 if voice == "Shotgun" else 1.3
    L = convolve(dry, outdoor_ir(secs, seed * 7 + 1, tail_db=-17.0))[:int(secs * SR)]
    R = convolve(dry, outdoor_ir(secs, seed * 7 + 2, tail_db=-17.0))[:int(secs * SR)]
    m = max(np.abs(L).max(), np.abs(R).max())
    return fade(L / m * 0.89), fade(R / m * 0.89)


def gun_near(voice, variant):
    seed = 2000 + 37 * list(VOICES).index(voice) + variant
    dry = gun_dry(voice, seed, close=0.5)
    secs = 1.5 if voice == "Shotgun" else 1.4
    x = convolve(dry, outdoor_ir(secs, seed * 3, ground=0.003, slaps=((0.12, -16.0), (0.26, -20.0)), tail_db=-11.0, decay=0.42))[:int(secs * SR)]
    return fade(normalize(x, -1.0))


def rifle_own(variant):
    seed = 100 + variant
    dry = rifle_dry(seed, close=1.0)
    secs = 1.3
    # The field answers quietly; each ear hears its own tail.
    L = convolve(dry, outdoor_ir(secs, seed * 7 + 1, tail_db=-17.0))[:int(secs * SR)]
    R = convolve(dry, outdoor_ir(secs, seed * 7 + 2, tail_db=-17.0))[:int(secs * SR)]
    m = max(np.abs(L).max(), np.abs(R).max())
    return fade(L / m * 0.89), fade(R / m * 0.89)


def rifle_near(variant):
    """Someone else's shot, within ~80 m: less of the felt thump, more field."""
    seed = 200 + variant
    dry = rifle_dry(seed, close=0.5)
    secs = 1.4
    x = convolve(dry, outdoor_ir(secs, seed * 3, ground=0.003, slaps=((0.12, -16.0), (0.26, -20.0)), tail_db=-11.0, decay=0.42))[:int(secs * SR)]
    return fade(normalize(x, -1.0))


def rifle_far(variant):
    """A shot hundreds of meters off: the air has taken the top, the ground the
    middle; a pop, a rolling low report, a long echo."""
    seed = 300 + variant
    r = np.random.default_rng(seed)
    secs = 2.2
    pop = lowpass(highpass(noise(0.05, seed), 180) * env_exp(0.05, 0.007, 0.0006), 2000)
    boom = lowpass(noise(0.5, seed + 1), 420) * env_exp(0.5, 0.07, 0.003)
    dry = pad(pop, 0.5) * 1.0 + boom * 0.7
    ir = outdoor_ir(secs, seed * 5, ground=0.012, ground_gain=0.6,
                    slaps=((0.3 * r.uniform(0.8, 1.2), -9.0), (0.7 * r.uniform(0.8, 1.2), -13.0)),
                    tail_db=-6.0, bright=1800, decay=0.65)
    x = lowpass(convolve(dry, ir)[:int(secs * SR)], 2300)
    return fade(normalize(x, -1.0), 0.002, 0.25)


def crack(variant):
    """A slug passing close: the supersonic crack (an N-wave), a short whiz, a little air."""
    seed = 400 + variant
    r = np.random.default_rng(seed)
    secs = 0.3
    n = np.zeros(int(secs * SR))
    w = int(0.00035 * SR * r.uniform(0.85, 1.2))
    place(n, np.concatenate([np.linspace(1, -1, w), [0.0]]), 0.004, 1.0)
    n = highpass(n, 500)
    hiss = band(noise(secs, seed), 2000, 10000) * env_exp(secs, 0.006, 0.0001)
    whiz = band(noise(secs, seed + 1), 900, 3800) * np.exp(-((t_axis(secs) - 0.012) / 0.012) ** 2)
    x = n * 1.0 + hiss * 0.3 + whiz * 0.16
    x = convolve(x, outdoor_ir(0.3, seed, slaps=((0.06, -24.0),), tail_db=-20.0, decay=0.08, bright=5000))[:len(x)]
    return fade(normalize(softclip(x * 1.2, 1.3), -1.5), 0.0002, 0.03)


def impact(kind, variant):
    seed = 500 + variant + (50 if kind == "hard" else 0)
    r = np.random.default_rng(seed)
    secs = 0.45
    if kind == "dirt":
        thud = lowpass(noise(secs, seed), 900) * env_exp(secs, 0.035, 0.0008)
        grit = band(noise(secs, seed + 1), 2000, 7000) * env_exp(secs, 0.012, 0.0004)
        # A sprinkle of debris falling back.
        debris = np.zeros(int(secs * SR))
        for k in range(r.integers(4, 9)):
            at = 0.03 + r.uniform(0, 0.25)
            place(debris, band(noise(0.01, seed + 10 + k), 2500, 8000) * env_exp(0.01, 0.0015), at, r.uniform(0.05, 0.15))
        x = thud * 1.0 + grit * 0.35 + debris
    else:
        hit = highpass(noise(secs, seed), 1200) * env_exp(secs, 0.006, 0.0001)
        chip = band(noise(secs, seed + 1), 600, 4000) * env_exp(secs, 0.02, 0.0003)
        ping = ring(secs, [r.uniform(2400, 3400), r.uniform(5200, 6400)], [0.05, 0.03], [0.25, 0.12])
        x = hit * 1.0 + chip * 0.5 + ping * (0.6 if variant % 2 == 0 else 0.15)
    x = convolve(x, outdoor_ir(0.45, seed, ground=0.002, slaps=((0.08, -22.0),), tail_db=-16.0, decay=0.12, bright=4000))[:len(x)]
    return fade(normalize(x, -2.0), 0.0005, 0.05)


# ---- feedback ---------------------------------------------------------------

def hit_tick():
    """Our shot hit: a crisp, dry tick (it must cut through the fight)."""
    secs = 0.09
    x = band(noise(secs, 600), 1800, 6500) * env_exp(secs, 0.004, 0.0001)
    x += ring(secs, [2350, 4100], [0.012, 0.007], [0.55, 0.25])
    return fade(normalize(x, -3.0), 0.0002, 0.02)


def hit_head():
    """A head hit: higher, with a short metallic ring."""
    secs = 0.22
    x = band(noise(secs, 601), 3000, 9000) * env_exp(secs, 0.003, 0.0001)
    x += ring(secs, [3950, 5920, 7900], [0.06, 0.04, 0.025], [0.5, 0.3, 0.15])
    return fade(normalize(x, -3.0), 0.0002, 0.05)


def kill_confirm():
    """A kill: two quick rising blips under a soft thump."""
    secs = 0.36
    out = np.zeros(int(secs * SR))
    for at, f in ((0.0, 1320.0), (0.075, 1980.0)):
        b = np.sin(2 * np.pi * f * t_axis(0.12)) * env_exp(0.12, 0.028, 0.0015)
        b += 0.3 * np.sin(2 * np.pi * f * 2 * t_axis(0.12)) * env_exp(0.12, 0.015, 0.0015)
        place(out, b, at, 0.6)
    thump = sweep(0.2, 110, 60, 4.0) * env_exp(0.2, 0.05, 0.002)
    place(out, thump, 0.0, 0.5)
    return fade(normalize(out, -3.0), 0.0005, 0.06)


def hurt(variant):
    """We're hit: a dull blow on armor, felt in the chest."""
    seed = 700 + variant
    secs = 0.4
    blow = sweep(secs, 140, 60, 5.0) * env_exp(secs, 0.045, 0.001)
    slap = band(noise(secs, seed), 250, 3000) * env_exp(secs, 0.03, 0.0003)
    plate = ring(secs, [740 + variant * 37, 1130], [0.06, 0.04], [0.15, 0.08])
    x = blow * 0.55 + slap * 1.0 + plate
    return fade(normalize(softclip(x * 1.3, 1.4), -2.0), 0.0005, 0.06)


def death():
    secs = 1.6
    rumble = lowpass(noise(secs, 800), 220) * env_exp(secs, 0.5, 0.01)
    whoosh = band(noise(secs, 801), 300, 1500) * np.exp(-((t_axis(secs) - 0.18) / 0.15) ** 2)
    tone = sweep(secs, 220, 70, 3.0) * env_exp(secs, 0.6, 0.02)
    x = rumble * 1.0 + whoosh * 0.35 + tone * 0.35
    return fade(normalize(x, -4.0), 0.002, 0.3)


def respawn():
    secs = 0.9
    t = t_axis(secs)
    f = 330 * 2 ** (t / secs * 1.0)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * np.clip(t / secs, 0, 1)) ** 2
    x += 0.4 * np.sin(2 * np.pi * np.cumsum(f * 1.5) / SR) * np.sin(np.pi * np.clip(t / secs, 0, 1)) ** 3
    x = convolve(x, outdoor_ir(1.0, 900, tail_db=-8.0, decay=0.3, bright=6000))[:len(x)]
    return fade(normalize(x, -6.0), 0.01, 0.15)


def gear(variant, up):
    """Bringing the sights up or down: a cloth rustle and a light click."""
    seed = 1000 + variant + (10 if up else 0)
    secs = 0.3
    rustle = band(noise(secs, seed), 800, 6000) * np.exp(-((t_axis(secs) - 0.06) / 0.05) ** 2)
    click = band(noise(secs, seed + 1), 3000, 9000) * env_exp(secs, 0.0025, 0.0001)
    out = rustle * 0.5 + np.roll(click, int((0.11 if up else 0.05) * SR)) * 0.6
    return fade(normalize(out, -6.0), 0.002, 0.05)


def footstep(variant, surface="dirt"):
    seed = 1100 + variant
    r = np.random.default_rng(seed)
    secs = 0.28
    out = np.zeros(int(secs * SR))
    # Heel then sole: two soft crunches; boots, so some low weight.
    for at, g in ((0.0, 1.0), (0.035 * r.uniform(0.8, 1.3), 0.65)):
        crunch = band(noise(0.15, seed + int(at * 1000)), 500, 6000) * env_exp(0.15, 0.022, 0.002)
        grains = np.zeros(int(0.15 * SR))
        for k in range(r.integers(6, 14)):
            place(grains, band(noise(0.006, seed + 30 + k), 2500, 9000) * env_exp(0.006, 0.0012), r.uniform(0, 0.06), r.uniform(0.05, 0.2))
        weight = lowpass(noise(0.15, seed + 7), 250) * env_exp(0.15, 0.03, 0.003)
        place(out, crunch * 0.5 + grains + weight * 0.9, at, g)
    return fade(normalize(out, -3.0), 0.001, 0.04)


# ---- handling: the cell, the trigger, the body ------------------------------

def close_space(x, seed):
    """Handling sounds are at arm's length: dry, with just the field's first bounce."""
    return convolve(x, outdoor_ir(0.3, seed, ground=0.0015, ground_gain=0.22, slaps=((0.07, -30.0),),
                                  tail_db=-26.0, decay=0.08, bright=7000))[:len(x)]


def latch(secs, seed, f=3700.0, level=1.0):
    """A small steel latch snapping: a bright tick with a short ring."""
    x = band(noise(secs, seed), 3000, 11000) * env_exp(secs, 0.0012, 0.0001)
    x += ring(secs, [f, f * 1.65, f * 2.4], [0.011, 0.006, 0.0035], [0.45, 0.25, 0.12])
    return x * level


def scrape(secs, seed, lo=1500.0, hi=7500.0, rate=1100.0, modes=(2300.0, 3900.0, 5600.0)):
    """Metal sliding on metal: a rough surface's micro-impacts, each ringing the
    parts' modes, over a thin hiss; swelling in and out."""
    r = np.random.default_rng(seed)
    n = int(secs * SR)
    imp = np.zeros(n)
    k = r.poisson(rate * secs)
    idx = r.integers(0, n, k)
    imp[idx] = r.uniform(0.25, 1.0, k) * r.choice([-1.0, 1.0], k)
    mode = ring(0.025, [m * r.uniform(0.96, 1.04) for m in modes], [0.004, 0.003, 0.002], [1.0, 0.6, 0.35])
    body = band(convolve(imp, mode)[:n], lo, hi)
    hiss = band(noise(secs, seed + 1), lo, hi) * 0.18
    t = t_axis(secs)
    swell = np.sin(np.pi * np.clip(t / secs, 0, 1)) ** 0.7
    return (body / (np.max(np.abs(body)) + 1e-9) + hiss) * swell


def cell_release():
    """The cap pressed: the latch lets go (two ticks), the spring kicks the cell
    out with a thunk and a puff of gas, and it slides the first few centimeters."""
    secs = 0.34
    out = np.zeros(int(secs * SR))
    place(out, latch(0.06, 2000, 3700.0), 0.0, 0.9)
    place(out, latch(0.05, 2001, 4300.0), 0.009, 0.45)
    thunk = ring(0.12, [310.0, 470.0, 1150.0], [0.035, 0.02, 0.012], [0.5, 0.25, 0.12])
    thunk += lowpass(noise(0.12, 2002), 700) * env_exp(0.12, 0.018, 0.0008) * 0.6
    place(out, thunk, 0.011, 0.8)
    puff = band(noise(0.16, 2003), 2500, 11000) * env_exp(0.16, 0.045, 0.006)
    place(out, puff, 0.012, 0.28)
    place(out, scrape(0.07, 2004, 1800.0, 8000.0, 1400.0), 0.03, 0.35)
    return fade(normalize(close_space(out, 2005), -3.0), 0.0003, 0.05)


def cell_out():
    """Pulled out of the bay: a scrape that quickens, then the canister rings as it clears."""
    secs = 0.36
    out = np.zeros(int(secs * SR))
    s = scrape(0.17, 2010, 1500.0, 8000.0, 1500.0)
    s *= np.linspace(0.6, 1.0, len(s))
    place(out, s, 0.0, 0.7)
    clear = ring(0.2, [1130.0, 3240.0, 5100.0], [0.07, 0.035, 0.02], [0.35, 0.2, 0.1])
    place(clear, latch(0.03, 2011, 2600.0), 0.0, 0.4)
    place(out, clear, 0.165, 0.6)
    return fade(normalize(close_space(out, 2012), -4.0), 0.002, 0.06)


def cell_pouch():
    """A hand at the belt: cloth and webbing, a buckle's click."""
    secs = 0.38
    t = t_axis(secs)
    rustle = band(noise(secs, 2020), 600, 5000) * np.exp(-((t - 0.1) / 0.07) ** 2)
    rustle += band(noise(secs, 2021), 1500, 7000) * np.exp(-((t - 0.22) / 0.05) ** 2) * 0.6
    out = rustle * 0.55
    place(out, latch(0.04, 2022, 2900.0, 0.35), 0.16)
    return fade(normalize(close_space(out, 2023), -7.0), 0.003, 0.06)


def cell_in():
    """Slid into the bay: a darker scrape, slowing, and a soft bump at the stop."""
    secs = 0.22
    out = np.zeros(int(secs * SR))
    s = scrape(0.12, 2030, 1100.0, 6000.0, 1000.0, (1900.0, 3300.0, 4800.0))
    s *= np.linspace(1.0, 0.7, len(s))
    place(out, s, 0.0, 0.7)
    bump = lowpass(noise(0.08, 2031), 900) * env_exp(0.08, 0.012, 0.0008) + ring(0.08, [520.0], [0.02], [0.3])
    place(out, bump, 0.115, 0.45)
    return fade(normalize(close_space(out, 2032), -5.0), 0.002, 0.05)


def cell_seat():
    """Pushed home: a weighty ka-chunk (the cell's mass meeting the stop, two
    latches catching) and the cell's contacts meeting, a small electric chirp."""
    secs = 0.42
    out = np.zeros(int(secs * SR))
    chunk = lowpass(noise(0.2, 2040), 900) * env_exp(0.2, 0.017, 0.0006)
    chunk += ring(0.2, [165.0, 410.0, 980.0, 2150.0], [0.05, 0.032, 0.016, 0.008], [0.4, 0.45, 0.28, 0.12])
    place(out, softclip(chunk * 1.4, 1.3), 0.0, 1.0)
    place(out, latch(0.05, 2041, 3500.0), 0.011, 0.75)
    place(out, latch(0.05, 2042, 4100.0), 0.028, 0.5)
    chirp = sweep(0.07, 900.0, 1900.0, 2.0) * env_exp(0.07, 0.025, 0.004)
    chirp += 0.35 * sweep(0.07, 2700.0, 5700.0, 2.0) * env_exp(0.07, 0.02, 0.004)
    crackle = band(noise(0.04, 2043) * (np.random.default_rng(2044).random(int(0.04 * SR)) < 0.03), 2000, 9000)
    place(out, chirp, 0.045, 0.16)
    place(out, crackle, 0.045, 0.25)
    return fade(normalize(close_space(out, 2045), -2.0), 0.0003, 0.08)


def cell_charge():
    """From empty, the cap turned: a ratchet (three clicks), then the cell
    charging, a whine rising, and a ping as it's ready."""
    secs = 0.62
    out = np.zeros(int(secs * SR))
    for k, at in enumerate((0.0, 0.042, 0.086)):
        place(out, latch(0.03, 2050 + k, 4400.0 - 300.0 * k, 0.8), at)
    t = t_axis(0.42)
    f = 210.0 * (2600.0 / 210.0) ** ((t / 0.42) ** 1.6)
    ph = 2 * np.pi * np.cumsum(f * (1 + 0.004 * np.sin(2 * np.pi * 31 * t))) / SR
    whine = np.sin(ph) + 0.3 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)
    shape = np.clip(t / 0.06, 0, 1) * (0.35 + 0.65 * t / 0.42)
    hum = lowpass(noise(0.42, 2054), 400) * 0.25 * shape
    place(out, (whine * shape * 0.22 + hum) * np.clip((0.42 - t) / 0.02, 0, 1), 0.1)
    place(out, ring(0.12, [2600.0, 5200.0, 7800.0], [0.05, 0.03, 0.015], [0.4, 0.18, 0.06]), 0.5, 0.7)
    return fade(normalize(close_space(out, 2055), -5.0), 0.0005, 0.05)


def dry_fire():
    """The trigger with nothing in the cell: a hollow click and the dead cell's fizzle."""
    secs = 0.22
    out = np.zeros(int(secs * SR))
    click = band(noise(0.04, 2060), 1500, 6500) * env_exp(0.04, 0.0018, 0.0001)
    click += ring(0.04, [1900.0, 3150.0], [0.008, 0.005], [0.5, 0.25])
    place(out, click, 0.0, 1.0)
    place(out, lowpass(noise(0.05, 2061), 420) * env_exp(0.05, 0.01, 0.0008), 0.012, 0.35)
    fizz = band(noise(0.1, 2062), 300, 2600) * env_exp(0.1, 0.035, 0.004)
    fizz *= 0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 120 * t_axis(0.1)))
    place(out, fizz, 0.02, 0.18)
    return fade(normalize(close_space(out, 2063), -4.0), 0.0003, 0.05)


def cell_clink(variant):
    """A spent cell hitting the ground: a thud in the dirt, the canister's ring,
    a smaller bounce or two."""
    seed = 2070 + variant * 10
    r = np.random.default_rng(seed)
    secs = 0.6
    out = np.zeros(int(secs * SR))
    f0 = 640.0 * r.uniform(0.92, 1.08)
    for k, (at, g) in enumerate(((0.0, 1.0), (r.uniform(0.11, 0.15), 0.42), (r.uniform(0.21, 0.26), 0.16))):
        thud = lowpass(noise(0.1, seed + k), 520) * env_exp(0.1, 0.016, 0.0006) * 0.55
        grit = band(noise(0.05, seed + 5 + k), 2000, 8000) * env_exp(0.05, 0.006, 0.0003) * 0.3
        tone = ring(0.3, [f0, f0 * 2.27, f0 * 4.6], [0.085, 0.05, 0.028], [0.42, 0.24, 0.1])
        place(out, pad(thud, 0.3) + pad(grit, 0.3) + tone, at, g)
    out = convolve(out, outdoor_ir(0.5, seed + 9, ground=0.002, slaps=((0.08, -26.0),), tail_db=-20.0, decay=0.15, bright=5000))[:len(out)]
    return fade(normalize(out, -4.0), 0.0005, 0.08)


def crouch(down):
    """Getting down or up: webbing and cloth, a strap's click; down, the knee's soft thud."""
    seed = 2090 + (0 if down else 5)
    secs = 0.34
    t = t_axis(secs)
    out = band(noise(secs, seed), 500, 5000) * np.exp(-((t - 0.07) / 0.05) ** 2) * 0.6
    out += band(noise(secs, seed + 1), 1800, 7500) * np.exp(-((t - (0.12 if down else 0.05)) / 0.035) ** 2) * 0.35
    place(out, latch(0.04, seed + 2, 2700.0 if down else 3200.0, 0.22), 0.04 if down else 0.09)
    if down:
        knee = lowpass(noise(0.12, seed + 3), 260) * env_exp(0.12, 0.03, 0.004)
        place(out, knee, 0.12, 0.9)
    return fade(normalize(close_space(out, seed + 4), -6.0), 0.002, 0.06)


# ---- the classes' abilities (U6) ---------------------------------------------

def jets_loop(secs=3.0):
    """Jump jets burning: a rough low roar and a hissing top, with the burn's flutter (a loop)."""
    t = t_axis(secs + 0.5)
    r = np.random.default_rng(3100)
    roar = lowpass(tilt(noise(secs + 0.5, 3101), -3.0), 900) * 1.4
    hiss = band(noise(secs + 0.5, 3102), 2500, 9000) * 0.18
    # The burn's flutter: a fast, uneven wobble of level.
    fl = np.convolve(r.standard_normal(len(t) // 400 + 2), np.ones(2) / 2, mode="same")
    flutter = 0.8 + 0.2 * np.tanh(np.interp(t, np.linspace(0, t[-1], len(fl)), fl))
    out = softclip((roar + hiss) * flutter, 1.6)
    return loopify(out / (np.abs(out).max() + 1e-9) * 0.5, 0.5)


def jets_ignite():
    """The jets lighting: a breathy whump and a rising hiss."""
    secs = 0.5
    t = t_axis(secs)
    whump = lowpass(noise(secs, 3110), 300) * env_exp(secs, 0.08, 0.004) * 1.6
    hiss = band(noise(secs, 3111), 1800, 8000) * np.clip(t / 0.15, 0, 1) * np.exp(-t / 0.3) * 0.5
    return fade(normalize(whump + hiss, -3.0), 0.001, 0.1)


def bulwark_on():
    """A Bulwark rising: a deep swell under a bright, shimmering chord that settles."""
    secs = 1.1
    t = t_axis(secs)
    swell = np.sin(2 * np.pi * (70 + 40 * t) * t) * np.clip(t / 0.12, 0, 1) * np.exp(-t / 0.5) * 0.8
    chord = sum(np.sin(2 * np.pi * f * t + k) * a for k, (f, a) in enumerate(((440, 0.3), (660, 0.22), (990, 0.16), (1320, 0.1))))
    shimmer = 1.0 + 0.35 * np.sin(2 * np.pi * 11 * t)
    chord = chord * shimmer * np.clip(t / 0.05, 0, 1) * np.exp(-t / 0.35)
    air = band(noise(secs, 3120), 3000, 9000) * np.exp(-t / 0.15) * 0.15
    return fade(normalize(swell + chord + air, -4.0), 0.001, 0.15)


def mend_on():
    """A Mend field opening: soft rising chimes over a warm hum."""
    secs = 1.4
    t = t_axis(secs)
    hum = np.sin(2 * np.pi * 196 * t) * 0.25 * np.clip(t / 0.2, 0, 1) * np.exp(-t / 0.7)
    out = hum.copy()
    for k, f in enumerate((523.25, 659.25, 783.99, 1046.5)):
        at = 0.06 * k
        tone = ring(secs - at, (f, f * 2.01), (0.45, 0.2), (0.35, 0.08))
        place(out, tone, at, 0.8)
    return fade(normalize(out, -5.0), 0.002, 0.2)


def mend_tick():
    """A heal landing: a small, soft bell."""
    secs = 0.35
    return fade(normalize(ring(secs, (1318.5, 2637.0, 3955.0), (0.12, 0.05, 0.03), (0.5, 0.15, 0.06)), -10.0), 0.001, 0.08)


def cloak(on):
    """Cloaking (a breath drawn in, falling and thinning to nothing) or uncloaking (the reverse, out)."""
    secs = 0.6
    t = t_axis(secs)
    x = band(noise(secs, 3130 + on), 600, 7000)
    f = sweep(secs, 2400, 300, 3.0) if on else sweep(secs, 300, 2400, 3.0)
    env = np.sin(np.pi * np.clip(t / secs, 0, 1)) ** (1.5 if on else 0.8)
    out = (x * 0.6 + f * 0.25) * env
    return fade(normalize(close_space(out, 3135 + on), -6.0), 0.003, 0.08)


# ---- beds (loops) -----------------------------------------------------------

def loopify(x, xfade):
    """A seamless loop: the end cross-faded into the start."""
    n = int(xfade * SR)
    head, body = x[:n], x[n:]
    a = np.linspace(0, 1, n)
    body[-n:] = body[-n:] * np.sqrt(1 - a) + head * np.sqrt(a)
    return body


def wind(secs=24.0):
    t = t_axis(secs + 2)
    out = []
    for ch in range(2):
        base = tilt(noise(secs + 2, 1200 + ch), -4.5)
        # Gusts: slow swells of level and brightness.
        g = np.convolve(np.random.default_rng(1300 + ch).standard_normal(len(t) // 4800 + 2), np.ones(3) / 3, mode="same")
        gust = np.interp(t, np.linspace(0, t[-1], len(g)), g)
        gust = 0.55 + 0.45 * np.tanh(gust * 0.8)
        low = lowpass(base, 420) * (0.5 + 0.5 * gust) * 0.7
        high = band(base, 450, 4000) * gust ** 2 * 1.1
        out.append(loopify(low + high, 2.0))
    m = max(np.abs(out[0]).max(), np.abs(out[1]).max())
    return out[0] / m * 0.7, out[1] / m * 0.7


def battle(secs=12.0, rate=9.0, seed=1400):
    """A fight far off: many shots (bursts) at random, softened by distance,
    on a low rumble; looped."""
    r = np.random.default_rng(seed)
    total = secs + 2
    out = np.zeros(int(total * SR))
    shots = [rifle_far(v) for v in range(4)]
    tm = 0.0
    while tm < total:
        burst = r.integers(1, 6)
        for k in range(burst):
            at = tm + k * r.uniform(0.09, 0.13)
            if at < total:
                place(out, shots[r.integers(0, 4)], at, r.uniform(0.15, 0.6))
        tm += r.exponential(1.0 / rate) * burst
    rumble = lowpass(tilt(noise(total, seed + 1), -6), 160)
    rumble *= 0.12 * np.sqrt(np.mean(out ** 2)) / (np.sqrt(np.mean(rumble ** 2)) + 1e-9)
    x = lowpass(out, 1800) + rumble
    return loopify(normalize(x, -2.0), 2.0)


def reed_chime(v):
    """Glass reeds touching in the wind (the Glasslands, K4's life): a few thin
    glass rods struck together, each ringing with a free bar's inharmonic
    partials (1, 2.756, 5.404) over a click of glass on glass; bright, a
    second or so. Its own stream of chances (the rest of the set is untouched)."""
    r = np.random.default_rng(1600 + v)
    secs = 1.9
    t = t_axis(1.5)
    out = np.zeros(int(secs * SR))
    for k in range(int(r.integers(3, 7))):
        f0 = r.uniform(1800.0, 3600.0)
        tone = np.zeros_like(t)
        for ratio, tau, amp in ((1.0, r.uniform(0.35, 0.75), 1.0), (2.756, 0.2, 0.4), (5.404, 0.07, 0.18)):
            tone += amp * np.sin(2 * np.pi * f0 * ratio * t + r.uniform(0, 6.28)) * np.exp(-t / tau)
        click = band(r.standard_normal(int(0.004 * SR)), 3000, 12000) * env_exp(0.004, 0.0012) * 0.35
        tone[:len(click)] += click
        tone *= np.clip(t / 0.0004, 0, 1)
        place(out, tone, 0.0 if k == 0 else r.uniform(0.02, 0.35), 1.0 if k == 0 else r.uniform(0.3, 1.0))
    return normalize(fade(out, 0.0005, 0.2), -6.0)


# ---- write ------------------------------------------------------------------

for voice in VOICES:
    for v in range(3):
        write(f"{voice}_Own_{v}", gun_own(voice, v), stereo=True)
        write(f"{voice}_Near_{v}", gun_near(voice, v))
for v in range(4):
    write(f"Shot_Own_{v}", rifle_own(v), stereo=True)
    write(f"Shot_Near_{v}", rifle_near(v))
    write(f"Shot_Far_{v}", rifle_far(v))
    write(f"Crack_{v}", crack(v))
    write(f"Impact_Dirt_{v}", impact("dirt", v))
    write(f"Impact_Hard_{v}", impact("hard", v))
    write(f"Footstep_{v}", footstep(v))
for v in range(3):
    write(f"Hurt_{v}", hurt(v))
write("Hit_Body", hit_tick())
write("Hit_Head", hit_head())
write("Kill", kill_confirm())
write("Death", death())
write("Respawn", respawn())
write("Sights_Up", gear(0, True))
write("Sights_Down", gear(1, False))
write("Cell_Release", cell_release())
write("Cell_Out", cell_out())
write("Cell_Pouch", cell_pouch())
write("Cell_In", cell_in())
write("Cell_Seat", cell_seat())
write("Cell_Charge", cell_charge())
write("Dry_Fire", dry_fire())
for v in range(2):
    write(f"Cell_Clink_{v}", cell_clink(v))
write("Jets_Loop", jets_loop())
write("Jets_Ignite", jets_ignite())
write("Bulwark_On", bulwark_on())
write("Mend_On", mend_on())
write("Mend_Tick", mend_tick())
write("Cloak_On", cloak(True))
write("Cloak_Off", cloak(False))
write("Crouch_Down", crouch(True))
write("Crouch_Up", crouch(False))
write("Wind", wind(), stereo=True)
write("Battle_Small", battle(rate=4.0, seed=1400))
write("Battle_Large", battle(rate=18.0, seed=1500))
for v in range(4):
    write(f"Reed_Chime_{v}", reed_chime(v))

# ---- a preview sheet: waveform and spectrogram per sound --------------------
if PREV:
    import zlib

    def png(path, img):
        h, w, _ = img.shape
        raw = b"".join(b"\x00" + img[y].astype(np.uint8).tobytes() for y in range(h))
        def chunk(t, d):
            c = struct.pack(">I", len(d)) + t + d
            return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
        data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
        open(path, "wb").write(data)

    W, H = 360, 150
    cols = 6
    rows = (len(made) + cols - 1) // cols
    sheet = np.full((rows * (H + 24), cols * (W + 8), 3), 18, dtype=np.float32)
    for i, (name, x) in enumerate(made):
        r_, c_ = divmod(i, cols)
        y0, x0 = r_ * (H + 24) + 20, c_ * (W + 8)
        secs = min(len(x) / SR, 2.0)
        n = int(secs * SR)
        xs = x[:n]
        # Spectrogram (log frequency 50 Hz-20 kHz), top 100 px.
        hop = max(1, n // W)
        win = 1024
        spec = np.zeros((100, W))
        for k in range(W):
            seg = xs[k * hop:k * hop + win]
            if len(seg) < win:
                seg = np.pad(seg, (0, win - len(seg)))
            S = np.abs(np.fft.rfft(seg * np.hanning(win)))
            f = np.fft.rfftfreq(win, 1 / SR)
            fl = np.geomspace(50, 20000, 101)
            for b in range(100):
                m = (f >= fl[b]) & (f < fl[b + 1])
                spec[99 - b, k] = S[m].max() if m.any() else 0
        db = 20 * np.log10(spec + 1e-6)
        db = np.clip((db + 40) / 60, 0, 1)
        img = np.stack([db ** 0.6 * 255, db ** 1.4 * 200, db ** 3 * 120], axis=-1)
        sheet[y0:y0 + 100, x0:x0 + W] = img
        # Waveform, bottom 46 px.
        env = np.array([np.abs(xs[k * hop:(k + 1) * hop]).max() if k * hop < n else 0 for k in range(W)])
        for k in range(W):
            hgt = int(env[k] * 22)
            sheet[y0 + 123 - hgt:y0 + 124 + hgt, x0 + k] = (120, 200, 160)
    png(os.path.join(PREV, "sfx_sheet.png"), np.clip(sheet, 0, 255))
    with open(os.path.join(PREV, "sfx_names.txt"), "w") as fh:
        fh.write("\n".join(f"{i}: {n}" for i, (n, _) in enumerate(made)))
    print("preview", os.path.join(PREV, "sfx_sheet.png"))
