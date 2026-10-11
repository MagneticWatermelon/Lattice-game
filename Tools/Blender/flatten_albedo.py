# Blender: make a tiling albedo even at large scale: divide out a wide blur
# (the scan's lighting drift and big blotches, which repeat as stripes across
# a terrain), keep the mean color and the fine detail. A tint and a contrast
# can be applied. Args after --: in.jpg out.png sigma_px [r g b tint] [contrast]
import bpy, sys, numpy as np
a = sys.argv[sys.argv.index("--") + 1:]
src, out, sigma = a[0], a[1], float(a[2])
tint = np.array([float(a[3]), float(a[4]), float(a[5])], dtype=np.float32) if len(a) >= 6 else np.ones(3, np.float32)
contrast = float(a[6]) if len(a) >= 7 else 1.0
img = bpy.data.images.load(src)
img.colorspace_settings.name = "Non-Color"   # work on the stored sRGB values
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., :3]
lin = np.where(px <= 0.04045, px / 12.92, ((px + 0.055) / 1.055) ** 2.4)
fy = np.fft.fftfreq(h)[:, None]; fx = np.fft.fftfreq(w)[None, :]
g = np.exp(-2 * (np.pi ** 2) * (sigma ** 2) * (fx ** 2 + fy ** 2))
blur = np.stack([np.real(np.fft.ifft2(np.fft.fft2(lin[..., c]) * g)) for c in range(3)], axis=-1)
mean = lin.reshape(-1, 3).mean(axis=0)
flat = lin / np.maximum(blur, 1e-4) * mean
lum = (flat * np.array([0.2126, 0.7152, 0.0722])).sum(-1, keepdims=True)
mlum = float((mean * np.array([0.2126, 0.7152, 0.0722])).sum())
flat = flat * ((mlum + (lum - mlum) * contrast) / np.maximum(lum, 1e-4))
flat = np.clip(flat * tint, 0, 1)
srgb = np.where(flat <= 0.0031308, flat * 12.92, 1.055 * np.power(flat, 1 / 2.4) - 0.055)
o = bpy.data.images.new("o", w, h, alpha=False)
o.colorspace_settings.name = "Non-Color"
o.pixels = np.concatenate([srgb, np.ones((h, w, 1), np.float32)], axis=-1).ravel()
o.filepath_raw = out; o.file_format = "PNG"; o.save()
print("wrote", out, "mean(lin)", mean.round(3), "blur range", float(blur.min()), float(blur.max()))
