# Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

1. The race loads on the road into Kingsbridge, just outside the town, facing it; none of Tokyo's arena, city or train is left.
2. Drive along the trunk road and into Kingsbridge: tiles, roads and ground textures appear ahead before you reach them, with no gaps
   or seams between tiles. Where roads meet, the smaller one stops at the bigger one's edge with a flared mouth, level with it (no
   lip, no flicker, nothing poking through); the same where two streets cross.
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
18. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
19. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
20. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
21. The log's `Weather:` line, and that the weather looks right (rain and snow follow the car).
22. Garages, alone and in a Multiplayer room. The log says `Garages: 20 (...)`. The map (MiniMap) shows a big cyan dot (wider than the trunk road) at each
   filling station, workshop, the circuit's pits, the rally service area and the hill climb paddock (one dot for the three pits, and one where a filling station and a workshop face each other), and `GARAGE` floats over each
   circle within about 70 m. Drive into a circle: the line at the bottom says to stop and shift into neutral; do it and the garage
   opens (shifting through neutral while rolling does not). While it is open the controls and the gun do nothing, the cursor is free,
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
