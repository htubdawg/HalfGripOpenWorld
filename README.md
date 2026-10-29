# HalfGripOpenWorld

A [Half Grip](https://store.steampowered.com/app/4163270/) mod: **Open World**, free roam in a 32 km (20 mile) square of country. It is
generated from a fixed seed, the same every time and for every player, the way real country comes about:

- **Land and water:** a coastal plain and the sea to the south, uplands and mountain ranges to the north. Rivers come from the way
  water really drains the land: each finds its way to the sea, gathering tributaries, cutting a channel, a floodplain and banks. There
  are lakes in the deepest hollows.
- **Places:** a market town, villages and about a hundred farms and hamlets, each where people would settle: flat ground, near a river
  but above its floodplain, near the coast, out of the mountains. A few big places and many small ones, fairly evenly spread.
- **Roads, grown in order of importance:** a dual-carriageway trunk road along the coast that bypasses the towns; main roads between
  neighbouring places (the strongest links first); a road over a mountain pass; streets grown out from the roads in every town and
  village; a lane to every farm; farm tracks into the fields. New roads follow the roads already there and branch off them at
  T-junctions, so the network has the shape real ones have. Every road is held to its class's grade, crosses rivers on bridges and
  meets the roads it crosses level. Roads that leave the world go into tunnels in the edge walls.
- **Ground:** a pixel-art texture per tile from what the land is used for: fields in parcels of different crops with hedges, woods,
  wet meadows along the rivers, built-up streets and gardens in towns, farmyards, beaches, rock and snow.

![Map of the world: the trunk road orange, main roads yellow, minor roads cream, lanes white, tracks brown, bridges red](docs/world.png)

It streams in 250 m tiles around the car, and a floating origin keeps the physics as precise 15 km out as at the spawn. It is a source
mod for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools). Version **0.3.0**, made for game version 0.418.

**Work in progress.** Buildings, trees and signs come next (see [docs/DESIGN.md](docs/DESIGN.md)).

## Install

1. Install BepInEx and HalfGripTools (steps 1 and 2 of the [HalfGripTools README](https://github.com/4l3x4nd3r0/HalfGripTools#install)).
2. Copy the `OpenWorld` folder into `BepInEx\HalfGripMods\` in the game folder.
3. Start the game. `BepInEx\LogOutput.log` should have `Loaded Open World 0.3.0` and `Open World map made from TokyoNeon`.

## Playing it

- **Solo:** set `TestMode = true` in `BepInEx\config\halfgrip.openworld.cfg` and start a run. Every race is then in the Open World.
  The Open World is never dealt on a normal run map, because a free-roam race has no finish line and would end the run.
- **Multiplayer:** with the [Multiplayer mod](https://github.com/4l3x4nd3r0/HalfGripMods), the host picks **OpenWorld** in the room's
  map list. Everyone needs this mod at the same version (the Multiplayer mod checks that every player has the same mods). The
  Multiplayer mod itself is not changed: this mod converts the positions it sends (see below). The host ends the race from the pause menu.
- You start on the trunk road near Kingsbridge, the main town, heading east; the turning into town is just ahead.
- **Reset** (hold R) puts the car back on its wheels where it is, not back at the spawn. A car that ends up in a river, a lake or the
  sea is put back on the nearest road.
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

- **Land** (`WorldGen.cs`, `Noise.cs`): height is a pure function of world x, z from integer-hash gradient noise, using only additions
  and multiplications (no `System.Random`, `UnityEngine.Random` or `sin`), so it comes out identical on every machine.
- **Rivers and lakes** (`Rivers.cs`): a priority flood drains every cell of a 100-unit grid to the sea; catchments give the rivers,
  which are smoothed, given meanders and a falling water level, and cut into the land.
- **Places** (`Places.cs`): every spot is scored for settling; a town, villages and farms are taken best first, each tier keeping its
  distance.
- **Roads** (`Network.cs`): routed by A* over the land, costed by grade per class, with bridges dear and road-sharing cheap; built in
  order of importance; town streets grown with local rules (stop at a crossing, join a road nearby). See [docs/DESIGN.md](docs/DESIGN.md).
- **Meshes** (`RoadMesh.cs`, `WaterMesh.cs`, `LandUse.cs`): each tile draws its roads, tracks, water and ground texture, all worked out
  on worker threads.
- **Floating origin** (`Origin.cs`, `GameCaches.cs`): past `ShiftDistance`, every root object in the scene moves back by whole tiles,
  along with world-space particles and trails and the positions the game keeps in fields; the cockpit stays where it is.
- **Multiplayer** (`MultiplayerLink.cs`): positions go over the network in world coordinates; the Multiplayer mod's packet writer and
  reader are patched to convert them. The packet format stays the same.
- **Free roam** (`Patches.cs`): no laps, no AI rivals in solo, no "wrong way" or "hold R" hints, resets in place, and "fell under the
  map" measured from the ground under the car.

## Checking a change

Compile against the game without starting it (Linux or macOS, or WSL; the game folder can be the HalfGripRefs repo's `game/`):

```
dotnet build <HalfGripTools>/HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<game folder>
tools/check.sh <game folder> <HalfGripTools>/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll
```

On Windows, HalfGripTools' own `tools\check-mods.ps1 -ModsPath <this repo> -Mod OpenWorld -GameDir <game folder>` does the same.

Check the world generator (layout, road density at real scale, junction types, grades, that the ground meets every road,
repeatability, tile time) and draw a map, of the whole world or of a square of it:

```
dotnet run -c Release --project tools/preview -- world.ppm 1024
dotnet run -c Release --project tools/preview -- town.ppm 640 -2600 -6800 6000    # west x, north z, side
```

## Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

1. The race loads on the trunk road near Kingsbridge, heading east; none of Tokyo's arena, city or train is left.
2. Drive along the trunk road and into Kingsbridge: tiles, roads and ground textures appear ahead before you reach them, with no gaps
   or seams between tiles, and the road does not flicker where roads meet. Try a lane to a farm and a farm track (looser grip).
3. Cross a river on a bridge (concrete sides and parapets); drive into a river or the sea and check you are put back on a road.
4. Past 1 km the log says `Origin shift 1` and the overlay's scene position jumps back near 0 while the world position keeps
   counting. Watch for a visible hitch, a jump of the car or camera, or tyre marks and smoke left behind.
5. Teleport (F7) a few times to 10 to 15 km out. The car should drive exactly as at the spawn: no jitter in the suspension or camera,
   and the hand, cursor and cockpit controls as steady as at the spawn (the log's shift lines say how many cockpit objects were kept).
6. Hold R far out: the car is put back on its wheels where it was.
7. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
8. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
9. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
10. The log's `Weather:` line, and that the weather looks right (rain and snow follow the car).
