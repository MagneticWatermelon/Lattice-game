# Blender: the rifle's ammo cell, which a reload takes out and puts back. The rifle's
# round bay (both sides of the receiver, a dark disc in a ring) holds a canister
# across it: this is that canister, so the hands can carry it. Seated, its end caps
# sit just proud of the bay's discs and cover them; out, the dark discs read as the
# empty bay. Modeled in the rifle GLB's frame (muzzle -x, top +z, size 1) but
# centered on its own middle, its axis along y (the rifle's left-right), so it can be
# carried by its center; the seat is CombatLook.CellSeat.
# Materials: CellBody (gunmetal), CellTrim (machined steel), CellGlow (the charge,
# lit in the faction's color in Unity).
# Args after --: rifle_view.glb out.fbx preview_dir
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix
rifle_glb, out, prev = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
col = bpy.context.scene.collection

# The seat, measured from the rifle: the bay's dark disc (both sides) is centered at
# x 0.1887, z -0.0716, radius 0.032, on a shallow dome reaching |y| 0.0945 (its ring,
# r 30-40 mm, at 0.0915-0.0906). The caps cover the disc, 1.7 mm proud of the dome
# (their recessed core 0.9 mm).
SEAT = Vector((0.1887, 0.0, -0.0716))
HALF = 0.0962
CAP_R, CAP_T = 0.0345, 0.009
BODY_R = 0.0300

mats = {}
for name, color in (("CellBody", (0.05, 0.055, 0.06, 1)), ("CellTrim", (0.42, 0.44, 0.46, 1)), ("CellGlow", (0.3, 1.0, 0.8, 1))):
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    mats[name] = m


