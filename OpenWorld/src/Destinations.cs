using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The world's key places, in a fixed order, for the debug teleport to go round: the spawn, the towns, the circuit's grid and paddock, the rally stage's start and service area, the hill climb's start and finish, the highway's start, the retail park, a filling
	/// station, a workshop, the parks, the lake trails, the villages and the mountain pass. Each is a point on a road (on its right-hand lane)
	/// and the road's direction there, so a car put there can drive off at once.
	/// </summary>
	internal static class Destinations
	{
		internal struct Place
		{
			public string Name;
			public double X;
			public double Z;
			public double Dx;
			public double Dz;
		}

		private static List<Place> all;

		/// <summary>The places, worked out the first time they are asked for (the layout must be built).</summary>
		internal static List<Place> All
		{
			get
			{
				if (all == null)
				{
					all = Make();
				}
				return all;
			}
		}

		private static List<Place> Make()
		{
			List<Place> list = new List<Place>();
			Network.Ensure();
			list.Add(new Place { Name = "Spawn", X = Network.SpawnX, Z = Network.SpawnZ, Dx = Network.SpawnDirX, Dz = Network.SpawnDirZ });
			foreach (Places.Place p in Places.All)
			{
				if (p.Kind == PlaceKind.Town)
				{
					OnRoad(list, p.Name + " (town centre)", p.X, p.Z);
				}
			}
			if (Circuit.Exists)
			{
				// Pole position, facing down the start straight; the paddock behind the garages, facing the same way.
				Circuit.Slot(0, out double gx, out double gz);
				list.Add(new Place { Name = Names.Circuit + " (grid)", X = gx, Z = gz, Dx = Circuit.DirX, Dz = Circuit.DirZ });
				Circuit.Straight((Circuit.PaddockA + Circuit.PaddockB) * 0.5, (Circuit.GaragesOut + Circuit.PaddockOut) * 0.5, out double px, out double pz);
				list.Add(new Place { Name = Names.Circuit + " (paddock)", X = px, Z = pz, Dx = Circuit.DirX, Dz = Circuit.DirZ });
			}
			if (Rally.Exists && Rally.Road != null)
			{
				// Behind the start line, facing along the stage; the service area's yard, facing the same way.
				Rally.At(Rally.StartS - 20.0, out double sx, out double sz, out double dx, out double dz);
				list.Add(new Place { Name = Names.Rally + " (start)", X = sx, Z = sz, Dx = dx, Dz = dz });
				foreach (Town.Lot lot in Town.Lots)
				{
					if (lot.Kind == LotKind.Service)
					{
						Town.World(lot, 0.0, lot.D * 0.4, out double lx, out double lz);
						list.Add(new Place { Name = Names.Rally + " (service area)", X = lx, Z = lz, Dx = Rally.DirX, Dz = Rally.DirZ });
					}
				}
			}
			if (HillClimb.Exists && HillClimb.Road != null)
			{
				// Behind the start line, facing up the road; the top, short of the finish line, facing on over the col.
				HillClimb.At(HillClimb.StartS - 20.0, out double sx, out double sz, out double dx, out double dz);
				list.Add(new Place { Name = Names.HillClimb + " (start)", X = sx, Z = sz, Dx = dx, Dz = dz });
				HillClimb.At(HillClimb.FinishS - 40.0, out double fx, out double fz, out double ex, out double ez);
				list.Add(new Place { Name = Names.HillClimb + " (finish)", X = fx, Z = fz, Dx = ex, Dz = ez });
			}
			foreach (Courses.Course c in Courses.All)
			{
				// The highway's grid, on the eastbound carriageway at the west edge, facing east.
				if (c.Kind == Courses.Kind.Highway)
				{
					list.Add(new Place { Name = Names.Highway + " (start)", X = c.GridX, Z = c.GridZ, Dx = c.GridDx, Dz = c.GridDz });
				}
			}
			bool garage = false, workshop = false;
			foreach (Town.Lot lot in Town.Lots)
			{
				string name = lot.Kind == LotKind.Retail ? lot.Place + " retail park" : lot.Kind == LotKind.Garage && !garage ? lot.Place + " filling station" : lot.Kind == LotKind.Workshop && !workshop ? lot.Place + " workshop" : null;
				if (name == null)
				{
					continue;
				}
				garage |= lot.Kind == LotKind.Garage;
				workshop |= lot.Kind == LotKind.Workshop;
				// In front of the lot, on its road.
				Town.World(lot, 0.0, -8.0, out double x, out double z);
				OnRoad(list, name, x, z);
			}
			// The parks, from the street beside them; the lake trails, from the road nearest the middle of each one's longest stretch.
			foreach (Parks.Park park in Parks.All)
			{
				OnRoad(list, park.Name, park.X, park.Z);
			}
			Dictionary<string, Network.Road> trails = new Dictionary<string, Network.Road>();
			List<string> order = new List<string>();
			foreach (Network.Road road in Network.Roads)
			{
				if (road.Kind != RoadKind.Path || road.Name == null || !road.Name.StartsWith("Lake trail ", StringComparison.Ordinal) || road.Name.EndsWith(" way", StringComparison.Ordinal))
				{
					continue;
				}
				if (!trails.TryGetValue(road.Name, out Network.Road longest))
				{
					order.Add(road.Name);
					trails[road.Name] = road;
				}
				else if (road.S[road.Count - 1] > longest.S[longest.Count - 1])
				{
					trails[road.Name] = road;
				}
			}
			foreach (string name in order)
			{
				Network.Road road = trails[name];
				OnRoad(list, name, road.X[road.Count / 2], road.Z[road.Count / 2]);
			}
			foreach (Places.Place p in Places.All)
			{
				if (p.Kind == PlaceKind.Village)
				{
					OnRoad(list, p.Name + " (village)", p.X, p.Z);
				}
			}
			Pass(list);
			return list;
		}

		/// <summary>The highest point of the road over the mountains to the north edge.</summary>
		private static void Pass(List<Place> list)
		{
			foreach (Network.Road road in Network.Roads)
			{
				if (road.Name == null || !road.Name.EndsWith(" - north", StringComparison.Ordinal))
				{
					continue;
				}
				int top = 0;
				for (int i = 1; i < road.Count; i++)
				{
					top = road.Y[i] > road.Y[top] ? i : top;
				}
				OnRoad(list, "Mountain pass", road.X[top], road.Z[top]);
				return;
			}
		}

		/// <summary>Adds the road point nearest (x, z), in the right-hand lane, facing along the road.</summary>
		internal static void OnRoad(List<Place> list, string name, double x, double z)
		{
			if (!Network.NearestRoad(x, z, Streamer.TileSize, out double rx, out double rz, out double dx, out double dz))
			{
				return;
			}
			// Right of the way the car faces is (dz, -dx); a lane is about 11 wide.
			list.Add(new Place { Name = name, X = rx + dz * 4.0, Z = rz - dx * 4.0, Dx = dx, Dz = dz });
		}
	}
}
