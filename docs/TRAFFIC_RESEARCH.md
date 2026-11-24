# Traffic against the real world: the highway-fixes branch reviewed (2026-11-24)

The owner's complaint: the traffic sucks because the roads aren't up to the job and the NPCs don't understand right of way. This
compares `traffic-highway-crossing-fixes` (0.33.0 plus the unfinished fixes in `2c14c4f`) with how real roads are built and how
Pennsylvania drivers are told to behave, and says what to do.

**Handy fact:** a game unit is 1/3.3 m and a foot is 0.3048 m, so **1 ft ≈ 1.0 unit**. Engineering figures in feet can be used as
units directly. 55 mph is about 81 units/s.

## Sources and how much to trust them

- **FHWA, *Alternative Intersections/Interchanges: Informational Report* (FHWA-HRT-09-060)**, chapters 3 (median U-turn) and 4
  (restricted crossing U-turn / J-turn): https://www.fhwa.dot.gov/publications/research/safety/09060/003.cfm and `004.cfm`. Read
  this session.
- **PA Driver's Manual (PUB 95) and the Vehicle Code (75 Pa.C.S.)**: the PennDOT hosts were added to the environment, but the fetch
  tool still could not reach them and a direct download was refused. The rules below are the well-known ones from the manual and the
  code, **written from memory, not quoted**. Check the section numbers against the manual before quoting them to anyone.
- **HCM gap-acceptance figures and AASHTO lane lengths**: standard textbook values, **from memory** (the FHWA pages read don't give
  them). They're good enough to size things in a game. They aren't citations.
- **The branch's own numbers**: `trafficview --seed 1` (`TV_DRIVE_ONLY=1`) run on the branch this session.

## What the branch does now (seed 1)

| | Branch | Real road at this design |
|---|---|---|
| Trunk volume (AADT-equivalent) | 27,000 to 31,000 (local traffic alone 3,800; the rest is the 100 circling cars) | PennDOT principal arterials: median 5,400 |
| Trunk through cars stopped | **134.9 min/day** (going on along it) | ≈ 0 at a junction with turn bays; never on an expressway |
| Trunk cars stopped waiting to turn off | 100.2 min/day | They wait in a turn bay, out of the through lanes |
| Worst stretch | Trunk edge 15, between node 33 (a secondary) and node 3 (the Kingsbridge link), 1,313 apart: 178 min | Junctions on a 55 mph arterial are spaced ¼ mile (1,320 ft) or more apart between median openings |
| Cars stopped across a carriageway | 0 (the new check passes) | 0 |
| Stop lines on another road's paving | 7 of 2,244 arms (**FAIL**) | 0 |
| Left turns off non-trunk main roads | **First come first served**: they still cut in front of oncoming cars | Yield to oncoming traffic, always |
| Gap a side-road car takes | Out of the box 1.0 s before the main-line car arrives (0.6 s after 20 s of waiting) | HCM critical gaps: 6.2 to 7.5 s headway (below) |

## Right of way: PA rules against the code

| PA rule (manual / 75 Pa.C.S., from memory) | Branch | Verdict |
|---|---|---|
| **Left turns yield to oncoming traffic** (§3322) | Only where the main line is the trunk (`twoLaneMain`). Everywhere else `MainTurnsGiveWay` is false, so a left-turner off a primary or secondary road goes first come first served and **stops oncoming cars**. That's why node 233 on the Westhaven link has 58 min of *main-line* cars waiting. | **Wrong**, and it's the owner's complaint. Make it unconditional. |
| **Side road / stop / yield: yield to traffic on the through road** (§3323) | The main line is worked out per junction, and side arms give way to it. | Right idea. The gap they accept is far too small (below). |
| **Uncontrolled junction: the first to arrive goes first; at the same time, yield to the car on the right** (§3321) | Junctions with no main line work this way. | Right. |
| **Entering from a driveway, alley or parking: yield to everyone on the road** (§3324) | Pulling out waits for 40 units clear and 3 s, and after 20 s the car behind lets it in. | Roughly right. Being let in after 20 s is a courtesy, not a rule. Fine on streets, but it should never happen on a 45 mph+ road. |
| **Turn right from the right lane, left from the leftmost lane** (§3331) | On the trunk, the branch moves left-turners into the inner lane within 700 units (`ExitReady`). Elsewhere roads have one lane each way. | Right on the branch. Not true in 0.33.0. |
| **Keep right except to pass** on multi-lane roads (§3313 / §3301) | Keeps right (`KeepRightLoss`), overtakes on the left. | Right. |
| **Don't enter a junction unless there's room to clear it** (manual) | Room checked by distance only, plus the branch's stricter check for carriageway crossings. | Mostly right. The distance-only room check still let a car stall in a box on seed 7. |
| **Merging onto a highway: yield to its traffic, match its speed in the acceleration lane; highway drivers move over if it's safe** (manual) | There are no acceleration lanes. A car joins the trunk from a standstill at the T and needs "room for that car to slow, braking firmly". The trunk moves over for joiners (190 a day). | The behaviour is fine. **The road is missing the acceleration lane**, so every join makes a 55 mph car brake. |
| **Following distance: at least 4 s** (manual) | IDM headway 1.5 s, gap 7 units. | Typical real highway headways are 1.5 to 2 s, so this is fine for a simulation. The 4 s rule is advice. |

