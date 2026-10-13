# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

The Unity client for **Lattice**, a PlanetSide-style MMOFPS: thousands of players on one 8,192 m continent, three factions, a lattice of capturable bases. The netcode, server, bots and a reference Bevy client live in a separate Rust repo, **lattice-net**, in WSL (`\\wsl$\Ubuntu\home\oguzhan\code\lattice-net`, i.e. `~/code/lattice-net` inside WSL).

- **The design source of truth is `docs/gdd.md` in lattice-net** (Part II is this client; Part III the milestones). It tags every claim *exists / decided / proposed / open*.
- lattice-net's own `CLAUDE.md` records its decisions (tiers, rewind caps, determinism rules). Read it before touching anything that crosses the plugin boundary.
- **State today:** U0 is built (lattice-net's `client-ffi`, rebuilt into this project by its `scripts/unity-plugin.sh`); U1 and U2 are built and pass their measured bars except where noted (the commit log has the numbers): the world (terrain, grass, the Bevy client's cover props, rocks, sky and cloud shadows), soldiers by band with friend-or-foe colors, the fight (first-person rifle and arms, holographic sight, tracers, flashes, impacts, the combat HUD, death view, battle audio), a join card. U1's 1k-blob frame p99 is 4.6 ms (this display's 180 Hz allows 5.56; IL2CPP player, the cover and rocks through one BatchRendererGroup, the first-person view a pass in the world camera). Open: U2's "you play it and it feels right" is the user's call. U3 is built: the session runs on the plugin's net thread by default (`--pump` for U1-U2's pump mode), and frozen frames (100 ms to 10 s) cost no inputs, resyncs or shots. U6's first part is built: crouch (Ctrl held or C toggled; the view eases down to the crouched eyes; soldiers kneel or crouch-walk by leg IK, the crowd from two more baked poses) and the rifle's rounds (its ammo cell, whose glow is the rounds left, a keyframed first-person reload that swaps it, timed by the plugin's predicted reload (`Combat/Reload.cs`), the HUD's rounds, handling sounds, near soldiers' reloads heard). **Kiln**, the first real map (lattice-net's `docs/kiln.md`), is at K0, its blockout: the plugin generates it (world id `LT_WORLD_KILN`, the default world, `--world kiln|kit|harrow|N|none`, built on a thread at launch), and the client draws its terrain from the plugin's per-sample materials (`lt_world_materials`, `TerrainBuilder`'s Kiln path), its sites' boxes in blockout materials, and its lanes and control points as veins of light (`World/LatticeVeins.cs`); its assets come from `LatticeSetupWorld.Kiln()` (`Assets/Lattice/World/Kiln/`). Kiln has no grass or rocks yet. K1 (the collision kit, plugin API v5) is drawn too: turned boxes along their axis and wedges as a box plus a prism (`CoverBuilder`), shields as slabs of light in their faction's color and lifts and drop shafts as columns of light (`World/Connectors.cs`), holes cut out of the terrain (`TerrainBuilder`, `TerrainData.SetHoles`); `--world kit` (with a server on `--world kit`) shows the kit's test level, one of each. K2 (the great sites in graybox: the Heart chamber, the Spans' galleries and wall tunnels, the Cliffholds' lift towers and deep gallery, the Geodes' wells and springs) needed no client changes: it is all boxes, holes and volumes. The client lives in `Assets/Lattice/` (one asmdef per layer, below), its scene is `Assets/Scenes/Lattice.unity`, and generated assets (fonts, terrain layers, materials, the animator, the scene itself) come from `Lattice > Setup` (`Assets/Lattice/Editor/LatticeSetup*.cs`), never hand-edited YAML. Run it headless with `& $unity run . -- -executeMethod Lattice.EditorTools.LatticeSetup.All`.

## Working autonomously

Work without the user's help: don't stop to ask questions or hand work back. Decide, build, verify, then report what you did and why.

