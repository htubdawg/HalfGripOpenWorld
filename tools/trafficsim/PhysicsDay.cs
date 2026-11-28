using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// Phase 5, physics cars (MicroPhysics.cs): the nearest micro cars driven by stand-in bodies as the game drives its physics cars
// (TrafficPhysics.cs). Each body is a plain car model: its acceleration follows the driver's (Agent.A) a fifth of a second late, less a
// little drag, and it steers by pure pursuit along Micro.WayAhead; its reading goes back into the micro every frame, which places the agent
// where the body is. Run frame by frame for 10 minutes round a pretend player driving the lane graph, the bodies taken and let go as the game
// does (the nearest 24 within 300 units, let go past 400). Fails on: a body in another car (body or drawn); a body straying off its line in
// ordinary driving; a micro car driven by a body standing 60 s with nothing holding it; the cars not conserved. Then one body is shoved off
// the road and turned across it: it must stay in its day and find its way back onto its line (Micro.RejoinAim, as the game's body does),
// and the rest carry on; one rolled onto its side must leave the day.
internal static partial class Program
{
	private sealed class StandIn
	{
		public int Car;
		public double X, Z, Hx, Hz, V, A;
		public bool Upright = true;
		public Micro.Rejoin Rejoin;
	}

	/// <summary>
	/// A stand-in body driving its way out of its spot or into it (as TrafficPhysics' Ways does): its rear axle steered by
	/// Manoeuvre.Follow, no tighter than a car turns, its speed following what Follow asks a fifth of a second late; with no way, pulled
	/// up and standing.
	/// </summary>
	private sealed class WayStandIn
	{
		public int Car, Leg;
		public double X, Z, Hx, Hz, V, Held, Since;
		public bool Into = true;
		public Manoeuvre Way;
	}

