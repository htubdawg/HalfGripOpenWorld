# Traffic against PA's rules of the road: the highway-fixes branch reviewed (2026-11-24)

The owner's complaint: the traffic sucks because the roads aren't up to the job and the NPCs don't understand right of way. This
compares `traffic-highway-crossing-fixes` (0.33.0 plus the unfinished fixes in `2c14c4f`) with the **Pennsylvania Driver's Manual**,
and lays out the owner's road design: **on and off ramps along the side of the trunk, and left-turn lanes in its median**. No
interchanges: it's a small rural area.

## Sources

- **PennDOT, *Pennsylvania Driver's Manual*, PUB 95 (4-21)**:
  https://www.pa.gov/content/dam/copapwp-pagov/en/penndot/documents/public/dvspubsforms/bdl/bdl-manuals/pa-drivers-manual-non-commercial/english/pub%2095.pdf
  (linked from https://www.pa.gov/agencies/dmv/driver-services/pennsylvania-drivers-manual). Page numbers below are the manual's
  printed ones (Chapter 3). Quotes are verbatim.
- **The branch's own numbers**: `trafficview --seed 1` (`TV_DRIVE_ONLY=1`) run on the branch this session.
- Lane lengths are worked out from the traffic's own `Accel` and `Brake`, not taken from a design manual (PUB 95 gives none).

**Handy:** 1 ft ≈ 1.0 game unit (0.3048 m × 3.3). 55 mph ≈ 81 units/s, so 9 s at 55 mph ≈ 730 units.

## What the manual says, and what the traffic does

### Who must yield (p. 48, "Negotiating Intersections")

> 2. Drivers turning left must yield to oncoming vehicles going straight ahead.
> 4. When two vehicles approach or enter an intersection from different roadways at or about the same time, drivers coming from the
>    left must yield to vehicles coming from the right.
> 5. A vehicle entering a public highway from an alley, private road or driveway must wait until the main road is clear.

Stop and yield signs (Ch. 2): stop or slow down, and give the right-of-way to cross traffic.

| Rule | Branch | Verdict |
|---|---|---|
| Left turn yields to oncoming going straight | Only where the main line is the trunk (`twoLaneMain`). Elsewhere `MainTurnsGiveWay` is false, so left-turners off primaries and secondaries go first come first served and **stop oncoming cars**. Node 233 on the Westhaven link: 58 min of main-line cars waiting a day. | **Breaks rule 2.** Make it unconditional. |
| Side road gives way to the through road | Per-junction main line; side arms give way. | Right. The gap taken is too short (below). |
| Same time, from different roads: yield to the right | Junctions with no main line: first come first served, the car on the right first within 1 s. | Right. |
| Driveway: wait until the main road is clear | Waits for 40 units and 3 s clear, **then after 20 s the car behind stops to let it in** (`LetInAfter`). | **Breaks rule 5** on any road with a speed limit; the manual's only courtesy is moving over for a car on an on-ramp (p. 52). Keep the let-in on 25 mph streets at most. |

### Gaps (p. 49, "Moving Into and Through Intersections Safely")

> To travel across an intersection with a two-way stop sign - You need about a 6-second gap in both directions to cross a 30 mph
> street ... To turn right - ... Allow a gap of about eight (8) seconds between you and any vehicle approaching from the left. ...
> To turn left - Make sure you have a clear gap to turn in front of oncoming traffic ... If you are turning from a minor onto a major
> street, allow a 9-second gap between you and any vehicle approaching from the right. ... Never start a left turn until you can see
> all the lanes you need to cross are clear, and you can safely make the turn.

The branch lets a side-road car go if it's **out of the box 1 s before** the main-line car gets there (0.6 s after 20 s of waiting,
`GiveWayMargin`/`GiveWayLeast`). Crossing the trunk from a standstill (a box of about 40 plus a car of 14, at `Accel` 5) takes about
4.6 s, so the gap it takes is about **5.2 to 5.6 s**. The manual says **8 s turning right and 9 s turning left**. At 55 mph that's
650 and 730 units of clear road, against the 420 to 450 the branch accepts. This is why trunk cars brake for joiners.