def ring_cyl(name, r, y0, y1, segs=40, mat="CellBody", cap=True):
    """A cylinder along y from y0 to y1."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, segments=segs, radius1=r, radius2=r, depth=abs(y1 - y0))
    rot = Matrix.Rotation(math.radians(-90), 4, 'X')   # z axis -> y axis
    for v in bm.verts:
        v.co = rot @ v.co + Vector((0, (y0 + y1) / 2, 0))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    col.objects.link(o)
    o.data.materials.append(mats[mat])
    return o


def bevel(o, width, segments=2, angle=40):
    b = o.modifiers.new("bevel", 'BEVEL')
    b.width = width
    b.segments = segments
    b.limit_method = 'ANGLE'
    b.angle_limit = math.radians(angle)


def apply_all(o):
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


parts = []
# The canister's body, inside the rifle when seated.
parts.append(ring_cyl("body", BODY_R, -HALF + CAP_T, HALF - CAP_T, 40, "CellBody"))
# Its charge window: a glowing band under six ribs, between two collars.
parts.append(ring_cyl("glow_band", BODY_R + 0.0004, -0.042, 0.042, 40, "CellGlow", cap=False))
for k in range(6):
    a = k / 6 * math.tau
    c = Vector((math.cos(a) * (BODY_R + 0.0012), 0, math.sin(a) * (BODY_R + 0.0012)))
    me = bpy.data.meshes.new("rib")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 0.0042, v.co.y * 0.086, v.co.z * 0.0042))
    rot = Matrix.Rotation(-a, 4, 'Y')
    for v in bm.verts:
        v.co = rot @ v.co + c
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("rib", me)
    col.objects.link(o)
    o.data.materials.append(mats["CellBody"])
    bevel(o, 0.0008, 1)
    parts.append(o)
for y in (-0.047, 0.047):
    c = ring_cyl("collar", BODY_R + 0.0022, y - 0.005, y + 0.005, 40, "CellTrim")
    bevel(c, 0.0012, 2)
    parts.append(c)
# Knurled grip rings by each cap (what the hand pulls on).
for s in (-1, 1):
    g = ring_cyl("grip", BODY_R + 0.0016, s * 0.062, s * (HALF - CAP_T), 48, "CellBody")
    bm = bmesh.new()
    bm.from_mesh(g.data)
    for v in bm.verts:
        r = math.hypot(v.co.x, v.co.z)
        if r > BODY_R:
            a = math.atan2(v.co.z, v.co.x)
            k = round(a / (math.tau / 48))
            if k % 2:
                f = (BODY_R + 0.0004) / r
                v.co.x *= f
                v.co.z *= f
    bm.to_mesh(g.data)
    bm.free()
    parts.append(g)
# The end caps: a machined rim with six notches, a dark ring, the glowing core.
for s in (-1, 1):
    y_in, y_out = s * (HALF - CAP_T), s * HALF
    cap = ring_cyl("cap", CAP_R, min(y_in, y_out), max(y_in, y_out), 48, "CellTrim")
    # Recess the face's middle: a cutter cylinder.
    cut = ring_cyl("cut", 0.0225, y_out - s * 0.0012 - 0.01, y_out - s * 0.0012 + 0.01, 48, "CellTrim")
    if s > 0:
        cut.location.y = 0.0  # (centered already: from y_out-0.0118 to y_out+0.0082)
    b = cap.modifiers.new("recess", 'BOOLEAN')
    b.object = cut
    b.operation = 'DIFFERENCE'
    b.solver = 'EXACT'
    apply_all(cap)
    bpy.data.objects.remove(cut, do_unlink=True)
    for k in range(6):
        a = (k + 0.5) / 6 * math.tau
        me = bpy.data.meshes.new("notch")
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * 0.006, v.co.y * 0.006, v.co.z * 0.007))
        rot = Matrix.Rotation(-a, 4, 'Y')
        cpos = Vector((math.cos(a) * CAP_R, y_out, math.sin(a) * CAP_R))
        for v in bm.verts:
            v.co = rot @ v.co + cpos
        bm.to_mesh(me)
        bm.free()
        n = bpy.data.objects.new("notch", me)
        col.objects.link(n)
        b = cap.modifiers.new("notch", 'BOOLEAN')
        b.object = n
        b.operation = 'DIFFERENCE'
        b.solver = 'EXACT'
        apply_all(cap)
        bpy.data.objects.remove(n, do_unlink=True)
    bevel(cap, 0.0011, 2, 35)
    parts.append(cap)
    # The recess's floor: a dark ring, and the core glowing in its middle.
    floor_y = y_out - s * 0.0012
    ring = ring_cyl("cap_ring", 0.0224, floor_y - s * 0.0012, floor_y, 48, "CellBody")
    parts.append(ring)
    core = ring_cyl("core", 0.0135, floor_y - s * 0.0005, floor_y + s * 0.0004, 40, "CellGlow")
    parts.append(core)
    # Six studs round the core, between the notches: the cap (and the whole cell)
    # looks the same every 60 degrees, so the empty reload's 60-degree twist can
    # hand the cell back unturned.
    for k in range(6):
        a = k / 6 * math.tau
        st = ring_cyl("stud", 0.0026, floor_y - s * 0.0004, floor_y + s * 0.0022, 12, "CellTrim")
        st.location = Vector((math.cos(a) * 0.0185, 0, math.sin(a) * 0.0185))
        bevel(st, 0.0006, 1)
        parts.append(st)

for o in parts:
    apply_all(o)
    if o.location.length > 0:
        o.data.transform(Matrix.Translation(o.location))
        o.location = (0, 0, 0)
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
cell = bpy.context.view_layer.objects.active
cell.name = "Cell"
# One slot per material, in a fixed order (Unity's submeshes follow it).
want = ["CellBody", "CellTrim", "CellGlow"]
# (A boolean's cut faces take its cutter's slot: the notches had none. They're trim.)
remap = {i: want.index(s.material.name if s.material else "CellTrim") for i, s in enumerate(cell.material_slots)}
for p in cell.data.polygons:
    p.material_index = remap[p.material_index]
# In place: clearing the slots would send every face to the first.
while len(cell.data.materials) > len(want):
    cell.data.materials.pop(index=len(cell.data.materials) - 1)
for i, n in enumerate(want):
    cell.data.materials[i] = mats[n]
counts = [0, 0, 0]
for p in cell.data.polygons:
    counts[p.material_index] += 1
print("faces per material", dict(zip(want, counts)))
# Triangulated here: the knurled rings' and the notched caps' faces are concave
# n-gons, which Unity's importer would drop as self-intersecting.
bm = bmesh.new()
bm.from_mesh(cell.data)
bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
bm.to_mesh(cell.data)
bm.free()
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.cube_project(cube_size=0.05)
bpy.ops.object.mode_set(mode='OBJECT')
tris = sum(len(p.vertices) - 2 for p in cell.data.polygons)
print("cell tris", tris, "materials", [m.name for m in cell.data.materials])
xs = [v.co.x for v in cell.data.vertices]; ys = [v.co.y for v in cell.data.vertices]; zs = [v.co.z for v in cell.data.vertices]
print("cell bbox x", min(xs), max(xs), "y", min(ys), max(ys), "z", min(zs), max(zs))
bpy.ops.object.select_all(action='DESELECT')
cell.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP')
print("exported", out)

# Previews: seated in the rifle, pulled half out, and alone.
bpy.ops.import_scene.gltf(filepath=rifle_glb)
rifle = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o is not cell]
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 900
sc.render.resolution_y = 650
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.74, 0.8, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.4
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 4
sun.rotation_euler = (math.radians(40), 0, math.radians(-35)); col.objects.link(sun)
for name, (base, metal, rough, emit) in {"CellBody": ((0.045, 0.05, 0.055, 1), 0.7, 0.42, 0), "CellTrim": ((0.5, 0.52, 0.55, 1), 1.0, 0.3, 0),
                                         "CellGlow": ((0.2, 1.0, 0.75, 1), 0.0, 0.3, 6)}.items():
    m = mats[name]
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = base
    bs.inputs["Metallic"].default_value = metal
    bs.inputs["Roughness"].default_value = rough
    if emit:
        bs.inputs["Emission Color"].default_value = base
        bs.inputs["Emission Strength"].default_value = emit
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); col.objects.link(cam); sc.camera = cam


def shot(nm, loc, target, lens=50):
    cam.data.lens = lens
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(prev, nm)
    bpy.ops.render.render(write_still=True)


cell.location = SEAT
shot("cell_seated_left.png", (0.2, -0.55, 0.05), (0.18, 0, -0.04), 70)
shot("cell_seated_34.png", (0.55, -0.5, 0.25), (0.15, 0, -0.03), 55)
cell.location = SEAT + Vector((0, -0.12, 0))
shot("cell_out_34.png", (0.55, -0.6, 0.25), (0.15, -0.06, -0.03), 50)
for o in rifle:
    o.hide_render = True
cell.location = Vector((0, 0, 0))
cell.rotation_euler = (0, 0, math.radians(25))
shot("cell_alone.png", (0.12, -0.32, 0.1), (0, 0, 0), 60)
