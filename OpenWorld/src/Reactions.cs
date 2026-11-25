using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>A reaction as the herds play it: each deer's start, its run (points and distances along them), and when it goes and gets there.</summary>
	internal sealed class Reacted
	{
		public Reaction R;
		/// <summary>The spot the group flees to, and the slot its calm day goes on from (the spot is the chain's at the slot before).</summary>
		public int Dst = -1;
		public long Slot;
		/// <summary>From this race time the group is calm again (a flee: the start of <see cref="Slot"/>; an alert: some seconds on).</summary>
		public double End;
		/// <summary>When the last deer is there.</summary>
		public double Done;
		public double[] StartX, StartZ, Go, Arrive;
		public double[][] PX, PZ, PS;
	}

	/// <summary>
	/// The deer's reactions (step 3 of the plan in docs/PLAN.md), pure as the calm deer are: a reaction (<see cref="Reaction"/>, settled
	/// by the room's host, <see cref="DeerRoom"/>) overrides what the calm day says a group does from its race time on, and every machine
	/// plays it the same from the same start, so nothing but the reaction itself is sent.
	///
	/// An alert: the deer stop grazing and watch, heads up, facing what they heard; where they are does not change. A flee: each deer
	/// stands a moment (the nearest first, the others a fraction of a second to two seconds after, one after another; frozen and staring
	/// for a few seconds first if it froze), then gallops, tail up, to the spot the group flees to: away from the road to the cover or the
	/// far field on the side it is on, or over the road at the crossing (in front of the car) to the other side. It runs only where deer
	/// walk calmly (from where it is to the nearest of the group's spots on its side, if that way is clear, then along the tested legs
	/// between spots, the crossing at its place), so a fleeing deer, like a calm one, never goes into deep water, over a drop or onto
	/// another road. There it stands watching, tail up, then settles; at the next slot's start (half a minute at least after the last
	/// gets there) the calm day goes on from that spot.
	/// </summary>
	internal sealed partial class Herds
	{
		/// <summary>A gallop (units a second, about 40 km/h), the ground one bound covers, and how long a deer takes to get up to speed.</summary>
		internal const double GallopSpeed = 36.0, GallopStride = 14.0, Ramp = 0.6;

		/// <summary>How long an alert lasts with nothing after, and how long a fled deer stands watching where it got to.</summary>
		internal const double AlertSeconds = 15.0, WatchSeconds = 12.0;

		/// <summary>The time a fled group has where it got to before its calm day goes on (else it waits a slot more).</summary>
		private const double HoldLeast = 30.0;

		/// <summary>Which side of its crossing road a point is (+1 the left, -1 the right), and how far off the road's centre line.</summary>
		internal static double Offset(Group g, double x, double z)
		{
			return (x - g.Site.X) * g.Site.Nx + (z - g.Site.Z) * g.Site.Nz;
		}

		internal static int SideOf(Group g, double x, double z)
		{
			return Offset(g, x, z) >= 0.0 ? 1 : -1;
		}

		/// <summary>
		/// The side of the group's road a point is on (+1 its left at the crossing, -1 its right), by the road itself at the nearest point
		/// along it within a few hundred units of the crossing (a lane curves: a spot along it can be across the line through the
		/// crossing), the road's left matched to the crossing's as the spots were placed.
		/// </summary>
		internal static int DeerSide(Group g, double x, double z)
		{
			Network.Road road = Network.Roads[g.Site.Road];
			double s0 = road.S[g.Site.I];
			double best = double.MaxValue, side = 1.0;
			for (int i = 0; i + 1 < road.Count; i++)
			{
				if (road.S[i + 1] < s0 - 400.0 || road.S[i] > s0 + 400.0)
				{
					continue;
				}
				double ax = road.X[i], az = road.Z[i], tx = road.X[i + 1] - ax, tz = road.Z[i + 1] - az;
				double l2 = tx * tx + tz * tz;
				if (l2 < 1e-12)
				{
					continue;
				}
				double f = Math.Max(0.0, Math.Min(1.0, ((x - ax) * tx + (z - az) * tz) / l2));
				double dx = x - (ax + tx * f), dz = z - (az + tz * f);
				double d2 = dx * dx + dz * dz;
				if (d2 < best)
				{
					best = d2;
					// The segment's left (-tz, tx), turned to agree with the crossing's.
					double nx = -tz, nz = tx;
					if (nx * g.Site.Nx + nz * g.Site.Nz < 0.0)
					{
						nx = -nx;
						nz = -nz;
					}
					side = dx * nx + dz * nz;
				}
			}
			return side >= 0.0 ? 1 : -1;
		}

		/// <summary>
		/// The side a deer is on: that of the group's spot it stands at (within a few units of the spot's round), else
		/// <see cref="DeerSide"/>. The spots' sides are where they were placed beside the road; the road's own line past its end (a lane
		/// carrying on as the hill climb) clamps to that end and can put a spot there on the other side, and a deer grazing at it fled
		/// straight over the lane, 40 units from its crossing, to a spot it took for its own side.
		/// </summary>
		internal static int SideAt(Group g, double x, double z)
		{
			int best = -1;
			double bestD = (SpotRadius + 3.0) * (SpotRadius + 3.0);
			for (int i = 0; i < g.Spots.Length; i++)
			{
				double dx = g.Spots[i].X - x, dz = g.Spots[i].Z - z;
				double d2 = dx * dx + dz * dz;
				if (d2 < bestD)
				{
					bestD = d2;
					best = i;
				}
			}
			return best >= 0 ? g.Spots[best].Side : DeerSide(g, x, z);
		}

		/// <summary>Whether a point is on the group's road (its paving and a little more).</summary>
		internal static bool OnRoad(Group g, double x, double z)
		{
			return Math.Abs(Offset(g, x, z)) < g.Site.Half + 3.0;
		}

		/// <summary>
		/// Which way a group flees, by chance from its reaction's number: freezing more at night (headlights), darting over the road half
		/// the time, more when the deer nearest the car stands in it (as real deer do, and to be a hazard on the road), and never over it for a group with spots on one side only or
		/// startled by a shot.
		/// </summary>
		internal static FleeWay ChooseWay(Group g, int seq, bool onRoad, double hour, bool shot)
		{
			double u = Unit(g.Salt, 0xC0DEUL, (ulong)seq, 0UL);
			bool night = hour < 6.0 || hour > 20.0;
			double freeze = night ? 0.25 : 0.08;
			double across = shot || !g.Crosses ? 0.0 : onRoad ? 0.7 : 0.5;
			return u < freeze ? FleeWay.Freeze : u < freeze + across ? FleeWay.Across : FleeWay.Away;
		}

		/// <summary>
		/// How long after a flee over the road would start (<paramref name="r"/>, its way <see cref="FleeWay.Across"/>) the first deer
		/// would be on the paving at the crossing, from the run it would play (nothing is kept); -1 if none would be. The car's machine
		/// times a dart by it, so the deer reach the road a moment before the car does.
		/// </summary>
		internal double CrossAfter(Group g, Reaction r, ref Move mv, ref Move spare)
		{
			if (!g.Crosses)
			{
				return -1.0;
			}
			Reacted rr = Play(g, r, g.Reactions.Count, ref mv, ref spare);
			double reach = g.Site.Half + 3.0, best = double.MaxValue;
			for (int d = 0; d < g.Members.Length; d++)
			{
				double[] px = rr.PX[d], pz = rr.PZ[d], ps = rr.PS[d];
				if (px == null)
				{
					continue;
				}
				for (int i = 0; i + 1 < px.Length; i++)
				{
					// The nearest the leg comes to the crossing's centre; on the paving if within its half width.
					double ax = px[i] - g.Site.X, az = pz[i] - g.Site.Z;
					double bx = px[i + 1] - px[i], bz = pz[i + 1] - pz[i];
					double len2 = bx * bx + bz * bz;
					double u = len2 > 1e-9 ? Math.Max(0.0, Math.Min(1.0, -(ax * bx + az * bz) / len2)) : 0.0;
					double qx = ax + bx * u, qz = az + bz * u;
					if (qx * qx + qz * qz > reach * reach)
					{
						continue;
					}
					// Where along the leg it first comes within reach.
					double c = ax * ax + az * az - reach * reach, b = ax * bx + az * bz;
					double enter = c <= 0.0 || len2 <= 1e-9 ? 0.0 : Math.Max(0.0, (-b - Math.Sqrt(Math.Max(0.0, b * b - len2 * c))) / len2);
					double at = rr.Go[d] + TimeFor(ps[i] + (ps[i + 1] - ps[i]) * enter) - r.T0;
					best = Math.Min(best, at);
					break;
				}
			}
			return best == double.MaxValue ? -1.0 : best;
		}

		/// <summary>
		/// Gives group g its reactions (as the room shows them, in order): the plays already made for the same reactions are kept, the
		/// rest made again, and the calm day's spots after each flee put right.
		/// </summary>
		internal void SetReactions(Group g, IReadOnlyList<Reaction> list)
		{
			int keep = 0;
			while (keep < g.Reactions.Count && keep < list.Count && g.Reactions[keep].R.Same(list[keep]))
			{
				keep++;
			}
			lock (g.Gate)
			{
				g.Reactions.RemoveRange(keep, g.Reactions.Count - keep);
				Rebase(g);
			}
			var mv = new Move();
			var spare = new Move();
			for (int i = keep; i < list.Count; i++)
			{
				Reacted rr = Play(g, list[i], g.Reactions.Count, ref mv, ref spare);
				lock (g.Gate)
				{
					g.Reactions.Add(rr);
					Rebase(g);
				}
			}
		}

		/// <summary>The calm day's spots from the reactions: each flee leaves the group at its spot; the chain made again from there.</summary>
		private static void Rebase(Group g)
		{
			g.Overrides.Clear();
			foreach (Reacted rr in g.Reactions)
			{
				if (rr.R.Kind != ReactKind.Flee)
				{
					continue;
				}
				// A later flee's spot holds from its slot on: anything an earlier one left after that is gone.
				List<long> later = null;
				foreach (long k in g.Overrides.Keys)
				{
					if (k > rr.Slot - 1)
					{
						(later ??= new List<long>()).Add(k);
					}
				}
				if (later != null)
				{
					foreach (long k in later)
					{
						g.Overrides.Remove(k);
					}
				}
				g.Overrides[rr.Slot - 1] = rr.Dst;
			}
			g.Chain.Clear();
		}

		/// <summary>How a reaction plays, from where each deer is when it starts (with the reactions before it, <paramref name="before"/> of them).</summary>
		private Reacted Play(Group g, Reaction r, int before, ref Move mv, ref Move spare)
		{
			int n = g.Members.Length;
			Reacted rr = new Reacted
			{
				R = r,
				StartX = new double[n],
				StartZ = new double[n],
				Go = new double[n],
				Arrive = new double[n],
				PX = new double[n][],
				PZ = new double[n][],
				PS = new double[n][],
			};
			int leader = 0;
			double nearest = double.MaxValue;
			for (int d = 0; d < n; d++)
			{
				LiveUpTo(g, d, r.T0, before, ref mv, ref spare, out DeerState s);
				rr.StartX[d] = s.X;
				rr.StartZ[d] = s.Z;
				double dx = s.X - r.FromX, dz = s.Z - r.FromZ;
				double d2 = dx * dx + dz * dz;
				if (d2 < nearest)
				{
					nearest = d2;
					leader = d;
				}
			}
			if (r.Kind == ReactKind.Alert)
			{
				rr.End = r.T0 + AlertSeconds;
				rr.Done = r.T0;
				return rr;
			}

			// Where to: away on the leader's side, or over the road from it.
			int sl = SideAt(g, rr.StartX[leader], rr.StartZ[leader]);
			int ts = r.Way == FleeWay.Across && g.Crosses ? -sl : sl;
			rr.Dst = Refuge(g, ts);

			// Who goes when: nearest first, then one after another.
			int[] order = new int[n];
			double[] far = new double[n];
			for (int d = 0; d < n; d++)
			{
				order[d] = d;
				double dx = rr.StartX[d] - r.FromX, dz = rr.StartZ[d] - r.FromZ;
				far[d] = dx * dx + dz * dz;
			}
			Array.Sort(order, (a, b) => far[a] != far[b] ? far[a].CompareTo(far[b]) : a.CompareTo(b));
			double go = r.T0 + 0.25;
			if (r.Way == FleeWay.Freeze)
			{
				go += 1.5 + 2.5 * Unit(g.Salt, 0xF1EEUL, (ulong)r.Seq, 255UL);
			}
			var points = new List<(double x, double z)>();
			for (int k = 0; k < n; k++)
			{
				int d = order[k];
				if (k > 0)
				{
					double u = Unit(g.Salt, 0xF1EEUL, (ulong)r.Seq, (ulong)k);
					go += r.Way == FleeWay.Across ? 0.8 + 1.7 * u : 0.35 + 0.8 * u;
				}
				rr.Go[d] = go;
				Run(g, rr, d, points);
				double len = rr.PS[d][rr.PS[d].Length - 1];
				rr.Arrive[d] = go + TimeFor(len);
				rr.Done = Math.Max(rr.Done, rr.Arrive[d]);
			}
			// The calm day goes on from the refuge at a slot's start, half a minute at least after the last deer gets there, and never in
			// the slot the flee started in (its calm moves before the flee stay as they were).
			long ke = (long)Math.Floor(rr.Done / SlotSeconds);
			rr.Slot = rr.Done > (ke + 1) * SlotSeconds - HoldLeast ? ke + 2 : ke + 1;
			rr.Slot = Math.Max(rr.Slot, (long)Math.Floor(r.T0 / SlotSeconds) + 2);
			rr.End = rr.Slot * SlotSeconds;
			return rr;
		}

		/// <summary>The spot a group flees to on a side: its cover if that is there, else the spot there furthest from the road.</summary>
		private static int Refuge(Group g, int side)
		{
			if (g.Spots[0].Side == side)
			{
				return 0;
			}
			int best = -1;
			double bestOff = -1.0;
			for (int i = 0; i < g.Spots.Length; i++)
			{
				if (g.Spots[i].Side != side)
				{
					continue;
				}
				double off = Math.Abs(Offset(g, g.Spots[i].X, g.Spots[i].Z));
				if (off > bestOff)
				{
					bestOff = off;
					best = i;
				}
			}
			return best >= 0 ? best : 0;
		}

		/// <summary>
		/// Deer d's run: from where it is to the nearest of the group's spots on its side that it can reach in a straight line clear of
		/// roads and water (or is at, or is crossing to), then spot to spot along the tested legs to the refuge, to its place there.
		/// </summary>
		private void Run(Group g, Reacted rr, int d, List<(double x, double z)> points)
		{
			double px = rr.StartX[d], pz = rr.StartZ[d];
			int side = SideAt(g, px, pz);
			// On its crossing (the only paving a deer is ever on): off it to its side's nearest spot, the way it was going.
			double cx = px - g.Site.X, cz = pz - g.Site.Z;
			bool crossing = cx * cx + cz * cz < (g.Site.Half + 12.0) * (g.Site.Half + 12.0);
			int n = g.Spots.Length;
			int[] near = new int[n];
			double[] far = new double[n];
			for (int i = 0; i < n; i++)
			{
				near[i] = i;
				double dx = g.Spots[i].X - px, dz = g.Spots[i].Z - pz;
				far[i] = (g.Spots[i].Side == side ? 0.0 : 1e12) + dx * dx + dz * dz;
			}
			Array.Sort(near, (a, b) => far[a] != far[b] ? far[a].CompareTo(far[b]) : a.CompareTo(b));
			int from = near[0];
			for (int k = 0; k < n; k++)
			{
				int i = near[k];
				if (g.Spots[i].Side != side)
				{
					break;
				}
				if (far[i] < (SpotRadius + 1.0) * (SpotRadius + 1.0) || crossing || Walkable(px, pz, g.Spots[i].X, g.Spots[i].Z, 1.0))
				{
					from = i;
					break;
				}
			}
			List<int> route = Route(g, from, rr.Dst);
			points.Clear();
			points.Add((px, pz));
			// On the paving: first straight off it to its side, square to the road (making for a spot along the road in one line, a deer
			// caught a little along from the crossing ran on down the lane).
			double off = Offset(g, px, pz);
			if (crossing && Math.Abs(off) < g.Site.Half + 3.0)
			{
				double step = side * (g.Site.Half + 5.0) - off;
				points.Add((px + g.Site.Nx * step, pz + g.Site.Nz * step));
			}
			points.Add((g.Spots[from].X, g.Spots[from].Z));
			double lateral = 0.6 * (d % 3 - 1);
			var legs = new List<(double x, double z)>();
			for (int k = 0; k + 1 < route.Count; k++)
			{
				Spot a = g.Spots[route[k]], b = g.Spots[route[k + 1]];
				Legs(g, a, b, legs);
				for (int j = 1; j < legs.Count; j++)
				{
					bool verge = a.Side != b.Side && (j == 1 || j == 2);
					points.Add(verge ? (legs[j].x + g.Tx * lateral, legs[j].z + g.Tz * lateral) : legs[j]);
				}
			}
			Base(g, rr.Dst, d, out double bx, out double bz);
			points.Add((bx, bz));
			int m = points.Count;
			rr.PX[d] = new double[m];
			rr.PZ[d] = new double[m];
			rr.PS[d] = new double[m];
			double sum = 0.0;
			for (int i = 0; i < m; i++)
			{
				if (i > 0)
				{
					double dx = points[i].x - points[i - 1].x, dz = points[i].z - points[i - 1].z;
					sum += Math.Sqrt(dx * dx + dz * dz);
				}
				rr.PX[d][i] = points[i].x;
				rr.PZ[d][i] = points[i].z;
				rr.PS[d][i] = sum;
			}
		}

		/// <summary>The fewest legs from spot a to spot b over the group's links (ties to the lower spot), a and b included.</summary>
		private static List<int> Route(Group g, int a, int b)
		{
			int n = g.Spots.Length;
			int[] prev = new int[n];
			for (int i = 0; i < n; i++)
			{
				prev[i] = -2;
			}
			prev[a] = -1;
			Queue<int> queue = new Queue<int>();
			queue.Enqueue(a);
			while (queue.Count > 0 && prev[b] == -2)
			{
				int c = queue.Dequeue();
				for (int i = 0; i < n; i++)
				{
					if (g.Links[c, i] && prev[i] == -2)
					{
						prev[i] = c;
						queue.Enqueue(i);
					}
				}
			}
			List<int> route = new List<int>();
			if (prev[b] == -2)
			{
				route.Add(a);
				return route;
			}
			for (int c = b; c != -1; c = prev[c])
			{
				route.Add(c);
			}
			route.Reverse();
			return route;
		}

		/// <summary>Seconds to gallop a distance from standing (up to speed over <see cref="Ramp"/>).</summary>
		internal static double TimeFor(double x)
		{
			double xr = 0.5 * GallopSpeed * Ramp;
			return x <= xr ? Math.Sqrt(2.0 * x * Ramp / GallopSpeed) : Ramp + (x - xr) / GallopSpeed;
		}

		/// <summary>The distance galloped in <paramref name="tau"/> seconds from standing.</summary>
		internal static double DistanceIn(double tau)
		{
			if (tau <= 0.0)
			{
				return 0.0;
			}
			return tau <= Ramp ? 0.5 * GallopSpeed / Ramp * tau * tau : 0.5 * GallopSpeed * Ramp + GallopSpeed * (tau - Ramp);
		}

		/// <summary>
		/// Where deer d of group g is and what it is doing at race time t, its reactions taken in: what <see cref="At"/> says, unless a
		/// reaction is going on. Pure: the same on every machine that has the same reactions.
		/// </summary>
		internal void Live(Group g, int d, double t, ref Move mv, ref Move spare, out DeerState s)
		{
			LiveUpTo(g, d, t, g.Reactions.Count, ref mv, ref spare, out s);
		}

		/// <summary><see cref="Live"/> with only the first <paramref name="upto"/> reactions.</summary>
		private void LiveUpTo(Group g, int d, double t, int upto, ref Move mv, ref Move spare, out DeerState s)
		{
			// The latest flee started by t holds until its end (then the calm day); an alert on top of it (or of the calm day) only
			// changes how the deer stand, while it lasts.
			for (int i = upto - 1; i >= 0; i--)
			{
				Reacted rr = g.Reactions[i];
				if (rr.R.T0 > t)
				{
					continue;
				}
				if (rr.R.Kind == ReactKind.Alert)
				{
					if (t >= rr.End)
					{
						continue;
					}
					LiveUpTo(g, d, t, i, ref mv, ref spare, out s);
					if (s.Motion == DeerMotion.Graze || s.Motion == DeerMotion.Stand || s.Motion == DeerMotion.Alert)
					{
						s.Motion = DeerMotion.Alert;
						s.Yaw = Network.Atan2(rr.R.FromZ - s.Z, rr.R.FromX - s.X);
					}
					return;
				}
				if (t >= rr.End)
				{
					break;
				}
				Fleeing(g, rr, d, t, ref spare, out s);
				return;
			}
			At(g, d, t, ref mv, ref spare, out s);
		}

		private void Fleeing(Group g, Reacted rr, int d, double t, ref Move spare, out DeerState s)
		{
			s = default;
			if (t < rr.Go[d])
			{
				s.X = rr.StartX[d];
				s.Z = rr.StartZ[d];
				s.Motion = DeerMotion.Alert;
				s.Yaw = Network.Atan2(rr.R.FromZ - s.Z, rr.R.FromX - s.X);
				s.Flag = rr.R.Way != FleeWay.Freeze;
				return;
			}
			if (t < rr.Arrive[d])
			{
				double[] px = rr.PX[d], pz = rr.PZ[d], ps = rr.PS[d];
				double tau = t - rr.Go[d];
				double x = DistanceIn(tau);
				int i = 0;
				while (i + 2 < ps.Length && ps[i + 1] <= x)
				{
					i++;
				}
				double len = ps[i + 1] - ps[i];
				double f = len > 1e-9 ? Math.Max(0.0, Math.Min(1.0, (x - ps[i]) / len)) : 1.0;
				s.X = px[i] + (px[i + 1] - px[i]) * f;
				s.Z = pz[i] + (pz[i + 1] - pz[i]) * f;
				// Facing the way it runs (a leg of no length: the next one's way).
				int j = i;
				while (j + 2 < ps.Length && ps[j + 1] - ps[j] < 1e-6)
				{
					j++;
				}
				s.Yaw = Network.Atan2(pz[j + 1] - pz[j], px[j + 1] - px[j]);
				s.Speed = tau <= Ramp ? GallopSpeed * tau / Ramp : GallopSpeed;
				s.Motion = s.Speed > 12.0 ? DeerMotion.Gallop : DeerMotion.Trot;
				s.Gait = x / (s.Motion == DeerMotion.Gallop ? GallopStride : TrotStride);
				s.Flag = true;
				return;
			}
			// There: standing watching, tail up, then settling into the spot as a calm deer does.
			Rest(g, rr.Dst, d, t, rr.Arrive[d], NextDeparture(g, rr.Slot, rr.Dst, d, ref spare), ref s);
			if (t - rr.Arrive[d] < WatchSeconds)
			{
				s.Flag = true;
				if (s.Motion != DeerMotion.Walk)
				{
					s.Motion = DeerMotion.Alert;
					s.Yaw = Network.Atan2(rr.R.FromZ - s.Z, rr.R.FromX - s.X);
				}
			}
		}

		/// <summary>Whether group g is galloping off (some deer still on the way) at race time t: no new reaction until they get there.</summary>
		internal static bool Running(Group g, double t)
		{
			for (int i = g.Reactions.Count - 1; i >= 0; i--)
			{
				Reacted rr = g.Reactions[i];
				if (rr.R.T0 <= t && rr.R.Kind == ReactKind.Flee)
				{
					return t < rr.Done;
				}
			}
			return false;
		}

		/// <summary>Whether group g is alert (heads up after a reaction, fled or not) at race time t.</summary>
		internal static bool Alerted(Group g, double t)
		{
			for (int i = g.Reactions.Count - 1; i >= 0; i--)
			{
				Reacted rr = g.Reactions[i];
				if (rr.R.T0 <= t)
				{
					return rr.R.Kind == ReactKind.Alert ? t < rr.End : t < rr.Done + WatchSeconds;
				}
			}
			return false;
		}
	}
}
