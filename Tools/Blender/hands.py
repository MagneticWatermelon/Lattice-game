# The soldier's hands closed into grips (the rig has no finger bones, so the
# mesh itself is posed): each finger bends at three joints toward the palm,
# the thumb swings across. Works in each hand bone's frame (cm): y along the
# hand, z across it (thumb at +z), x the back of the hand (right) or the palm (left).
import numpy as np
from mathutils import Matrix, Vector

# Per hand: knuckle line (y), finger joints after it (cm), bend angles (degrees),
# the thumb's pivot and swing.
GRIPS = {
    # Right: a pistol grip, fingers closed around it.
    "Right": dict(knuckle=11.2, joints=(0.0, 4.0, 6.7), bends=(62.0, 82.0, 42.0), blend=0.9,
                  thumb_pivot=(None, 5.0, 3.6), thumb_swing=-38.0, thumb_curl=28.0, thumb_z=3.9),
    # Left: under the handguard, fingers half closed around its side.
    "Left": dict(knuckle=10.4, joints=(0.0, 4.0, 6.6), bends=(48.0, 52.0, 26.0), blend=0.9,
                 thumb_pivot=(None, 4.6, 3.6), thumb_swing=30.0, thumb_curl=18.0, thumb_z=3.9),
}

def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def rot2(px, py, cx, cy, a):
    """Rotate points (px, py) about (cx, cy) by angles a (radians, arrays)."""
    c, s = np.cos(a), np.sin(a)
    dx, dy = px - cx, py - cy
    return cx + dx * c - dy * s, cy + dx * s + dy * c

def curl(body, arm, log=print):
    me = body.data
    gi = {g.index: g.name for g in body.vertex_groups}
    co = np.array([v.co[:] for v in me.vertices], dtype=np.float64)
    mw = np.array(body.matrix_world)
    world = co @ mw[:3, :3].T + mw[:3, 3]
    for side, g in GRIPS.items():
        bone = arm.data.bones[side + "Hand"]
        F = arm.matrix_world @ bone.matrix_local
        S = F.to_scale()[0]
        Fn = np.array(F.normalized())
        R = Fn[:3, :3]; T = Fn[:3, 3]
        # Weights on the hand bone: only vertices mostly on it move (fingers are all hand).
        w = np.zeros(len(co))
        hid = next(gg.index for gg in body.vertex_groups if gg.name == side + "Hand")
        for v in me.vertices:
            for e in v.groups:
                if e.group == hid:
                    w[v.index] = e.weight
        idx = np.nonzero(w > 0.05)[0]
        local = (world[idx] - T) @ R * 100.0                       # world -> bone frame, cm
        x, y, z = local[:, 0], local[:, 1], local[:, 2]
        sign = -1.0 if side == "Right" else 1.0                   # toward the palm: right -x, left +x
        # The thumb: beyond the index finger's side (+z), short of the finger tips.
        # A smooth mask, so no face is torn between a finger and the thumb.
        tz0 = g["thumb_z"]
        thumb = smooth(tz0 - 0.2, tz0 + 0.6, z) * (1.0 - smooth(12.6, 13.6, y)) * smooth(2.0, 4.0, y)
        finger = 1.0 - smooth(tz0 - 0.2, tz0 + 0.6, z) * (1.0 - smooth(12.6, 13.6, y))
        k = g["knuckle"]
        near = (y > k - 1.5) & (y < k + 1.5) & (z < tz0 - 0.5)
        xc = 0.5 * (x[near].min() + x[near].max())
        log(f"{side}: hand verts {len(idx)}, knuckle centre x {xc:.2f} cm")
        nx, ny = x.copy(), y.copy()
        # Joints from the tip back to the knuckle: each turns everything beyond it.
        for j in range(2, -1, -1):
            jy = k + g["joints"][j]
            a = np.radians(g["bends"][j]) * smooth(jy - g["blend"], jy + g["blend"], y) * finger
            # About z: +angle turns +y toward -x. Right hand closes with +, left with -.
            nx, ny = rot2(nx, ny, xc, jy, -sign * a)
        # Thumb: swings across the palm about the hand's axis.
        pz = g["thumb_pivot"][2]
        px = xc
        sw = np.radians(g["thumb_swing"]) * thumb
        c, s_ = np.cos(sw), np.sin(sw)
        dx, dz = nx - px, z - pz
        nx = px + dx * c + dz * s_
        nz = pz - dx * s_ + dz * c
        out = np.stack([nx, ny, nz], axis=1) / 100.0
        new_world = out @ R.T + T
        w_move = np.clip(w[idx] * 1.5, 0, 1)[:, None]     # partial weights (wrist) move partly
        world[idx] = world[idx] * (1 - w_move) + new_world * w_move
    inv = np.linalg.inv(mw)
    newco = world @ inv[:3, :3].T + inv[:3, 3]
    for i, v in enumerate(me.vertices):
        v.co = newco[i]
    me.update()
