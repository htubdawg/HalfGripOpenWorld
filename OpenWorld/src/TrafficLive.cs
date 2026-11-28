using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic of a race, live (the rework's phase 5, <c>docs/TRAFFIC_REWORK.md</c> §6): the race's plans (<see cref="Schedules"/>), every
	/// car simulated all race long by the meso (<see cref="Meso"/>) and, round the player, by the micro (<see cref="Micro"/>), from the race's
	/// start hour on its clock; where each car is for drawing; where each parked car stands (<see cref="TrafficParking"/>).
	///
	/// <para>Built on a worker at the race's start (<see cref="Build"/>, about a second and a half), then stepped on the main thread a meso step
	/// (a quarter second) at a time as the race clock passes (<see cref="Advance"/>). After each step every car's pose is kept with the one
	/// before it, so <see cref="Pose"/> draws each car smoothly between the last two steps, a step behind the clock: the simulation moves in
	/// steps, the drawing every frame. Nothing is recorded ahead: a car held up is simply late. No Unity types (the game's side is
	/// <see cref="TrafficCars"/>).</para>
	/// </summary>
	internal sealed class TrafficLive
	{
		/// <summary>What a car is doing in a pose: not drawn (beyond the map, in a tunnel, between plans), driving, or parked.</summary>
		internal const byte None = 0, Driving = 1, Parked = 2;

		/// <summary>At most this many meso steps a frame: a hitch is caught up over the next frames rather than all at once (5 s of the race).</summary>
		private const int MostSteps = 20;
		/// <summary>The next day's plans are begun on a worker this long before the day's end (3 hours of the clock, 5 minutes of the race; they take about a second).</summary>
		private const double PlanAhead = 300.0;

		internal readonly Schedules Plans;
		internal readonly Meso M;
		internal readonly Micro Mi;
		internal readonly TrafficParking Parking;
		internal readonly int Seed;
		internal readonly double Start;

		/// <summary>
		/// The day the clock is in (0 the race's; a race running past 03:00 goes on into the next, <see cref="Meso.NextDay"/>) and that day's
		/// plans (for the overlay: a weekday or the weekend). The next day's are made on a worker ahead of its start.
		/// </summary>
		internal int DayIndex;
		internal Schedules Today;
		/// <summary>Not state (<see cref="TrafficState"/>): a loaded traffic begins the next day's plans again itself when it is due.</summary>
		[NonSerialized] private System.Threading.Tasks.Task<Schedules> nextDay;

		/// <summary>The player's car (world x, z), given to the micro as its one player; none before the race has a car.</summary>
		internal double PlayerX, PlayerZ;
		internal bool HasPlayer;

		/// <summary>The player's car as the micro's drivers see it (they stop behind it, follow it, keep out of a junction it blocks); none: unseen.</summary>
		internal Micro.Body PlayerBody;
		internal bool HasBody;

		/// <summary>Other bodies on the roads the drivers see: physics cars gone from their day (wrecks, cars knocked off their line) where they lie.</summary>
		internal readonly List<Micro.Body> OtherBodies = new List<Micro.Body>();

		/// <summary>Per car its pose at the step before last (0) and the last step (1): middle, way faced, road height (scene), what it is doing.</summary>
		private readonly double[] x0, z0, hx0, hz0, y0, x1, z1, hx1, hz1, y1;
		private readonly byte[] k0, k1;
		private double t0, t1;

		/// <summary>Per car: the parked spot it was given and the stop it was parked at for it (-1: not parked).</summary>
		private readonly TrafficParking.Spot[] spots;
		private readonly int[] parkedStop, parkedPlace;

		/// <summary>
		/// Per car: its way out of its spot or into it while it drives it (<see cref="Manoeuvre"/>; null otherwise), since when (day seconds),
		/// and whether it is the way in. A car pulling out is held at its place in the lane meanwhile (<see cref="Micro.PullOutTime"/>).
		/// </summary>
		private readonly Manoeuvre[] man;
		private readonly double[] manSince, manY;
		private readonly bool[] manIn;
		/// <summary>The cars driving a way out or in now.</summary>
		private readonly List<int> manoeuvring = new List<int>();
		/// <summary>Per car that has just pulled up at its stop: where it stood (middle, nose) and when, to drive into its spot from once it is given one.</summary>
		private readonly double[] upX, upZ, upHx, upHz, upY, upAt;
		private readonly bool[] pulledUp;
		/// <summary>Per car: its spot was given it before it arrived (<see cref="ArriveAlong"/>), so it is held while it is still driving.</summary>
		private readonly bool[] early;
		/// <summary>
		/// Per car: held still where it is drawn (<see cref="Freeze"/>), its loose body standing in for it (the contact physics): its way out
		/// or in and its wait to drive in don't go on, and a car held in the lane for its way out stays held, so when it is let go its drawn
		/// copy carries on from just where the body stood.
		/// </summary>
		private readonly bool[] frozen;
		private readonly List<int> frozenCars = new List<int>();
		/// <summary>
		/// Per car: knocked as a standing contact car and let go to drive off by its body (<see cref="LetGoByBody"/>): it comes onto the lane
		/// with no drawn way out and no hold (the game takes its body as a physics car, astray, finding its way back to its line from where it lies).
		/// </summary>
		private readonly bool[] byBody;

		/// <summary>For the log and trafficsim: ways out and in driven, those no gentler than a car turns allowed, and the longest.</summary>
		internal int WaysOut, WaysIn, TightWays;
		internal double LongestWay;
		/// <summary>For trafficsim: every way out or in driven (the car, its start, whether in, the way).</summary>
		internal List<(int car, double at, bool into, Manoeuvre way, TrafficParking.Spot spot)> Ways = null;

		/// <summary>For the overlay and the log: the build's time, the last second's stepping time, cars driving, micro and parked.</summary>
		[NonSerialized] internal double BuildMs, StepMs;
		internal int DrivingNow, MicroNow, ParkedNow;
		[NonSerialized] private double stepSum;
		[NonSerialized] private double stepSince;

		private TrafficLive(Schedules plans, Meso m, double start)
		{
			Plans = plans;
			M = m;
			Seed = plans.Seed;
			Start = start;
			Today = plans;
			Mi = new Micro(m);
			Mi.Move = mi =>
			{
				mi.PX.Clear();
				mi.PZ.Clear();
				if (HasPlayer)
				{
					mi.PX.Add(PlayerX);
					mi.PZ.Add(PlayerZ);
				}
				mi.Bodies.Clear();
				if (HasBody)
				{
					mi.Bodies.Add(PlayerBody);
				}
				mi.Bodies.AddRange(OtherBodies);
				ManoeuvreBodies(mi);
			};
			Parking = new TrafficParking(m.Day);
			Mi.PullOutAlong = PullOutAlong;
			Mi.PullOutTime = PullOutTime;
			Mi.Arriving = Arriving;
			Mi.ArriveAlong = ArriveAlong;
			Mi.PullOutClear = PullOutClear;
			M.Room = Room;
			M.Unseen = Unseen;
			M.KerbNear = KerbNear;
			int n = plans.Cars.Count;
			man = new Manoeuvre[n];
			manSince = new double[n];
			manY = new double[n];
			manIn = new bool[n];
			upX = new double[n];
			upZ = new double[n];
			upHx = new double[n];
			upHz = new double[n];
			upY = new double[n];
			upAt = new double[n];
			pulledUp = new bool[n];
			early = new bool[n];
			frozen = new bool[n];
			byBody = new bool[n];
			m.Held = frozen;
			x0 = new double[n];
			z0 = new double[n];
			hx0 = new double[n];
			hz0 = new double[n];
			y0 = new double[n];
			x1 = new double[n];
			z1 = new double[n];
			hx1 = new double[n];
			hz1 = new double[n];
			y1 = new double[n];
			k0 = new byte[n];
			k1 = new byte[n];
			spots = new TrafficParking.Spot[n];
			parkedStop = new int[n];
			parkedPlace = new int[n];
			for (int i = 0; i < n; i++)
			{
				parkedStop[i] = -1;
			}
			Snapshot();
			Snapshot();
		}

		/// <summary>
		/// The race's traffic from its seed, at day second <paramref name="start"/> (the race's start hour): its plans and every car where they
		/// have it then, and its first step run with the player at (<paramref name="px"/>, <paramref name="pz"/>), the spawn: the junctions
		/// round the player turn micro then, which places their cars where the meso has them, and that is done before any car is drawn (a
		/// junction otherwise changes level only out of sight). On a worker.
		/// </summary>
		internal static TrafficLive Build(int seed, double start, double px, double pz)
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			Traffic.Ensure();
			Lanes.Ensure();
			Schedules plans = Schedules.Build(seed);
			Meso m = Meso.Start(plans, start, start);
			TrafficLive live = new TrafficLive(plans, m, start);
			live.PlayerX = px;
			live.PlayerZ = pz;
			live.HasPlayer = true;
			live.M.Run(start + Meso.Step);
			live.Snapshot();
			live.Snapshot();
			live.BuildMs = watch.Elapsed.TotalMilliseconds;
			return live;
		}

		/// <summary>
		/// A traffic built from the seed at day second <paramref name="start"/> and not stepped: what <see cref="TrafficState.Load"/> fills with a
		/// saved state (its call-backs wired as a build wires them).
		/// </summary>
		internal static TrafficLive Fresh(int seed, double start)
		{
			Traffic.Ensure();
			Lanes.Ensure();
			Schedules plans = Schedules.Build(seed);
			return new TrafficLive(plans, Meso.Start(plans, start, start), start);
		}

		/// <summary>The day second the race clock <paramref name="raceSeconds"/> is at (the lights' clock counts from the race's start too).</summary>
		internal double DayAt(double raceSeconds)
		{
			return Start + raceSeconds + Skip;
		}

		/// <summary>Seconds the traffic has been moved on from the race's clock by the debug skip (<see cref="SkipAhead"/>).</summary>
		internal double Skip;

		/// <summary>
		/// The debug skip (the sky's, <c>Sky.Skipped</c>): the traffic's day moved <paramref name="seconds"/> on, run through in steps over
		/// the next frames (<see cref="MostSteps"/> a frame: 5 minutes take about a second). The lights' clock is moved back as far, so the
		/// meso's and micro's lights stay the drawn ones (which run on the race's clock). Only forward: a simulation can't go back.
		/// </summary>
		internal void SkipAhead(double seconds)
		{
			if (seconds <= 0.0)
			{
				return;
			}
			Skip += seconds;
			M.RaceStart += seconds;
		}

		/// <summary>
		/// Runs the simulation on to day second <paramref name="now"/>, at most <see cref="MostSteps"/> steps this call, into the next day at
		/// 03:00 (every car is planned home by 02:30). Returns how many it ran.
		/// </summary>
		internal int Advance(double now)
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			int steps = 0;
			while (M.Now + Meso.Step <= now + 1e-9 && steps < MostSteps)
			{
				double dayEnd = (DayIndex + 1) * Traffic.DaySeconds;
				if (nextDay == null && M.Now >= dayEnd - PlanAhead - 1e-9)
				{
					int seed = Schedules.DaySeed(Seed, DayIndex + 1);
					nextDay = System.Threading.Tasks.Task.Run(() => Schedules.Build(seed));
				}
				if (M.Now >= dayEnd - 1e-9)
				{
					// The day's end: waits for the next day's plans if they aren't made yet (they were begun minutes ago).
					Schedules next = nextDay.Result;
					nextDay = null;
					M.NextDay(next, dayEnd);
					DayIndex++;
					Today = next;
				}
				HoldFrozen();
				M.Run(M.Now + Meso.Step);
				Snapshot();
				steps++;
			}
			// The boundary's events are trafficsim's: the game keeps none (a day's would pile up).
			Mi.Events.Clear();
			stepSum += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			if (now - stepSince >= 1.0 || now < stepSince)
			{
				StepMs = stepSum / Math.Max(1e-6, now - stepSince);
				stepSum = 0.0;
				stepSince = now;
			}
			return steps;
		}

		/// <summary>
		/// Waits for the next day's plans if they are being made (trafficsim: its race clock runs far faster than the worker, which in the
		/// game has minutes of the race to make them in).
		/// </summary>
		internal void WaitForNextDay()
		{
			nextDay?.Wait();
		}

		/// <summary>Every car's pose now, the last one kept as the one before; parked cars given their spots (and spots given back by cars gone).</summary>
		private void Snapshot()
		{
			Array.Copy(x1, x0, x1.Length);
			Array.Copy(z1, z0, z1.Length);
			Array.Copy(hx1, hx0, hx1.Length);
			Array.Copy(hz1, hz0, hz1.Length);
			Array.Copy(y1, y0, y1.Length);
			Array.Copy(k1, k0, k1.Length);
			t0 = t1;
			t1 = M.Now;
			int driving = 0, micro = 0, parked = 0;
			for (int i = 0; i < M.Cars.Length; i++)
			{
				Meso.Vehicle v = M.Cars[i];
				k1[i] = None;
				if (v.Mode == Meso.Mode.Driving)
				{
					if (pulledUp[i])
					{
						pulledUp[i] = false;
						wayIn.Remove(i);
					}
					if (man[i] != null && manIn[i])
					{
						if (!v.Micro && v.Conn < 0 && v.Lane >= 0 && Mi.LaneMicro[v.Lane] && WayPose(i))
						{
							// Due off again before it is in its spot (a short stop, or its body held up on the way in): it drives in, and
							// pulls out once in (PullOutClear holds it).
							driving++;
							continue;
						}
						// Setting off again before it was in its spot (out of the micro): its way in dropped.
						EndWay(i);
					}
					if (early[i] && (parkedStop[i] != v.Stop || parkedPlace[i] != v.TargetPlace))
					{
						// Its spot given it on the way was at a place it has been sent on from (full).
						early[i] = false;
						Parking.Leave(i);
						parkedStop[i] = -1;
					}
					if (!early[i] && !v.Micro && parkedStop[i] >= 0 && v.Conn < 0 && v.Lane >= 0 && Mi.LaneMicro[v.Lane] && !spots[i].Away)
					{
						// Waiting in its spot for a gap to pull out into: still parked there (the meso's pose would have it rolling off up
						// the road already).
						driving++;
						ParkedPose(i, spots[i]);
						continue;
					}
					if (parkedStop[i] >= 0 && !early[i])
					{
						Parking.Leave(i);
						parkedStop[i] = -1;
					}
					driving++;
					micro += v.Micro ? 1 : 0;
					if (man[i] != null && WayPose(i))
					{
						continue;
					}
					if (DrivingPose(i, out x1[i], out z1[i], out hx1[i], out hz1[i], out y1[i]))
					{
						k1[i] = Driving;
					}
					continue;
				}
				if (v.Mode != Meso.Mode.Parked)
				{
					if (pulledUp[i])
					{
						pulledUp[i] = false;
						wayIn.Remove(i);
					}
					early[i] = false;
					if (man[i] != null)
					{
						EndWay(i);
					}
					if (parkedStop[i] >= 0)
					{
						Parking.Leave(i);
						parkedStop[i] = -1;
					}
					continue;
				}
				parked++;
				early[i] = false;
				if (parkedStop[i] != v.Stop || parkedPlace[i] != v.Place)
				{
					spots[i] = Parking.Park(i, v.Stop, v.Place, 0.0, 0.0, v.KerbAt, v.ParkedAt);
					if (KerbAtStart && spots[i].Away && !v.KerbAt && !Parking.OffMap(i, v.Stop, v.Place) && M.ParkAtKerb(v))
					{
						// No space it can be drawn in (the race's start: a home's car round the back, a full place): at the nearest free kerb.
						spots[i] = Parking.Park(i, v.Stop, v.Place, 0.0, 0.0, true, v.ParkedAt);
					}
					parkedStop[i] = v.Stop;
					parkedPlace[i] = v.Place;
				}
				TrafficParking.Spot s = spots[i];
				if (pulledUp[i])
				{
					// Pulled up at its stop: it drives into its spot from there once its way in is clear (it waits, standing in the lane, for a
					// car standing in its way to move on, at most WaitIn).
					if (s.Away)
					{
						pulledUp[i] = false;
						wayIn.Remove(i);
					}
					else
					{
						if (!wayIn.TryGetValue(i, out Manoeuvre way))
						{
							way = Manoeuvre.In(upX[i], upZ[i], upHx[i], upHz[i], s.X, s.Z, s.Hx, s.Hz, ParkedNear(s.X, s.Z, i));
							wayIn[i] = way;
						}
						if (M.Now - upAt[i] >= WaitIn || SweptClear(way, i))
						{
							List<TrafficParking.Spot> around = ParkedNear(s.X, s.Z, i);
							if (!way.ClearOf(around))
							{
								// A neighbour has parked since it was worked out.
								way = Manoeuvre.In(upX[i], upZ[i], upHx[i], upHz[i], s.X, s.Z, s.Hx, s.Hz, around);
							}
							pulledUp[i] = false;
							wayIn.Remove(i);
							StartWay(i, way, M.Now, true, s);
							manY[i] = upY[i];
						}
						else
						{
							x1[i] = upX[i];
							z1[i] = upZ[i];
							hx1[i] = upHx[i];
							hz1[i] = upHz[i];
							y1[i] = upY[i];
							k1[i] = Driving;
							continue;
						}
					}
				}
				if (s.Away)
				{
					continue;
				}
				if (man[i] != null && manIn[i] && WayPose(i))
				{
					continue;
				}
				ParkedPose(i, s);
			}
			DrivingNow = driving;
			MicroNow = micro;
			ParkedNow = parked;
		}

		private void ParkedPose(int i, TrafficParking.Spot s)
		{
			x1[i] = s.X;
			z1[i] = s.Z;
			hx1[i] = s.Hx;
			hz1[i] = s.Hz;
			y1[i] = double.NaN;
			k1[i] = Parked;
		}

		// ---- driving out of a spot and into one --------------------------------------------------------------------------------

		/// <summary>
		/// The place beside spot <paramref name="s"/> on lane <paramref name="lane"/> (its front's distance along the carriageway, looked for
		/// round <paramref name="along"/>) a car drives out to (<paramref name="into"/> false) or into its spot from best, and that way: at the
		/// kerb a little ahead of the space (out forwards; in, drawing up past it and reversing in), off the road a turn's room ahead of it or
		/// behind (out forwards turning, or backing out swinging round; in turning, or backing in), whichever is quickest. NaN if the spot isn't
		/// by this lane.
		/// </summary>
		private double Beside(int self, TrafficParking.Spot s, int lane, double along, bool into, out Manoeuvre way)
		{
			way = null;
			if (s.Away)
			{
				return double.NaN;
			}
			int cw = Meso.CarriagewayOf(lane);
			double lo = Meso.StartOf(lane), hi = Meso.Reach(lane), proj = double.NaN, bestD = double.MaxValue;
			for (double a = Math.Max(lo, along - 200.0); a <= Math.Min(hi, along + 200.0); a += 2.0)
			{
				Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, a - Lanes.Axle), out double x, out double z, out double _, out double _, out double _);
				double d = (x - s.X) * (x - s.X) + (z - s.Z) * (z - s.Z);
				if (d < bestD)
				{
					bestD = d;
					proj = a;
				}
			}
			if (double.IsNaN(proj) || bestD > Traffic.CarLength * Traffic.CarLength * 25.0)
			{
				return double.NaN;
			}
			// Off its stop line (the junction there is its driver's to decide) and onto the lane proper.
			double first = Math.Min(lo + Traffic.CarLength, hi - 1.0), last = Math.Max(first, hi - Lanes.All[lane].Hold - 1.0);
			double[] tries = s.AtKerb
				? new[] { proj + Micro.PullRoom, proj + Micro.PullRoom * 1.3 }
				: new[] { proj - Traffic.CarLength * 1.2, proj + Traffic.CarLength * 1.2, proj - Traffic.CarLength * 0.6, proj + Traffic.CarLength * 0.6, proj + Traffic.CarLength * 2.0 };
			double best = double.NaN, bestCost = double.MaxValue;
			Manoeuvre found = null;
			List<TrafficParking.Spot> around = ParkedNear(s.X, s.Z, self);
			void Try(double t)
			{
				double a = Math.Max(first, Math.Min(last, t));
				Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, a - Lanes.Axle), out double ex, out double ez, out double _, out double ehx, out double ehz);
				List<TrafficParking.Spot> near = around;
				if (Math.Abs(ex - s.X) > 60.0 || Math.Abs(ez - s.Z) > 60.0)
				{
					// Far along the lane: the cars parked round there too.
					near = new List<TrafficParking.Spot>(around);
					near.AddRange(ParkedNear(ex, ez, self));
				}
				Manoeuvre m = into ? Manoeuvre.In(ex, ez, ehx, ehz, s.X, s.Z, s.Hx, s.Hz, near) : Manoeuvre.Out(s.X, s.Z, s.Hx, s.Hz, ex, ez, ehx, ehz, near, LaneBack(s));
				double cost = m.Duration + (m.MinRadius < Manoeuvre.TurnRadius || m.Hemmed ? 60.0 : 0.0);
				if (cost < bestCost)
				{
					bestCost = cost;
					best = a;
					found = m;
				}
			}
			foreach (double t in tries)
			{
				Try(t);
			}
			for (int k = 1; k <= 3 && found != null && found.Hemmed && s.AtKerb && !into; k++)
			{
				// No room to turn round by the space (the way it is to go is back behind it, cars parked behind and across the road): pulling
				// ahead to turn in the road there.
				Try(proj - Traffic.CarLength * k);
			}
			way = found;
			return best;
		}

		/// <summary>
		/// How far back along the lane behind where it comes onto it a way out of spot <paramref name="s"/> may join it: from the kerb, the room
		/// the cars coming leave a car pulling out (<see cref="Micro.PullRoom"/>, less a little); off the road, none (they stop right behind it).
		/// </summary>
		private static double LaneBack(TrafficParking.Spot s)
		{
			return s.AtKerb ? Micro.PullRoom - 2.0 : 0.0;
		}

		/// <summary>The way ahead of a micro car <see cref="SweptClear"/> looks along: a point every <see cref="SweepStep"/>, as far as these hold.</summary>
		private const double SweepStep = 2.0;
		private readonly double[] sweepX = new double[400], sweepZ = new double[400];

		/// <summary>The longest a car that has pulled up at its stop waits for its way into its spot to clear (seconds).</summary>
		private const double WaitIn = 20.0;

		/// <summary>
		/// Whether way <paramref name="m"/>, driven from now, keeps clear of the micro cars (but <paramref name="self"/>) and the cars driving
		/// their ways: none standing where it goes, none coming (going on as it is along its way) to where it is when it is there that couldn't
		/// stop comfortably for it first, and none coming that couldn't stop comfortably short of anywhere it is still to go (the drivers see the
		/// way still to go as standing bodies, <see cref="ManoeuvreBodies"/>, and stop for it).
		/// </summary>
		private bool SweptClear(Manoeuvre m, int self)
		{
			m.At(0.0, out double sx, out double sz, out double _, out double _, out double _, out double _);
			double reach = m.Length + 200.0;
			int samples = Math.Max(1, (int)Math.Ceiling(m.Duration / 0.5));
			TrafficParking.Spot[] path = new TrafficParking.Spot[samples + 1];
			for (int k = 0; k <= samples; k++)
			{
				m.At(m.Duration * k / samples, out double x, out double z, out double hx, out double hz, out double _, out double _);
				path[k] = new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz };
			}
			foreach (int j in Mi.Active)
			{
				if (j == self)
				{
					continue;
				}
				Micro.Agent a = Mi.Agents[j];
				Mi.Pose(j, out double x, out double z, out double hx, out double hz);
				if (Math.Abs(x - sx) > reach || Math.Abs(z - sz) > reach)
				{
					continue;
				}
				double v = a.HoldUntil > Mi.T ? 0.0 : a.V, stop = v * v / (2.0 * Driver.Brake);
				// Where it will be, going on as it is along its way (round the bends: a straight line from a car on a bend misses the way).
				int along = v > 0.0 ? Mi.WayAhead(j, SweepStep, sweepX, sweepZ, out bool _) : 0;
				// The drivers see all the way still to go as standing (ManoeuvreBodies): none may meet any of it before it can stop comfortably,
				// though at its speed it would be past before the car driving the way got there.
				for (int n = 1; v >= Micro.Standing && n + 1 < along && n * SweepStep < stop + 2.0; n++)
				{
					double dx = sweepX[n + 1] - sweepX[n], dz = sweepZ[n + 1] - sweepZ[n], dl = Math.Sqrt(dx * dx + dz * dz);
					if (dl < 1e-9)
					{
						continue;
					}
					TrafficParking.Spot q = new TrafficParking.Spot { X = sweepX[n], Z = sweepZ[n], Hx = dx / dl, Hz = dz / dl };
					foreach (TrafficParking.Spot p in path)
					{
						if (Math.Abs(p.X - q.X) < 20.0 && Math.Abs(p.Z - q.Z) < 20.0 && TrafficParking.Overlap(p, q, 0.5))
						{
							return false;
						}
					}
				}
				for (int k = 0; k <= samples; k++)
				{
					double t = m.Duration * k / samples;
					TrafficParking.Spot p = path[k];
					TrafficParking.Spot q = new TrafficParking.Spot { X = x + hx * v * t, Z = z + hz * v * t, Hx = hx, Hz = hz };
					if (along >= 2)
					{
						double f = v * t / SweepStep;
						int n = Math.Min(along - 2, (int)f);
						double u = Math.Min(1.0, f - n), dx = sweepX[n + 1] - sweepX[n], dz = sweepZ[n + 1] - sweepZ[n], dl = Math.Sqrt(dx * dx + dz * dz);
						if (f < along - 1 && dl > 1e-9)
						{
							q = new TrafficParking.Spot { X = sweepX[n] + dx * u, Z = sweepZ[n] + dz * u, Hx = dx / dl, Hz = dz / dl };
						}
					}
					if (!TrafficParking.Overlap(p, q, 0.5))
					{
						continue;
					}
					double ahead = (p.X - x) * hx + (p.Z - z) * hz;
					if (v < Micro.Standing || stop > ahead - Traffic.CarLength)
					{
						return false;
					}
				}
			}
			foreach (int j in manoeuvring)
			{
				if (j == self)
				{
					continue;
				}
				man[j].At(M.Now - manSince[j], out double x, out double z, out double hx, out double hz, out double _, out double _);
				TrafficParking.Spot q = new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz };
				foreach (TrafficParking.Spot p in path)
				{
					if (TrafficParking.Overlap(p, q, 0.5))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Whether car <paramref name="car"/>'s way out of its spot onto lane <paramref name="lane"/> at <paramref name="along"/> is clear now (<see cref="SweptClear"/>).</summary>
		private bool PullOutClear(int car, int lane, double along)
		{
			if (man[car] != null && manIn[car] && !byBody[car])
			{
				// Still driving into its spot (its stop shorter than its way in, or its body held up on it): in first, then out.
				return false;
			}
			if (parkedStop[car] < 0 || spots[car].Away || man[car] != null || byBody[car])
			{
				return true;
			}
			int cw = Meso.CarriagewayOf(lane);
			Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, along - Lanes.Axle), out double ex, out double ez, out double _, out double ehx, out double ehz);
			Manoeuvre way;
			if (planned.TryGetValue(car, out var p) && Math.Abs(p.x - ex) < 0.5 && Math.Abs(p.z - ez) < 0.5)
			{
				way = p.way;
			}
			else
			{
				TrafficParking.Spot s = spots[car];
				way = Manoeuvre.Out(s.X, s.Z, s.Hx, s.Hz, ex, ez, ehx, ehz, ParkedNear(s.X, s.Z, car), LaneBack(s));
				planned[car] = (way, ex, ez);
			}
			return SweptClear(way, car);
		}

		/// <summary>
		/// The cars parked within 100 of (<paramref name="x"/>, <paramref name="z"/>) but <paramref name="self"/>: what a way out or in keeps
		/// clear of. Those drawn in their spots, and every spot held (a car given its spot on its way there, one waiting in it to pull out),
		/// so a way isn't planned through a space that is about to be filled.
		/// </summary>
		private List<TrafficParking.Spot> ParkedNear(double x, double z, int self)
		{
			const double Reach = 100.0;
			List<TrafficParking.Spot> near = new List<TrafficParking.Spot>();
			for (int j = 0; j < k1.Length; j++)
			{
				if (j == self)
				{
					continue;
				}
				if (parkedStop[j] >= 0 && !spots[j].Away)
				{
					TrafficParking.Spot s = spots[j];
					if (Math.Abs(s.X - x) < Reach && Math.Abs(s.Z - z) < Reach)
					{
						near.Add(s);
					}
					if (k1[j] != Parked || (Math.Abs(x1[j] - s.X) < 0.01 && Math.Abs(z1[j] - s.Z) < 0.01))
					{
						continue;
					}
				}
				if (k1[j] == Parked && Math.Abs(x1[j] - x) < Reach && Math.Abs(z1[j] - z) < Reach)
				{
					near.Add(new TrafficParking.Spot { X = x1[j], Z = z1[j], Hx = hx1[j], Hz = hz1[j] });
				}
			}
			return near;
		}

		/// <summary>Per car pulling out: the way worked out for where it comes onto the lane (<see cref="PullOutAlong"/>), and that place.</summary>
		private readonly Dictionary<int, (Manoeuvre way, double x, double z)> planned = new Dictionary<int, (Manoeuvre, double, double)>();
		/// <summary>Per car waiting to pull out: the lane and the meso's place it was last asked about, and the answer (asked every step while it waits).</summary>
		private readonly Dictionary<int, (int lane, double along, double at)> asked = new Dictionary<int, (int, double, double)>();
		/// <summary>Per car pulled up at its stop: its way into its spot (worked out once, while it waits for it to clear).</summary>
		private readonly Dictionary<int, Manoeuvre> wayIn = new Dictionary<int, Manoeuvre>();

		/// <summary>Where car <paramref name="car"/> pulling out of its spot comes onto lane <paramref name="lane"/> (<see cref="Beside"/>); NaN: the meso's place.</summary>
		private double PullOutAlong(int car, int lane, double along)
		{
			if (asked.TryGetValue(car, out var q) && q.lane == lane && q.along == along && planned.ContainsKey(car))
			{
				return q.at;
			}
			planned.Remove(car);
			asked.Remove(car);
			if (parkedStop[car] < 0 || man[car] != null || byBody[car])
			{
				return double.NaN;
			}
			double at = Beside(car, spots[car], lane, along, false, out Manoeuvre way);
			if (!double.IsNaN(at))
			{
				int cw = Meso.CarriagewayOf(lane);
				Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, at - Lanes.Axle), out double ex, out double ez, out double _, out double _, out double _);
				planned[car] = (way, ex, ez);
				asked[car] = (lane, along, at);
			}
			return at;
		}

		/// <summary>
		/// Car <paramref name="car"/> has just been placed in the lane pulling out: its way from its spot (or from where it is drawn, if it was
		/// still driving into it) to there, and how long it is held while it drives it.
		/// </summary>
		private double PullOutTime(int car)
		{
			if (Mi.Agents[car] == null || byBody[car])
			{
				planned.Remove(car);
				asked.Remove(car);
				return 0.0;
			}
			double sx, sz, shx, shz;
			TrafficParking.Spot s = spots[car];
			Mi.Pose(car, out double ex, out double ez, out double ehx, out double ehz);
			Manoeuvre m = null;
			if (man[car] != null)
			{
				man[car].At(Mi.T - manSince[car], out sx, out sz, out shx, out shz, out double _, out double _);
				EndWay(car);
			}
			else if (parkedStop[car] >= 0 && !s.Away)
			{
				sx = s.X;
				sz = s.Z;
				shx = s.Hx;
				shz = s.Hz;
				if (planned.TryGetValue(car, out var p) && Math.Abs(p.x - ex) < 0.5 && Math.Abs(p.z - ez) < 0.5 && p.way.ClearOf(ParkedNear(sx, sz, car)))
				{
					m = p.way;
				}
			}
			else
			{
				planned.Remove(car);
				asked.Remove(car);
				return 0.0;
			}
			planned.Remove(car);
			asked.Remove(car);
			m ??= Manoeuvre.Out(sx, sz, shx, shz, ex, ez, ehx, ehz, ParkedNear(sx, sz, car), man[car] == null ? LaneBack(s) : 0.0);
			// Held a whole number of steps (it stands at the end meanwhile), so its body is gone from the step it pulls away in.
			m.Duration = Math.Ceiling(m.Duration / Meso.Step - 1e-9) * Meso.Step;
			StartWay(car, m, Mi.T, false, s);
			manY[car] = AgentY(Mi.Agents[car]);
			return m.Duration;
		}

		/// <summary>
		/// Car <paramref name="car"/> on its last lane: it is given its spot now (held from here: the meso parks it there when it arrives) and
		/// pulls up beside it where it drives in best (<see cref="Beside"/>); NaN: the meso's place.
		/// </summary>
		private double ArriveAlong(int car, int lane, double along)
		{
			Meso.Vehicle v = M.Cars[car];
			Schedules.Car c = M.Day[car];
			if (c.Through || c.Stops == null || v.Stop >= c.Stops.Length || c.Stops[v.Stop].Kind == Schedules.StopKind.Away)
			{
				return double.NaN;
			}
			if (parkedStop[car] != v.Stop || parkedPlace[car] != v.TargetPlace)
			{
				// At the kerb on its own side of the road: the way it comes along this lane.
				Lanes.Point(Lanes.All[lane], Meso.RoadS(Meso.CarriagewayOf(lane), along - Lanes.Axle), out double _, out double _, out double _, out double wx, out double wz);
				spots[car] = Parking.Park(car, v.Stop, v.TargetPlace, wx, wz, v.KerbAt, v.Target);
				parkedStop[car] = v.Stop;
				parkedPlace[car] = v.TargetPlace;
			}
			early[car] = true;
			return Beside(car, spots[car], lane, along, true, out Manoeuvre _);
		}

		/// <summary>
		/// The meso's <see cref="Meso.Room"/>: whether car <paramref name="v"/> arriving at its target gets a space it is drawn in. If not, the
		/// space it was given on its way in (none) is dropped: the meso sends it on, and it is given one where it goes.
		/// </summary>
		/// <summary>Whether cars parked at the race's start with no space drawn are moved to a kerb (off for now: see the handoff).</summary>
		internal static bool KerbAtStart = Environment.GetEnvironmentVariable("TRAFFICSIM_KERBSTART") == "1";

		/// <summary>
		/// The meso's <see cref="Meso.Unseen"/>: whether parked car <paramref name="v"/> may set off without coming out of nothing in a
		/// player's sight. A car drawn in its spot drives out of it; one in no drawn space (round the back, a spot never given) waits while a
		/// player is within <see cref="SeenFrom"/> of where it pulls out.
		/// </summary>
		private bool Unseen(Meso.Vehicle v)
		{
			int i = v.Car;
			if (Parking.OffMap(i, v.Stop, v.Place) || (parkedStop[i] >= 0 && !spots[i].Away))
			{
				return true;
			}
			for (int p = 0; p < Mi.PX.Count; p++)
			{
				double dx = v.ParkedAt.X - Mi.PX[p], dz = v.ParkedAt.Z - Mi.PZ[p];
				if (dx * dx + dz * dz < SeenFrom * SeenFrom)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>How far from a player a car may come onto the road out of no drawn space: past the drawn moving cars' reach (about 450 m) and a little.</summary>
		internal const double SeenFrom = 1600.0;

		/// <summary>The meso's <see cref="Meso.KerbNear"/>: the nearest free kerb space to <paramref name="near"/>, on its own stretch on the side car <paramref name="v"/> comes along.</summary>
		private bool KerbNear(Meso.Vehicle v, Traffic.Anchor near, out Traffic.Anchor at)
		{
			WayAtStop(v, out double wx, out double wz);
			return Parking.KerbNear(near, wx, wz, out at, v.Car);
		}

		/// <summary>The way car <paramref name="v"/> drives at its stop on its lane (0, 0 if it is on none).</summary>
		private void WayAtStop(Meso.Vehicle v, out double wx, out double wz)
		{
			wx = wz = 0.0;
			if (v.Mode != Meso.Mode.Driving || v.Lane < 0 || v.Conn >= 0 && M.Now < v.Entered)
			{
				return;
			}
			int lane = v.Lane;
			double along = v.Ends ? v.DestAlong : Meso.Reach(lane);
			Lanes.Point(Lanes.All[lane], Meso.RoadS(Meso.CarriagewayOf(lane), along - Lanes.Axle), out double _, out double _, out double _, out wx, out wz);
		}

		private bool Room(Meso.Vehicle v)
		{
			int car = v.Car;
			Schedules.Car c = M.Day[car];
			if (c.Through || c.Stops == null || v.Stop >= c.Stops.Length)
			{
				return true;
			}
			WayAtStop(v, out double wx, out double wz);
			if (Parking.Room(car, v.Stop, v.TargetPlace, v.KerbAt, v.Target, wx, wz))
			{
				return true;
			}
			if (parkedStop[car] >= 0)
			{
				Parking.Leave(car);
			}
			parkedStop[car] = -1;
			early[car] = false;
			return false;
		}

		/// <summary>Car <paramref name="car"/> pulls up at its stop: where it stands, to drive into its spot from once the meso parks it.</summary>
		private void Arriving(int car)
		{
			Mi.Pose(car, out upX[car], out upZ[car], out upHx[car], out upHz[car]);
			upY[car] = AgentY(Mi.Agents[car]);
			upAt[car] = Mi.T;
			pulledUp[car] = true;
			wayIn.Remove(car);
		}

		private void StartWay(int car, Manoeuvre m, double at, bool into, TrafficParking.Spot spot)
		{
			if (man[car] == null)
			{
				manoeuvring.Add(car);
			}
			man[car] = m;
			manSince[car] = at;
			manIn[car] = into;
			if (into)
			{
				WaysIn++;
			}
			else
			{
				WaysOut++;
			}
			TightWays += m.MinRadius < Manoeuvre.TurnRadius ? 1 : 0;
			LongestWay = Math.Max(LongestWay, m.Duration);
			Ways?.Add((car, at, into, m, spot));
		}

		private void EndWay(int car)
		{
			man[car] = null;
			manoeuvring.Remove(car);
		}

		/// <summary>Car <paramref name="i"/>'s pose on its way out or in now (driving); false once it has driven it (the way dropped).</summary>
		private bool WayPose(int i)
		{
			Manoeuvre m = man[i];
			double t = M.Now - manSince[i];
			if (t >= m.Duration)
			{
				EndWay(i);
				return false;
			}
			m.At(t, out x1[i], out z1[i], out hx1[i], out hz1[i], out double _, out double _);
			// The road's height where it comes on or pulled up: the car is stood on what is under its wheels, looked for from over that.
			y1[i] = manY[i];
			k1[i] = Driving;
			return true;
		}

		/// <summary>
		/// Holds car <paramref name="i"/> still where it is drawn (<paramref name="on"/>), or lets it go on: a parked car, or one driving out of
		/// its spot or into it, made a loose car by the contact physics stands as its body does until the player has gone (<see cref="frozen"/>).
		/// </summary>
		internal void Freeze(int i, bool on, double now = double.NaN)
		{
			if (frozen[i] == on)
			{
				return;
			}
			frozen[i] = on;
			if (on)
			{
				frozenCars.Add(i);
				if (!double.IsNaN(now) && t1 > t0)
				{
					// Drawn a step behind the clock (Pose): its clocks wound back to where it is drawn at day second now, and held there.
					double back = t1 - Math.Max(t0, Math.Min(t1, now - Meso.Step));
					if (man[i] != null)
					{
						manSince[i] += back;
					}
					if (pulledUp[i])
					{
						upAt[i] += back;
					}
					Micro.Agent a = Mi.Agents[i];
					if (a != null && a.HoldUntil > Mi.T)
					{
						a.HoldUntil += back;
					}
					if (man[i] != null && WayPose(i))
					{
						x0[i] = x1[i];
						z0[i] = z1[i];
						hx0[i] = hx1[i];
						hz0[i] = hz1[i];
						y0[i] = y1[i];
						k0[i] = k1[i];
					}
				}
			}
			else
			{
				frozenCars.Remove(i);
			}
		}

		/// <summary>
		/// Whether car <paramref name="i"/> can be held still where it is drawn (<see cref="Freeze"/>): parked (or waiting in its spot to pull
		/// out), driving out of its spot or into it, pulled up to drive in, or held standing in the lane. Not a car driving.
		/// </summary>
		internal bool CanStand(int i)
		{
			Micro.Agent a = Mi.Agents[i];
			return k1[i] == Parked || man[i] != null || pulledUp[i] || (a != null && !a.Driven && a.HoldUntil > Mi.T);
		}

		/// <summary>Whether car <paramref name="i"/> is held still (<see cref="Freeze"/>).</summary>
		internal bool Frozen(int i)
		{
			return frozen[i];
		}

		/// <summary>
		/// Whether held car <paramref name="i"/> would be driving off now if it weren't held: parked with its departure come (the meso puts a
		/// held car's off a second at a time, <see cref="Meso.Held"/>), or on its way out of its spot (in the lane already, held there).
		/// </summary>
		internal bool DueOut(int i)
		{
			if (!frozen[i])
			{
				return false;
			}
			Meso.Vehicle v = M.Cars[i];
			if (v.Mode == Meso.Mode.Parked && !pulledUp[i] && man[i] == null)
			{
				return v.LeaveAt <= M.Now + 1.0 + 1e-9;
			}
			return man[i] != null && !manIn[i] && Mi.Agents[i] != null;
		}

		/// <summary>
		/// Held car <paramref name="i"/> (<see cref="DueOut"/>) let go to drive off by its body (the owner: a knocked car does its trips while
		/// the player watches): a car on its way out drops its drawn way and its hold in the lane; a parked one sets off with the meso as any
		/// other, onto the lane where the meso has it, with no way out drawn and no hold (<see cref="ByBody"/>). The game takes its body there.
		/// </summary>
		internal void LetGoByBody(int i)
		{
			byBody[i] = true;
			if (man[i] != null && !manIn[i])
			{
				EndWay(i);
				Micro.Agent a = Mi.Agents[i];
				if (a != null)
				{
					a.HoldUntil = double.MinValue;
				}
			}
			planned.Remove(i);
			asked.Remove(i);
			Freeze(i, false);
		}

		/// <summary>Whether car <paramref name="i"/> was let go to drive off by its body (<see cref="LetGoByBody"/>) and its body isn't taken yet.</summary>
		internal bool ByBody(int i)
		{
			return byBody[i];
		}

		/// <summary>Car <paramref name="i"/>'s body taken (or given up): it drives as every other car from here.</summary>
		internal void TookBody(int i)
		{
			byBody[i] = false;
		}

		/// <summary>Whether car <paramref name="i"/> is still in its spot waiting to pull out (drawn parked, its car driving in the meso).</summary>
		internal bool WaitingInSpot(int i)
		{
			return M.Cars[i].Mode == Meso.Mode.Parked || (M.Cars[i].Mode == Meso.Mode.Driving && k1[i] == Parked);
		}

		/// <summary>Before a step: the frozen cars' clocks held back a step (their ways, their wait to drive in, a hold in the lane for a way out).</summary>
		private void HoldFrozen()
		{
			foreach (int i in frozenCars)
			{
				if (man[i] != null)
				{
					manSince[i] += Meso.Step;
				}
				if (pulledUp[i])
				{
					upAt[i] += Meso.Step;
				}
				Micro.Agent a = Mi.Agents[i];
				if (a != null && a.HoldUntil > Mi.T)
				{
					a.HoldUntil += Meso.Step;
				}
			}
		}

		/// <summary>
		/// Car <paramref name="i"/> leaves the day from wherever it is (the contact physics: knocked as a loose car, its body is the car from now
		/// on): out of the micro or its queue, or out of its spot (given back); its way dropped. Its household goes without it.
		/// </summary>
		internal void LoseCar(int i)
		{
			Freeze(i, false);
			byBody[i] = false;
			if (Mi.Agents[i] != null)
			{
				Mi.LoseCar(i);
			}
			else
			{
				M.LoseOutside(M.Cars[i]);
			}
			if (man[i] != null)
			{
				EndWay(i);
			}
			pulledUp[i] = false;
			wayIn.Remove(i);
			planned.Remove(i);
			asked.Remove(i);
			k0[i] = k1[i] = None;
		}

		/// <summary>Whether car <paramref name="i"/> is driving out of its spot or into it now.</summary>
		internal bool Manoeuvring(int i)
		{
			return man[i] != null;
		}

		/// <summary>The cars driving a way out of their spot or into it now (the game makes the nearest physics cars driving it).</summary>
		internal IReadOnlyList<int> WaysNow => manoeuvring;

		/// <summary>
		/// Car <paramref name="i"/>'s way out or in (null: none), how far into it its clock is at day second <paramref name="at"/> (the game
		/// gives the time its cars are drawn at, a step behind its clock), and whether it is the way in.
		/// </summary>
		internal Manoeuvre WayOf(int i, double at, out double t, out bool into)
		{
			Manoeuvre m = man[i];
			t = m != null ? at - manSince[i] : 0.0;
			into = m != null && manIn[i];
			return m;
		}

		/// <summary>
		/// Car <paramref name="i"/>'s way's clock held back <paramref name="seconds"/> (a physics car driving it is behind where the clock has it:
		/// held up, or slower than the plan), and with it the micro's hold in the lane for a way out and the body the drivers see.
		/// </summary>
		internal void WindBack(int i, double seconds)
		{
			if (man[i] == null || seconds <= 0.0)
			{
				return;
			}
			manSince[i] += seconds;
			Micro.Agent a = Mi.Agents[i];
			if (!manIn[i] && a != null && !a.Driven && a.HoldUntil > Mi.T)
			{
				a.HoldUntil += seconds;
			}
		}

		/// <summary>Whether car <paramref name="i"/> has pulled up at its stop and waits there to drive into its spot.</summary>
		internal bool PulledUp(int i)
		{
			return pulledUp[i];
		}

		/// <summary>
		/// The cars driving out of their spots or into them, as bodies the micro's drivers see while they are on the road (a car behind
		/// waits for one pulling in or out across its lane). One pulling in stops being one as it comes into its spot (a car at the kerb is
		/// as near a lane as a body blocks, and is parked by then).
		/// </summary>
		private void ManoeuvreBodies(Micro mi)
		{
			foreach (int i in manoeuvring)
			{
				Manoeuvre m = man[i];
				double t = mi.T - manSince[i];
				if (t >= m.Duration || t < 0.0)
				{
					continue;
				}
				m.At(m.Duration, out double ex, out double ez, out double _, out double _, out double _, out double _);
				m.At(t, out double x, out double z, out double hx, out double hz, out double vx, out double vz);
				if ((x - ex) * (x - ex) + (z - ez) * (z - ez) >= 2.5 * 2.5)
				{
					mi.Bodies.Add(new Micro.Body { X = x, Z = z, Hx = hx, Hz = hz, Vx = vx, Vz = vz, HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 });
				}
				// Where it is still to go, standing: a car behind one that will back into a space waits clear of where it backs to, as drivers
				// do for a car signalling to park. (Not its end: in its spot it is parked; in the lane, a car pulling out is there already.)
				double left = WayLeft(m, t);
				for (double d = 7.0; d < left - 2.5; d += 7.0)
				{
					double tt = TimeAt(m, t, d);
					m.At(tt, out double fx, out double fz, out double fhx, out double fhz, out double _, out double _);
					mi.Bodies.Add(new Micro.Body { X = fx, Z = fz, Hx = fhx, Hz = fhz, HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 });
				}
			}
			for (int i = 0; i < pulledUp.Length; i++)
			{
				if (pulledUp[i])
				{
					// Pulled up, waiting for its way into its spot to clear.
					mi.Bodies.Add(new Micro.Body { X = upX[i], Z = upZ[i], Hx = upHx[i], Hz = upHz[i], HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 });
				}
			}
		}

		/// <summary>How far way <paramref name="m"/> has still to go <paramref name="t"/> seconds in (sampled every half second: near enough for bodies).</summary>
		private static double WayLeft(Manoeuvre m, double t)
		{
			double left = 0.0;
			m.At(t, out double px, out double pz, out double _, out double _, out double _, out double _);
			for (double u = t + 0.5; ; u += 0.5)
			{
				double uu = Math.Min(u, m.Duration);
				m.At(uu, out double x, out double z, out double _, out double _, out double _, out double _);
				left += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
				px = x;
				pz = z;
				if (uu >= m.Duration)
				{
					return left;
				}
			}
		}

		/// <summary>The time way <paramref name="m"/> has gone <paramref name="d"/> further from <paramref name="t"/> seconds in (to within a quarter second).</summary>
		private static double TimeAt(Manoeuvre m, double t, double d)
		{
			m.At(t, out double px, out double pz, out double _, out double _, out double _, out double _);
			double gone = 0.0;
			for (double u = t + 0.25; u < m.Duration; u += 0.25)
			{
				m.At(u, out double x, out double z, out double _, out double _, out double _, out double _);
				gone += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
				px = x;
				pz = z;
				if (gone >= d)
				{
					return u;
				}
			}
			return m.Duration;
		}

		/// <summary>A driving car's middle, the way it faces and its road's height (scene); false where it isn't to be drawn (through a tunnel's jump).</summary>
		private bool DrivingPose(int i, out double x, out double z, out double hx, out double hz, out double y)
		{
			x = z = hx = hz = y = 0.0;
			Meso.Vehicle v = M.Cars[i];
			if (v.Micro && Mi.Agents[i] != null)
			{
				Micro.Agent a = Mi.Agents[i];
				if (a.Conn >= 0 && Lanes.Connectors[a.Conn].Turn == Lanes.Jump)
				{
					return false;
				}
				Mi.Pose(i, out x, out z, out hx, out hz);
				y = AgentY(a);
				return true;
			}
			if (!M.Where(i, out int lane, out int conn, out double along))
			{
				return false;
			}
			if (conn >= 0)
			{
				Lanes.Connector cn = Lanes.Connectors[conn];
				if (cn.Turn == Lanes.Jump)
				{
					return false;
				}
				Micro.OnConnector(cn, along, out x, out z, out hx, out hz);
				y = ConnectorY(cn, along);
				return true;
			}
			if (lane < 0)
			{
				return false;
			}
			int cw = Meso.CarriagewayOf(lane);
			Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, along - Lanes.Axle), out x, out z, out double ry, out hx, out hz);
			y = ry + Network.Lift;
			return true;
		}

		/// <summary>The road's height (scene) under a micro car.</summary>
		private static double AgentY(Micro.Agent a)
		{
			return a.Conn >= 0 ? ConnectorY(Lanes.Connectors[a.Conn], a.X) : LaneY(a.Lane, a.X);
		}

		/// <summary>The road's height (scene) under a car <paramref name="along"/> its lane's carriageway.</summary>
		private static double LaneY(int lane, double along)
		{
			int cw = Meso.CarriagewayOf(lane);
			Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, along - Lanes.Axle), out double _, out double _, out double y, out double _, out double _);
			return y + Network.Lift;
		}

		/// <summary>The road's height (scene) <paramref name="d"/> along a connector's line.</summary>
		private static double ConnectorY(Lanes.Connector cn, double d)
		{
			int n = cn.D.Length;
			if (n == 0 || cn.Y == null || cn.Y.Length != n)
			{
				return double.NaN;
			}
			if (n == 1 || d <= 0.0)
			{
				return cn.Y[0] + Network.Lift;
			}
			if (d >= cn.Length)
			{
				return cn.Y[n - 1] + Network.Lift;
			}
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (cn.D[mid] <= d)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			double span = cn.D[hi] - cn.D[lo], f = span > 1e-12 ? (d - cn.D[lo]) / span : 0.0;
			return cn.Y[lo] + (cn.Y[hi] - cn.Y[lo]) * f + Network.Lift;
		}

		/// <summary>How many cars there are (every household's and the through cars).</summary>
		internal int Count => M.Cars.Length;

		/// <summary>
		/// Car <paramref name="i"/> drawn at day second <paramref name="now"/>: between its last two poses, a step behind the clock. Its middle,
		/// the way it faces (unit), its road's height (scene; NaN parked: the ground's) and what it is doing.
		/// </summary>
		internal byte Pose(int i, double now, out double x, out double z, out double hx, out double hz, out double y)
		{
			byte k = k1[i];
			x = x1[i];
			z = z1[i];
			hx = hx1[i];
			hz = hz1[i];
			y = y1[i];
			if (k != Driving || k0[i] != Driving || t1 <= t0)
			{
				return k;
			}
			double f = (now - Meso.Step - t0) / (t1 - t0);
			f = Math.Max(0.0, Math.Min(1.0, f));
			double dx = x1[i] - x0[i], dz = z1[i] - z0[i];
			if (dx * dx + dz * dz > 120.0 * 120.0)
			{
				// A jump (the wrap, a car placed): no sliding across.
				return k;
			}
			x = x0[i] + dx * f;
			z = z0[i] + dz * f;
			y = double.IsNaN(y0[i]) || double.IsNaN(y1[i]) ? y1[i] : y0[i] + (y1[i] - y0[i]) * f;
			double ax = hx0[i] + (hx1[i] - hx0[i]) * f, az = hz0[i] + (hz1[i] - hz0[i]) * f, len = Math.Sqrt(ax * ax + az * az);
			if (len > 1e-6)
			{
				hx = ax / len;
				hz = az / len;
			}
			return k;
		}

		/// <summary>Where parked car <paramref name="i"/> stands (meaningful while its pose is <see cref="Parked"/>).</summary>
		internal TrafficParking.Spot SpotOf(int i)
		{
			return spots[i];
		}

		/// <summary>Car <paramref name="i"/>'s last pose (world x, z) and what it is doing, for choosing which cars to draw.</summary>
		internal byte Where(int i, out double x, out double z)
		{
			x = x1[i];
			z = z1[i];
			return k1[i];
		}

		/// <summary>
		/// Car <paramref name="i"/>'s day for the follow-car key: what it is doing now, its stops with when its driver wants to leave each, and
		/// (driving) where it is going.
		/// </summary>
		internal string Describe(int i)
		{
			Meso.Vehicle v = M.Cars[i];
			Schedules.Car c = M.Day[i];
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append("car ").Append(i);
			if (c.Through)
			{
				sb.Append(", passing through on the highway");
			}
			else
			{
				sb.Append(" of household ").Append(c.Household).Append(" (its car ").Append(c.Index + 1).Append(')');
			}
			sb.Append(": ").Append(v.Mode == Meso.Mode.Driving ? (v.Micro ? "driving (near)" : "driving") : v.Mode.ToString().ToLowerInvariant());
			if (!c.Through && c.Stops != null)
			{
				if (v.Mode == Meso.Mode.Driving && v.Stop < c.Stops.Length)
				{
					sb.Append(" to ").Append(StopName(c.Stops[v.Stop])).Append(", set off ").Append(Clock((v.TripStart)));
				}
				else if (v.Mode == Meso.Mode.Parked && v.Stop < c.Stops.Length)
				{
					sb.Append(" at ").Append(StopName(c.Stops[v.Stop])).Append(v.LeaveAt < 1e9 ? ", leaves " + Clock((v.LeaveAt)) : " for the night");
				}
				sb.Append("\n  day:");
				for (int s = 0; s < c.Stops.Length; s++)
				{
					Schedules.Stop st = c.Stops[s];
					sb.Append(s == v.Stop ? " [" : " ").Append(StopName(st));
					if (st.WantLeave < 1e9)
					{
						sb.Append(' ').Append(Clock((st.WantLeave)));
					}
					sb.Append(s == v.Stop ? "]" : "");
				}
			}
			return sb.ToString();
		}

		/// <summary>Day second <paramref name="t"/> as the clock shows it (hh:mm).</summary>
		internal static string Clock(double t)
		{
			double hour = Schedules.HourOf(t);
			hour -= Math.Floor(hour / 24.0) * 24.0;
			int minutes = (int)Math.Floor(hour * 60.0);
			return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
		}

		private static string StopName(Schedules.Stop st)
		{
			switch (st.Kind)
			{
				case Schedules.StopKind.Home:
					return "home";
				case Schedules.StopKind.Place:
					return st.Dest >= 0 && st.Dest < Traffic.Destinations.Count ? (Traffic.Destinations[st.Dest].Name ?? Traffic.Destinations[st.Dest].Kind.ToString()) : "a place";
				case Schedules.StopKind.Visit:
					return "friends";
				default:
					return "beyond the map";
			}
		}
	}
}
