using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// Phase 4: the micro level (Micro.cs, Driver.cs) round pretend players over the meso's day, and the visible faults (docs/TRAFFIC_REWORK.md
// section 5), none allowed: two micro cars' bodies overlapping; two cars inside one crossing, merging or head-on conflict area past their
// stop lines together; a car standing in a junction box; a car standing over 10 s with nothing (by its driver's own account, followed car to
// car) holding it; a car held past Meso.Stuck (a deadlock); a car changing level (into or out of micro) within sight of a player; a car
// moving further in a step than its speed takes it (a jump); every car in exactly one place every 10 s (conservation, both levels).
// Measured: the micro's saturated headway at the lights, and (for the first seed) the day with every junction micro against the meso
// alone: the delay per trip and the time lost per lane by road class, which is how the meso's figures are set (consistency).
internal static partial class Program
{
	/// <summary>A pretend player: drives the lane graph at each lane's limit, choosing its way at random at each junction (mostly straight on), or stands still. A ghost: nothing it does touches the traffic.</summary>
	private sealed class Walker
	{
		public int Lane = -1, Conn = -1;
		public double X;
		public bool Still;
		public double SX, SZ;
		public Rng Rng;
		public string Name;

		public void Step(double dt)
		{
			if (Still)
			{
				return;
			}
			if (Conn < 0)
			{
				X += Lanes.All[Lane].Limit * dt;
				double end = Meso.Reach(Lane);
				if (X >= end)
				{
					List<int> outs = Lanes.All[Lane].Out;
					if (outs.Count == 0)
					{
						X = end;
						return;
					}
					int pick = outs[0];
					double roll = Rng.Next();
					List<int> straight = outs.Where(c => Lanes.Connectors[c].Turn == Lanes.Straight || Lanes.Connectors[c].Turn == Lanes.Jump).ToList();
					List<int> turns = outs.Where(c => Lanes.Connectors[c].Turn != Lanes.Round).ToList();
					if (straight.Count > 0 && roll < 0.7)
					{
						pick = straight[(int)(Rng.Next() * straight.Count) % straight.Count];
					}
					else if (turns.Count > 0)
					{
						pick = turns[(int)(Rng.Next() * turns.Count) % turns.Count];
					}
					Conn = pick;
					X -= end;
				}
			}
			else
			{
				Lanes.Connector cn = Lanes.Connectors[Conn];
				X += Math.Max(cn.Limit, 1.0) * dt;
				if (X >= cn.Length)
				{
					double x = Meso.JoinAt(Conn) + (X - cn.Length);
					Lane = cn.To;
					Conn = -1;
					X = x;
				}
			}
			Pose();
		}

		public void Pose()
		{
			if (Still)
			{
				return;
			}
			if (Conn >= 0)
			{
				Micro.OnConnector(Lanes.Connectors[Conn], X, out SX, out SZ, out double _, out double _);
				return;
			}
			int cw = Meso.CarriagewayOf(Lane);
			Lanes.Point(Lanes.All[Lane], Meso.RoadS(cw, X - Lanes.Axle), out SX, out SZ, out double _, out double _, out double _);
		}
	}

	/// <summary>The pretend players of race <paramref name="seed"/>: one standing at Kingsbridge's light (node 6), one driving from the trunk, one from a random road.</summary>
	private static List<Walker> Walkers(int seed)
	{
		Rng rng = new Rng((ulong)seed * 0x9E3779B97F4A7C15UL + 77UL);
		List<Walker> list = new List<Walker>();
		Traffic.Node k = Traffic.Nodes[6];
		list.Add(new Walker { Still = true, SX = k.X, SZ = k.Z, Rng = rng, Name = "standing at node 6" });
		List<int> trunk = new List<int>(), main = new List<int>();
		for (int l = 0; l < Lanes.All.Count; l++)
		{
			Lanes.Lane lane = Lanes.All[l];
			if (lane.Kind != Lanes.Through || lane.Length < 50.0 || !Traffic.OnMain(lane.Edge))
			{
				continue;
			}
			main.Add(l);
			if (Network.Roads[Traffic.Edges[lane.Edge].Road].Kind == RoadKind.Trunk)
			{
				trunk.Add(l);
			}
		}
		foreach ((List<int> from, string name) in new[] { (trunk, "driving from the trunk"), (main, "driving from a random road") })
		{
			int l = from[(int)(rng.Next() * from.Count) % from.Count];
			Walker w = new Walker { Lane = l, X = Meso.StartOf(l) + Lanes.Axle, Rng = new Rng(rng.NextU64()), Name = name };
			w.Pose();
			list.Add(w);
		}
		return list;
	}

