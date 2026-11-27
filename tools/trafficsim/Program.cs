using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// The traffic rework's headless checks (docs/TRAFFIC_REWORK.md, section 5), grown phase by phase. Phase 2: each race's plans; phase 3:
// the meso (Meso.cs) running each race's whole day from 03:00 on the lane graph, and the first seed's from its race's start hour as well:
// every car in exactly one place every 10 s (conservation), no car held at a lane's end over Meso.Stuck (a deadlock), every leg routed,
// every planned trip made and every car home by the day's end, the first seed's day the same run twice; for the first seed the
// simulated day against the surveys (hours, counts by road), the delays and the worst queues. Phase 4 (MicroDay.cs): the same day with the
// micro level round pretend players, every visible fault failing it, and the micro against the meso (consistency).
//   dotnet run -c Release --project tools/trafficsim -- [first seed] [seeds]
// Builds the world and the traffic's map once (and checks 2 race seeds in 7 fall on a weekend), then the plans (Schedules.cs) for each race seed (32 from 1 by default) and checks:
// the plans the same built twice; every household's car there and no other; each car's day starting and ending at its home, its stops
// wanted in order, every leg's route joined up from the stop it leaves to the stop it reaches; every car home before the next day on
// empty roads; through cars on the trunk; the share of the day spent driving the survey's (Traffic.DrivingShare). For the first seed
// it reports the demand against the surveys: trips and cars on the road by hour (FHWA NT-3, PennDOT TPG 9), the purposes' mix and trip
// lengths (NHTS 2017), counts by road class as a day's vehicles at real time (PennDOT's counters), the places' parking through the day,
// and the state at the race's start hour. Exits 1 if a check fails, naming the failing checks on its last line.
internal static partial class Program
{
	private static int failures;
	private static readonly SortedSet<string> failed = new SortedSet<string>();

	private static bool Check(bool ok, string name, string what)
	{
		if (!ok)
		{
			failures++;
			failed.Add(name);
			if (failures < 40)
			{
				Console.WriteLine("  FAIL " + name + ": " + what);
			}
		}
		return ok;
	}

	private static int Main(string[] args)
	{
		int first = args.Length > 0 ? int.Parse(args[0]) : 1;
		int seeds = args.Length > 1 ? int.Parse(args[1]) : 32;
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Network.Ensure();
		Console.WriteLine("layout built in " + watch.ElapsedMilliseconds + " ms");
		Traffic.Ensure();
		Console.WriteLine("traffic's map: " + Traffic.Report);
		int weekends = 0;
		for (int seed = 1; seed <= 7000; seed++)
		{
			weekends += Schedules.WeekendOf(seed) ? 1 : 0;
		}
		Console.WriteLine("weekend days: " + weekends + " of 7,000 race seeds (2,000 expected)");
		watch.Restart();
		Lanes.Ensure();
		Console.WriteLine("lane graph: " + Lanes.Report);
		Check(Math.Abs(weekends - 2000) < 120, "weekends", weekends + " of 7,000 seeds fall on a weekend");
		if (Environment.GetEnvironmentVariable("TRAFFICSIM_GEOM") == "1")
		{
			Geometry();
			return 0;
		}
		if (Environment.GetEnvironmentVariable("TRAFFICSIM_GAME") == "only")
		{
			for (int seed = first; seed < first + seeds; seed++)
			{
				GameDay(seed, 10.0);
			}
			Console.WriteLine(failures == 0 ? "all checks pass" : failures + " failures; failing: " + string.Join(", ", failed));
			return failures == 0 ? 0 : 1;
		}
		for (int seed = first; seed < first + seeds; seed++)
		{
			Schedules s = Schedules.Build(seed);
			if (seed == first && Environment.GetEnvironmentVariable("TRAFFICSIM_GAME") != "0")
			{
				GameDay(seed, 10.0);
			}
			Console.WriteLine("seed " + seed + ": " + s.Report() + "; " + s.Routes + " routes, " + s.Unrouted + " unrouted, " + s.Late + " tours late for a car, "
				+ s.Dropped + " given up");
			Checks(s);
			if (seed == first)
			{
				Demand(s);
			}
			Simulate(s, 0.0, seed == first);
			if (seed == first)
			{
				Simulate(s, Schedules.StartOf(StartHourOf(seed)), true);
			}
			MicroDay(s, 0.0, seed == first, false);
			if (seed == first && Environment.GetEnvironmentVariable("TRAFFICSIM_EVERYWHERE") != "0")
			{
				Meso meso = Meso.Start(s, 0.0, 0.0);
				meso.Trips = new List<(int, int, double, double, bool)>();
				meso.Run(Traffic.DaySeconds);
				Consistency(meso, MicroDay(s, 0.0, true, true));
			}
		}
		Console.WriteLine(failures == 0 ? "all checks pass" : failures + " failures; failing: " + string.Join(", ", failed));
		return failures == 0 ? 0 : 1;
	}