**Fix:** a side-road car goes only when the nearest main-line car whose way it crosses or joins is at least 8 s (right turn), 9 s
(left turn) or 6 s (across) from the junction, at that car's speed. Patience doesn't shrink it (the manual has no such thing). On the
trunk, the ramps and median lanes below take most of these movements out of the gap-taking altogether.

### Turning lanes and position (p. 45)

> To turn left on multi-lane streets and highways, start from the left lane. If you are turning right, start from the right lane. If
> you are turning onto a highway, which has more than one (1) lane in the direction you wish to travel, turn into the closest lane
> going in that direction. ... If you want to change to another lane, wait until after you have safely completed your turn.

> [Center turn lane] When you enter the center turn lane, enter it completely. Do not leave the back end of your vehicle sticking
> into the normal travel lane, blocking traffic.

Signal at least 300 ft before turning at 35 mph or more (p. 44). Do not pass within 100 ft of an intersection (p. 47).

Branch: on the trunk, left-turners move to the inner lane and right-turners to the outer lane within 700 units (`ExitReady`). Through
cars keep to the outer lane for 400 units before a trunk junction (`PocketAhead`), so a left-turner waits **in the inner through lane**.
That's what the manual says not to do: stop with your car in a travel lane. The trunk has no turn lane for it to wait in. That's
the road problem, not a rule problem.

### Not blocking, and stopping halfway (p. 46)

> Stopping halfway across a street is safe only when there is a turning lane provided in the center median large enough to hold your
> entire vehicle. ... Even if you have a green light, do not enter an intersection if there are vehicles blocking your way. Wait until
> you are sure you can clear the intersection before starting to cross it.

Branch: room past the box is checked by distance. A carriageway crossing on the trunk now also needs its way out moving or clear well
past the box (`CrossesCarriageway`). There were 0 cars stopped across a carriageway on seed 1. **Right.** The plain room check still let one
car stall in a box on seed 7. Apply the carriageway rule (way out clear for the whole car) at every junction.

### Divided highways (p. 49)

> Making a right turn onto a divided highway is no different from making a right turn at any other intersection. ... To correctly
> turn left onto a divided highway, you need to cross the "first" roadway when there is a safe gap in traffic approaching from your
> left, and then turn left on the other side of the barrier, when there is a safe gap in traffic approaching from the right on the
> "second" roadway.

A **two-stage** left: one carriageway at a time, waiting in the median between. The trunk's median (10 units, 14 with its shoulders)
can't hold a 14-long car crosswise, so by p. 46 a car may not stop there. Today it must find 9 s both ways at once.

### Entering and leaving a highway (pp. 46, 52)

> Entrance ramps for highways often have acceleration lanes. These lanes run beside main traffic lanes and should be long enough to
> allow you to accelerate to the speed of traffic before you enter the highway itself. ... It is illegal to pass a vehicle ahead of
> you in an acceleration lane. ... 1. Put your turn signal on, and look for an opening in traffic. 2. Accelerate up to the speed of
> traffic. 3. Merge into the opening in traffic.

> Get in the proper lane for exiting well in advance. Do not slow down until after you have moved into the exit lane. ... Do not
> swerve across several lanes to try to make the exit at the last second. Do not cross over the median to try to turn around.

> If you see a vehicle about to enter the highway from an on-ramp, a common courtesy is to change lanes ... Even though the entering
> driver is required to yield the right-of-way to you ...

> If you are the driver entering the highway, you must yield to the exiting driver. [in a shared weaving lane]

Branch: there are no ramps. Joiners start from a standstill at the T and need the trunk car to brake "firmly". Leavers slow in the
outer through lane. The trunk moving over for joiners (190 a day) matches the courtesy. **The roads are missing what the manual
assumes a highway has.**

### Following distance (p. 35)

> A 4-second following distance ... will allow you to steer or brake to avoid a hazard safely on the highway, if the pavement is dry.
> ... If you pass the marking or object in less than four (4) seconds, you are following too closely.

Branch: IDM time headway 1.5 s (`Headway`) plus 7 units. That's half the manual's 4 s. **Owner's call:** 4 s is the law-abiding
driver. It also halves what a lane carries (about 900 cars an hour instead of about 2,000), so queues get longer behind every
stop. 2 to 4 s is the sensible range; 4 s on the trunk is easy to try.

### Passing and keeping right (pp. 46 to 47)

