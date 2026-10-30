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
  village; a lane to every farm; farm tracks into the fields; footpaths on from the dead ends of streets. New roads follow the roads already there and branch off them at
  T-junctions, so the network has the shape real ones have. Every road is held to its class's grade, crosses rivers on bridges clear
  of the water, runs on a low embankment where the land by the coast or a lake is lower than the water, and meets the roads it joins or crosses flush: it stops at the other road's edge with a flared mouth. Paved roads
  have gravel verges down to the ground, and paved sidewalks with a kerb in towns and villages; farm tracks (two tyre ruts and a
  grass strip) and gravel footpaths lie on shallow beds pressed into the ground, with soft grassy edges. Roads that
  leave the world go into tunnels in the edge walls.
- **Ground:** a pixel-art texture per tile from what the land is used for: fields in parcels of different crops with hedges, woods,
  wet meadows along the rivers, built-up streets and gardens in towns, farmyards, beaches, rock and snow.
- **Buildings:** houses along the streets of every town and village, terraced and up to six storeys in a town's middle, detached and
  set back further out, in brick, render, white and stone with tiled, slate or flat roofs; shops with flats over them along the high
  street; a cluster of glass towers, 8 to 14 storeys, in the main town's centre; a stone church with a spire in every village; a
  farmhouse, barn and shed at every farm. Each house is on its own street's side of the block, so none overlap. Solid.
- **Retail park and businesses:** a shopping centre behind a big car park (marked bays, lamp posts) at the main town's edge, and
  shops with parking, workshops with yards and filling stations with a canopy over the pumps along the main roads at the edges of
  towns and villages. Their car parks and forecourts are flat, drivable and grip like tarmac.
- **Parks and trails:** a few whole blocks in each town given over to a mown lawn with clumps of trees, the big ones with a pond in
  the middle and a path round it, paths in from the streets on every side and benches along them; a riverside park with a walk beside
  the water where a river runs through Kingsbridge; and footpaths round the lakes, following their shores, with a bench now and then
  looking out over the water.
- **Race circuit:** a closed tarmac lap of about 2.9 km on flat open country, laid out from the seed like the rest: nine corners
  both ways, from fast sweepers to a hairpin, red and white kerbs, gravel traps outside the corners (they slow you down), grass
  run-off and an armco barrier round the outside. The start straight has a grid, a start gantry, a pit wall, a pit lane and a row of
  garages, with the paddock behind them; three grandstands look on. Its own road leads from the paddock gate to the nearest main
  road, and no other road crosses it. Free roam only for now: no timing yet.
- **Rally stage:** a closed loop of gravel road of about 5 km through the forest on the north-east upland, laid out from the seed:
  some thirty corners, from flat-out kinks to first-gear hairpins, over crests and dips (about 100 m of climbing a lap, never steeper
  than 12%), with straw bales on the outside of the tightest corners. A short straight holds the finish and start arches, with the
  service area beside it (a gravel yard with the teams' vans and awnings); a track leads from there to the nearest lane, and no other
  road comes near the stage. Loose gravel grip. Free roam only, like the circuit.
- **Trees and grass:** the Forest map's own pines in the woods, along hedges, in gardens and in clumps in the parks (with trunks you
  can hit), and grass tufts on lawns, meadows, pasture and moor, with ripe wheat in the wheat fields, drawn near the car; none in the
  water.

![Map of the world: the trunk road orange, main roads yellow, minor roads cream, lanes white, tracks brown, bridges red; the circuit in the north-west, the rally stage (orange) in the north-east](docs/world.png)

It streams in 250 m tiles around the car, and a floating origin keeps the physics as precise 15 km out as at the spawn. It is a source
mod for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools). Version **0.9.0**, made for game version 0.418.

**Work in progress.** Signs, street lamps and a far view come next (see [docs/DESIGN.md](docs/DESIGN.md)).

## Install

