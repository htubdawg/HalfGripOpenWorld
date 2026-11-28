using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// Phase 5, physics cars (MicroPhysics.cs): the nearest micro cars driven by stand-in bodies as the game drives its physics cars
// (TrafficPhysics.cs). Each body is a plain car model: its acceleration follows the driver's (Agent.A) a fifth of a second late, less a
// little drag, and it steers by pure pursuit along Micro.WayAhead; its reading goes back into the micro every frame, which places the agent
// where the body is. Run frame by frame for 10 minutes round a pretend player driving the lane graph, the bodies taken and let go as the game
// does (the nearest 24 within 300 units, let go past 400). Fails on: a body in another car (body or drawn); a body straying off its line in
// ordinary driving; a micro car driven by a body standing 60 s with nothing holding it; the cars not conserved. Then one body is shoved off
// the road: it must leave the day within a few seconds, and the rest carry on.
internal static partial class Program
{
	private sealed class StandIn
	{
		public int Car;
		public double X, Z, Hx, Hz, V, A;
	}

	private static void PhysicsDay(int seed, double minutes)
	{
		double start = Schedules.StartOf(StartHourOf(seed));
		Meso.Ensure();
		Walker w = Walkers(seed)[1];
		TrafficLive live = TrafficLive.Build(seed, start, w.SX, w.SZ);
		Micro mi = live.Mi;
		string where = "seed " + seed + " physics cars in game from " + TrafficLive.Clock(start);
		const double frame = 1.0 / 60.0, near = 300.0, far = 400.0;
		const int most = 24;
		Dictionary<int, StandIn> bodies = new Dictionary<int, StandIn>();
		double[] xs = new double[16], zs = new double[16];
		double race = 0.0, nextTake = 0.0, nextCheck = 0.0, moved = 0.0;
		int taken = 0, overlaps = 0, standing = 0, unconserved = 0;
		List<string> firsts = new List<string>();
		void Note(string what)
		{
			if (firsts.Count < 8)
			{
				firsts.Add(what);
			}
		}
		Dictionary<int, double> stoodFree = new Dictionary<int, double>();
		List<int> drop = new List<int>();
		int shoved = -1;
		double shovedAt = 0.0;
		// A body wrecked dead in its lane (as TrafficPhysics does on a wreck: out of the day at once), lying there as a loose body the
		// drivers see; the cars behind must stop for it, not drive into it.
		StandIn wreck = null;
		double wreckAt = 0.0;
		int wreckHits = 0, wreckQueue = 0;
		HashSet<int> wreckCars = new HashSet<int>();
		double end = minutes * 60.0;
		while (race < end + 6.0)
		{
			race += frame;
			w.Step(frame);
			live.PlayerX = w.SX;
			live.PlayerZ = w.SZ;
			double now = live.DayAt(race);
			// The bodies read before the simulation steps (as TrafficPhysics does in its Update), the wreck given as a body in the road.
			foreach (StandIn b in bodies.Values)
			{
				mi.Read(b.Car, ReadingOf(b, now));
			}
			live.OtherBodies.Clear();
			if (wreck != null)
			{
				live.OtherBodies.Add(new Micro.Body { X = wreck.X, Z = wreck.Z, Hx = wreck.Hx, Hz = wreck.Hz, Vx = wreck.Hx * wreck.V, Vz = wreck.Hz * wreck.V, HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 });
			}
			live.Advance(now);
			// Bodies whose agent is gone (parked, or left the day) let go; the rest driven a frame.
			drop.Clear();
			foreach (StandIn b in bodies.Values)
			{
				Micro.Agent a = mi.Agents[b.Car];
				double pd = Math.Sqrt((b.X - w.SX) * (b.X - w.SX) + (b.Z - w.SZ) * (b.Z - w.SZ));
				bool jump = false;
				int n = a != null ? mi.WayAhead(b.Car, 4.0, xs, zs, out jump) : 0;
				if (a == null || !a.Driven)
				{
					drop.Add(b.Car);
					continue;
				}
				if ((pd > far && b.Car != shoved) || jump)
				{
					mi.GiveBody(b.Car, b.X, b.Z);
					drop.Add(b.Car);
					continue;
				}
				Drive(b, a.A, n, xs, zs, frame);
				moved += b.V * frame;
			}
			foreach (int c in drop)
			{
				bodies.Remove(c);
			}
			if (race >= nextTake && race < end)
			{
				nextTake = race + 0.2;
				List<(double d, int car)> cands = new List<(double, int)>();
				foreach (int c in mi.Active)
				{
					Micro.Agent a = mi.Agents[c];
					if (a.Driven || (a.Conn >= 0 && Lanes.Connectors[a.Conn].Turn == Lanes.Jump))
					{
						continue;
					}
					if (live.Pose(c, now, out double x, out double z, out double _, out double _, out double _) != TrafficLive.Driving)
					{
						continue;
					}
					double d = Math.Sqrt((x - w.SX) * (x - w.SX) + (z - w.SZ) * (z - w.SZ));
					if (d < near)
					{
						cands.Add((d, c));
					}
				}
				cands.Sort();
				foreach ((double _, int c) in cands)
				{
					if (bodies.Count >= most)
					{
						break;
					}
					live.Pose(c, now, out double x, out double z, out double hx, out double hz, out double _);
					StandIn b = new StandIn { Car = c, X = x, Z = z, Hx = hx, Hz = hz, V = mi.Agents[c].V, A = mi.Agents[c].A };
					if (mi.TakeBody(c, ReadingOf(b, now)))
					{
						bodies[c] = b;
						taken++;
					}
				}
			}
			// The shove: the nearest moving body pushed 25 units sideways and turned across the road.
			if (shoved < 0 && race >= end * 0.5)
			{
				foreach (StandIn b in bodies.Values)
				{
					if (b.V > 2.0)
					{
						shoved = b.Car;
						shovedAt = race;
						b.X += -b.Hz * 25.0;
						b.Z += b.Hx * 25.0;
						(b.Hx, b.Hz) = (-b.Hz, b.Hx);
						b.V = 0.0;
						break;
					}
				}
			}
			if (wreck == null && race >= end * 0.75)
			{
				foreach (StandIn b in bodies.Values)
				{
					Micro.Agent a = mi.Agents[b.Car];
					if (b.Car != shoved && b.V > 5.0 && a != null && a.Conn < 0)
					{
						wreck = b;
						wreckAt = race;
						mi.LoseCar(b.Car);
						break;
					}
				}
			}
			if (wreck != null && wreck.V > 0.0)
			{
				// It slides to a stop on locked wheels, straight on.
				wreck.V = Math.Max(0.0, wreck.V - Driver.MaxBrake * 0.8 * frame);
				wreck.X += wreck.Hx * wreck.V * frame;
				wreck.Z += wreck.Hz * wreck.V * frame;
			}
			if (wreck != null && int.TryParse(Environment.GetEnvironmentVariable("TRAFFICSIM_PHYS_TRACE"), out int tc) && mi.Agents[tc] != null && ((int)(race * 60) % 10) == 0)
			{
				Micro.Agent ta = mi.Agents[tc];
				Console.WriteLine("    trace " + race.ToString("0.00") + " car " + tc + " lane " + ta.Lane + " conn " + ta.Conn + " X " + ta.X.ToString("0.0") + " V " + ta.V.ToString("0.0") + " A " + ta.A.ToString("0.0") + " why " + ta.Reason + " " + ta.ReasonCar + " blocks " + mi.BlocksText());
			}
			if (wreck != null)
			{
				TrafficParking.Spot ws = new TrafficParking.Spot { X = wreck.X, Z = wreck.Z, Hx = wreck.Hx, Hz = wreck.Hz };
				int queue = 0;
				foreach (int c in mi.Active)
				{
					double ox, oz, ohx, ohz;
					if (bodies.TryGetValue(c, out StandIn o))
					{
						(ox, oz, ohx, ohz) = (o.X, o.Z, o.Hx, o.Hz);
					}
					else if (live.Pose(c, now, out ox, out oz, out ohx, out ohz, out double _) != TrafficLive.Driving)
					{
						continue;
					}
					if ((ox - wreck.X) * (ox - wreck.X) + (oz - wreck.Z) * (oz - wreck.Z) > 120.0 * 120.0)
					{
						continue;
					}
					queue += mi.Agents[c].V < Micro.Standing ? 1 : 0;
					if (race > wreckAt + 0.5 && TrafficParking.Overlap(ws, new TrafficParking.Spot { X = ox, Z = oz, Hx = ohx, Hz = ohz }, -0.3))
					{
						wreckHits++;
						if (!wreckCars.Contains(c) && wreckCars.Count < 6)
						{
							wreckCars.Add(c);
							Micro.Agent wa = mi.Agents[c];
							Console.WriteLine("    into the wreck: car " + c + " lane " + wa.Lane + " conn " + wa.Conn + " X " + wa.X.ToString("0.0") + " V " + wa.V.ToString("0.0") + " why " + wa.Reason + " wreck V " + wreck.V.ToString("0.0") + " at " + race.ToString("0.0") + " bodies laid " + mi.BlocksText() + " wreck facing " + wreck.Hx.ToString("0.00") + ", " + wreck.Hz.ToString("0.00"));
						}
						Note("car " + c + " in the wreck (car " + wreck.Car + ") at " + wreck.X.ToString("0") + ", " + wreck.Z.ToString("0") + " at " + race.ToString("0.0") + " s");
					}
				}
				wreckQueue = Math.Max(wreckQueue, queue);
			}
			if (shoved >= 0 && bodies.TryGetValue(shoved, out StandIn sb))
			{
				// It lies where it was shoved, as a car off the road on its brakes.
				sb.V = 0.0;
				sb.A = 0.0;
			}
			// Every body clear of every other car drawn near it.
			foreach (StandIn b in bodies.Values)
			{
				if (b.Car == shoved)
				{
					continue;
				}
				TrafficParking.Spot me = new TrafficParking.Spot { X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz };
				foreach (int c in mi.Active)
				{
					if (c == b.Car || c == shoved)
					{
						continue;
					}
					double ox, oz, ohx, ohz;
					if (bodies.TryGetValue(c, out StandIn o))
					{
						if (c < b.Car)
						{
							continue;
						}
						(ox, oz, ohx, ohz) = (o.X, o.Z, o.Hx, o.Hz);
					}
					else if (live.Pose(c, now, out ox, out oz, out ohx, out ohz, out double _) != TrafficLive.Driving)
					{
						continue;
					}
					if ((ox - b.X) * (ox - b.X) + (oz - b.Z) * (oz - b.Z) > 40.0 * 40.0)
					{
						continue;
					}
					if (TrafficParking.Overlap(me, new TrafficParking.Spot { X = ox, Z = oz, Hx = ohx, Hz = ohz }, -0.3))
					{
						overlaps++;
						Micro.Agent ba = mi.Agents[b.Car], ca = mi.Agents[c];
						Note("body " + b.Car + " in car " + c + (o != null ? " (a body)" : "") + " at " + b.X.ToString("0") + ", " + b.Z.ToString("0") + " at " + race.ToString("0.0") + " s (lanes " + ba.Lane + "/" + ba.Conn + " from " + ba.ChangeFrom + " X " + ba.X.ToString("0.0") + " and " + ca.Lane + "/" + ca.Conn + " from " + ca.ChangeFrom + " X " + ca.X.ToString("0.0") + ")");
					}
				}
				Micro.Agent a = mi.Agents[b.Car];
				if (a != null && a.V < Micro.Standing && a.Reason == Micro.Why.None)
				{
					stoodFree.TryGetValue(b.Car, out double since);
					stoodFree[b.Car] = since + frame;
					if (since + frame > 60.0 && since <= 60.0)
					{
						standing++;
						Note("body " + b.Car + " standing a minute with nothing holding it at " + b.X.ToString("0") + ", " + b.Z.ToString("0"));
					}
				}
				else
				{
					stoodFree.Remove(b.Car);
				}
			}
			if (race >= nextCheck)
			{
				nextCheck += 10.0;
				if (!live.M.Conserved(out string why) || !mi.Consistent(out why))
				{
					unconserved++;
					Note("not conserved at " + race.ToString("0") + " s: " + why);
				}
			}
		}
		int straysBefore = mi.Strays - (shoved >= 0 && live.M.Cars[shoved].Mode == Meso.Mode.Gone ? 1 : 0) - (wreck != null ? 1 : 0);
		Console.WriteLine("  physics, " + where + ": " + taken + " bodies taken, " + bodies.Count + " at the end, they drove " + (moved / 3.3 / 1000.0).ToString("0.0") + " km; "
			+ straysBefore + " strayed off their line driving; the shoved one (car " + shoved + ") " + (shoved >= 0 ? live.M.Cars[shoved].Mode.ToString().ToLowerInvariant() : "none") + "; gone from the day " + live.M.Gone);
		Console.WriteLine("    the wreck: " + (wreck != null ? "car " + wreck.Car + " at " + wreck.X.ToString("0") + ", " + wreck.Z.ToString("0") + ", " + wreckHits + " car-frames in it, at most " + wreckQueue + " standing near it" : "none (no body moving)"));
		foreach (string f in firsts)
		{
			Console.WriteLine("    " + f);
		}
		Check(wreckHits == 0, "physics wreck", where + ": cars drove into a wreck in their lane (" + wreckHits + " car-frames)");
		Check(taken > 0 && moved > 300.0, "physics drive", where + ": the bodies hardly drove (" + taken + " taken, " + moved.ToString("0") + " units)");
		Check(overlaps == 0, "physics overlap", where + ": " + overlaps + " frames a body was in another car");
		Check(straysBefore == 0, "physics stray", where + ": " + straysBefore + " bodies strayed off their line driving");
		Check(standing == 0, "physics standing", where + ": " + standing + " bodies stood a minute with nothing holding them");
		Check(unconserved == 0, "physics conservation", where + ": the cars not conserved " + unconserved + " times");
		Check(shoved < 0 || live.M.Cars[shoved].Mode == Meso.Mode.Gone && mi.Agents[shoved] == null, "physics shove", where + ": the shoved body (car " + shoved + ") is still in the day");
	}

