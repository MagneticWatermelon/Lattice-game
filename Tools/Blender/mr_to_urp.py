# Blender: glTF metallic-roughness (G roughness, B metallic) to URP's metallic
# map (R metallic, A smoothness). Args after --: in.png out.png [smoothness scale]
import bpy, sys, numpy as np
a = sys.argv[sys.argv.index("--") + 1:]
src, out = a[0], a[1]
scale = float(a[2]) if len(a) > 2 else 1.0
img = bpy.data.images.load(src); img.colorspace_settings.name = "Non-Color"
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
o = np.zeros_like(px)
o[..., 0] = px[..., 2]
o[..., 1] = px[..., 2]
o[..., 2] = px[..., 2]
o[..., 3] = np.clip((1.0 - px[..., 1]) * scale, 0, 1)
im = bpy.data.images.new("o", w, h, alpha=True); im.colorspace_settings.name = "Non-Color"
im.pixels = o.ravel(); im.filepath_raw = out; im.file_format = "PNG"; im.save()
print("wrote", out, "metal mean", float(o[..., 0].mean()), "smooth mean", float(o[..., 3].mean()))