	// ---- checks ---------------------------------------------------------------------------------------------------------

	private static ulong Hash(Schedules s)
	{
		ulong h = 1469598103934665603UL;
		void Add(double v)
		{
			h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL;
		}
		foreach (Schedules.Car c in s.Cars)
		{
			Add(c.Household);
			Add(c.Index);
			Add(c.Through ? 1 : 0);
			Add(c.Start.S);
			Add(c.Way);
			foreach (Schedules.Stop st in c.Stops)
			{
				Add((int)st.Kind);
				Add((int)st.Purpose);
				Add(st.Dest);
				Add(st.Host);
				Add(st.At.Edge);
				Add(st.At.S);
				Add(st.WantLeave);
				Add(st.Stay);
			}
			foreach (Schedules.Leg l in c.Legs)
			{
				Add(l.Length);
				Add(l.Time);
				foreach (int r in l.Route)
				{
					Add(r);
				}
			}
		}
		return h;
	}

	private static void Checks(Schedules s)
	{
		Check(Hash(s) == Hash(Schedules.Build(s.Seed)), "repeatable", "seed " + s.Seed + " built twice differs");
		// Every household's cars, once each, and the through cars.
		int[] seen = new int[Traffic.Households.Count];
		int through = 0;
		foreach (Schedules.Car c in s.Cars)
		{
			if (c.Through)
			{
				through++;
				Check(Network.Roads[Traffic.Edges[c.Start.Edge].Road].Kind == RoadKind.Trunk, "through", "a through car starts off the trunk");
				continue;
			}
			seen[c.Household]++;
		}
		Check(through == (Wrap.A >= 0 ? Schedules.Circling : 0), "through", through + " through cars");
		for (int h = 0; h < seen.Length; h++)
		{
			Check(seen[h] == Traffic.Households[h].Cars, "cars", "household " + h + " has " + Traffic.Households[h].Cars + " cars, " + seen[h] + " planned");
		}
		foreach (Schedules.Car c in s.Cars)
		{
			if (c.Through)
			{
				continue;
			}
			string who = "seed " + s.Seed + " household " + c.Household + " car " + c.Index;
			Traffic.Anchor home = Traffic.Households[c.Household].Home;
			Check(c.Stops.Length >= 1 && c.Legs.Length == c.Stops.Length - 1, "chains", who + ": " + c.Stops.Length + " stops, " + c.Legs.Length + " legs");
			if (c.Stops.Length == 0)
			{
				continue;
			}
			Schedules.Stop a = c.Stops[0], z = c.Stops[c.Stops.Length - 1];
			Check(a.Kind == Schedules.StopKind.Home && Same(a.At, home) && z.Kind == Schedules.StopKind.Home && Same(z.At, home), "chains", who + ": not from home to home");
			Check(z.WantLeave == double.MaxValue, "chains", who + ": the last stop is left");
			for (int i = 0; i + 1 < c.Stops.Length; i++)
			{
				Schedules.Stop st = c.Stops[i];
				Check(st.WantLeave >= 0.0 && st.WantLeave < Traffic.DaySeconds, "chains", who + " stop " + i + " left at " + st.WantLeave.ToString("0"));
				Check(i == 0 || st.WantLeave >= c.Stops[i - 1].WantLeave, "order", who + " stop " + i + " wanted left before the one before it");
				Check(st.Kind == Schedules.StopKind.Home || st.Stay >= Schedules.MinStay, "chains", who + " stop " + i + " stays " + st.Stay.ToString("0"));
				Check(Joined(c.Legs[i], st.At, c.Stops[i + 1].At, out string why), "routes", who + " leg " + i + ": " + why);
			}
			// Home before the next day on empty roads.
			Schedules.Where w = Schedules.At(c, Traffic.DaySeconds);
			Check(w.State == Schedules.State.Parked && w.Index == c.Stops.Length - 1, "home", who + ": not home by the day's end (" + w.State + " " + w.Index + ")");
		}
		double share = s.PlannedSeconds / Math.Max(1.0, s.TargetSeconds);
		Check(share > 0.97 && share < 1.03, "driving", "seed " + s.Seed + ": " + (share * 100.0).ToString("0") + "% of the survey's driving planned");
		Check(s.Unrouted == 0, "routes", "seed " + s.Seed + ": " + s.Unrouted + " legs with no route");
	}

