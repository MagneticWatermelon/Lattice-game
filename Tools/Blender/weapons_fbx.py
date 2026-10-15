# Blender: the classes' weapons (U6), made from the rifle so they share its material,
# receiver, ammo cell, grip and sight rail: the carbine, the shotgun, the marksman
# rifle and the SMG. What sets them apart is what's ahead of the left hand: the
# handguard is shortened or lengthened (the muzzle block moved with it), the
# shotgun's front swells and opens into a wide bore, the marksman rifle grows a long
# barrel with a brake. Added parts take a near-black texel of the rifle's own texture.
# Also the marksman rifle's scope (Scope.fbx: body, lens caps, glass, like the
# holographic sight). All in the rifle GLB's frame (muzzle -x, top +z, 1 m long).
# Args after --: rifle.glb rifle_view.glb out_dir preview_dir
# Writes <Name>.fbx (third person), <Name>View.fbx (first person), <Name>Low.fbx (the
# crowd's, ~600 triangles) per weapon, and prints each muzzle (for CombatLook/SoldierLook).
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector

rifle_glb, view_glb, out_dir, prev = sys.argv[sys.argv.index("--") + 1:][:4]
os.makedirs(out_dir, exist_ok=True)
os.makedirs(prev, exist_ok=True)

# The handguard runs from the receiver (A) to the muzzle block (B); the left hand
# holds it behind A, so nothing behind A moves.
A, B, FRONT = -0.12, -0.43, -0.5
BORE_Z = 0.069  # the bore's height (the muzzle in CombatLook/SoldierLook)

# name: handguard scale, front swell, barrels out of the muzzle block (side offset,
# radius, length) and a brake on a single barrel (length, radius)
WEAPONS = {
    "Carbine": dict(mid=0.62),
    "Shotgun": dict(mid=0.55, swell=1.3, barrels=[(-0.03, 0.026, 0.06), (0.03, 0.026, 0.06)]),
    "Marksman": dict(mid=1.35, barrels=[(0.0, 0.018, 0.12)], brake=(0.06, 0.027)),
    "Smg": dict(mid=0.3),
}


def remap_x(x, mid):
    if x >= A:
        return x
    if x >= B:
        return A + (x - A) * mid
    return A + (B - A) * mid + (x - B)


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    m = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # The folding iron sights folded down, as rifle_fbx.py does.
    for v in m.data.vertices:
        x, z = v.co.x, v.co.z
        if z > 0.17 and (0.265 <= x <= 0.365 or -0.185 <= x <= -0.105):
            v.co.z = 0.17 + (z - 0.17) * 0.2
    m.data.update()
    return m


def dark_uv(obj):
    """The UV of the darkest 9x9 patch of the object's base color texture."""
    img = None
    for mat in obj.data.materials:
        if mat and mat.use_nodes:
            for n in mat.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image is not None:
                    links = [l for l in mat.node_tree.links if l.from_node == n and l.to_socket.name == "Base Color"]
                    if links:
                        img = n.image
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    lum = px[..., :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    k = 9
    c = np.cumsum(np.cumsum(np.pad(lum, ((1, 0), (1, 0))), 0), 1)
    box = c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]
    y, x = np.unravel_index(np.argmin(box), box.shape)
    u, v = (x + k / 2) / w, (y + k / 2) / h
    print(f"dark texel of {img.name} ({w}x{h}) at {u:.4f}, {v:.4f}: {box[y, x] / (k * k):.4f}")
    return (u, v)


def make_mesh(name, build):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def cyl_x(bm, x0, x1, r0, r1, y=0.0, z=BORE_Z, segs=20):
    """A frustum along x, from x0 (radius r0) to x1 (r1), capped."""
    geom = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r0, radius2=r1, depth=abs(x1 - x0))
    rot = Vector((0, 0, 1)).rotation_difference(Vector((1, 0, 0)) if x1 > x0 else Vector((-1, 0, 0)))
    mid = Vector(((x0 + x1) / 2, y, z))
    for v in geom["verts"]:
        v.co = rot @ v.co + mid


def box_bm(bm, x0, x1, y0, y1, z0, z1):
    geom = bmesh.ops.create_cube(bm, size=1.0)
    for v in geom["verts"]:
        v.co = Vector(((x0 + x1) / 2 + v.co.x * (x1 - x0), (y0 + y1) / 2 + v.co.y * (y1 - y0), (z0 + z1) / 2 + v.co.z * (z1 - z0)))


def attach(base, part, uv):
    """Joins part into base, every UV of the part on the dark texel, its faces flat-shaded."""
    if not part.data.uv_layers:
        part.data.uv_layers.new(name=base.data.uv_layers[0].name)
    for layer in part.data.uv_layers:
        for d in layer.data:
            d.uv = uv
    part.data.materials.clear()
    for mat in base.data.materials:
        part.data.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT')
    part.select_set(True)
    base.select_set(True)
    bpy.context.view_layer.objects.active = base
    bpy.ops.object.join()


