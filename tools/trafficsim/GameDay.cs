using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// Phase 5: the game's own path, headless (TrafficLive.cs, TrafficParking.cs): the race's traffic built as the game builds it on a worker,
// then advanced frame by frame on the race clock round one pretend player driving the lane graph, as TrafficCars runs it. Fails on: a car's
// drawn pose not a number; a drawn moving car jumping between frames further than its speed takes it (the wrap's jump and cars placed by
// the micro's boundary excepted: those are out of sight); two parked cars drawn in one another; a parked car on another road's paving, in
// a house or the water; a car parked but not drawn where it should be (its own space at home lost); the simulation falling behind the
// clock; a car pulling out of its spot or into it (Manoeuvre.cs) jumping as it leaves its spot or comes into it, turning tighter than a car
// can, taking over half a minute, or running into a micro car or a parked one. Measured: the build's time, and the simulation's cost a second of the race (the frame-time budget).
internal static partial class Program
{
	private static void GameDay(int seed, double minutes, double from = -1.0)
	{
		double startHour = StartHourOf(seed);
		double start = from >= 0.0 ? from : Schedules.StartOf(startHour);
		bool dayEnd = from >= 0.0;
		Meso.Ensure();
		Walker w = Walkers(seed)[1];
		var watch = System.Diagnostics.Stopwatch.StartNew();
		TrafficLive live = TrafficLive.Build(seed, start, w.SX, w.SZ);
		double buildMs = watch.Elapsed.TotalMilliseconds;
		live.PlayerX = w.SX;
		live.PlayerZ = w.SZ;
		live.HasPlayer = true;
		live.Ways = new List<(int, double, bool, Manoeuvre, TrafficParking.Spot)>();
		string where = "seed " + seed + " in game from " + TrafficLive.Clock(start);
		int pullJumps = 0, wayHits = 0, wayParkedHits = 0, waysSeen = 0;
		HashSet<int> hitCars = new HashSet<int>();
		// The day's end: through cars' places just before 03:00, departures and arrivals after it.
		double boundary = Traffic.DaySeconds;
		double[] tx = null, tz = null;
		int departuresAt = -1, arrivalsAt = -1, begunAtOnce = -1;
		int n = live.Count;
		double[] lx = new double[n], lz = new double[n];
		byte[] lk = new byte[n];
		for (int i = 0; i < n; i++)
		{
			lk[i] = live.Pose(i, start, out lx[i], out lz[i], out double _, out double _, out double _);
		}
		const double frame = 1.0 / 60.0;
		int trace = int.TryParse(Environment.GetEnvironmentVariable("TRAFFICSIM_GAME_TRACE"), out int tr) ? tr : -1;
		double race = 0.0, worstStep = 0.0, worstAt = 0.0, stepSum = 0.0;
		int frames = 0, badPose = 0, jumps = 0, overlaps = 0, offPaving = 0, behind = 0;
		List<string> firsts = new List<string>();
		void Note(string what)
		{
			if (firsts.Count < 8)
			{
				firsts.Add(what);
			}
		}
		double nextParkCheck = 0.0;
		while (race < minutes * 60.0)
		{
			race += frame;
			frames++;
			w.Step(frame);
			live.PlayerX = w.SX;
			live.PlayerZ = w.SZ;
			double now = live.DayAt(race);
			if (dayEnd && now >= boundary - 60.0 && live.DayIndex == 0)
			{
				// The game has minutes of the race to make the next day's plans in; this clock runs far faster than the worker.
				live.WaitForNextDay();
			}
			long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
			live.Advance(now);
			double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			stepSum += ms;
			if (ms > worstStep)
			{
				worstStep = ms;
				worstAt = race;
			}
			if (live.M.Now + Meso.Step < now - 1e-6)
			{
				behind++;
			}
			if (dayEnd && tx == null && live.M.Now >= boundary - 10.0)
			{
				tx = new double[n];
				tz = new double[n];
				for (int i = 0; i < n; i++)
				{
					live.Where(i, out tx[i], out tz[i]);
				}
			}
			if (dayEnd && departuresAt < 0 && live.DayIndex == 1)
			{
				departuresAt = live.M.Departures;
				arrivalsAt = live.M.Arrivals;
				begunAtOnce = live.M.Begun;
			}
			for (int i = 0; i < n; i++)
			{
				byte k = live.Pose(i, now, out double x, out double z, out double hx, out double hz, out double y);
				if (k == TrafficLive.None)
				{
					lk[i] = k;
					continue;
				}
				if (double.IsNaN(x) || double.IsNaN(z) || double.IsNaN(hx) || double.IsNaN(hz) || double.IsInfinity(x) || (k == TrafficLive.Driving && double.IsNaN(y)))
				{
					badPose++;
					Note("car " + i + " drawn at " + x + ", " + z + " facing " + hx + ", " + hz + " height " + y + " at " + TrafficLive.Clock(now));
				}
				if (trace == i)
				{
					Meso.Vehicle tv = live.M.Cars[i];
					Console.WriteLine("    trace " + race.ToString("0.000") + " now " + now.ToString("0.000") + " meso " + live.M.Now.ToString("0.000") + " k " + k + " at " + x.ToString("0.00") + ", " + z.ToString("0.00") + " micro " + tv.Micro + " lane " + tv.Lane + " conn " + tv.Conn + (live.Mi.Agents[i] != null ? " agent X " + live.Mi.Agents[i].X.ToString("0.00") + " V " + live.Mi.Agents[i].V.ToString("0.00") + " conn " + live.Mi.Agents[i].Conn : ""));
				}
				if (k != lk[i] && (k == TrafficLive.Parked || lk[i] == TrafficLive.Parked) && lk[i] != TrafficLive.None)
				{
					// Out of its spot or into it: from where it was drawn, no jump (it drives the way between).
					double dx = x - lx[i], dz = z - lz[i], d = Math.Sqrt(dx * dx + dz * dz);
					double pd = Math.Sqrt((x - live.PlayerX) * (x - live.PlayerX) + (z - live.PlayerZ) * (z - live.PlayerZ));
					if (d > 2.0 && pd < Micro.Sight)
					{
						pullJumps++;
						Note("car " + i + " jumped " + d.ToString("0.0") + " " + (k == TrafficLive.Parked ? "into" : "out of") + " its spot at " + x.ToString("0") + ", " + z.ToString("0") + " (" + pd.ToString("0") + " from the player) at " + TrafficLive.Clock(now));
					}
				}
				if (k == TrafficLive.Driving && lk[i] == TrafficLive.Driving)
				{
					double dx = x - lx[i], dz = z - lz[i], d = Math.Sqrt(dx * dx + dz * dz);
					double pd = Math.Sqrt((x - live.PlayerX) * (x - live.PlayerX) + (z - live.PlayerZ) * (z - live.PlayerZ));
					// The fastest road (the trunk, 65 mph) and a little: a car seen moving faster than that between two frames jumped.
					if (d > 40.0 * frame * 3.0 && d < 120.0 && pd < Micro.Sight)
					{
						jumps++;
						Note("car " + i + " jumped " + d.ToString("0.0") + " in a frame at " + x.ToString("0") + ", " + z.ToString("0") + " (" + pd.ToString("0") + " from the player) at " + TrafficLive.Clock(now));
					}
				}
				lx[i] = x;
				lz[i] = z;
				lk[i] = k;
			}
			if (frames % 6 == 0)
			{
				WayChecks(live, now, ref wayHits, ref wayParkedHits, ref waysSeen, hitCars, Note);
			}
			if (race >= nextParkCheck)
			{
				nextParkCheck += 10.0;
				// Every parked car drawn: none in another, none on another road, in a house or the water.
				Dictionary<long, List<(int car, TrafficParking.Spot s)>> cells = new Dictionary<long, List<(int, TrafficParking.Spot)>>();
				for (int i = 0; i < n; i++)
				{
					if (live.Pose(i, now, out double x, out double z, out double hx, out double hz, out double _) != TrafficLive.Parked)
					{
						continue;
					}
					TrafficParking.Spot s = live.SpotOf(i);
					long key = ((long)Math.Floor(x / 40.0) << 32) ^ (uint)(int)Math.Floor(z / 40.0);
					int gx = (int)Math.Floor(x / 40.0), gz = (int)Math.Floor(z / 40.0);
					for (int ox = -1; ox <= 1; ox++)
					{
						for (int oz = -1; oz <= 1; oz++)
						{
							if (!cells.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out var list))
							{
								continue;
							}
							foreach ((int car, TrafficParking.Spot o) in list)
							{
								if (TrafficParking.Overlap(s, o, -0.2))
								{
									overlaps++;
									Note("cars " + car + " and " + i + " parked in one another at " + x.ToString("0") + ", " + z.ToString("0") + " at " + TrafficLive.Clock(now));
								}
							}
						}
					}
					if (!cells.TryGetValue(key, out var mine))
					{
						mine = new List<(int, TrafficParking.Spot)>();
						cells[key] = mine;
					}
					mine.Add((i, s));
					if (race < 1.0)
					{
						// Once (it is slow): every spot clear of the other roads, the houses and the water. A kerb spot stands on its own road.
						bool clear = TrafficParking.Clear(s, s.AtKerb ? s.Road : -1);
						if (!clear)
						{
							offPaving++;
							Note("car " + i + " parked on a road, in a house or the water at " + x.ToString("0") + ", " + z.ToString("0"));
						}
					}
				}
			}
		}
		double perSecond = stepSum / (minutes * 60.0);
		Console.WriteLine("  game, " + where + ": built in " + buildMs.ToString("0") + " ms; " + (minutes).ToString("0") + " minutes of race at 60 frames a second: simulation "
			+ perSecond.ToString("0.00") + " ms a second of the race, worst frame " + worstStep.ToString("0.0") + " ms (" + worstAt.ToString("0.0") + " s in); at the end " + live.DrivingNow + " driving (" + live.MicroNow + " near), "
			+ live.ParkedNow + " parked; parking " + live.Parking.OwnSpaces + " own spaces at home, " + live.Parking.KerbHomes + " at the kerb, " + live.Parking.RoundTheBack + " round the back");
		int tight = 0, slow = 0, ways = live.Ways.Count;
		double worstRadius = double.MaxValue, longest = 0.0, lengthSum = 0.0, timeSum = 0.0;
		int backed = 0;
		foreach ((int car, double at, bool into, Manoeuvre m, TrafficParking.Spot spot) in live.Ways)
		{
			worstRadius = Math.Min(worstRadius, m.MinRadius);
			longest = Math.Max(longest, m.Duration);
			lengthSum += m.Length;
			timeSum += m.Duration;
			backed += m.Legs.Exists(l => l.Back) ? 1 : 0;
			string waysEnv = Environment.GetEnvironmentVariable("TRAFFICSIM_WAYS");
			if (waysEnv == "1" || (waysEnv == "bad" && (m.MinRadius < Manoeuvre.TurnRadius * 0.99 || m.Duration > 30.0 || hitCars.Contains(car * 2 + 1))))
			{
				m.At(0.0, out double ax, out double az, out double ahx, out double ahz, out double _, out double _);
				m.At(m.Duration, out double bx, out double bz, out double bhx, out double bhz, out double _, out double _);
				double fwd = (bx - ax) * ahx + (bz - az) * ahz, side = -(bx - ax) * ahz + (bz - az) * ahx;
				Console.WriteLine("      way car " + car + (into ? " in" : " out") + (spot.AtKerb ? " kerb" : spot.Away ? " away" : " off road") + " from " + ax.ToString("0") + ", " + az.ToString("0") + ": end " + fwd.ToString("0.0") + " ahead " + side.ToString("0.0") + " left, facing " + (Math.Atan2(-bhx * ahz + bhz * ahx, bhx * ahx + bhz * ahz) * 180.0 / Math.PI).ToString("0") + "°; "
					+ string.Join(" + ", m.Legs.Select(l => (l.Back ? "back " : "fwd ") + l.Length.ToString("0.0") + " r" + l.MinRadius.ToString("0.0") + " " + l.Time.ToString("0.0") + "s")) + " = " + m.Duration.ToString("0.0") + " s");
			}
			if (m.MinRadius < Manoeuvre.TurnRadius * 0.99)
			{
				if (tight++ < 3)
				{
					Note("car " + car + "'s way " + (into ? "into" : "out of") + " its spot at " + spot.X.ToString("0") + ", " + spot.Z.ToString("0") + (spot.AtKerb ? " (kerb)" : "") + " turns at " + m.MinRadius.ToString("0.0") + " units, " + m.Legs.Count + " legs, at " + TrafficLive.Clock(at));
				}
			}
			if (m.Duration > 30.0)
			{
				if (slow++ < 3)
				{
					Note("car " + car + "'s way " + (into ? "into" : "out of") + " its spot at " + spot.X.ToString("0") + ", " + spot.Z.ToString("0") + " takes " + m.Duration.ToString("0.0") + " s over " + m.Length.ToString("0") + " units");
				}
			}
		}
		Console.WriteLine("    ways out of spots " + live.WaysOut + ", into them " + live.WaysIn + " (" + backed + " backing), " + waysSeen + " seen driving them near the player; "
			+ (ways > 0 ? "on average " + (lengthSum / ways / 3.3).ToString("0.0") + " m in " + (timeSum / ways).ToString("0.0") + " s, the longest " + longest.ToString("0.0") + " s, the tightest turn " + (worstRadius / 3.3).ToString("0.0") + " m" : "none"));
		foreach (string f in firsts)
		{
			Console.WriteLine("    " + f);
		}
		Check(ways > 0 || dayEnd, "game ways", where + ": no car drove out of its spot or into it");
		Check(pullJumps == 0, "game ways", where + ": " + pullJumps + " cars jumped out of their spots or into them in sight");
		Check(tight == 0, "game ways", where + ": " + tight + " ways out or in turn tighter than a car can");
		Check(slow == 0, "game ways", where + ": " + slow + " ways out or in take over half a minute");
		Check(wayHits == 0, "game ways", where + ": " + wayHits + " times a car driving out of its spot or into it was in a micro car");
		Check(wayParkedHits == 0, "game ways", where + ": " + wayParkedHits + " times a car driving out of its spot or into it was in a parked car");
		Check(badPose == 0, "game poses", where + ": " + badPose + " poses not a number");
		Check(jumps == 0, "game jumps", where + ": " + jumps + " drawn cars jumped between frames in sight");
		Check(overlaps == 0, "game parking", where + ": " + overlaps + " pairs of parked cars in one another");
		Check(offPaving == 0, "game parking", where + ": " + offPaving + " parked cars on another road, in a house or the water");
		Check(behind == 0, "game clock", where + ": the simulation fell behind the clock in " + behind + " frames");
		Check(buildMs < 6000.0, "game build", where + ": built in " + buildMs.ToString("0") + " ms");
		if (dayEnd)
		{
			DayEndChecks(live, where, n, tx, tz, departuresAt, arrivalsAt, begunAtOnce);
			return;
		}
		BlockDay(seed);
		ContactDay(seed);
		PhysicsDay(seed, 10.0);
	}

	/// <summary>
	/// The cars driving out of their spots or into them within sight of the player: none in a micro car (a driving one's body: the drivers
	/// see the way as a body, and a car pulling out is held in the lane where it comes on) nor in a parked one.
	/// </summary>
	private static void WayChecks(TrafficLive live, double now, ref int hits, ref int parkedHits, ref int seen, HashSet<int> hitCars, Action<string> note)
	{
		int n = live.Count;
		List<(int car, TrafficParking.Spot s)> moving = new List<(int, TrafficParking.Spot)>();
		for (int i = 0; i < n; i++)
		{
			if (!live.Manoeuvring(i) || live.Pose(i, now, out double x, out double z, out double hx, out double hz, out double _) != TrafficLive.Driving)
			{
				continue;
			}
			if ((x - live.PlayerX) * (x - live.PlayerX) + (z - live.PlayerZ) * (z - live.PlayerZ) > Micro.Sight * Micro.Sight)
			{
				continue;
			}
			moving.Add((i, new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz }));
		}
		if (moving.Count == 0)
		{
			return;
		}
		foreach ((int car, TrafficParking.Spot s) in moving)
		{
			if (hitCars.Add(car * 2))
			{
				seen++;
			}
		}
		for (int j = 0; j < n; j++)
		{
			byte k = live.Pose(j, now, out double x, out double z, out double hx, out double hz, out double _);
			if (k == TrafficLive.None)
			{
				continue;
			}
			TrafficParking.Spot o = new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz };
			foreach ((int car, TrafficParking.Spot s) in moving)
			{
				if (car == j || Math.Abs(s.X - x) > 20.0 || Math.Abs(s.Z - z) > 20.0)
				{
					continue;
				}
				if (k == TrafficLive.Driving && live.Manoeuvring(j))
				{
					continue;
				}
				if (!TrafficParking.Overlap(s, o, -0.3))
				{
					continue;
				}
				if (k == TrafficLive.Parked)
				{
					parkedHits++;
				}
				else
				{
					hits++;
				}
				hitCars.Add(car * 2 + 1);
				note("car " + car + " driving " + (live.Manoeuvring(car) ? "its way" : "") + " in " + (k == TrafficLive.Parked ? "parked" : "driving") + " car " + j + " at " + x.ToString("0") + ", " + z.ToString("0") + " at " + TrafficLive.Clock(now));
			}
		}
	}

	/// <summary>
	/// The day's end (a race running past 03:00): the game's path from shortly before it into the next morning. Fails if the next day
	/// doesn't begin, if a car home for the night hasn't begun it by the end (gone cars and those still out apart), if no car sets off or
	/// arrives in the new day, or if a through car stopped circling. Reported: the cars that began it at once and the trips made since.
	/// </summary>
	private static void DayEndChecks(TrafficLive live, string where, int n, double[] tx, double[] tz, int departuresAt, int arrivalsAt, int begunAtOnce)
	{
		Check(live.DayIndex == 1 && departuresAt >= 0, "day's end", where + ": the next day did not begin (day " + live.DayIndex + ")");
		if (departuresAt < 0 || tx == null)
		{
			return;
		}
		int still = 0, stopped = 0, through = 0, home = 0;
		for (int i = 0; i < n; i++)
		{
			Meso.Vehicle v = live.M.Cars[i];
			if (v.Through)
			{
				through++;
				live.Where(i, out double x, out double z);
				double d = Math.Sqrt((x - tx[i]) * (x - tx[i]) + (z - tz[i]) * (z - tz[i]));
				if (v.Mode != Meso.Mode.Gone && d < 50.0)
				{
					stopped++;
				}
				continue;
			}
			bool raceDay = ReferenceEquals(live.M.Day[i], live.Plans.Cars[i]);
			if (raceDay && v.Mode == Meso.Mode.Parked && v.Stop + 1 >= live.M.Day[i].Stops.Length)
			{
				still++;
			}
			home += !raceDay ? 1 : 0;
		}
		int departed = live.M.Departures - departuresAt, arrived = live.M.Arrivals - arrivalsAt;
		Console.WriteLine("  day's end, " + where + ": day 2 a " + (live.Today.Weekend ? "weekend day" : "weekday") + ", " + begunAtOnce + " cars began it at 03:00, "
			+ home + " by the end; since 03:00 " + departed + " set off, " + arrived + " arrived; " + (through - stopped) + " of " + through + " through cars still circling");
		Check(still == 0, "day's end", where + ": " + still + " cars home for the night never began the next day");
		Check(departed > 0 && arrived > 0, "day's end", where + ": no car set off (" + departed + ") or arrived (" + arrived + ") in the next day");
		Check(stopped == 0, "day's end", where + ": " + stopped + " through cars stopped at the day's end");
		LateHome(live.Seed);
	}

	/// <summary>
	/// Cars still out at the day's end (none are on the seeds tried: every car is planned home by 02:30 with time to spare): the meso alone
	/// from 18:00 given the next day's plans then, as if the day ended with the evening's cars out. Fails if a car still out begins its next
	/// day before it is home for the night, or a car home for the night hasn't begun it once the evening is over.
	/// </summary>
	private static void LateHome(int seed)
	{
		Schedules plans = Schedules.Build(seed);
		double at = Schedules.StartOf(18.0);
		Meso m = Meso.Start(plans, at, at);
		Schedules next = Schedules.Build(Schedules.DaySeed(seed, 1));
		int n = m.Cars.Length, outThen = 0, early = 0, notBegun = 0;
		bool[] wasOut = new bool[n];
		for (int i = 0; i < n; i++)
		{
			Meso.Vehicle v = m.Cars[i];
			wasOut[i] = !v.Through && !(v.Mode == Meso.Mode.Parked && v.Stop + 1 >= plans.Cars[i].Stops.Length);
			outThen += wasOut[i] ? 1 : 0;
		}
		m.NextDay(next, Traffic.DaySeconds);
		int begunAtOnce = m.Begun;
		bool[] seen = new bool[n];
		for (double t = at + Meso.Step; t <= Traffic.DaySeconds + 1e-9; t += Meso.Step)
		{
			m.Run(t);
			for (int i = 0; i < n; i++)
			{
				Meso.Vehicle v = m.Cars[i];
				if (seen[i] || v.Through || ReferenceEquals(m.Day[i], plans.Cars[i]))
				{
					continue;
				}
				// Just begun: parked at home at the next day's first stop (it rests a while before setting off).
				seen[i] = true;
				if (v.Mode != Meso.Mode.Parked || v.Stop != 0 || v.Place >= 0)
				{
					early++;
				}
			}
		}
		for (int i = 0; i < n; i++)
		{
			Meso.Vehicle v = m.Cars[i];
			if (!v.Through && v.Mode != Meso.Mode.Gone && ReferenceEquals(m.Day[i], plans.Cars[i]))
			{
				notBegun++;
			}
		}
		Console.WriteLine("  day's end with cars out, seed " + seed + " from 18:00: " + outThen + " cars out or not yet home for the night, " + begunAtOnce
			+ " began the next day at once, " + (m.Begun - begunAtOnce) + " as they got home; " + notBegun + " never began it");
		Check(notBegun == 0, "day's end", "seed " + seed + ": " + notBegun + " cars out at the day's end never began the next day once home");
		Check(early == 0, "day's end", "seed " + seed + ": " + early + " cars began the next day anywhere but parked at home");
	}

	/// <summary>
	/// A player blocking a road (the plan's persistence check): the player stands at Kingsbridge's light (node 6) and its car stands across
	/// the busiest lane in, 60 units short of the stop line, for two minutes, then drives off. Fails if a micro car drives into it, if no
	/// queue forms behind it, or if the queue is still standing two minutes after it has gone. Reported: the longest queue behind it and how
	/// far back into the meso it reached.
	/// </summary>
	private static void BlockDay(int seed)
	{
		double start = Schedules.StartOf(StartHourOf(seed));
		Meso.Ensure();
		Traffic.Node k6 = Traffic.Nodes[6];
		TrafficLive live = TrafficLive.Build(seed, start, k6.X, k6.Z);
		string where = "seed " + seed + " in game from " + TrafficLive.Clock(start) + ", the player's car blocking a lane into node 6";
		// The lane in with the most cars over the first minute.
		int[] count = new int[Lanes.All.Count];
		HashSet<(int, int)> seen = new HashSet<(int, int)>();
		double race = 0.0;
		const double frame = 1.0 / 30.0;
		while (race < 60.0)
		{
			race += frame;
			live.Advance(live.DayAt(race));
			foreach (int c in live.Mi.Active)
			{
				Micro.Agent a = live.Mi.Agents[c];
				if (a.Conn >= 0 && Lanes.Connectors[a.Conn].Node == 6 && seen.Add((c, a.Conn)))
				{
					count[Lanes.Connectors[a.Conn].From]++;
				}
			}
		}
		int lane = -1;
		for (int l = 0; l < Lanes.All.Count; l++)
		{
			if (Lanes.All[l].To == 6 && Lanes.All[l].Kind == Lanes.Through && Meso.Reach(l) - Meso.StartOf(l) > 120.0 && (lane < 0 || count[l] > count[lane]))
			{
				lane = l;
			}
		}
		if (!Check(lane >= 0, "game block", where + ": no lane into node 6 to block"))
		{
			return;
		}
		int cw = Meso.CarriagewayOf(lane);
		double mid = Meso.Reach(lane) - 60.0;
		Lanes.Point(Lanes.All[lane], Meso.RoadS(cw, mid), out double bx, out double bz, out double _, out double hx, out double hz);
		TrafficParking.Spot block = new TrafficParking.Spot { X = bx, Z = bz, Hx = hx, Hz = hz };
		// The player's car comes onto the spot only once no car is on it (a player can't land on a car), within a minute.
		for (double until = race + 60.0; race < until; )
		{
			bool clear = true;
			foreach (int c in live.Mi.Active)
			{
				live.Mi.Pose(c, out double x, out double z, out double ax, out double az);
				clear &= !TrafficParking.Overlap(block, new TrafficParking.Spot { X = x, Z = z, Hx = ax, Hz = az }, 2.0);
			}
			if (clear)
			{
				break;
			}
			race += frame;
			live.Advance(live.DayAt(race));
		}
		live.PlayerBody = new Micro.Body { X = bx, Z = bz, Hx = hx, Hz = hz, HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 };
		live.HasBody = true;
		int hits = 0, longest = 0, standingAfter = 0, mesoBehind = 0;
		string firstHit = null;
		double end = race + 120.0;
		while (race < end + 120.0)
		{
			race += frame;
			if (race >= end)
			{
				live.HasBody = false;
			}
			live.Advance(live.DayAt(race));
			int queue = 0;
			foreach (int c in live.Mi.Active)
			{
				Micro.Agent a = live.Mi.Agents[c];
				live.Mi.Pose(c, out double x, out double z, out double ax, out double az);
				if (live.HasBody && TrafficParking.Overlap(block, new TrafficParking.Spot { X = x, Z = z, Hx = ax, Hz = az }, -0.3))
				{
					hits++;
					firstHit ??= "car " + c + " in the player's car at " + x.ToString("0") + ", " + z.ToString("0") + " at " + race.ToString("0") + " s";
				}
				if (a.Conn < 0 && Meso.CarriagewayOf(a.Lane) == cw && a.X < mid && a.V < Micro.Standing)
				{
					queue++;
				}
			}
			if (live.HasBody)
			{
				longest = Math.Max(longest, queue);
				int behind = 0;
				foreach (Meso.Vehicle v in live.M.Cars)
				{
					behind += v.Mode == Meso.Mode.Driving && !v.Micro && v.HeldSince >= 0.0 ? 1 : 0;
				}
				mesoBehind = Math.Max(mesoBehind, behind);
			}
			else if (race >= end + 115.0)
			{
				standingAfter = Math.Max(standingAfter, queue);
			}
		}
		Console.WriteLine("  game, " + where + " (lane " + lane + ", " + count[lane] + " cars through in the minute before): " + hits + " car-frames in it, longest queue behind it " + longest
			+ " standing, two minutes after it went " + standingAfter + " still standing on that road" + (firstHit != null ? "; first: " + firstHit : ""));
		Check(hits == 0, "game block", where + ": cars drove into the player's car (" + firstHit + ")");
		Check(longest >= 2, "game block", where + ": no queue formed behind it (" + longest + " standing)");
		Check(standingAfter <= 2, "game block", where + ": " + standingAfter + " still standing two minutes after it went");
	}

	/// <summary>
	/// The contact physics' part in the simulation, with the player at node 6: a parked car and a car driving out of its spot or into it held
	/// still (<see cref="TrafficLive.Freeze"/>) for a minute, as while their loose bodies stand in for them, must not move (nor a car held
	/// in the lane for its way out), and let go must carry on from there with no jump; then a parked car, one on its way out or in and a
	/// micro car driving knocked out of their day (<see cref="TrafficLive.LoseCar"/>) must be gone for good, never drawn again, with every
	/// other car still in one place (conservation) and the day going on.
	/// </summary>
	private static void ContactDay(int seed)
	{
		double start = Schedules.StartOf(StartHourOf(seed));
		Meso.Ensure();
		Traffic.Node k6 = Traffic.Nodes[6];
		TrafficLive live = TrafficLive.Build(seed, start, k6.X, k6.Z);
		live.PlayerX = k6.X;
		live.PlayerZ = k6.Z;
		live.HasPlayer = true;
		string where = "seed " + seed + " in game from " + TrafficLive.Clock(start) + ", contact cars at node 6";
		const double frame = 1.0 / 60.0;
		double race = 0.0;
		int n = live.Count;
		bool Near(int i, double reach)
		{
			byte k = live.Where(i, out double x, out double z);
			return k != TrafficLive.None && (x - k6.X) * (x - k6.X) + (z - k6.Z) * (z - k6.Z) < reach * reach;
		}
		// A parked car and one on its way out or in, near the player (looked for over up to three minutes).
		int parked = -1, way = -1;
		while (race < 180.0 && (parked < 0 || way < 0))
		{
			race += frame;
			live.Advance(live.DayAt(race));
			for (int i = 0; i < n; i++)
			{
				if (parked < 0 && Near(i, 500.0) && live.Where(i, out double _, out double _) == TrafficLive.Parked && !live.Manoeuvring(i) && live.CanStand(i))
				{
					parked = i;
				}
				if (way < 0 && i != parked && Near(i, 800.0) && live.Manoeuvring(i))
				{
					way = i;
				}
			}
		}
		if (!Check(parked >= 0 && way >= 0, "game contact", where + ": no parked car (" + parked + ") or car on its way out or in (" + way + ") near the player"))
		{
			return;
		}
		int[] held = { parked, way };
		double[] hx = new double[2], hz = new double[2], ax = new double[2];
		double wound = 0.0;
		for (int k = 0; k < 2; k++)
		{
			live.Pose(held[k], live.DayAt(race), out double dx, out double dz, out double _, out double _, out double _);
			live.Freeze(held[k], true, live.DayAt(race));
			live.Pose(held[k], live.DayAt(race), out hx[k], out hz[k], out double _, out double _, out double _);
			ax[k] = live.Mi.Agents[held[k]] != null ? live.Mi.Agents[held[k]].X : double.NaN;
			wound = Math.Max(wound, Math.Sqrt((dx - hx[k]) * (dx - hx[k]) + (dz - hz[k]) * (dz - hz[k])));
		}
		double moved = 0.0, agentMoved = 0.0;
		double until = race + 60.0;
		while (race < until)
		{
			race += frame;
			live.Advance(live.DayAt(race));
			for (int k = 0; k < 2; k++)
			{
				int c = held[k];
				if (live.Pose(c, live.DayAt(race), out double x, out double z, out double _, out double _, out double _) != TrafficLive.None)
				{
					double mv = Math.Sqrt((x - hx[k]) * (x - hx[k]) + (z - hz[k]) * (z - hz[k]));
					if (mv > moved + 1e-4 && Environment.GetEnvironmentVariable("TRAFFICSIM_CONTACT_TRACE") == "1")
					{
						Console.WriteLine("    held car " + c + " moved " + mv.ToString("0.00") + " at " + race.ToString("0.00") + " s, manoeuvring " + live.Manoeuvring(c) + ", " + live.M.Cars[c].Mode);
					}
					moved = Math.Max(moved, mv);
				}
				Micro.Agent a = live.Mi.Agents[c];
				if (a != null && !double.IsNaN(ax[k]) && a.HoldUntil > live.Mi.T)
				{
					agentMoved = Math.Max(agentMoved, Math.Abs(a.X - ax[k]));
				}
			}
		}
		Check(wound < 1.0, "game contact", where + ": a car held still jumped " + wound.ToString("0.00") + " as it was held");
		Check(moved < 0.6, "game contact", where + ": a car held still moved " + moved.ToString("0.00") + " (cars " + parked + ", " + way + ")");
		Check(agentMoved < 0.6, "game contact", where + ": a car held in the lane for its way out moved " + agentMoved.ToString("0.00") + " while held still");
		double jump = 0.0;
		double[] lx = (double[])hx.Clone(), lz = (double[])hz.Clone();
		foreach (int c in held)
		{
			live.Freeze(c, false);
		}
		until = race + 20.0;
		while (race < until)
		{
			race += frame;
			live.Advance(live.DayAt(race));
			for (int k = 0; k < 2; k++)
			{
				if (live.Pose(held[k], live.DayAt(race), out double x, out double z, out double _, out double _, out double _) == TrafficLive.None)
				{
					continue;
				}
				jump = Math.Max(jump, Math.Sqrt((x - lx[k]) * (x - lx[k]) + (z - lz[k]) * (z - lz[k])));
				lx[k] = x;
				lz[k] = z;
			}
		}
		Check(jump < 2.0, "game contact", where + ": a car let go jumped " + jump.ToString("0.0") + " in a frame");
		// A parked car knocked (the owner: it carries on its day later): held while its body lies where it was knocked, then let go, it
		// must still set off on its plans. The one held above: held again over its next departure time, then let go.
		int again = parked;
		double leave = live.M.Cars[again].LeaveAt;
		bool setOff = false;
		if (live.M.Cars[again].Mode == Meso.Mode.Parked && leave < live.M.Now + 1800.0)
		{
			live.Freeze(again, true, live.DayAt(race));
			while (live.M.Now < leave + 60.0)
			{
				race += 0.25;
				live.Advance(live.DayAt(race));
			}
			Check(live.M.Cars[again].Mode == Meso.Mode.Parked || live.Where(again, out double _, out double _) == TrafficLive.Parked, "game contact", where + ": a knocked parked car held still drove off");
			live.Freeze(again, false);
			for (double t = race + 600.0; race < t && !setOff; )
			{
				race += 0.25;
				live.Advance(live.DayAt(race));
				setOff = live.M.Cars[again].Mode != Meso.Mode.Parked;
			}
			Check(setOff, "game contact", where + ": a knocked parked car let go never set off on its day (car " + again + ")");
		}
		// Knocked out of the day: a parked car, one on its way out or in, a micro car driving (and its household's day goes on).
		int lostParked = -1, lostWay = -1, lostDriving = -1;
		until = race + 180.0;
		while (race < until && (lostParked < 0 || lostWay < 0 || lostDriving < 0))
		{
			race += frame;
			live.Advance(live.DayAt(race));
			for (int i = 0; i < n; i++)
			{
				if (i == parked || i == way || live.M.Cars[i].Mode == Meso.Mode.Gone)
				{
					continue;
				}
				if (lostParked < 0 && Near(i, 500.0) && live.Where(i, out double _, out double _) == TrafficLive.Parked && !live.Manoeuvring(i))
				{
					lostParked = i;
					live.LoseCar(i);
				}
				else if (lostWay < 0 && Near(i, 800.0) && live.Manoeuvring(i))
				{
					lostWay = i;
					live.LoseCar(i);
				}
				else if (lostDriving < 0 && Near(i, 400.0) && live.Mi.Agents[i] != null && live.Mi.Agents[i].V > 3.0)
				{
					lostDriving = i;
					live.LoseCar(i);
				}
			}
		}
		Check(lostParked >= 0 && lostWay >= 0 && lostDriving >= 0, "game contact", where + ": not every kind of car found to knock (" + lostParked + ", " + lostWay + ", " + lostDriving + ")");
		int[] lost = { lostParked, lostWay, lostDriving };
		int drawnAgain = 0, departures = live.M.Departures, bad = 0;
		until = race + 300.0;
		while (race < until)
		{
			race += frame;
			live.Advance(live.DayAt(race));
			foreach (int c in lost)
			{
				if (c >= 0 && (live.M.Cars[c].Mode != Meso.Mode.Gone || live.Pose(c, live.DayAt(race), out double _, out double _, out double _, out double _, out double _) != TrafficLive.None))
				{
					drawnAgain++;
				}
			}
			int queued = 0, driving = 0;
			foreach (Meso.Queue q in live.M.Queues)
			{
				queued += q.Cars.Count;
			}
			foreach (Meso.Vehicle v in live.M.Cars)
			{
				driving += v.Mode == Meso.Mode.Driving ? 1 : 0;
				if (v.Mode == Meso.Mode.Gone && v.Lane >= 0)
				{
					bad++;
				}
			}
			bad += queued != driving ? 1 : 0;
		}
		Console.WriteLine("  game, " + where + ": cars " + parked + " (parked) and " + way + " (on its way) held still a minute, moved " + moved.ToString("0.00") + ", let go with a jump of "
			+ jump.ToString("0.0") + "; knocked out of the day " + string.Join(", ", lost) + ", " + (live.M.Departures - departures) + " departures after");
		Check(drawnAgain == 0, "game contact", where + ": a car knocked out of its day came back (" + drawnAgain + " frames)");
		Check(bad == 0, "game contact", where + ": the queues don't hold every car driving after cars were knocked out (" + bad + " frames)");
		Check(live.M.Departures > departures, "game contact", where + ": the day stopped after cars were knocked out");
	}
}