	private static bool Same(Traffic.Anchor a, Traffic.Anchor b)
	{
		return a.Edge == b.Edge && a.S == b.S && a.X == b.X && a.Z == b.Z;
	}

	/// <summary>Whether a leg's route starts on the edge it leaves from, ends on the one it reaches, each edge leading into the next.</summary>
	private static bool Joined(Schedules.Leg l, Traffic.Anchor from, Traffic.Anchor to, out string why)
	{
		why = "";
		if (l.Route.Length == 0)
		{
			why = "no route";
			return false;
		}
		if (l.Route[0] >> 1 != from.Edge || l.Route[l.Route.Length - 1] >> 1 != to.Edge)
		{
			why = "not from its stop to the next";
			return false;
		}
		for (int i = 0; i + 1 < l.Route.Length; i++)
		{
			Traffic.Edge e = Traffic.Edges[l.Route[i] >> 1], f = Traffic.Edges[l.Route[i + 1] >> 1];
			int end = (l.Route[i] & 1) == 0 ? e.B : e.A, start = (l.Route[i + 1] & 1) == 0 ? f.A : f.B;
			// (Or through the wrap's tunnels: the far end's tunnel is the way on, by the lane graph's jump.)
			bool jump = Meso.LanesOf(l.Route[i]).Any(ln => Lanes.All[ln].Out.Any(c => Lanes.Connectors[c].Turn == Lanes.Jump && Meso.CarriagewayOf(Lanes.Connectors[c].To) == l.Route[i + 1]));
			if (end != start && !jump)
			{
				why = "edges " + (l.Route[i] >> 1) + " and " + (l.Route[i + 1] >> 1) + " don't meet";
				return false;
			}
		}
		if (double.IsNaN(l.Time) || l.Time <= 0.0 || l.Length <= 0.0)
		{
			why = "length " + l.Length + ", time " + l.Time;
			return false;
		}
		return true;
	}

	// ---- the demand against the surveys --------------------------------------------------------------------------------

	/// <summary>A day's count on a road at real time from one in the world's day: the clock runs 36 times fast, cars drive in real time.</summary>
	private const double RealDay = 86400.0 / Traffic.DaySeconds;

