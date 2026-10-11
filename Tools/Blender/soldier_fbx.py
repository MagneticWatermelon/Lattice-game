# Blender: the soldier for Unity. One armature with every clip as an action,
# four LODs of the mesh (all skinned to it), hands closed into grips; and the
# first-person arms: the same arms cut from the body, skinned only to the arm
# bones (so each arm can be placed on its own).
# Args after --: src_dir soldier.fbx arms.fbx
import bpy, sys, os, bmesh
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here)
import hands
src, out, arms_out = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 24

def imp(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

new = imp(os.path.join(src, "soldier_rigged.glb"))
arm = next(o for o in new if o.type == 'ARMATURE')
body = next(o for o in new if o.type == 'MESH' and o.name.startswith("char"))
for o in new:
    if o.type == 'MESH' and o is not body:
        bpy.data.objects.remove(o, do_unlink=True)
rest = arm.animation_data.action if arm.animation_data else None
arm.name = "Soldier"
arm.data.name = "Soldier"
hands.curl(body, arm)

clips = [("Idle", "anim_idle.glb"), ("Walk", "anim_walk.glb"), ("Run", "anim_run.glb"), ("RunAim", "anim_run_aim.glb"),
         ("Sprint", "anim_sprint.glb"), ("Jump", "anim_jump.glb"), ("Death", "anim_death.glb")]
for name, f in clips:
    objs = imp(os.path.join(src, f))
    a2 = next(o for o in objs if o.type == 'ARMATURE')
    act = a2.animation_data.action
    act.name = name
    act.use_fake_user = True
    # Kept with a fake user: the exporter writes every action that fits the armature as a take.
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    print("clip", name, tuple(act.frame_range))
if rest is not None:
    bpy.data.actions.remove(rest)
if arm.animation_data:
    arm.animation_data.action = None

# The first-person arms: a copy of the body with everything but the arms (from
# the collarbones out) cut away.
arms = body.copy()
arms.data = body.data.copy()
arms.name = "Arms"
bpy.context.collection.objects.link(arms)
keep_groups = {}
for side in ("Left", "Right"):
    for b in ("Shoulder", "Arm", "ForeArm", "Hand"):
        keep_groups[arms.vertex_groups[side + b].index] = side
bm = bmesh.new()
bm.from_mesh(arms.data)
deform = bm.verts.layers.deform.active
drop = []
for v in bm.verts:
    d = v[deform]
    w = sum(wt for g, wt in d.items() if g in keep_groups)
    if w < 0.5:
        drop.append(v)
bmesh.ops.delete(bm, geom=drop, context='VERTS')
# Only the arm bones move what's left: each vertex's weights renormalized over them.
for v in bm.verts:
    d = v[deform]
    items = [(g, wt) for g, wt in d.items() if g in keep_groups]
    total = sum(wt for _, wt in items) or 1.0
    for g in list(d.keys()):
        del d[g]
    for g, wt in items:
        d[g] = wt / total
bm.to_mesh(arms.data)
bm.free()
print("arms", len(arms.data.vertices), "verts", sum(len(p.vertices) - 2 for p in arms.data.polygons), "tris")


# LODs: the same skin, fewer triangles.
body.name = "Soldier_LOD0"
for i, ratio in enumerate([0.25, 0.07, 0.022], start=1):
    lod = body.copy()
    lod.data = body.data.copy()
    lod.name = f"Soldier_LOD{i}"
    bpy.context.collection.objects.link(lod)
    for m in list(lod.modifiers):
        if m.type != 'ARMATURE':
            lod.modifiers.remove(m)
    dec = lod.modifiers.new("Decimate", 'DECIMATE')
    dec.ratio = ratio
    dec.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = lod
    # The decimate must come before the armature in the stack to be applied.
    bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    print("lod", i, sum(len(p.vertices) - 2 for p in lod.data.polygons), "tris")
print("lod 0", sum(len(p.vertices) - 2 for p in body.data.polygons), "tris")

common = dict(apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
              object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
              add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=True,
              armature_nodetype='NULL', path_mode='STRIP', embed_textures=False, use_selection=True)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type == 'ARMATURE' or (o.type == 'MESH' and o.name.startswith("Soldier_LOD")):
        o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0, **common)
print("exported", out)
# The model's arms are short (stylized: big shoulders, ~35 cm shoulder to wrist).
# In first person the hands must reach a rifle held out in front, so these arms
# are longer: the upper arm (never on screen) by half, the forearm by a fifth;
# the hands keep their size. Mesh and bones move together, along each arm.
UPPER, FORE = 1.5, 1.2
from mathutils import Vector
mw = arms.matrix_world
inv = mw.inverted()
aw = arm.matrix_world
for side in ("Left", "Right"):
    b = arm.data.bones
    sh = aw @ b[side + "Arm"].head_local
    el = aw @ b[side + "ForeArm"].head_local
    wr = aw @ b[side + "Hand"].head_local
    d = (wr - sh).normalized()
    se, sw = (el - sh).dot(d), (wr - sh).dot(d)
    def stretch(s):
        if s <= 0: return s
        if s <= se: return s * UPPER
        if s <= sw: return se * UPPER + (s - se) * FORE
        return se * UPPER + (sw - se) * FORE + (s - sw)
    gidx = {arms.vertex_groups[side + n].index for n in ("Arm", "ForeArm", "Hand")}
    for v in arms.data.vertices:
        w = sum(g.weight for g in v.groups if g.group in gidx)
        if w <= 0: continue
        p = mw @ v.co
        s0 = (p - sh).dot(d)
        v.co = inv @ (p + d * (stretch(s0) - s0) * min(1.0, w))
    side_stretch = (stretch(se) - se, stretch(sw) - sw)
    print(side, "arm stretched: elbow +%.3f, wrist +%.3f m" % side_stretch)
    globals()[side + "_shift"] = (sh, d, se, sw, stretch)
# The bones: the armature's rest pose for the arms export only (the soldier is already written).
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
ainv = aw.inverted()
for side in ("Left", "Right"):
    sh, d, se, sw, stretch = globals()[side + "_shift"]
    for n in ("ForeArm", "Hand"):
        eb = arm.data.edit_bones[side + n]
        for attr in ("head", "tail"):
            p = aw @ getattr(eb, attr)
            s0 = (p - sh).dot(d)
            setattr(eb, attr, ainv @ (p + d * (stretch(s0) - s0)))
    # The upper arm's tail follows the elbow.
    eb = arm.data.edit_bones[side + "Arm"]
    p = aw @ eb.tail
    s0 = (p - sh).dot(d)
    eb.tail = ainv @ (p + d * (stretch(s0) - s0))
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); arms.select_set(True)
bpy.ops.export_scene.fbx(filepath=arms_out, bake_anim=False, **common)
print("exported", arms_out)
