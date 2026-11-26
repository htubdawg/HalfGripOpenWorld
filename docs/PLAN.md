# Plan

Long-term goals and the owner's standing decisions. What is being worked on now, and the next step, is in `docs/HANDOFF.md`. Finished
work is in git history (this file held it until the 2026-11-25 cleanup), and finished research is in `docs/research/`. Each PR runs
`tools/check.sh`, `tools/preview` and `tools/meshcheck` before it goes up, and adds checks for what it fixes. Keep this file to goals:
when one is done, delete it here.

## Standing decisions (don't reopen)

**General**
- Copy or call no code from the Multiplayer mod or anyone else's (`RemoteCar`, its smoothing, ...): our own, written from the idea, so
  the mod is free of any licence. Its source may be read (`github.com/4l3x4nd3r0/halfgripmods`, `mods/Multiplayer/src`).
- The mod's messages go over HalfGripTools' `ctx.Net`, never the Multiplayer mod's AI sync.
- Sounds reuse the game's own clips; never synthesize a sound.
- The room's host settles anything shared (events, deer, traffic); the host never changes (the room ends when it leaves).

**Traffic**
- Follow the PA Driver's Manual (PUB 95; `docs/research/TRAFFIC_RULES.md`). Drive on the right (Pennsylvania).
- No roundabouts, ever.
- Following distance 2 s at most (now 1.5 s, `TrafficDay.Headway`).
- Right of way, not first come first served: main roads (and the obvious through road among equals) never stop for side roads. No
  stop signs needed; painted lines or signs later, as looks only.
- Every moving car near the player stays a physics car, oncoming ones included. Never propose limiting physics cars by direction or
  by whether the player is closing on them (refused twice).
