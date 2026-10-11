# Blender: the rifle for Unity (from the Bevy client's GLB): one mesh, 1 unit = 1 m, its
# folding sights folded down (a holographic sight rides the rail instead). The GLB's
# muzzle is -x, its top +z. Args after --: in.glb out.fbx name
import bpy, sys, os
src, out, name = sys.argv[sys.argv.index("--") + 1:][:3]
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
bpy.ops.object.select_all(action='DESELECT'); m.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP')
print("exported", out)
