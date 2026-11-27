using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Each race's plans for every car in the world (the traffic rework's phase 2, <c>docs/TRAFFIC_REWORK.md</c>): a household's car's
	/// day as a chain of stops (home, work, a shop on the way back, home, a visit in the evening, home), each with the time its driver
	/// wants to leave it and the least it stays. These are plans, not a timetable: a car leaves a stop at the later of the time wanted
	/// and its arrival plus the least stay, so a car held up on the road is simply late and its next stops shift (<see cref="Leave"/>).
	/// Nothing here says where a car is on the road; that is the simulation's (phase 3), which starts from <see cref="At"/>.
	///
	/// From the race's seed, with plain arithmetic, hashing and ties broken by index, under <see cref="Network"/>'s rules, so every
	/// machine gets the same plans (the host's simulation need not be, but a late joiner and the tools can read the plans alike).
	/// The figures are the surveys' (<c>docs/research/TRAFFIC_STUDIES.md</c>): tours by purpose (NHTS 2017), their hours (NHTS and
	/// FHWA NT-3), the share of the day households spend driving (<see cref="Traffic.DrivingShare"/>), places by gravity.
	///
	/// Time: day seconds from 03:00 (the quietest hour, when every car is home), 100 to the hour (<see cref="Traffic.DaySeconds"/>).
	/// Driving takes real seconds while the clock runs 36 times fast, so a drive spans an hour or more of the world's day; a stay is
	/// counted from leaving home for it (<see cref="Stop.WantLeave"/>), or every way home would land late in the night. No Unity types:
	/// <c>tools/trafficsim</c> builds and checks it.
	/// </summary>
	internal sealed class Schedules
	{
		// ---- the day's figures (docs/research/TRAFFIC_STUDIES.md) -------------------------------------------------

		/// <summary>The hour the day starts at (day second 0): the quietest, when every car is home.</summary>
		internal const double DayStartHour = 3.0;
		/// <summary>Day seconds in an hour of the world's clock.</summary>
		internal const double Hour = Traffic.DaySeconds / 24.0;

		/// <summary>
		/// No tour leaves home after 21:00: a drive spans an hour or more of the world's clock, so a later one would put the evening's
		/// traffic into the small hours the counters see empty.
		/// </summary>
		private const double LastOut = 18.0 * Hour;
		/// <summary>Every car is planned home by 02:30, before the next day starts.</summary>
		private const double HomeBy = Traffic.DaySeconds - 0.5 * Hour;
		/// <summary>The least a car stays anywhere (real seconds): long enough to park, get out, do something and come back.</summary>
		internal const double MinStay = 20.0;
		/// <summary>A car is not taken out again until it has been home this long.</summary>
		private const double Rest = 60.0;
		/// <summary>A tour wanting a car still out waits for it this long at most (an hour); later it is given up.</summary>
		private const double WaitForCar = Hour;
		/// <summary>Straight-line distance at which a place draws half as strongly as one next door: weight / (1 + (d / Reach)²).</summary>
		private const double Reach = 3000.0;
		/// <summary>
		/// The share of commutes with a stop on the way home (shopping, an errand): about a third of work tours chain one in the NHTS
		/// (McGuckin and Nakamoto, "Trips, Chains and Tours", 2004).
		/// </summary>
		private const double ChainShare = 0.3;
		/// <summary>
		/// Cars circling the trunk all day (through traffic: the wrap makes the trunk a loop). 100 keep a car in sight along the highway
		/// most of the time (the owner, 2026-11-24, found 10 too thin); about 18,000 a day on the trunk's count.
		/// </summary>
		internal const int Circling = 100;

		/// <summary>
		/// Tours a household makes in a day, by purpose (work per worker, the rest per household): weekday, weekend. A weekday's mix is the
		/// NHTS 2017's trips to or from work, shopping, errands and social and recreational (24, 20, 23, 22% of the year's; work a little
		/// more on a weekday): about 30, 21, 26, 23% of the tours. The old traffic's 0.86 a worker made work 41%.
		/// </summary>
		private static readonly double[] RateWeekday = { 0.54, 0.5, 0.62, 0.55 };
		private static readonly double[] RateWeekend = { 0.12, 0.75, 0.45, 0.85 };

		/// <summary>When tours leave home, by hour from midnight: weights (work, shop, errand, social), weekday then weekend.</summary>
		private static readonly double[][] HoursWeekday =
		{
			new double[] { 0, 0, 0, 1, 5, 14, 17, 10, 5, 2, 1.5, 1.5, 1.5, 1.5, 1.5, 1, 1, 0.5, 0.3, 0.2, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0, 0.2, 1, 3, 6, 8, 9, 9, 9, 9, 9.5, 9.5, 8, 5, 2, 0.5, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.5, 2, 5, 7, 8, 8, 8, 8, 8, 8, 8.5, 8.5, 6, 3, 1.5, 0.5, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0, 0, 0.5, 1, 2, 3, 4, 4, 4, 4.5, 5.5, 8, 10, 10, 7, 3, 0, 0, 0, 0 },
		};
		private static readonly double[][] HoursWeekend =
		{
			new double[] { 0, 0, 0, 1, 4, 8, 10, 8, 6, 4, 3, 3, 2, 2, 2, 1.5, 1, 0.5, 0, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.5, 2, 5, 8, 9.5, 10, 10, 10, 9.5, 8, 6, 4, 2, 0.5, 0, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.3, 1, 3, 6, 8, 9, 9, 9, 9, 8.5, 7, 5, 3, 1.5, 0.5, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.3, 1, 3, 5, 6, 6, 6.5, 7, 7.5, 8, 8, 7, 5, 2, 0.5, 0, 0, 0, 0, 0 },
		};

		/// <summary>What the counters see on Pennsylvania's central rural collectors (PennDOT TPG 9), percent of the day from midnight.</summary>
		internal static readonly double[] HoursCounted =
		{
			0.45, 0.28, 0.28, 0.48, 1.08, 2.79, 4.66, 5.99, 5.45, 5.33, 5.56, 6.09, 6.42, 6.38, 7.06, 8.21, 8.77, 7.68, 5.50, 4.02, 3.14, 2.12, 1.39, 0.87,
		};
		/// <summary>Vehicle trips by hour on a weekday (FHWA NT-3, 2001 NHTS), percent of the day from midnight.</summary>
		internal static readonly double[] TripsWeekday =
		{
			0.3, 0.2, 0.2, 0.1, 0.5, 1.3, 3.2, 7.7, 5.9, 4.5, 5.1, 5.9, 6.2, 6.0, 6.6, 7.7, 7.8, 8.6, 7.4, 5.6, 4.1, 3.1, 1.5, 0.8,
		};
		/// <summary>Vehicle trips by hour on a Saturday (FHWA NT-3, 2001 NHTS), percent of the day from midnight.</summary>
		internal static readonly double[] TripsSaturday =
		{
			0.8, 0.3, 0.2, 0.1, 0.3, 0.5, 1.1, 2.2, 3.9, 5.6, 7.7, 8.3, 8.6, 7.7, 8.0, 7.6, 7.2, 6.7, 6.8, 5.0, 4.2, 3.6, 2.0, 1.5,
		};

		// ---- what a day's plans hold -------------------------------------------------------------------------------------

		internal enum StopKind
		{
			/// <summary>The car's own home: its drive pad, kerb, drive's end or farmyard.</summary>
			Home,
			/// <summary>A place that draws trips (<see cref="Traffic.Destinations"/>): parked in its lot or at its kerb.</summary>
			Place,
			/// <summary>Another household's home, visited: parked at its kerb.</summary>
			Visit,
			/// <summary>Beyond the map, down a road out: the car leaves the map at the portal and comes back in there.</summary>
			Away,
		}

		/// <summary>One stop of a car's day.</summary>
		internal struct Stop
		{
			public StopKind Kind;
			/// <summary>Why it is there (Home's is the tour's that brought it back; meaningless for the first).</summary>
			public Traffic.Purpose Purpose;
			/// <summary>The place (index in <see cref="Traffic.Destinations"/>; -1 but at a Place or Away) or household visited (-1 but on a Visit).</summary>
			public int Dest, Host;
			public Traffic.Anchor At;
			/// <summary>When its driver wants to set off from it (day seconds; MaxValue for the day's last stop, home for the night).</summary>
			public double WantLeave;
			/// <summary>The least it stays once there (real seconds), however late it arrives.</summary>
			public double Stay;
		}

		/// <summary>The drive from one stop to the next on empty roads: the planned route, what the simulation starts from and may change.</summary>
		internal sealed class Leg
		{
			/// <summary>The edges driven in order, each as edge * 2 (driven from A to B) or edge * 2 + 1 (from B to A).</summary>
			public int[] Route;
			/// <summary>Its length (units) and its time at the roads' limits (real seconds).</summary>
			public double Length, Time;
		}

		/// <summary>A car: a household's, with its day's stops (first and last its home) and the legs between them; or a through car.</summary>
		internal sealed class Car
		{
			/// <summary>The household (index in <see cref="Traffic.Households"/>) and which of its cars; -1 for a through car.</summary>
			public int Household = -1, Index;
			public Stop[] Stops;
			/// <summary>Leg i goes from stop i to stop i + 1.</summary>
			public Leg[] Legs;
			/// <summary>A through car: circling the trunk all day from <see cref="Start"/> the way <see cref="Way"/> along its edge (+1 from A to B).</summary>
			public bool Through;
			public Traffic.Anchor Start;
			public int Way;
		}

		internal readonly int Seed;
		internal readonly bool Weekend;
		internal readonly List<Car> Cars = new List<Car>();

		/// <summary>For the log and trafficsim: what was planned and how long it took.</summary>
		internal double BuildMs, Keep, TargetSeconds, CandidateSeconds, PlannedSeconds;
		internal int Candidates, Kept, Dropped, Late, Unrouted, Routes;
		internal readonly int[] TourCount = new int[4];
		internal int Chained;

		/// <summary>Whether a race from <paramref name="seed"/> falls on a weekend day (2 in 7), as its weather is drawn from the seed.</summary>
		internal static bool WeekendOf(int seed)
		{
			return Plots.Unit(Noise.Mix((ulong)(uint)seed ^ 0x5C4E_D01EUL), 0) < 2.0 / 7.0;
		}

		/// <summary>The day second of hour <paramref name="startHour"/> (a race's, from <c>Sky.StartHourOf</c>).</summary>
		internal static double StartOf(double startHour)
		{
			double h = startHour - DayStartHour;
			h -= 24.0 * Math.Floor(h / 24.0);
			return h * Hour;
		}

		/// <summary>The hour of the world's clock (0 to 24, from midnight) at day second <paramref name="t"/>.</summary>
		internal static double HourOf(double t)
		{
			double h = DayStartHour + t / Hour;
			return h - 24.0 * Math.Floor(h / 24.0);
		}

		private readonly ulong salt;

		private Schedules(int seed)
		{
			Seed = seed;
			Weekend = WeekendOf(seed);
			salt = Noise.Mix(Noise.Mix(WorldGen.Seed + 7400UL) ^ (ulong)(uint)seed);
		}

		/// <summary>The day's plans for a race from <paramref name="seed"/> (the traffic's map built first if it isn't).</summary>
		internal static Schedules Build(int seed)
		{
			Traffic.Ensure();
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			Schedules s = new Schedules(seed);
			s.Portals();
			s.Plan();
			s.Throughs();
			s.BuildMs = watch.Elapsed.TotalMilliseconds;
			return s;
		}

		internal string Report()
		{
			return Cars.Count + " cars (" + Circling + " through), a " + (Weekend ? "weekend" : "weekday") + "; " + Kept + " tours of " + Candidates
				+ " (work " + TourCount[0] + ", shop " + TourCount[1] + ", errand " + TourCount[2] + ", social " + TourCount[3] + ", " + Chained
				+ " commutes with a stop); " + (PlannedSeconds / Math.Max(1.0, TargetSeconds) * 100.0).ToString("0") + "% of the survey's driving; "
				+ BuildMs.ToString("0") + " ms";
		}

		// ---- the roads out -------------------------------------------------------------------------------------------------

		/// <summary>Per place, the portal (index in <see cref="Network.Portals"/>) it stands at, -1 if it isn't a road out.</summary>
		private int[] portalOf;
		/// <summary>How much more a road out draws than its own figures say (<see cref="Beyond"/>).</summary>
		private double pull = 1.0;

		/// <summary>
		/// The roads out: which portal each stands at, and how much the ones that really leave the map draw. Driving into one of the
		/// trunk's tunnels brings a car back in at the other (<see cref="Wrap"/>), so those are no way off the map; the roads that do leave
		/// take their draw too, so the share of work, shops and friends beyond the map stays the same.
		/// </summary>
		private void Portals()
		{
			List<Traffic.Destination> dests = Traffic.Destinations;
			portalOf = new int[dests.Count];
			double own = 0.0, all = 0.0;
			for (int d = 0; d < dests.Count; d++)
			{
				portalOf[d] = -1;
				if (dests[d].Kind != Traffic.DestinationKind.Edge)
				{
					continue;
				}
				for (int p = 0; p < Network.Portals.Count; p++)
				{
					if (Network.Portals[p].X == dests[d].At.X && Network.Portals[p].Z == dests[d].At.Z)
					{
						portalOf[d] = p;
						break;
					}
				}
				double w = dests[d].Work + dests[d].Shop + dests[d].Errand + dests[d].Social;
				all += w;
				own += Beyond(d) ? w : 0.0;
			}
			pull = own > 0.0 ? all / own : 1.0;
		}

		/// <summary>Whether place <paramref name="d"/> can be driven to: every place but a road out into one of the wrap's tunnels.</summary>
		private bool Beyond(int d)
		{
			return portalOf[d] < 0 || Wrap.A < 0 || !Wrap.Open(portalOf[d]);
		}

		// ---- the plan ------------------------------------------------------------------------------------------------------

		/// <summary>A tour from home and back: its stops after home (one, or two with a stop on the way back) and the legs.</summary>
		private sealed class Tour
		{
			public int Household, Key;
			public Traffic.Purpose Purpose;
			public double Depart;
			public Stop[] Stops;
			/// <summary>Out, (on,) home.</summary>
			public Leg[] Legs;
			public double Time;
		}

		private double U(ulong h, int k)
		{
			return Plots.Unit(h, k);
		}

		/// <summary>The day second of a leaving time drawn from an hour table: the hour by its weight, the minute uniform.</summary>
		private static double DrawHour(double[] weights, double u, double v)
		{
			double sum = 0.0;
			foreach (double w in weights)
			{
				sum += w;
			}
			double pick = u * sum, acc = 0.0;
			int hour = 23;
			for (int h = 0; h < 24; h++)
			{
				acc += weights[h];
				if (pick < acc)
				{
					hour = h;
					break;
				}
			}
			return ((hour - (int)DayStartHour + 24) % 24) * Hour + v * Hour;
		}

		private static double Gravity(double weight, double x0, double z0, double x1, double z1)
		{
			double d2 = ((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / (Reach * Reach);
			return weight / (1.0 + d2);
		}

		private static double Draw(Traffic.Destination d, Traffic.Purpose p)
		{
			switch (p)
			{
				case Traffic.Purpose.Work: return d.Work;
				case Traffic.Purpose.Shop: return d.Shop;
				case Traffic.Purpose.Errand: return d.Errand;
				default: return d.Social;
			}
		}

		/// <summary>How long a purpose keeps a car there, counted from leaving home (day seconds): a work day, an hour's shopping...</summary>
		private static double Dwell(Traffic.Purpose p, double a, double b, double c)
		{
			switch (p)
			{
				case Traffic.Purpose.Work: return (8.5 + 2.0 * (a + b + c - 1.5)) * Hour;
				case Traffic.Purpose.Shop: return (0.3 + 0.7 * a) * Hour;
				case Traffic.Purpose.Errand: return (0.15 + 0.35 * a) * Hour;
				default: return (0.75 + 1.25 * a) * Hour;
			}
		}

		private readonly Heap heap = new Heap();
		private double[] cost;
		private int[] via;
		private readonly List<int> touched = new List<int>();
		private readonly List<(int edge, int dir)> route = new List<(int edge, int dir)>();

		/// <summary>The quickest way by road from <paramref name="from"/> to <paramref name="to"/> on empty roads; null if there is none.</summary>
		private Leg Way(Traffic.Anchor from, Traffic.Anchor to)
		{
			if (cost == null)
			{
				cost = new double[Traffic.Nodes.Count];
				via = new int[Traffic.Nodes.Count];
				for (int i = 0; i < cost.Length; i++)
				{
					cost[i] = double.MaxValue;
					via[i] = -1;
				}
			}
			Routes++;
			if (!Traffic.Route(from, to, route, heap, cost, via, touched))
			{
				return null;
			}
			Leg leg = new Leg { Route = new int[route.Count] };
			for (int i = 0; i < route.Count; i++)
			{
				(int e, int dir) = route[i];
				Traffic.Edge edge = Traffic.Edges[e];
				leg.Route[i] = 2 * e + (dir > 0 ? 0 : 1);
				double length;
				if (route.Count == 1)
				{
					length = Math.Abs(to.S - from.S);
				}
				else if (i == 0)
				{
					length = dir > 0 ? edge.Sb - from.S : from.S - edge.Sa;
				}
				else if (i == route.Count - 1)
				{
					length = dir > 0 ? to.S - edge.Sa : edge.Sb - to.S;
				}
				else
				{
					length = edge.Length;
				}
				length = Math.Max(0.0, length);
				leg.Length += length;
				leg.Time += edge.Length > 0.0 ? edge.Time * length / edge.Length : 0.0;
			}
			return leg;
		}

		/// <summary>
		/// The day's tours: candidates from the survey's rates, kept with the one probability that gives the survey's share of the day
		/// spent driving, then given to the household's cars in the order they leave (a tour wanting a car still out waits up to an hour
		/// for it, else is given up), and each car's tours chained into its day.
		/// </summary>
		private void Plan()
		{
			List<Traffic.Household> hh = Traffic.Households;
			List<Traffic.Destination> dests = Traffic.Destinations;
			double[] rates = Weekend ? RateWeekend : RateWeekday;
			double[][] hours = Weekend ? HoursWeekend : HoursWeekday;
			double[] weight = new double[dests.Count + 8];
			int[] hosts = new int[8];
			List<Tour>[] byHousehold = new List<Tour>[hh.Count];
			int withCars = 0;
			for (int h = 0; h < hh.Count; h++)
			{
				byHousehold[h] = new List<Tour>();
				Traffic.Household home = hh[h];
				if (home.Cars == 0)
				{
					continue;
				}
				withCars++;
				ulong hs = Noise.Mix(salt ^ home.Hash);
				int k = 0;
				for (int purpose = 0; purpose < 4; purpose++)
				{
					int n = purpose == 0 ? home.Workers : 1;
					for (int i = 0; i < n; i++, k += 32)
					{
						if (U(hs, k) >= rates[purpose])
						{
							continue;
						}
						Traffic.Purpose why = (Traffic.Purpose)purpose;
						double depart = DrawHour(hours[purpose], U(hs, k + 1), U(hs, k + 2));
						if (depart >= LastOut)
						{
							continue;
						}
						if (!Choose(home, h, why, home.Home.X, home.Home.Z, 1.0, hs, k + 6, weight, hosts, out Stop first))
						{
							continue;
						}
						first.WantLeave = depart + Dwell(why, U(hs, k + 3), U(hs, k + 4), U(hs, k + 5));
						first.Stay = MinStay;
						List<Stop> stops = new List<Stop> { first };
						// A stop on the way home from work: a shop or an errand near the way, chosen by its draw from halfway home.
						if (why == Traffic.Purpose.Work && first.Kind == StopKind.Place && U(hs, k + 20) < ChainShare)
						{
							Traffic.Purpose on = U(hs, k + 21) < 0.5 ? Traffic.Purpose.Shop : Traffic.Purpose.Errand;
							double mx = 0.5 * (home.Home.X + first.At.X), mz = 0.5 * (home.Home.Z + first.At.Z);
							if (Choose(home, h, on, mx, mz, 0.5, hs, k + 22, weight, hosts, out Stop second) && second.Kind == StopKind.Place && second.Dest != first.Dest)
							{
								second.WantLeave = first.WantLeave + Dwell(on, U(hs, k + 23), 0.5, 0.5);
								second.Stay = MinStay;
								stops.Add(second);
							}
						}
						Tour t = new Tour { Household = h, Key = k, Purpose = why, Depart = depart, Stops = stops.ToArray(), Legs = new Leg[stops.Count + 1] };
						Traffic.Anchor at = home.Home;
						bool ok = true;
						for (int j = 0; j <= stops.Count && ok; j++)
						{
							Traffic.Anchor to = j < stops.Count ? stops[j].At : home.Home;
							t.Legs[j] = Way(at, to);
							if (t.Legs[j] == null)
							{
								Unrouted++;
							}
							// A drive shorter than a few car lengths is no trip (a neighbour's), so the tour isn't made.
							ok = t.Legs[j] != null && t.Legs[j].Length >= 30.0;
							if (ok)
							{
								t.Time += t.Legs[j].Time;
							}
							at = to;
						}
						if (!ok)
						{
							continue;
						}
						byHousehold[h].Add(t);
						Candidates++;
						CandidateSeconds += t.Time;
					}
				}
			}
			// Thinning: the share of the day households spend driving (NHTS), counted on empty roads.
			TargetSeconds = Traffic.DrivingShare * Traffic.DaySeconds * withCars;
			// Tours given up (no car free, home too late) take driving out again, so the share kept is worked out afresh from what was
			// planned a few times over (no routes: cheap), then the last pass makes the cars' days.
			Keep = Math.Min(1.0, TargetSeconds / Math.Max(1.0, CandidateSeconds));
			List<Tour> kept = new List<Tour>();
			double bestKeep = Keep, bestOff = double.MaxValue;
			for (int pass = 0; pass <= KeepPasses; pass++)
			{
				bool last = pass == KeepPasses;
				if (last)
				{
					Keep = bestKeep;
				}
				Kept = Dropped = Late = Chained = 0;
				Array.Clear(TourCount, 0, TourCount.Length);
				PlannedSeconds = 0.0;
				for (int h = 0; h < hh.Count; h++)
				{
					kept.Clear();
					ulong ks = Noise.Mix(salt ^ hh[h].Hash ^ 0x5EED_0000UL);
					foreach (Tour t in byHousehold[h])
					{
						if (U(ks, t.Key) < Keep)
						{
							kept.Add(t);
						}
					}
					kept.Sort((a, b) => a.Depart != b.Depart ? a.Depart.CompareTo(b.Depart) : a.Key.CompareTo(b.Key));
					Assign(h, kept, last);
				}
				if (!last && PlannedSeconds > 0.0)
				{
					double off = Math.Abs(PlannedSeconds / TargetSeconds - 1.0);
					if (off < bestOff)
					{
						bestOff = off;
						bestKeep = Keep;
					}
					Keep = Math.Min(1.0, Keep * TargetSeconds / PlannedSeconds);
				}
			}
		}

		/// <summary>
		/// How many times the share of tours kept is tried (<see cref="Plan"/>) before the cars' days are made with the one that came
		/// nearest the survey's driving (tours come whole and some are given up, so it is not quite proportional).
		/// </summary>
		private const int KeepPasses = 6;

		/// <summary>
		/// Where a trip of purpose <paramref name="why"/> goes: every place by its draw for the purpose over the distance from (x, z)
		/// (<paramref name="reach"/> times <see cref="Reach"/>); a visit also among eight households drawn at random.
		/// </summary>
		private bool Choose(Traffic.Household home, int h, Traffic.Purpose why, double x, double z, double reach, ulong hs, int k, double[] weight, int[] hosts, out Stop stop)
		{
			stop = default;
			List<Traffic.Destination> dests = Traffic.Destinations;
			List<Traffic.Household> hh = Traffic.Households;
			double r2 = reach * reach, sum = 0.0;
			for (int d = 0; d < dests.Count; d++)
			{
				Traffic.Destination dest = dests[d];
				if (!Beyond(d))
				{
					weight[d] = 0.0;
					continue;
				}
				double w = Draw(dest, why) * (portalOf[d] >= 0 ? pull : 1.0);
				double dd = ((dest.At.X - x) * (dest.At.X - x) + (dest.At.Z - z) * (dest.At.Z - z)) / (Reach * Reach * r2);
				weight[d] = w / (1.0 + dd);
				sum += weight[d];
			}
			for (int j = 0; j < 8; j++)
			{
				hosts[j] = -1;
				weight[dests.Count + j] = 0.0;
				if (why != Traffic.Purpose.Social)
				{
					continue;
				}
				int other = (int)(U(hs, k + j) * hh.Count);
				if (other == h || other >= hh.Count)
				{
					continue;
				}
				hosts[j] = other;
				weight[dests.Count + j] = Gravity(3.0, x, z, hh[other].Home.X, hh[other].Home.Z);
				sum += weight[dests.Count + j];
			}
			if (sum <= 0.0)
			{
				return false;
			}
			double pick = U(hs, k + 8) * sum, acc = 0.0;
			for (int d = 0; d < dests.Count + 8; d++)
			{
				acc += weight[d];
				if (weight[d] <= 0.0 || pick >= acc)
				{
					continue;
				}
				stop.Purpose = why;
				stop.Dest = -1;
				stop.Host = -1;
				if (d >= dests.Count)
				{
					stop.Kind = StopKind.Visit;
					stop.Host = hosts[d - dests.Count];
					stop.At = hh[stop.Host].Home;
				}
				else
				{
					stop.Kind = portalOf[d] >= 0 ? StopKind.Away : StopKind.Place;
					stop.Dest = d;
					stop.At = dests[d].At;
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Gives household <paramref name="h"/>'s kept tours (in the order they leave) to its cars: each to the car home longest (lowest
		/// index on a tie) if it has rested by then, else to the first back if within <see cref="WaitForCar"/> (the tour leaves late),
		/// else none. A tour that would bring its car home after <see cref="HomeBy"/> is given up. Then, if <paramref name="make"/>, each
		/// car's day is chained (else only what was planned is counted). The tours are left as they were.
		/// </summary>
		private void Assign(int h, List<Tour> kept, bool make)
		{
			Traffic.Household home = Traffic.Households[h];
			int n = home.Cars;
			double[] free = new double[n];
			List<(Tour tour, double depart)>[] mine = new List<(Tour, double)>[n];
			for (int c = 0; c < n; c++)
			{
				mine[c] = new List<(Tour, double)>();
			}
			foreach (Tour t in kept)
			{
				int best = 0;
				for (int c = 1; c < n; c++)
				{
					if (free[c] < free[best])
					{
						best = c;
					}
				}
				double depart = t.Depart;
				if (free[best] > depart)
				{
					if (free[best] - depart > WaitForCar)
					{
						Dropped++;
						continue;
					}
					depart = free[best];
					Late++;
				}
				// When it would be home on empty roads, keeping to its stops' wanted times.
				double at = depart;
				for (int j = 0; j < t.Stops.Length; j++)
				{
					at += t.Legs[j].Time;
					at = Math.Max(at + t.Stops[j].Stay, t.Stops[j].WantLeave + (depart - t.Depart));
				}
				at += t.Legs[t.Stops.Length].Time;
				if (at > HomeBy)
				{
					Dropped++;
					continue;
				}
				free[best] = at + Rest;
				mine[best].Add((t, depart));
				Kept++;
				TourCount[(int)t.Purpose]++;
				Chained += t.Stops.Length > 1 ? 1 : 0;
				PlannedSeconds += t.Time;
			}
			for (int c = 0; c < n && make; c++)
			{
				List<Stop> stops = new List<Stop>();
				List<Leg> legs = new List<Leg>();
				Stop homeStop = new Stop { Kind = StopKind.Home, At = home.Home, Dest = -1, Host = -1, Stay = Rest };
				Stop first = homeStop;
				first.Stay = 0.0;
				first.WantLeave = mine[c].Count > 0 ? mine[c][0].depart : double.MaxValue;
				stops.Add(first);
				for (int i = 0; i < mine[c].Count; i++)
				{
					(Tour t, double depart) = mine[c][i];
					// A late start shifts the whole tour: its stays are counted from leaving home.
					foreach (Stop st in t.Stops)
					{
						Stop shifted = st;
						shifted.WantLeave += depart - t.Depart;
						stops.Add(shifted);
					}
					legs.AddRange(t.Legs);
					Stop back = homeStop;
					back.Purpose = t.Purpose;
					back.WantLeave = i + 1 < mine[c].Count ? mine[c][i + 1].depart : double.MaxValue;
					stops.Add(back);
				}
				Cars.Add(new Car { Household = h, Index = c, Stops = stops.ToArray(), Legs = legs.ToArray() });
			}
		}

		/// <summary>
		/// Through traffic: cars circling the trunk all day, into one tunnel and out of the other (the wrap joins the trunk into a loop,
		/// so traffic passing through it never leaves), spread evenly along its long stretches at 03:00, each way in turn.
		/// </summary>
		private void Throughs()
		{
			if (Wrap.A < 0)
			{
				return;
			}
			List<int> trunk = new List<int>();
			double total = 0.0;
			for (int e = 0; e < Traffic.Edges.Count; e++)
			{
				if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk && Traffic.OnMain(e) && Traffic.Edges[e].Length > 200.0)
				{
					trunk.Add(e);
					total += Traffic.Edges[e].Length;
				}
			}
			int ti = 0;
			double before = 0.0;
			for (int i = 0; i < Circling && trunk.Count > 0; i++)
			{
				double want = (i + 0.5) * total / Circling;
				while (ti + 1 < trunk.Count && before + Traffic.Edges[trunk[ti]].Length <= want)
				{
					before += Traffic.Edges[trunk[ti]].Length;
					ti++;
				}
				Traffic.Edge e = Traffic.Edges[trunk[ti]];
				double u = Math.Max(0.0, Math.Min(1.0, (want - before) / Math.Max(1.0, e.Length)));
				// Clear of the junctions at its ends by a car's length or two.
				double s = Math.Max(e.Sa + 30.0, Math.Min(e.Sb - 30.0, e.Sa + (e.Sb - e.Sa) * u));
				Traffic.At(Network.Roads[e.Road], s, out double x, out double z, out double _);
				Traffic.Anchor start = new Traffic.Anchor { Edge = trunk[ti], S = s, Side = 1, X = x, Z = z };
				Cars.Add(new Car { Through = true, Start = start, Way = i % 2 == 0 ? 1 : -1, Stops = new Stop[0], Legs = new Leg[0] });
			}
		}

		// ---- reading the plans ---------------------------------------------------------------------------------------------

		/// <summary>
		/// When a car that arrived at its stop <paramref name="i"/> at <paramref name="arrived"/> sets off from it: when its driver wants
		/// to, or after the least stay if it came late. What the simulation asks each time a car parks.
		/// </summary>
		internal static double Leave(Stop stop, double arrived)
		{
			return Math.Max(stop.WantLeave, arrived + stop.Stay);
		}

		internal enum State
		{
			/// <summary>Parked at stop <see cref="Where.Index"/> (or beyond the map, if that stop is <see cref="StopKind.Away"/>).</summary>
			Parked,
			/// <summary>On leg <see cref="Where.Index"/>, <see cref="Where.Along"/> of its length driven.</summary>
			Driving,
			/// <summary>A through car: on the trunk all day.</summary>
			Through,
		}

		internal struct Where
		{
			public State State;
			public int Index;
			public double Along;
			/// <summary>Parked: when it arrived and when it will leave. Driving: when the leg began and when it ends.</summary>
			public double From, Until;
		}

		/// <summary>
		/// Where car <paramref name="c"/> is planned to be at day second <paramref name="t"/> on empty roads, each leg at its free time and
		/// each stop left by <see cref="Leave"/>: what the simulation's state at the race's start is built from (cars at work, parked at
		/// the shops, on the roads part way).
		/// </summary>
		internal static Where At(Car c, double t)
		{
			if (c.Through)
			{
				return new Where { State = State.Through, From = 0.0, Until = double.MaxValue };
			}
			double arrived = 0.0;
			for (int i = 0; i < c.Stops.Length; i++)
			{
				double leave = i + 1 < c.Stops.Length ? Leave(c.Stops[i], arrived) : double.MaxValue;
				if (t < leave)
				{
					return new Where { State = State.Parked, Index = i, From = arrived, Until = leave };
				}
				double end = leave + c.Legs[i].Time;
				if (t < end)
				{
					return new Where { State = State.Driving, Index = i, Along = (t - leave) / Math.Max(1e-6, c.Legs[i].Time), From = leave, Until = end };
				}
				arrived = end;
			}
			return new Where { State = State.Parked, Index = c.Stops.Length - 1, From = arrived, Until = double.MaxValue };
		}
	}
}
