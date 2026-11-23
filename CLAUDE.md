# HalfGripOpenWorld

A Half Grip (Steam app 4163270, Unity, Windows) mod: Open World, free roam in a square of country 32,000 units (about 9.7 km) across generated from a fixed seed the
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
| `WorldScene.cs` | Builds the world in the Tokyo scene: hides Tokyo's arena and city, grid on the trunk road, tunnel mouths (the trunk's open, see `Wrap`), token track loop |
| `WorldAssets.cs` | Frees the materials and textures made for a race when the world goes (they are HideAndDontSave, so nothing else would) |
| `WorldGen.cs`, `Noise.cs`, `Rng.cs`, `Heap.cs` | The land's height function (layers) and the deterministic randomness and ordering under it |
| `Rivers.cs` | Drainage (priority flood), rivers and lakes, and their cut into the land |
| `Places.cs` | Settlement scores and placement in tiers; softening the land under places; farmland reach |
| `Network.cs` | The road hierarchy: routing, sharing, junctions, profiles, bridges, grown streets and tracks, the back roads joined up (links where the way by road is a long detour, through ways from dead ends), drives and their homes, the ground's road beds |
| `TownPlan.cs` | The towns' streets ring by ring (seafront, old roads, back lanes, ring roads, terrace patches, crescents and closes), streets meeting square, ground kept for the retail park and market place (part of `Network`) |
| `LandUse.cs` | What the ground is at a point (fields, woods, meadow, town...), as a colour |
| `RoadMesh.cs`, `WaterMesh.cs` | Per-tile road, verge, sidewalk, track, drive and footpath, and water meshes and their pixel-art textures; covered bridges on the lanes |
| `Destinations.cs` | The key places the debug teleport goes round |
| `Buildings.cs` | Per-tile houses, high-street shops, the main town's towers, village churches, farm buildings, roadside houses and mailboxes on the drives, and the lots' buildings and surfaces (one mesh, one collider), their materials; houses' walls drawn a storey at a time from wall atlases, roofs as thick slabs |
| `Town.cs` | Lots: the towns' market places, the retail park and businesses on the main roads, laid out with the roads; the levelled ground under them |
| `Parks.cs` | Parks (town blocks and a riverside, ponds dug in), their paths, trails round the lakes, benches; laid out after the road index |
| `Circuit.cs`, `CircuitMesh.cs`, `Rally.cs`, `HillClimb.cs` | The race courses: the circuit, the rally stage, the hill climb (sited and laid out in `Places.Build`; the stage's and the climb's roads made first in `Network.Build`) |
| `Herds.cs` | The deer: where they could live (`DeerSites`, fixed with the map: crossings on quiet roads where woods meet fields) and each race's groups from the race's seed (who is a buck, the rare fawn), their spots, legs tested for water and drops, and what each deer does as a pure function of the race clock (`Herds.At`). No Unity types |
| `Deer.cs` | The deer in the game: the skinned mesh in Unity's frame (`DeerMesh`; a buck's with the antlers as a second submesh and material, a fawn the doe's drawn small), and each deer near the car posed in code from `Herds.At`; hits by this machine's car and shots by its guns (tested in code, no colliders), the car's damage, dead bodies drawn |
| `Reactions.cs` | Part of `Herds`: the deer's reactions (alert, flee away, across or after freezing) played from a reaction alone, along the tested legs; the calm day going on from the refuge. No Unity types |
| `DeerRoom.cs` | The deer in a room: each group's reactions in order and the kills, claimed by the machine that saw them and settled by the room's host (first claim wins), Hello for late joiners; the deer's messages. No Unity types (`tools/deersim` drives it) |
| `DeerKills.cs` | The deer killed in a race: each body's flight, landing and slide (or fall when shot) on fixed steps, from the kill (its message read and written here, sent by `DeerRoom`). No Unity types |
| `DeerModel.cs` | **Generated** by `tools/deer/export_cs.py` from `tools/deer/deer.json` (frozen: the painted coat is on its UVs) and `tools/deer/antlers.json` (`antlers.py`): the deer's mesh, bones and poses, and the buck's antlers. Never edit by hand |
| `Plots.cs` | The house plots along the towns' and villages' roads and the footprint test (`Plots.Along`, `Plots.Fits`): what `Buildings` draws and `Traffic` counts as households. No Unity types |
| `Traffic.cs` | The traffic's map, fixed with the world: the driving graph (drives in, no junction where a road passes over another), speed limits and lanes (right-hand), households and their cars, the places that draw trips and their parking, routes. No Unity types (`tools/trafficview` builds it) |
| `TrafficDay.cs` | One race's day of traffic from its seed: the households' tours thinned to the survey's driving time, through traffic circling the trunk, every car driven on fixed steps (speed profiles, IDM, uncontrolled junctions by reservation, pulling out and being let in), recorded; any car's place at any time (`At`). No Unity types (`tools/trafficview` checks it). See `docs/TRAFFIC_HANDOFF.md` |
| `TrafficParking.cs` | Where each parked car of a day stands and faces: drive pads, cottages, farmyards, the lots' painted bays and yards, kerb spaces cut along each stretch and shared (terraces keep theirs all day, the rest take the nearest free one as they arrive), all clear of other roads, the water, buildings and each other. No Unity types (`tools/trafficview` checks it) |
| `TrafficLate.cs` | The lateness ledger (stage 3): cars behind their timetable (drawn where the day had them that long ago, the stay at the next stop cut short) or held back in their space, gone (wrecked) or hidden; a late car that would overlap another hidden (the farther from the player), and late and hidden cars caught up once neither place can be seen. No Unity types (`tools/trafficview` checks it) |
| `TrafficCars.cs` | The traffic in the game (stage 2): the day built on a worker at the start, the nearest cars drawn from a pool as copies of the enemy car's bodies (painted, wheels turning), pulled between their space and their lane, stood on the ground by raycasts, a box on the Enemy layer (a wall to hit), headlights on the nearest few at night |
| `TrafficDrive.cs` | Stage 3's driver as arithmetic: where a car is on its leg and the line it steers along, the speed it aims for (profile, the car in front, junctions given way at, the trip's end, never early), how late it is. No Unity types (`tools/trafficview` drives it) |
| `TrafficPhysics.cs` | Part of `TrafficCars` (stage 3): the moving cars near the player made the game's enemy car with our driver (`TrafficBody`), pushed, shot and wrecked (a wreck no kill: the `TakeDamage` patch), back on their timetable through the ledger out of reach; drawn cars held back or hidden where a physics car is; contact physics: a drawn car (parked, or moving and refused by the bubble) the player is about to touch made a loose car (no driver, on its brakes), drawn again untouched or retired knocked |
| `TrafficRoom.cs` | Stage 4, the traffic in a room: who drives each physics car (claimed by the nearest player, granted by the room's host, given back with its ledger entry or handed to a nearer player, a leaver's cars given back), the ledger's entries settled by the host, the host's clock, the states sent and the stand-ins' playback; the messages. No Unity types (`tools/trafficsim` drives it) |
| `TrafficShared.cs` | Part of `TrafficCars` (stage 4): the room read each frame, this machine's physics cars' states sent, stand-ins (`TrafficBody` kinematic) for the cars other machines drive, hits on them sent to the driver, a car handed over turned from one into the other in place. Traffic cars never get modded items or bodies (a plain `CarStats`, our own skin) |
| `Foliage.cs` | The Forest's pines (captured from the main menu), grass tufts, per-tile scatter, instanced drawing |
| `Names.cs` | Every name the world shows (towns, villages, courses): change them here and the signs follow |
| `Signs.cs`, `Lettering.cs` | Road signs: the junction graph and shortest ways, name signs and fingerposts, the lettering atlas; the pixel font |
| `GarageSites.cs` | Where the garages are (filling stations, workshops, the circuit's pits, the rally service area, the hill climb paddock), read from the layout |
| `Garage.cs`, `GarageUi.cs`, `GarageRefit.cs` | The garages in a race: circles, opening on stop in neutral, god mode; the window; the live refit (gun copies) |
| `ShifterHand.cs` | The frame the hand lets go of the active shifter in neutral (what opens a garage and signs on to an event) |
| `GameMode.cs` | The Open World's game mode: a wreck respawns the car at the spawn (in an event, at its last gate), no AI (solo; in a room, the room's race set so) |
| `Courses.cs` | The events' courses read from the layout: centre line, start box off the course, grid spot and a room race's grid slots, waiting spots, checkpoint gates, time limit (pure arithmetic: the preview lists them, meshcheck checks them) |
| `EventRoom.cs` | The events in a room: lobbies, one race per course, standings, cut-off and time limit, results, the weapon rules, and their messages; held by the room's host (the arbiter), on its clock (each machine's real time and the offset to it, synced once a race on the grid, before green is dealt). No Unity types (`tools/eventsim` drives it) |
| `EventRace.cs` | The events in the game: the start boxes and gates drawn, the course's panel, following the local player's lobby (waiting spot, grid slot, start lights to the lobby's start time, timing through the gates, gates sent to the arbiter), respawn at the last gate, the board's text, best times in the config |
| `EventLink.cs` | `EventRoom`'s messages over HalfGripTools' `ctx.Net` (the room's host the arbiter; alone, this machine); holds those that come before the world is built; watches the room for a change; the events' own clock |
| `EventUi.cs` | Part of `GarageUi`: the course's panel (lobbies, Join, Create with the rules), the lobby strip under the board (Start, Leave, pressed with the hand) and the open lobbies panel at the top left |
| `EventBoard.cs` | The cockpit's leaderboard and lap counter: the event on them, tucked into the screen's edge out of one, brought out by the hand |
| `Weather.cs` | Draws a day look and a night look from the game's weathers for each race (room seed), with their rain, snow and grip taken out |
| `Storms.cs` | The weather over the world: rain cells born, drifting with the wind and dying, as a pure function of room seed, position and race time; wetness looking back; the snow line |
| `Sky.cs` | Every frame in a race: the time of day (sun, moon, light, sky, ambient probe, fog, headlights) and the storms at the car (rain, snow, sound, wet roads, grip) |
| `StormField.cs` | The storms round the car on a coarse grid for the painted sky (`Storms.Fill` fills it on a worker). No Unity types |
| `SkyPaint.cs` | The sky painted in code into a panoramic texture: the gradient, the sun's glow and disc, the moon, the stars turning, drifting cloud, the land past the far view lit and fogged; where the sun and moon are at an hour. No Unity types (`tools/skyview` draws it) |
| `Skyline.cs`, `SkylineBuild.cs` | The land past the far view's drawn edge as the painted sky shows it: per sky texel round the horizon, the land a ray meets, its distance, colour and slope (the data; and its build, rays marched over `FarGrid` on a worker). No Unity types (`tools/farview` checks it) |
| `Streamer.cs` | Tile streaming, the per-frame origin check, the highway's wrap (the far end's ground loaded ahead, the jump), debug teleport and overlay; each tile's map picture |
| `WeatherView.cs` | **Public API** for other mods (Slap Pack reads it by reflection): rain, snow, wind, cloud, wetness and the hour at the car, set by `Sky`. See README |
| `MapView.cs` | **Public API** for other mods (MiniMap reads it by reflection): loaded tiles' map pictures and the origin. See README |
| `FarGrid.cs` | The far view's low-detail world: heights every 50 (the tiles' vertices; water lifted, woods' canopy), a picture every 25, the skirts' depths along tile edges. Pure, built after the layout |
| `FarView.cs` | The far view: chunk meshes past the loaded tiles (holes where tiles are, skirts), the sea to the horizon, the cameras' reach |
| `Origin.cs` | The floating origin: moves the scene, particles and trails |
| `Wrap.cs` | The highway wraps round: the trunk's two tunnels joined (a car into one comes out of the other); pure, `Streamer` does the jump as an origin shift that keeps the car and its cameras in place, `WorldScene` builds the open tunnels |
| `GameCaches.cs` | World positions the game keeps in fields, moved on each shift |
| `MultiplayerLink.cs` | Positions over the network in world coordinates; stand-in cars moved on each shift |

## Rules that matter

- **Determinism.** Every player in a room must get the identical ground and roads. `WorldGen`, `Rivers`, `Places` and `Network` use
  only `Rng`, integer hashing, `+ - * /`, `Math.Floor`, `Math.Sqrt`, `Math.Abs`, `DetMath.Sin`/`Cos` and `Network.Atan2`, and break
  every tie (heaps, sorts) by index. Never `System.Random`, `UnityEngine.Random`,
  `Math.Sin`/`Cos`/`Exp`/`Pow`, or a config value. A change to it changes the world, so everyone in a room needs the same version (the Multiplayer mod enforces that).
  `Storms` and `Herds` (the deer) follow the same rules (from the room's seed and the race clock); `Sky` and `Deer` only draw what
  they say, so they may use floats.
- **Origin.** Anything that keeps a world position outside a transform must be moved on `Origin.Shifted` (or listed in `GameCaches`).
  Positions sent over the network are world coordinates. Only x and z shift; heights never change.
- Bump `version` in `mod.json` with each release and keep `README.md` in step (its `Version **x.y.z**` and `Loaded Open World x.y.z`
  lines). Releases are made by hand: the owner runs `.github/workflows/release.yml` (Actions > Release > Run workflow) on `main`
  after merging; it tags `v<version>` and makes the release "Version <version>" with `OpenWorld.zip` (it fails if that version is
  already tagged or the README's differs). It does not compile the mod (no game DLLs there): run `tools/check.sh` before merging.
- C# 10 and netstandard2.1 APIs only.
- Units: game units, about 3.3 to a real metre (a car is about 14 long, a lane 11). Size things at real scale through that.
  Text a player reads (README, `mod.json`, config descriptions, log lines, the overlay) gives real distances (units / 3.3) or
  says "units": never units called metres. The world is 32,000 units, about 9.7 km, across; until 0.26.1 it was called 32 km.
- Heights: `WorldGen.Height` is a scene height (includes `Network.Lift`); road `Y`, river `W` and place `Y` are from sea level (add
  `Network.Lift` for the scene). Mixing the two sank every building once.
- Match the style: tabs, `///` summaries that explain *why*, `Mod.Info(...)` for verbose logging, `Mod.Log.LogWarning` and a try/catch
  around risky scene work.
- **Keys.** Every key the mod reads (debug ones too) goes through HalfGripTools' `ctx.Keys.Add(key, label, section, shown)` in
  `Mod.Load`, so it is listed in the game's Settings > Controls and can be rebound there; read it with `ModKey.Down`/`Held`. Never a
  `ConfigEntry<KeyCode>` or a raw `Input`/`GameInput` read of a fixed key. Pick a default the game does not use: its keys include F1,
  F3, F4, F6, **F8 (bug report)**, F9, F10 and F12 (see `Controls.cs`, `DevBinds.cs`, `ReportBugKey.cs` in the decompile). `Add`
  returns null if the key is taken, so check for null.
- Do not invent an `ObjectiveKind` (a saved run with one is deleted).
- `HalfGripOpenWorld.MapView` is a public API that other mods (MiniMap) read by reflection: never rename it or change its members'
  names or signatures. It is left out of `tools/meshcheck` (it needs `Streamer`).
- `HalfGripOpenWorld.WeatherView` is the same kind of public API (Slap Pack reads it): the weather at the car, written by `Sky` each
  frame. Never rename it or its members; if a member's meaning changes, bump its `ApiVersion`. See the README's Weather API.

## Checking changes

- `tools/check.sh <game folder> <HalfGripTools.dll>` compiles the mod (see README). Run it before every push.
- `dotnet run -c Release --project tools/preview -- out.ppm [pixels] [x z span]` checks the generator (layout, road density at real
  scale, junctions, grades, ground meets roads, repeatability, tile time, the back roads' detours and the farmland's reach to a road)
  and draws a map; look at it after any generator change.
- `dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>]` runs the world against Unity's types: road surfaces under
  the terrain (clip), roads poking through the roads drawn over them (poke), roads floating (float), grades, bridges clear of the
  water, river water meeting its banks (water), verges meeting the ground, farm track beds, building bases (bases), mesh faces
  (faces), lots, track and path ribbons over the ground (ribbons), sidewalk kerbs, roads under drawn water (flooded), plants in
  the water (plants), ponds, lawns and benches (parks), the events' gates and start boxes (events), paving showing ground or a
  verge at junctions (holes), roads carrying on end to end (joints), dead ends rounded off (deadends), tracks under paved roads
  (tucked), places where three or more roads end drawn as one plate (hubs), the far view meeting the tiles with no gap (farview),
  house walls reading whole storeys and roofs with undersides (facades), drives' houses and mailboxes clear of the roads and water
  (homes), houses, towers and churches off the roads, sidewalks and tracks (clear), few country lane dead ends, no through way hooking round and no main road dead end (backroads), the deer's mesh and their spots, legs and
  positions never in deep water, over a drop or on a road but at their crossing, the buck's antlers clear of the body and the ground in every pose, fawns few and only in families, dead deer's bodies coming to rest on the ground, fleeing deer never in deep water, on another road or jumping (deer), the highway's wrap: the trunk's tunnels paired, every lane landing in its lane, the tunnel floors clear of the ground (wrap);
  exits 1 on a failure. It reads the drawn
  surface from `Network.Point` and `Network.Spans`, as `RoadMesh` does. Add a check for every bug fixed.
- `dotnet run -c Release --project tools/jview -- at out.ppm <x> <z> [span]` draws the tiles' road meshes from above round a point,
  marking ground in a mouth (cyan), surfaces poking through (magenta), paving under the ground (yellow) and steps; other modes draw a
  list of points, random junctions or dead ends, list hubs, places or the roads near a point (see the top of its `Program.cs`).
  Look at the junctions an owner reports with it, and before and after any change to junctions, cuts or `RoadMesh`.
- `dotnet run -c Release --project tools/eventsim -- [seed]` drives `EventRoom` on pretend machines over a pretend network with
  delays (joins, leaves, the Host leaving, creates and Starts at once, cut-off, time limit, leaving mid-race, a late joiner, the
  rules, twenty players at random, clocks 30 s apart and machines stalling, worlds built late) and checks every machine agrees with
  the arbiter, syncs its clock on the grid and sees green with it; run it after any change to `EventRoom`.
- `dotnet run -c Release --project tools/deersim -- [seed]` drives `DeerRoom` on pretend machines over a pretend network (claims of
  one group's reaction and kills of one deer from several machines at once, a late joiner, twenty players, stale messages, alone)
  and checks every machine ends with the host's reactions and kills; run it after any change to `DeerRoom`.
- `dotnet run -c Release --project tools/trafficsim -- [seed]` drives `TrafficRoom` on pretend machines over a pretend network (claims of
  one car from several machines at once with releases and hand-overs, checking no car is ever driven on two machines; a late joiner; a
  player leaving with cars; ledger changes from everybody; hits and states after a hand-over; the clock with machines 30 s apart and
  stalling; the stand-ins' playback over a lossy network; who is nearest; twenty players; bad messages; alone) and checks every machine
  ends with the host's drivers and ledger; run it after any change to `TrafficRoom`.
- `dotnet run -c Release --project tools/trafficview [-- --seed N] [--picture out.ppm] [--closeup out.ppm [x z span hour]]` builds the
  traffic's map and reports it (households, cars, places, routes found and joined), then race N's day of traffic twice and checks it
  (the same twice, gaps, junction boxes, speeds, counts by road class against PennDOT, the hours against TPG 9, the wrap; the parking:
  no two parked cars overlapping, no space on another road's paving, in water or in a building, the cost of choosing the cars near
  the player; the lateness ledger; stage 3's physics cars driven by `TrafficDrive` round a pretend player: none in another, in the
  player's car or crossing in a junction box beyond a few, no jump going back on the timetable, nobody waiting for ever; contact physics' touch test: cases by hand, and no car keeping to its lane about to touch a parked one) and can draw
  it, and a close look at the parked cars (`TV_DRIVE_ONLY=1` runs only the day's and the physics cars' checks); run it after any
  change to `Traffic`, `TrafficDay`, `TrafficParking`, `TrafficLate`, `TrafficDrive`, `Plots` or the roads.
- `dotnet run -c Release --project tools/stormview -- out.ppm [room seed] [minutes]` checks the storms (rain cover over 20 seeds,
  repeatability, cost) and draws the world at six times; look at it after any change to `Storms`.
- `dotnet run -c Release --project tools/skyview -- out.ppm [room seed]` checks the painted sky (the sun's disc where its light comes
  from, the horizon the fog's colour, storms seen toward them and nowhere else, repeatability, cost) and draws it at ten times and
  weathers; look at it after any change to `SkyPaint` or the sun's and moon's places.
- `dotnet run -c Release --project tools/farview -- out.ppm [drawn]` builds the far view's low-detail world (time, memory,
  repeatability) and the land painted past it into the sky (it stands where the far view drawn to the fog's end would, a storm's rain
  in front of it, cost), and draws its picture and three views over the country, each to the fog's end and as the game shows it; look
  at it after any change to `FarGrid`, `Skyline`, the land use or how the far view is lit.
- In game: see the test checklist in `docs/TESTING.md`. The owner tests in game and reports with screenshots and `LogOutput.log`.

## What is next

`docs/PLAN.md` holds the agreed plan (fixes, then town fabric, then race circuits). Work from it and keep it current.

## Working with the owner

- The owner merges pull requests; work on a branch and open a PR.
- If you lack access to something (a repo, a host, a tool, game files), ask the owner for it. Do not try workarounds.
