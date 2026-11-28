using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where each parked car of the live traffic (<see cref="TrafficLive"/>) stands and which way it faces: a household's own cars on its drive
	/// pad, by a cottage's drive or in a farmyard, a terrace's first car at the kerb by its door; a visitor at the kerb by the house it visits;
	/// at a shop or the retail park in the painted bays (the same arithmetic <see cref="Buildings"/> paints them with), at a workshop, filling
	/// station, market or course's yard in rows on its surface, then along the kerb. A place's spaces are given out as cars arrive (the lowest
	/// free one) and come free as they leave, so two cars never stand in one spot.
	///
	/// <para>The arithmetic of the spots is carried over from the old traffic's parking (<c>traffic-test-branch</c>); the giving out is new: the
	/// meso parks a car at its stop's anchor on the road, and this only says where along the kerb or in the lot it stands, so nothing here
	/// changes where the simulation has a car. It needn't be deterministic (each machine draws its own). No Unity types.</para>
	/// </summary>
	internal sealed class TrafficParking
	{
		/// <summary>A parked car: the middle of the car, the way its nose faces (unit), whether it is out of sight (not drawn), and the road
		/// it stands on at the kerb (-1 off the road; 0 is a road too, so only meaningful with <see cref="AtKerb"/>).</summary>
		internal struct Spot
		{
			public double X, Z, Hx, Hz;
			public bool Away, AtKerb;
			public int Road;
		}

		/// <summary>A painted bay's width and length (as <see cref="Buildings"/> paints them: 2.7 m by 4.8 m).</summary>
		internal const double BayWidth = 9.0, BayLength = 16.0;

		/// <summary>Spacing of cars parked one behind the other along a kerb (a car and a gap: about 5.8 m, room to pull in and out).</summary>
		internal const double KerbSpacing = 19.0;

		/// <summary>How far in from the edge of the paving the middle of a car at the kerb is, on a road wide enough; and the room left between it and a car driving past in the lane.</summary>
		internal const double KerbIn = 3.5, KerbClear = 0.4;

		/// <summary>A car's footprint for the overlap tests (about 14 by 5.4 units).</summary>
		internal const double CarLong = 14.0, CarWide = 5.4;

		/// <summary>How many kerb spaces either way from the door a car looks along before it is parked round the corner, out of sight.</summary>
		private const int KerbTries = 7;

		/// <summary>Each car's plans as the meso drives them (<see cref="Meso.Day"/>: the next day's once a car begins it).</summary>
		private readonly Schedules.Car[] cars;

		/// <summary>Per car: its own space at home (off the road or at the kerb; <see cref="Spot.Away"/> round the back).</summary>
		private readonly Spot[] home;

		/// <summary>Per car with its space at home at the kerb: that space's key (<see cref="kerbTaken"/>), so it can move across the road.</summary>
		private readonly long[] homeKey;

		/// <summary>Per car: the spot it is parked in away from home now (when <see cref="held"/> says it holds one), and what it holds.</summary>
		private readonly Spot[] spot;
		private readonly Hold[] held;

		private struct Hold
		{
			/// <summary>0 nothing; 1 a place's space <see cref="K"/> of destination <see cref="Of"/>; 2 a kerb space (<see cref="Key"/>).</summary>
			public byte Kind;
			public int Of, K;
			public long Key;
		}

		/// <summary>Per destination with a lot: its spaces on the lot (lot-frame u, v and the way the nose faces along v), and which are taken.</summary>
		private readonly Dictionary<int, List<(double u, double v, double nu, double nv)>> lotSpaces = new Dictionary<int, List<(double, double, double, double)>>();
		private readonly Dictionary<int, List<bool>> taken = new Dictionary<int, List<bool>>();

		/// <summary>Kerb spaces taken (edge, side, index along it).</summary>
		private readonly HashSet<long> kerbTaken = new HashSet<long>();

		/// <summary>The homes' own spaces off the road, in cells of <see cref="CellSize"/>, for keeping kerb spaces and other pads off them.</summary>
		private readonly Dictionary<long, List<Spot>> fixedSpots = new Dictionary<long, List<Spot>>();
		private const double CellSize = 32.0;

		/// <summary>For the log: cars with a space at home off the road, at the kerb, and round the back.</summary>
		internal int OwnSpaces, KerbHomes, RoundTheBack;

		/// <summary>
		/// The homes' spaces, made once the race's cars are known: each household's own cars' spaces off the road, then the first car of a
		/// household with none at the kerb by its door (any more round the back, out of sight: a terrace street lined with every household's
		/// every car is far busier than a real one).
		/// </summary>
		internal TrafficParking(Schedules.Car[] cars)
		{
			this.cars = cars;
			EnsureHouses();
			int n = cars.Length;
			home = new Spot[n];
			homeKey = new long[n];
			spot = new Spot[n];
			held = new Hold[n];
			bool[] own = new bool[n];
			for (int c = 0; c < n; c++)
			{
				Schedules.Car car = cars[c];
				home[c] = new Spot { Away = true };
				if (car.Household < 0)
				{
					continue;
				}
				// Taken one after another, each clear of those before (two neighbours' pads can meet between their houses).
				if (OffRoad(car.Household, car.Index, out Spot s))
				{
					home[c] = s;
					own[c] = true;
					Keep(s);
					OwnSpaces++;
				}
			}
			for (int c = 0; c < n; c++)
			{
				Schedules.Car car = cars[c];
				if (car.Household < 0 || own[c])
				{
					continue;
				}
				if (car.Index == 0 && TakeKerb(Traffic.Households[car.Household].Home, out Spot s, out long key))
				{
					home[c] = s;
					homeKey[c] = key;
					KerbHomes++;
				}
				else
				{
					RoundTheBack++;
				}
			}
		}

		/// <summary>
		/// Where car <paramref name="car"/> parked by the meso at its stop <paramref name="stop"/> (at place <paramref name="place"/>, -1 for a
		/// home or the kerb) stands: its own space at home, else a space taken now (and held until <see cref="Leave"/>). At the kerb, given the
		/// way the car comes (<paramref name="wayX"/>, <paramref name="wayZ"/>; 0, 0 if not known), a space on its own side of the road first,
		/// facing the way it comes: one across the road would have it turn round in the street to get into it.
		/// </summary>
		internal Spot Park(int car, int stop, int place, double wayX = 0.0, double wayZ = 0.0, bool kerbAt = false, Traffic.Anchor kerb = default)
		{
			Leave(car);
			Schedules.Car c = cars[car];
			if (kerbAt)
			{
				// Sent on to a kerb space for want of room at its stop (Meso.KerbAt): the space found there, or the nearest free one to it.
				return Kerb(car, kerb, wayX, wayZ, true);
			}
			Schedules.Stop st = c.Stops[Math.Max(0, Math.Min(stop, c.Stops.Length - 1))];
			if (place < 0 && st.Kind == Schedules.StopKind.Home)
			{
				if (wayX == 0.0 && wayZ == 0.0)
				{
					// Not arriving where it is drawn (the race's start, or parked out of sight): facing the way its next drive leaves.
					LeavingWay(c, stop, out wayX, out wayZ);
				}
				Spot h = home[car];
				if (h.AtKerb && !h.Away && (wayX != 0.0 || wayZ != 0.0) && h.Hx * wayX + h.Hz * wayZ < 0.0
					&& TakeKerb(Traffic.Households[c.Household].Home, out Spot other, out long key, wayX, wayZ))
				{
					if (other.Hx * wayX + other.Hz * wayZ > 0.0)
					{
						// Its space at the kerb moved to the side of the road it comes along.
						DropKerb(homeKey[car]);
						homeKey[car] = key;
						home[car] = other;
					}
					else
					{
						DropKerb(key);
					}
				}
				return home[car];
			}
			if (st.Kind == Schedules.StopKind.Away)
			{
				return new Spot { Away = true };
			}
			if (place >= 0)
			{
				Traffic.Destination d = Traffic.Destinations[place];
				if (d.Kind == Traffic.DestinationKind.Edge)
				{
					return new Spot { Away = true };
				}
				if (d.Lot >= 0)
				{
					List<(double u, double v, double nu, double nv)> spaces = LotSpaces(place);
					List<bool> t = taken[place];
					for (int k = 0; k < spaces.Count; k++)
					{
						if (!t[k])
						{
							t[k] = true;
							held[car] = new Hold { Kind = 1, Of = place, K = k };
							spot[car] = LotSpot(Town.Lots[d.Lot], spaces[k]);
							return spot[car];
						}
					}
				}
				return Kerb(car, d.At, wayX, wayZ);
			}
			// A visit (or a lot that sent it on to the kerb): along the kerb by the door.
			Traffic.Anchor at = st.Kind == Schedules.StopKind.Visit && st.Host >= 0 ? Traffic.Households[st.Host].Home : st.At;
			return Kerb(car, at, wayX, wayZ);
		}

		/// <summary>The way car <paramref name="c"/> sets off from its stop <paramref name="stop"/> along the stretch it is on (its next leg's first edge); 0, 0 if not known.</summary>
		private static void LeavingWay(Schedules.Car c, int stop, out double x, out double z)
		{
			x = z = 0.0;
			if (c.Legs == null || stop < 0 || stop >= c.Legs.Length || c.Legs[stop]?.Route == null || c.Legs[stop].Route.Length == 0)
			{
				return;
			}
			int first = c.Legs[stop].Route[0];
			Traffic.Edge e = Traffic.Edges[first >> 1];
			Traffic.Anchor a = c.Stops[stop].At;
			if (a.Edge != first >> 1)
			{
				return;
			}
			Traffic.Heading(Network.Roads[e.Road], a.S, out double tx, out double tz);
			double sign = (e.Sb >= e.Sa ? 1.0 : -1.0) * ((first & 1) == 0 ? 1.0 : -1.0);
			x = tx * sign;
			z = tz * sign;
		}

		/// <summary>
		/// Whether car <paramref name="car"/> arriving at its stop <paramref name="stop"/> (place <paramref name="place"/>) would get a space the
		/// game draws it in (not <see cref="Spot.Away"/>): tried and given back. Its stop off the map has room (it is out of sight there).
		/// </summary>
		internal bool Room(int car, int stop, int place, bool kerbAt, Traffic.Anchor kerb, double wayX = 0.0, double wayZ = 0.0)
		{
			Schedules.Car c = cars[car];
			Schedules.Stop st = c.Stops[Math.Max(0, Math.Min(stop, c.Stops.Length - 1))];
			if (!kerbAt && (st.Kind == Schedules.StopKind.Away || (place >= 0 && Traffic.Destinations[place].Kind == Traffic.DestinationKind.Edge)))
			{
				return true;
			}
			if (!kerbAt && place < 0 && st.Kind == Schedules.StopKind.Home)
			{
				return !home[car].Away;
			}
			Hold before = held[car];
			Spot was = spot[car];
			if (before.Kind != 0)
			{
				// It holds a space already (given it on its way in): that one answers.
				return !was.Away;
			}
			Spot s = Park(car, stop, place, wayX, wayZ, kerbAt, kerb);
			Leave(car);
			spot[car] = was;
			return !s.Away;
		}

		/// <summary>Whether car <paramref name="car"/>'s stop <paramref name="stop"/> (place <paramref name="place"/>) is off the map, where it isn't drawn and needn't be.</summary>
		internal bool OffMap(int car, int stop, int place)
		{
			Schedules.Car c = cars[car];
			Schedules.Stop st = c.Stops[Math.Max(0, Math.Min(stop, c.Stops.Length - 1))];
			return st.Kind == Schedules.StopKind.Away || (place >= 0 && Traffic.Destinations[place].Kind == Traffic.DestinationKind.Edge);
		}

		/// <summary>How far (straight line, units) from where a car finds no room it looks for a free kerb space: about 450 m, a few streets.</summary>
		private const double KerbReach = 1500.0;

		/// <summary>
		/// The nearest free kerb space to anchor <paramref name="near"/>, as an anchor on its stretch: along the whole of its own stretch first,
		/// then the stretches round it a junction at a time (in the order the junctions list them, so every machine finds the same), within
		/// <see cref="KerbReach"/>. Nothing is taken: the car is sent there and takes it when it parks.
		/// </summary>
		/// <summary>How many stretches the kerb search looks along at most.</summary>
		private const int KerbSearch = 80;

		internal bool KerbNear(Traffic.Anchor near, double wayX, double wayZ, out Traffic.Anchor at, int car = -1)
		{
			at = near;
			if (near.Edge < 0 || near.Edge >= Traffic.Edges.Count)
			{
				return false;
			}
			List<int> queue = new List<int> { near.Edge };
			HashSet<int> seen = new HashSet<int> { near.Edge };
			for (int qi = 0; qi < queue.Count && qi < KerbSearch; qi++)
			{
				int e = queue[qi];
				Traffic.Edge te = Traffic.Edges[e];
				Traffic.Anchor from = near;
				if (e != near.Edge)
				{
					from = new Traffic.Anchor { Edge = e, S = (te.Sa + te.Sb) * 0.5, Side = near.Side };
				}
				int whole = (int)(Math.Abs(te.Sb - te.Sa) / KerbSpacing) + 2;
				// On its own stretch, the side it comes along (it doesn't cross the road to a space); further on, either (it comes along a
				// stretch either way: on getting there, one across the road sends it on again from there).
				bool own = e == near.Edge && (wayX != 0.0 || wayZ != 0.0);
				if (FindKerb(from, own ? wayX : 0.0, own ? wayZ : 0.0, whole, car >= 0, out Spot found, out long fk, out double sAt, out int side, own, true))
				{
					if (car >= 0)
					{
						// Held for it from now: two cars sent on at once would otherwise be sent to neighbouring spaces, each roomy on its own,
						// and back into one another.
						Leave(car);
						held[car] = new Hold { Kind = 2, Key = fk };
						spot[car] = found;
					}
					Traffic.At(Network.Roads[te.Road], sAt, out double x, out double z, out double _);
					at = new Traffic.Anchor { Edge = e, S = sAt, Side = side, X = x, Z = z };
					return true;
				}
				foreach (int node in new[] { te.A, te.B })
				{
					Traffic.Node nd = Traffic.Nodes[node];
					if ((nd.X - near.X) * (nd.X - near.X) + (nd.Z - near.Z) * (nd.Z - near.Z) > KerbReach * KerbReach)
					{
						continue;
					}
					foreach (int o in Traffic.Links[node])
					{
						if (seen.Add(o))
						{
							queue.Add(o);
						}
					}
				}
			}
			return false;
		}

		/// <summary>Car <paramref name="car"/> leaves the space it took away from home (its own space at home is always kept).</summary>
		internal void Leave(int car)
		{
			Hold h = held[car];
			if (h.Kind == 1)
			{
				taken[h.Of][h.K] = false;
			}
			else if (h.Kind == 2)
			{
				DropKerb(h.Key);
			}
			held[car] = default;
		}

		private Spot Kerb(int car, Traffic.Anchor at, double wayX, double wayZ, bool oneSide = false)
		{
			if (FindKerb(at, wayX, wayZ, KerbTries, true, out Spot s, out long key, out double _, out int _, oneSide && (wayX != 0.0 || wayZ != 0.0)))
			{
				held[car] = new Hold { Kind = 2, Key = key };
				spot[car] = s;
				return s;
			}
			return new Spot { Away = true };
		}

		// ---- the kerb ------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// A free kerb space near anchor <paramref name="a"/>: in front of it first, then a car's length either way in turn, on its side of the
		/// road and then the other, clear of the junctions at the stretch's ends, the homes' own spaces, the houses, other roads and the water.
		/// Only the town streets and the secondary roads have parking at the kerb: a main road keeps its traffic moving, a narrow lane has no
		/// room for a car to pass one parked. Given the way a car comes (<paramref name="wayX"/>, <paramref name="wayZ"/>), the side it comes
		/// along first (its spaces face that way) rather than the anchor's.
		/// </summary>
		private bool TakeKerb(Traffic.Anchor a, out Spot s, out long key, double wayX = 0.0, double wayZ = 0.0)
		{
			return FindKerb(a, wayX, wayZ, KerbTries, true, out s, out key, out double _, out int _);
		}

		/// <summary>
		/// <see cref="TakeKerb"/>'s search: <paramref name="tries"/> spaces either way along the stretch, taken if <paramref name="take"/>; where
		/// the space is along the road (<paramref name="sOut"/>) and its side.
		/// </summary>
		private bool FindKerb(Traffic.Anchor a, double wayX, double wayZ, int tries, bool take, out Spot s, out long key, out double sOut, out int sideOut, bool oneSide = false, bool roomy = false)
		{
			s = new Spot { Away = true };
			key = 0;
			sOut = 0.0;
			sideOut = 1;
			if (a.Edge < 0 || a.Edge >= Traffic.Edges.Count)
			{
				return false;
			}
			Traffic.Edge e = Traffic.Edges[a.Edge];
			Network.Road road = Network.Roads[e.Road];
			if (road.Kind != RoadKind.Street && (road.Kind != RoadKind.Secondary || roomy))
			{
				// A car sent to a kerb space looks for one in the town's streets, not along a secondary road: backing into a space there,
				// the cars behind it come on (and the stretch is busy enough already with those parked at their stop).
				return false;
			}
			KerbLimits(e, out double lo, out double hi);
			if (hi <= lo)
			{
				return false;
			}
			int first = a.Side >= 0 ? 1 : -1;
			if (wayX != 0.0 || wayZ != 0.0)
			{
				// A side's spaces face against the road's way on its left (OnKerb).
				Traffic.Heading(road, a.S, out double tx, out double tz);
				first = tx * wayX + tz * wayZ >= 0.0 ? -1 : 1;
			}
			foreach (int side in oneSide ? new[] { first } : new[] { first, -first })
			{
				for (int k = 0; k < 2 * tries + 1; k++)
				{
					double sAt = a.S + Spread(k) * KerbSpacing;
					if (sAt < lo || sAt > hi)
					{
						continue;
					}
					// Spaces on a fixed pitch along the stretch, so two cars near one another take neighbouring spaces, never half of one.
					long index = (long)Math.Round((sAt - lo) / KerbSpacing);
					sAt = lo + index * KerbSpacing;
					if (sAt > hi)
					{
						continue;
					}
					long kk = ((long)a.Edge << 24) ^ ((long)(side > 0 ? 1 : 0) << 23) ^ index;
					if (kerbTaken.Contains(kk))
					{
						continue;
					}
					Spot here = OnKerb(road, sAt, side);
					here.AtKerb = true;
					here.Road = e.Road;
					if (!fits.TryGetValue(kk, out bool fit))
					{
						// What doesn't change in a race (the homes' own spaces, the houses, other roads, the water), worked out once a space.
						fit = !Blocked(here) && Clear(here, e.Road);
						fits[kk] = fit;
					}
					if (!fit || KerbBlocked(here, roomy ? Roomy : 0.5))
					{
						continue;
					}
					if (take)
					{
						TakeKerbKey(kk, here);
					}
					s = here;
					key = kk;
					sOut = sAt;
					sideOut = side;
					return true;
				}
			}
			return false;
		}

		/// <summary>Where a stretch's kerb may hold cars: from a car's half length past the junction box at one end to the same short of the other.</summary>
		private static void KerbLimits(Traffic.Edge e, out double lo, out double hi)
		{
			lo = Math.Min(e.Sa, e.Sb) + Traffic.Nodes[e.Sa <= e.Sb ? e.A : e.B].Box + KerbSpacing * 0.5;
			hi = Math.Max(e.Sa, e.Sb) - Traffic.Nodes[e.Sa <= e.Sb ? e.B : e.A].Box - KerbSpacing * 0.5;
		}

		/// <summary>A car at the kerb of <paramref name="road"/> at road distance <paramref name="s"/> on side <paramref name="side"/>, facing the way traffic on that side goes (driving on the right).</summary>
		private static Spot OnKerb(Network.Road road, double s, int side)
		{
			Traffic.At(road, s, out double x, out double z, out double _);
			Traffic.Heading(road, s, out double tx, out double tz);
			// Left of the road's way is (-tz, tx); a car on the left faces against the road's way. Far enough out that a car driving past in
			// the lane clears it: on a town street two wheels up on the sidewalk. Never past the sidewalk's back, where a terrace's front stands.
			double half = Network.Widened(road, s), wide = half - road.Half;
			double off = Math.Min(Math.Max(half - KerbIn, Traffic.LaneOffset(road) + wide + CarWide + KerbClear), half + Network.Verge - CarWide * 0.5 - 0.1);
			return new Spot { X = x - tz * off * side, Z = z + tx * off * side, Hx = tx * -side, Hz = tz * -side };
		}

		/// <summary>0, 1, -1, 2, -2...: spaces handed out from the middle outwards.</summary>
		private static int Spread(int k)
		{
			return (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
		}

		// ---- the homes ------------------------------------------------------------------------------------------------------------

		/// <summary>Keeps <paramref name="s"/> as a home's own space: no kerb space, nor another home's, is made over it.</summary>
		private void Keep(Spot s)
		{
			long key = Cell(s.X, s.Z);
			if (!fixedSpots.TryGetValue(key, out List<Spot> list))
			{
				list = new List<Spot>();
				fixedSpots[key] = list;
			}
			list.Add(s);
		}

		private static long Cell(double x, double z)
		{
			return ((long)Math.Floor(x / CellSize) << 32) ^ (uint)(int)Math.Floor(z / CellSize);
		}

		/// <summary>Per kerb space (its key) whether it is clear of what doesn't change in a race: a cache, the same worked out again.</summary>
		[NonSerialized] private Dictionary<long, bool> fits = new Dictionary<long, bool>();

		/// <summary>The kerb spaces taken, by key, and their keys in cells of <see cref="CellSize"/>: on a bend, or where two stretches meet, two spaces with different keys can stand in one another.</summary>
		private readonly Dictionary<long, Spot> kerbSpots = new Dictionary<long, Spot>();
		private readonly Dictionary<long, List<long>> kerbCells = new Dictionary<long, List<long>>();

		private void TakeKerbKey(long kk, Spot here)
		{
			kerbTaken.Add(kk);
			kerbSpots[kk] = here;
			long cell = Cell(here.X, here.Z);
			if (!kerbCells.TryGetValue(cell, out List<long> list))
			{
				list = new List<long>();
				kerbCells[cell] = list;
			}
			list.Add(kk);
		}

		private void DropKerb(long kk)
		{
			kerbTaken.Remove(kk);
			if (kerbSpots.TryGetValue(kk, out Spot was))
			{
				kerbSpots.Remove(kk);
				if (kerbCells.TryGetValue(Cell(was.X, was.Z), out List<long> list))
				{
					list.Remove(kk);
				}
			}
		}

		/// <summary>Whether a car at the kerb at <paramref name="s"/> would stand in a kerb space already taken.</summary>
		/// <summary>The room a car sent to a kerb space wants clear round it (units, each way): to drive into it without touching a neighbour.</summary>
		private const double Roomy = 6.0;

		private bool KerbBlocked(Spot s, double margin = 0.5)
		{
			int gx = (int)Math.Floor(s.X / CellSize), gz = (int)Math.Floor(s.Z / CellSize);
			for (int ox = -1; ox <= 1; ox++)
			{
				for (int oz = -1; oz <= 1; oz++)
				{
					if (kerbCells.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<long> list))
					{
						foreach (long k in list)
						{
							if (Overlap(s, kerbSpots[k], margin))
							{
								return true;
							}
						}
					}
				}
			}
			return false;
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

		/// <summary>
		/// Space <paramref name="k"/> at household <paramref name="h"/>'s home off the road: on a house's drive pad, by a cottage's drive, in a
		/// farm's yard; the first of a few places to try that is clear of every road's paving, the water, the lots' buildings and the houses.
		/// False if none is (the car goes to the kerb), and always for a terrace.
		/// </summary>
		private bool OffRoad(int h, int k, out Spot s)
		{
			Traffic.Household hh = Traffic.Households[h];
			Traffic.Anchor a = hh.Home;
			Network.Road road = RoadOf(a, out double px, out double pz, out double tx, out double tz);
			int sideSign = a.Side >= 0 ? 1 : -1;
			double nx = -tz * sideSign, nz = tx * sideSign;
			double along = (a.X - px) * tx + (a.Z - pz) * tz, off = (a.X - px) * nx + (a.Z - pz) * nz;
			s = default;
			switch (hh.Kind)
			{
				case Traffic.HomeKind.House:
				{
					if (k >= hh.Cars)
					{
						return false;
					}
					// On the pad beside the house, nose in, its tail clear of the street and its sidewalk; a second car beside the first,
					// further from the house. Beside the house on the other side if that one is taken by a road, the water or a house.
					double back = Math.Max(off, road.Half + Network.Shoulder + CarLong * 0.5 + 1.0);
					foreach (double u0 in new[] { along, -along })
					{
						double u = u0 + Math.Sign(u0) * BayWidth * 1.1 * k;
						s = new Spot { X = px + tx * u + nx * back, Z = pz + tz * u + nz * back, Hx = nx, Hz = nz };
						if (Clear(s, -1) && !Blocked(s))
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
					d = Math.Max(d, CarLong * 0.5 + CarWide * 0.5 + 4.0);
					foreach (double sd in new[] { 1.0, -1.0 })
					{
						double u = Spread(k) * BayWidth * 1.2;
						s = new Spot { X = px + dx * d * sd + tx * u, Z = pz + dz * d * sd + tz * u, Hx = dx * sd, Hz = dz * sd };
						if (Clear(s, -1) && !Blocked(s))
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
						s = new Spot { X = px + fx * o + tx * u, Z = pz + fz * o + tz * u, Hx = fx, Hz = fz };
						if (Clear(s, -1) && !Blocked(s))
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

		/// <summary>An anchor's road, its point and way (unit) at the anchor.</summary>
		private static Network.Road RoadOf(Traffic.Anchor a, out double px, out double pz, out double tx, out double tz)
		{
			Network.Road road = Network.Roads[Traffic.Edges[a.Edge].Road];
			Traffic.At(road, a.S, out px, out pz, out double _);
			Traffic.Heading(road, a.S, out tx, out tz);
			return road;
		}

		/// <summary>Whether a parked car at <paramref name="s"/> is clear of the houses, every road's paving but <paramref name="own"/> (a car at the kerb stands on its road), the water and the lots' buildings (its corners and middle).</summary>
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
		/// The houses (the plots <see cref="Plots"/> keeps that fit, as <see cref="Buildings"/> draws them): middle, the way along the road, half
		/// width along it and half depth across, in cells of <see cref="HouseCell"/>. Fixed with the world: made once.
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
				var grid = new Dictionary<long, List<(double, double, double, double, double, double)>>();
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
							if (!grid.TryGetValue(key, out var list))
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
					if (!houses.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out var list))
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

		// ---- the lots ------------------------------------------------------------------------------------------------------------

		/// <summary>A lot-frame space as a spot in the world.</summary>
		private static Spot LotSpot(Town.Lot lot, (double u, double v, double nu, double nv) s)
		{
			Town.World(lot, s.u, s.v, out double x, out double z);
			// Along the lot is (Dx, Dz), away from the road (-Dz, Dx).
			double hx = lot.Dx * s.nu - lot.Dz * s.nv, hz = lot.Dz * s.nu + lot.Dx * s.nv;
			return new Spot { X = x, Z = z, Hx = hx, Hz = hz };
		}

		/// <summary>A destination's spaces on its lot, made when first asked for (those a road's paving, the water or a building reaches into left out).</summary>
		private List<(double u, double v, double nu, double nv)> LotSpaces(int d)
		{
			if (lotSpaces.TryGetValue(d, out var found))
			{
				return found;
			}
			var list = new List<(double u, double v, double nu, double nv)>();
			Town.Lot lot = Town.Lots[Traffic.Destinations[d].Lot];
			Spaces(lot, list);
			list.RemoveAll(sp => !Clear(LotSpot(lot, sp), -1));
			lotSpaces[d] = list;
			taken[d] = new List<bool>(new bool[list.Count]);
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
					// Two blocks of bays each side of a middle island, in two double rows parked nose to nose (Buildings.BayRows).
					double block = Math.Floor((half - 13.0) / BayWidth) * BayWidth;
					int across = (int)Math.Round(block / BayWidth);
					foreach (double v0 in new[] { 14.0, 64.0 })
					{
						for (int row = 0; row <= 1; row++)
						{
							double a = v0 + row * BayLength;
							double nv = row == 0 ? 1.0 : -1.0;
							foreach (double sign in new[] { -1.0, 1.0 })
							{
								var block2 = new List<(double u, double v, double nu, double nv)>();
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
					Row(list, half, 0.0, BayLength * 0.5 + 2.0, 1.0);
					break;
				case LotKind.Service:
				case LotKind.Paddock:
					Row(list, half, 0.0, Math.Max(BayLength, lot.D * 0.35), 1.0);
					break;
			}
		}

		/// <summary>The spaces at even places in the list first, then the odd: next to an empty bay a car pulls in clear of its neighbours.</summary>
		private static void EveryOther(List<(double u, double v, double nu, double nv)> list, int perRow)
		{
			var first = new List<(double u, double v, double nu, double nv)>();
			var then = new List<(double u, double v, double nu, double nv)>();
			for (int i = 0; i < list.Count; i++)
			{
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

		/// <summary>A row of spaces side by side across a lot at depth <paramref name="v"/>, within its sides, each side of a gap of half width <paramref name="gap"/> round the middle.</summary>
		private static void Row(List<(double u, double v, double nu, double nv)> list, double half, double gap, double v, double nv)
		{
			double pitch = BayWidth * 1.1;
			var row = new List<(double u, double v, double nu, double nv)>();
			for (double inner = gap + pitch * 0.5; inner <= half - 4.0 - pitch * 0.5; inner += pitch)
			{
				row.Add((-inner, v, 0.0, nv));
				row.Add((inner, v, 0.0, nv));
			}
			row.Sort((a, b) => a.u.CompareTo(b.u));
			EveryOther(row, row.Count);
			list.AddRange(row);
		}
	}
}