### Gap acceptance (HCM two-way stop control, from memory)

Critical headway in the main-line flow that a driver accepts (two-lane / four-lane major road), and the follow-up headway for the
next car in the queue:

| Movement | Critical gap | Follow-up |
|---|---|---|
| Left turn off the major road | 4.1 s | 2.2 s |
| Right turn from the side road | 6.2 / 6.9 s | 3.3 s |
| Straight across from the side road | 6.5 s | 4.0 s |
| Left turn from the side road | 7.1 / 7.5 s | 3.5 s |

At 55 mph a 7.5 s gap is about 600 units of empty road. The branch lets a car go if it is **out of the box 1 s before** the main-line car
arrives. Crossing 4 lanes from a standstill (a box of about 40 plus a car of 14, at `Accel` 5 u/s²) takes about 4.6 s, so the branch's
effective gap is about 5.6 s, falling to 5.2 s with patience. That is shorter than any real driver takes across a four-lane highway, and
it is why the trunk's cars brake for joiners.

**What that costs, by HCM capacity:** with about 850 veh/h on both carriageways (the circling cars in real time), a side-road left
turn across the trunk can serve about **250 veh/h**. A left turn off the trunk (4.1 s against about 425 veh/h oncoming) can serve about
**1,100 veh/h**, but only if it has somewhere to wait that isn't a through lane.

## The roads: what's wrong with the trunk's junctions

