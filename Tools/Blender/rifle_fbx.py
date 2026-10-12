# Blender: the rifle for Unity (from the Bevy client's GLB): one mesh, 1 unit = 1 m, its
# folding sights folded down (a holographic sight rides the rail instead). The GLB's
# muzzle is -x, its top +z. Args after --: in.glb out.fbx name [low.fbx low_name triangles]
# With the last three, also a light copy for the crowd (soldiers tens of pixels tall):
# decimated to about that many triangles, same UVs and frame.
import bpy, sys, os
args = sys.argv[sys.argv.index("--") + 1:]
src, out, name = args[:3]
low = args[3:6] if len(args) >= 6 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
m = bpy.context.view_layer.objects.active
m.name = name
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# Fold the sights: rear (x 0.27..0.36) and front (x -0.18..-0.11), down to just over the rail.
RAIL = 0.17
folded = 0
for v in m.data.vertices:
    x, z = v.co.x, v.co.z
    if z > RAIL and (0.265 <= x <= 0.365 or -0.185 <= x <= -0.105):
        v.co.z = RAIL + (z - RAIL) * 0.2
        folded += 1
m.data.update()
print("folded", folded, "vertices")


def export(obj, path):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP')
    print("exported", path, len(obj.data.polygons), "faces")


export(m, out)

if low:
    low_out, low_name, target = low[0], low[1], int(low[2])
    lo = m.copy()
    lo.data = m.data.copy()
    lo.name = low_name
    bpy.context.collection.objects.link(lo)
    bpy.context.view_layer.update()
    # The GLB's faces share no vertices (split for its normals and UVs): weld them
    # first, or collapsing edges shreds the surface into loose triangles. (UVs stay
    # per face corner: the seams survive the weld.)
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    print("welded", before, "->", len(bm.verts), "vertices")
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
    print("decimated", tris, "->", sum(len(p.vertices) - 2 for p in lo.data.polygons), "triangles")
    export(lo, low_out)