def variant(src, name, p, uv, extras=True):
    """The rifle reshaped as weapon p; with its added barrels unless extras is False."""
    m = src.copy()
    m.data = src.data.copy()
    m.name = name
    bpy.context.scene.collection.objects.link(m)
    mid, swell = p.get("mid", 1.0), p.get("swell", 1.0)
    for v in m.data.vertices:
        x, y, z = v.co
        if swell != 1.0 and x < A:
            k = 1.0 + (swell - 1.0) * smooth((A - x) / 0.1)
            ku = 1.0 + (k - 1.0) * 0.45  # less up than down: the rail stays under the sight
            v.co.y = y * k
            v.co.z = BORE_Z + (z - BORE_Z) * (k if z < BORE_Z else ku)
        v.co.x = remap_x(x, mid)
    m.data.update()
    front = remap_x(FRONT, mid)
    muzzle = front
    if "barrels" in p:
        brake = p.get("brake")
        muzzle = front - p["barrels"][0][2] - (brake[0] if brake else 0.0)
    if extras:
        add_barrels(m, name, p, uv, 20)
        print(f"{name}: length {0.5 - muzzle:.3f} m, muzzle at x {muzzle:.4f} (CombatLook/SoldierLook muzzle z {-muzzle:.4f})")
    return m


def add_barrels(m, name, p, uv, segs):
    """Weapon p's barrels (and brake) out of the muzzle block of m, segs sides round."""
    if "barrels" not in p:
        return
    front = remap_x(FRONT, p.get("mid", 1.0))
    brake = p.get("brake")
    def build(bm):
        for y, r, length in p["barrels"]:
            # Out of the block (from inside it), a collar at the face, a ring at the end.
            cyl_x(bm, front + 0.04, front - length, r, r * 0.92, y=y, segs=segs)
            cyl_x(bm, front + 0.004, front - 0.012, r * 1.22, r * 1.22, y=y, segs=segs)
            if segs > 8:
                cyl_x(bm, front - length + 0.008, front - length - 0.002, r * 1.12, r * 1.12, y=y, segs=segs)
        if brake:
            bl, br = brake
            x0 = front - p["barrels"][0][2]
            cyl_x(bm, x0 + 0.006, x0 - bl, br, br * 0.9, segs=segs)
            # Its ports: slabs across, standing out each side.
            for i in range(3 if segs > 8 else 1):
                xa = x0 - 0.012 - i * 0.016
                box_bm(bm, xa - 0.006, xa, -br - 0.006, br + 0.006, BORE_Z - 0.008, BORE_Z + 0.008)
    attach(m, make_mesh(name + "_barrels", build), uv)


def export(obj, path):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP')
    print("exported", path, len(obj.data.polygons), "faces")


def decimate(obj, name, target):
    lo = obj.copy()
    lo.data = obj.data.copy()
    lo.name = name
    bpy.context.scene.collection.objects.link(lo)
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bm.to_mesh(lo.data)
    bm.free()
    tris = sum(len(p.vertices) - 2 for p in lo.data.polygons)
    mod = lo.modifiers.new("Decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = min(1.0, target / max(tris, 1))
    mod.use_collapse_triangulate = True
    bpy.ops.object.select_all(action='DESELECT')
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print("decimated", name, tris, "->", sum(len(p.vertices) - 2 for p in lo.data.polygons), "triangles")
    return lo


# --- The scope: low on the rail (top z 0.17) over the receiver, its axis the line
# of sight (z 0.206: CombatLook.ScopeSight). Rear lens at x 0.255.
SZ = 0.206
def scope_parts(bm, segs=28):
    detail = segs > 8
    cyl_x(bm, -0.04, 0.2, 0.019, 0.019, z=SZ, segs=segs)              # tube
    cyl_x(bm, -0.075, -0.04, 0.03, 0.019, z=SZ, segs=segs)            # objective bell
    cyl_x(bm, -0.1, -0.075, 0.03, 0.03, z=SZ, segs=segs)
    cyl_x(bm, 0.2, 0.215, 0.019, 0.0235, z=SZ, segs=segs)             # eyepiece
    cyl_x(bm, 0.215, 0.255, 0.0235, 0.0235, z=SZ, segs=segs)
    for x in (0.0, 0.15):                                             # mounts: rings and bases
        if detail:
            cyl_x(bm, x - 0.009, x + 0.009, 0.0225, 0.0225, z=SZ, segs=segs)
        box_bm(bm, x - 0.012, x + 0.012, -0.02, 0.02, 0.164 if detail else 0.13, SZ - 0.01)
    if not detail:
        return
    cyl_x(bm, 0.226, 0.232, 0.0235, 0.0235, z=SZ, segs=segs)          # its grip ring
    # Turrets: elevation on top, windage on the right.
    geom = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.011, radius2=0.011, depth=0.016)
    for v in geom["verts"]:
        v.co += Vector((0.08, 0.0, SZ + 0.019 + 0.006))
    geom = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.011, radius2=0.011, depth=0.016)
    rot = Vector((0, 0, 1)).rotation_difference(Vector((0, 1, 0)))
    for v in geom["verts"]:
        v.co = rot @ v.co + Vector((0.08, 0.019 + 0.006, SZ))