The trunk (`Network.Classes[0]`) is a 55 mph dual carriageway, two lanes each way, with a **flat median 10 units wide (about 3 m)**.
Every road meets it in a plain at-grade T or crossroads (boxes of 36 to 41 units). It has:
- **no left-turn bays** (the median can't hold a car, which is 14 long and needs at least 47 ft of median to wait in: FHWA ch. 4);
- **no median refuge**, so crossing or turning left onto it is one movement across all four lanes, needing gaps in both directions at
  once;
- **no deceleration lanes**, so a car turning right slows from 81 u/s in the through lane;
- **no acceleration lanes**, so a car joining from a standstill takes about 16 s and 650 units to reach 55 mph (AASHTO wants about
  960 ft of acceleration lane for 55 mph from a stop, from memory);
- **junctions close together**: nodes 33 and 3 are 1,313 apart, and node 10's arms are 164 and 181 long.

**No real road carrying this traffic is built like this.** PennDOT puts a road of about 30,000 a day at 55 mph on interchanges (US 22,
US 322, US 15 are the models). Where at-grade junctions stay on a four-lane divided highway, FHWA's answers are:
- **Turn bays in a wide median**: 47 to 71 ft desirable, 250 ft or more of bay. Each car turning left waits off the through lanes.
- **RCUT / J-turn** (FHWA ch. 4): side roads may only turn right. A car wanting left, or across, turns right, then makes a U-turn at a
  one-way median opening 400 to 1,000 ft downstream (Maryland's unsignalized J-turns on US 301: about 1,400 ft; NCDOT minimum 800 ft).
  This removes the crossing-four-lanes movement entirely. Crashes fell 31% (US 23/74) and 90% (US 301 at MD 313). It suits junctions
  where the side road carries under about 20% of the total. That holds here at every trunk junction outside the peak.
- **Signals**: a car game's highway shouldn't have them.

**The 3 m median is the root of the owner's screenshot.** The branch's workaround (left-turners wait in the inner lane, through cars
keep to the outer one for 400 units) is how a four-lane *undivided* road with no turn lane behaves. It's legal, but it's exactly why
real engineers add turn lanes: through cars still stopped for 135 min a day on seed 1.

## The traffic itself

- **The circling cars are 88% of the trunk.** That volume was chosen for the drive (the owner wants more, step C of the perf plan:
  300 or 1,000), not from counts. More through traffic only works if the junctions stop needing gaps across it, which is the road fix
  below. At 300 circling cars, the side roads could hardly join (3,606 min of junction waiting).
- **The day runs at 36x**, so each town's whole peak hour arrives at its trunk junction in under two real minutes. Real junctions
  never see that rate, which is one more reason the trunk's joins must be free-flowing (merges and grade separation), not gap-taking.

## Verdict on the branch

**Keep (it matches the rules):** left turns off the trunk yield; left-turners move to the inner lane and right-turners to the outer;
the room check for carriageway crossings (no car stops across the highway); wider boxes for slanted roads (`ObliqueBox`); the new
trafficview checks.

**Not good enough:**
1. **Left turns yield only on the trunk.** Every other main road still lets left-turners cut across oncoming cars. Fix: make
   `MainTurnYields` unconditional (drop `MainTurnsGiveWay`). On two-lane roads the oncoming flow is light, so queues behind
   left-turners will be short. Measure it.
2. **Gaps are about half of real.** Fix: the side car's test becomes "the main-line car is at least *critical gap* seconds from the
   conflict point" (7.5 s left / 6.5 s across / 6.9 s right on the trunk; 7.1 / 6.5 / 6.2 on two-lane roads), with a small cut for
   patience, instead of "out 1 s before it arrives". Joining a lane, it should also never need the main-line car to brake harder than
   about 2 to 3 m/s² (the branch allows firm braking).
3. **Through cars on the trunk still stop** (135 min), because the road has nowhere for turning cars to be. No driving rule fixes
   that.
4. **Stop lines still fail** on 7 arms (slanted lanes at the 75 cap), and the branch was never run over the 16 seeds.
5. **Physics cars** keep the lane they had when made physical (`HoldLane`), so near the player a left-turner may be in the outer lane
   and cut across the inner one. Check `TrafficDrive` against the new lane rule.

## What to do (recommended order)

**1. Driving rules (no world change; the day changes, so a version bump):** 1 and 2 above, finish 4, check 5. Then run the 16 seeds.
This alone fixes the owner's "doesn't understand right of way" on every road but the trunk's junctions.

**2. The trunk's junctions rebuilt (a world change, so everyone in a room needs the new version):**
- **Recommended: grade-separated junctions where the town links meet the trunk** (Kingsbridge node 3, Westhaven node 5, node 10,
  node 33, and whichever others carry a town). Use a simple diamond: the side road bridges the trunk (the network already does bridges,
  and the traffic graph already makes no junction where roads cross more than 4 apart), with four one-way ramps. Each ramp is a merge
  with an acceleration lane of about 1,000 units, or a diverge with a deceleration lane of about 500. Through traffic never stops.
  This is what a PA road carrying this traffic looks like, and it's the only layout that takes 300 or 1,000 circling cars.
- **Cheaper alternative: RCUT / J-turns everywhere else on the trunk** (farm lanes, minor roads). Side roads become right-in,
  right-out with a short acceleration lane. One-way median U-turn openings about 1,000 to 1,400 units each side, with a bay. That
  needs the median widened locally (a loon or bulb-out, about 50 units) only at the U-turn openings.
- Space junctions at least 1,320 units apart along the trunk (FHWA's access-management figure for principal arterials). Merge or
  drop the ones closer than that (node 33 next to node 3).

**3. Then raise the circling cars** (the perf plan's step C), and re-measure frame time.

**Owner decisions needed:**
- The trunk's junctions: **interchanges** at the town links plus J-turns elsewhere (recommended), J-turns only, or turn bays only?
- Can junctions on the trunk closer than 1,320 units apart be merged or cut?
- Are you OK with the day changing (a version bump; everyone in a room updates)?
