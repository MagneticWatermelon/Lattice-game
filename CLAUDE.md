# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

The Unity client for **Lattice**, a PlanetSide-style MMOFPS: thousands of players on one 8,192 m continent, three factions, a lattice of capturable bases. The netcode, server, bots and a reference Bevy client live in a separate Rust repo, **lattice-net**, in WSL (`\\wsl$\Ubuntu\home\oguzhan\code\lattice-net`, i.e. `~/code/lattice-net` inside WSL).

- **The design source of truth is `docs/gdd.md` in lattice-net** (Part II is this client; Part III the milestones). It tags every claim *exists / decided / proposed / open*.
- lattice-net's own `CLAUDE.md` records its decisions (tiers, rewind caps, determinism rules). Read it before touching anything that crosses the plugin boundary.
- **State today:** U0 is built (lattice-net's `client-ffi`, rebuilt into this project by its `scripts/unity-plugin.sh`); U1 and U2 are in progress. The client lives in `Assets/Lattice/` (one asmdef per layer, below), its scene is `Assets/Scenes/Lattice.unity`, and generated assets (fonts, terrain layers, materials, the animator, the scene itself) come from `Lattice > Setup` (`Assets/Lattice/Editor/LatticeSetup*.cs`), never hand-edited YAML. Run it headless with `& $unity run . -- -executeMethod Lattice.EditorTools.LatticeSetup.All`.

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
- **Frame order** (a `NetDriver` inserted into the PlayerLoop): EarlyUpdate `lt_begin_frame`, `lt_poll` → Update: read input into the intent, `lt_fire` (before inputs, with the view before recoil), `lt_inputs` → LateUpdate: `lt_own` (camera), `lt_render` (the crowd), drain events and tracers → PostLateUpdate `lt_flush`. `lt_render` runs exactly once per frame and is the only source of entity positions. From U3, poll/inputs/flush move to a plugin-owned net thread (Unity stalls can exceed the 5 s connection timeout). The API stays the same.
- **Interop:** blittable structs, caller-owned buffers (`NativeArray<T>`), no managed allocations per frame on either side.
- **Planned C# layers (namespaces/asmdefs):** `Lattice.Native` (P/Invoke, generated constants, a loader that reloads a rebuilt DLL in the editor), `.Net`, `.World`, `.Players`, `.LocalPlayer`, `.Combat`, `.Audio`, `.UI`, `.Services`, `.Diagnostics`.

### Coordinates (GDD §12.5)

Game space is right-handed, z up (x east, y north); Unity is left-handed, y up. The map is centered on Unity's origin.

- position `(x, y, z)` → Unity `(x − 4096, z, y − 4096)`
- yaw θ (radians, counterclockwise from east) → `Quaternion.Euler(0, 90° − θ, 0)`; view (θ, pitch φ up) → `Quaternion.Euler(−φ, 90° − θ, 0)`
- The canonical view is the game's (θ, φ). Derive Unity rotations from it, never the reverse. Mouse right decreases θ, mouse up increases φ.
- The movement stick is computed in Rust, not C#. `Coords.cs` gets EditMode tests mirroring lattice-net's `client/src/coords.rs`.

### Rendering (GDD §14)

URP (Forward+, GPU Resident Drawer), Input System, Burst/Jobs, UI Toolkit. The crowd is drawn in bands by screen size, not distance: rich (≤ 96 skinned), crowd (≤ ~1,250 GPU-instanced with baked vertex animation), far (impostors). Entity views are pooled, never instantiated mid-fight. **What you see is what blocks you:** drawn terrain and cover must match the game's collision. Budgets (§19): the plugin's calls ≤ 0.5 ms and view updates ≤ 1 ms of main thread per frame; 144 fps at 1080p in a 300-player fight on an RTX 3060-class PC.

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
  - Builds: `& $unity build`. Other batch jobs: `& $unity run . -- -executeMethod Class.Method` (never pass `-batchmode` or `-quit`).
  - Anything interactive: start a headless editor that stays up (no `-quit`), then drive it as above: `Start-Process $editor "-batchmode -projectPath $PWD -logFile $PWD\Logs\agent-editor.log"`. `unity status` doesn't list it, and `unity list` answers before the project has loaded (~70 s here), so main-thread commands time out until then: retry `eval` until it answers. When done, save, then stop it with `& $unity close $PWD`. (`unity command quit` fails outside Play mode.)
  - All of these start their own editor, so they fail while the project is open.
- **Never close an editor you didn't start:** `unity close` exits without saving.
- **Compile errors put the editor in Safe Mode**: the Pipeline package doesn't load, so `unity command` can't connect. `& $unity pipeline list` confirms it; fix the C#, then restart the editor.
- **Entering Play mode isn't proof the game runs**: an unfocused editor can freeze at frame 1. Follow `& $unity skill show --path references/playmode-verification-loop.md`.

`*.csproj` and `*.sln` are generated by Unity and gitignored.

## Meshes and textures: Blender

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
./target/release/lattice-server --spawn blob &
./target/release/lattice-bots --count 1000 --duration 600 &
```

## Conventions

- The **Bevy client** (`lattice-net/client/`) is the reference implementation until Unity passes U2. When behavior is unclear (tracers, interpolation, HUD, coordinate math), match what it does.
- **Milestone pass bars are measured, not eyeballed:** e.g. U0 means 0 corrections over 60 s against a local server with no per-frame managed allocations; U2 means `rewinds_trimmed` and `renders_held` are 0 in the server's session log. See GDD §20.
- **Self-checks** (planned, carried over from the Bevy client) let Claude Code run and judge a Windows build: `--autoplay`, `--autofire`, `--screenshot PATH --after S`, `--exit-after S`, and a summary appended to `lattice-report.txt` on exit.
- Faction colors: amber (Ordinate), jade (Free Compact), magenta (Resonance), plus an IFF mode. Don't reuse the Bevy client's red/blue/purple placeholders.
- Large source art (e.g. Meshy GLBs, big `.blend` files) stays out of git until there's an LFS remote.
- Any change that needs protocol or server work happens in lattice-net (GDD §21 lists what each milestone needs).
