# Blender: compose images into a grid, each scaled to fit a cell of width `size` keeping its
# aspect (cells as tall as the tallest scaled image). Args after --: out.png cols size img1 img2 ...
import bpy, sys, numpy as np
argv = sys.argv[sys.argv.index("--") + 1:]
out, cols, size, files = argv[0], int(argv[1]), int(argv[2]), argv[3:]
imgs = []
for f in files:
    img = bpy.data.images.load(f)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    nh = max(1, int(round(h * size / w)))
    ys = (np.arange(nh) * h // nh).clip(0, h - 1)
    xs = (np.arange(size) * w // size).clip(0, w - 1)
    imgs.append(px[ys][:, xs])
cell_h = max(i.shape[0] for i in imgs)
rows = (len(imgs) + cols - 1) // cols
sheet = np.zeros((rows * cell_h, cols * size, 4), dtype=np.float32)
sheet[..., 3] = 1
for i, tile in enumerate(imgs):
    r, c = divmod(i, cols)
    th = tile.shape[0]
    # Blender rows start at the bottom: the first image goes top-left.
    y0 = (rows - 1 - r) * cell_h + (cell_h - th)
    sheet[y0:y0 + th, c * size:(c + 1) * size] = tile
o = bpy.data.images.new("sheet", cols * size, rows * cell_h, alpha=True)
o.pixels = sheet.ravel()
o.filepath_raw = out
o.file_format = "PNG"
o.save()
print("wrote", out)
