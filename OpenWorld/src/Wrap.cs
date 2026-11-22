using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The highway wraps round, arcade style: a car that drives into the trunk road's tunnel at one edge of the world comes out of the
	/// tunnel at the other edge, at the same speed, in the same lane, facing the same way. The trunk leaves the world straight out of the
	/// west and the east edges (<see cref="Network"/>'s Extend), so the two tunnels face opposite ways and the jump is a plain move with
	/// no turn: going west you are in the westbound carriageway at both ends.
	///
	/// The two tunnels are glued at a plane <see cref="Depth"/> inside each: a car crossing it going out of one is put the same distance
	/// back from it in the other, going in. The road north has no partner and its tunnel stays shut. Pure (no Unity types), so
	/// <c>tools/meshcheck</c> checks it; <see cref="Streamer"/> does the jump and <see cref="WorldScene"/> builds the open tunnels.
	/// </summary>
	internal static class Wrap
	{
		/// <summary>How far into a tunnel the glue plane is: deep enough that a driver's eye is well inside the dark when the car goes.</summary>
		internal const double Depth = 14.0;

		/// <summary>
		/// How deep an open tunnel is built (its back wall): short of where the edge wall's ground rises to the floor. The back wall is
		/// only seen, not solid: a fast car's nose may reach it in the frame before the wrap sees the car past the glue plane.
		/// </summary>
		internal const double Length = 24.0;

		/// <summary>Within this far of an open tunnel's mouth, the ground round the other mouth is loaded, so a car lands on something.</summary>
		internal const double Preload = 700.0;

		/// <summary>The two portals (indices in <see cref="Network.Portals"/>) joined, or -1 if the world has no such pair.</summary>
		internal static int A = -1, B = -1;

		/// <summary>Finds the pair: the two portals of one road, facing opposite ways (the trunk's). Called once the layout is built.</summary>
		internal static void Find()
		{
			A = B = -1;
			for (int i = 0; i < Network.Portals.Count; i++)
			{
				for (int j = i + 1; j < Network.Portals.Count; j++)
				{
					Network.Portal p = Network.Portals[i], q = Network.Portals[j];
					if (p.Road == q.Road && p.DirX == -q.DirX && p.DirZ == -q.DirZ)
					{
						A = i;
						B = j;
						return;
					}
				}
			}
		}

		/// <summary>True if portal <paramref name="i"/> is one of the wrap's open tunnels.</summary>
		internal static bool Open(int i)
		{
			return i >= 0 && (i == A || i == B);
		}

		/// <summary>The other end of the wrap from portal <paramref name="i"/> (one of <see cref="A"/>, <see cref="B"/>).</summary>
		internal static int Other(int i)
		{
			return i == A ? B : A;
		}

		/// <summary>How far point (x, z) is into portal <paramref name="i"/>'s tunnel (negative in the world) and across it (right of the way out).</summary>
		internal static void Into(int i, double x, double z, out double along, out double across)
		{
			Network.Portal p = Network.Portals[i];
			along = (x - p.X) * p.DirX + (z - p.Z) * p.DirZ;
			across = (x - p.X) * -p.DirZ + (z - p.Z) * p.DirX;
		}

		/// <summary>True if (x, z) is inside portal <paramref name="i"/>'s mouth, past the glue plane.</summary>
		internal static bool Past(int i, double x, double z)
		{
			Into(i, x, z, out double along, out double across);
			return along > Depth && along < Length && Math.Abs(across) < Network.Portals[i].Half + 4.0;
		}

		/// <summary>
		/// The move from portal <paramref name="i"/>'s tunnel to the other's: add (dx, dz) to a point and dy to its height. The point
		/// <see cref="Depth"/> + k into one lands <see cref="Depth"/> - k into the other, as far across, the road's height the other's.
		/// </summary>
		internal static void Move(int i, out double dx, out double dz, out double dy)
		{
			Network.Portal p = Network.Portals[i], q = Network.Portals[Other(i)];
			// p + dirP * (D + k) + across  ->  q + dirQ * (D - k) + across, with dirQ = -dirP: the same across, the move is (q - p) + 2D dirQ.
			dx = q.X - p.X + 2.0 * Depth * q.DirX;
			dz = q.Z - p.Z + 2.0 * Depth * q.DirZ;
			dy = q.Y - p.Y;
		}
	}
}
