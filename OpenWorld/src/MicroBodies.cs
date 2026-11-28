using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Part of <see cref="Micro"/>: bodies the micro's drivers see that aren't theirs, the player's car first. Each step every body is laid onto
	/// the lanes and connectors it covers (its footprint across a lane's line within a car's half width of it), and a driver whose way runs
	/// through it treats it as the car ahead, at its speed along the lane: so a car stops behind the player standing in the road, queues
	/// behind it at a junction, follows it at its speed. The queue a player causes is a real one (it spills back into the meso through the
	/// lanes' storage) and is still there when the player comes back. No Unity types: the game sets <see cref="Bodies"/> before each step.
	/// </summary>
	internal sealed partial class Micro
	{
		/// <summary>A body on the roads: its middle (world x, z), the way it faces (unit), its velocity (units a second), half its length and width.</summary>
		internal struct Body
		{
			public double X, Z, Hx, Hz, Vx, Vz, HalfLong, HalfWide;
		}

		/// <summary>The bodies the drivers see this step (the game puts the player's car here).</summary>
		internal readonly List<Body> Bodies = new List<Body>();

		/// <summary>
		/// What the bodies cover: on lane <c>lane</c> (or connector <c>conn</c>, the other -1) from <c>rear</c> to <c>front</c> along it (a lane's
		/// carriageway distance as a car's <see cref="Agent.X"/> counts it; a connector's distance past its stop line), moving along it at
		/// <c>v</c>.
		/// </summary>
		private readonly List<(int lane, int conn, double rear, double front, double v)> blocks = new List<(int, int, double, double, double)>();

		/// <summary>For trafficsim and the overlay: how many lanes and connectors the bodies cover now.</summary>
		internal int Blocked => blocks.Count;

		/// <summary>For trafficsim: what the bodies cover, as text.</summary>
		internal string BlocksText()
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach ((int lane, int conn, double rear, double front, double v) in blocks)
			{
				sb.Append(lane >= 0 ? "lane " + lane : "conn " + conn).Append(' ').Append(rear.ToString("0.0")).Append("..").Append(front.ToString("0.0")).Append("; ");
			}
			return sb.ToString();
		}

		/// <summary>How close to a lane's line (beyond the body's own half extent across it) a body blocks it: half a car and a little.</summary>
		private const double BlockReach = Traffic.CarLength * 0.2 + 0.5;

		// ---- the index of lane and connector lines -------------------------------------------------------------------------

		private const double BodyCell = 50.0;
		private static Dictionary<long, List<(int lane, double s, double x, double z, double hx, double hz)>> laneLines;
		private static Dictionary<long, List<int>> connAt;
		private static int bodiesFor = -1;

		private static void PrepareBodies()
		{
			lock (gate)
			{
				if (bodiesFor == Lanes.All.Count && laneLines != null)
				{
					return;
				}
				var lines = new Dictionary<long, List<(int, double, double, double, double, double)>>();
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					Lanes.Lane lane = Lanes.All[l];
					double len = Math.Abs(lane.End - lane.Start);
					int n = Math.Max(1, (int)Math.Ceiling(len / 8.0));
					for (int k = 0; k <= n; k++)
					{
						double s = lane.Start + (lane.End - lane.Start) * k / n;
						Lanes.Point(lane, s, out double x, out double z, out double _, out double hx, out double hz);
						long key = BodyKey(x, z);
						if (!lines.TryGetValue(key, out var list))
						{
							lines[key] = list = new List<(int, double, double, double, double, double)>();
						}
						list.Add((l, s, x, z, hx, hz));
					}
				}
				var conns = new Dictionary<long, List<int>>();
				for (int c = 0; c < Lanes.Connectors.Count; c++)
				{
					Lanes.Connector cn = Lanes.Connectors[c];
					if (cn.Turn == Lanes.Jump || cn.X.Length == 0)
					{
						continue;
					}
					HashSet<long> keys = new HashSet<long>();
					for (int p = 0; p < cn.X.Length; p++)
					{
						keys.Add(BodyKey(cn.X[p], cn.Z[p]));
					}
					foreach (long key in keys)
					{
						if (!conns.TryGetValue(key, out var list))
						{
							conns[key] = list = new List<int>();
						}
						list.Add(c);
					}
				}
				laneLines = lines;
				connAt = conns;
				bodiesFor = Lanes.All.Count;
			}
		}

		private static long BodyKey(double x, double z)
		{
			long cx = (long)Math.Floor(x / BodyCell), cz = (long)Math.Floor(z / BodyCell);
			return (cx << 32) ^ (cz & 0xffffffffL);
		}

		// ---- laying the bodies on the lanes -----------------------------------------------------------------------------------

		/// <summary>Lays every body onto the lanes and connectors it covers (once a step, before the drivers decide).</summary>
		private void LayBodies()
		{
			blocks.Clear();
			if (Bodies.Count == 0)
			{
				return;
			}
			PrepareBodies();
			Dictionary<int, (double d, double s, double hx, double hz)> nearest = new Dictionary<int, (double, double, double, double)>();
			HashSet<int> conns = new HashSet<int>();
			foreach (Body b in Bodies)
			{
				nearest.Clear();
				conns.Clear();
				double reach = Math.Max(b.HalfLong, b.HalfWide) + BlockReach + 8.0;
				int gx0 = (int)Math.Floor((b.X - reach) / BodyCell), gx1 = (int)Math.Floor((b.X + reach) / BodyCell);
				int gz0 = (int)Math.Floor((b.Z - reach) / BodyCell), gz1 = (int)Math.Floor((b.Z + reach) / BodyCell);
				for (int gx = gx0; gx <= gx1; gx++)
				{
					for (int gz = gz0; gz <= gz1; gz++)
					{
						long key = ((long)gx << 32) ^ ((long)gz & 0xffffffffL);
						if (laneLines.TryGetValue(key, out var list))
						{
							foreach ((int lane, double s, double x, double z, double hx, double hz) p in list)
							{
								double dx = p.x - b.X, dz = p.z - b.Z, d = dx * dx + dz * dz;
								if (d < reach * reach && (!nearest.TryGetValue(p.lane, out var best) || d < best.d))
								{
									nearest[p.lane] = (d, p.s, p.hx, p.hz);
								}
							}
						}
						if (connAt.TryGetValue(key, out var cl))
						{
							foreach (int c in cl)
							{
								conns.Add(c);
							}
						}
					}
				}
				foreach (KeyValuePair<int, (double d, double s, double hx, double hz)> kv in nearest)
				{
					LayOnLane(b, kv.Key, kv.Value.s);
				}
				foreach (int c in conns)
				{
					LayOnConnector(b, c);
				}
			}
		}

		/// <summary>The body's half extent along a line facing (ux, uz), and across it.</summary>
		private static void Extents(Body b, double ux, double uz, out double along, out double across)
		{
			double dot = Math.Abs(b.Hx * ux + b.Hz * uz), cross = Math.Abs(b.Hx * uz - b.Hz * ux);
			along = dot * b.HalfLong + cross * b.HalfWide;
			across = cross * b.HalfLong + dot * b.HalfWide;
		}

		private void LayOnLane(Body b, int l, double s0)
		{
			Lanes.Lane lane = Lanes.All[l];
			// The body's middle projected onto the lane's line, refined from the nearest sample (two steps of Newton along the line).
			double s = s0;
			double x = 0.0, z = 0.0, hx = 0.0, hz = 0.0;
			double lo = Math.Min(lane.Start, lane.End), hi = Math.Max(lane.Start, lane.End);
			for (int it = 0; it < 3; it++)
			{
				Lanes.Point(lane, s, out x, out z, out double _, out hx, out hz);
				double t = (b.X - x) * hx + (b.Z - z) * hz;
				// hx, hz is the way of travel; road s runs with it going +1 and against it going -1.
				double ns = s + t * lane.Dir;
				ns = Math.Max(lo, Math.Min(hi, ns));
				if (Math.Abs(ns - s) < 0.05)
				{
					s = ns;
					break;
				}
				s = ns;
			}
			Lanes.Point(lane, s, out x, out z, out double _, out hx, out hz);
			double off = Math.Abs((b.X - x) * -hz + (b.Z - z) * hx);
			double ahead = (b.X - x) * hx + (b.Z - z) * hz;
			Extents(b, hx, hz, out double along, out double across);
			if (off > across + BlockReach || Math.Abs(ahead) > along + 1.0)
			{
				return;
			}
			int cw = Meso.CarriagewayOf(l);
			double mid = Meso.Along(cw, s);
			double v = b.Vx * hx + b.Vz * hz;
			blocks.Add((l, -1, mid - along, mid + along, Math.Max(0.0, v)));
		}

		private void LayOnConnector(Body b, int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			int n = cn.X.Length;
			double best = double.MaxValue, bestD = 0.0, bhx = 1.0, bhz = 0.0;
			for (int p = 0; p + 1 < n; p++)
			{
				double ax = cn.X[p], az = cn.Z[p], ex = cn.X[p + 1] - ax, ez = cn.Z[p + 1] - az, len2 = ex * ex + ez * ez;
				if (len2 < 1e-12)
				{
					continue;
				}
				double t = Math.Max(0.0, Math.Min(1.0, ((b.X - ax) * ex + (b.Z - az) * ez) / len2));
				double px = ax + ex * t, pz = az + ez * t, dx = b.X - px, dz = b.Z - pz, d = dx * dx + dz * dz;
				if (d < best)
				{
					double len = Math.Sqrt(len2);
					best = d;
					bestD = cn.D[p] + (cn.D[p + 1] - cn.D[p]) * t;
					bhx = ex / len;
					bhz = ez / len;
				}
			}
			if (best == double.MaxValue)
			{
				return;
			}
			Extents(b, bhx, bhz, out double along, out double across);
			if (Math.Sqrt(best) > across + BlockReach)
			{
				return;
			}
			double v = b.Vx * bhx + b.Vz * bhz;
			blocks.Add((-1, c, bestD - along, bestD + along, Math.Max(0.0, v)));
		}

		/// <summary>
		/// Whether the bodies leave room past connector <paramref name="c"/>'s box onto lane <paramref name="to"/> (its entry at <paramref
		/// name="entry"/>, <paramref name="need"/> wanted past it): none standing in the box on the connector, none on the lane out short of
		/// the room even after going on as far as <paramref name="gone"/> lets it (from its speed and where it is). A car doesn't go into a
		/// junction it would be stuck in behind the player.
		/// </summary>
		private bool BodiesLeaveRoom(int c, int to, double entry, double need, Func<double, double, double> gone)
		{
			foreach ((int bl, int bc, double rear, double front, double v) in blocks)
			{
				if (bc == c)
				{
					return false;
				}
				if (bl == to && front > entry && rear + gone(v, rear) - entry < need)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Whether a car with its front at <paramref name="x"/> going <paramref name="v"/> may change into lane <paramref name="to"/> for the
		/// bodies on it: none alongside it, none ahead it would have to brake harder than <see cref="Driver.SafeBrake"/> for; its acceleration
		/// in the new lane (<paramref name="accNew"/>, MOBIL's) lowered for the nearest ahead, so a lane with a wreck in it isn't one worth
		/// moving into (a through car moved right into a wreck's lane 120 short of it and couldn't stop).
		/// </summary>
		private bool BodiesLetChange(int to, double x, double v, double limit, ref double accNew)
		{
			foreach ((int bl, int bc, double rear, double front, double bv) in blocks)
			{
				if (bl != to)
				{
					continue;
				}
				if (front > x - Traffic.CarLength - Driver.Gap0 && rear < x + Driver.Gap0)
				{
					return false;
				}
				if (rear >= x + Driver.Gap0)
				{
					accNew = Math.Min(accNew, Driver.Follow(v, limit, rear - x, bv));
					if (accNew < -Driver.SafeBrake)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// The bodies on lane <paramref name="lane"/> (or connector <paramref name="conn"/>) ahead of a front at <paramref name="x"/> along it:
		/// each one's gap from there, plus <paramref name="add"/>, and its speed, to <paramref name="consider"/> as a car ahead.
		/// </summary>
		private void BodiesAhead(int lane, int conn, double x, double add, Action<double, double, int> consider)
		{
			foreach ((int bl, int bc, double rear, double front, double v) in blocks)
			{
				if ((lane >= 0 && bl == lane) || (conn >= 0 && bc == conn))
				{
					// A body round a car's front (it drove into the car's side) is ahead of it too: it can't go through it.
					if (front > x)
					{
						consider(Math.Max(0.0, rear - x) + add, v, -1);
					}
				}
			}
		}
	}
}