1. Install BepInEx and HalfGripTools (steps 1 and 2 of the [HalfGripTools README](https://github.com/4l3x4nd3r0/HalfGripTools#install)).
2. Copy the `OpenWorld` folder into `BepInEx\HalfGripMods\` in the game folder.
3. Start the game. `BepInEx\LogOutput.log` should have `Loaded Open World 0.9.0` and `Open World map made from TokyoNeon`.

## Playing it

- **Solo:** set `TestMode = true` in `BepInEx\config\halfgrip.openworld.cfg` and start a run. Every race is then in the Open World.
  The Open World is never dealt on a normal run map, because a free-roam race has no finish line and would end the run.
- **Multiplayer:** with the [Multiplayer mod](https://github.com/4l3x4nd3r0/HalfGripMods), the host picks **OpenWorld** in the room's
  map list. Everyone needs this mod at the same version (the Multiplayer mod checks that every player has the same mods). The
  Multiplayer mod itself is not changed: this mod converts the positions it sends (see below). The host ends the race from the pause menu.
- You start on the road into Kingsbridge, the main town, just outside it and facing its middle.
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
| World | `GrassReach` | 2 | Tiles round the car whose grass is drawn (0 turns grass off). |
| World | `ShiftDistance` | 1000 | Metres from the scene's origin before the scene is moved back. |
| Debug | `Teleport` | false | `TeleportKey` (F7) jumps the car to the next key place (the spawn, the towns, the circuit's grid and paddock, the rally stage's start and service area, the retail park, the villages, a filling station and a workshop, the mountain pass), Shift+F7 to the one before; the place's name shows at the top of the screen. |
| Debug | `ShowCoords` | false | Shows the world position, origin offset and tile count on screen. |
| Debug | `VerboseLog` | false | Logs every origin shift and tile timings. |

## How it works

- **Land** (`WorldGen.cs`, `Noise.cs`): height is a pure function of world x, z from integer-hash gradient noise, using only additions
  and multiplications (no `System.Random`, `UnityEngine.Random` or `sin`), so it comes out identical on every machine.
- **Rivers and lakes** (`Rivers.cs`): a priority flood drains every cell of a 100-unit grid to the sea; catchments give the rivers,
  which are smoothed, given meanders and a falling water level a unit under the lowest land along both banks (as cut by the bigger
  rivers), and cut into the land: the nearest river shapes the ground, and a low bank keeps a river's water from hanging over a
  lower river beside it.
- **Places** (`Places.cs`): every spot is scored for settling; a town, villages and farms are taken best first, each tier keeping its
  distance.
- **Roads** (`Network.cs`): routed by A* over the land, costed by grade per class, with bridges dear and road-sharing cheap; built in
  order of importance; town streets grown with local rules (stop at a crossing, join a road nearby). See [docs/DESIGN.md](docs/DESIGN.md).
- **Parks and trails** (`Parks.cs`): a town's blocks are the connected ground left between its roads; a few compact ones near the
  middle become parks (a pond dug in the big ones), and a river's bank through the built-up town another; their paths are footpaths
  added to the network. Each lake's shore is traced and a trail laid a little way up from it.
- **Circuit** (`Circuit.cs`, `CircuitMesh.cs`): sited before the villages and farms on the flattest open box of ground, clear of
  towns, rivers, the coast and the mountains; its lap is a chain of corners round a ring squashed to the box, the best of 40 that fit,
  and its ground is levelled to a plane with banks back to the land. The router keeps every other road out of it.
- **Rally stage** (`Rally.cs`): sited after the villages and before the farms on the open upland whose land rolls most like a forest
  stage, clear of water, mountains and places; its loop is a straight and waypoints round a ring, routed leg by leg by A* (grade and
  a slow noise make it wind; each leg keeps off the ones before it), rounded, the best of 30 that fit. It is a road of its own class
  (`RoadKind.Rally`), unpaved like a track, made before every other road; the router keeps them out of it, and the land use keeps
  forest round it.
- **Meshes** (`RoadMesh.cs`, `WaterMesh.cs`, `LandUse.cs`, `Buildings.cs`): each tile draws its roads, verges, water, buildings and
  ground texture, all worked out on worker threads.
- **Trees and grass** (`Foliage.cs`): scattered from each tile's ground pixels and drawn GPU-instanced; the pines are captured from
  the main menu (as the Highway mod does), with simple pines made in code as a fallback.
- **Floating origin** (`Origin.cs`, `GameCaches.cs`): past `ShiftDistance`, every root object in the scene moves back by whole tiles,
  along with world-space particles and trails and the positions the game keeps in fields; the cockpit stays where it is.
- **Multiplayer** (`MultiplayerLink.cs`): positions go over the network in world coordinates; the Multiplayer mod's packet writer and
  reader are patched to convert them. The packet format stays the same.
- **Free roam** (`Patches.cs`): no laps, no AI rivals in solo, no "wrong way" or "hold R" hints, resets in place, and "fell under the
  map" measured from the ground under the car.

## Map API for other mods

`HalfGripOpenWorld.MapView` (`MapView.cs`) is a public API other mods rely on (the MiniMap mod reads it by reflection, by type and
member name). **Do not rename it or change its members' names or signatures.** All its members are static properties or methods:

| Member | |
|---|---|
| `bool Active` | True while an Open World race is loaded. |
| `double OriginX`, `double OriginZ` | World coordinates of the scene's 0,0: add them to a scene position for a world position. |
| `float TileSize` | 250. Tile (tx, tz) covers world x from `tx * TileSize` to `(tx + 1) * TileSize`, and z the same. |
| `int TilePixels` | 64, the side of a tile's map picture. |
| `bool TryGetTile(int tx, int tz, Color32[] into)` | Copies a loaded tile's map picture into `into` (`TilePixels` squared, rows from the south edge, x running east); false if the tile isn't loaded. Main thread only. |
| `int Version` | Goes up whenever a tile's map picture is added or removed. |

A tile's map picture is its land use with water and roads painted in, in the colours of `docs/world.png` (trunk orange, primary
yellow, secondary cream, streets grey, lanes white, tracks brown, footpaths pale tan, the rally stage orange), roads at least 2 pixels wide, car parks and yards light grey
with their buildings dark grey, and the circuit's track dark grey with red kerbs, sand gravel traps, a mid-grey pit apron and its
garages and stands dark grey. It is made with the tile on a worker
thread and kept only while the tile is loaded. Display only: nothing in the world depends on it.

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

Check the built world against Unity's own types (road surfaces under the terrain, roads poking through each other, grades, bridges
clear of the water, no road under water drawn over it, river water meeting its banks, verges meeting the ground, track beds, building
bases, mesh faces, car parks lying on their ground and meeting the verge, track and path ribbons over the ground, sidewalk kerbs, no
tree or grass in the water, ponds' edges under the ground, no building on a park's lawn; the circuit's lap closed and clear of
itself, its surfaces over the ground, nothing else in its fence, its barrier and stands clear of the track; the rally stage closed,
clear of itself, held to its grade, nothing else in its fence, its bales off it); it exits 1 if a check fails:

```
dotnet run -c Release --project tools/meshcheck -p:GameDir=<game folder>
```

## Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

1. The race loads on the road into Kingsbridge, just outside the town, facing it; none of Tokyo's arena, city or train is left.
2. Drive along the trunk road and into Kingsbridge: tiles, roads and ground textures appear ahead before you reach them, with no gaps
   or seams between tiles. Where roads meet, the smaller one stops at the bigger one's edge with a flared mouth, level with it (no
   lip, no flicker, nothing poking through); the same where two streets cross.
3. Cross rivers on bridges (concrete sides and parapets), small streams too: no water over the road anywhere. Follow a river and its
   tributaries, in the lowland and up in the mountains: the water's edge meets the bank everywhere, never hanging in the air with
   ground or sky showing under it. Drive into a river or the sea and check you are put back on a road.
4. The gravel verges either side of the paved roads: no step at the road's edge or where they meet the grass, a little less grip
   than the road, none on bridges. In Kingsbridge and the villages, sidewalks instead (no bump from the road, a kerb at their back,
   none in front of a car park or forecourt). A farm track, and a footpath out of the end of a town street: crisp ruts and gravel
   (not blocky), no ground showing through them or at their edges, no flicker where a path ends on a track, no grass tufts or
   trees on it, looser grip than the grass beside it.
5. In Kingsbridge: houses line the streets, none stand on a road, you cannot drive through them. In the country: woods of pines (you
   hit their trunks), grass round the car, wheat in the wheat fields. The log has `Forest trees captured: ...` (if it says simple pines
   stand in, the main menu's trees were not found). In Kingsbridge's middle, the glass towers (setbacks, plant on the roofs); along
   its main roads, shopfronts in four colours; in a village, the church and its spire.
6. The retail park at Kingsbridge's south-west edge, about 200 m north of the spawn, and a filling station or workshop at a
   village's edge: drive on and off the car park from the road with no bump at its edge, the bay lines crisp (not blocky), no grass
   or trees on it, no ground poking through it, no flicker; hit a lamp post and a canopy post (solid).
7. Teleport (F7) to Kingsbridge Park and the others: a striped lawn up to the back of the sidewalk, no houses on it, a pond with
   its water's edge meeting the bank all round (no edge hanging in the air), the path round it and the paths in from the streets
   meeting it, benches beside them (solid, standing on the ground), clumps of trees, no trees or grass in the pond. Kingsbridge
   Riverside: the walk along the river's east bank. A lake trail (teleport to `Lake trail 1`): a gravel path round the shore, no
   water over it, benches facing the lake. Grass and trees nowhere in a river's bed or a lake's shallows.
8. The workshops (teleport to `... workshop`): the metal cladding in clear vertical ribs and the roof in rows, no fine pinkish
   stripes or shimmer from near or far; the same on the retail park's walls and the farm barns' roofs.
9. The circuit (teleport to `Circuit (grid)`): drive a lap. The track, kerbs and pit lane lie flat on the ground with no lip at
   their edges and no ground or grass showing through; the kerbs are red and white blocks, a little raised; the gravel traps slow
   the car right down; the barrier is solid. The gantry over the start line faces the grid, the garages' doors face the pit lane,
   and the three grandstands face the track with their seats rising away from it. `Circuit (paddock)`: drive out of the gate onto
   the circuit road, no step where it leaves the paddock. No trees on the run-off.
10. The rally stage (teleport to `Rally stage (start)`): drive a lap. Gravel all the way, with the two worn lines down it, looser than
   tarmac; no ground showing through it and no step at its edges; forest close on both sides but no trunk on the road; no seam where
   the lap comes back onto the straight. The straw bales on the outside of the tight corners are solid and stand on the ground. The
   arches over the start and finish lines stand clear of the road. `Rally stage (service area)`: the gravel yard, vans and awnings,
   no bump onto the stage; drive out along the track at its back to the lane, where the track ends at the lane's edge (it does not
   run on across or under it).
11. The trunk road along the coast (east of the spawn, and west towards the edge): no sea water drawn over it anywhere; it runs on a
   low bank above the shore.
12. Past 1 km the log says `Origin shift 1` and the overlay's scene position jumps back near 0 while the world position keeps
   counting. Watch for a visible hitch, a jump of the car or camera, or tyre marks and smoke left behind.
13. Teleport (F7) round the key places, several of them 10 to 15 km out. The car should drive exactly as at the spawn: no jitter in the suspension or camera,
   and the hand, cursor and cockpit controls as steady as at the spawn (the log's shift lines say how many cockpit objects were kept).
14. Hold R far out: the car is put back on its wheels where it was.
15. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
16. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
17. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
18. The log's `Weather:` line, and that the weather looks right (rain and snow follow the car).