	private static void Demand(Schedules s)
	{
		Console.WriteLine();
		Console.WriteLine("demand, seed " + s.Seed + " (a " + (s.Weekend ? "weekend" : "weekday") + "; local traffic at " + Traffic.TrafficAmount + " of the survey's):");
		int households = 0, withCars = 0, cars = 0;
		foreach (Traffic.Household h in Traffic.Households)
		{
			households++;
			withCars += h.Cars > 0 ? 1 : 0;
			cars += h.Cars;
		}
		// Trips by the hour they start, cars on the road by the hour (sampled every 5 s), on empty roads.
		double[] starts = new double[24], moving = new double[24];
		int legs = 0;
		double length = 0.0;
		double[] lengthBy = new double[4];
		int[] legsBy = new int[4];
		foreach (Schedules.Car c in s.Cars)
		{
			if (c.Through)
			{
				continue;
			}
			double arrived = 0.0;
			for (int i = 0; i < c.Legs.Length; i++)
			{
				double leave = Schedules.Leave(c.Stops[i], arrived);
				starts[(int)Schedules.HourOf(leave) % 24]++;
				legs++;
				length += c.Legs[i].Length;
				Traffic.Purpose p = c.Stops[i + 1].Kind == Schedules.StopKind.Home ? c.Stops[i + 1].Purpose : c.Stops[i + 1].Purpose;
				lengthBy[(int)p] += c.Legs[i].Length;
				legsBy[(int)p]++;
				arrived = leave + c.Legs[i].Time;
			}
		}
		int peak = 0;
		double peakAt = 0.0;
		for (double t = 0.0; t < Traffic.DaySeconds; t += 5.0)
		{
			int n = 0;
			foreach (Schedules.Car c in s.Cars)
			{
				if (!c.Through && Schedules.At(c, t).State == Schedules.State.Driving)
				{
					n++;
				}
			}
			moving[(int)Schedules.HourOf(t) % 24] += n;
			if (n > peak)
			{
				peak = n;
				peakAt = t;
			}
		}
		Console.WriteLine("  " + households + " households (" + withCars + " with cars), " + cars + " cars; " + legs + " trips: "
			+ ((double)legs / withCars).ToString("0.00") + " a household with a car in a 40-minute day (the survey's 5.1 at real time, thinned to its share of the"
			+ " day driving), " + (s.Keep * 100.0).ToString("0") + "% of candidate tours kept");
		Console.WriteLine("  most cars on the road at once: " + peak + " at " + Clock(Schedules.HourOf(peakAt)) + " (plus " + Schedules.Circling
			+ " through); the survey says about " + (400 * Traffic.TrafficAmount).ToString("0") + " at the evening peak");
		double[] trips = s.Weekend ? Schedules.TripsSaturday : Schedules.TripsWeekday;
		Console.WriteLine("  hour   trips  NT-3   moving  TPG 9");
		double sumStarts = starts.Sum(), sumMoving = moving.Sum();
		double worstTrips = 0.0, worstMoving = 0.0;
		for (int h = 0; h < 24; h++)
		{
			double a = starts[h] / sumStarts * 100.0, b = moving[h] / sumMoving * 100.0;
			worstTrips = Math.Max(worstTrips, Math.Abs(a - trips[h]));
			worstMoving = Math.Max(worstMoving, Math.Abs(b - Schedules.HoursCounted[h]));
			Console.WriteLine("  " + h.ToString("00") + "   " + a.ToString("0.0").PadLeft(6) + trips[h].ToString("0.0").PadLeft(6) + b.ToString("0.0").PadLeft(8)
				+ Schedules.HoursCounted[h].ToString("0.0").PadLeft(7));
		}
		Console.WriteLine("  hours: trips within " + worstTrips.ToString("0.0") + " points of NT-3's, cars moving within " + worstMoving.ToString("0.0")
			+ " of the counters'; peak hour " + (moving.Max() / sumMoving * 100.0).ToString("0.0") + "% of the day's driving (PennDOT's K-factors: 9 to 11)");
		string[] names = { "work", "shop", "errand", "social" };
		double[] nhtsShare = { 24, 20, 23, 22 };
		double[] nhtsMiles = { 12.0, 7.0, 6.9, 10.6 };
		Console.WriteLine("  purpose  tours  share (NHTS of these four)  trip miles (NHTS, on a map 6 across)");
		int tours = s.TourCount.Sum();
		for (int p = 0; p < 4; p++)
		{
			Console.WriteLine("  " + names[p].PadRight(8) + s.TourCount[p].ToString().PadLeft(6) + (s.TourCount[p] * 100.0 / tours).ToString("0").PadLeft(6) + "% ("
				+ (nhtsShare[p] / 89.0 * 100.0).ToString("0") + "%)" + "".PadRight(14) + Miles(lengthBy[p] / Math.Max(1, legsBy[p])).ToString("0.0").PadLeft(5)
				+ " (" + nhtsMiles[p].ToString("0.0") + ")");
		}
		Console.WriteLine("  average trip " + Miles(length / Math.Max(1, legs)).ToString("0.0") + " miles (NHTS 9.6); " + s.Chained + " commutes with a stop on the way home");
		Volumes(s);
		Parking(s);
		Start(s);
	}

