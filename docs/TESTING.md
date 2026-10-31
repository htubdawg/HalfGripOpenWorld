# Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

Last run (0.17.0, by the owner): 1, 7 to 12, 15 to 17b and 19 to 23 good. Not run that time: 2 to 6, 13, 14, 18 and 22b.

1. The race loads on the road into Kingsbridge, just outside the town, facing it; none of Tokyo's arena, city or train is left.
2. Drive along the trunk road and into Kingsbridge: tiles, roads and ground textures appear ahead before you reach them, with no gaps
   or seams between tiles. Where roads meet, the smaller one stops at the bigger one's edge with a flared mouth, level with it (no
   lip, no flicker, nothing poking through); the same where two streets cross. More junctions and dead ends: 24.
3. Cross rivers on bridges (concrete sides and parapets), small streams too: no water over the road anywhere. Follow a river and its
   tributaries, in the lowland and up in the mountains: the water's edge meets the bank everywhere, never hanging in the air with
   ground or sky showing under it. The bridge at world 0.208, -2.517 km (a main road in a cutting over a river in a gorge): no ground
   pokes through the deck at either end. Drive into a river or the sea and check you are put back on a road.
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
11. The hill climb (teleport to `Hill climb (start)`): drive up. Tarmac like a lane, no ground showing through it and no step at its
   edges or where cut into the hillside; the bends and the hairpin near the top have a real radius, no kink. The start arch stands
   clear of the road; the paddock beside the start straight lies flat with no bump onto the road. Turn round at the bottom and drive
   down the lane: no seam or step where the lane runs into the climb's foot. `Hill climb (finish)`: the finish arch, then the
   turning area beside the end of the road, flat and big enough to turn round on.
12. Road signs: drive into Kingsbridge from the spawn and out to a village. A white name sign stands on the left at the edge of each
   place, facing you, its name crisp and readable from a few car lengths, not mirrored, its back plain grey; none on the trunk road.
   At a junction of main roads (teleport to a village and follow its main road out), the fingerpost stands back from the road, each
   finger pointing along its road, readable from both sides (name at the left, miles at the right), none reaching over the road;
   brown fingers on the way to the circuit, the rally stage and the hill climb. Follow the fingers to a place: the miles count down.
   Out in the country, a fingerpost at most junctions of lanes and minor roads, outside the towns and villages.
   Posts are solid, no house or tree stands in a sign. The log has a `Road signs:` line.
13. Country lanes, tracks and footpaths: follow a few out from a farm or hamlet. Most run on to another road rather than stopping
   in a field; where one was carried on, no kink or step where the old road ends and the new one begins, and its far end meets the
   road it joins with a mouth like any other junction.
14. The trunk road along the coast (east of the spawn, and west towards the edge): no sea water drawn over it anywhere; it runs on a
   low bank above the shore.
15. Past 1 km the log says `Origin shift 1` and the overlay's scene position jumps back near 0 while the world position keeps
   counting. Watch for a visible hitch, a jump of the car or camera, or tyre marks and smoke left behind.
