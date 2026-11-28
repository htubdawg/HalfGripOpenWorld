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
		private System.Threading.Tasks.Task<Schedules> nextDay;

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

		/// <summary>For the overlay and the log: the build's time, the last second's stepping time, cars driving, micro and parked.</summary>
		internal double BuildMs, StepMs;
		internal int DrivingNow, MicroNow, ParkedNow;
		private double stepSum;
		private double stepSince;

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
			};
			Parking = new TrafficParking(m.Day);
			int n = plans.Cars.Count;
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
					if (parkedStop[i] >= 0)
					{
						Parking.Leave(i);
						parkedStop[i] = -1;
					}
					driving++;
					micro += v.Micro ? 1 : 0;
					if (DrivingPose(i, out x1[i], out z1[i], out hx1[i], out hz1[i], out y1[i]))
					{
						k1[i] = Driving;
					}
					continue;
				}
				if (v.Mode != Meso.Mode.Parked)
				{
					if (parkedStop[i] >= 0)
					{
						Parking.Leave(i);
						parkedStop[i] = -1;
					}
					continue;
				}
				parked++;
				if (parkedStop[i] != v.Stop || parkedPlace[i] != v.Place)
				{
					spots[i] = Parking.Park(i, v.Stop, v.Place);
					parkedStop[i] = v.Stop;
					parkedPlace[i] = v.Place;
				}
				TrafficParking.Spot s = spots[i];
				if (s.Away)
				{
					continue;
				}
				x1[i] = s.X;
				z1[i] = s.Z;
				hx1[i] = s.Hx;
				hz1[i] = s.Hz;
				y1[i] = double.NaN;
				k1[i] = Parked;
			}
			DrivingNow = driving;
			MicroNow = micro;
			ParkedNow = parked;
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
				y = a.Conn >= 0 ? ConnectorY(Lanes.Connectors[a.Conn], a.X) : LaneY(a.Lane, a.X);
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