def make_scope(segs=28):
    s = make_mesh("Scope", lambda bm: scope_parts(bm, segs))
    mb = bpy.data.materials.get("HoloBody") or bpy.data.materials.new("HoloBody"); mb.diffuse_color = (0.05, 0.055, 0.06, 1)
    mi = bpy.data.materials.get("HoloInner") or bpy.data.materials.new("HoloInner"); mi.diffuse_color = (0.01, 0.01, 0.01, 1)
    mg = bpy.data.materials.get("HoloGlass") or bpy.data.materials.new("HoloGlass"); mg.diffuse_color = (0.6, 0.8, 0.9, 0.3)
    for mt in (mb, mi, mg):
        s.data.materials.append(mt)
    # The lens ends' faces matte black (inside the glass).
    for f in s.data.polygons:
        if abs(f.normal.x) > 0.95 and ((f.center.x < -0.099) or (f.center.x > 0.254)) and abs(f.center.z - SZ) < 0.025:
            f.material_index = 1
    glass = make_mesh("ScopeGlass", lambda bm: [cyl_x(bm, -0.1015, -0.1005, 0.027, 0.027, z=SZ, segs=28),
                                                 cyl_x(bm, 0.2555, 0.2565, 0.0205, 0.0205, z=SZ, segs=28)])
    for mt in (mb, mi, mg):
        glass.data.materials.append(mt)
    for f in glass.data.polygons:
        f.material_index = 2
    bpy.ops.object.select_all(action='DESELECT')
    s.select_set(True); glass.select_set(True)
    bpy.context.view_layer.objects.active = s
    bpy.ops.object.join()
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=0.05); bpy.ops.object.mode_set(mode='OBJECT')
    print("scope tris", sum(len(p.vertices) - 2 for p in s.data.polygons))
    return s


def preview(objs, path, scope=None):
    """Side views, one row per weapon, on a 10 cm grid (workbench, textured)."""
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = 1400, 300 * len(objs)
    for i, o in enumerate(objs):
        o.location = (0, 0, -0.45 * i)
        o.hide_render = False
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'
    cam.ortho_scale = max(1.45, 0.45 * len(objs))
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (-0.17, -2, -0.45 * (len(objs) - 1) / 2); co.rotation_euler = (math.radians(90), 0, 0)
    sc.render.resolution_x, sc.render.resolution_y = 1400, int(1400 * 0.45 * len(objs) / 1.45)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    for o in objs:
        o.location = (0, 0, 0)


for src_path, suffix in ((view_glb, "View"), (rifle_glb, "")):
    src = load(src_path)
    uv = dark_uv(src)
    made = []
    for name, p in WEAPONS.items():
        v = variant(src, name + suffix, p, uv)
        export(v, os.path.join(out_dir, name + suffix + ".fbx"))
        made.append(v)
    if suffix == "":
        # The crowd's: the body decimated alone (added parts would spend its budget),
        # then the barrels and the marksman rifle's scope added back eight-sided, in
        # the rifle's texture (at tens of pixels the scope's glass doesn't show).
        lows = []
        for name, p in WEAPONS.items():
            body = variant(src, name + "Body", p, uv, extras=False)
            lo = decimate(body, name + "Low", 540 if "barrels" in p else 600)
            add_barrels(lo, name, p, uv, 8)
            if name == "Marksman":
                attach(lo, make_scope(8), uv)
            print(name + "Low", sum(len(f.vertices) - 2 for f in lo.data.polygons), "triangles")
            export(lo, os.path.join(out_dir, name + "Low.fbx"))
            lows.append(lo)
        for o in bpy.data.objects:
            o.hide_render = o not in [src] + made
        src.hide_render = False
        preview([src] + made, os.path.join(prev, "weapons_side.png"))
        for o in bpy.data.objects:
            o.hide_render = o not in [src] + lows
        preview([src] + lows, os.path.join(prev, "weapons_low.png"))

scope = make_scope()
export(scope, os.path.join(out_dir, "Scope.fbx"))
