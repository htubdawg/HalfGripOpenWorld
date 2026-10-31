# HalfGripOpenWorld

A Half Grip (Steam app 4163270, Unity, Windows) mod: Open World, free roam in a 32 km square of country generated from a fixed seed the
way real country comes about (rivers, settlements, a road hierarchy, land use), built in the Tokyo scene, streamed in tiles, with a
floating origin. Current release: see `OpenWorld/mod.json`.

## How the mod runs

- It is a **source mod** for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) (BepInEx 5). At game start the loader compiles
  `OpenWorld/src/*.cs` with Roslyn (**C# 10**, netstandard2.1) against the game's `Assembly-CSharp.dll`, the Unity DLLs, BepInEx,
  0Harmony and HalfGripTools. What ships is the `OpenWorld/` folder, installed to `<game>\BepInEx\HalfGripMods\OpenWorld\`.
- The entry point is `Mod : HalfGripMod` in `src/Mod.cs`. `ctx.PatchAll()` applies the `[HarmonyPatch]` classes in `src/Patches.cs`.
  The Multiplayer mod is patched by hand in `src/MultiplayerLink.cs` at the first scene load, by name, because its classes are internal.
- The loader API is in HalfGripTools' `docs/WRITING_MODS.md`; what is known about the game is in its `docs/GAME_NOTES.md`. The Highway mod
  in [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods) is the model for building a map inside the Stadium scene.
- Game DLLs and scene dumps are in the private `htubdawg/HalfGripRefs` repo (clone it next to this one; `tools/meshcheck` looks for
  `../halfgriprefs/game` by default). Decompile with
  `ilspycmd -p -o <dir> <HalfGripRefs>/game/HalfGrip_Data/Managed/Assembly-CSharp.dll` (set `DOTNET_ROLL_FORWARD=Major` on a newer .NET).

## Layout of OpenWorld/src/

| File | Role |
|---|---|
| `Mod.cs` | Config, scene-load hook |
| `MapEntry.cs` | Adds the OpenWorld map to `RunConfig.maps` as a copy of TokyoNeon with its own track index (30) |
| `Patches.cs` | Harmony: track index, TestMode and weather draw, kept out of run map draws, no laps, no solo AI, reset in place, fell-under-ground |
| `WorldScene.cs` | Builds the world in the Tokyo scene: hides Tokyo's arena and city, grid on the trunk road, tunnel mouths, token track loop |
| `WorldGen.cs`, `Noise.cs`, `Rng.cs`, `Heap.cs` | The land's height function (layers) and the deterministic randomness and ordering under it |
| `Rivers.cs` | Drainage (priority flood), rivers and lakes, and their cut into the land |
| `Places.cs` | Settlement scores and placement in tiers; softening the land under places; farmland reach |
| `Network.cs` | The road hierarchy: routing, sharing, junctions, profiles, bridges, grown streets and tracks, the ground's road beds |
| `LandUse.cs` | What the ground is at a point (fields, woods, meadow, town...), as a colour |
| `RoadMesh.cs`, `WaterMesh.cs` | Per-tile road, verge, sidewalk, track and footpath, and water meshes and their pixel-art textures |
| `Destinations.cs` | The key places the debug teleport goes round |
| `Buildings.cs` | Per-tile houses, high-street shops, the main town's towers, village churches, farm buildings, and the lots' buildings and surfaces (one mesh, one collider), their materials |
| `Town.cs` | Lots: the retail park and businesses on the main roads, laid out with the roads; the levelled ground under them |
| `Parks.cs` | Parks (town blocks and a riverside, ponds dug in), their paths, trails round the lakes, benches; laid out after the road index |
| `Circuit.cs`, `CircuitMesh.cs`, `Rally.cs`, `HillClimb.cs` | The race courses: the circuit, the rally stage, the hill climb (sited and laid out in `Places.Build`; the stage's and the climb's roads made first in `Network.Build`) |
| `Foliage.cs` | The Forest's pines (captured from the main menu), grass tufts, per-tile scatter, instanced drawing |
| `Names.cs` | Every name the world shows (towns, villages, courses): change them here and the signs follow |
| `Signs.cs`, `Lettering.cs` | Road signs: the junction graph and shortest ways, name signs and fingerposts, the lettering atlas; the pixel font |
| `GarageSites.cs` | Where the garages are (filling stations, workshops, the circuit's pits, the rally service area, the hill climb paddock), read from the layout |
| `Garage.cs`, `GarageUi.cs`, `GarageRefit.cs` | The garages in a race: circles, opening on stop in neutral, god mode; the window; the live refit (gun copies) |
| `GameMode.cs` | The Open World's game mode: a wreck respawns the car at the spawn, no AI (solo; in a room, the room's race set so); later the events |
| `Weather.cs` | Draws a weather from every map's for each race (room seed in Multiplayer) |
| `Streamer.cs` | Tile streaming, the per-frame origin check, debug teleport and overlay; each tile's map picture |
| `MapView.cs` | **Public API** for other mods (MiniMap reads it by reflection): loaded tiles' map pictures and the origin. See README |
| `Origin.cs` | The floating origin: moves the scene, particles and trails |
| `GameCaches.cs` | World positions the game keeps in fields, moved on each shift |
| `MultiplayerLink.cs` | Positions over the network in world coordinates; stand-in cars moved on each shift |

## Rules that matter

- **Determinism.** Every player in a room must get the identical ground and roads. `WorldGen`, `Rivers`, `Places` and `Network` use
  only `Rng`, integer hashing, `+ - * /`, `Math.Floor`, `Math.Sqrt`, `Math.Abs`, `DetMath.Sin`/`Cos` and `Network.Atan2`, and break
  every tie (heaps, sorts) by index. Never `System.Random`, `UnityEngine.Random`,
  `Math.Sin`/`Cos`/`Exp`/`Pow`, or a config value. A change to it changes the world, so everyone in a room needs the same version (the Multiplayer mod enforces that).
- **Origin.** Anything that keeps a world position outside a transform must be moved on `Origin.Shifted` (or listed in `GameCaches`).
  Positions sent over the network are world coordinates. Only x and z shift; heights never change.
- Bump `version` in `mod.json` with each release and keep `README.md` in step.
- C# 10 and netstandard2.1 APIs only.
- Units: game units, about 3.3 to a real metre (a car is about 14 long, a lane 11). Size things at real scale through that.
- Heights: `WorldGen.Height` is a scene height (includes `Network.Lift`); road `Y`, river `W` and place `Y` are from sea level (add
  `Network.Lift` for the scene). Mixing the two sank every building once.
- Match the style: tabs, `///` summaries that explain *why*, `Mod.Info(...)` for verbose logging, `Mod.Log.LogWarning` and a try/catch
  around risky scene work.
- Do not invent an `ObjectiveKind` (a saved run with one is deleted).
- `HalfGripOpenWorld.MapView` is a public API that other mods (MiniMap) read by reflection: never rename it or change its members'
  names or signatures. It is left out of `tools/meshcheck` (it needs `Streamer`).

## Checking changes

- `tools/check.sh <game folder> <HalfGripTools.dll>` compiles the mod (see README). Run it before every push.
- `dotnet run -c Release --project tools/preview -- out.ppm [pixels] [x z span]` checks the generator (layout, road density at real
  scale, junctions, grades, ground meets roads, repeatability, tile time) and draws a map; look at it after any generator change.
- `dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>]` runs the world against Unity's types: road surfaces under
  the terrain (clip), roads poking through the roads drawn over them (poke), roads floating (float), grades, bridges clear of the
  water, river water meeting its banks (water), verges meeting the ground, farm track beds, building bases (bases), mesh faces
  (faces), lots, track and path ribbons over the ground (ribbons), sidewalk kerbs, roads under drawn water (flooded), plants in
  the water (plants), ponds, lawns and benches (parks); exits 1 on a failure. It reads the drawn
  surface from `Network.Point` and `Network.Spans`, as `RoadMesh` does. Add a check for every bug fixed.
- In game: see the test checklist in `docs/TESTING.md`. The owner tests in game and reports with screenshots and `LogOutput.log`.

## What is next

`docs/PLAN.md` holds the agreed plan (fixes, then town fabric, then race circuits). Work from it and keep it current.

## Working with the owner

- The owner merges pull requests; work on a branch and open a PR.
- If you lack access to something (a repo, a host, a tool, game files), ask the owner for it. Do not try workarounds.
