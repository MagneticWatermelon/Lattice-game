# Envelope and spectrum facts per WAV: where the energy is in time and frequency.
import sys, os, wave, numpy as np
d = sys.argv[1]
names = sys.argv[2:] if len(sys.argv) > 2 else sorted(f[:-4] for f in os.listdir(d) if f.endswith(".wav"))
print("%-14s %5s %6s | RMS dB vs peak at  5ms  20   50  100  200  400  800 ms | centroid Hz 0-50ms 50-400ms | low<200 %%" % ("name", "secs", "peak"))
for n in names:
    with wave.open(os.path.join(d, n + ".wav")) as w:
        ch = w.getnchannels(); sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768
    if ch == 2:
        x = x.reshape(-1, 2).mean(1)
    win = int(0.005 * sr)
    rms = np.array([np.sqrt(np.mean(x[i:i + win] ** 2) + 1e-12) for i in range(0, len(x) - win, win)])
    pk = rms.max()
    def at(ms):
        i = int(ms / 5)
        return 20 * np.log10(rms[i] / pk) if i < len(rms) else float("nan")
    def centroid(a, b):
        seg = x[int(a * sr):int(b * sr)]
        if len(seg) < 64: return float("nan")
        S = np.abs(np.fft.rfft(seg * np.hanning(len(seg))))
        f = np.fft.rfftfreq(len(seg), 1 / sr)
        return (S * f).sum() / (S.sum() + 1e-9)
    S = np.abs(np.fft.rfft(x)) ** 2; f = np.fft.rfftfreq(len(x), 1 / sr)
    low = S[f < 200].sum() / S.sum() * 100
    print("%-14s %5.2f %6.1f | %s | %7.0f %7.0f | %4.0f" % (n, len(x) / sr, 20 * np.log10(np.abs(x).max() + 1e-9), " ".join("%5.0f" % at(m) for m in (5, 20, 50, 100, 200, 400, 800)), centroid(0, 0.05), centroid(0.05, 0.4), low))
