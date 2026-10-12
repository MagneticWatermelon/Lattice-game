# Asset pipeline

How the client's generated art and sound are made. Every committed model,
texture edit and sound effect in `Assets/Lattice/` that didn't come straight
from a package or Poly Haven came out of one of these scripts, so they can be
remade or changed. Unity only imports the results (CLAUDE.md: mesh and texture
work happens in Blender).

Run Blender scripts headless, arguments after `--`:

```powershell
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
& $blender --background --factory-startup --python Tools/Blender/<script>.py -- <args>
```

The numpy-only scripts run on Blender's bundled Python (`...\Blender 5.1\5.1\python\bin\python.exe -I`).

## Sources (not in this repo)

| what | where |
|---|---|
| the soldier, rigged, and its clips (Meshy) | `lattice-net/client/assets/meshy_output/20261006_004827_soldier-user_c44b834b/` (`soldier_rigged.glb`, `anim_*.glb`; gitignored there) |
| the rifle (third person, first person) | `lattice-net/client/assets/models/rifle.glb`, `rifle_view.glb` |
| the cover props and the rocks | `lattice-net/client/assets/models/{wall,crate,container,sandbags,post,command,bunker,rocks}.glb` (unit boxes, base at 0, front +z) |
| terrain materials | Poly Haven (CC0): `forrest_ground_01`, `brown_mud_dry`, `ground_grey`, ... (ARM, diffuse, GL normal, displacement at 2k) |

## Models

| script | makes | args |
|---|---|---|
| `Blender/soldier_fbx.py` (with `hands.py`) | `Players/Soldier/Soldier.fbx`: one armature, every clip as a take, four LODs; the hands closed into grips in the mesh (the rig has no finger bones). `Combat/Arms/SoldierArms.fbx`: the first-person arms, cut from the collarbones out, skinned to the arm bones only, upper arm and forearm lengthened (they must reach a rifle held out in front) | `src_dir soldier.fbx arms.fbx` |
| `Blender/rifle_fbx.py` | `Combat/Rifle/Rifle.fbx`, `RifleView.fbx`: the GLB as one mesh, its folding iron sights folded down. With the last three args, also `RifleLow.fbx`: welded and decimated to 600 triangles, the crowd's rifle | `in.glb out.fbx Name [low.fbx LowName triangles]` |
| `Blender/cell_fbx.py` | `Combat/Rifle/Cell.fbx`: the rifle's ammo cell, which the reload takes out and puts back. A canister across the receiver's round bay (measured from the rifle: its dark disc at x 0.1887, z -0.0716, r 0.032, on a dome to y 0.0945), centered on its own middle, its axis along the rifle's left-right; looks the same every 60 degrees (the empty reload's twist hands it back unturned); body, trim and glow materials; renders previews seated and out | `rifle_view.glb out.fbx preview_dir` |
| `Blender/holo_fbx.py` | `Combat/Rifle/HoloSight.fbx`: the holographic sight, in the rifle mesh's own frame (body, matte hood inside, glass); renders previews | `rifle_view.glb out.fbx preview_dir` |
| `Blender/prop_fbx.py` | `World/Cover/*.fbx`: a static GLB as one FBX mesh, in Unity's axes (the client draws the bare mesh, not the FBX's transform), with its textures beside it (then `mr_to_urp.py` for `_metal.png`) | `in.glb out.fbx Name` |

## Textures

| script | does | args |
|---|---|---|
| `Blender/mr_to_urp.py` | glTF metallic-roughness to URP's metallic map (R metallic, A smoothness) | `in.png out.png [smoothness scale]` |
| `Blender/rifle_metal.py` | the rifles' metal maps from their albedo: teal panels as satin paint, near-black as anodized metal, the rest bare metal (the source marked everything fully metallic, so the teal mirrored the sky) | `albedo metal_in metal_out` |
| `Blender/pack_mask.py` | a URP terrain mask map from Poly Haven ARM + displacement (R metallic, G AO, B height, A smoothness) | `arm disp out.png` |
| `Blender/flatten_albedo.py` | a tiling albedo evened out at large scale (no stripes across a terrain) | see its header |
| `Blender/clouds.py` | `World/Sky/Clouds.png`: the sky's cloud noise, tileable (R Perlin-Worley shapes, G cellular detail, B fine wisps), and a preview of the shapes cut to the sky's cover | `out.png [preview.png]` |
| `Blender/contact_sheet.py` | renders and screenshots into one sheet, for checking by eye | `out.png cols size img...` |

## Sound

`Audio/sfx.py OUT_DIR [PREVIEW_DIR]` synthesizes every sound effect in
`Audio/Clips` (numpy only: nothing recorded, nothing to license): the rifle
(own, others' near and far), supersonic cracks, impacts, hit, head and kill
confirms, hurts, death, respawn, sights up/down, footsteps, the reload's
handling (the cell released, pulled out, the pouch, the fresh one slid in,
seated, charged from empty), the dry trigger, a spent cell's clink,
crouching down and up, wind and the distant battle beds, with an open-field reverb whose tail is set in dB below
the direct sound. `SFX_ONLY=Shot_Own,Crack` remakes some. `PREVIEW_DIR` gets a
waveform/spectrogram sheet; `Audio/sfx_stats.py DIR [names]` prints each
clip's envelope (dB below peak at 5-800 ms), spectral centroid and low-end
share: what to check when tuning (a close shot should be ~14 dB down by 50 ms,
the field's echo quieter than the shot).

After remaking sounds, copy the WAVs into `Assets/Lattice/Audio/Clips/` and run
`Lattice > Setup > Audio` (import rules: one-shots decoded in memory, beds
compressed).