	/// <summary>A frame of a way stand-in on its way's clock <paramref name="t"/>: how far behind the clock it is, off the way, and turned from it.</summary>
	private static void WayDrive(WayStandIn b, double t, double dt, out double behind, out double off, out double facing)
	{
		b.Way.Follow(ref b.Leg, t, b.X, b.Z, b.Hx, b.Hz, b.V, out double curv, out double speed, out behind, out off, out facing);
		double acc = Math.Max(-Manoeuvre.Stop * 1.5, Math.Min(Manoeuvre.Pull * 1.5, (speed - b.V) / Math.Max(dt, Lag)));
		b.V += acc * dt;
		curv = Math.Max(-1.0 / 12.0, Math.Min(1.0 / 12.0, curv));
		double rx = b.X - b.Hx * Manoeuvre.RearAxle, rz = b.Z - b.Hz * Manoeuvre.RearAxle;
		double turn = b.V * curv * dt, c = Math.Cos(turn), s = Math.Sin(turn);
		(b.Hx, b.Hz) = (b.Hx * c - b.Hz * s, b.Hx * s + b.Hz * c);
		rx += b.Hx * b.V * dt;
		rz += b.Hz * b.V * dt;
		b.X = rx + b.Hx * Manoeuvre.RearAxle;
		b.Z = rz + b.Hz * Manoeuvre.RearAxle;
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
		// Ways out of spots and into them are few near one player in 10 minutes: the stand-ins take them anywhere the micro is round it
		// (the game takes them within 300, as every physics car), for something to test.
		const double wayNear = 1500.0, wayFar = 1700.0;
		const int most = 24;
		Dictionary<int, StandIn> bodies = new Dictionary<int, StandIn>();
		Dictionary<int, WayStandIn> ways = new Dictionary<int, WayStandIn>();
		List<int> wayDrop = new List<int>();
		int wayOuts = 0, wayIns = 0, wayTaken = 0, wayKnocked = 0, wayStuck = 0, wayHits = 0;
		double wayHeldMost = 0.0, wayOffMost = 0.0;
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
		int shoved = -1, rolled = -1;
		double shovedAt = 0.0, shovedBack = -1.0, rolledAt = 0.0;
		bool shovedAstray = false;
		string shovedHow = "back on its line";
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
					if (b.Upright && live.M.Cars[b.Car].Mode == Meso.Mode.Parked && (live.PulledUp(b.Car) || live.Manoeuvring(b.Car)))
					{
						// Pulled up at its stop: it drives into its spot (the way stand-ins).
						ways[b.Car] = new WayStandIn { Car = b.Car, X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz, Since = race };
					}
					continue;
				}
				if ((pd > far && b.Car != shoved && b.Car != rolled) || jump)
				{
					mi.GiveBody(b.Car, b.X, b.Z);
					drop.Add(b.Car);
					continue;
				}
				if (!b.Upright)
				{
					b.V = 0.0;
				}
				else if (mi.IsAstray(b.Car, out double off))
				{
					RejoinDrive(b, a.A, off, n, xs, zs, frame);
				}
				else
				{
					b.Rejoin = default;
					Drive(b, a.A, n, xs, zs, frame);
				}
				moved += b.V * frame;
			}
			foreach (int c in drop)
			{
				bodies.Remove(c);
			}
			// The way stand-ins: each driven a frame on its way's clock (held back while it is behind), taken by the micro at the end of a
			// way out, let go at the end of a way in.
			wayDrop.Clear();
			foreach (WayStandIn b in ways.Values)
			{
				Manoeuvre m = live.WayOf(b.Car, now - Meso.Step, out double t, out bool into);
				double pd = Math.Sqrt((b.X - w.SX) * (b.X - w.SX) + (b.Z - w.SZ) * (b.Z - w.SZ));
				if (pd > wayFar)
				{
					wayDrop.Add(b.Car);
					continue;
				}
				if (m == null)
				{
					b.Way = null;
					b.V = 0.0;
					if (live.PulledUp(b.Car))
					{
						continue;
					}
					wayDrop.Add(b.Car);
					Micro.Agent a = mi.Agents[b.Car];
					StandIn sb = new StandIn { Car = b.Car, X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz, V = 0.0 };
					if (!b.Into && a != null && !a.Driven && live.M.Cars[b.Car].Mode == Meso.Mode.Driving && mi.TakeBody(b.Car, ReadingOf(sb, now)))
					{
						bodies[b.Car] = sb;
						wayOuts++;
					}
					else
					{
						wayIns += b.Into ? 1 : 0;
					}
					continue;
				}
				if (b.Way != m)
				{
					b.Way = m;
					b.Leg = m.ClockLeg(t);
					b.Held = 0.0;
				}
				b.Into = into;
				WayDrive(b, t, frame, out double behind, out double off, out double facing);
				moved += Math.Abs(b.V) * frame;
				wayOffMost = Math.Max(wayOffMost, off);
				if (race - b.Since > 0.5 && Manoeuvre.OffWay(off, facing))
				{
					wayKnocked++;
					Note("way body " + b.Car + (into ? " in" : " out") + " off its way (" + off.ToString("0.0") + " off, facing " + facing.ToString("0.00") + ") at " + b.X.ToString("0") + ", " + b.Z.ToString("0") + " at " + race.ToString("0.0") + " s");
					wayDrop.Add(b.Car);
					continue;
				}
				if (behind > 1.5)
				{
					live.WindBack(b.Car, frame);
					b.Held += frame;
					wayHeldMost = Math.Max(wayHeldMost, b.Held);
					if (b.Held > 30.0)
					{
						wayStuck++;
						Note("way body " + b.Car + " held up 30 s behind its way's clock at " + b.X.ToString("0") + ", " + b.Z.ToString("0"));
						wayDrop.Add(b.Car);
					}
				}
				else if (behind < 0.5)
				{
					b.Held = 0.0;
				}
			}
			foreach (int c in wayDrop)
			{
				ways.Remove(c);
			}
			if (race >= nextTake && race < end)
			{
				nextTake = race + 0.2;
				List<(double d, int car)> cands = new List<(double, int)>();
				foreach (int c in mi.Active)
				{
					Micro.Agent a = mi.Agents[c];
					if (a.Driven || (a.Conn >= 0 && Lanes.Connectors[a.Conn].Turn == Lanes.Jump) || a.HoldUntil > mi.T || live.Manoeuvring(c))
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
				foreach (int c in live.WaysNow)
				{
					if (ways.ContainsKey(c) || bodies.ContainsKey(c) || live.Pose(c, now, out double x, out double z, out double _, out double _, out double _) != TrafficLive.Driving)
					{
						continue;
					}
					double d = Math.Sqrt((x - w.SX) * (x - w.SX) + (z - w.SZ) * (z - w.SZ));
					if (d < wayNear)
					{
						cands.Add((d * near / wayNear, c));
					}
				}
				cands.Sort();
				foreach ((double _, int c) in cands)
				{
					if (bodies.Count + ways.Count >= most)
					{
						break;
					}
					if (live.Manoeuvring(c))
					{
						live.Pose(c, now, out double wx, out double wz, out double whx, out double whz, out double _);
						live.WayOf(c, now - Meso.Step, out double wt, out bool win).At(wt, out double _, out double _, out double _, out double _, out double wvx, out double wvz);
						ways[c] = new WayStandIn { Car = c, X = wx, Z = wz, Hx = whx, Hz = whz, V = wvx * whx + wvz * whz, Into = win, Since = race };
						wayTaken++;
						continue;
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
			if (shoved >= 0 && shovedBack < 0.0 && Environment.GetEnvironmentVariable("TRAFFICSIM_SHOVE_TRACE") == "1" && ((int)(race * 60) % 60) == 0
				&& bodies.TryGetValue(shoved, out StandIn st))
			{
				Micro.Agent ta = mi.Agents[shoved];
				Console.WriteLine("    shoved " + (race - shovedAt).ToString("0") + " s: at " + st.X.ToString("0.0") + ", " + st.Z.ToString("0.0") + " facing " + st.Hx.ToString("0.00") + ", " + st.Hz.ToString("0.00") + " V " + st.V.ToString("0.0")
					+ (ta == null ? " no agent" : " lane " + ta.Lane + " conn " + ta.Conn + " X " + ta.X.ToString("0.0") + " off " + ta.Off.ToString("0.0") + " astray " + ta.Astray + " A " + ta.A.ToString("0.0") + " why " + ta.Reason + " " + ta.ReasonCar + " best " + ta.StrayBest.ToString("0.0") + " back " + st.Rejoin.Back.ToString("0.0")));
			}
			if (shoved >= 0 && shovedBack < 0.0)
			{
				Micro.Agent sa = mi.Agents[shoved];
				shovedAstray |= sa != null && sa.Astray;
				if (shovedAstray && sa != null && !sa.Astray && sa.Driven)
				{
					shovedBack = race;
				}
				else if (sa == null && live.M.Cars[shoved].Mode != Meso.Mode.Gone)
				{
					// Its agent gone but not from the day: it reached its stop (parked) on the way back, or was handed to the meso.
					shovedBack = race;
					shovedHow = live.M.Cars[shoved].Mode.ToString().ToLowerInvariant();
				}
			}
			// The roll: a moving body put on its side where it is.
			if (rolled < 0 && race >= end * 0.6)
			{
				foreach (StandIn b in bodies.Values)
				{
					if (b.Car != shoved && b.V > 2.0)
					{
						rolled = b.Car;
						rolledAt = race;
						b.Upright = false;
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
					if (b.Car != shoved && b.Car != rolled && b.V > 5.0 && a != null && a.Conn < 0)
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
			// Every body clear of every other car drawn near it.
			foreach (StandIn b in bodies.Values)
			{
				if (!b.Upright || mi.IsAstray(b.Car, out double _))
				{
					// Off the road (finding its way back) or on its side: the drivers don't see it as in their lane.
					continue;
				}
				TrafficParking.Spot me = new TrafficParking.Spot { X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz };
				foreach (int c in mi.Active)
				{
					if (c == b.Car || mi.IsAstray(c, out double _) || (bodies.TryGetValue(c, out StandIn oc) && !oc.Upright))
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
			foreach (WayStandIn b in ways.Values)
			{
				if (b.Way == null)
				{
					continue;
				}
				TrafficParking.Spot me = new TrafficParking.Spot { X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz };
				foreach (int c in mi.Active)
				{
					if (c == b.Car || ways.ContainsKey(c) || mi.IsAstray(c, out double _))
					{
						continue;
					}
					double ox, oz, ohx, ohz;
					if (bodies.TryGetValue(c, out StandIn o))
					{
						(ox, oz, ohx, ohz) = (o.X, o.Z, o.Hx, o.Hz);
					}
					else if (live.Pose(c, now, out ox, out oz, out ohx, out ohz, out double _) != TrafficLive.Driving)
					{
						continue;
					}
					if ((ox - b.X) * (ox - b.X) + (oz - b.Z) * (oz - b.Z) < 40.0 * 40.0 && TrafficParking.Overlap(me, new TrafficParking.Spot { X = ox, Z = oz, Hx = ohx, Hz = ohz }, -0.3))
					{
						wayHits++;
						Note("way body " + b.Car + (b.Into ? " in" : " out") + " in car " + c + " at " + b.X.ToString("0") + ", " + b.Z.ToString("0") + " at " + race.ToString("0.0") + " s");
					}
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
		int straysBefore = mi.Strays - (shovedAstray ? 1 : 0);
		int lostBefore = mi.LostCars - (wreck != null ? 1 : 0) - (rolled >= 0 && live.M.Cars[rolled].Mode == Meso.Mode.Gone ? 1 : 0);
		bool rolledGone = rolled < 0 || live.M.Cars[rolled].Mode == Meso.Mode.Gone && mi.Agents[rolled] == null;
		Console.WriteLine("  physics, " + where + ": " + taken + " bodies taken, " + bodies.Count + " at the end, they drove " + (moved / 3.3 / 1000.0).ToString("0.0") + " km; "
			+ straysBefore + " strayed off their line driving, " + lostBefore + " left the day driving; the shoved one (car " + shoved + ") "
			+ (shoved < 0 ? "none" : shovedBack >= 0.0 ? shovedHow + " " + (shovedBack - shovedAt).ToString("0.0") + " s after" : live.M.Cars[shoved].Mode == Meso.Mode.Gone ? "gone from its day" : shovedAstray ? "still off its line" : "never astray")
			+ "; the rolled one (car " + rolled + ") " + (rolled < 0 ? "none" : rolledGone ? "gone from its day" : "still in its day") + "; gone from the day " + live.M.Gone + ", rejoined " + mi.Rejoins);
		Console.WriteLine("    ways driven by bodies: " + wayTaken + " taken on their way, " + wayOuts + " out and taken on the lane, " + wayIns + " into their spot, held up at most " + wayHeldMost.ToString("0.0") + " s behind the clock, at most " + wayOffMost.ToString("0.0") + " off its way; "
			+ wayKnocked + " off their way, " + wayStuck + " stuck, " + wayHits + " frames in a car");
		Console.WriteLine("    the wreck: " + (wreck != null ? "car " + wreck.Car + " at " + wreck.X.ToString("0") + ", " + wreck.Z.ToString("0") + ", " + wreckHits + " car-frames in it, at most " + wreckQueue + " standing near it" : "none (no body moving)"));
		foreach (string f in firsts)
		{
			Console.WriteLine("    " + f);
		}
		Check(wayKnocked == 0 && wayStuck == 0, "physics ways", where + ": " + wayKnocked + " bodies driving their way out of a spot or into one went off it, " + wayStuck + " were stuck");
		Check(wayHits == 0, "physics ways", where + ": " + wayHits + " frames a body driving its way out of a spot or into one was in a car");
		Check(wreckHits == 0, "physics wreck", where + ": cars drove into a wreck in their lane (" + wreckHits + " car-frames)");
		Check(taken > 0 && moved > 300.0, "physics drive", where + ": the bodies hardly drove (" + taken + " taken, " + moved.ToString("0") + " units)");
		Check(overlaps == 0, "physics overlap", where + ": " + overlaps + " frames a body was in another car");
		Check(straysBefore == 0 && lostBefore == 0, "physics stray", where + ": " + straysBefore + " bodies strayed off their line driving, " + lostBefore + " left the day");
		Check(standing == 0, "physics standing", where + ": " + standing + " bodies stood a minute with nothing holding them");
		Check(unconserved == 0, "physics conservation", where + ": the cars not conserved " + unconserved + " times");
		Check(shoved < 0 || shovedBack >= 0.0 && shovedBack - shovedAt < 45.0, "physics shove", where + ": the shoved body (car " + shoved + ") didn't find its way back onto its line within 45 s");
		Check(rolledGone, "physics roll", where + ": the body on its side (car " + rolled + ") is still in the day");
	}

	/// <summary>
	/// A frame of an astray stand-in body finding its way back (as the game's body does, <see cref="Micro.RejoinAim"/>): at a crawl forward
	/// towards a point along its way ahead, held back by its driver's acceleration, or backing up with the wheels the other way.
	/// </summary>
	private static void RejoinDrive(StandIn b, double want, double off, int n, double[] xs, double[] zs, double dt)
	{
		bool back = Micro.RejoinAim(ref b.Rejoin, b.X, b.Z, b.Hx, b.Hz, b.V, off, n, xs, zs, dt, out double tx, out double tz);
		double dx = tx - b.X, dz = tz - b.Z, tl = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
		double sin = (b.Hx * dz - b.Hz * dx) / tl, cos = (b.Hx * dx + b.Hz * dz) / tl;
		double alpha = Math.Atan2(sin, cos);
		double target = back ? -Micro.RejoinBack : Math.Min(Micro.RejoinSpeed, b.V + Math.Max(0.0, want) * 2.0);
		if (!back && want < 0.0)
		{
			target = Math.Max(0.0, b.V + want * dt);
		}
		double acc = Math.Max(-Driver.MaxBrake, Math.Min(Driver.Accel, 1.5 * (target - b.V)));
		b.V += acc * dt;
		// Forward: pure pursuit, no tighter than a car turns; back: full lock the other way (the nose comes round towards the point).
		double curv = back ? -Math.Sign(alpha) / 12.0 : cos < Micro.RejoinLock ? Math.Sign(alpha) / 12.0 : Math.Max(-1.0 / 12.0, Math.Min(1.0 / 12.0, 2.0 * Math.Sin(alpha) / Math.Max(tl, 6.0)));
		double turn = b.V * curv * dt;
		double c = Math.Cos(turn), s = Math.Sin(turn);
		(b.Hx, b.Hz) = (b.Hx * c - b.Hz * s, b.Hx * s + b.Hz * c);
		b.X += b.Hx * b.V * dt;
		b.Z += b.Hz * b.V * dt;
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
		return new Micro.Reading { X = b.X, Z = b.Z, Hx = b.Hx, Hz = b.Hz, Vx = b.V * b.Hx, Vz = b.V * b.Hz, At = now - Meso.Step, Upright = b.Upright };
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