	/// <summary>How late a stand-in body's acceleration follows its driver's (seconds) and the drag it has; TRAFFICSIM_PHYS=lag,drag to try others.</summary>
	private static readonly double Lag = Figure(0, 0.2), Drag = Figure(1, 0.4);

	private static double Figure(int k, double fallback)
	{
		string[] parts = (Environment.GetEnvironmentVariable("TRAFFICSIM_PHYS") ?? "").Split(',');
		return k < parts.Length && double.TryParse(parts[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : fallback;
	}

	/// <summary>A stand-in body's reading for the micro: where it is now, standing for the time the other cars are drawn at (a step behind).</summary>
	private static Micro.Reading ReadingOf(StandIn b, double now)
	{
		return new Micro.Reading { X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz, Vx = b.V * b.Hx, Vz = b.V * b.Hz, At = now - Meso.Step, Upright = true };
	}

	/// <summary>
	/// A frame of a stand-in body: its acceleration following the driver's a fifth of a second late less a little drag (a real car never does
	/// just what it is asked), steered by pure pursuit at a point along the way ahead further the faster it goes, turning no tighter than a car.
	/// </summary>
	private static void Drive(StandIn b, double want, int n, double[] xs, double[] zs, double dt)
	{
		b.A += (want - b.A) * Math.Min(1.0, dt / Lag);
		double acc = Math.Max(-Driver.MaxBrake, Math.Min(Driver.Accel * 1.4, b.A)) - Drag;
		b.V = Math.Max(0.0, b.V + acc * dt);
		if (n > 0)
		{
			double look = Math.Max(10.0, Math.Min(45.0, 8.0 + 0.6 * b.V));
			int k = 0;
			while (k + 1 < n && (xs[k] - b.X) * (xs[k] - b.X) + (zs[k] - b.Z) * (zs[k] - b.Z) < look * look)
			{
				k++;
			}
			double tx = xs[k] - b.X, tz = zs[k] - b.Z, tl = Math.Sqrt(tx * tx + tz * tz);
			if (tl > 1.0)
			{
				double sin = (b.Hx * tz - b.Hz * tx) / tl, cos = (b.Hx * tx + b.Hz * tz) / tl;
				double alpha = Math.Atan2(sin, cos);
				double curv = Math.Max(-1.0 / 12.0, Math.Min(1.0 / 12.0, 2.0 * Math.Sin(alpha) / Math.Max(tl, 6.0)));
				double turn = b.V * curv * dt;
				double c = Math.Cos(turn), s = Math.Sin(turn);
				(b.Hx, b.Hz) = (b.Hx * c - b.Hz * s, b.Hx * s + b.Hz * c);
			}
		}
		b.X += b.Hx * b.V * dt;
		b.Z += b.Hz * b.V * dt;
	}
}