	/// <summary>Faults found by kind (each counted once per car and kind), with the first few described.</summary>
	private sealed class Faults
	{
		public readonly SortedDictionary<string, int> Count = new SortedDictionary<string, int>();
		public readonly List<string> First = new List<string>();
		public readonly List<string> All = new List<string>();
		private readonly HashSet<(string, int)> seen = new HashSet<(string, int)>();

		public void Add(string kind, int car, string what)
		{
			if (!seen.Add((kind, car)))
			{
				return;
			}
			Count[kind] = Count.TryGetValue(kind, out int n) ? n + 1 : 1;
			All.Add(kind + ": " + what);
			if (First.Count(f => f.StartsWith(kind + ":")) < 4)
			{
				First.Add(kind + ": " + what);
			}
		}
	}

	/// <summary>Watches the micro cars after every micro step for the visible faults.</summary>
	private sealed class Watch
	{
		private readonly Micro mi;
		private readonly Faults faults;
		private readonly double[] px, pz, py, hx, hz;
		private readonly double[] lastX, lastZ, lastV;
		private readonly int[] lastStep;
		private int step;
		private int events;
		public int PeakMicro, PeakInSight;
		public double MicroCarSeconds;
		private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
		private readonly Dictionary<int, List<(int car, double d)>> atConn = new Dictionary<int, List<(int, double)>>();

		public Watch(Micro mi, Faults faults)
		{
			this.mi = mi;
			this.faults = faults;
			int n = mi.M.Cars.Length;
			px = new double[n];
			pz = new double[n];
			py = new double[n];
			hx = new double[n];
			hz = new double[n];
			lastX = new double[n];
			lastZ = new double[n];
			lastV = new double[n];
			lastStep = new int[n];
			for (int i = 0; i < n; i++)
			{
				lastStep[i] = -10;
			}
		}

		internal string Where(int i)
		{
			Micro.Agent a = mi.Agents[i];
			string at = a.Conn >= 0 ? "connector " + a.Conn + " (node " + Lanes.Connectors[a.Conn].Node + ") " + a.X.ToString("0.0") + " in" : "lane " + a.Lane + " (to node " + Lanes.All[a.Lane].To + ") at " + a.X.ToString("0.0") + " of " + Meso.Reach(a.Lane).ToString("0.0");
			return "car " + i + " on " + at + ", " + a.V.ToString("0.0") + " u/s, " + a.Reason + (a.ReasonCar >= 0 ? " " + a.ReasonCar : "") + " at " + px[i].ToString("0") + ", " + pz[i].ToString("0")
				+ " " + Clock(Schedules.HourOf(mi.T));
		}

		private bool InSight(double x, double z)
		{
			for (int p = 0; p < mi.PX.Count; p++)
			{
				double dx = x - mi.PX[p], dz = z - mi.PZ[p];
				if (dx * dx + dz * dz <= Micro.Sight * Micro.Sight)
				{
					return true;
				}
			}
			return false;
		}

