using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where each parked car of a day of traffic (<see cref="TrafficDay"/>) stands and which way it faces: a household's own cars on its
	/// drive pad, at the kerb in front of a terrace or by a cottage or a farm; a visitor at the kerb by the house it visits; at a shop or
	/// the retail park in the painted bays (the same arithmetic <see cref="Buildings"/> paints them with), at a workshop, filling station,
	/// market or course's yard in rows on its surface; at the offices, the high streets and the churches along the kerb. A place's spaces
	/// are given out in the order cars arrive (the lowest free one), so two cars never stand in one spot; a place fuller than its spaces
	/// carries on along the kerb.
	///
	/// The day says only where a car is parked (its anchor); this is how it is drawn, so it may use anything pure and fixed with the day.
	/// No Unity types (<c>tools/trafficview</c> checks it).
	/// </summary>
	internal sealed class TrafficParking
	{
		/// <summary>A parked car: the middle of the car, the way its nose faces (unit), and whether it is off the map.</summary>
		internal struct Spot
		{
			public double X, Z, Hx, Hz;
			public bool Away;
		}

		/// <summary>A painted bay's width and length (as <see cref="Buildings"/> paints them: 2.7 m by 4.8 m).</summary>
		internal const double BayWidth = 9.0, BayLength = 16.0;

		/// <summary>Spacing of cars parked one behind the other along a kerb (a car and a gap: about 5.2 m).</summary>
		internal const double KerbSpacing = 17.0;

		/// <summary>How far in from the edge of the paving the middle of a car at the kerb is (half a car and a little), on a road wide
		/// enough; and the room left between it and a car driving past in the lane.</summary>
		internal const double KerbIn = 3.5, KerbClear = 0.4;

		private readonly TrafficDay day;

		/// <summary>Per leg: the space it ends in at its place (-1: it ends off the map, or never ran).</summary>
		private readonly int[] slot;

		/// <summary>Per car: its place in its household's cars (its space at home), and the leg before each leg of its own (-1: none).</summary>
		private readonly int[] homeIndex;
		private readonly int[] before;

		/// <summary>Per destination with a lot: its spaces on the lot (lot-frame u, v and the way the nose faces along v).</summary>
		private readonly Dictionary<int, List<(double u, double v, double nu, double nv)>> lotSpaces = new Dictionary<int, List<(double, double, double, double)>>();

		/// <summary>Per car: its own space at home off the road (<see cref="OffRoad"/>), and whether it has one (else the kerb, as a terrace's).</summary>
		private readonly Spot[] ownSpot;
		private readonly bool[] hasOwn;

		/// <summary>The most cars any place held at once over the day, and how many stays carried on along the kerb past a place's spaces.</summary>
		internal int Busiest, Overflow;

		internal TrafficParking(TrafficDay day)
		{
			this.day = day;
			EnsureHouses();
			slot = new int[day.Legs.Count];
			before = new int[day.Legs.Count];
			homeIndex = new int[day.Cars.Count];
			ownSpot = new Spot[day.Cars.Count];
			hasOwn = new bool[day.Cars.Count];
			for (int i = 0; i < slot.Length; i++)
			{
				slot[i] = -1;
				before[i] = -1;
			}
			int lastHousehold = -1, n = 0;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Car car = day.Cars[c];
				n = car.Household == lastHousehold ? n + 1 : 0;
				lastHousehold = car.Household;
				homeIndex[c] = n;
				if (car.Household >= 0)
				{
					// Taken one after another, each clear of those before (two neighbours' pads can meet between their houses).
					hasOwn[c] = OffRoad(car.Household, n, out ownSpot[c]);
					if (hasOwn[c])
					{
						Hold(ownSpot[c]);
					}
				}
				int prev = -1;
				foreach (int li in car.Legs)
				{
					if (day.Legs[li].Start < 0.0)
					{
						continue;
					}
					before[li] = prev;
					prev = li;
				}
			}
			Assign();
		}

		/// <summary>The place a leg ends at, as a key: a destination's index, or -1 - a household's for a home; int.MinValue off the map.</summary>
		private int PlaceOf(TrafficDay.Leg leg)
		{
			if (leg.Vanishes)
			{
				return int.MinValue;
			}
			if (leg.Return)
			{
				int h = day.Cars[leg.Car].Household;
				return h >= 0 ? -1 - h : int.MinValue;
			}
			if (leg.Host >= 0)
			{
				return -1 - leg.Host;
			}
			return leg.Dest >= 0 ? leg.Dest : int.MinValue;
		}

		/// <summary>
		/// The kerb along one side of one stretch of road, cut into spaces a car's length apart, clear of the junctions at its ends:
		/// each space's road distance, the terrace household's car that keeps it all day (-1: none), and the stays in it.
		/// </summary>
		private sealed class KerbSide
		{
			public int Edge, Side;
			public double[] S;
			public int[] Kept;
			public List<(double arrive, double leave)>[] Stays;
		}

		private readonly Dictionary<int, KerbSide> kerbs = new Dictionary<int, KerbSide>();

		/// <summary>Per leg ending at the kerb: its side and space there (else null).</summary>
		private readonly Dictionary<int, (KerbSide side, int k)> kerbOf = new Dictionary<int, (KerbSide, int)>();

		/// <summary>Per terrace car: its kerb space at home.</summary>
		private readonly Dictionary<int, (KerbSide side, int k)> kerbHome = new Dictionary<int, (KerbSide, int)>();

		/// <summary>Kerb spaces made, and those struck out: over a home's own space, in a house, on another road or the water.</summary>
		internal int KerbSpaces, StruckHome, StruckHouse, StruckOther;

		/// <summary>Stays that found no free space at the kerb near their place (they stand where the old arithmetic put them).</summary>
		internal int KerbFull;

		/// <summary>The kerb of edge <paramref name="edge"/>'s side <paramref name="side"/> (1 left of the road's way), made when first asked for.</summary>
		private KerbSide KerbOf(int edge, int side)
		{
			int key = edge * 2 + (side > 0 ? 1 : 0);
			if (kerbs.TryGetValue(key, out KerbSide found))
			{
				return found;
			}
			Traffic.Edge e = Traffic.Edges[edge];
			KerbLimits(e, out double lo, out double hi);
			int n = hi > lo ? (int)Math.Floor((hi - lo) / KerbSpacing) : 0;
			double start = (lo + hi) * 0.5 - (n - 1) * KerbSpacing * 0.5;
			KerbSide k = new KerbSide { Edge = edge, Side = side > 0 ? 1 : -1, S = new double[n], Kept = new int[n], Stays = new List<(double, double)>[n] };
			Network.Road road = Network.Roads[e.Road];
			for (int i = 0; i < n; i++)
			{
				k.S[i] = start + i * KerbSpacing;
				k.Stays[i] = new List<(double, double)>();
				// A space over a house's drive pad, a cottage's or a farm's yard, reaching onto another road (at a junction's corner) or
				// over the water (on a bridge) is not a space.
				Spot spot = OnKerb(road, k.S[i], k.Side);
				bool home = Blocked(spot), house = !home && InHouse(spot, 0.0), other = !home && !house && !Clear(spot, e.Road);
				k.Kept[i] = home || house || other ? Struck : -1;
				KerbSpaces++;
				StruckHome += home ? 1 : 0;
				StruckHouse += house ? 1 : 0;
				StruckOther += other ? 1 : 0;
			}
			kerbs[key] = k;
			return k;
		}

		/// <summary>A kerb space that is never used (it is over a home's own parking).</summary>
		private const int Struck = -2;

		/// <summary>The homes' own spaces off the road (drive pads, cottages, farms), in cells of <see cref="CellSize"/>, for striking out kerb spaces over them.</summary>
		private readonly Dictionary<long, List<Spot>> fixedSpots = new Dictionary<long, List<Spot>>();
		private const double CellSize = 32.0;

		private static long Cell(double x, double z)
		{
			return ((long)Math.Floor(x / CellSize) << 32) ^ (uint)(int)Math.Floor(z / CellSize);
		}

		/// <summary>Keeps <paramref name="s"/> as a home's own space: no kerb space, nor another home's, is made over it.</summary>
		private void Hold(Spot s)
		{
			long key = Cell(s.X, s.Z);
			if (!fixedSpots.TryGetValue(key, out List<Spot> list))
			{
				list = new List<Spot>();
				fixedSpots[key] = list;
			}
			list.Add(s);
		}

		/// <summary>Whether a car at <paramref name="s"/> would overlap a home's own space.</summary>
		private bool Blocked(Spot s)
		{
			int gx = (int)Math.Floor(s.X / CellSize), gz = (int)Math.Floor(s.Z / CellSize);
			for (int ox = -1; ox <= 1; ox++)
			{
				for (int oz = -1; oz <= 1; oz++)
				{
					if (fixedSpots.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<Spot> list))
					{
						foreach (Spot f in list)
						{
							if (Overlap(s, f, 1.0))
							{
								return true;
							}
						}
					}
				}
			}
			return false;
		}

		/// <summary>A car's footprint for the overlap test (about 14 by 5.4 units).</summary>
		internal const double CarLong = 14.0, CarWide = 5.4;

		/// <summary>Whether two parked cars' footprints, each grown by <paramref name="margin"/> all round, overlap (two rectangles, separating axes).</summary>
		internal static bool Overlap(Spot a, Spot b, double margin)
		{
			double dx = b.X - a.X, dz = b.Z - a.Z;
			double l = CarLong + margin * 2.0, wd = CarWide + margin * 2.0;
			if (dx * dx + dz * dz > l * l + wd * wd)
			{
				return false;
			}
			foreach ((double ux, double uz) in new[] { (a.Hx, a.Hz), (a.Hz, -a.Hx), (b.Hx, b.Hz), (b.Hz, -b.Hx) })
			{
				double ra = Math.Abs(a.Hx * ux + a.Hz * uz) * l * 0.5 + Math.Abs(a.Hz * ux - a.Hx * uz) * wd * 0.5;
				double rb = Math.Abs(b.Hx * ux + b.Hz * uz) * l * 0.5 + Math.Abs(b.Hz * ux - b.Hx * uz) * wd * 0.5;
				if (Math.Abs(dx * ux + dz * uz) > ra + rb)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Where a stretch's kerb may hold cars: from a car's half length past the junction box at one end to the same short of the other.</summary>
		private static void KerbLimits(Traffic.Edge e, out double lo, out double hi)
		{
			lo = Math.Min(e.Sa, e.Sb) + Traffic.Nodes[e.Sa <= e.Sb ? e.A : e.B].Box + KerbSpacing * 0.5;
			hi = Math.Max(e.Sa, e.Sb) - Traffic.Nodes[e.Sa <= e.Sb ? e.B : e.A].Box - KerbSpacing * 0.5;
		}

		/// <summary>
		/// A kerb space for a stay from <paramref name="arrive"/> to <paramref name="leave"/> (or all day, for <paramref name="keeper"/> &gt;= 0),
		/// near anchor <paramref name="a"/>: the nearest free one on its side of its stretch, else on the other side; false if both are full.
		/// </summary>
		private bool TakeKerb(Traffic.Anchor a, double arrive, double leave, int keeper, out (KerbSide side, int k) got)
		{
			got = default;
			int side = a.Side >= 0 ? 1 : -1;
			// Its own stretch first, then the stretches of the same road either side, nearest first (a full street parks round the corner).
			foreach (int edge in Stretches(a))
			{
				foreach (int sd in new[] { side, -side })
				{
					KerbSide ks = KerbOf(edge, sd);
					int best = -1;
					double bestD = double.MaxValue;
					for (int i = 0; i < ks.S.Length; i++)
					{
						double d = Math.Abs(ks.S[i] - a.S);
						if (d >= bestD || ks.Kept[i] != -1 || (keeper >= 0 && ks.Stays[i].Count > 0))
						{
							continue;
						}
						bool free = true;
						foreach ((double ar, double lv) in ks.Stays[i])
						{
							if (ar < leave && arrive < lv)
							{
								free = false;
								break;
							}
						}
						if (free)
						{
							best = i;
							bestD = d;
						}
					}
					if (best >= 0)
					{
						if (keeper >= 0)
						{
							ks.Kept[best] = keeper;
						}
						else
						{
							ks.Stays[best].Add((arrive, leave));
						}
						got = (ks, best);
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>How far along its road (either way) a car looks for a kerb space when its own stretch is full.</summary>
		private const double KerbSearch = 260.0;

		/// <summary>The anchor's stretch, then the other stretches of its road within <see cref="KerbSearch"/> of it, nearest first.</summary>
		private static List<int> Stretches(Traffic.Anchor a)
		{
			List<(double d, int e)> found = new List<(double, int)> { (-1.0, a.Edge) };
			int road = Traffic.Edges[a.Edge].Road;
			foreach (int e in Traffic.RoadEdges[road])
			{
				if (e == a.Edge)
				{
					continue;
				}
				Traffic.Edge ed = Traffic.Edges[e];
				double lo = Math.Min(ed.Sa, ed.Sb), hi = Math.Max(ed.Sa, ed.Sb);
				double d = a.S < lo ? lo - a.S : a.S > hi ? a.S - hi : 0.0;
				if (d < KerbSearch)
				{
					found.Add((d, e));
				}
			}
			found.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.e.CompareTo(y.e));
			List<int> list = new List<int>(found.Count);
			foreach ((double _, int e) in found)
			{
				list.Add(e);
			}
			return list;
		}

		/// <summary>Whether a stay at place <paramref name="place"/> in space <paramref name="k"/> is at the kerb.</summary>
		private bool AtKerb(int place, int k)
		{
			if (place < 0)
			{
				// A visitor (own cars' stays at home are not given out here): at the kerb unless there is room off the road.
				return !OffRoad(-1 - place, k, out Spot _);
			}
			Traffic.Destination d = Traffic.Destinations[place];
			if (d.Kind == Traffic.DestinationKind.Edge)
			{
				return false;
			}
			return d.Lot < 0 || k >= LotSpaces(place).Count;
		}

		/// <summary>
		/// Gives every stay its space. First the terraces' cars their kerb spaces for the day (in the order of the households). Then, per
		/// place, stays in the order they arrive (ties by leg), each the lowest space free then; a household's own cars keep their own
		/// spaces at home (0 up), so visitors there take the spaces after them. Last, every stay whose space is at the kerb (a kerb place, a
		/// lot full, a visitor in town) the nearest free kerb space on its stretch, in the order they arrive.
		/// </summary>
		private void Assign()
		{
			for (int c = 0; c < day.Cars.Count; c++)
			{
				int h = day.Cars[c].Household;
				if (h >= 0 && !hasOwn[c])
				{
					if (TakeKerb(Traffic.Households[h].Home, 0.0, double.MaxValue, c, out (KerbSide, int) got))
					{
						kerbHome[c] = got;
					}
					else
					{
						KerbFull++;
					}
				}
			}
			Dictionary<int, List<(double arrive, double leave, int leg)>> stays = new Dictionary<int, List<(double, double, int)>>();
			for (int li = 0; li < day.Legs.Count; li++)
			{
				TrafficDay.Leg leg = day.Legs[li];
				if (leg.Start < 0.0)
				{
					continue;
				}
				int place = PlaceOf(leg);
				if (place == int.MinValue)
				{
					continue;
				}
				if (leg.Return && place < 0)
				{
					// Home: its own space.
					slot[li] = homeIndex[leg.Car];
					continue;
				}
				// It stays until the car's next leg leaves (or all day).
				double leave = double.MaxValue;
				bool next = false;
				foreach (int lj in day.Cars[leg.Car].Legs)
				{
					if (next && day.Legs[lj].Start >= 0.0)
					{
						leave = day.Legs[lj].Start;
						break;
					}
					next |= lj == li;
				}
				if (!stays.TryGetValue(place, out List<(double, double, int)> list))
				{
					list = new List<(double, double, int)>();
					stays[place] = list;
				}
				list.Add((leg.End, leave, li));
			}
			List<int> keys = new List<int>(stays.Keys);
			keys.Sort();
			List<double> freeAt = new List<double>();
			List<(double arrive, double leave, int leg)> toKerb = new List<(double, double, int)>();
			foreach (int place in keys)
			{
				List<(double arrive, double leave, int leg)> list = stays[place];
				list.Sort((a, b) => a.arrive != b.arrive ? a.arrive.CompareTo(b.arrive) : a.leg.CompareTo(b.leg));
				int first = place < 0 ? Traffic.Households[-1 - place].Cars : 0;
				int spaces = place >= 0 ? Traffic.Destinations[place].Spaces : int.MaxValue;
				freeAt.Clear();
				foreach ((double arrive, double leave, int leg) in list)
				{
					int k = 0;
					while (k < freeAt.Count && freeAt[k] > arrive)
					{
						k++;
					}
					if (k == freeAt.Count)
					{
						freeAt.Add(leave);
					}
					else
					{
						freeAt[k] = leave;
					}
					slot[leg] = first + k;
					int held = 0;
					foreach (double f in freeAt)
					{
						held += f > arrive ? 1 : 0;
					}
					Busiest = Math.Max(Busiest, held);
					Overflow += k >= spaces ? 1 : 0;
					if (AtKerb(place, first + k))
					{
						toKerb.Add((arrive, leave, leg));
					}
				}
			}
			toKerb.Sort((a, b) => a.arrive != b.arrive ? a.arrive.CompareTo(b.arrive) : a.leg.CompareTo(b.leg));
			foreach ((double arrive, double leave, int leg) in toKerb)
			{
				if (TakeKerb(day.Legs[leg].To, arrive, leave, -1, out (KerbSide, int) got))
				{
					kerbOf[leg] = got;
				}
				else
				{
					KerbFull++;
				}
			}
		}

		/// <summary>The spot of kerb space <paramref name="k"/> of side <paramref name="ks"/>.</summary>
		private static Spot KerbSpot(KerbSide ks, int k)
		{
			Network.Road road = Network.Roads[Traffic.Edges[ks.Edge].Road];
			return OnKerb(road, ks.S[k], ks.Side);
		}

		/// <summary>A car at the kerb of <paramref name="road"/> at road distance <paramref name="s"/> on side <paramref name="side"/>, facing the way traffic on that side goes (driving on the right).</summary>
		private static Spot OnKerb(Network.Road road, double s, int side)
		{
			Traffic.At(road, s, out double x, out double z, out double _);
			Traffic.Heading(road, s, out double tx, out double tz);
			// Left of the road's way is (-tz, tx); a car on the left faces against the road's way. Far enough out that a car driving past
			// in the lane clears it (the day's traffic does not see parked cars): on a town street two wheels up on the sidewalk. Never past
			// the sidewalk's back, where a terrace's front stands (on a narrow lane that leaves it touching a passing car's side).
			double off = Math.Min(Math.Max(road.Half - KerbIn, Traffic.LaneOffset(road) + CarWide + KerbClear), road.Half + Network.Verge - CarWide * 0.5 - 0.1);
			return new Spot { X = x - tz * off * side, Z = z + tx * off * side, Hx = tx * -side, Hz = tz * -side };
		}

		/// <summary>Where car <paramref name="car"/> stands parked after leg <paramref name="leg"/> (-1: at home, before any trip).</summary>
		internal Spot Parked(int car, int leg)
		{
			if (leg < 0)
			{
				TrafficDay.Car c = day.Cars[car];
				if (c.Household < 0)
				{
					return new Spot { Away = true };
				}
				if (kerbHome.TryGetValue(car, out (KerbSide side, int k) own))
				{
					return KerbSpot(own.side, own.k);
				}
				// A car with no room at the kerb anywhere near is parked out of sight round the corner: not drawn.
				return hasOwn[car] ? ownSpot[car] : new Spot { Away = true };
			}
			TrafficDay.Leg l = day.Legs[leg];
			int place = PlaceOf(l);
			if (place == int.MinValue || slot[leg] < 0)
			{
				return new Spot { Away = true };
			}
			if (l.Return && place < 0)
			{
				return Parked(car, -1);
			}
			if (kerbOf.TryGetValue(leg, out (KerbSide side, int k) kerb))
			{
				return KerbSpot(kerb.side, kerb.k);
			}
			if (AtKerb(place, slot[leg]))
			{
				// No room at the kerb: parked out of sight.
				return new Spot { Away = true };
			}
			if (place < 0)
			{
				return OffRoad(-1 - place, slot[leg], out Spot visit) ? visit : new Spot { Away = true };
			}
			return AtDestination(place, slot[leg]);
		}

		/// <summary>For the checks: whether car <paramref name="car"/> parked after leg <paramref name="leg"/> is at the kerb (and on which road).</summary>
		internal bool IsKerb(int car, int leg, out int road)
		{
			road = -1;
			(KerbSide side, int k) got;
			bool kerb = leg < 0 || (day.Legs[leg].Return && PlaceOf(day.Legs[leg]) < 0) ? kerbHome.TryGetValue(car, out got) : kerbOf.TryGetValue(leg, out got);
			if (kerb)
			{
				road = Traffic.Edges[got.side.Edge].Road;
			}
			return kerb;
		}

		/// <summary>For the checks: how a parked car's space was given (kerb side and space, or place and slot).</summary>
		internal string Describe(int car, int leg)
		{
			if (leg < 0 || (day.Legs[leg].Return && PlaceOf(day.Legs[leg]) < 0))
			{
				if (kerbHome.TryGetValue(car, out (KerbSide side, int k) own))
				{
					return "home kerb e" + own.side.Edge + " side " + own.side.Side + " #" + own.k + "/" + own.side.S.Length;
				}
				return "home " + homeIndex[car];
			}
			if (kerbOf.TryGetValue(leg, out (KerbSide side, int k) kerb))
			{
				return "kerb e" + kerb.side.Edge + " side " + kerb.side.Side + " #" + kerb.k + "/" + kerb.side.S.Length;
			}
			return "place " + PlaceOf(day.Legs[leg]) + " slot " + slot[leg];
		}

		/// <summary>Where the car stood before leg <paramref name="leg"/> left (the spot it pulls out of).</summary>
		internal Spot Before(int leg)
		{
			TrafficDay.Leg l = day.Legs[leg];
			return l.Appears ? new Spot { Away = true } : Parked(l.Car, before[leg]);
		}

		/// <summary>
		/// Space <paramref name="k"/> at household <paramref name="h"/>'s home off the road (its own cars first, then visitors): on a
		/// house's drive pad, by a cottage's drive, in a farm's yard; the first of a few places to try that is clear of every road's paving,
		/// the water and the lots' buildings. False if none is (the car goes to the kerb), and always for a terrace.
		/// </summary>
		internal bool OffRoad(int h, int k, out Spot spot)
		{
			Traffic.Household hh = Traffic.Households[h];
			Traffic.Anchor a = hh.Home;
			Network.Road road = RoadOf(a, out double px, out double pz, out double tx, out double tz);
			Away(a, tx, tz, out double nx, out double nz);
			double along = (a.X - px) * tx + (a.Z - pz) * tz, off = (a.X - px) * nx + (a.Z - pz) * nz;
			spot = default;
			switch (hh.Kind)
			{
				case Traffic.HomeKind.House:
				{
					if (k >= hh.Cars)
					{
						return false;
					}
					// On the pad beside the house, nose in, its tail clear of the street and its sidewalk; a second car behind the first.
					// Beside the house on the other side if that one is taken by a road or the water.
					double back = Math.Max(off, road.Half + Network.Shoulder + CarLong * 0.5 + 1.0) + BayLength * k;
					foreach (double u in new[] { along, -along })
					{
						spot = new Spot { X = px + tx * u + nx * back, Z = pz + tz * u + nz * back, Hx = nx, Hz = nz };
						if (Fine(spot) && !Blocked(spot))
						{
							return true;
						}
					}
					return false;
				}
				case Traffic.HomeKind.Cottage:
				{
					// Side by side by the end of the drive, facing along it, each further from the drive than the last; or the other side.
					double dx = a.X - px, dz = a.Z - pz;
					double d = Math.Sqrt(dx * dx + dz * dz);
					if (d < 1e-6)
					{
						dx = nx;
						dz = nz;
						d = 9.0;
					}
					dx /= d;
					dz /= d;
					foreach (double sd in new[] { 1.0, -1.0 })
					{
						double r = d + BayWidth * 1.2 * k;
						spot = new Spot { X = px + dx * r * sd, Z = pz + dz * r * sd, Hx = tx, Hz = tz };
						if (Fine(spot) && !Blocked(spot))
						{
							return true;
						}
					}
					return false;
				}
				case Traffic.HomeKind.Farm:
				{
					// In the yard off the road towards the farm, facing it, side by side; further in if the near row is taken.
					double fx = a.X - px, fz = a.Z - pz;
					double d = Math.Sqrt(fx * fx + fz * fz);
					if (d < 1e-6)
					{
						fx = nx;
						fz = nz;
					}
					else
					{
						fx /= d;
						fz /= d;
					}
					foreach (double extra in new[] { 14.0, 32.0, 50.0 })
					{
						double o = road.Half + Network.Shoulder + extra, u = Spread(k) * BayWidth * 1.2;
						spot = new Spot { X = px + fx * o + tx * u, Z = pz + fz * o + tz * u, Hx = fx, Hz = fz };
						if (Fine(spot) && !Blocked(spot))
						{
							return true;
						}
					}
					return false;
				}
				default:
					return false;
			}
		}

		/// <summary>Whether a parked car at <paramref name="s"/> is clear of every road's paving, the water and the lots' buildings (its corners and middle).</summary>
		internal static bool Fine(Spot s)
		{
			return Clear(s, -1);
		}

		/// <summary>As <see cref="Fine"/> (and out of the houses), its own road <paramref name="own"/> left out (a car at the kerb stands on it).</summary>
		internal static bool Clear(Spot s, int own)
		{
			// At the kerb a car may come right up to a terrace's front (behind the sidewalk); elsewhere a little room is kept.
			if (InHouse(s, own >= 0 ? 0.0 : 0.5))
			{
				return false;
			}
			double ax = s.Hx * CarLong * 0.5, az = s.Hz * CarLong * 0.5, bx = s.Hz * CarWide * 0.5, bz = -s.Hx * CarWide * 0.5;
			foreach ((double u, double v) in Corners)
			{
				double x = s.X + ax * u + bx * v, z = s.Z + az * u + bz * v;
				if (Network.OnOtherPaving(x, z, own) || Rivers.Water(x, z, out double _))
				{
					return false;
				}
				Town.Lot lot = Town.At(x, z, out bool building);
				if (lot != null && building)
				{
					return false;
				}
			}
			return true;
		}

		private static readonly (double u, double v)[] Corners = { (1.0, 1.0), (1.0, -1.0), (-1.0, 1.0), (-1.0, -1.0), (0.0, 0.0) };

		/// <summary>
		/// The houses (the plots <see cref="Plots"/> keeps that fit, as <see cref="Buildings"/> draws them; towers have their own footprint
		/// and stand back from the kerb): middle, the way along the road, half width along it and half depth across, in cells of
		/// <see cref="HouseCell"/>. Fixed with the world: made once.
		/// </summary>
		private static Dictionary<long, List<(double x, double z, double ux, double uz, double hw, double hd)>> houses;
		private static readonly object housesGate = new object();
		private const double HouseCell = 64.0;

		private static void EnsureHouses()
		{
			lock (housesGate)
			{
				if (houses != null)
				{
					return;
				}
				Dictionary<long, List<(double, double, double, double, double, double)>> grid = new Dictionary<long, List<(double, double, double, double, double, double)>>();
				List<Plots.Plot> plots = new List<Plots.Plot>();
				for (int r = 0; r < Network.Roads.Count; r++)
				{
					Network.Road road = Network.Roads[r];
					for (int i = 0; i + 1 < road.Count; i++)
					{
						plots.Clear();
						Plots.Along(r, road, i, plots);
						foreach (Plots.Plot p in plots)
						{
							if (!Plots.Fits(p))
							{
								continue;
							}
							long key = ((long)Math.Floor(p.Cx / HouseCell) << 32) ^ (uint)(int)Math.Floor(p.Cz / HouseCell);
							if (!grid.TryGetValue(key, out List<(double, double, double, double, double, double)> list))
							{
								list = new List<(double, double, double, double, double, double)>();
								grid[key] = list;
							}
							list.Add((p.Cx, p.Cz, p.Dx, p.Dz, p.Width * 0.5, p.Depth * 0.5));
						}
					}
				}
				houses = grid;
			}
		}

		/// <summary>Whether a car at <paramref name="s"/>, grown by <paramref name="margin"/> all round, would stand in a house.</summary>
		internal static bool InHouse(Spot s, double margin)
		{
			int gx = (int)Math.Floor(s.X / HouseCell), gz = (int)Math.Floor(s.Z / HouseCell);
			for (int ox = -1; ox <= 1; ox++)
			{
				for (int oz = -1; oz <= 1; oz++)
				{
					if (!houses.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<(double x, double z, double ux, double uz, double hw, double hd)> list))
					{
						continue;
					}
					foreach ((double x, double z, double ux, double uz, double hw, double hd) h in list)
					{
						double dx = h.x - s.X, dz = h.z - s.Z;
						bool apart = false;
						foreach ((double ax, double az) in new[] { (s.Hx, s.Hz), (s.Hz, -s.Hx), (h.ux, h.uz), (h.uz, -h.ux) })
						{
							double ra = Math.Abs(s.Hx * ax + s.Hz * az) * (CarLong * 0.5 + margin) + Math.Abs(s.Hz * ax - s.Hx * az) * (CarWide * 0.5 + margin);
							double rb = Math.Abs(h.ux * ax + h.uz * az) * h.hw + Math.Abs(h.uz * ax - h.ux * az) * h.hd;
							if (Math.Abs(dx * ax + dz * az) > ra + rb)
							{
								apart = true;
								break;
							}
						}
						if (!apart)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>Space <paramref name="k"/> at destination <paramref name="d"/>: its lot's spaces, then along the kerb.</summary>
		internal Spot AtDestination(int d, int k)
		{
			Traffic.Destination dest = Traffic.Destinations[d];
			if (dest.Kind == Traffic.DestinationKind.Edge)
			{
				return new Spot { Away = true };
			}
			if (dest.Lot < 0)
			{
				return Kerb(dest.At, k);
			}
			List<(double u, double v, double nu, double nv)> spaces = LotSpaces(d);
			if (k >= spaces.Count)
			{
				return Kerb(dest.At, k - spaces.Count);
			}
			return LotSpot(Town.Lots[dest.Lot], spaces[k]);
		}

		/// <summary>A lot-frame space as a spot in the world.</summary>
		private static Spot LotSpot(Town.Lot lot, (double u, double v, double nu, double nv) s)
		{
			Town.World(lot, s.u, s.v, out double x, out double z);
			// Along the lot is (Dx, Dz), away from the road (-Dz, Dx).
			double hx = lot.Dx * s.nu - lot.Dz * s.nv, hz = lot.Dz * s.nu + lot.Dx * s.nv;
			return new Spot { X = x, Z = z, Hx = hx, Hz = hz };
		}

		/// <summary>A destination's spaces on its lot, made once.</summary>
		internal List<(double u, double v, double nu, double nv)> LotSpaces(int d)
		{
			if (lotSpaces.TryGetValue(d, out List<(double, double, double, double)> found))
			{
				return found;
			}
			List<(double u, double v, double nu, double nv)> list = new List<(double, double, double, double)>();
			Town.Lot lot = Town.Lots[Traffic.Destinations[d].Lot];
			Spaces(lot, list);
			// Those a road's paving, the water or a building reaches into (a yard cut by a road's corner) are left out.
			list.RemoveAll(sp => !Fine(LotSpot(lot, sp)));
			lotSpaces[d] = list;
			return list;
		}

		/// <summary>
		/// A lot's parking spaces in its own frame (u along the road, v away from it), nearest the door first: the bays as
		/// <see cref="Buildings"/> paints them on a shop's and the retail park's car park; rows on the surface of a workshop's yard, a
		/// filling station's forecourt (at the pumps first), a market place and a course's yard.
		/// </summary>
		internal static void Spaces(Town.Lot lot, List<(double u, double v, double nu, double nv)> list)
		{
			list.Clear();
			double half = lot.W * 0.5;
			Town.Building(lot.Kind, out double bw, out double bv, out double _);
			switch (lot.Kind)
			{
				case LotKind.Retail:
				{
					// Two blocks of bays each side of a middle island, in two double rows parked nose to nose (Buildings.BayRows): the
					// rows nearest the store first.
					double block = Math.Floor((half - 13.0) / BayWidth) * BayWidth;
					int across = (int)Math.Round(block / BayWidth);
					foreach (double v0 in new[] { 64.0, 14.0 })
					{
						for (int row = 1; row >= 0; row--)
						{
							double a = v0 + row * BayLength;
							// The first row's noses at its back line (v1), the second's at its front (v0).
							double nv = row == 0 ? 1.0 : -1.0;
							for (int i = 0; i < across; i++)
							{
								double inner = 3.0 + BayWidth * (i + 0.5);
								list.Add((-inner, a + BayLength * 0.5, 0.0, nv));
								list.Add((inner, a + BayLength * 0.5, 0.0, nv));
							}
						}
					}
					break;
				}
				case LotKind.Shop:
				{
					// One row of bays in front of the shop, noses towards it.
					double block = Math.Floor((lot.W - 8.0) / BayWidth) * BayWidth;
					int across = (int)Math.Round(block / BayWidth);
					for (int i = 0; i < across; i++)
					{
						list.Add((-block * 0.5 + BayWidth * (i + 0.5), 8.0 + BayLength * 0.5, 0.0, 1.0));
					}
					list.Sort((a, b) => Math.Abs(a.u).CompareTo(Math.Abs(b.u)));
					break;
				}
				case LotKind.Workshop:
					// A row along the front of the shed, noses to it, the yard in front left to drive in.
					Row(list, half, 0.0, bv - BayLength * 0.5 - 1.0, 1.0);
					break;
				case LotKind.Garage:
				{
					// At the pumps (under the canopy, either side of the pump island), then along the front of the shop.
					list.Add((-14.0, 20.0, 0.0, 1.0));
					list.Add((14.0, 20.0, 0.0, 1.0));
					Row(list, half, bw * 0.5 + 2.0, bv - BayLength * 0.5 - 1.0, 1.0);
					break;
				}
				case LotKind.Market:
					// Along the front of the market place, short of the stalls round the cross.
					Row(list, half, 0.0, BayLength * 0.5 + 2.0, 1.0);
					break;
				case LotKind.Service:
				case LotKind.Paddock:
					// A row across the middle of the yard, in front of the vans along the back.
					Row(list, half, 0.0, Math.Max(BayLength, lot.D * 0.35), 1.0);
					break;
			}
		}

		/// <summary>A row of spaces side by side across a lot at depth <paramref name="v"/>, within its sides, each side of a gap of half width <paramref name="gap"/> round the middle, nearest the middle first.</summary>
		private static void Row(List<(double u, double v, double nu, double nv)> list, double half, double gap, double v, double nv)
		{
			double pitch = BayWidth * 1.1;
			for (double inner = gap + pitch * 0.5; inner <= half - 4.0 - pitch * 0.5; inner += pitch)
			{
				list.Add((-inner, v, 0.0, nv));
				list.Add((inner, v, 0.0, nv));
			}
		}

		/// <summary>
		/// Space <paramref name="k"/> along the kerb on an anchor's side of its road: in front of the anchor first, then a car's length
		/// either way in turn, kept clear of the junctions at the stretch's ends (past them, on the other side of the road). Cars face
		/// the way traffic on that side goes (driving on the right).
		/// </summary>
		internal static Spot Kerb(Traffic.Anchor a, int k)
		{
			Traffic.Edge e = Traffic.Edges[a.Edge];
			Network.Road road = Network.Roads[e.Road];
			KerbLimits(e, out double lo, out double hi);
			int side = a.Side >= 0 ? 1 : -1;
			double s = a.S + Spread(k) * KerbSpacing;
			if (hi > lo && (s < lo || s > hi))
			{
				// Round the stretch on the other side.
				side = -side;
				s = s < lo ? lo + (lo - s) : hi - (s - hi);
			}
			s = hi > lo ? Math.Max(lo, Math.Min(hi, s)) : (lo + hi) * 0.5;
			return OnKerb(road, s, side);
		}

		/// <summary>0, 1, -1, 2, -2...: spaces handed out from the middle outwards.</summary>
		private static int Spread(int k)
		{
			return (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
		}

		/// <summary>An anchor's road, its point and way (unit) at the anchor.</summary>
		private static Network.Road RoadOf(Traffic.Anchor a, out double px, out double pz, out double tx, out double tz)
		{
			Network.Road road = Network.Roads[Traffic.Edges[a.Edge].Road];
			Traffic.At(road, a.S, out px, out pz, out double _);
			Traffic.Heading(road, a.S, out tx, out tz);
			return road;
		}

		/// <summary>The way away from the road on the anchor's side (unit): left of the road's way for side 1.</summary>
		private static void Away(Traffic.Anchor a, double tx, double tz, out double nx, out double nz)
		{
			int side = a.Side >= 0 ? 1 : -1;
			nx = -tz * side;
			nz = tx * side;
		}
	}
}
