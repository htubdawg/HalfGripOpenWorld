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
	/// The day asks for each trip's space as the trip sets off (<see cref="Take"/>) and starts and ends the trip on the lane alongside its
	/// space, so a car drawn pulling in or out stands where the day has it and never on lane the day thinks free. So the choosing is part
	/// of the day: plain arithmetic in the day's order (the drawing below may use floats and trigonometry). No Unity types
	/// (<c>tools/trafficview</c> checks it).
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

		/// <summary>Spacing of cars parked one behind the other along a kerb (a car and a gap: about 5.8 m, room to pull in and out).</summary>
		internal const double KerbSpacing = 19.0;

		/// <summary>How far in from the edge of the paving the middle of a car at the kerb is (half a car and a little), on a road wide
		/// enough; and the room left between it and a car driving past in the lane.</summary>
		internal const double KerbIn = 3.5, KerbClear = 0.4;

		private readonly TrafficDay day;

		/// <summary>Per leg: the space it ends in at its place (-1: it ends off the map, or never ran).</summary>
		private readonly List<int> slot = new List<int>();

		/// <summary>Per car: its place in its household's cars (its space at home); per leg, the leg before it of its car (-1: none).</summary>
		private readonly int[] homeIndex;
		private readonly List<int> before = new List<int>();

		/// <summary>Per car: the last leg it set off on (-1: none yet), so where it stands now.</summary>
		private readonly int[] last;

		/// <summary>Per place: when each of its spaces comes free (<see cref="double.MaxValue"/> while a car holds it).</summary>
		private readonly Dictionary<int, List<double>> freeAt = new Dictionary<int, List<double>>();

		/// <summary>Per leg ending at the kerb: the stay's index in its space's stays, to end it when the car leaves.</summary>
		private readonly Dictionary<int, int> kerbStay = new Dictionary<int, int>();

		/// <summary>Per destination with a lot: its spaces on the lot (lot-frame u, v and the way the nose faces along v).</summary>
		private readonly Dictionary<int, List<(double u, double v, double nu, double nv)>> lotSpaces = new Dictionary<int, List<(double, double, double, double)>>();

		/// <summary>Per car: its own space at home off the road (<see cref="OffRoad"/>), and whether it has one (else the kerb, as a terrace's).</summary>
		private readonly Spot[] ownSpot;
		private readonly bool[] hasOwn;

		/// <summary>The most cars any place held at once over the day, and how many stays carried on along the kerb past a place's spaces.</summary>
		internal int Busiest, Overflow;

		/// <summary>
		/// The homes' spaces, made once the day's cars are known and before it is driven: each household's own cars' spaces off the road,
		/// then the terraces' first cars' kerb spaces. The rest is given out as the day runs (<see cref="Take"/>).
		/// </summary>
		internal TrafficParking(TrafficDay day)
		{
			this.day = day;
			EnsureHouses();
			homeIndex = new int[day.Cars.Count];
			ownSpot = new Spot[day.Cars.Count];
			hasOwn = new bool[day.Cars.Count];
			last = new int[day.Cars.Count];
			int lastHousehold = -1, n = 0;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Car car = day.Cars[c];
				n = car.Household == lastHousehold ? n + 1 : 0;
				lastHousehold = car.Household;
				homeIndex[c] = n;
				last[c] = -1;
				if (car.Household >= 0)
				{
					// Taken one after another, each clear of those before (two neighbours' pads can meet between their houses).
					hasOwn[c] = OffRoad(car.Household, n, out ownSpot[c]);
					if (hasOwn[c])
					{
						Hold(ownSpot[c]);
					}
				}
			}
			for (int c = 0; c < day.Cars.Count; c++)
			{
				int h = day.Cars[c].Household;
				// A household's first car at the kerb by its door; any more are parked round the back, out of sight (a terrace street
				// lined with every household's every car is far busier than a real one).
				if (h >= 0 && !hasOwn[c] && homeIndex[c] == 0)
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
		}

		/// <summary>Where car <paramref name="car"/> stands now, between trips (the day asks as a trip waits to set off).</summary>
		internal Spot Here(int car)
		{
			return Parked(car, last[car]);
		}

		/// <summary>
		/// Leg <paramref name="li"/> sets off at sim time <paramref name="w"/>: the space it leaves comes free a little later, and the
		/// space it will end in is taken now and held until it leaves that (a place's lowest free space, the nearest free kerb space by
		/// its door if that is at the kerb). Returns where it will park (<see cref="Spot.Away"/> off the map or out of sight).
		/// </summary>
		internal Spot Take(int li, double w)
		{
			while (slot.Count <= li)
			{
				slot.Add(-1);
				before.Add(-1);
			}
			TrafficDay.Leg leg = day.Legs[li];
			int prev = last[leg.Car];
			before[li] = prev;
			last[leg.Car] = li;
			if (prev >= 0)
			{
				Release(prev, w + Clearing);
			}
			int place = PlaceOf(leg);
			if (place == int.MinValue)
			{
				return new Spot { Away = true };
			}
			if (leg.Return && place < 0)
			{
				// Home: its own space.
				slot[li] = homeIndex[leg.Car];
				return Parked(leg.Car, li);
			}
			if (!freeAt.TryGetValue(place, out List<double> spaces))
			{
				spaces = new List<double>();
				freeAt[place] = spaces;
			}
			int first = place < 0 ? Traffic.Households[-1 - place].Cars : 0;
			int k = 0;
			while (k < spaces.Count && spaces[k] > w)
			{
				k++;
			}
			if (k == spaces.Count)
			{
				spaces.Add(double.MaxValue);
			}
			else
			{
				spaces[k] = double.MaxValue;
			}
			slot[li] = first + k;
			int held = 0;
			foreach (double f in spaces)
			{
				held += f > w ? 1 : 0;
			}
			Busiest = Math.Max(Busiest, held);
			Overflow += place >= 0 && k >= Traffic.Destinations[place].Spaces ? 1 : 0;
			if (AtKerb(place, first + k))
			{
				// The kerb on the side the car arrives on first (driving on the right, the right of its way): a space across the road means
				// a U-turn through the oncoming lane, which the day does not see.
				TrafficDay.Seg end = leg.Path[leg.Path.Length - 1];
				int own = end.Edge == leg.To.Edge ? -end.Dir : 0;
				if (TakeKerb(leg.To, w, double.MaxValue, -1, out (KerbSide side, int k) got, own))
				{
					kerbOf[li] = got;
					kerbStay[li] = got.side.Stays[got.k].Count - 1;
				}
				else
				{
					KerbFull++;
				}
			}
			return Parked(leg.Car, li);
		}

		/// <summary>
		/// How long after a car sets off its space stays held: it is drawn pulling out over the first stretch of its trip, and the next
		/// car's whole trip to the space (it takes it as it sets off) comes after this.
		/// </summary>
		private const double Clearing = PullMost + 10.0;

		/// <summary>The space leg <paramref name="li"/> ended in comes free at <paramref name="at"/>.</summary>
		private void Release(int li, double at)
		{
			TrafficDay.Leg leg = day.Legs[li];
			int place = PlaceOf(leg);
			if (place == int.MinValue || slot[li] < 0 || (leg.Return && place < 0))
			{
				return;
			}
			int first = place < 0 ? Traffic.Households[-1 - place].Cars : 0;
			freeAt[place][slot[li] - first] = at;
			if (kerbOf.TryGetValue(li, out (KerbSide side, int k) kerb))
			{
				List<(double arrive, double leave)> stays = kerb.side.Stays[kerb.k];
				int i = kerbStay[li];
				stays[i] = (stays[i].arrive, at);
			}
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
			// Only the town streets and the secondary roads have parking at the kerb: a main road keeps its traffic moving, a narrow lane
			// has no room for a car to pass one parked.
			RoadKind kind = Network.Roads[e.Road].Kind;
			int n = hi > lo && (kind == RoadKind.Secondary || kind == RoadKind.Street) ? (int)Math.Floor((hi - lo) / KerbSpacing) : 0;
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
		private bool TakeKerb(Traffic.Anchor a, double arrive, double leave, int keeper, out (KerbSide side, int k) got, int prefer = 0)
		{
			got = default;
			int side = prefer != 0 ? prefer : a.Side >= 0 ? 1 : -1;
			// Its own stretch only, within a short walk of the door: a car with no space there is parked round the back, out of sight.
			foreach (int edge in new[] { a.Edge })
			{
				foreach (int sd in new[] { side, -side })
				{
					KerbSide ks = KerbOf(edge, sd);
					int best = -1;
					double bestD = double.MaxValue;
					for (int i = 0; i < ks.S.Length; i++)
					{
						double d = Math.Abs(ks.S[i] - a.S);
						if (d >= bestD || d > KerbNear || ks.Kept[i] != -1 || (keeper >= 0 && ks.Stays[i].Count > 0))
						{
							continue;
						}
						bool free = true;
						foreach ((double ar, double lv) in ks.Stays[i])
						{
							if (ar - Pad < leave && arrive < lv + Pad)
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

		/// <summary>Seconds a kerb space is kept clear either side of a stay.</summary>
		private const double Pad = 5.0;

		/// <summary>How far along the kerb from the door (either way) a car looks for a space.</summary>
		private const double KerbNear = 60.0;

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
			double half = Network.Widened(road, s), wide = half - road.Half;
			double off = Math.Min(Math.Max(half - KerbIn, Traffic.LaneOffset(road) + wide + CarWide + KerbClear), half + Network.Verge - CarWide * 0.5 - 0.1);
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
			if (place == int.MinValue || leg >= slot.Count || slot[leg] < 0)
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

		/// <summary>
		/// If car <paramref name="car"/> parked after leg <paramref name="leg"/> (-1: at home before any trip) is at the kerb: the stretch,
		/// the road distance of its space and the side (1 left of the road's way). For pulling in and out alongside the space.
		/// </summary>
		internal bool KerbPlace(int car, int leg, out int edge, out double s, out int side)
		{
			edge = -1;
			s = 0.0;
			side = 0;
			(KerbSide side, int k) got;
			bool kerb = leg < 0 || (day.Legs[leg].Return && PlaceOf(day.Legs[leg]) < 0) ? kerbHome.TryGetValue(car, out got) : kerbOf.TryGetValue(leg, out got);
			if (!kerb)
			{
				return false;
			}
			edge = got.side.Edge;
			s = got.side.S[got.k];
			side = got.side.Side;
			return true;
		}

		/// <summary>The longest a car is drawn pulling out of or into its space (seconds): a U-turn and a long way off the road.</summary>
		internal const double PullMost = CurveMost + TurnTime;

		/// <summary>The curve between space and lane takes 1.5 s and a sixth of a second a unit apart, within these; a U-turn this long more.</summary>
		private const double CurveLeast = 2.0, CurveMost = 5.0, TurnTime = 2.5;

		/// <summary>
		/// How long a car takes to pull out of <paramref name="spot"/> onto the lane at path distance <paramref name="p"/> of
		/// <paramref name="leg"/>, or in: the day holds it still on the lane that long (TrafficDay), and it is drawn moving between the two
		/// meanwhile. 0 off the road (a tunnel's end). Part of the day: plain arithmetic.
		/// </summary>
		internal static double PullTime(Spot spot, TrafficDay.Leg leg, double p)
		{
			TrafficDay.Place at = default;
			TrafficDay.OnPath(leg, p, ref at);
			if (at.Edge < 0)
			{
				return 0.0;
			}
			bool across = spot.Hx * at.Hx + spot.Hz * at.Hz < -0.3;
			double off = LaneOf(leg, p);
			double bx = across ? at.X - at.Hz * off * 2.0 : at.X, bz = across ? at.Z + at.Hx * off * 2.0 : at.Z;
			double sx = spot.X - bx, sz = spot.Z - bz;
			double side = Math.Sqrt(sx * sx + sz * sz);
			return Math.Max(CurveLeast, Math.Min(CurveMost, 1.5 + side / 6.0)) + (across ? TurnTime : 0.0);
		}

		/// <summary>Whether <paramref name="spot"/> faces against the way <paramref name="leg"/> goes at <paramref name="p"/>: it is drawn turning round through the far lane.</summary>
		internal static bool Backwards(Spot spot, TrafficDay.Leg leg, double p)
		{
			TrafficDay.Place at = default;
			TrafficDay.OnPath(leg, p, ref at);
			return at.Edge >= 0 && spot.Hx * at.Hx + spot.Hz * at.Hz < -0.3;
		}

		/// <summary>Whether a driving car is pulling out of its space at the start of its trip, or into one at its end.</summary>
		internal bool InPull(TrafficDay.Place at, out bool start)
		{
			TrafficDay.Leg leg = day.Legs[at.Leg];
			start = !leg.Appears && at.T < leg.PullOut;
			return start || (!leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn);
		}

		/// <summary>
		/// Where a driving car is drawn: its place on the day's path, except over the first and last few seconds of a trip, where it pulls
		/// out of its space and into one. The day starts and ends each trip on the lane alongside its space (TrafficDay.MoveEnd), so the
		/// car moves between its space and where the day has it at that moment: never onto lane behind or ahead of it that the day thinks
		/// free (another car may stand there).
		/// </summary>
		internal void Drawn(int car, TrafficDay.Place at, out double x, out double z, out double hx, out double hz)
		{
			x = at.X;
			z = at.Z;
			hx = at.Hx;
			hz = at.Hz;
			if (at.State != TrafficDay.State.Driving || at.Leg < 0)
			{
				return;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			Corner(leg, at.P, ref x, ref z, ref hx, ref hz);
			if (!leg.Appears && at.T < leg.PullOut && Pull(car, leg, before[at.Leg], at, true, ref x, ref z, ref hx, ref hz))
			{
				return;
			}
			if (!leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn)
			{
				Pull(car, leg, at.Leg, at, false, ref x, ref z, ref hx, ref hz);
			}
		}

		/// <summary>
		/// Through a junction: within its box the car is drawn on a curve from its lane where it comes into the box to its lane where it
		/// leaves, not along the roads' lines to the junction's middle and out (which jumps sideways by the two lanes' offsets and snaps
		/// round at the middle). The day's junctions reckon movements by these same chords, so cars it lets through together are drawn
		/// clear of each other. Drawing only.
		/// </summary>
		internal static void Corner(TrafficDay.Leg leg, double p, ref double x, ref double z, ref double hx, ref double hz)
		{
			TrafficDay.Seg[] path = leg.Path;
			// The nearest junction along the path: between seg j - 1 and seg j, j the first stretch after the one p is on (by binary
			// search: a circling car's leg is the whole day, about 250 stretches, and this runs for every drawn car every frame and many
			// times a physics step for every physics car).
			int j = Math.Max(1, TrafficDrive.SegAt(path, p) + 1);
			if (j < path.Length && j > 1 && p - path[j - 1].P0 < path[j].P0 - p)
			{
				j--;
			}
			if (j >= path.Length || path[j - 1].Edge < 0 || path[j].Edge < 0 || path[j - 1].Edge == path[j].Edge)
			{
				return;
			}
			TrafficDay.Seg a = path[j - 1];
			Traffic.Edge e = Traffic.Edges[a.Edge];
			int node = a.Dir > 0 ? e.B : e.A;
			double r = Math.Min(Traffic.Nodes[node].Box, Math.Min(a.Len, path[j].Len) * 0.5);
			double pj = path[j].P0;
			if (r < 1.0 || Math.Abs(p - pj) >= r)
			{
				return;
			}
			TrafficDay.Place pa = default, pb = default;
			TrafficDay.OnPath(leg, pj - r, ref pa);
			TrafficDay.OnPath(leg, pj + r, ref pb);
			// The bend's corner: where the lane in, carried on, meets the lane out, carried back (the middle, going straight on).
			double cx = (pa.X + pb.X) * 0.5, cz = (pa.Z + pb.Z) * 0.5;
			double cross = pa.Hx * pb.Hz - pa.Hz * pb.Hx;
			if (Math.Abs(cross) > 0.05)
			{
				double t = ((pb.X - pa.X) * pb.Hz - (pb.Z - pa.Z) * pb.Hx) / cross;
				if (t > 0.0 && t < 2.0 * r)
				{
					cx = pa.X + pa.Hx * t;
					cz = pa.Z + pa.Hz * t;
				}
			}
			double k = (p - (pj - r)) / (2.0 * r), u = 1.0 - k;
			x = u * u * pa.X + 2.0 * u * k * cx + k * k * pb.X;
			z = u * u * pa.Z + 2.0 * u * k * cz + k * k * pb.Z;
			double dx = u * (cx - pa.X) + k * (pb.X - cx), dz = u * (cz - pa.Z) + k * (pb.Z - cz);
			double len = Math.Sqrt(dx * dx + dz * dz);
			if (len > 1e-6)
			{
				hx = dx / len;
				hz = dz / len;
			}
		}

		/// <summary>
		/// One end's pull (<paramref name="start"/>: out of the space the car was in after leg <paramref name="parkedAfter"/>); false if
		/// the car is not within it. The day holds the car still on its lane beside the space for <see cref="PullTime"/> (the cars behind
		/// wait for it), so it is drawn moving between the space and that one spot, timed from the space (q from 0 there to 1 in the
		/// lane): a curve, arriving along the way the space faces (a kerb space straight sideways: there is too little room between the
		/// cars to angle in or out); a space across the road (facing the other way) also has a U-turn between the far lane and its own,
		/// which the day makes only with the oncoming lane clear. Drawing only (floats and trigonometry are fine).
		/// </summary>
		private bool Pull(int car, TrafficDay.Leg leg, int parkedAfter, TrafficDay.Place at, bool start, ref double x, ref double z, ref double hx, ref double hz)
		{
			Spot spot = Parked(car, parkedAfter);
			if (spot.Away)
			{
				return false;
			}
			double off = LaneOf(leg, at.P);
			double facing = spot.Hx * at.Hx + spot.Hz * at.Hz;
			bool across = facing < -0.3;
			bool parallel = Math.Abs(facing) > 0.7;
			// The lane end of the curve: where the day has the car (in the far lane, facing the other way, before or after a U-turn).
			double bx = at.X, bz = at.Z;
			if (across)
			{
				bx -= at.Hz * off * 2.0;
				bz += at.Hx * off * 2.0;
			}
			double sx = spot.X - bx, sz = spot.Z - bz;
			double side = Math.Sqrt(sx * sx + sz * sz);
			double total = start ? leg.PullOut : leg.PullIn;
			double curve = total - (across ? TurnTime : 0.0);
			double left = start ? at.T : leg.End - leg.Start - at.T;
			if (curve <= 0.0)
			{
				return false;
			}
			if (left >= total)
			{
				return false;
			}
			if (left > curve)
			{
				// The U-turn: over to the middle of the road, round there (the only place a street with cars at both kerbs has room for
				// it), then into its own lane.
				// 1 where the curve meets the far lane, 0 in its own lane (time counts from the space either way).
				double round = 1.0 - (left - curve) / TurnTime;
				double toMiddle = Smooth(round / 0.3), turn = Smooth((round - 0.3) / 0.4), toLane = Smooth((round - 0.7) / 0.3);
				double leftward = off * (toMiddle + toLane);
				x = at.X - at.Hz * leftward;
				z = at.Z + at.Hx * leftward;
				double a = Math.Atan2(at.Hx, at.Hz) - Math.PI * turn;
				hx = Math.Sin(a);
				hz = Math.Cos(a);
				return true;
			}
			double q = left / curve;
			if (parallel)
			{
				// At the kerb: out sideways over the first half (the cars parked a few units in front and behind leave no room to angle
				// out), along the lane slowly at first, kept straight.
				double da = sx * at.Hx + sz * at.Hz;
				double nx = sx - at.Hx * da, nz = sz - at.Hz * da;
				double along = 1.0 - q * q, sideways = 1.0 - Smooth(2.0 * q);
				x = bx + at.Hx * da * along + nx * sideways;
				z = bz + at.Hz * da * along + nz * sideways;
				hx = spot.Hx;
				hz = spot.Hz;
				return true;
			}
			double g = 1.0 - Smooth(q);
			int place = parkedAfter >= 0 ? PlaceOf(day.Legs[parkedAfter]) : -1;
			if (place < 0 || Traffic.Destinations[place].Lot < 0)
			{
				// A drive pad, a cottage's or a farmyard's: along the road (or drive), where nothing is parked, to the space's own line,
				// round a small corner, then straight along that line into the space, clear of the cars in the rows and columns beside
				// it (a curve cutting the corner would cross their tails).
				double along = sx * spot.Hx + sz * spot.Hz;
				double kx = spot.X - spot.Hx * along, kz = spot.Z - spot.Hz * along;
				Corner(bx, bz, kx, kz, spot.X, spot.Z, g, out x, out z, out double dx, out double dz);
				double fhx0 = across ? -at.Hx : at.Hx, fhz0 = across ? -at.Hz : at.Hz;
				double way0 = along >= 0.0 ? 1.0 : -1.0;
				Turn(fhx0, fhz0, dx * way0, dz * way0, Smooth(2.0 * g), out hx, out hz);
				return true;
			}
			// A lot's bay: off the road first, square to it, then into the bay along the way it faces (between the cars either side): a
			// bay well along the road (a lot beside a junction) is reached over the lot, not over the junction's mouth.
			double lat = -sx * at.Hz + sz * at.Hx;
			double c1x = bx - at.Hz * lat * 0.6, c1z = bz + at.Hx * lat * 0.6;
			double c2x = spot.X - spot.Hx * side * 0.6, c2z = spot.Z - spot.Hz * side * 0.6;
			double v = 1.0 - g;
			x = v * v * v * bx + 3.0 * v * v * g * c1x + 3.0 * v * g * g * c2x + g * g * g * spot.X;
			z = v * v * v * bz + 3.0 * v * v * g * c1z + 3.0 * v * g * g * c2z + g * g * g * spot.Z;
			// Facing the way it moves along the curve (nose first into the space, or reversing in if the space faces back towards the
			// road), turning from its lane's way over the first half.
			double tx = v * v * (c1x - bx) + 2.0 * v * g * (c2x - c1x) + g * g * (spot.X - c2x);
			double tz = v * v * (c1z - bz) + 2.0 * v * g * (c2z - c1z) + g * g * (spot.Z - c2z);
			double tl = Math.Sqrt(tx * tx + tz * tz);
			double fhx = across ? -at.Hx : at.Hx, fhz = across ? -at.Hz : at.Hz;
			if (tl < 1e-6)
			{
				Turn(fhx, fhz, spot.Hx, spot.Hz, g, out hx, out hz);
				return true;
			}
			double way = (spot.X - c2x) * spot.Hx + (spot.Z - c2z) * spot.Hz >= 0.0 ? 1.0 : -1.0;
			Turn(fhx, fhz, tx / tl * way, tz / tl * way, Smooth(2.0 * g), out hx, out hz);
			return true;
		}

		/// <summary>
		/// The point a share <paramref name="g"/> of the way along a path from A to K to B by distance, its corner at K rounded off (at most
		/// 5 units back from it each way), and the path's way there (unit).
		/// </summary>
		private static void Corner(double ax, double az, double kx, double kz, double bx, double bz, double g, out double x, out double z, out double dx, out double dz)
		{
			double ux = kx - ax, uz = kz - az, la = Math.Sqrt(ux * ux + uz * uz);
			double wx = bx - kx, wz = bz - kz, lb = Math.Sqrt(wx * wx + wz * wz);
			if (la > 1e-6)
			{
				ux /= la;
				uz /= la;
			}
			if (lb > 1e-6)
			{
				wx /= lb;
				wz /= lb;
			}
			else
			{
				wx = ux;
				wz = uz;
			}
			if (la <= 1e-6)
			{
				ux = wx;
				uz = wz;
			}
			// The rounded corner: a quadratic from f before K to f after it (reckoned 2 f long).
			double f = Math.Min(5.0, Math.Min(la, lb) * 0.5);
			double d = g * (la + lb);
			if (d <= la - f)
			{
				x = ax + ux * d;
				z = az + uz * d;
				dx = ux;
				dz = uz;
				return;
			}
			if (d < la + f)
			{
				double t = (d - (la - f)) / (2.0 * f);
				double s0x = kx - ux * f, s0z = kz - uz * f, s1x = kx + wx * f, s1z = kz + wz * f;
				x = (1 - t) * (1 - t) * s0x + 2 * (1 - t) * t * kx + t * t * s1x;
				z = (1 - t) * (1 - t) * s0z + 2 * (1 - t) * t * kz + t * t * s1z;
				double tx = (1 - t) * (kx - s0x) + t * (s1x - kx), tz = (1 - t) * (kz - s0z) + t * (s1z - kz), tl = Math.Sqrt(tx * tx + tz * tz);
				dx = tl > 1e-9 ? tx / tl : wx;
				dz = tl > 1e-9 ? tz / tl : wz;
				return;
			}
			double rest = Math.Min(lb, d - la);
			x = kx + wx * rest;
			z = kz + wz * rest;
			dx = wx;
			dz = wz;
		}

		/// <summary>The lane offset of the road a leg's path is on at distance <paramref name="p"/>.</summary>
		private static double LaneOf(TrafficDay.Leg leg, double p)
		{
			TrafficDay.Place pl = default;
			TrafficDay.OnPath(leg, Math.Max(0.0, Math.Min(leg.Length, p)), ref pl);
			return pl.Edge >= 0 ? Traffic.LaneOffset(Network.Roads[Traffic.Edges[pl.Edge].Road]) : 5.5;
		}

		private static double Smooth(double k)
		{
			k = Math.Max(0.0, Math.Min(1.0, k));
			return k * k * (3.0 - 2.0 * k);
		}

		/// <summary>A way between (ax, az) at 0 and (bx, bz) at 1, turning the short way round (unit).</summary>
		private static void Turn(double ax, double az, double bx, double bz, double k, out double hx, out double hz)
		{
			double a0 = Math.Atan2(ax, az), a1 = Math.Atan2(bx, bz);
			double d = a1 - a0;
			while (d > Math.PI)
			{
				d -= 2.0 * Math.PI;
			}
			while (d < -Math.PI)
			{
				d += 2.0 * Math.PI;
			}
			double a = a0 + d * Math.Max(0.0, Math.Min(1.0, k));
			hx = Math.Sin(a);
			hz = Math.Cos(a);
		}

		/// <summary>The leg that left the car where leg <paramref name="leg"/> starts from (-1: home before any trip; int.MinValue: it came from off the map).</summary>
		internal int BeforeLeg(int leg)
		{
			return day.Legs[leg].Appears ? int.MinValue : leg < before.Count ? before[leg] : -1;
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
			return "place " + PlaceOf(day.Legs[leg]) + " slot " + (leg < slot.Count ? slot[leg] : -1);
		}

		/// <summary>Where the car stood before leg <paramref name="leg"/> left (the spot it pulls out of).</summary>
		internal Spot Before(int leg)
		{
			TrafficDay.Leg l = day.Legs[leg];
			return l.Appears || leg >= before.Count ? new Spot { Away = true } : Parked(l.Car, before[leg]);
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
					// On the pad beside the house, nose in, its tail clear of the street and its sidewalk; a second car beside the first,
					// further from the house (behind it, it could not get in or out). Beside the house on the other side if that one is taken
					// by a road, the water or a house.
					double back = Math.Max(off, road.Half + Network.Shoulder + CarLong * 0.5 + 1.0);
					foreach (double u0 in new[] { along, -along })
					{
						double u = u0 + Math.Sign(u0) * BayWidth * 1.1 * k;
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
					// Side by side beside the end of the drive, noses away from it, one after another along it; or the other side.
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
					// Its tail clear of a car on the drive beside it (the day starts and ends a trip there, beside the space).
					d = Math.Max(d, CarLong * 0.5 + CarWide * 0.5 + 4.0);
					foreach (double sd in new[] { 1.0, -1.0 })
					{
						double u = Spread(k) * BayWidth * 1.2;
						spot = new Spot { X = px + dx * d * sd + tx * u, Z = pz + dz * d * sd + tz * u, Hx = dx * sd, Hz = dz * sd };
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
					// row along the front aisle first (driven straight into from it), then the rest.
					double block = Math.Floor((half - 13.0) / BayWidth) * BayWidth;
					int across = (int)Math.Round(block / BayWidth);
					foreach (double v0 in new[] { 14.0, 64.0 })
					{
						for (int row = 0; row <= 1; row++)
						{
							double a = v0 + row * BayLength;
							// The first row's noses at its back line (v1), the second's at its front (v0).
							double nv = row == 0 ? 1.0 : -1.0;
							// Each block's bays in order across, every other one first.
							foreach (double sign in new[] { -1.0, 1.0 })
							{
								List<(double u, double v, double nu, double nv)> block2 = new List<(double, double, double, double)>();
								for (int i = 0; i < across; i++)
								{
									block2.Add((sign * (3.0 + BayWidth * (i + 0.5)), a + BayLength * 0.5, 0.0, nv));
								}
								EveryOther(block2, across);
								list.AddRange(block2);
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
					EveryOther(list, list.Count);
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

		/// <summary>The spaces at even places in the list first, then the odd: next to an empty bay a car pulls in clear of its neighbours.</summary>
		private static void EveryOther(List<(double u, double v, double nu, double nv)> list, int perRow)
		{
			List<(double u, double v, double nu, double nv)> first = new List<(double, double, double, double)>(), then = new List<(double, double, double, double)>();
			for (int i = 0; i < list.Count; i++)
			{
				// Within each row (perRow at a time) alternate bays; rows keep their order.
				int at = i % perRow;
				(at % 2 == 0 ? first : then).Add(list[i]);
				if (at == perRow - 1 || i == list.Count - 1)
				{
					first.AddRange(then);
					then.Clear();
				}
			}
			list.Clear();
			list.AddRange(first);
		}

		/// <summary>A row of spaces side by side across a lot at depth <paramref name="v"/>, within its sides, each side of a gap of half width <paramref name="gap"/> round the middle, nearest the middle first.</summary>
		private static void Row(List<(double u, double v, double nu, double nv)> list, double half, double gap, double v, double nv)
		{
			double pitch = BayWidth * 1.1;
			List<(double u, double v, double nu, double nv)> row = new List<(double, double, double, double)>();
			for (double inner = gap + pitch * 0.5; inner <= half - 4.0 - pitch * 0.5; inner += pitch)
			{
				row.Add((-inner, v, 0.0, nv));
				row.Add((inner, v, 0.0, nv));
			}
			// Across in order, every other one first (a car pulls in next to an empty space).
			row.Sort((a, b) => a.u.CompareTo(b.u));
			EveryOther(row, row.Count);
			list.AddRange(row);
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
