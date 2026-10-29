# Design notes

## Why 32 km needs a floating origin

Unity positions are 32-bit floats. The step between representable positions grows with distance from 0,0:

| Distance | Step | Effect on a car |
|---|---|---|
| 2 km | 0.12 mm | none |
| 5 km | 0.5 mm | none |
| 10 km | 1 mm | suspension and camera jitter begins |
| 16 km (the world's edge, from its centre) | 1 mm | jitter |
| 20 km and beyond | 2 mm and more | visible wobble, unstable contacts |

So the physics must never run far from 0,0. `Origin` moves the whole scene back by whole tiles when the car passes `ShiftDistance`,
and keeps the true position as a double offset. Only x and z move.

Shrinking the car instead does not help: precision relative to the car's own size is what matters, so a half-size car at 5 km is
like a full-size car at 10 km, and its handling would all need retuning.

## What moves on a shift

Transforms of every scene root (not screen-space canvases), then `Physics.SyncTransforms`.

**Except the cockpit.** The game draws the cockpit (dashboard, hand, guns, shifter, mirror) as a separate rig on the `INTERIOR` layer
that stands still near 0,0 and is seen by its own `InteriorCamera`; only the car outside moves. The rig shares a root (`Player`) with
the car, so after the roots move, the top of every `INTERIOR` subtree is put back, and the cockpit's particles, trails and bodies are
left alone. The interior's post-processing volume (`ENVIROMENT/Weather/InteriorVolume`) is on that layer too. 0.1.0 moved the
cockpit with the world, and after a few shifts the hand and controls shook with float error. Interpolated rigidbodies have interpolation
switched off for two frames so they do not visibly slide. World-space particles, trail renderers and world-space line renderers are
moved point by point.

Positions the game keeps in fields (from a decompile of 0.418; every `Vector3`, `Vector3[]` and `List<Vector3>` field was checked, and
those holding world positions rather than offsets or directions are listed):

| Class | Field | Notes |
|---|---|---|
| `PathManager` | `sampledPoints`, `kdTree`, `laneSolves[].points` | the tree is rebuilt from the moved samples; `lanes` are offsets and heights, unchanged |
| `LapManager` | `lapDrivenFrom`, `projectedPathIndices` | |
| `TimeRewinder` | `frames[].pos` | the rewind item's recording |
| `RoadPickup` | `restPosition` | |
| `WheatTrampler` | `previousPosition` | on every car |
| `horizontalChicken` | `startPoint` | |
| `EnemyController` | `targetPoint` | AI |
| `FreeFlightCamera` | `targetPosition`, `homePosition` | debug camera |
| `WaypointsManager` | `processedWaypoints` | |
| `TrainPath` | `sampledPoints`, `lastPositions` | Tokyo only |
| `PlayerResetLogic` | `lowestOf` | a reference to `sampledPoints`; heights only, unchanged |
| Multiplayer `RemoteCar` | `position`, `AimPoint` | moved by `MultiplayerLink` |

Not yet handled: the Multiplayer mod's rocket ghosts keep their own last state; a rocket in flight during a shift may jump once.

## Multiplayer

The Multiplayer mod is not changed. Every position goes through its `W.Vec` / `R.Vec` (packet writer and reader), so those are
patched: a position is turned into world coordinates as it is written and back into this player's scene coordinates as it is read.
Which `Vec()` in each message is a position (protocol 7):

| Message | Vec()s | Positions |
|---|---|---|
| Car | position, velocity, aim point (when a gun is raised) | even |
| Respawned | position | even |
| AiState | (position, velocity) per AI car | even |
| Rocket | (position, velocity) per rocket | even |
| RocketEnd | position | even |
| Shot | landing points | all |

At hook time the message numbers are checked against the Multiplayer mod's `Msg` names; on a mismatch nothing is patched and a warning
is logged. Floats are good to about 1 mm at 16 km, which is plenty for drawing another car (only local physics needs better). Remote
cars are kinematic stand-ins, so one over ground that is not loaded on this machine floats; it does not fall.

## World

![Map of the whole 32 km world, north up: highway orange, rural roads yellow, town streets grey](world.png)

Units are game units: a car is about 14 long and a lane 11 (about 3.3 units to a real metre, so the world is about 10 km at car scale,
8 minutes edge to edge at 120 on the dash).

`WorldGen.Natural(x, z)`: rolling lowland (4 km gradient fBm, 90), mountain ranges where a 9 km mask allows (domain-warped ridged
noise, 520), hills (600, 22), bumps (70, 1.2), and a 700 wall over the last 1.5 km of the edge. Each noise octave is turned 37 degrees so
no lattice lines up. `Base` adds a flat plateau under each town (radius 1500, blending over 900) and village (180, over 500) at the
land's average height there. `Height` adds the road beds.

## Layout (`Network.cs`)

1. **Towns:** Westhaven in the west band, Eastport in the east, each the flattest open ground (least mountain mask and roughness) of 60
   seeded tries.
2. **Villages:** up to 18, on open ground (mountain mask 0), 3.5 km apart and 4 km from the towns.
3. **Highway:** A* over a 200-unit grid of `Base`, a step costing length x (1 + 6 (grade / 4%)^2); every 5th cell kept, Chaikin x4,
   resampled every 8. Profile: the land averaged over 45 samples each way, pinned to the towns' heights, held to 5%. Four lanes, 66 wide.
4. **Rural roads:** a minimum spanning tree over all places plus each place's nearest unjoined neighbour under 7 km (never town to
   town). A* with grades costed against 7%, extra cost on and beside roads already built (so roads cross rather than run alongside) and
   through towns (so they meet the ring road). Where one crosses a road already built it is pinned to that road's height there. Held to
   9%. Two lanes, 24 wide.
5. **Streets:** each town has a ring road at its edge and a grid every 200 across it, turned to a seeded angle, all at the town's height.
6. The whole world is lifted so the road at the spawn is at 0.5 (the Multiplayer mod puts cars at height 1 on the grid).

The ground at a point is the bed (0.3 under the surface) of the road that claims it most: fully within half the width plus 4, then a
bank back to the land that is wider the bigger the cut or fill (1.6 x the height difference plus 12, up to 150). Segments are found
through a 128-unit grid. About 2 to 7 ms per tile on a worker thread.

Road surfaces are per tile: each road piece (between two samples) is drawn by the tile its middle is in, and both ends of a piece come
from the samples, so pieces meet exactly across tile edges. Highway surfaces sit 0.06 above their line, rural 0.03, streets 0, so where
two overlap at a junction one is cleanly on top.

## Assets

Only one race scene is loaded at a time, plus the main menu (always loaded before a race; its objects can be kept alive, as the
Highway mod does). The world is built in Tokyo, so it can use Tokyo's tall buildings, temples, arch, street lamps, barriers and sakura
trees; the menu gives the Forest's pine trees, wooden fences, grass and wheat, and its pixel terrain textures. Anything else is made in
code (roads, guardrails, signs, simple buildings with generated pixel textures).

## Next steps

1. **Ground and countryside:** ground textures by zone and slope, trees, fields, fences, farm buildings, village houses.
2. **Towns:** buildings on the blocks (Tokyo's and generated ones), street lamps and props.
3. **Highway dressing:** guardrails, median barrier, signs with place names, proper junctions where roads meet it.
4. **Landmarks and far view:** low-detail skylines and mountains past the loaded tiles.
5. **Collider baking off the main thread** (`Physics.BakeMesh`) if tile building shows in the frame time.
