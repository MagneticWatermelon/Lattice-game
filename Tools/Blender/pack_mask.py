# Blender: pack a URP mask map from Poly Haven's ARM (AO, roughness, metal) and
# displacement: R metallic, G AO, B height, A smoothness. Args after --: arm disp out.png
import bpy, sys, numpy as np
arm_p, disp_p, out = sys.argv[sys.argv.index("--") + 1:][:3]
def load(p):
    img = bpy.data.images.load(p)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    return np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4), w, h
arm, w, h = load(arm_p)
disp, w2, h2 = load(disp_p)
assert (w, h) == (w2, h2), (w, h, w2, h2)
mask = np.empty((h, w, 4), dtype=np.float32)
mask[..., 0] = arm[..., 2]          # metallic
mask[..., 1] = arm[..., 0]          # AO
mask[..., 2] = disp[..., 0]         # height
mask[..., 3] = 1.0 - arm[..., 1]    # smoothness
o = bpy.data.images.new("mask", w, h, alpha=True, float_buffer=False)
o.colorspace_settings.name = "Non-Color"
o.pixels = mask.ravel()
o.filepath_raw = out
o.file_format = "PNG"
o.save()
print("wrote", out, w, h, "smooth mean", float(mask[..., 3].mean()), "ao mean", float(mask[..., 1].mean()))
