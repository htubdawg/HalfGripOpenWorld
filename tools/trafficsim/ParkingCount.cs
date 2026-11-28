using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// TRAFFICSIM_PARKING=1: the parking the world draws against the cars that need it. Per place: its lot's bays as drawn, the kerb spaces on
// its stretch (if its road has kerb parking), the meso's count (Destination.Spaces), and the most cars its plans have parked there at once
// over the day (Schedules.At every 2 day seconds, on empty roads). Homes: cars against their own spaces. Visits: the most at once.
internal static partial class Program
{
	private static void ParkingCount(int seed)
	{
		Schedules plans = Schedules.Build(seed);
		int nd = Traffic.Destinations.Count;
		int[] now = new int[nd], most = new int[nd];
		int visitsMost = 0, visitsNoKerbMost = 0, homesMost = 0;
		for (double t = 0.0; t < Traffic.DaySeconds; t += 2.0)
		{
			Array.Clear(now, 0, nd);
			int visits = 0, visitsNoKerb = 0, homes = 0;
			foreach (Schedules.Car c in plans.Cars)
			{
				if (c.Through)
				{
					continue;
				}
				Schedules.Where w = Schedules.At(c, t);
				if (w.State != Schedules.State.Parked)
				{
					continue;
				}
				Schedules.Stop st = c.Stops[w.Index];
				if (st.Kind == Schedules.StopKind.Place)
				{
					now[st.Dest]++;
				}
				else if (st.Kind == Schedules.StopKind.Visit)
				{
					visits++;
					Traffic.Anchor at = st.Host >= 0 ? Traffic.Households[st.Host].Home : st.At;
					visitsNoKerb += KerbOf(at.Edge) == 0 ? 1 : 0;
				}
				else if (st.Kind == Schedules.StopKind.Home)
				{
					homes++;
				}
			}
			for (int d = 0; d < nd; d++)
			{
				most[d] = Math.Max(most[d], now[d]);
			}
			visitsMost = Math.Max(visitsMost, visits);
			visitsNoKerbMost = Math.Max(visitsNoKerbMost, visitsNoKerb);
			homesMost = Math.Max(homesMost, homes);
		}
		Console.WriteLine("parking, seed " + seed + (plans.Weekend ? " (a weekend day)" : " (a weekday)") + ": by kind of place, places / drawn lot bays / kerb spaces on their stretch / the meso's count / the most cars there at once (summed over places) / places where more cars come at once than lot and kerb hold, and the cars over");
		List<(int bays, int kerb, int meso, int most)> rows = new List<(int, int, int, int)>();
		var lot = new List<(double u, double v, double nu, double nv)>();
		foreach (Traffic.DestinationKind kind in Enum.GetValues(typeof(Traffic.DestinationKind)))
		{
			int count = 0, bays = 0, kerb = 0, meso = 0, peak = 0, short_ = 0, over = 0;
			for (int d = 0; d < nd; d++)
			{
				Traffic.Destination dest = Traffic.Destinations[d];
				if (dest.Kind != kind)
				{
					continue;
				}
				count++;
				int b = 0;
				if (dest.Lot >= 0)
				{
					TrafficParking.Spaces(Town.Lots[dest.Lot], lot);
					b = lot.Count;
				}
				int k = KerbOf(dest.At.Edge);
				bays += b;
				kerb += k;
				meso += dest.Spaces;
				peak += most[d];
				if (most[d] > b + k)
				{
					short_++;
					over += most[d] - b - k;
				}
			}
			if (count > 0)
			{
				Console.WriteLine("    " + kind + ": " + count + " / " + bays + " / " + kerb + " / " + meso + " / " + peak + " / " + short_ + " short by " + over);
			}
		}
		int own = 0, kerbHome = 0, none = 0, cars = 0;
		TrafficParking p = new TrafficParking(plans.Cars.ToArray());
		Console.WriteLine("    homes: " + Traffic.Households.Count + " households, " + plans.Cars.Count(c => !c.Through) + " cars; " + p.OwnSpaces + " with their own space, " + p.KerbHomes + " at the kerb by the door, " + p.RoundTheBack + " with none (round the back); the most at home at once " + homesMost);
		Console.WriteLine("    visits: the most parked at once " + visitsMost + ", " + visitsNoKerbMost + " of them at a home whose road has no kerb parking");
	}

	/// <summary>Kerb spaces (both sides, at the kerb pitch) on a stretch whose road has kerb parking; 0 otherwise.</summary>
	private static int KerbOf(int edge)
	{
		if (edge < 0 || edge >= Traffic.Edges.Count)
		{
			return 0;
		}
		Traffic.Edge e = Traffic.Edges[edge];
		RoadKind kind = Network.Roads[e.Road].Kind;
		if (kind != RoadKind.Secondary && kind != RoadKind.Street)
		{
			return 0;
		}
		double len = Math.Abs(e.Sb - e.Sa) - Traffic.Nodes[e.A].Box - Traffic.Nodes[e.B].Box - TrafficParking.KerbSpacing;
		return len <= 0.0 ? 0 : 2 * ((int)(len / TrafficParking.KerbSpacing) + 1);
	}
}
