using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Part of <see cref="Micro"/>: physics cars (the rework's phase 5, <c>docs/TRAFFIC_REWORK.md</c> §4: "a physics car is a micro car whose
	/// position comes from physics"). The game makes the nearest micro cars real cars (<see cref="TrafficPhysics"/>); each stays a micro agent,
	/// deciding as every other does, but where it is and how fast it goes are read back from its body each step instead of worked out: the
	/// body's middle projected onto the agent's lane or connector gives its <see cref="Agent.X"/>, its speed along the line its
	/// <see cref="Agent.V"/>. The driver's acceleration (<see cref="Agent.A"/>) is what the body's pedals are worked from, and
	/// <see cref="WayAhead"/> the line it steers along. So there is nothing to reconcile: a body slowed by a hill, held up by the player or
	/// pushed back is simply where it is, and the cars behind see it there.
	///
	/// <para>A body knocked off its line (more than a lane off it, or turned away from it, for a second) is <see cref="Agent.Astray"/>: it
	/// keeps its place in its queue and its day, and finds its way back at a crawl (<see cref="RejoinAim"/>: onto its line at a slant, backing
	/// round first if it faces the wrong way or has run into something), the owner's report: a car pushed off the road made no attempt to get
	/// back, even pushed back onto it. Only a body that gets no nearer for <see cref="GiveUpAfter"/>, lies on its side for
	/// <see cref="OnSideFor"/>, or is wrecked leaves the day (<see cref="Meso.Mode.Gone"/>): its household goes without it (the plan's rule).
	/// No Unity types: <c>tools/trafficsim</c> drives stand-in bodies through the same calls.</para>
	/// </summary>
	internal sealed partial class Micro
	{
		/// <summary>A physics car's body as last read: its middle (world x, z), the way it faces (unit), its velocity, whether it is upright,
		/// and the day second the reading stands for (the game gives the time its other cars are drawn at, a step behind its clock).</summary>
		internal struct Reading
		{
			public double X, Z, Hx, Hz, Vx, Vz, At;
			public bool Upright;
		}

		/// <summary>Per car, its body's last reading (only for cars driven by a body).</summary>
		private Reading[] readings;

		/// <summary>How far a body may be off its line, and how far its way may be turned from the line's (the cosine), before it is straying.</summary>
		private const double StrayOff = 14.0, StrayFacing = 0.5;
		/// <summary>How long a body may be off its line before it is astray and finds its way back (seconds): a wobble onto the verge isn't.</summary>
		private const double StrayFor = 1.0;
		/// <summary>Back on its line: this near it and facing along it this well (less than the straying figures, so it doesn't flicker).</summary>
		private const double BackOff = 6.0, BackFacing = 0.85;
		/// <summary>An astray body that gets no nearer its line (by <see cref="Nearer"/>) for this long is stuck and leaves the day; one on its side this long too (seconds).</summary>
		internal const double GiveUpAfter = 30.0, OnSideFor = 4.0;
		private const double Nearer = 3.0;
		/// <summary>How fast an astray body goes forward and back finding its way to its line (units a second: about 9 and 4 km/h).</summary>
		internal const double RejoinSpeed = 8.0, RejoinBack = 4.0;
		/// <summary>Forward with the point it heads for less than this far ahead (the cosine of the angle to it), full lock towards it.</summary>
		internal const double RejoinLock = 0.5;
		/// <summary>The furthest a reading may move an agent on in one step (a body put somewhere else is let go, not followed).</summary>
		private const double MostRead = 30.0;

		/// <summary>For the overlay and trafficsim: cars driven by bodies now, times a body went astray, cars that left the day (wrecked, on
		/// their side, stuck off their line), astray bodies that got back to their line.</summary>
		internal int BodiesNow, Strays, LostCars, Rejoins;

		/// <summary>
		/// Car <paramref name="i"/> is driven by a body from now on (its first reading given with it): its place and speed read from the body.
		/// False if it isn't a micro car.
		/// </summary>
		internal bool TakeBody(int i, Reading r)
		{
			Agent a = Agents[i];
			if (a == null)
			{
				return false;
			}
			readings ??= new Reading[Agents.Length];
			readings[i] = r;
			if (!a.Driven)
			{
				a.Driven = true;
				a.OffLeft = 0.0;
				a.StrayedFor = 0.0;
				a.Astray = false;
				a.OnSide = 0.0;
				BodiesNow++;
			}
			return true;
		}

		/// <summary>Car <paramref name="i"/>'s body read now (each frame, before the simulation steps).</summary>
		internal void Read(int i, Reading r)
		{
			if (readings != null && Agents[i] != null && Agents[i].Driven)
			{
				readings[i] = r;
			}
		}

		/// <summary>
		/// Car <paramref name="i"/>'s body is let go (the player went off, or it was taken back for another): it drives on as the others do,
		/// from where the body was, its drawn middle settling onto its line from the body's (world <paramref name="x"/>, <paramref name="z"/>).
		/// </summary>
		internal void GiveBody(int i, double x, double z)
		{
			Agent a = Agents[i];
			if (a == null || !a.Driven)
			{
				return;
			}
			a.Driven = false;
			a.Astray = false;
			BodiesNow--;
			SettleFrom(a, x, z);
		}

		/// <summary>Whether car <paramref name="i"/> is driven by a body.</summary>
		internal bool HasBody(int i)
		{
			return Agents[i] != null && Agents[i].Driven;
		}

		/// <summary>Every body's reading put into its agent: where along its line it is (forward only), its speed; a body straying too long leaves the day.</summary>
		private void ReadBodies()
		{
			if (BodiesNow == 0 || readings == null)
			{
				return;
			}
			scratch.Clear();
			scratch.AddRange(Active);
			foreach (int i in scratch)
			{
				Agent a = Agents[i];
				if (a != null && a.Driven)
				{
					ReadBody(i, a);
				}
			}
		}

		private void ReadBody(int i, Agent a)
		{
			Reading r = readings[i];
			Meso.Vehicle v = M.Cars[i];
			double dt = Math.Max(-0.5, Math.Min(0.5, T - r.At));
			double px = r.X + r.Vx * dt, pz = r.Z + r.Vz * dt;
			double x, side, ux, uz;
			if (a.Conn >= 0)
			{
				OntoConnector(Lanes.Connectors[a.Conn], a.X, px, pz, out x, out side, out ux, out uz);
			}
			else
			{
				OntoLane(a.Lane, a.X, px, pz, out x, out side, out ux, out uz);
			}
			double facing = r.Hx * ux + r.Hz * uz;
			a.Off = side;
			a.OnSide = r.Upright ? 0.0 : a.OnSide + Dt;
			if (a.OnSide > OnSideFor)
			{
				LoseCar(i);
				return;
			}
			if (!a.Astray)
			{
				bool off = Math.Abs(side) > StrayOff || facing < StrayFacing;
				a.StrayedFor = off ? a.StrayedFor + Dt : 0.0;
				if (a.StrayedFor > StrayFor)
				{
					a.Astray = true;
					a.StrayBest = StrayScore(side, facing);
					a.NoNearer = 0.0;
					Strays++;
					Log(Kind.Astray, i);
				}
			}
			else if (Math.Abs(side) < BackOff && facing > BackFacing && r.Upright)
			{
				a.Astray = false;
				a.StrayedFor = 0.0;
				Rejoins++;
				Log(Kind.Rejoined, i);
			}
			else
			{
				double score = StrayScore(side, facing);
				if (score < a.StrayBest - Nearer)
				{
					a.StrayBest = score;
					a.NoNearer = 0.0;
				}
				else if ((a.NoNearer += Dt) > GiveUpAfter)
				{
					LoseCar(i);
					return;
				}
			}
			// Where the body is, back as well as on (put down where it was drawn, a moment behind its agent; pushed back), but never back
			// into the car behind it in the micro's order: that car would then count it as behind and drive into it.
			double floor = Math.Max(a.X - MostRead, Behind(i, a) + Traffic.CarLength + 0.5 * Driver.Gap0);
			x = Math.Max(Math.Min(x, a.X + MostRead), Math.Min(floor, a.X));
			if (a.Conn < 0 && !v.Ends && !a.Go)
			{
				// Past its stop line without having decided to go (it braked late): it is still waiting there, as far as the others know.
				x = Math.Min(x, Math.Max(a.X, Meso.Reach(a.Lane) - 0.01));
			}
			a.X = x;
			a.V = Math.Max(0.0, r.Vx * ux + r.Vz * uz);
		}

		/// <summary>How far an astray body is from being back on its line: its distance off it, and a turn the wrong way counted as distance.</summary>
		internal static double StrayScore(double side, double facing)
		{
			return Math.Abs(side) + 20.0 * (1.0 - facing);
		}

		/// <summary>Whether car <paramref name="i"/>'s body is astray (knocked off its line, finding its way back), and how far off it is (left +).</summary>
		internal bool IsAstray(int i, out double off)
		{
			Agent a = Agents[i];
			off = a != null ? a.Off : 0.0;
			return a != null && a.Driven && a.Astray;
		}

		/// <summary>An astray body's manoeuvre: how long it has still to back up, how long it has been standing when it meant to move.</summary>
		internal struct Rejoin
		{
			public double Back, Stuck;
		}

		/// <summary>
		/// How an astray body finds its way back to its line (the game's body and trafficsim's stand-ins alike): the point it heads for along
		/// its way ahead (<paramref name="n"/> points <paramref name="xs"/>, <paramref name="zs"/>) at least its distance off the line on, so it
		/// comes back onto the line at a slant rather than square across it; and whether to back up (true) rather than drive forward. It backs
		/// up, the wheels the other way, when that point is behind it (a car pushed round), or when it has stood a while meaning to move (nosed
		/// into something); it stops backing once the point is fairly ahead, after a few seconds, or when backing gets it nowhere. Going forward
		/// with the point well off to one side (<see cref="RejoinLock"/>) it turns on full lock, not the wide arc pure pursuit would take.
		/// Its middle (<paramref name="x"/>, <paramref name="z"/>), the way it faces (unit), its speed forward (back, less than nothing).
		/// </summary>
		internal static bool RejoinAim(ref Rejoin s, double x, double z, double hx, double hz, double v, double off, int n, double[] xs, double[] zs,
			double dt, out double tx, out double tz)
		{
			tx = x + hx;
			tz = z + hz;
			if (n == 0)
			{
				s.Back = 0.0;
				return false;
			}
			double look = Math.Max(12.0, Math.Abs(off) + 12.0);
			int k = 0;
			while (k + 1 < n && (xs[k] - x) * (xs[k] - x) + (zs[k] - z) * (zs[k] - z) < look * look)
			{
				k++;
			}
			tx = xs[k];
			tz = zs[k];
			double dx = tx - x, dz = tz - z, dl = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
			double cos = (hx * dx + hz * dz) / dl;
			if (s.Back > 0.0)
			{
				s.Back -= dt;
				s.Stuck = Math.Abs(v) < 0.3 ? s.Stuck + dt : 0.0;
				if (cos > 0.7 || s.Stuck > 1.5)
				{
					s.Back = 0.0;
					s.Stuck = 0.0;
				}
				return s.Back > 0.0;
			}
			if (Math.Abs(v) < 2.0 && cos < 0.0)
			{
				// Facing away from it: back round (a three-point turn's first leg).
				s.Back = 4.0;
				s.Stuck = 0.0;
				return true;
			}
			s.Stuck = Math.Abs(v) < 0.5 ? s.Stuck + dt : 0.0;
			if (s.Stuck > 2.5)
			{
				// Meaning to go forward but standing: up against something. Back off it.
				s.Back = 2.5;
				s.Stuck = 0.0;
				return true;
			}
			return false;
		}

		/// <summary>The front of the car behind car <paramref name="i"/> on its lane or connector in the last step's order (no car: far back).</summary>
		private double Behind(int i, Agent a)
		{
			List<int> list = a.Conn >= 0 ? onConn[a.Conn] : onLane[a.Lane];
			double best = double.MinValue;
			foreach (int j in list)
			{
				Agent b = Agents[j];
				if (j != i && b != null && b.X < a.X && b.X > best && (b.Conn >= 0) == (a.Conn >= 0))
				{
					best = b.X;
				}
			}
			return best;
		}

		/// <summary>
		/// World point (<paramref name="px"/>, <paramref name="pz"/>), a car's middle, as a front's distance along lane <paramref name="lane"/>'s
		/// carriageway (as <see cref="Agent.X"/> counts it), looked for from <paramref name="guess"/>; how far it is off the line (left +) and the
		/// line's way there. Past either end of the lane it carries on along the end's tangent.
		/// </summary>
		private static void OntoLane(int lane, double guess, double px, double pz, out double x, out double side, out double ux, out double uz)
		{
			Lanes.Lane l = Lanes.All[lane];
			int cw = Meso.CarriagewayOf(lane);
			double lo = Math.Min(l.Start, l.End), hi = Math.Max(l.Start, l.End);
			double s = Math.Max(lo, Math.Min(hi, Meso.RoadS(cw, guess - Lanes.Axle)));
			double lx = 0.0, lz = 0.0;
			ux = 1.0;
			uz = 0.0;
			for (int it = 0; it < 4; it++)
			{
				Lanes.Point(l, s, out lx, out lz, out double _, out ux, out uz);
				double t = (px - lx) * ux + (pz - lz) * uz;
				double ns = Math.Max(lo, Math.Min(hi, s + t * l.Dir));
				bool done = Math.Abs(ns - s) < 0.05;
				s = ns;
				if (done)
				{
					break;
				}
			}
			Lanes.Point(l, s, out lx, out lz, out double _, out ux, out uz);
			double along = (px - lx) * ux + (pz - lz) * uz;
			side = (px - lx) * -uz + (pz - lz) * ux;
			// What is left along the line is past an end (within it, Newton has taken it to nothing).
			x = Meso.Along(cw, s) + Lanes.Axle + along;
		}

		/// <summary>The same onto connector <paramref name="cn"/>: how far along its line (as a car's <see cref="Agent.X"/> on it), looked for near <paramref name="guess"/>.</summary>
		private static void OntoConnector(Lanes.Connector cn, double guess, double px, double pz, out double x, out double side, out double ux, out double uz)
		{
			int n = cn.X.Length;
			x = guess;
			side = 0.0;
			ux = 1.0;
			uz = 0.0;
			double best = double.MaxValue;
			for (int p = 0; p + 1 < n; p++)
			{
				if (cn.D[p + 1] < guess - 15.0 || cn.D[p] > guess + 45.0)
				{
					continue;
				}
				double ax = cn.X[p], az = cn.Z[p], ex = cn.X[p + 1] - ax, ez = cn.Z[p + 1] - az, len2 = ex * ex + ez * ez;
				if (len2 < 1e-12)
				{
					continue;
				}
				double raw = ((px - ax) * ex + (pz - az) * ez) / len2;
				// The first and last pieces carry on past the connector's ends.
				double t = Math.Max(p == 0 ? double.MinValue : 0.0, Math.Min(p + 2 == n ? double.MaxValue : 1.0, raw));
				double qx = ax + ex * t, qz = az + ez * t, dx = px - qx, dz = pz - qz, d2 = dx * dx + dz * dz;
				if (d2 < best)
				{
					double len = Math.Sqrt(len2);
					best = d2;
					ux = ex / len;
					uz = ez / len;
					x = cn.D[p] + (cn.D[p + 1] - cn.D[p]) * t;
					side = dx * -uz + dz * ux;
				}
			}
		}

		/// <summary>
		/// Car <paramref name="i"/> leaves the day where it is (a body wrecked, on its side, or stuck off its line): out of its queue and the
		/// micro, gone from its plans (<see cref="Meso.Mode.Gone"/>). Its body stays in the world as the game has it.
		/// </summary>
		internal void LoseCar(int i)
		{
			Agent a = Agents[i];
			if (a == null)
			{
				return;
			}
			Log(Kind.Lost, i);
			Meso.Vehicle v = M.Cars[i];
			Remove(i, v.Lane);
			M.Lose(v);
			LostCars++;
		}

		/// <summary>
		/// The way ahead of car <paramref name="i"/>'s middle, a point every <paramref name="step"/> into <paramref name="xs"/>, <paramref name="zs"/>
		/// (world), from where it is now: along its lane (the lane it is changing into) and connector and the lane after, as far as its route
		/// is known (the connector after next is chosen only as it comes to it). How many points; <paramref name="jump"/> if the way runs into the
		/// wrap's tunnel (a body can't follow that).
		/// </summary>
		internal int WayAhead(int i, double step, double[] xs, double[] zs, out bool jump)
		{
			jump = false;
			Agent a = Agents[i];
			if (a == null)
			{
				return 0;
			}
			Meso.Vehicle v = M.Cars[i];
			int lane = a.Lane, conn = a.Conn, hops = 0, n = 0;
			double x = a.X;
			if (conn >= 0 && Lanes.Connectors[conn].Turn == Lanes.Jump)
			{
				jump = true;
				return 0;
			}
			while (n < xs.Length && hops < 4)
			{
				if (conn >= 0)
				{
					Lanes.Connector cn = Lanes.Connectors[conn];
					if (x <= cn.Length)
					{
						OnConnector(cn, x, out xs[n], out zs[n], out double _, out double _);
						n++;
						x += step;
						continue;
					}
					x = Meso.JoinAt(conn) + (x - cn.Length);
					lane = cn.To;
					conn = -1;
					hops++;
					continue;
				}
				double end = Meso.Reach(lane);
				// The connector it takes at this lane's end: known for the lane it is on, or the lane after the connector it is on.
				int c = -1;
				if (!v.Ends && ((a.Conn < 0 && hops == 0) || (a.Conn >= 0 && hops == 1)))
				{
					c = NextConn(i, lane);
				}
				if (x <= end || c < 0 || Lanes.Connectors[c].From != lane)
				{
					Lanes.Lane l = Lanes.All[lane];
					int cw = Meso.CarriagewayOf(lane);
					double lo = Math.Min(l.Start, l.End), hi = Math.Max(l.Start, l.End);
					double s = Meso.RoadS(cw, x - Lanes.Axle);
					if (s < lo || s > hi)
					{
						if (n > 0 && x > end)
						{
							// The end of what is known: no further.
							break;
						}
						s = Math.Max(lo, Math.Min(hi, s));
					}
					Lanes.Point(l, s, out xs[n], out zs[n], out double _, out double _, out double _);
					n++;
					x += step;
					continue;
				}
				if (Lanes.Connectors[c].Turn == Lanes.Jump)
				{
					jump = true;
					break;
				}
				x -= end;
				conn = c;
				hops++;
			}
			return n;
		}
	}
}