- Traffic lights: their logic is built and they are drawn, American style (mast arms, yellow heads; the owner asked on 2026-11-25). Ask before adding lights anywhere new.
- A wreck is not a kill (no `EnemyKilled`, combo, lifesteal); traffic cars never get modded items or bodies.
- **No car is ever hidden** (the owner, 2026-11-26: "The cars cannot get hidden that is unacceptable"; it overturns the rule of
  2026-11-23 that hid a late car overlapping another). A drawn car with something in its way the day doesn't know of (a physics car, a
  late car) slows and waits behind it as a driver would, and runs late. Only a wreck goes (it burns and explodes, as the game's do).
- The highway (the owner, 2026-11-25): no overpasses or underpasses anywhere (no interchanges); at-grade junctions, traffic lights
  where needed are fine. "The median is not meant to be used as a lane": no median lanes to merge from, no cars standing in the
  median; dedicated left-turn lanes, with left-turn arrows where there is a light. The median stays mostly open so players can cross
  into the oncoming traffic at will; K-rails here and there; a wide grass median. Keep the general route (it may change a little)
  and the wrap.
- Kingsbridge's node 6 (the owner, 2026-11-25: "install the actual turn lane"): a real left-turn lane, the road widened and painted,
  with a left arrow at its light (since 2026-11-26 a flashing yellow arrow only: a green arrow made the average trip stand 38 s
  against 31.5 s; the owner, 2026-11-26: keep the flashing yellow only). Turn lanes go "everywhere it makes sense to have one" (the owner, 2026-11-26): decided by the
  engineering and the numbers, not asked about one by one. "There is no possible way that having uncontrolled intersections is
  optimal" (the owner, 2026-11-26): more turn lanes and control; four-way stops are fine (a stop sign model later).
  Shown that the US rules (MUTCD) warrant no new light or four-way stop at the game's counts, the owner chose "both, Kingsbridge
  first": fix Kingsbridge's queues first, then try lights and four-way stops at more junctions, keeping each that doesn't make the
  per-trip waiting worse.
- No centre turn lanes (two-way left-turn lanes): "those are extremely dangerous and hard to read" (the owner, 2026-11-26). Turn
  lanes are separate bays at each junction, never running back through the junction behind. Next: turn lanes on the ways into
  Kingsbridge's light (a right-turn lane for the cars coming up from the trunk turning to Pinewick; the Pinewick road split left and
  right at the light), then left-turn bays at the link's other junctions where the gap allows, each kept only if it helps.
  A turn lane may run back through the junction before it, marked as a through lane there ("we can have the left lane extend beyond
  a junction if its marked as a left/straight lane at the first one", the owner, 2026-11-26). "I just want the traffic to work and
  the roads to feel realistic": the engineering is ours. Signs (stop signs, lane-use signs and arrows) to be modelled later: keep a
  list of what each change needs (docs/HANDOFF.md).
- Act on the whole network, not junction by junction (the owner, 2026-11-26, late night: "Do we just need to act on a wider area
  instead of addressing each junction individually?"). Routes take the waiting into account: the day may be built twice (or more),
  drivers re-routing round the first pass's queues ("the load-time is already very short"). The Kingsbridge link (the main town's
  road down to the trunk) gets two lanes each way ("we can just go ahead and add another lane as well"). The junction-by-junction
  turn-lane plan above is superseded by these; the southbound left lane through the junction before the light was tried and made
  the waiting worse (docs/HANDOFF.md). Done (2026-11-26): the day is built twice, half the trips of the second pass routed round the
  first pass's queues by the hour (`TrafficDay.BuildPasses`, `RerouteShare`); a car kept out of a gridlock for three minutes takes
  another way. The second lane each way was put to the owner again (the waiting moved to side lanes); they chose four-way stops or
  lights where they help. Both paused for the owner's report of cars standing still for no reason (docs/HANDOFF.md): fix that first.
- Judge traffic changes by how the waiting is shared (the owner, 2026-11-26): waiting spread fairly over everyone is fine; the same
  total moved onto other drivers who now wait longer is not. Report averages per trip, in plain words.
- Right on red (the owner, 2026-11-25: "keep right on red when it helps"): on only where it helps. Measured over 16 seeds it did not
  help, so it is off (`TrafficDay.RightOnRedOn`); measure again after traffic changes (trafficview `TV_ROR=1`).

**Deer** (all in 0.28.0 to 0.31.0)
- Where deer live is drawn fresh each race, like the weather (from `Weather.Seed`, salted), not fixed with the map.
- The machine whose car, gun or kill startles a group decides the reaction and claims it; the host keeps the first claim. Each machine
  tests hits only for its own car and shots only for its own gun.
- Deer never go into water deeper than their belly (about 2 units), off drops, or along other roads. They flee only along tested
  legs and cross only at their crossing place.
- A hit damages the car; deer can be shot. Ask if the damage feels like too much.
- `tools/deer/deer.json` is frozen: the owner's painted coat (`OpenWorld/textures/deer.png`) is on its UVs. Don't reshape it.
- One set of antlers for every buck (a classic eight-pointer, not procedural). A fawn is the doe shrunk, and rare (no model of its
  own, no spots).
- No debug key to stand a deer in front of the car. A teleport to just short of a crossing place may be added later.

**Performance**
- The goal: about 130 to 140 fps (the owner's cap) on a 265K and a 3060, with ten times the highway traffic or more.

## Goals

**The highway (next; see `docs/HANDOFF.md`)**
- Rework the trunk road's layout completely.
- Then more circling cars, in measured steps (100 now; 300 chokes the side roads' joins today).

**Traffic**
- Turn lanes on the towns' roads (agreed 2026-11-24, not started).
- Stage 2's leftovers: lamps and a painted pool of light for the cars past the nearest 6. A car whose kerb space is down the street
  drives along to it a little fast. Moving traffic drives past parked cars at the kerb, not round them.
- Eight short town streets (three in Kingsbridge, five in Westhaven, about 110 houses) are joined to nothing but footpaths, so traffic
  leaves their houses out (trafficview lists them). Join each to the nearest road in `TownPlan`, or don't lay it; add a meshcheck
  check.

**Roads and towns**
- A few lanes meet another at a shallow angle, leaving a sliver of ground or verge in the narrow corner: (9301, 9211), (-6569,
  -9954), (-7272, 1156); preview's `square` lists two. Offer to move their ends, with the owner's screenshots.
- Towns' PR C (if wanted): schools and pubs, back alleys, a village green for the bigger villages.
- Street furniture: street lamps (Tokyo's), guardrails on the trunk road.

**Events**
- A ghost of the best run, and split times against the best at each gate.
- AI rivals, including for a lobby of one.
- Ghosting players outside an event who are near it; a kick for the host if needed.
- The map marks' states (the marks are painted into the tiles' map pictures, which MiniMap reads; a change would repaint those
  tiles). To agree with the owner.

**Sky and far view**
- The moon's phases (it is always full: it sits opposite the sun).
- Towns and towers as shapes in the far view (only their colours now), lit windows at night, far water that shines.
- Far view costs left from the 0.20.1 review: the tile edges' dips from the tiles' own heights, a chunk's vertices kept when only its
  holes change, the picture freed once uploaded, `Follow`'s per-frame tile lookups, the finest detail's reach following
  `TileRadius`, detail hysteresis.
- Collider baking off the main thread (`Physics.BakeMesh`) if tile building shows in the frame time.

**Deer (ideas: offer them, don't build them unasked)**
- Reactions to other players' cars are decided on those players' machines (fine while everyone runs the mod).
- Deer walk through trees in the woods (trees are per tile, not in the generator's functions).
- Fawns' spots or proportions, lying down, antlers by season or age, a buck's tucked tail or heavier neck, fawns keeping close to the
  doe when they run.
- The antlers as a target for shots (a shot through them misses now).
- A picture of a group's escape routes in `preview`.

**MiniMap**
- Streets are drawn grey and bridges in their road's colour (world.png draws them red). Change `Streamer.MapRoads` if the owner asks.
