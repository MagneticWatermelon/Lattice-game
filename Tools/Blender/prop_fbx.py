# Blender: a static model for Unity: one mesh, its textures beside the FBX
# (Name_albedo/_normal/_mr.png), 1 unit = 1 m. Args after --: in.glb out.fbx name
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
mins = [min((m.matrix_world @ v.co)[k] for v in m.data.vertices) for k in range(3)]
maxs = [max((m.matrix_world @ v.co)[k] for v in m.data.vertices) for k in range(3)]
print("bounds", [round(v, 3) for v in mins], [round(v, 3) for v in maxs], "tris", sum(len(p.vertices) - 2 for p in m.data.polygons))
texdir = os.path.dirname(out)
mat = m.data.materials[0] if m.data.materials else None
if mat and mat.node_tree:
    for link in mat.node_tree.links:
        n = link.from_node
        if n.type == 'TEX_IMAGE' and n.image:
            role = "albedo" if link.to_socket.name == "Base Color" else "normal" if link.to_node.type == 'NORMAL_MAP' else "mr" if link.to_node.type == 'SEPARATE_COLOR' or link.to_socket.name in ("Roughness", "Metallic") else None
            if role:
                n.image.filepath_raw = os.path.join(texdir, f"{name}_{role}.png"); n.image.file_format = 'PNG'; n.image.save()
                print("texture", role, n.image.size[0])
bpy.ops.object.select_all(action='DESELECT'); m.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP')
print("exported", out)
