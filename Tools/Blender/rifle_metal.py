# Blender: the rifle's metal map (URP: metallic in R, smoothness in A) from its albedo:
# the source marks everything fully metallic, so the teal reads as polished cyan chrome.
# Teal panels become satin paint (dielectric), the near-black parts anodized metal, the
# rest bare metal. Masks are soft so material edges follow the albedo's own edges.
# Args: albedo metal_in metal_out (smoothness is before the material's x0.62).
import bpy, sys, numpy as np
alb, met_in, met_out = sys.argv[sys.argv.index("--") + 1:][:3]
def load(p):
    im = bpy.data.images.load(p); im.colorspace_settings.name = 'Non-Color'
    w, h = im.size
    return im, np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
_, a = load(alb)
mim, m = load(met_in)
if m.shape != a.shape:
    ys = (np.arange(m.shape[0]) * a.shape[0] // m.shape[0]); xs = (np.arange(m.shape[1]) * a.shape[1] // m.shape[1])
    a = a[ys][:, xs]
def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b)
sat = (mx - mn) / np.maximum(mx, 1e-4)
teal = ss(0.22, 0.42, sat) * ss(1.1, 1.45, g / np.maximum(r, 1e-3)) * ss(0.08, 0.16, mx)
dark = (1 - teal) * (1 - ss(0.07, 0.16, mx))
bare = np.clip(1 - teal - dark, 0, 1)
metal = teal * 0.0 + dark * 0.75 + bare * 1.0
smooth = teal * 0.74 + dark * 0.66 + bare * 0.9
# Keep the source's smoothness variation (scratches, wear) around the new levels.
src_s = m[..., 3]
smooth = np.clip(smooth + (src_s - src_s.mean()) * 0.8, 0, 1)
out = np.stack([metal, metal, metal, smooth], axis=-1).astype(np.float32)
o = bpy.data.images.new("metal", m.shape[1], m.shape[0], alpha=True)
o.colorspace_settings.name = 'Non-Color'
o.pixels = out.ravel()
o.filepath_raw = met_out
o.file_format = 'PNG'
o.save()
print("wrote", met_out, "teal %.0f%% dark %.0f%% bare %.0f%%" % (teal.mean() * 100, dark.mean() * 100, bare.mean() * 100))