16. Teleport (F7) round the key places, several of them 10 to 15 km out. The car should drive exactly as at the spawn: no jitter in the suspension or camera,
   and the hand, cursor and cockpit controls as steady as at the spawn (the log's shift lines say how many cockpit objects were kept).
17. Hold R far out: the car is put back on its wheels where it was.
17b. Get wrecked (into a wall at speed, or the dev console's `kill player` or `hp 0`): `WRECKED`, the car burns, and after 3 seconds (`RespawnSeconds`)
   it is at the start, on the spawn's grid slot facing up the road, at full health, driving normally (it accelerates, no flames);
   `BACK AT THE START` shows. Get wrecked far out (teleport first): the same, and the ground round the start is there. The race does
   not end. Pause while wrecked: the respawn waits. Hold R: still resets in place. In a Multiplayer room set to Demolition or "out,
   watch", with AI RIVALS above 0: no AI cars on the grid; a wrecked player respawns after the room's time at the start, the others
   see the car come back there, and nobody is put out. Two players wrecked together, or one waiting on the grid: the second lands on
   the next slot back, not on top.
18. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
19. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
20. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
21. Day, night and storms (0.17.0), in a Multiplayer room. The log has `Weather: <day> by day, <night> by night (from the room's
   seed)` and `Sky: the race starts at hh:mm, a day lasts 40 minutes; storms: a ... day, wind ..., snow above ...`. With `ShowCoords`
   the overlay's last line gives the time, the sun's height, the rain, cloud, wetness and grip at the car and the nearest storm; both
   players should see the same time and, at the same place, the same storm. Turn on `WeatherSkip` and press the Weather skip key (F5) through a whole day (or toggle the Weather fast key, F2, for 10x)
   (8 presses a day): the shadows swing from east to west, the light goes gold at dusk, the sky darkens and the night sky comes in,
   the cockpit dims at night (the cabin is not lit at midnight), the headlights go on at dusk and off after dawn, and nothing jumps
   or flickers at the swaps (the light fades out and back round sunset and sunrise). Note whether the ambient light changes at
   all (the world's shadows go blue and dark at night): if it stays as bright as day, the probe override did not take. Drive
   towards the nearest storm: the sky and fog darken first, then rain starts and grows (the sound with it), the road darkens and
   shines, and the overlay's grip drops; drive out the other side: the rain stops, the road stays wet and dries over about 10
   minutes. In the rain, the drops reach the ground and splash on it,
   on the road and on buildings, on high ground and down in a valley alike, with no drops stopping or splashing in mid-air (the
   log says `Sky: rain and snow land on 1 collision plane(s)`). Teleport about (origin shifts) while it rains, and drive up and down
   a long hill in the rain: the rain stays, and the drops splash on the road round the car, uphill and downhill. Up in the northern mountains above the snow line it snows instead. Frame time with heavy rain (the graphics budget
   still cuts the rain on a low setting).
22. Garages, alone and in a Multiplayer room. The log says `Garages: 20 (...)`. The map (MiniMap) shows a big cyan dot (wider than the trunk road) at each
   filling station, workshop, the circuit's pits, the rally service area and the hill climb paddock (one dot for the three pits, and one where a filling station and a workshop face each other), and `GARAGE` floats over each
   circle within about 70 m. Drive into a circle: the line at the bottom says to stop and shift into neutral; do it and the garage
   opens when you let go of the shifter (standing in neutral still holding it does not, nor does shifting through neutral while rolling). While it is open the controls and the gun do nothing, the cursor is free,
   crashes, hazards and other players' shots do no damage, and the health shown stays put. Drag an item from the list onto a slot (the slots it fits light up), or right-click it: it is on the
   car at once (the new gun, wheel or shifter in the cockpit, the body's look outside), ammo and nitro full a moment later. Every
   gun (the game's and the modded M1911, Minigun, RPG, Sniper Rifle, Railgun) swaps and fires as at the start of a race, with its own
   held model; swapping away from the Minigun takes its roof gun off the car (seen from outside and by others) and leaves no remote,
   and fitting it again gives one roof gun, not two. In a room, an RPG fitted here fires rockets the others see fly and explode, and
   the Multiplayer mod's banned items (slow motion, rerolls) are not in the lists. Dragging a fitted item off its slot, or right-clicking it (in the slot or the list), takes it off; dragging one
   medium item onto the other medium slot swaps them; an item dropped on a slot it does not fit says so. Repair fills the health;
   Refill the ammo and nitro.
   Esc or Close closes it; it does not open again until you shift into gear or leave the circle. The next race starts with the
   loadout from the menu or the room, not the garage's (the log says `Garage: the race's own loadout is back`).
22b. Respawn at a garage (0.16.0), alone and in a Multiplayer room. The garage window's bottom right says `Respawn after a wreck: the
   start`. Press Respawn here: `A wreck comes back here`, the log says `Respawn point: <garage>`, the button reads Respawn at start
   and the bottom right `this garage`; another garage's window says `another garage`. Close, drive off, get wrecked: after the wait
   the car is in that garage's circle, facing out of the lot (along the pit lane to its exit in the pits), repaired, on its wheels,
   `BACK AT THE GARAGE`; the garage does not open until you shift into gear and back to neutral. In a room, with another player's
   car parked in that circle: you come back at the start instead. Press Respawn at start and get wrecked: back at the start. Wrecked
   in an event: still back at the last checkpoint. Start the next race (or a new run): a wreck goes back to the start again.
23. Events, solo (0.15.3). The log says `Events: Circuit 8.6 km, 27 gates, 3 laps; Rally stage 4.9 km, 16 gates; Hill climb 1.4 km,
   5 gates`. Out of an event the cockpit's board (top right) is tucked into the screen's right edge with a sliver showing; the hand
   over the sliver slides it out with the cars in the race (`cars 1` solo, `players N` in a room, on the lap counter), and it goes
   back when the hand leaves. An orange ring is in the circuit's pit lane past the last garage, in the rally service area and in the
   hill climb paddock (beside the garage's circle), off the course, with orange `EVENT` over it from up to about 70 m, and a
   chequered disc in an orange ring on the map (MiniMap) at each, a little larger than a garage's and over the one beside it. Drive
   into one: a panel like the garage's (`EVENT  Circuit`, the length, laps, checkpoints and best, `Stop and shift into neutral to
   start`) shows under the middle of the screen; the board stays tucked away. Stop in it in neutral and let go of the shifter (holding it in neutral does nothing): the panel goes, the car is put
   on the grid (pole on the circuit, just behind the start arch elsewhere), facing up the course, and the board slides out; the
   start lights hang over the start line facing the car, and go red, amber, both, then green with 3, 2, 1, GO in the cockpit's
   text, each on time. Before green, shift into gear and floor it: the car stays on its spot (the wheels may spin, the car should
   not slide or turn, and the log has no `Origin shift ... NaN`) and leaves on the green. An `Origin shift` logged just after
   `Event: signed on` (the grid is often far enough from the box for one) must leave the car on its spot. Drive the course: the first
   orange gate (posts and a bar, no collision) stands across the road ahead with the next one beyond it, each gate passed moves the
   pair on, the board counts the checkpoints (the lap counter the lap on the circuit, with each lap's time on the board), the finish
   gate is white. Cut a corner past a gate: `MISSED A CHECKPOINT`, the run finishes with no best time. A clean run: `NEW BEST`, and
   `halfgrip.openworld.cfg` has the time under `[Best times]`; a slower one shows the gap in red. Get wrecked mid-event: after the
   respawn the car is just past the last gate passed, facing on along the course, `BACK AT THE LAST CHECKPOINT`, and the clock ran
   on. Drive far off the course: `BACK TO THE COURSE`, then `EVENT OVER` after 10 s; teleport mid-event: `EVENT OVER`. Pause: the
   clock stops. In a Multiplayer room the same works for yourself.
24. Junctions and dead ends (0.17.1). With `ShowCoords`, drive to these (world km) and look round each, from the car and from
   outside: no ground, grass or verge showing in a mouth, no wedge of ground where a road meets another at a slant (on either side),
   no sidewalk or verge lying across a mouth, no step or flicker where the surfaces meet, nothing of one road poking through
   another.
   - A street meeting a street at a slant in Westhaven (11.482, -10.303), and Kingsbridge's streets round (-1.802, -4.088).
   - Two farm lanes carrying on end to end (-9.333, -9.646): one road round the bend, no notch or seam, the verges unbroken.
   - Main roads ending in a village, their streets carrying on: Northcote (-1.486, 5.487), Kestrel (12.082, -4.155), and
     (7.710, -4.893). The main road narrows smoothly into the street, no square end standing out across it.
   - Redford's middle (-6.838, -5.281), where two main roads and two streets end at one place.
   - Villages where three or more roads end at one point (0.17.2), by the teleport: Ashby, Carrow, Elmsted, Kestrel, Millbrook
     (and Westhaven's centre). One plain asphalt plate joins them: each road stops square at its edge, the corners between them
     curved with the sidewalk or verge carried round, no ground in the middle, no road's square end across another, no step
     where a road meets the plate. At Kestrel the main road's old hook west of the village is gone (grass there).
   - A farm track meeting a lane that meets the trunk road (-12.822, 2.269): the track is under the lane's surface, not over it.
   - Dead ends: a Kingsbridge street (-2.135, -3.547) and a lane (-7.894, 4.300) end rounded off, the sidewalk or verge carried
     round the end down to the grass (no square edge standing over the ground); a farm track (-0.282, 2.578) narrows to a point
     into the grass. Drive off each end: no bump.
25. The painted sky (0.18.0). With `VerboseLog` the log says `Sky: painted sky on a copy of FS002_Rainy (Skybox/Panoramic)` (or
   another panoramic sky), and there is no `could not make the painted sky` or `painted sky stopped` warning. With `WeatherSkip`, the
   Weather fast key (F2) or skip key (F5), go through a day: by day a blue sky, paler at the horizon, with a white sun disc and a
   glow round it, standing where the shadows say the light comes from (shadows point away from it) and moving with them; the sun
   goes orange and the sky round it gold and red as it sets in the west, the disc going down behind the horizon; the sky goes deep
   blue, then black, the stars come out (small pixel squares, not stretched, also straight overhead) and turn slowly, and the moon
   (a grey disc with darker patches) rises in the east opposite where the sun set and crosses the sky. Nothing jumps: no cut between
   a day sky and a night sky. The distant ground in fog meets the sky at the horizon without a band of a different colour, at every
   hour. A few clouds by day (more on a wet day, see the log's `showery`/`wet`), which slide across slowly and stay over the same
   land as you drive (drive a kilometre: they pass overhead); under a storm the whole sky is grey cloud and the sun and stars are
   hidden. Look straight up and all round: no seam or smear in the sky (the picture's edges meet in the west), no stretched stars at
   the top. The frame rate is as before (the painting costs about 0.7 ms a frame).
26. The far view and storms seen from afar (0.19.0). The log says `Far view on (the world's grid built in ... ms)` within a few
   seconds of the race starting, and no `Far view failed` or `chunk ... could not be made` warning. On a clear day (`ShowCoords`: cloud
   near 0), from the spawn and from the mountain pass and the hill climb's finish (teleport): the country goes on past the loaded
   ground to the horizon, fading into the sky's colour with no band between: the mountains in the north with snow and rock, the woods
   standing up dark green, fields, lakes and the sea in the south going on past the coast to the horizon, the main roads as thin grey
   lines. Where the loaded ground ends (about 1.3 km out with TileRadius 5) there is no gap, no sky showing through, no step, no
   flicker; buildings and trees appear there (that is expected). Drive a few kilometres fast and teleport: the far ground follows
   with no hole opening for a frame and nothing poking through the road or the ground near the car. Far mountains and coast do not
   flicker or shimmer (z-fighting at a distance would show as stripes; report a screenshot if so). Under cloud and in the rain the
   view closes in smoothly (a few hundred metres in a downpour) and opens again as it clears. With `FarView` off in the cfg, the fog
   ends at the loaded ground as before. Storms (a showery or wet day in the log; `WeatherSkip` and F2 to find one): a storm some
   way off shows as dark lumpy cloud over its place, grey at its edge, with grey streaked rain hanging under it down to the horizon;
   it grows as it comes, and it is overhead when the rain starts; one behind you is behind you (turn round). Away from storms the
   sky is as in 0.18.0. The frame rate is about as before (with `VerboseLog`, `Tiles: ... ms each` unchanged).
   In the free-flight view too (0.19.1): the country goes on past the loaded ground to the horizon. With `ShowCoords`, the last line
   says `far view: N chunks, fog A to B, <camera> draws to C` with C a little over B (thousands on a clear day), in every view.
27. The land past the far view painted into the sky (0.20.0). The log says `Sky: the land past the far view painted into the sky (N
   texels, M ms a build)` a few seconds into the race, and no `the land past the far view stopped`. On a clear day (`ShowCoords`:
   cloud near 0; its last line now `fog A to B, <camera> draws to C` with C about `FarViewDistance` and B up to 40000), from the
   highest road near the coast looking north and from the spawn: the mountains in the north stand on the horizon past where the far
   view ends, hazy and lit from the same side as the near hills, their snow lighter. Where the far view ends (about 14 km out) there
   is no line, no band of sky, no step in brightness or colour; take a screenshot of it from high ground. Drive along: the far
   mountains keep their places (they do not slide with the car) and do not flicker. The near country is a little clearer than in 0.19.
   At sunset and at night the painted land darkens with the meshes. A storm between you and the mountains hides them behind its rain.
   As the rain closes in, the far mountains go first, smoothly. Look south from high ground over the sea: the sea goes on past the far
   view's edge with no seam (its shine there is a guess; report a screenshot if the far sea is brighter or darker than the near).
   With `FarSkyline=false`: the fog ends at the far view's edge as in 0.19.1. The frame rate is about as before.
