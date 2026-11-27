using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// Phase 5: the game's own path, headless (TrafficLive.cs, TrafficParking.cs): the race's traffic built as the game builds it on a worker,
// then advanced frame by frame on the race clock round one pretend player driving the lane graph, as TrafficCars runs it. Fails on: a car's
// drawn pose not a number; a drawn moving car jumping between frames further than its speed takes it (the wrap's jump and cars placed by
// the micro's boundary excepted: those are out of sight); two parked cars drawn in one another; a parked car on another road's paving, in
// a house or the water; a car parked but not drawn where it should be (its own space at home lost); the simulation falling behind the
// clock. Measured: the build's time, and the simulation's cost a second of the race (the frame-time budget).
internal static partial class Program
{
	private static void GameDay(int seed, double minutes)
	{
		double startHour = StartHourOf(seed);
		double start = Schedules.StartOf(startHour);
		Meso.Ensure();
		Walker w = Walkers(seed)[1];
		var watch = System.Diagnostics.Stopwatch.StartNew();
		TrafficLive live = TrafficLive.Build(seed, start, w.SX, w.SZ);
		double buildMs = watch.Elapsed.TotalMilliseconds;
		live.PlayerX = w.SX;
		live.PlayerZ = w.SZ;
		live.HasPlayer = true;
		string where = "seed " + seed + " in game from " + TrafficLive.Clock(start);
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
			long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
			live.Advance(now);
			double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			stepSum += ms;
			if (ms > worstStep)
			{
				worstStep = ms;
				worstAt = race;
			}
			if (live.M.Now + Meso.Step < Math.Min(now, Traffic.DaySeconds) - 1e-6)
			{
				behind++;
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
		foreach (string f in firsts)
		{
			Console.WriteLine("    " + f);
		}
		Check(badPose == 0, "game poses", where + ": " + badPose + " poses not a number");
		Check(jumps == 0, "game jumps", where + ": " + jumps + " drawn cars jumped between frames in sight");
		Check(overlaps == 0, "game parking", where + ": " + overlaps + " pairs of parked cars in one another");
		Check(offPaving == 0, "game parking", where + ": " + offPaving + " parked cars on another road, in a house or the water");
		Check(behind == 0, "game clock", where + ": the simulation fell behind the clock in " + behind + " frames");
		Check(buildMs < 6000.0, "game build", where + ": built in " + buildMs.ToString("0") + " ms");
		BlockDay(seed);
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
}
