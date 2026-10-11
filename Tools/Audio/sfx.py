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


# ---- write ------------------------------------------------------------------

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
write("Wind", wind(), stereo=True)
write("Battle_Small", battle(rate=4.0, seed=1400))
write("Battle_Large", battle(rate=18.0, seed=1500))

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
