using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>What a garage stands on.</summary>
	internal enum GarageKind
	{
		FillingStation = 0,
		Workshop = 1,
		Pits = 2,
		Service = 3,
		Paddock = 4,
	}

	/// <summary>
	/// Where the garages are: a circle on the open surface of each filling station's forecourt and workshop's yard, three along the
	/// circuit's pit lane in front of its garages, one in the rally stage's service area and one in the hill climb's paddock. Read from
	/// the layout, never added to it: generation is unchanged, and every player in a room gets the same garages because they get the
	/// same layout. Pure arithmetic (no Unity), so the preview draws them and meshcheck checks they lie on clear ground.
	/// </summary>
	internal static class GarageSites
	{
		internal sealed class Site
		{
			public GarageKind Kind;
			/// <summary>World x, z of the circle's middle.</summary>
			public double X;
			public double Z;
			/// <summary>Height of the surface under it, from sea level (add <see cref="Network.Lift"/> for the scene).</summary>
			public double Y;
			/// <summary>The surface's rise per unit east and north: lots follow their road's grade, the circuit's plane tilts a little.</summary>
			public double Gx;
			public double Gz;
			public double Radius;
			public string Name;
		}

		/// <summary>The circle's radius: room for a car (about 14 long) to stop in it without aiming.</summary>
		internal const double Radius = 9.0;

		/// <summary>The pit lane's circles are a little smaller: the lane is 35 wide between the pit wall and the garages.</summary>
		internal const double PitRadius = 8.0;

		private static readonly object gate = new object();
		private static List<Site> all;

		/// <summary>Every garage in the world, once the layout is built.</summary>
		internal static List<Site> All
		{
			get
			{
				lock (gate)
				{
					if (all == null)
					{
						Network.Ensure();
						all = Build();
					}
					return all;
				}
			}
		}

		private static List<Site> Build()
		{
			List<Site> list = new List<Site>();
			foreach (Town.Lot lot in Town.Lots)
			{
				double u = 0.0, v;
				GarageKind kind;
				string name;
				switch (lot.Kind)
				{
					// Behind the pumps, in front of the kiosk: the pumps stand 20 back, the canopy's posts 27, the kiosk starts 44 back.
					case LotKind.Garage: kind = GarageKind.FillingStation; v = 34.0; name = lot.Place + " filling station"; break;
					// The middle of the yard in front of the workshop's doors (the building starts 26 back).
					case LotKind.Workshop: kind = GarageKind.Workshop; v = 13.0; name = lot.Place + " workshop"; break;
					// Well in front of the vans and awnings along the back.
					case LotKind.Service: kind = GarageKind.Service; v = lot.D * 0.3; name = Names.Rally + " service area"; break;
					case LotKind.Paddock: kind = GarageKind.Paddock; v = lot.D * 0.3; name = Names.HillClimb + " paddock"; break;
					default: continue;
				}
				Town.World(lot, u, v, out double x, out double z);
				list.Add(new Site { Kind = kind, X = x, Z = z, Y = Town.Surface(lot, u), Gx = lot.Grade * lot.Dx, Gz = lot.Grade * lot.Dz, Radius = Radius, Name = name });
			}
			if (Circuit.Exists)
			{
				// In the pit lane, in front of the garages' doors: a quarter, a half and three quarters along the row.
				double v = Circuit.PitLaneOut - PitRadius - 2.0;
				for (int i = 1; i <= 3; i++)
				{
					double u = Circuit.GaragesA + (Circuit.GaragesB - Circuit.GaragesA) * i / 4.0;
					Circuit.Straight(u, v, out double x, out double z);
					list.Add(new Site { Kind = GarageKind.Pits, X = x, Z = z, Y = Circuit.PlaneAt(x, z) + Circuit.Lift, Gx = Circuit.Px, Gz = Circuit.Pz, Radius = PitRadius, Name = Names.Circuit + " pits" });
				}
			}
			return list;
		}

		/// <summary>Height of the surface under a site at (x, z), from sea level.</summary>
		internal static double SurfaceAt(Site s, double x, double z)
		{
			return s.Y + s.Gx * (x - s.X) + s.Gz * (z - s.Z);
		}

		/// <summary>The garage whose circle (x, z) is in, or null.</summary>
		internal static Site At(double x, double z)
		{
			foreach (Site s in All)
			{
				double dx = x - s.X, dz = z - s.Z;
				if (dx * dx + dz * dz <= s.Radius * s.Radius)
				{
					return s;
				}
			}
			return null;
		}
	}
}
