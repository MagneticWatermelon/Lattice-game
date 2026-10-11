# Blender: a holographic sight for the rifle's rail, modeled in the rifle GLB's frame
# (muzzle -x, top +z, size 1), so in Unity it sits on the rifle with no offset.
# A clamp on the rail, a low body, a hood (a squared tube) over two glass windows.
# Two materials: HoloBody, HoloGlass (the reticle is the glass shader's).
# Args after --: rifle.glb out.fbx preview_dir
import bpy, bmesh, sys, os, math
from mathutils import Vector
rifle_glb, out, prev = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
col = bpy.context.scene.collection

def box(name, x0, x1, y0, y1, z0, z1, bevel=0.0, segments=2):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((x0 + x1) / 2 + v.co.x * (x1 - x0), (y0 + y1) / 2 + v.co.y * (y1 - y0), (z0 + z1) / 2 + v.co.z * (z1 - z0)))
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    if bevel > 0:
        b = o.modifiers.new("bevel", 'BEVEL'); b.width = bevel; b.segments = segments; b.limit_method = 'ANGLE'
    return o

def cyl(name, center, axis, r, length, verts=20, bevel=0.0):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=length)
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis))
    for v in bm.verts:
        v.co = rot @ v.co + Vector(center)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    if bevel > 0:
        b = o.modifiers.new("bevel", 'BEVEL'); b.width = bevel; b.segments = 2; b.limit_method = 'ANGLE'
    return o

def apply_all(o):
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)

# Window: the hood's opening; the line of sight runs through its middle (z 0.224).
WY, WZ0, WZ1 = 0.0262, 0.2025, 0.2465
parts = []
parts.append(box("clamp", 0.092, 0.208, -0.027, 0.027, 0.164, 0.182, 0.0025))
parts.append(cyl("bolt", (0.15, 0.0, 0.173), (0, 1, 0), 0.0045, 0.066, 16, 0.001))
parts.append(cyl("bolt_head", (0.15, -0.034, 0.173), (0, 1, 0), 0.0065, 0.004, 6, 0.0008))
parts.append(box("body", 0.082, 0.218, -0.03, 0.03, 0.18, 0.2005, 0.004, 3))
# Buttons on the back face, a knob on the right side.
parts.append(box("btn_l", 0.217, 0.2215, -0.017, -0.006, 0.185, 0.195, 0.0012))
parts.append(box("btn_r", 0.217, 0.2215, 0.006, 0.017, 0.185, 0.195, 0.0012))
parts.append(cyl("knob", (0.14, 0.034, 0.19), (0, 1, 0), 0.0075, 0.009, 24, 0.001))
# The hood: a squared tube along x, its front edge cut back at the top.
hood = box("hood", 0.142, 0.206, -0.0298, 0.0298, 0.198, 0.2505, 0.0)
hole = box("hole", 0.05, 0.25, -WY, WY, WZ0, WZ1, 0.0)
bm_ = hood.modifiers.new("cut", 'BOOLEAN'); bm_.object = hole; bm_.operation = 'DIFFERENCE'; bm_.solver = 'EXACT'
apply_all(hood)
bpy.data.objects.remove(hole, do_unlink=True)
bv = hood.modifiers.new("bevel", 'BEVEL'); bv.width = 0.0016; bv.segments = 3; bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(40)
parts.append(hood)
# A visor: the hood's top runs on past the window.
parts.append(box("visor", 0.118, 0.15, -0.0298, 0.0298, 0.2455, 0.2505, 0.0015))
body_parts = list(parts)
for o in body_parts:
    apply_all(o)
bpy.ops.object.select_all(action='DESELECT')
for o in body_parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = body_parts[0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "HoloSight"
mb = bpy.data.materials.new("HoloBody"); mb.diffuse_color = (0.05, 0.055, 0.06, 1)
body.data.materials.append(mb)
# The hood's inside (around the window): its own matte black, as real sights are.
mi = bpy.data.materials.new("HoloInner"); mi.diffuse_color = (0.01, 0.01, 0.01, 1)
body.data.materials.append(mi)
inner = 0
for f in body.data.polygons:
    c = f.center; n = f.normal
    in_window = abs(c.y) < WY + 0.0012 and WZ0 - 0.0012 < c.z < WZ1 + 0.0012 and 0.14 < c.x < 0.21
    if in_window and abs(n.x) < 0.5:
        f.material_index = 1; inner += 1
print("inner faces", inner)
# Glass: two thin panes inside the hood, rear and front.
glass = []
for nm, x in (("rear_glass", 0.199), ("front_glass", 0.149)):
    me = bpy.data.meshes.new(nm)
    y0, y1, z0, z1 = -WY + 0.0003, WY - 0.0003, WZ0 + 0.0003, WZ1 - 0.0003
    me.from_pydata([(x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new(nm, me); col.objects.link(g)
    glass.append(g)
bpy.ops.object.select_all(action='DESELECT')
for g in glass:
    g.select_set(True)
bpy.context.view_layer.objects.active = glass[0]
bpy.ops.object.join()
gl = bpy.context.view_layer.objects.active
mg = bpy.data.materials.new("HoloGlass"); mg.diffuse_color = (0.6, 0.8, 0.9, 0.3)
gl.data.materials.append(mg)
# One object, two materials.
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True); gl.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
holo = bpy.context.view_layer.objects.active
holo.name = "HoloSight"
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
# UVs for the shader (box projection is enough for flat materials).
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.cube_project(cube_size=0.05); bpy.ops.object.mode_set(mode='OBJECT')
print("holo tris", sum(len(p.vertices) - 2 for p in holo.data.polygons), "materials", [m.name for m in holo.data.materials])
bpy.ops.object.select_all(action='DESELECT'); holo.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='OFF', bake_anim=False, path_mode='STRIP')
print("exported", out)

# Previews: on the rifle, 3/4 from the rear-left, and down the line of sight.
bpy.ops.import_scene.gltf(filepath=rifle_glb)
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and o is not holo:
        mw = o.matrix_world
        for v in o.data.vertices:
            w = mw @ v.co
            if w.z > 0.17 and (0.265 <= w.x <= 0.365 or -0.185 <= w.x <= -0.105):
                w.z = 0.17 + (w.z - 0.17) * 0.2
                v.co = mw.inverted() @ w
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = 900; sc.render.resolution_y = 650
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.74, 0.8, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.5
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 4
sun.rotation_euler = (math.radians(35), 0, math.radians(-30)); col.objects.link(sun)
mb.use_nodes = True
bs = mb.node_tree.nodes["Principled BSDF"]; bs.inputs["Base Color"].default_value = (0.05, 0.055, 0.06, 1); bs.inputs["Metallic"].default_value = 0.6; bs.inputs["Roughness"].default_value = 0.45
mg.use_nodes = True
gs = mg.node_tree.nodes["Principled BSDF"]; gs.inputs["Base Color"].default_value = (0.7, 0.9, 1.0, 1); gs.inputs["Alpha"].default_value = 0.25; gs.inputs["Roughness"].default_value = 0.05
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); col.objects.link(cam); sc.camera = cam
cam.data.lens = 50
def shot(nm, loc, target):
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Z').to_euler()
    sc.render.filepath = os.path.join(prev, nm); bpy.ops.render.render(write_still=True)
shot("holo_34.png", (0.55, 0.3, 0.42), (0.12, 0.0, 0.2))
cam.data.lens = 35
shot("holo_eye.png", (0.42, 0.0, 0.224), (0.0, 0.0, 0.224))
cam.data.lens = 70
shot("holo_side.png", (0.15, -0.6, 0.22), (0.15, 0.0, 0.2))
