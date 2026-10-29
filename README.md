# HalfGripOpenWorld

A [Half Grip](https://store.steampowered.com/app/4163270/) mod: **Open World**, free roam in a 32 km (20 mile) square world. It is
generated from a fixed seed, so the world is the same every time and for every player: two towns, **Westhaven** and **Eastport**, joined
by a four-lane highway; eighteen villages linked by rural roads; open lowland, mountain ranges with passes, and a wall of mountains at the
edge. It streams in 250 m tiles around the car, and a floating origin keeps the physics as precise 15 km out as at the spawn.

![Map of the world: the highway in orange, rural roads in yellow, the two towns' street grids](docs/world.png)

It is a source mod for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools). Version **0.2.0**, made for game version 0.418.

**Work in progress.** It has the land, the towns' street grids and all the roads, the streaming, the floating origin, a random weather
each race and Multiplayer support. Buildings, trees, signs and ground textures come next (see [docs/DESIGN.md](docs/DESIGN.md)).

## Install

1. Install BepInEx and HalfGripTools (steps 1 and 2 of the [HalfGripTools README](https://github.com/4l3x4nd3r0/HalfGripTools#install)).
2. Copy the `OpenWorld` folder into `BepInEx\HalfGripMods\` in the game folder.
3. Start the game. `BepInEx\LogOutput.log` should have `Loaded Open World 0.2.0` and `Open World map made from TokyoNeon`.

## Playing it

- **Solo:** set `TestMode = true` in `BepInEx\config\halfgrip.openworld.cfg` and start a run. Every race is then in the Open World.
  The Open World is never dealt on a normal run map, because a free-roam race has no finish line and would end the run.
- **Multiplayer:** with the [Multiplayer mod](https://github.com/4l3x4nd3r0/HalfGripMods), the host picks **OpenWorld** in the room's
  map list. Everyone needs this mod at the same version (the Multiplayer mod checks that every player has the same mods). The
  Multiplayer mod itself is not changed: this mod converts the positions it sends (see below). The host ends the race from the pause menu.
- You start on the highway just outside Westhaven, heading for Eastport.
- **Reset** (hold R) puts the car back on its wheels where it is, not back at the spawn.
- **Weather** is drawn from all the game's weathers each race (in a Multiplayer room from the room's seed, so everyone gets the same).

## Settings

All settings only change your own game. Nothing that shapes the world is a setting, because every player in a room has to drive on
the same ground.

| Section | Key | Default | What it does |
|---|---|---|---|
| Map | `TestMode` | false | Every race you start is in the Open World. |
| World | `TileRadius` | 5 | Tiles kept loaded around the car (5 is about 1.3 km; the fog ends there). |
| World | `ShiftDistance` | 1000 | Metres from the scene's origin before the scene is moved back. |
| Debug | `Teleport` | false | Lets `TeleportKey` (F7) jump the car `TeleportDistance` (5000 m) ahead. |
| Debug | `ShowCoords` | false | Shows the world position, origin offset and tile count on screen. |
| Debug | `VerboseLog` | false | Logs every origin shift and tile timings. |

## How it works

- **World** (`WorldGen.cs`, `Noise.cs`): height is a pure function of world x, z from integer-hash gradient noise, using only
  additions and multiplications (no `System.Random`, `UnityEngine.Random` or `sin`), so it comes out identical on every machine.
- **Layout** (`Network.cs`): towns and villages are placed on open ground, then roads are routed by A* over the land (costed by grade,
  so they go round mountains and through passes), smoothed into curves, and given a height profile held to 5% (highway) or 9% (rural)
  that meets every road it crosses level. The ground is then cut and filled to each road's bed, with banks beside it.
- **Roads** (`RoadMesh.cs`): each tile draws the road pieces in it as one mesh with a collider, with pixel-art lane textures made in code.
- **Streaming** (`Streamer.cs`): heights are worked out on worker threads, then turned into a mesh and collider on the main thread, a
  few tiles a frame, nearest first.
- **Floating origin** (`Origin.cs`, `GameCaches.cs`): past `ShiftDistance`, every root object in the scene moves back by whole tiles,
  along with world-space particles and trails and the positions the game keeps in fields (track line samples and their search tree,
  the lap counter, the rewind recorder, and others).
- **Multiplayer** (`MultiplayerLink.cs`): every player's scene is shifted differently, so positions go over the network in world
  coordinates. The Multiplayer mod's packet writer and reader are patched to add the local origin to positions as they are written,
  and take it off as they are read. Velocities, rotations and everything else pass unchanged, and the packet format stays the same.
- **Free roam** (`Patches.cs`): no laps, no AI rivals in solo, no "wrong way" or "hold R" hints, resets in place, and "fell under the
  map" measured from the ground under the car.

## Checking a change

Compile against the game without starting it (Linux or macOS, or WSL; the game folder can be the HalfGripRefs repo's `game/`):

```
dotnet build <HalfGripTools>/HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<game folder>
tools/check.sh <game folder> <HalfGripTools>/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll
```

On Windows, HalfGripTools' own `tools\check-mods.ps1 -ModsPath <this repo> -Mod OpenWorld -GameDir <game folder>` does the same.

Check the world generator (the layout, road grades, that the ground meets every road, repeatability, tile time) and draw a map of
the whole world:

```
dotnet run -c Release --project tools/preview -- world.ppm 1024
dotnet run -c Release --project tools/preview -- world.ppm 1024 list    # also lists every road
```

## Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

1. The race loads on the highway outside Westhaven, facing Eastport; none of Tokyo's arena, city or train is left.
2. Drive along the highway: tiles and road appear ahead before you reach them, with no gaps or seams between tiles, and the road does
   not flicker where roads meet. Turn off into Westhaven's streets and onto a rural road.
3. Past 1 km the log says `Origin shift 1` and the overlay's scene position jumps back near 0 while the world position keeps
   counting. Watch for a visible hitch, a jump of the car or camera, or tyre marks and smoke left behind.
4. Teleport (F7) a few times to 10 to 15 km out. The car should drive exactly as at the spawn: no jitter in the suspension or camera,
   and the hand, cursor and cockpit controls as steady as at the spawn (the log's shift lines say how many cockpit objects were kept).
5. Hold R far out: the car is put back on its wheels where it was.
6. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
7. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
8. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
9. The log's `Weather:` line, and that the weather looks right (rain and snow follow the car).
