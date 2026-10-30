# Plan

Agreed with the owner after testing 0.4.3. Work top to bottom; each PR runs `tools/check.sh`, `tools/preview` and `tools/meshcheck`
before it goes up, and adds checks for what it fixes.

## PR 1: fixes (0.4.4: built, all checks pass, waiting for the owner's test in game)

How it was done, where it differs from the notes below:

- Bridges: crossings found by intersecting road segments with river lines; each deck spans the water at the angle crossed, plus 6
  each side; `Hold` and `Seat` never move a deck; the banks beside a bridge never fill the channel. On 0.4.3, 3179 of 7351 road
  points over water were less than 4 above it (the lowest 6.1 under it); now none (`meshcheck` bridges).
- Junctions go further than the note: wherever a road's centre line runs onto a road drawn over it (bigger, or of its class and
  built earlier), at its end or in a crossing, it is cut out along that road's edges, flared, and its apron tilted to meet that
  road's surface 0.05 under it. Mouths may be up to a quarter steeper than the class grade (`meshcheck` grades).
- Farm tracks are 10 wide (were 9); the ground texture is now 128 pixels a tile (the land use stays 64) so the ruts and strip show.
  Their grip comes from a `HandlingModifier` subclass on the tile that looks the wheel's point up in the texture.
- Found on the way: a road's smoothed line could miss the router cells it was routed through, so a later route built the same
  stretch again on top of it; the cells are now marked. This changed the layout a little (fewer duplicate main roads).

1. **Bridges** (reported: a river drawn over a bridge). Likely cause: crossings are found by testing road *samples* (8 apart) for
   being inside a river's channel, so a narrow river can be stepped over with no bridge, and the road then dips below the water. A
   second suspect: `Seat` (run after `Bridges`) lowering a deck near a junction. Fix: find crossings by intersecting road segments with
   river lines; deck clear of the water over the span and its approaches; `Seat` never lowers bridge samples. Check: every road point
   over a channel at least 4 above the water.
2. **Farm tracks into the ground** (owner likes them): no mesh; the ground pressed into a shallow bed (about 0.3, soft edges); the
   ground texture paints dirt with two tyre ruts and a grass strip; no grass or trees on them; dirt grip.
3. **Dirt verges** on every paved road: a strip about 4 wide each side from the road surface down to the real ground at its outer edge,
   gravel texture, a little less grip; none on bridges. Check: no step over about 0.1 at either edge.
4. **Junctions** (rare road-on-road clipping left): a minor road ends at the major road's edge with a flat apron in the mouth;
   same-class crossings share one height. Check: `meshcheck` poke at 0.

## PR 2: town fabric (the Tokyo landmarks go: they do not fit the look)

Everything made in code in the same pixel style, placed from the seed with the own-side-of-the-block rule:

- **Centre** (main town): a small cluster of **skyscrapers**, 8 to 14 storeys (about twice today's tallest), stacked setback blocks,
  glass curtain-wall texture, rooftop plant.
- **High street**: terraced shops (coloured shopfronts, sign bands) with flats over, along main roads in the core.
- **Residential**: today's houses.
- **Retail park** at the main town's edge on a main road: a **shopping centre / plaza** (big low building, glass front, sign band, flat
  roof) facing a large **car park** (painted bays, lamp posts, kerbs, flat and drivable) with an entrance road.
- **Small businesses** on main roads at town and village edges: shops, workshops, a garage or filling station, each with a small car park
  or forecourt.
- **Parks**: a few town blocks and riverbanks in town: lawn, tree clumps, a pond, benches, footpaths.
- **Walking trails**: narrow gravel paths painted into the ground like the tracks, looping round lakes and through parks; drivable.

## PR 3: race circuits

Two to four closed circuits across the map, each joined to the network by an access road and with a start/finish gantry:
a **tarmac circuit** on open flat land (about 3-4 km real lap, kerbs, run-off, pit straight and building, grandstands, paddock car
park; lap shape as ProcTrack's generator: start straight, corners, a hairpin), a **rally loop** on forest tracks in the uplands, and a
**hill climb** up a mountain road to a pass. Free roam first; lap timing on a circuit later (our own HUD, not the game's race flow).