		private static readonly int whyLane = int.Parse(Environment.GetEnvironmentVariable("TRAFFICSIM_WHY") ?? "-1");
		private static readonly int[] trace = (Environment.GetEnvironmentVariable("TRAFFICSIM_TRACE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
		private static readonly double[] traceAt = (Environment.GetEnvironmentVariable("TRAFFICSIM_TRACE_AT") ?? "0,0").Split(',').Select(double.Parse).ToArray();

		public void Check(Micro m)
		{
			step++;
			List<int> act = mi.Active;
			int inSight = 0;
			foreach (int i in act)
			{
				mi.Pose(i, out px[i], out pz[i], out hx[i], out hz[i]);
				py[i] = Height(i);
				inSight += InSight(px[i], pz[i]) ? 1 : 0;
			}
			if (whyLane >= 0 && step % 8 == 0)
			{
				foreach (int i in act)
				{
					Micro.Agent a = mi.Agents[i];
					if (a != null && a.Conn < 0 && a.Lane == whyLane && a.V < 1.0)
					{
						Console.WriteLine("      why " + mi.T.ToString("0") + " " + Where(i) + (a.ReasonCar >= 0 ? " | " + Where(a.ReasonCar) : ""));
					}
				}
			}
			if (trace.Length > 0 && mi.T >= traceAt[0] && mi.T <= traceAt[1])
			{
				foreach (int i in trace)
				{
					if (mi.Agents[i] != null)
					{
						Micro.Agent a = mi.Agents[i];
						Console.WriteLine("      trace " + mi.T.ToString("0.000") + " " + Where(i) + " go " + a.Go + " committed " + a.Committed + " acc " + a.A.ToString("0.0")
							+ (a.Conn < 0 ? "" : TrafficSignals.Has(Lanes.Connectors[a.Conn].Node) ? " light " + TrafficSignals.State(Lanes.Connectors[a.Conn].Node, Lanes.Connectors[a.Conn].InArm, Lanes.Connectors[a.Conn].OutArm, mi.T - mi.M.RaceStart) : ""));
					}
					else if (mi.M.Cars[i].Mode == Meso.Mode.Driving)
					{
						Meso.Vehicle v = mi.M.Cars[i];
						mi.M.Where(i, out int wl, out int wc, out double wa);
						Console.WriteLine("      trace " + mi.T.ToString("0.000") + " meso car " + i + " lane " + v.Lane + " conn " + v.Conn + " (" + wl + "/" + wc + " at " + wa.ToString("0.0")
							+ ") entered " + v.Entered.ToString("0.00") + " connAt " + v.ConnAt.ToString("0.00") + " ready " + v.Ready.ToString("0.00") + " v " + v.V.ToString("0.0") + " vConn " + v.VConn.ToString("0.0") + " vOut " + v.VOut.ToString("0.0"));
					}
				}
			}
			PeakMicro = Math.Max(PeakMicro, act.Count);
			PeakInSight = Math.Max(PeakInSight, inSight);
			MicroCarSeconds += act.Count * Micro.Dt;
			// Jumps: no further in a step than its speed takes it (the wrap's jump through the tunnels apart).
			foreach (int i in act)
			{
				Micro.Agent a = mi.Agents[i];
				if (lastStep[i] == step - 1)
				{
					double dx = px[i] - lastX[i], dz = pz[i] - lastZ[i], d = Math.Sqrt(dx * dx + dz * dz);
					// A lane off its road's line runs longer round a bend than the road's distance its speed is in (up to a few percent).
					if (d > (Math.Max(a.V, lastV[i]) * 1.2 + Driver.MaxBrake * Micro.Dt) * Micro.Dt + 0.5 && d < 1000.0)
					{
						faults.Add("jump", i, Where(i) + " moved " + d.ToString("0.0") + " in a step");
					}
				}
				lastStep[i] = step;
				lastX[i] = px[i];
				lastZ[i] = pz[i];
				lastV[i] = a.V;
			}
			// Overlaps.
			foreach (List<int> l in grid.Values)
			{
				l.Clear();
			}
			foreach (int i in act)
			{
				long key = ((long)Math.Floor(px[i] / 20.0) << 32) ^ ((long)Math.Floor(pz[i] / 20.0) & 0xffffffffL);
				if (!grid.TryGetValue(key, out List<int> list))
				{
					grid[key] = list = new List<int>();
				}
				list.Add(i);
			}
			foreach (int i in act)
			{
				long cx = (long)Math.Floor(px[i] / 20.0), cz = (long)Math.Floor(pz[i] / 20.0);
				for (long gx = cx - 1; gx <= cx + 1; gx++)
				{
					for (long gz = cz - 1; gz <= cz + 1; gz++)
					{
						if (!grid.TryGetValue((gx << 32) ^ (gz & 0xffffffffL), out List<int> list))
						{
							continue;
						}
						foreach (int j in list)
						{
							if (j <= i || Math.Abs(py[i] - py[j]) > 4.0)
							{
								continue;
							}
							if (Overlap(i, j))
							{
								faults.Add("overlap", Math.Min(i, j) * 100000 + Math.Max(i, j), Where(i) + " in " + Where(j) + (InSight(px[i], pz[i]) ? " (in sight)" : ""));
							}
						}
					}
				}
			}
			// Two cars in one area past their stop lines together.
			foreach (List<(int, double)> l in atConn.Values)
			{
				l.Clear();
			}
			foreach (int i in act)
			{
				if (mi.AtJunction(i, out int c, out double d) && d >= 0.0)
				{
					if (!atConn.TryGetValue(c, out List<(int, double)> list))
					{
						atConn[c] = list = new List<(int, double)>();
					}
					list.Add((i, d));
				}
			}
			foreach (KeyValuePair<int, List<(int car, double d)>> kv in atConn)
			{
				foreach ((int i, double di) in kv.Value)
				{
					foreach (int ai in Conflicts.Of[kv.Key])
					{
						Conflicts.Area area = Conflicts.All[ai];
						if (area.Kind == Conflicts.Diverging || area.A != kv.Key)
						{
							continue;
						}
						if (di < area.A0 || di > area.A1 || !atConn.TryGetValue(area.B, out List<(int car, double d)> others))
						{
							continue;
						}
						foreach ((int j, double dj) in others)
						{
							if (j != i && dj >= area.B0 && dj <= area.B1)
							{
								faults.Add("area", Math.Min(i, j) * 100000 + Math.Max(i, j), Where(i) + " and " + Where(j) + " in area " + ai + " (kind " + area.Kind + ", yield " + area.Yield + ")");
							}
						}
					}
				}
			}
			// Standing: in a box, for no reason, deadlocked.
			foreach (int i in act)
			{
				Micro.Agent a = mi.Agents[i];
				if (a.StoodSince < 0.0)
				{
					continue;
				}
				double stood = mi.T - a.StoodSince;
				if (a.Conn >= 0 && Lanes.Connectors[a.Conn].KeepClear && a.X > 0.5 && stood >= 1.0)
				{
					faults.Add("box", i, Where(i) + " standing in the junction's box " + stood.ToString("0") + " s");
				}
				if (stood > Meso.Stuck)
				{
					faults.Add("deadlock", i, Where(i) + " standing " + stood.ToString("0") + " s");
				}
				if (stood > 10.0)
				{
					string end = Chain(i, out bool loop);
					if (end != null)
					{
						faults.Add(loop ? "standoff" : "no reason", i, Where(i) + " standing " + stood.ToString("0") + " s: " + end);
					}
				}
			}
			// Cars changing level in sight.
			for (; events < mi.Events.Count; events++)
			{
				Micro.Event e = mi.Events[events];
				if ((e.Kind == Micro.Kind.In || e.Kind == Micro.Kind.Out || e.Kind == Micro.Kind.FlipIn || e.Kind == Micro.Kind.FlipOut) && InSight(e.X, e.Z))
				{
					faults.Add("in sight", e.Car, "car " + e.Car + " " + e.Kind + " at " + e.X.ToString("0") + ", " + e.Z.ToString("0") + " " + Clock(Schedules.HourOf(e.T)));
				}
			}
		}

		/// <summary>The car standing car <paramref name="i"/> is held by, followed to its end: null if that is a reason (a light, a car moving, a meso car, a lane change's gap...), else what is wrong.</summary>
		private string Chain(int i, out bool loop)
		{
			loop = false;
			HashSet<int> seen = new HashSet<int>();
			int k = i;
			for (int hop = 0; hop < 60; hop++)
			{
				Micro.Agent a = mi.Agents[k];
				if (a == null)
				{
					return null;
				}
				if (!seen.Add(k))
				{
					loop = true;
					return "held round a loop of " + seen.Count + " cars";
				}
				if (a.StoodSince < 0.0)
				{
					return null;
				}
				switch (a.Reason)
				{
					case Micro.Why.Light:
					case Micro.Why.Stop:
						return null;
					case Micro.Why.None:
					case Micro.Why.Bend:
						return hop == 0 ? "nothing holds it (" + a.Reason + ")" : "behind car " + k + ", which nothing holds (" + a.Reason + ")";
				}
				if (a.ReasonCar < 0)
				{
					return null;
				}
				k = a.ReasonCar;
			}
			return null;
		}

		private double Height(int i)
		{
			Micro.Agent a = mi.Agents[i];
			if (a.Conn >= 0)
			{
				Lanes.Connector cn = Lanes.Connectors[a.Conn];
				double f = cn.Length > 0.0 ? Math.Max(0.0, Math.Min(1.0, a.X / cn.Length)) : 0.0;
				return cn.Y[0] + (cn.Y[cn.Y.Length - 1] - cn.Y[0]) * f;
			}
			Lanes.Point(Lanes.All[a.Lane], Meso.RoadS(Meso.CarriagewayOf(a.Lane), a.X - Lanes.Axle), out double _, out double _, out double y, out double _, out double _);
			return y;
		}

		/// <summary>Whether the bodies of cars <paramref name="i"/> and <paramref name="j"/> (the game's enemy cars, 14 by 5.4) overlap: the separating axis test on two rectangles.</summary>
		private bool Overlap(int i, int j)
		{
			const double hl = Traffic.CarLength * 0.5, hw = 2.7;
			double dx = px[j] - px[i], dz = pz[j] - pz[i];
			if (dx * dx + dz * dz > (2.0 * hl) * (2.0 * hl) * 1.2)
			{
				return false;
			}
			double[] ax = { hx[i], -hz[i], hx[j], -hz[j] }, az = { hz[i], hx[i], hz[j], hx[j] };
			for (int k = 0; k < 4; k++)
			{
				double ux = ax[k], uz = az[k];
				double ri = hl * Math.Abs(hx[i] * ux + hz[i] * uz) + hw * Math.Abs(-hz[i] * ux + hx[i] * uz);
				double rj = hl * Math.Abs(hx[j] * ux + hz[j] * uz) + hw * Math.Abs(-hz[j] * ux + hx[j] * uz);
				if (Math.Abs(dx * ux + dz * uz) >= ri + rj)
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>
	/// The meso's day from <paramref name="start"/> with the micro level round the pretend players (or, <paramref name="everywhere"/>, at
	/// every junction), every visible fault checked after every micro step. Returns the meso for the consistency comparison.
	/// </summary>
	private static Meso MicroDay(Schedules s, double start, bool report, bool everywhere)
	{
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Meso m = Meso.Start(s, start, start);
		Micro mi = new Micro(m);
		List<Walker> walkers = Walkers(s.Seed);
		if (everywhere)
		{
			mi.NearR = mi.FarR = 1e9;
		}
		mi.Move = x =>
		{
			x.PX.Clear();
			x.PZ.Clear();
			foreach (Walker w in walkers)
			{
				w.Step(Meso.Step);
				x.PX.Add(w.SX);
				x.PZ.Add(w.SZ);
			}
		};
		Faults faults = new Faults();
		Watch w = new Watch(mi, faults);
		mi.After = w.Check;
		string where = "seed " + s.Seed + " from " + Clock(Schedules.HourOf(start)) + (everywhere ? ", every junction micro" : ", micro round 3 players");
		bool conserved = true;
		for (double t = start + 10.0; t <= Traffic.DaySeconds + 1e-9 && conserved; t += 10.0)
		{
			m.Run(t);
			conserved = Check(m.Conserved(out string why), "conservation", where + " at " + Clock(Schedules.HourOf(t)) + ": " + why)
				&& Check(mi.Consistent(out why), "conservation", where + " at " + Clock(Schedules.HourOf(t)) + ": " + why);
		}
		m.Run(Traffic.DaySeconds);
		int notHome = 0;
		foreach (Meso.Vehicle v in m.Cars)
		{
			if (!v.Through && (v.Mode != Meso.Mode.Parked || v.Stop != s.Cars[v.Car].Stops.Length - 1))
			{
				notHome++;
				if (notHome <= 5)
				{
					Console.WriteLine("    not home: car " + v.Car + " " + v.Mode + " stop " + v.Stop + " of " + s.Cars[v.Car].Stops.Length + ", trip from " + v.TripStart.ToString("0")
						+ " free " + v.TripFree.ToString("0") + " s, planned " + string.Join(" ", s.Cars[v.Car].Stops.Select(st => st.WantLeave < 1e9 ? st.WantLeave.ToString("0") + "+" + st.Stay.ToString("0") : "end")) + " legs " + string.Join(" ", s.Cars[v.Car].Legs.Select(lg => lg.Time.ToString("0"))) + ", " + (v.Mode == Meso.Mode.Driving ? (v.Micro ? w.Where(v.Car) : "meso lane " + v.Lane + " " + m.Why(v.Car)) : "parked"));
				}
			}
		}
		double ms = watch.Elapsed.TotalMilliseconds;
		Console.WriteLine("  micro, " + where + ": " + m.Arrivals + " arrived, " + notHome + " not home; peak " + w.PeakMicro + " micro cars (" + w.PeakInSight + " in sight), "
			+ (w.MicroCarSeconds / 3600.0).ToString("0") + " car hours driven micro; " + mi.Spawned + " came in, " + mi.HandedOut + " handed out, " + mi.Flips + " junction groups changed level, "
			+ mi.Missed + " missed their lane; headway at the lights from a standing queue " + (mi.Headways > 0 ? (mi.HeadwaySum / mi.Headways).ToString("0.00") + " s over " + mi.Headways : "not seen")
			+ ", from its fifth car (HCM's saturation headway) " + (mi.Sats > 0 ? (mi.SatSum / mi.Sats).ToString("0.00") + " s over " + mi.Sats : "not seen")
			+ "; delay " + (m.DelaySum / Math.Max(1, m.Arrivals)).ToString("0.0") + " s a trip; ran in " + (ms / 1000.0).ToString("0.0") + " s");
		foreach (string f in faults.First.Take(report ? 24 : 10))
		{
			Console.WriteLine("    " + f);
		}
		string dump = Environment.GetEnvironmentVariable("TRAFFICSIM_FAULTS");
		if (dump != null)
		{
			System.IO.File.AppendAllLines(dump, faults.All.Select(f => where + " | " + f));
		}
		foreach (KeyValuePair<string, int> kv in faults.Count)
		{
			Check(false, "micro " + kv.Key, where + ": " + kv.Value + " cars");
		}
		Check(notHome == 0, "home", where + ": " + notHome + " cars not home at the day's end");
		Check(m.StuckCars.Count == 0, "deadlock", where + ": " + m.StuckCars.Count + " meso cars held over " + Meso.Stuck + " s");
		return m;
	}

	/// <summary>Where a connector's ends sit against the lanes' poses at the same places (a car going from one to the other mustn't jump).</summary>
	private static void Geometry()
	{
		Meso.Ensure();
		List<(double d, string what)> bad = new List<(double, string)>();
		for (int c = 0; c < Lanes.Connectors.Count; c++)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			if (cn.Turn == Lanes.Jump)
			{
				continue;
			}
			int cwF = Meso.CarriagewayOf(cn.From), cwT = Meso.CarriagewayOf(cn.To);
			Lanes.Point(Lanes.All[cn.From], Meso.RoadS(cwF, Meso.Reach(cn.From) - Lanes.Axle), out double x0, out double z0, out double _, out double _, out double _);
			Lanes.Point(Lanes.All[cn.To], Meso.RoadS(cwT, Meso.JoinAt(c) - Lanes.Axle), out double x1, out double z1, out double _, out double _, out double _);
			int n = cn.X.Length - 1;
			double d0 = Math.Sqrt((x0 - cn.X[0]) * (x0 - cn.X[0]) + (z0 - cn.Z[0]) * (z0 - cn.Z[0]));
			double d1 = Math.Sqrt((x1 - cn.X[n]) * (x1 - cn.X[n]) + (z1 - cn.Z[n]) * (z1 - cn.Z[n]));
			bad.Add((d0, "connector " + c + " start (lane " + cn.From + " length " + Lanes.All[cn.From].Length.ToString("0.0") + ", reach " + Meso.Reach(cn.From).ToString("0.0") + ", kind " + Lanes.All[cn.From].Kind + ")"));
			bad.Add((d1, "connector " + c + " end (lane " + cn.To + " length " + Lanes.All[cn.To].Length.ToString("0.0") + ", join from " + Meso.JoinAt(c).ToString("0.0") + ", reach " + Meso.Reach(cn.To).ToString("0.0") + ", wide " + cn.Wide + ")"));
		}
		string probe = Environment.GetEnvironmentVariable("TRAFFICSIM_PROBE");
		if (probe != null)
		{
			string[] f = probe.Split(',');
			int lane = int.Parse(f[0]);
			double from = double.Parse(f[1]), to = double.Parse(f[2]);
			int cw = Meso.CarriagewayOf(lane);
			double px = 0, pz = 0;
			for (double x = from; x <= to; x += 0.5)
			{
				double s = Meso.RoadS(cw, x - Lanes.Axle);
				Lanes.Point(Lanes.All[lane], s, out double lx, out double lz, out double _, out double _, out double _);
				Console.WriteLine("  x " + x.ToString("0.0") + " s " + s.ToString("0.0") + " at " + lx.ToString("0.00") + ", " + lz.ToString("0.00") + " step " + Math.Sqrt((lx - px) * (lx - px) + (lz - pz) * (lz - pz)).ToString("0.00") + " offset " + Lanes.Offset(Lanes.All[lane], s).ToString("0.00"));
				px = lx;
				pz = lz;
			}
		}
		string lanesOf = Environment.GetEnvironmentVariable("TRAFFICSIM_LANE");
		if (lanesOf != null)
		{
			int cw = Meso.CarriagewayOf(int.Parse(lanesOf));
			foreach (int l in Meso.LanesOf(cw))
			{
				Lanes.Lane ln = Lanes.All[l];
				Console.WriteLine("  lane " + l + " kind " + ln.Kind + " index " + ln.Index + " parent " + ln.Parent + " left " + ln.LeftOf + " right " + ln.RightOf + " start along " + Meso.StartOf(l).ToString("0.0")
					+ " reach " + Meso.Reach(l).ToString("0.0") + " length " + ln.Length.ToString("0.0") + " out " + string.Join(" ", ln.Out.Select(c => c + "(" + Lanes.Connectors[c].Turn + ">" + Lanes.Connectors[c].To + ")")));
			}
		}
		bad.Sort((a, b) => b.d.CompareTo(a.d));
		Console.WriteLine("connector ends off the lanes' poses: " + bad.Count(b => b.d > 0.5) + " over 0.5 of " + bad.Count);
		foreach ((double d, string what) in bad.Take(15))
		{
			Console.WriteLine("  " + d.ToString("0.00") + " " + what);
		}
	}

	/// <summary>Consistency (§5): the first seed's day at every junction micro against the meso alone, by road class: the time lost per car on a lane.</summary>
	private static void Consistency(Meso meso, Meso micro)
	{
		if (Environment.GetEnvironmentVariable("TRAFFICSIM_WORST") == "1")
		{
			var rows = Enumerable.Range(0, Lanes.All.Count).Select(l => (l, d: micro.LaneLost[l] - meso.LaneLost[l])).OrderByDescending(r => r.d).Take(25);
			foreach ((int l, double d) in rows)
			{
				Lanes.Lane ln = Lanes.All[l];
				Console.WriteLine("    worse in micro: lane " + l + " to node " + ln.To + " " + Network.Roads[Traffic.Edges[ln.Edge].Road].Kind + (TrafficSignals.Has(ln.To) ? " lit" : "") + " by " + d.ToString("0") + " s: meso "
					+ (meso.LaneLost[l] / Math.Max(1, meso.LaneExits[l])).ToString("0.0") + " s over " + meso.LaneExits[l] + ", micro " + (micro.LaneLost[l] / Math.Max(1, micro.LaneExits[l])).ToString("0.0") + " s over " + micro.LaneExits[l]
					+ "; outs " + string.Join(" ", ln.Out.Select(c => Lanes.Connectors[c].Turn + "@" + Lanes.Connectors[c].Limit.ToString("0") + "/" + ln.Limit.ToString("0"))) + " hold " + ln.Hold.ToString("0"));
			}
		}
		Console.WriteLine("  consistency, meso against micro everywhere: delay a trip " + (meso.DelaySum / Math.Max(1, meso.Arrivals)).ToString("0.0") + " s against "
			+ (micro.DelaySum / Math.Max(1, micro.Arrivals)).ToString("0.0") + " s; seconds lost per car on a lane, by road class (lit lanes apart):");
		for (int k = 0; k <= (int)RoadKind.Drive; k++)
		{
			for (int lit = 0; lit < 2; lit++)
			{
				double a = 0.0, b = 0.0;
				int na = 0, nb = 0;
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					Lanes.Lane lane = Lanes.All[l];
					if ((int)Network.Roads[Traffic.Edges[lane.Edge].Road].Kind != k || (TrafficSignals.Has(lane.To) ? 1 : 0) != lit)
					{
						continue;
					}
					a += meso.LaneLost[l];
					na += meso.LaneExits[l];
					b += micro.LaneLost[l];
					nb += micro.LaneExits[l];
				}
				if (na + nb == 0)
				{
					continue;
				}
				Console.WriteLine("    " + ((RoadKind)k).ToString().ToLower().PadRight(10) + (lit == 1 ? "lit  " : "     ") + (a / Math.Max(1, na)).ToString("0.0").PadLeft(6) + " s over "
					+ na.ToString().PadLeft(6) + "   micro " + (b / Math.Max(1, nb)).ToString("0.0").PadLeft(6) + " s over " + nb.ToString().PadLeft(6));
			}
		}
	}
}