- Build to the GDD's *proposed* items unless the user has rejected one. On an *open* question, take the GDD's recommendation (§22), otherwise the option that's easiest to undo, and name your choice in the report.
- Verify your own work instead of asking the user to: tests, the editor console, screenshots and renders you look at yourself, the client's self-checks against a local server and bots.
- When only the user can unblock something (a sign-in, a license, a purchase), finish everything around it, then say exactly what's needed.
- Still ask before spending money (lattice-net's cloud scripts), changing the host's network config (run lattice-net's `scripts/netstate.sh save` first), or doing anything irreversible or outward-facing: force-pushing, deleting history or work you didn't create, publishing.

## Architecture (planned, GDD §12–§13)

**The rule: if it affects the wire, prediction or a hit, it's Rust. If it's only seen or heard, it's C#.**

- The Rust client stack (`lattice-net`, `lattice-game`, `lattice-client-core`) is embedded as a native plugin, `lattice-client-ffi` (a cdylib, `lattice_client.dll`) behind a C ABI (`lt_*` functions). It's built in lattice-net and copied here, together with generated C# bindings (`scripts/unity-plugin.sh`, planned). Don't port netcode, prediction, the input encoding or weapon math to C#: prediction must match the Linux server bit for bit, and only the Rust code is checked for that.
- **Constants are generated, never retyped**: weapon, movement, hitbox and tier values come from `cargo run -p lattice-client-ffi --bin gen-cs`. The C# side checks `lt_version()` at load, which catches a plugin and bindings that don't match.
- **Not used:** Netcode for GameObjects or Entities, or Unity Physics/PhysX for anything gameplay-relevant (PhysX only for cosmetic ragdolls and debris). Unity's `Time` never reaches the netcode: the plugin's monotonic clock, read once per frame by `lt_begin_frame`, is the only "now".
- **Frame order** (a `NetDriver` inserted into the PlayerLoop): EarlyUpdate `lt_begin_frame`, `lt_poll` → Update: read input into the intent, `lt_fire` (before inputs, with the view before recoil), `lt_inputs` → LateUpdate: `lt_own` (camera), `lt_render` (the crowd), drain events and tracers → PostLateUpdate `lt_flush`. `lt_render` runs exactly once per frame and is the only source of entity positions. On the net thread (U3, the default: `LT_CONFIG_NET_THREAD`), the plugin's thread polls, makes inputs from the latest intent and flushes about every millisecond, so `lt_poll`/`lt_flush` do nothing and `lt_inputs` only sets the intent: the frame's calls stay the same.
- **Interop:** blittable structs, caller-owned buffers (`NativeArray<T>`), no managed allocations per frame on either side.
- **Planned C# layers (namespaces/asmdefs):** `Lattice.Native` (P/Invoke, generated constants, a loader that reloads a rebuilt DLL in the editor), `.Net`, `.World`, `.Players`, `.LocalPlayer`, `.Combat`, `.Audio`, `.UI`, `.Services`, `.Diagnostics`.

### Coordinates (GDD §12.5)

Game space is right-handed, z up (x east, y north); Unity is left-handed, y up. The map is centered on Unity's origin.

- position `(x, y, z)` → Unity `(x − 4096, z, y − 4096)`
- yaw θ (radians, counterclockwise from east) → `Quaternion.Euler(0, 90° − θ, 0)`; view (θ, pitch φ up) → `Quaternion.Euler(−φ, 90° − θ, 0)`
- The canonical view is the game's (θ, φ). Derive Unity rotations from it, never the reverse. Mouse right decreases θ, mouse up increases φ.
- The movement stick is computed in Rust, not C#. `Coords.cs` gets EditMode tests mirroring lattice-net's `client/src/coords.rs`.

### Rendering (GDD §14)

URP (Forward+, GPU Resident Drawer), Input System, Burst/Jobs, UI Toolkit. The crowd is drawn in bands by screen size, not distance: rich (≤ 96 skinned), crowd (≤ ~1,250 GPU-instanced with baked vertex animation), far (impostors). Entity views are pooled, never instantiated mid-fight. Static props (the cover, the rocks: ~45,000 pieces) have no GameObjects: `World/StaticField` draws them through one BatchRendererGroup, levels of detail picked by a Burst job as the camera moves (as GameObjects with LOD groups they cost ~2 ms of main thread at p99). The first-person rifle and arms are a render pass in the world camera (`Combat/ViewModelFeature`), not an overlay camera. **What you see is what blocks you:** drawn terrain and cover must match the game's collision. Budgets (§19): the plugin's calls ≤ 0.5 ms and view updates ≤ 1 ms of main thread per frame; 144 fps at 1080p in a 300-player fight on an RTX 3060-class PC.

## Unity: through the Unity CLI

Interact with Unity only through the **Unity CLI**: not the Unity MCP server (deprecated), and not by hand-editing `.unity`, `.prefab` or `.asset` YAML. The CLI ships with Unity Hub and isn't on PATH. The project's editor is **Unity 6000.6.5f1**.

```powershell
$unity  = "C:\Program Files\Unity Hub\resources\unity.exe"
$editor = "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
& $unity skill show    # the CLI's own guide for agents: read it before first use
```

Pass `--json` when you parse output, and branch on `success` and the exit code.

- **Editor open** (`Temp/UnityLockfile` exists; `& $unity status` lists the project as `ready`): drive it through the project's `com.unity.pipeline` package. `& $unity command` lists every command. The main ones:
  - `& $unity command run_tests -- --mode editor --filter CoordsTests` (`--mode all|editor|playmode`)
  - `& $unity command console -- --tail 50 --level error`; `& $unity recompile` checks that the scripts compile
  - `editor_play`, `editor_stop`, `capture_game_view -- --save_path Logs/shot.png`, `eval -- --code '<C#>'`
  - scene and asset edits through its commands, then `save_scene` or `save_all`
- **Editor closed:**
  - Tests: `& $unity test . --mode EditMode --output Logs/test-results.xml --timeout 900`. One test or fixture: `--filter Lattice.Tests.CoordsTests`. Exit 8 means tests failed; 6 means no verdict (compile error, crash, timeout).
  - Builds: `& $unity build`. The player is IL2CPP (a first build takes ~3 min, then ~1); a build folder made with the other backend must be deleted first. Other batch jobs: `& $unity run . -- -executeMethod Class.Method` (never pass `-batchmode` or `-quit`).
  - Anything interactive: start a headless editor that stays up (no `-quit`), then drive it as above: `Start-Process $editor "-batchmode -projectPath $PWD -logFile $PWD\Logs\agent-editor.log"`. `unity status` doesn't list it, and `unity list` answers before the project has loaded (~70 s here), so main-thread commands time out until then: retry `eval` until it answers. When done, save, then stop it with `& $unity close $PWD`. (`unity command quit` fails outside Play mode.)
  - All of these start their own editor, so they fail while the project is open.
- **Never close an editor you didn't start:** `unity close` exits without saving.
- **Compile errors put the editor in Safe Mode**: the Pipeline package doesn't load, so `unity command` can't connect. `& $unity pipeline list` confirms it; fix the C#, then restart the editor.
- **Entering Play mode isn't proof the game runs**: an unfocused editor can freeze at frame 1. Follow `& $unity skill show --path references/playmode-verification-loop.md`.

`*.csproj` and `*.sln` are generated by Unity and gitignored.

## Meshes and textures: Blender

The scripts that made the committed art and sound, and where their sources live, are in `Tools/` (see `Tools/README.md`): change an asset by changing its script and rerunning it.

Do all mesh and texture work in **Blender 5.1**: modeling, cleanup, decimation and LODs, UVs, baking, texture painting and editing, rigging fixes and export, Meshy output included. Unity only imports the results and builds materials from them.

```powershell
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
& $blender --background <file.blend> --python <script.py>    # headless; or --python-expr "<code>"
```

- With Blender open, use the Blender MCP tools (`mcp__blender__*`): the add-on's bridge starts with Blender on port 9876, and needs Blender's "Allow Online Access" preference on. Without Blender open, use their `*_for_cli` variants, which run it in the background, or run it headless as above.
- Don't overwrite a `.blend` you didn't create: save your edits to a new file.
- Check results by rendering a PNG and looking at it yourself. The MCP render tools ignore the path you pass and return the one they wrote (`filepath`): read that.
- Export FBX into `Assets/` at 1 unit = 1 m, facing Unity +Z (game north). GLB import (glTFast) is planned but not installed. Keep `.blend` sources outside `Assets/`: Unity imports a `.blend` only by running Blender in the background.
- Cover props must match their collision boxes exactly (see Rendering). Faction variants come from one material with a team mask (GDD §14.4), not a texture set per faction.

## Local server and bots

In WSL; the Windows side reaches them at the WSL IP from `hostname -I`, port 40000:

```sh
cd ~/code/lattice-net
cargo build --release -p lattice-sim
./target/release/lattice-server --world kiln --spawn blob &
./target/release/lattice-bots --world kiln --count 1000 --duration 600 &
```

The server picks the world (`--world kiln|harrow|N`; its default is Harrow). The client prebuilds its own `--world` (default Kiln) and builds whatever else the Welcome names; a plugin built from a different generator than the server's builds a different world, and the join card says so (`world_mismatch`: the Welcome's digest isn't ours). `--spawn lanes` with the bots' `--run-lanes` walks every lane.

## Conventions

- The **Bevy client** (`lattice-net/client/`) is the reference implementation until Unity passes U2. When behavior is unclear (tracers, interpolation, HUD, coordinate math), match what it does.
- **Milestone pass bars are measured, not eyeballed:** e.g. U0 means 0 corrections over 60 s against a local server with no per-frame managed allocations; U2 means `rewinds_trimmed` and `renders_held` are 0 in the server's session log. See GDD §20.
- **Self-checks** let Claude Code run and judge a Windows build (`Builds/Windows/Lattice.exe`, run from `Builds/run`): `--autoplay`, `--autofire`, `--autotarget` (aim at the nearest enemy, fire when on it; it tops up between fights), `--crouch` (play crouched), `--hud-test` (a scripted run of hit markers, hurts and kills fed to the HUD only), `--screenshot PATH --after S` (`--shots N --shot-every S` for a burst), `--exit-after S`, `--no-vsync` (measure frame cost uncapped: vsynced, the GPU downclocks and its times mean little), `--render-scale 2` (make the GPU the bottleneck so its times compare between runs) with `--off grass,ssao,shadows,clouds,bloom,cover,rocks,fpview` (what each costs), `--net-graph` (open the net graph, closed by default, from the start), `--profile PATH` (a development build records the profiler from its first frame into a .raw the editor loads), `--freeze AT:MS,...` (stall the main thread: U3's check; the server's session log shows stand-ins), `--pump` (no net thread), `--controls`, `--faction-colors`, `--tier-colors`, and a summary appended to `lattice-report.txt` on exit (frames, GPU and thread times, the longest frame and when, managed allocations from when the world is built, the rounds (ammo corrections, reloads, dry pulls), audio levels, the net session). `Reload.DebugAt` (set through `eval` in Play mode) holds the first-person reload at any moment, for stills; the server's `--immortal` gives unlimited rounds, so nobody reloads there. The headless editor can't show HUD canvases or play audio, and its hand-rendered shots miss what's queued with `Graphics.RenderMesh*` (the far crowd; `GrassField.Draw(cam)` queues the grass): check those in a build.
- Faction colors: amber (Ordinate), jade (Free Compact), magenta (Resonance), plus an IFF mode. Don't reuse the Bevy client's red/blue/purple placeholders.
- Large source art (e.g. Meshy GLBs, big `.blend` files) stays out of git until there's an LFS remote.
- Any change that needs protocol or server work happens in lattice-net (GDD §21 lists what each milestone needs).