	private static double Miles(double units)
	{
		return units / 3.3 / 1609.344;
	}

	private static string Clock(double hour)
	{
		int m = (int)Math.Floor(hour * 60.0 + 0.5) % 1440;
		return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
	}

	/// <summary>
	/// Each road's count as a day's vehicles at real time (each edge's passes in the world's day times 36), by road class, against
	/// PennDOT's counters: the median edge (by length) and the busiest.
	/// </summary>
	private static void Volumes(Schedules s)
	{
		double[] count = new double[Traffic.Edges.Count];
		foreach (Schedules.Car c in s.Cars)
		{
			foreach (Schedules.Leg l in c.Legs)
			{
				foreach (int r in l.Route)
				{
					count[r >> 1]++;
				}
			}
		}
		// Through cars: each passes each stretch of the trunk once a lap.
		double lap = 0.0;
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk && Traffic.OnMain(e))
			{
				lap += Traffic.Edges[e].Time;
			}
		}
		double laps = lap > 0.0 ? Traffic.DaySeconds / lap : 0.0;
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk && Traffic.OnMain(e))
			{
				count[e] += Schedules.Circling * laps;
			}
		}
		Console.WriteLine("  a day's vehicles at real time (both ways) by road: median by length, 90th percentile, busiest; PennDOT's counters");
		string[] said = { "rural principal arterials: median 5,400 (2,600 to 21,600)", "rural minor arterials: median 5,000 (2,000 to 15,700)",
			"rural collectors: median 1,800 (400 to 8,800)", "urban collectors and locals: up to 5,700", "local: about 9.4 a house served (ITE 210)",
			"a drive: 9.4 a house" };
		for (int k = 0; k <= (int)RoadKind.Drive; k++)
		{
			List<(double v, double len)> list = new List<(double, double)>();
			for (int e = 0; e < Traffic.Edges.Count; e++)
			{
				if ((int)Network.Roads[Traffic.Edges[e].Road].Kind == k && Traffic.OnMain(e))
				{
					list.Add((count[e] * RealDay, Traffic.Edges[e].Length));
				}
			}
			if (list.Count == 0)
			{
				continue;
			}
			list.Sort((a, b) => a.v.CompareTo(b.v));
			double total = list.Sum(x => x.len);
			double Pct(double q)
			{
				double acc = 0.0;
				foreach ((double v, double len) in list)
				{
					acc += len;
					if (acc >= q * total)
					{
						return v;
					}
				}
				return list[list.Count - 1].v;
			}
			Console.WriteLine("  " + ((RoadKind)k).ToString().ToLower().PadRight(10) + Pct(0.5).ToString("0").PadLeft(7) + Pct(0.9).ToString("0").PadLeft(8)
				+ list[list.Count - 1].v.ToString("0").PadLeft(8) + "   " + said[k]);
		}
		Console.WriteLine("  (the trunk with " + Schedules.Circling + " through cars at " + laps.ToString("0.0") + " laps a day each)");
	}

	/// <summary>Each place's parked cars through the day on empty roads against its spaces: the busiest, and those planned over.</summary>
	private static void Parking(Schedules s)
	{
		int n = Traffic.Destinations.Count;
		int[] most = new int[n];
		double[] mostAt = new double[n];
		int[] now = new int[n];
		for (double t = 0.0; t < Traffic.DaySeconds; t += 10.0)
		{
			Array.Clear(now, 0, n);
			foreach (Schedules.Car c in s.Cars)
			{
				if (c.Through)
				{
					continue;
				}
				Schedules.Where w = Schedules.At(c, t);
				if (w.State == Schedules.State.Parked && c.Stops[w.Index].Kind == Schedules.StopKind.Place)
				{
					now[c.Stops[w.Index].Dest]++;
				}
			}
			for (int d = 0; d < n; d++)
			{
				if (now[d] > most[d])
				{
					most[d] = now[d];
					mostAt[d] = t;
				}
			}
		}
		int over = 0;
		List<int> order = Enumerable.Range(0, n).Where(d => Traffic.Destinations[d].Spaces > 0).OrderByDescending(d => (double)most[d] / Traffic.Destinations[d].Spaces).ThenBy(d => d).ToList();
		foreach (int d in order)
		{
			over += most[d] > Traffic.Destinations[d].Spaces ? 1 : 0;
		}
		Console.WriteLine("  parking: " + over + " of " + order.Count + " places planned fuller than their spaces at some hour (the simulation sends the rest on to"
			+ " the next lot); the fullest:");
		foreach (int d in order.Take(8))
		{
			Traffic.Destination dest = Traffic.Destinations[d];
			Console.WriteLine("    " + (dest.Kind + " " + dest.Name).PadRight(28) + most[d].ToString().PadLeft(4) + " of " + dest.Spaces.ToString().PadRight(4) + " at "
				+ Clock(Schedules.HourOf(mostAt[d])));
		}
	}

	/// <summary>The race's start: where the plans have every car at the start hour, which the simulation starts from.</summary>
	private static void Start(Schedules s)
	{
		double hour = StartHourOf(s.Seed);
		double t = Schedules.StartOf(hour);
		int home = 0, away = 0, beyond = 0, driving = 0;
		foreach (Schedules.Car c in s.Cars)
		{
			if (c.Through)
			{
				continue;
			}
			Schedules.Where w = Schedules.At(c, t);
			if (w.State == Schedules.State.Driving)
			{
				driving++;
			}
			else if (c.Stops[w.Index].Kind == Schedules.StopKind.Home)
			{
				home++;
			}
			else if (c.Stops[w.Index].Kind == Schedules.StopKind.Away)
			{
				beyond++;
			}
			else
			{
				away++;
			}
		}
		Console.WriteLine("  the race starts at " + Clock(hour) + ": " + home + " cars at home, " + away + " parked out, " + beyond + " beyond the map, " + driving
			+ " on the road (and " + Schedules.Circling + " through)");
	}

	// ---- the simulation (Meso) ------------------------------------------------------------------------------------------

	/// <summary>
	/// Runs the meso from day second <paramref name="start"/> to the day's end and checks: every car in one place all day (conservation,
	/// every 10 s), no car stuck at a lane's end for <see cref="Meso.Stuck"/> (a deadlock), every leg routed, every trip planned made
	/// and every car home by the day's end; reports the day against the surveys, the delays and the worst queues if <paramref name="report"/>.
	/// </summary>
	private static void Simulate(Schedules s, double start, bool report)
	{
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Meso m = Meso.Start(s, start, start);
		double built = watch.Elapsed.TotalMilliseconds;
		int planned = 0;
		foreach (Schedules.Car c in s.Cars)
		{
			if (c.Through)
			{
				continue;
			}
			double arrived = 0.0;
			for (int i = 0; i < c.Legs.Length; i++)
			{
				double leave = Schedules.Leave(c.Stops[i], arrived);
				planned += leave >= start ? 1 : 0;
				arrived = leave + c.Legs[i].Time;
			}
		}
		string where = "seed " + s.Seed + " from " + Clock(Schedules.HourOf(start));
		bool conserved = true;
		for (double t = start + 10.0; t <= Traffic.DaySeconds + 1e-9 && conserved; t += 10.0)
		{
			m.Run(t);
			conserved = Check(m.Conserved(out string why), "conservation", where + " at " + Clock(Schedules.HourOf(t)) + ": " + why);
		}
		m.Run(Traffic.DaySeconds);
		double ran = watch.Elapsed.TotalMilliseconds - built;
		int notHome = 0, onRoad = 0;
		foreach (Meso.Vehicle v in m.Cars)
		{
			if (v.Through)
			{
				continue;
			}
			Schedules.Car c = s.Cars[v.Car];
			if (v.Mode == Meso.Mode.Driving)
			{
				onRoad++;
			}
			if (v.Mode != Meso.Mode.Parked || v.Stop != c.Stops.Length - 1)
			{
				notHome++;
				if (notHome <= 5)
				{
					double planHome = 0.0, arr = 0.0;
					for (int i = 0; i < c.Legs.Length; i++)
					{
						planHome = Schedules.Leave(c.Stops[i], arr) + c.Legs[i].Time;
						arr = planHome;
					}
					m.Where(v.Car, out int ln, out int cn, out double al);
					Console.WriteLine("    not home: car " + v.Car + " " + v.Mode + " stop " + v.Stop + " of " + c.Stops.Length + ", planned home at " + planHome.ToString("0")
						+ ", trip from " + v.TripStart.ToString("0") + " free " + v.TripFree.ToString("0") + " s, lane " + ln + " conn " + cn + " held since " + v.HeldSince.ToString("0")
						+ ", by a light " + v.HeldByLight + ", connector " + v.Next + " of " + v.Path.Count + "; planned legs " + string.Join(" ", c.Legs.Select(lg => lg.Time.ToString("0"))));
				}
			}
		}
		Console.WriteLine("  meso, " + where + ": " + m.Departures + " trips set off (" + planned + " planned from then), " + m.Arrivals + " arrived, "
			+ m.Redirects + " sent on from a full lot, " + m.Reroutes + " rerouted round a queue, " + m.Unrouted + " unrouted; " + notHome + " not home at the day's end ("
			+ onRoad + " on the road); started in " + built.ToString("0") + " ms, ran the day in " + ran.ToString("0") + " ms (" + m.Routes + " routes, "
			+ m.RouteMs.ToString("0") + " ms)");
		Check(m.Unrouted == 0, "meso routes", where + ": " + m.Unrouted + " legs with no way on the lane graph");
		Check(m.StuckCars.Count == 0, "deadlock", where + ": " + m.StuckCars.Count + " cars held over " + Meso.Stuck + " s"
			+ string.Concat(m.StuckCars.Take(5).Select(x => "; car " + x.car + " at the end of lane " + x.lane + " (node " + Lanes.All[x.lane].To + ") from "
				+ Clock(Schedules.HourOf(x.since)))));
		if (report || m.StuckCars.Count > 0)
		{
			foreach ((int car, int lane, double since) in m.StuckCars.Take(8))
			{
				Console.WriteLine("    stuck: car " + car + " " + m.Why(car));
			}
		}
		Check(notHome == 0, "home", where + ": " + notHome + " cars not home at the day's end");
		Check(start > 0.0 || m.Departures == planned, "trips", where + ": " + m.Departures + " of " + planned + " planned trips set off");
		if (report)
		{
			// Not needed (the host runs it and sends what happens), but nothing in it is left to chance: the same day twice.
			Meso again = Meso.Start(s, start, start);
			again.Run(Traffic.DaySeconds);
			Check(again.Arrivals == m.Arrivals && again.DelaySum == m.DelaySum && again.Redirects == m.Redirects, "meso repeatable", where + ": run twice differs");
		}
		if (!report)
		{
			return;
		}
		double trips = Math.Max(1, m.Arrivals);
		Console.WriteLine("    delay over free roads: " + (m.DelaySum / trips).ToString("0.0") + " s a trip on average, worst " + m.WorstDelay.ToString("0")
			+ " s; peak " + m.PeakMoving + " cars on the road at " + Clock(Schedules.HourOf(m.PeakAt)) + " (and " + Schedules.Circling + " through)");
		if (start == 0.0)
		{
			double[] counted = s.Weekend ? Schedules.TripsSaturday : Schedules.TripsWeekday;
			double sumStarts = m.StartsByHour.Sum(), sumMoving = m.MovingByHour.Sum();
			double worstTrips = 0.0, worstMoving = 0.0;
			for (int h = 0; h < 24; h++)
			{
				worstTrips = Math.Max(worstTrips, Math.Abs(m.StartsByHour[h] / sumStarts * 100.0 - counted[h]));
				worstMoving = Math.Max(worstMoving, Math.Abs(m.MovingByHour[h] / sumMoving * 100.0 - Schedules.HoursCounted[h]));
			}
			Console.WriteLine("    simulated hours: trips within " + worstTrips.ToString("0.0") + " points of NT-3's, cars moving within " + worstMoving.ToString("0.0")
				+ " of the counters' (TPG 9); peak hour " + (m.MovingByHour.Max() / sumMoving * 100.0).ToString("0.0") + "% of the day's driving");
			Console.WriteLine("    a day's vehicles at real time (both ways), simulated: median by length, 90th percentile, busiest");
			for (int k = 0; k <= (int)RoadKind.Drive; k++)
			{
				List<(double v, double len)> list = new List<(double, double)>();
				for (int e = 0; e < Traffic.Edges.Count; e++)
				{
					if ((int)Network.Roads[Traffic.Edges[e].Road].Kind == k && Traffic.OnMain(e))
					{
						list.Add((m.EdgeCount[e] * RealDay, Traffic.Edges[e].Length));
					}
				}
				if (list.Count == 0)
				{
					continue;
				}
				list.Sort((a, b) => a.v.CompareTo(b.v));
				double total = list.Sum(x => x.len);
				double Pct(double q)
				{
					double acc = 0.0;
					foreach ((double v, double len) in list)
					{
						acc += len;
						if (acc >= q * total)
						{
							return v;
						}
					}
					return list[list.Count - 1].v;
				}
				Console.WriteLine("    " + ((RoadKind)k).ToString().ToLower().PadRight(10) + Pct(0.5).ToString("0").PadLeft(7) + Pct(0.9).ToString("0").PadLeft(8)
					+ list[list.Count - 1].v.ToString("0").PadLeft(8));
			}
		}
		Console.WriteLine("    the worst queues (longest a car stood at a lane's end, the most cars in it):");
		foreach (int l in Enumerable.Range(0, m.Queues.Length).OrderByDescending(l => m.Queues[l].LongestHeld).ThenBy(l => l).Take(6))
		{
			Lanes.Lane lane = Lanes.All[l];
			Traffic.Node n = Traffic.Nodes[lane.To];
			Console.WriteLine("      lane " + l + " (" + Network.Roads[Traffic.Edges[lane.Edge].Road].Kind.ToString().ToLower() + ", into node " + lane.To + " at "
				+ n.X.ToString("0") + ", " + n.Z.ToString("0") + (TrafficSignals.Has(lane.To) ? ", lit" : "") + "): " + m.Queues[l].LongestHeld.ToString("0") + " s, "
				+ m.Queues[l].Most + " cars of " + m.Queues[l].Storage);
		}
	}

	/// <summary>The hour a race from <paramref name="seed"/> starts at, as <c>Sky.StartHourOf</c> (Sky is left out of this tool).</summary>
	private static double StartHourOf(int seed)
	{
		ulong h = Noise.Mix((ulong)(uint)seed ^ 0x0E7A_7140UL);
		double u = (double)(h >> 11) * (1.0 / 9007199254740992.0);
		double v = (double)(Noise.Mix(h) >> 11) * (1.0 / 9007199254740992.0);
		return u < 0.75 ? 7.0 + 10.0 * v : 24.0 * v;
	}
}