Pass on the left, or on the right where there are two or more marked lanes each way; not within 100 ft of an intersection, bridge or
tunnel. If tailgated, "move over to the right lane, if possible" (p. 36). Branch: overtakes on the left and keeps right
(`KeepRightLoss`); lane changes clear of junctions. **Right.** Check that the 100-unit no-pass zone covers bridges and the wrap's tunnels.

## What the branch's day looks like (seed 1)

- Trunk through cars **stopped 134.9 min a day**; cars waiting to turn off 100.2 min. Worst: trunk edge 15 between node 33
  (secondary) and node 3 (Kingsbridge link), 1,313 apart, 178 min.
- 0 cars stopped across a carriageway (the new check passes). Stop lines on another road's paving: **7 of 2,244 arms (FAIL)**. Not run
  over the 16 seeds.
- The trunk carries 27,000 to 31,000 a day equivalent. Local traffic alone is 3,800; the rest is the 100 circling cars.

## The roads: the owner's design

**Off ramps (deceleration lanes)** on the right of each carriageway before every junction where cars turn right off the trunk. A car
moves into it first, then slows, as p. 52 says. Length from the traffic's own `Brake` (8.25 u/s², about 2.5 m/s², comfortable):
81 → 13 u/s takes (81² − 13²) / (2 × 8.25) ≈ **390 units**, plus a taper of about 100 to move over in.

**On ramps (acceleration lanes)** on the right after every junction where cars turn right onto the trunk (and where the left-turners
from the median come out, below). A car accelerates to the traffic's speed, then merges into a gap. It may not pass in it, and must
yield to the trunk (p. 46). From a standstill at `Accel` 5: 0 → 75 u/s takes 75² / 10 ≈ **560 units**, plus a taper of about 100.
Where an on ramp and the next off ramp would overlap, it's a weaving lane: entering yields to exiting (p. 52).

**Left-turn lanes in the median** at every junction where cars turn left off the trunk. The car moves in completely (p. 45: no tail in
the travel lane) and waits there for a gap in the oncoming carriageway, out of the through lanes. Storage of about 5 cars
(5 × (14 + 7) ≈ 105 units) plus a taper. The median and its shoulders are 14 units between the inner lanes, which already takes an
11-unit lane. Most junctions need only paint and a cut through the median, a little widening at most.

**Left onto the trunk from a side road (two-stage, p. 49):** the car crosses the near carriageway on a gap from its left, then waits in
the median before joining the far one. Two ways to give it that place:
- (a) widen the median at the junction to about 30 units so a car fits crosswise, and it waits for a 9 s gap from its right; or
- (b) a median acceleration lane: it turns into a lane on the left of the far carriageway and merges right into the inner lane
  (common on rural four-lane roads, but a merge into the fast lane).

**Spacing:** nodes 33 and 3 (1,313 apart), and node 10 with arms of 164 and 181, would have ramps and turn lanes overlapping. Where that
happens, either one continuous auxiliary lane between them (a weaving lane) or merge the junctions.

All of this is a world change: everyone in a room needs the new version.

## What to do

1. **Driving rules** (no world change; the day changes, so a version bump):
   - left turns yield to oncoming traffic everywhere (drop `MainTurnsGiveWay`);
   - the manual's gaps: 8 s right, 9 s left, 6 s across, no shrinking with patience;
   - no let-in for driveways off roads above 25 mph;
   - the full-clearance room rule at every junction;
   - following distance per the owner's call;
   - finish the branch: the 7 stop lines, the 16 seeds, physics cars (`HoldLane`) following the lane rule.
2. **The trunk's junctions** (world change): off ramps, on ramps and median left-turn lanes as above, built in `Network`/`RoadMesh`,
   with the traffic driving them as lanes of their own (the trunk's lane keys already do two lanes). Add checks: no car decelerates
   in a through lane for a turn, no car stops in a through lane to turn left, no trunk car brakes harder than about 2.5 m/s² for a
   joiner.
3. **Then** more circling cars and frame time.

## Owner decisions needed

- **Following distance:** the manual's 4 s, or something between (real traffic runs at about 1.5 to 2 s)?
- **Left onto the trunk from a side road:** (a) a wider median to wait in crosswise, or (b) a median acceleration lane?
- **Junctions too close for their lanes** (nodes 33/3, node 10): one weaving lane between them, or merge them?
