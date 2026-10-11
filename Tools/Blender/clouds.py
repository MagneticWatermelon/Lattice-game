# Blender (numpy): the sky's cloud noise, one tileable texture. Args after --: out.png [preview.png]
#   R  cloud shapes: Perlin fBm made billowy by inverted Worley (cells read as puffs)
#   G  detail: Worley fBm at higher frequency (eats the shapes' edges into tufts)
#   B  wisps: fine Perlin fBm (thin cloud and streaks)
# Every octave is periodic over the texture, so it tiles without seams.
import bpy, sys, numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
out = args[0]
preview = args[1] if len(args) > 1 else None
N = 512
rng = np.random.default_rng(20261012)
xs = (np.arange(N) + 0.5) / N
X, Y = np.meshgrid(xs, xs)


def perlin(period):
    """Gradient noise with `period` cells across the texture, about -0.7..0.7."""
    ang = rng.uniform(0, 2 * np.pi, (period, period))
    gx, gy = np.cos(ang), np.sin(ang)
    px, py = X * period, Y * period
    xi, yi = np.floor(px).astype(int), np.floor(py).astype(int)
    xf, yf = px - xi, py - yi

    def corner(ox, oy):
        ix, iy = (xi + ox) % period, (yi + oy) % period
        return gx[iy, ix] * (xf - ox) + gy[iy, ix] * (yf - oy)

    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10)
    v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    a = corner(0, 0) * (1 - u) + corner(1, 0) * u
    b = corner(0, 1) * (1 - u) + corner(1, 1) * u
    return a * (1 - v) + b * v


def worley(period):
    """Distance to the nearest of one random point per cell (cells across: `period`), 0..~1."""
    pts = rng.uniform(0, 1, (period, period, 2))
    px, py = X * period, Y * period
    xi, yi = np.floor(px).astype(int), np.floor(py).astype(int)
    d = np.full(X.shape, 9.0)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            cx, cy = xi + ox, yi + oy
            p = pts[cy % period, cx % period]
            d = np.minimum(d, np.hypot(cx + p[..., 0] - px, cy + p[..., 1] - py))
    return np.clip(d, 0, 1)


def fbm(f, base, octaves, gain=0.5):
    s, a, tot = 0.0, 1.0, 0.0
    for o in range(octaves):
        s = s + a * f(base * 2 ** o)
        tot += a
        a *= gain
    return s / tot


def norm(a):
    lo, hi = np.percentile(a, 0.5), np.percentile(a, 99.5)
    return np.clip((a - lo) / (hi - lo), 0, 1)


per = norm(fbm(perlin, 4, 6))
cells = 1 - fbm(worley, 4, 3, 0.45)
# Perlin-Worley: the Perlin field, pushed up where the cells' puffs are.
shapes = norm(per * 0.62 + norm(cells) * 0.38 + (per * norm(cells)) * 0.25)
detail = norm(1 - fbm(worley, 16, 3, 0.5))
wisps = norm(fbm(perlin, 16, 4))

img = np.ones((N, N, 4), np.float32)
img[..., 0], img[..., 1], img[..., 2] = shapes, detail, wisps
o = bpy.data.images.new("clouds", N, N, alpha=True)
o.colorspace_settings.name = "Non-Color"
o.pixels = img.ravel()
o.filepath_raw = out
o.file_format = "PNG"
o.save()
print("wrote", out, "shapes mean %.2f detail mean %.2f wisps mean %.2f" % (shapes.mean(), detail.mean(), wisps.mean()))

if preview:
    # Shapes after the sky's coverage cut (cover 0.45), tiled 2 x 2: to look at by eye.
    cover = 0.45
    d = np.clip((shapes - (1 - cover)) / cover, 0, 1)
    d = np.clip(d - (1 - detail) * 0.35, 0, 1)
    t = np.tile(d, (2, 2))
    pv = np.ones((2 * N, 2 * N, 4), np.float32)
    pv[..., 0] = pv[..., 1] = 0.35 + 0.65 * t
    pv[..., 2] = 0.55 + 0.45 * t
    p = bpy.data.images.new("preview", 2 * N, 2 * N, alpha=True)
    p.pixels = pv.ravel()
    p.filepath_raw = preview
    p.file_format = "PNG"
    p.save()
    print("wrote", preview)
