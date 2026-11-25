using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The arithmetic of a physics traffic car's driver (stage 3 of the traffic plan), kept out of the game so trafficview can test it:
	/// where on its leg a car is (<see cref="Project"/>), the line it steers along (its lane, through a junction on the same curve stage 2
	/// draws it on), the speed it aims for (<see cref="Target"/>: the leg's speed profile, the car in front, a junction to give way at, the
	/// end of its trip, and never ahead of its timetable by much), and how late it is when it goes back on its timetable
	/// (<see cref="Lateness"/>).
	///
	/// The driver is local and simple: it does not have to match the day exactly, only never drive through anyone and never block a
	/// junction. A physics car is driven by one machine only, so floats and its own order are fine here; the doubles are for the world's
	/// coordinates. No Unity types.
	/// </summary>
	internal static class TrafficDrive
	{
		/// <summary>A car's length (units), centre to centre the least two cars one behind the other can be.</summary>
		internal const double CarLength = 14.0;
		/// <summary>How far ahead along its path a car looks for the car in front (as the day does).</summary>
		internal const double LookAhead = 200.0;
		/// <summary>The last stretch of a trip: there a physics car goes back on its timetable, which draws the pull into its space.</summary>
		internal const double EndZone = 45.0;
		/// <summary>No car made a physics car within this of a tunnel along its path (the day drives the wrap and the road out); one already
		/// a physics car drives on into the tunnel and goes back in its dark (<see cref="InDark"/>).</summary>
		internal const double TunnelKeep = 80.0;
		/// <summary>Kept waiting this long at a junction, a car gives way only to cars in the box, not to cars still coming (no deadlock);
		/// three times as long, not to a drawn car standing in the box either (the day holds it there for someone who is a physics car now).</summary>
		internal const double HoldBreak = 8.0;
		/// <summary>How far past the room a car needs beyond a junction's box it looks for the end of a queue there.</summary>
		private const double QueueLook = 120.0;
		/// <summary>Where the cars past the junction's box are (reused: the drivers run on one thread).</summary>
		[ThreadStatic] private static List<double> pastBox;
		/// <summary>Seconds off the box a car coming is still let go first by a car tired of waiting (<see cref="HoldBreak"/>).</summary>
		private const double ImpatientLook = 2.5;
		/// <summary>Seconds held at a side road's red before a physics car gives up on the light (see <see cref="Target"/>).</summary>
		internal const double LightGiveUp = 30.0;
		/// <summary>Seconds more a physics car giving way leaves before a car with the right of way gets to the box, than a day's car does (less as it waits, as the day's margin).</summary>
		private const double PhysicsGapMore = 1.5;
		/// <summary>How far ahead of its timetable a car may get before it eases off (not on the last stretch: it goes back early at its end).</summary>
		internal const double EarlyMost = 40.0;
		/// <summary>The time gap a car keeps to the car in front when working out a safe speed (seconds), and to a line it stops at.</summary>
		private const double FollowGap = 1.0, StopGap = 0.5;

		/// <summary>A car near a physics car: another physics car (where it is on its leg) or a drawn one (where the day has it).</summary>
		internal struct Mover
		{
			public int Car, Leg;
			public double P, V;
			public double X, Z, Hx, Hz;
			public bool Physical;
			/// <summary>A drawn car pulling out of its space or into one: drawn off its lane (X, Z is where), so also in the way there.</summary>
			public bool Pulling;
			/// <summary>Filled by <see cref="Prepare"/>: the edge, way and road distance it is on (-1: none), and the junction ahead (its middle
			/// not yet passed): the node (-1: none), where it is along the path, the arms in and out, and how far to the box's near edge
			/// (negative: in the box's near half).</summary>
			public int Edge, Dir;
			public double S;
			/// <summary>Filled by <see cref="Prepare"/>: how far over to the trunk's inner lane it is (0 the outer lane, and on every other road).</summary>
			public double Lane;
			public int Node, InArm, OutArm;
			public double NodeP, ToBox;
			/// <summary>The junction whose box's far half it is in (-1: none), with its arms: two junctions can be closer than their boxes.</summary>
			public int Back, BackIn, BackOut;
			/// <summary>
			/// Filled by <see cref="Prepare"/>: the junction after the one ahead where the stretch between them is shorter than a car past
			/// the first's box (-1: none), with its arms and how far to its box: a car there is as good as at the second already (Kingsbridge's
			/// nodes 132 and 8, 19 apart).
			/// </summary>
			public int Then, ThenIn, ThenOut;
			public double ThenToBox;
		}

		/// <summary>Something in the way with no path (the player's car, a wreck, a car knocked off its road): where it is and its velocity.</summary>
		internal struct Obstacle
		{
			public double X, Z, Vx, Vz;
		}

		/// <summary>
		/// Whether drawn car <paramref name="m"/>, standing (as the day had it, when it is late: <see cref="TrafficLate.SkipStand"/>), has
		/// nothing here and now to stand for: no car or obstacle in front of it on its lane within <see cref="StandClear"/>, no red or amber
		/// light at the junction ahead, and at a junction it is close to, nobody else in its box or about to come into it.
		/// </summary>
		internal static bool NothingToStandFor(TrafficDay day, in Mover m, List<Mover> movers, List<Obstacle> obstacles, double w)
		{
			if (m.Physical || m.Pulling || m.V >= 0.5 || m.Back >= 0)
			{
				return false;
			}
			bool atJunction = m.Node >= 0 && m.ToBox < StandClear;
			if (atJunction && TrafficSignals.Has(m.Node) && day.Lights.State(m.Node, m.InArm, m.OutArm, w) != TrafficSignals.Green)
			{
				return false;
			}
			foreach (Mover o in movers)
			{
				if (o.Car == m.Car)
				{
					continue;
				}
				double dx = o.X - m.X, dz = o.Z - m.Z;
				double along = dx * m.Hx + dz * m.Hz, side = Math.Abs(dx * m.Hz - dz * m.Hx);
				if (along > -2.0 && along < StandClear && side < 4.5)
				{
					return false;
				}
				if (atJunction && (o.Back == m.Node || (o.Node == m.Node && o.ToBox < 20.0)))
				{
					return false;
				}
			}
			foreach (Obstacle o in obstacles)
			{
				double dx = o.X - m.X, dz = o.Z - m.Z;
				double along = dx * m.Hx + dz * m.Hz, side = Math.Abs(dx * m.Hz - dz * m.Hx);
				if (along > -2.0 && along < StandClear + (atJunction ? m.ToBox + 2.0 * Traffic.Nodes[m.Node].Box : 0.0) && side < (atJunction ? StandClear : 4.5))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>How far ahead a standing drawn car must have its lane clear to have nothing to stand for.</summary>
		internal const double StandClear = 40.0;

		/// <summary>What a driver keeps from step to step.</summary>
		internal struct State
		{
			/// <summary>Seconds it has been held at the junction it is waiting at, and which node.</summary>
			public double Waited;
			public int WaitNode;
			/// <summary>What holds it at the junction it is at (for the overlay and trafficview): 0 nothing, 1 its light, 2 no room past the
			/// box, 3 a car crossing in the box, 4 a car that can't stop any more, 5 giving way, 6 a car there first.</summary>
			public int Held;
			/// <summary>Why it is going slower than its profile (for the overlay): 0 free, 1 a car in front, 2 a junction, 3 the trip's end, 4 early.</summary>
			public int Why;
			/// <summary>For trafficview's notes: the car in front along its path, and the one standing past the junction's box (-1: none).</summary>
			public int Front, Blocker;
		}

		// ---- the line ------------------------------------------------------------------------------------------------------------

		/// <summary>The line a car on leg <paramref name="leg"/> steers along at path distance <paramref name="p"/>: its lane, curved through junctions.</summary>
		internal static void Line(TrafficDay.Leg leg, double p, out double x, out double z, out double hx, out double hz)
		{
			TrafficDay.Place at = default;
			p = Math.Max(0.0, Math.Min(leg.Length, p));
			TrafficDay.OnPath(leg, p, ref at);
			x = at.X;
			z = at.Z;
			hx = at.Hx;
			hz = at.Hz;
			TrafficParking.Corner(leg, p, ref x, ref z, ref hx, ref hz);
		}

		/// <summary>
		/// Where along leg <paramref name="leg"/> world point (x, z) is: the path distance of the nearest point of its line, searched from a
		/// little behind <paramref name="from"/> (the last answer) to well ahead, so a car is never found on a part of its path it has passed
		/// or not reached (a path can come back near itself). <paramref name="side"/> is how far off the line it is.
		/// </summary>
		internal static double Project(TrafficDay.Leg leg, double from, double x, double z, out double side, double back = 15.0, double ahead = 60.0)
		{
			double lo = Math.Max(0.0, from - back), hi = Math.Min(leg.Length, from + ahead);
			double best = lo, bestD = double.MaxValue;
			double stride = Math.Max(0.5, Math.Min(2.0, (hi - lo) / 16.0));
			for (double p = lo; p <= hi + 1e-9; p += stride)
			{
				Line(leg, p, out double lx, out double lz, out double _, out double _);
				double d = (lx - x) * (lx - x) + (lz - z) * (lz - z);
				if (d < bestD)
				{
					bestD = d;
					best = p;
				}
			}
			// Refine to the line between the samples either side.
			double step = stride * 0.5;
			for (int i = 0; i < 5; i++)
			{
				for (int sign = -1; sign <= 1; sign += 2)
				{
					double p = best + sign * step;
					if (p < 0.0 || p > leg.Length)
					{
						continue;
					}
					Line(leg, p, out double lx, out double lz, out double _, out double _);
					double d = (lx - x) * (lx - x) + (lz - z) * (lz - z);
					if (d < bestD)
					{
						bestD = d;
						best = p;
					}
				}
				step *= 0.5;
			}
			side = Math.Sqrt(bestD);
			return best;
		}

		/// <summary>Whether a car at (x, z) facing (hx, hz) is on its leg's line at <paramref name="p"/>: within 8 units of it, heading within 45 degrees.</summary>
		internal static bool OnLane(TrafficDay.Leg leg, double p, double x, double z, double hx, double hz)
		{
			Line(leg, p, out double lx, out double lz, out double lhx, out double lhz);
			double d = Math.Sqrt((lx - x) * (lx - x) + (lz - z) * (lz - z));
			double len = Math.Sqrt(hx * hx + hz * hz);
			return d < 8.0 && len > 1e-6 && (hx * lhx + hz * lhz) / len > 0.7071;
		}

		/// <summary>
		/// Whether two cars' bodies overlap: each a box <see cref="CarLength"/> by 5.2 at (x, z) facing (hx, hz) (unit), tested along both
		/// cars' axes (two rectangles overlap unless one of their four axes separates them). <paramref name="margin"/> grows each box.
		/// </summary>
		internal static bool Overlap(double ax, double az, double ahx, double ahz, double bx, double bz, double bhx, double bhz, double margin = 0.0)
		{
			double dx = bx - ax, dz = bz - az;
			if (dx * dx + dz * dz > 20.0 * 20.0)
			{
				return false;
			}
			double l = CarLength * 0.5 + margin, w = 2.6 + margin;
			double cos = Math.Abs(ahx * bhx + ahz * bhz), sin = Math.Abs(ahx * bhz - ahz * bhx);
			// Each box's reach along an axis of the other: its half length times the cosine, its half width times the sine (and so on).
			double alongA = Math.Abs(dx * ahx + dz * ahz), sideA = Math.Abs(dx * ahz - dz * ahx);
			double alongB = Math.Abs(dx * bhx + dz * bhz), sideB = Math.Abs(dx * bhz - dz * bhx);
			return alongA < l + l * cos + w * sin && sideA < w + l * sin + w * cos && alongB < l + l * cos + w * sin && sideB < w + l * sin + w * cos;
		}

		/// <summary>
		/// Contact physics: whether the player's car, a box <paramref name="pl"/> long by <paramref name="pw"/> wide (half sizes) at (px, pz)
		/// facing (fx, fz) (unit), moving at (vx, vz), would meet another box (half sizes <paramref name="ol"/> by <paramref name="ow"/> at
		/// (ox, oz) facing (hx, hz), unit) within <paramref name="ahead"/> seconds and <paramref name="extra"/> units more. The player's box
		/// swept along its velocity is bounded by one box in its own frame (grown by the sweep's reach along each of its axes), and the two
		/// boxes are tested on their four axes (two rectangles meet unless one of the axes separates them). <paramref name="margin"/> grows
		/// both. Cheap: the traffic calls it each frame for the drawn cars near the player.
		/// </summary>
		internal static bool Sweeps(double px, double pz, double fx, double fz, double pl, double pw, double vx, double vz, double ahead, double extra,
			double ox, double oz, double hx, double hz, double ol, double ow, double margin = 0.0)
		{
			double speed = Math.Sqrt(vx * vx + vz * vz);
			double sx = vx * ahead, sz = vz * ahead;
			if (speed > 0.1)
			{
				sx += vx / speed * extra;
				sz += vz / speed * extra;
			}
			// The sweep's middle, and the player's box grown by half the sweep along its length and its width.
			double cx = px + sx * 0.5, cz = pz + sz * 0.5;
			double rx = fz, rz = -fx;
			double l = pl + Math.Abs(sx * fx + sz * fz) * 0.5 + margin, w = pw + Math.Abs(sx * rx + sz * rz) * 0.5 + margin;
			double hrx = hz, hrz = -hx;
			double l2 = ol + margin, w2 = ow + margin;
			double dx = ox - cx, dz = oz - cz;
			if (dx * dx + dz * dz > (l + w + l2 + w2) * (l + w + l2 + w2))
			{
				return false;
			}
			// Each box's reach along an axis: its half length times the cosine to it plus its half width times the sine.
			double ff = Math.Abs(fx * hx + fz * hz), fr = Math.Abs(fx * hrx + fz * hrz);
			double rf = Math.Abs(rx * hx + rz * hz), rr = Math.Abs(rx * hrx + rz * hrz);
			return Math.Abs(dx * fx + dz * fz) <= l + l2 * ff + w2 * fr
				&& Math.Abs(dx * rx + dz * rz) <= w + l2 * rf + w2 * rr
				&& Math.Abs(dx * hx + dz * hz) <= l2 + l * ff + w * rf
				&& Math.Abs(dx * hrx + dz * hrz) <= w2 + l * fr + w * rr;
		}

		/// <summary>
		/// Whether a physics car at <paramref name="p"/> on leg <paramref name="leg"/> is in the dark of a tunnel it is driving into: its
		/// middle within <see cref="DarkBack"/> of the glue plane (<see cref="Wrap.Depth"/> in) or past it. There it goes back on its
		/// timetable, out of the player's sight; a physics car drives right in (the day takes it through the wrap), not handed back
		/// before the mouth, where it would be seen to brake or vanish (owner, 2026-11-24).
		/// </summary>
		internal static bool InDark(TrafficDay.Leg leg, double p)
		{
			// The seg it is on, the one before (just past the plane) or the next (a few units short of a short tunnel stretch).
			TrafficDay.Seg[] path = leg.Path;
			int k = SegAt(path, p);
			for (int j = Math.Max(0, k - 1); j <= Math.Min(path.Length - 1, k + 1); j++)
			{
				TrafficDay.Seg s = path[j];
				if (s.Edge < 0 && !s.Inbound && p >= s.P0 + s.Len - DarkBack && p <= s.P0 + s.Len + DarkBack)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>How far short of the glue plane a car's middle counts as in the dark (its tail is then well inside the mouth).</summary>
		internal const double DarkBack = 3.0;

		/// <summary>
		/// The point a physics car steers for, <paramref name="ahead"/> on from <paramref name="p"/> along its line. Past the glue plane of a
		/// tunnel it is driving into, the line goes on at the far tunnel (the wrap), so the point is kept straight on down this tunnel.
		/// </summary>
		internal static void Toward(TrafficDay.Leg leg, double p, double ahead, out double x, out double z)
		{
			double q = p + ahead;
			TrafficDay.Seg[] path = leg.Path;
			for (int j = Math.Max(0, SegAt(path, p) - 1); j < path.Length && path[j].P0 < q; j++)
			{
				TrafficDay.Seg s = path[j];
				double end = s.P0 + s.Len;
				if (s.Edge < 0 && !s.Inbound && end >= p - DarkBack && end < q)
				{
					Line(leg, end, out x, out z, out double _, out double _);
					x += s.Dx * (q - end);
					z += s.Dz * (q - end);
					return;
				}
			}
			Line(leg, q, out x, out z, out double _, out double _);
		}

		/// <summary>
		/// Whether a tunnel is on leg <paramref name="leg"/>'s path within <see cref="TunnelKeep"/> either side of <paramref name="p"/>. Only
		/// the stretches within reach are looked at, found by binary search: a circling car's leg is the whole day (about 250 stretches),
		/// and walking it from its start on every call cost frames once 100 of them were on the trunk (0.32.1).
		/// </summary>
		internal static bool NearTunnel(TrafficDay.Leg leg, double p)
		{
			TrafficDay.Seg[] path = leg.Path;
			// The stretch before the one at p - TunnelKeep too: it may end exactly there.
			for (int j = Math.Max(0, SegAt(path, p - TunnelKeep) - 1); j < path.Length && path[j].P0 <= p + TunnelKeep; j++)
			{
				TrafficDay.Seg s = path[j];
				if (s.Edge < 0 && s.P0 + s.Len >= p - TunnelKeep)
				{
					return true;
				}
			}
			return false;
		}

		// ---- the timetable -------------------------------------------------------------------------------------------------------

		/// <summary>Sim time <paramref name="w"/> on leg <paramref name="leg"/>'s own clock (a leg that ran past the day's end runs on w + a day).</summary>
		internal static double LegClock(TrafficDay.Leg leg, double w)
		{
			return leg.Start - w > Traffic.DaySeconds * 0.5 ? w + Traffic.DaySeconds : w;
		}

		/// <summary>When (on the leg's clock) the day had leg <paramref name="leg"/>'s car at path distance <paramref name="p"/>: the first time it got there.</summary>
		internal static double TimeAt(TrafficDay.Leg leg, double p)
		{
			float[] rec = leg.Rec;
			int n = rec.Length;
			if (n == 0 || p <= rec[0])
			{
				return leg.Start;
			}
			if (p >= rec[n - 1])
			{
				return leg.End;
			}
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) / 2;
				if (rec[mid] >= p)
				{
					hi = mid;
				}
				else
				{
					lo = mid;
				}
			}
			// rec[lo] < p <= rec[hi]. Every record is RecordStep after the last but the final one, which is at End.
			double t0 = leg.Start + lo * TrafficDay.RecordStep;
			double t1 = hi == n - 1 ? leg.End : leg.Start + hi * TrafficDay.RecordStep;
			double span = rec[hi] - rec[lo];
			return span > 1e-9 ? t0 + (t1 - t0) * (p - rec[lo]) / span : t1;
		}

		/// <summary>How late (seconds) a car at path distance <paramref name="p"/> of its leg is at sim time <paramref name="w"/>: negative if it is early.</summary>
		internal static double Lateness(TrafficDay.Leg leg, double p, double w)
		{
			return LegClock(leg, w) - TimeAt(leg, p);
		}

		/// <summary>Where the day has leg <paramref name="leg"/>'s car at sim time <paramref name="w"/> (path distance) and its speed there.</summary>
		internal static double OnTime(TrafficDay.Leg leg, double w, out double speed)
		{
			double t = Math.Max(leg.Start, Math.Min(leg.End, LegClock(leg, w)));
			double p = TrafficDay.DistanceAt(leg, t);
			speed = (TrafficDay.DistanceAt(leg, Math.Min(leg.End, t + 0.25)) - p) / 0.25;
			return p;
		}

		// ---- the speed -----------------------------------------------------------------------------------------------------------

		/// <summary>Fills in where mover <paramref name="m"/> is on the graph and the junction it is at or coming to.</summary>
		internal static void Prepare(TrafficDay day, ref Mover m)
		{
			m.Edge = -1;
			m.Node = -1;
			m.Back = -1;
			m.Then = -1;
			if (m.Leg < 0)
			{
				return;
			}
			TrafficDay.Seg[] path = day.Legs[m.Leg].Path;
			int k = SegAt(path, m.P);
			TrafficDay.Seg s = path[k];
			if (s.Edge >= 0)
			{
				m.Edge = s.Edge;
				m.Dir = s.Dir;
				m.S = s.S0 + s.Dir * Math.Max(0.0, Math.Min(s.Len, m.P - s.P0));
			}
			m.Lane = TrafficDay.TwoLane(s) ? TrafficDay.LaneAt(day.Legs[m.Leg], m.P) : 0.0;
			// In the box of the junction just passed; and the next one, wherever it is.
			if (k > 0)
			{
				int back = TrafficDay.NodeAfter(path, k - 1);
				if (back >= 0 && path[k - 1].Edge != s.Edge && m.P - s.P0 < Traffic.Nodes[back].Box)
				{
					m.Back = back;
					m.BackIn = TrafficDay.LinkIndex(back, path[k - 1].Edge);
					m.BackOut = TrafficDay.LinkIndex(back, s.Edge);
				}
			}
			for (int j = k; j + 1 < path.Length; j++)
			{
				int node = TrafficDay.NodeAfter(path, j);
				if (node >= 0 && path[j].Edge != path[j + 1].Edge)
				{
					SetNode(ref m, path, j, node);
					// The pair's second junction (as the day lets a car through both at once: TrafficDay.PairedNext).
					int j2 = j + 1;
					if (j2 + 1 < path.Length && path[j2].Edge >= 0 && path[j2 + 1].Edge >= 0 && path[j2].Edge != path[j2 + 1].Edge)
					{
						int then = TrafficDay.NodeAfter(path, j2);
						if (then >= 0 && then != node && path[j2].Len - Traffic.Nodes[then].Box < Traffic.Nodes[node].Box + CarLength + 4.0)
						{
							m.Then = then;
							m.ThenIn = TrafficDay.LinkIndex(then, path[j2].Edge);
							m.ThenOut = TrafficDay.LinkIndex(then, path[j2 + 1].Edge);
							m.ThenToBox = path[j2].P0 + path[j2].Len - Traffic.Nodes[then].Box - m.P;
						}
					}
					return;
				}
				if (path[j].P0 + path[j].Len > m.P + LookAhead)
				{
					return;
				}
			}
		}

		private static void SetNode(ref Mover m, TrafficDay.Seg[] path, int j, int node)
		{
			m.Node = node;
			m.NodeP = path[j].P0 + path[j].Len;
			m.InArm = TrafficDay.LinkIndex(node, path[j].Edge);
			m.OutArm = TrafficDay.LinkIndex(node, path[j + 1].Edge);
			m.ToBox = m.NodeP - Traffic.Nodes[node].Box - m.P;
		}

		/// <summary>The last stretch of <paramref name="path"/> starting at or before <paramref name="p"/> (0 if none): a binary search.</summary>
		internal static int SegAt(TrafficDay.Seg[] path, double p)
		{
			int lo = 0, hi = path.Length - 1;
			while (lo < hi)
			{
				int mid = (lo + hi + 1) / 2;
				if (path[mid].P0 <= p)
				{
					lo = mid;
				}
				else
				{
					hi = mid - 1;
				}
			}
			return lo;
		}

		/// <summary>
		/// A car pulling out of a space across the road turns over the oncoming lane: whether a car <paramref name="along"/> ahead of it
		/// (along its way out; behind it, negative) and <paramref name="side"/> to the side, in either lane, is in its way or coming into it.
		/// </summary>
		internal static bool AcrossItsWay(double along, double side)
		{
			return side < 16.0 && along > -30.0 && along < 70.0;
		}

		/// <summary>Lowers <paramref name="target"/> to a safe speed behind something at (x, z) going <paramref name="lead"/> if it is ahead of <paramref name="me"/> in its lane.</summary>
		private static void InTheWay(in Mover me, double x, double z, double lead, ref double target, ref int why, double sideMore = 0.0)
		{
			double dx = x - me.X, dz = z - me.Z;
			double along = dx * me.Hx + dz * me.Hz, side = Math.Abs(dx * me.Hz - dz * me.Hx);
			if (along <= 0.0 || along > 80.0 || side > 6.0 + sideMore)
			{
				return;
			}
			double safe = SafeSpeed(along - CarLength - TrafficDay.MinGap, lead, FollowGap);
			if (safe < target)
			{
				target = safe;
				why = 1;
			}
		}

		/// <summary>The fastest a car can go and still stop <paramref name="gap"/> units on (behind something going <paramref name="lead"/>), keeping a time gap.</summary>
		internal static double SafeSpeed(double gap, double lead, double timeGap)
		{
			double b = TrafficDay.Brake, bt = b * timeGap;
			return -bt + Math.Sqrt(bt * bt + lead * lead + 2.0 * b * Math.Max(0.0, gap));
		}

		/// <summary>
		/// The speed physics car <paramref name="me"/> (prepared) aims for now: the least of its leg's profile here and a little ahead; a
		/// safe speed behind the car in front on its path (physics or drawn) and behind anything in its way with no path (the player);
		/// stopping at a junction's box while a car whose way crosses its own is in the box, or is coming and goes first (the sooner there,
		/// the car on the right within a second; a drawn car never gives way, so it always goes first), or while its way out has no room past
		/// the box; stopping at its trip's end; and easing off when it gets well ahead of its timetable (<paramref name="onTimeP"/>,
		/// <paramref name="onTimeV"/>: where and how fast the day has it). <paramref name="dt"/> moves the junction wait on.
		/// </summary>
		internal static double Target(TrafficDay day, in Mover me, float[] profile, List<Mover> others, List<Obstacle> obstacles, double onTimeP, double onTimeV, double now, double dt, ref State st)
		{
			TrafficDay.Leg leg = day.Legs[me.Leg];
			double v = Math.Max(0.0, me.V);
			double target = Math.Min(TrafficDay.DesiredAt(profile, me.P), TrafficDay.DesiredAt(profile, me.P + v * 0.5));
			int why = 0;
			double free = target;

			// The car in front along the path; and the nearest stopped one past the junction coming up (its way out must have room).
			TrafficDay.Seg[] path = leg.Path;
			int k0 = SegAt(path, me.P);
			double nearestP = double.MaxValue, nearestV = 0.0, nearestX = 0.0, nearestZ = 0.0;
			bool blockedOut = false;
			st.Front = -1;
			st.Blocker = -1;
			double slowP = double.MaxValue;
			int slowCar = -1;
			if (pastBox == null)
			{
				pastBox = new List<double>();
			}
			pastBox.Clear();
			// Room past the box: stopped behind the car there, its back is out of the box (P is a car's middle: a car's length and a half,
			// and the gap, past the box's edge; it was a car's length and 4, and a car stopped behind a queue stood with its back in the box).
			double boxOut = me.Node >= 0 ? me.NodeP + Traffic.Nodes[me.Node].Box + 1.5 * CarLength + TrafficDay.MinGap : double.MinValue;
			foreach (Mover o in others)
			{
				// A drawn car pulling out of its space or into one is not on the lane yet (or any more): it is in the way only where it is
				// drawn (below). Counted on the lane, one held back in its space just past a junction kept the cars behind it waiting
				// for it, and it for them.
				if (o.Car == me.Car || o.Edge < 0 || o.Pulling)
				{
					continue;
				}
				for (int j = k0; j < path.Length && path[j].P0 <= me.P + LookAhead; j++)
				{
					TrafficDay.Seg s = path[j];
					if (s.Edge != o.Edge || s.Dir != o.Dir)
					{
						continue;
					}
					if (TrafficDay.TwoLane(s) && Math.Abs(o.Lane - (j == k0 ? me.Lane : TrafficDay.LaneAt(leg, s.P0 + (o.S - s.S0) * s.Dir))) > 0.6)
					{
						// The trunk's other lane: beside it or passing, not in front.
						break;
					}
					double into = (o.S - s.S0) * s.Dir;
					if (into < -0.01 || into > s.Len + 0.01)
					{
						continue;
					}
					double po = s.P0 + into;
					if (po <= me.P + 0.5)
					{
						continue;
					}
					if (po < nearestP)
					{
						st.Front = o.Car;
						nearestP = po;
						nearestV = Math.Max(0.0, o.V);
						nearestX = o.X;
						nearestZ = o.Z;
					}
					if (me.Node >= 0 && po < boxOut + QueueLook)
					{
						// (The cars ahead of it short of the node go out the same way: they need room there too.)
						if (po > me.NodeP && o.V < 4.0 && po < slowP)
						{
							slowP = po;
							slowCar = o.Car;
						}
						if (pastBox.Count < 32)
						{
							pastBox.Add(po);
						}
					}
					break;
				}
			}
			if (slowP < double.MaxValue)
			{
				// Past the box, the queue up to the first car standing or crawling there: room for this car behind the cars already in
				// it, its back out of the box ("do not enter an intersection if there are vehicles blocking your way", PA Driver's Manual
				// p. 46). Following a moving queue in and stopping when it stops left cars standing in the boxes of Kingsbridge's close
				// junctions, each holding up the next.
				int ahead = 0;
				foreach (double q in pastBox)
				{
					ahead += q < slowP ? 1 : 0;
				}
				if (slowP < boxOut + ahead * (CarLength + TrafficDay.MinGap))
				{
					blockedOut = true;
					st.Blocker = slowCar;
				}
			}
			if (!blockedOut && me.Node >= 0 && me.ToBox > 0.0 && me.ToBox < 60.0 && obstacles.Count > 0)
			{
				// Something with no path (the player's car, a wreck) standing on its way through the box or just past it: no room either.
				// Seen only straight ahead, a car turning into the junction found the player's car round the corner once in the box, and
				// stood there across the other cars' ways (seed 13, the player's car stopped across a lane).
				double near = 0.5 * CarLength + 3.0;
				for (double p = me.NodeP - Traffic.Nodes[me.Node].Box; p <= boxOut && !blockedOut; p += 4.0)
				{
					Line(leg, p, out double lx, out double lz, out double _, out double _);
					foreach (Obstacle ob in obstacles)
					{
						double dx = ob.X - lx, dz = ob.Z - lz;
						if (ob.Vx * ob.Vx + ob.Vz * ob.Vz < 16.0 && dx * dx + dz * dz < near * near)
						{
							blockedOut = true;
							break;
						}
					}
				}
			}
			if (nearestP < double.MaxValue)
			{
				// Along the path, and in a straight line: round a sharp turn the car in front is nearer than its distance along the way.
				double gap = nearestP - me.P - CarLength - TrafficDay.MinGap;
				double dx = nearestX - me.X, dz = nearestZ - me.Z;
				gap = Math.Min(gap, Math.Sqrt(dx * dx + dz * dz) - CarLength - TrafficDay.MinGap);
				double safe = SafeSpeed(gap, nearestV, FollowGap);
				if (safe < target)
				{
					target = safe;
					why = 1;
				}
			}
			// A car that went into the junction just ahead from the same arm, whichever way out: until it is out of the box their ways
			// share its entry (round a sharp turn they part late), so it is kept a car's distance and the gap in a straight line.
			if (me.Node >= 0 && me.ToBox < 40.0)
			{
				foreach (Mover o in others)
				{
					if (o.Car == me.Car || o.Back != me.Node || o.BackIn != me.InArm)
					{
						continue;
					}
					double dx = o.X - me.X, dz = o.Z - me.Z;
					double safe = SafeSpeed(Math.Sqrt(dx * dx + dz * dz) - CarLength - TrafficDay.MinGap, Math.Max(0.0, o.V), FollowGap);
					if (safe < target)
					{
						target = safe;
						why = 1;
					}
				}
			}
			// Anything with no path in the way, and drawn cars pulling in or out where they are drawn: ahead along the car's heading,
			// within its lane's width.
			foreach (Obstacle ob in obstacles)
			{
				InTheWay(me, ob.X, ob.Z, Math.Max(0.0, ob.Vx * me.Hx + ob.Vz * me.Hz), ref target, ref why);
			}
			foreach (Mover o in others)
			{
				if (o.Pulling && o.Car != me.Car)
				{
					// Turned across the lane, its nose or tail reaches further to the side than its middle.
					double before = target;
					InTheWay(me, o.X, o.Z, 0.0, ref target, ref why, 0.5 * CarLength * Math.Abs(me.Hx * o.Hz - me.Hz * o.Hx));
					if (target < before && target < 0.5)
					{
						// (For the notes: what holds it is a car pulling in or out.)
						st.Front = o.Car;
					}
				}
			}

			// The junction coming up: stop at its box while someone crossing goes first, or while there is no room past it.
			bool hold = false;
			if (me.Node >= 0 && me.ToBox > 0.0)
			{
				double reach = v * v / (2.0 * TrafficDay.Brake) + 1.5 * v + 25.0;
				if (me.ToBox < reach)
				{
					hold = blockedOut;
					st.Held = blockedOut ? 2 : 0;
					// Its light, as the day ran it: red, or amber it can stop for (as the day's cars).
					// A side road's light changes for the day's cars only: one held at red past LightGiveUp with none of them there to
					// call the green goes as at a give way (the light out of it), as a driver does at a light that never changes.
					bool unlit = TrafficSignals.Has(me.Node) && st.WaitNode == me.Node && st.Waited > LightGiveUp && TrafficSignals.PhaseOf(me.Node, me.InArm, me.OutArm) != TrafficSignals.MainPhase;
					if (!hold && TrafficSignals.Has(me.Node) && !unlit && !TrafficSignals.MayGo(day.Lights.State(me.Node, me.InArm, me.OutArm, now), v, me.ToBox - CarLength * 0.5))
					{
						hold = true;
						st.Held = 1;
					}
					double etaMe = me.ToBox / Math.Max(v, 4.0);
					bool patient = st.WaitNode != me.Node || st.Waited < HoldBreak;
					// Past braking to a stop short of the box: committed, it goes whoever else is coming.
					bool meCommitted = me.ToBox < v * v / (2.0 * TrafficDay.Brake) + 2.0;
					foreach (Mover o0 in others)
					{
						if (hold)
						{
							break;
						}
						Mover o = o0;
						if (o.Node >= 0 && o.Node != me.Node && o.Then == me.Node && o.Back != me.Node)
						{
							// Coming to the junction just before this one, over a stretch too short to stop on: seen as coming to this one.
							o.Node = o.Then;
							o.InArm = o.ThenIn;
							o.OutArm = o.ThenOut;
							o.ToBox = o.ThenToBox;
						}
						if (o.Car == me.Car || o.Pulling)
						{
							continue;
						}
						if (o.Back == me.Node && o.Node != me.Node)
						{
							// In the box's far half, on its way out.
							if (o.BackIn != me.InArm && TrafficDay.Crosses(me.Node, me.InArm, me.OutArm, o.BackIn, o.BackOut))
							{
								hold = true;
								st.Held = 3;
								st.Blocker = o.Car;
								break;
							}
							continue;
						}
						if (o.Node != me.Node || o.InArm == me.InArm || !TrafficDay.Crosses(me.Node, me.InArm, me.OutArm, o.InArm, o.OutArm))
						{
							continue;
						}
						if (!unlit && TrafficSignals.Apart(me.Node, me.InArm, me.OutArm, o.InArm, o.OutArm) && o.ToBox > CarLength * 0.5
							&& o.ToBox - CarLength * 0.5 >= o.V * o.V / (2.0 * TrafficSignals.AmberBrake))
						{
							// Coming on the other green: its light holds it (only one already in the box, above, keeps this one back). Not
							// one too close to stop for its amber (it goes through, as the day's cars do): seen as any car coming. A
							// main-road car off a short stretch (Kingsbridge's node 8, 19 from the junction before) met a side car let go
							// on its green in the box.
							continue;
						}
						if (o.ToBox < -0.5 || (o.ToBox < CarLength * 0.5 && o.V > 2.0))
						{
							// In the box (or its front in it, going in). A drawn car standing there is the day's, waiting for a car it doesn't know is physical now: given up on in time.
							if (o.Physical || o.V > 0.5 || st.WaitNode != me.Node || st.Waited < 3.0 * HoldBreak)
							{
								hold = true;
								st.Held = 3;
								st.Blocker = o.Car;
								break;
							}
							continue;
						}
						double ov = Math.Max(0.0, o.V);
						bool oCommitted = o.ToBox < ov * ov / (2.0 * TrafficDay.Brake) + 2.0 && ov > 2.0;
						if (oCommitted && !meCommitted)
						{
							// It can't stop for this car any more (a car that set off late must not cut across a fast one): it goes first.
							hold = true;
							st.Held = 4;
							st.Blocker = o.Car;
							break;
						}
						if (meCommitted)
						{
							continue;
						}
						// The right of way (the main line, as the day has it): a car giving way waits for a gap, whatever its wait, as long as
						// one it gives way to gets there before it would be through; one with the right of way never waits for a physics
						// car giving way to it (a drawn one, which follows its timetable, is still let go first when it is sooner).
						if (unlit ? TrafficDay.YieldsUnlit(me.Node, me.InArm, me.OutArm, o.InArm, o.OutArm) : TrafficDay.Yields(me.Node, me.InArm, me.OutArm, o.InArm, o.OutArm))
						{
							double box = Traffic.Nodes[me.Node].Box;
							double waited = st.WaitNode == me.Node ? st.Waited : 0.0;
							double margin = TrafficDay.GiveWayAfter(waited) + PhysicsGapMore * Math.Max(0.0, 1.0 - waited / TrafficDay.GiveWayPatience);
							// Its reckoning is rough (full pull-away, no easing for the turn, the other's speed as it is): a wider margin than the day's.
							double clearMe = TrafficDay.ClearTime(v, me.ToBox - CarLength * 0.5 + 2.0 * box + CarLength) + margin;
							if (ov < 0.5)
							{
								// Standing (at its line or behind): not coming, as in the day. Once it moves off, it is reckoned again.
								continue;
							}
							if (o.ToBox / Math.Max(ov, 4.0) < Math.Max(clearMe, TrafficDay.SideGap(me.Node, me.InArm, me.OutArm, waited)))
							{
								hold = true;
								st.Held = 5;
								st.Blocker = o.Car;
								break;
							}
							continue;
						}
						if (o.Physical && TrafficDay.Yields(me.Node, o.InArm, o.OutArm, me.InArm, me.OutArm))
						{
							continue;
						}
						if (!patient)
						{
							// Tired of waiting it goes whoever is still coming, but not across a car about to get there: from a standstill
							// it would still be in the box.
							if (ov > 2.0 && o.ToBox / ov < ImpatientLook)
							{
								hold = true;
								st.Held = 6;
								break;
							}
							continue;
						}
						double oReach = ov * ov / (2.0 * TrafficDay.Brake) + 1.5 * ov + 25.0;
						if (o.ToBox > oReach || (ov < 0.5 && o.ToBox > CarLength * 0.5 + 4.0))
						{
							// Not coming yet, or held up short of its line by something else (one waiting at its line, front at the box, counts).
							continue;
						}
						double etaO = o.ToBox / Math.Max(ov, 4.0);
						bool first;
						if (!o.Physical)
						{
							// A drawn car follows its timetable and gives way to nobody.
							first = etaO < etaMe + 1.0;
						}
						else if (Math.Abs(etaO - etaMe) > 1.0)
						{
							first = etaO < etaMe;
						}
						else
						{
							bool oRight = TrafficDay.RightOf(me.Node, o.InArm, me.InArm), meRight = TrafficDay.RightOf(me.Node, me.InArm, o.InArm);
							first = oRight != meRight ? oRight : o.Car < me.Car;
						}
						hold = first;
						st.Held = first ? 6 : 0;
						st.Blocker = first ? o.Car : st.Blocker;
					}
				}
			}
			if (hold)
			{
				// Its front at the box's edge (ToBox is to its middle).
				double stop = SafeSpeed(me.ToBox - CarLength * 0.5 - 1.0, 0.0, StopGap);
				if (stop < target)
				{
					target = stop;
					why = 2;
				}
				if (st.WaitNode != me.Node)
				{
					st.WaitNode = me.Node;
					st.Waited = 0.0;
				}
				if (v < 1.0)
				{
					st.Waited += dt;
				}
			}
			else if (me.Node != st.WaitNode)
			{
				st.Waited = 0.0;
			}

			// The trip's end (it goes back on its timetable before it gets there, which pulls it in; this is in case it can't yet).
			if (!leg.Vanishes)
			{
				double stop = SafeSpeed(leg.Length - me.P - 2.0, 0.0, StopGap);
				if (stop < target)
				{
					target = stop;
					why = 3;
				}
			}

			// Never far ahead of its timetable. Easing off, not stopping (the day's car may be waiting at a junction this one has passed),
			// and never in a box or just before one. Not on the last stretch: there it drives on to its trip's end and goes back on the
			// timetable early (it used to wait there to be on time, on the lane, holding up everyone behind it).
			bool last = me.P > leg.Length - EndZone - 30.0;
			double early = EarlyMost;
			if (!last && me.P > onTimeP + early - 30.0 && me.Back < 0 && (me.Node < 0 || me.ToBox > 15.0))
			{
				// Never below half its road's speed (4 at least): a car crawling on the trunk is run into.
				double cap = Math.Max(Math.Max(4.0, 0.5 * free), Math.Max(0.0, onTimeV) + Math.Max(0.0, onTimeP + early - me.P) * 0.6);
				if (cap < target)
				{
					target = cap;
					why = 4;
				}
			}
			st.Why = target < free - 0.5 ? why : 0;
			return Math.Max(0.0, target);
		}
	}
}
