using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The left-turn bays off the trunk (pure, no Unity types): where a primary road ends on another in a T (a junction with lights),
	/// the main road's cars turning left wait in a lane of their own in the middle of the road, out of the way of the cars going on
	/// (the owner, 2026-11-25, of Kingsbridge's node 6: "install the actual turn lane"). The road is widened by a lane there
	/// (<see cref="Network.Road.Wide"/>, laid out by <see cref="Network"/> before the junctions are cut): the through lanes move out half a
	/// lane each over a taper, and the middle between them is a painted median, the bay where it runs up to the T. The bay gets a left
	/// arrow at the lights (<see cref="TrafficSignals.Arrows"/>, protected and permissive: the arrow when a car waits in it, else the
	/// left on the main line's green giving way). <see cref="RoadMesh"/> paints it, <see cref="TrafficDay"/> drives it,
	/// <see cref="SignalPoles"/> hangs its arrow head. The trunk's own bays are <see cref="TrunkLanes"/>.
	/// </summary>
	internal static class TurnBays
	{
		/// <summary>A bay: on road <see cref="Road"/> for its cars going <see cref="Dir"/> (+1 the way its distance grows), from
		/// <see cref="S0"/> to <see cref="S1"/> along it (S0 &lt; S1), turning left at <see cref="Junction"/> into road <see cref="Other"/>.</summary>
		internal struct Bay
		{
			public int Road, Other, Dir;
			public double S0, S1, Junction;
			/// <summary>The side road's half width; the junction behind the bay (along the road; NaN: none near) and its road's half width.</summary>
			public double SideHalf, Back, BackHalf;
			/// <summary>The bay runs on back through the junction behind (<see cref="Back"/>), a through lane there: its cars go straight
			/// on at it in the bay, and move into it before it.</summary>
			public bool Through;
		}

		internal static readonly List<Bay> Bays = new List<Bay>();

		/// <summary>
		/// Where bays are laid: the T's of main roads the owner asked for, by place (the layout is fixed by the seed; a site with no T of
		/// main roads near it gets nothing, and meshcheck says so). Kingsbridge's T where the road to Pinewick leaves the town's link
		/// (trafficview's node 6, 2026-11-25). The other T's of main roads with room for one (the link's T with the road to Redford, node 8;
		/// Northcote's road north meeting Millbrook's) would take one by adding them here.
		/// </summary>
		internal static (double X, double Z)[] Sites = { (-2388.0, -4337.0) };

		/// <summary>How near a site the T must be.</summary>
		internal const double SiteReach = 30.0;

		/// <summary>How much the road is widened: a lane.</summary>
		internal const double Extra = 11.0;
		/// <summary>The longest bay (about 8 cars) and the shortest worth having (two).</summary>
		internal const double Longest = 170.0, Shortest = 40.0;
		/// <summary>The widening's taper: the through lanes move out half a lane over it (about 30 m, MUTCD's shifting taper at 35 mph); where
		/// a junction beyond the T is near, as short as <see cref="ShortTaper"/>.</summary>
		internal const double Taper = 100.0, ShortTaper = 50.0;
		/// <summary>The bay ends this far past the side road's edge from the junction's middle (at the stop line, as the traffic's junction box).</summary>
		internal const double StopBack = 9.0;
		/// <summary>The widening runs on this far past a junction's road's edge, and the bay begins this far past the junction behind.</summary>
		internal const double PastJunction = 12.0;
		/// <summary>No bay this near a road's ends.</summary>
		internal const double EndClear = 120.0;

		/// <summary>The middle of the road between the through lanes (a painted median, and the bay) is this much wider than the widening:
		/// the two texture columns round the centre line it replaces.</summary>
		internal const double MiddleExtra = 2.0;

		/// <summary>How far from the centre line the middle's edges are at distance <paramref name="s"/> along road <paramref name="road"/> (0 where it is not widened).</summary>
		internal static double MiddleHalf(Network.Road road, double s)
		{
			double w = Network.WideAt(road, s);
			return w <= 0.0 ? 0.0 : (w + MiddleExtra) * 0.5;
		}

		/// <summary>The bay on road <paramref name="road"/> for cars going <paramref name="dir"/> at distance <paramref name="s"/> along it, or -1.</summary>
		internal static int Find(int road, int dir, double s)
		{
			for (int i = 0; i < Bays.Count; i++)
			{
				Bay b = Bays[i];
				if (b.Road == road && b.Dir == dir && s >= b.S0 && s <= b.S1)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>The bay on road <paramref name="road"/> for cars going <paramref name="dir"/> that ends at the junction at <paramref name="js"/> along it, or -1.</summary>
		internal static int At(int road, int dir, double js)
		{
			for (int i = 0; i < Bays.Count; i++)
			{
				Bay b = Bays[i];
				if (b.Road == road && b.Dir == dir && Math.Abs(b.Junction - js) < 30.0)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>Whether distance <paramref name="s"/> along road <paramref name="road"/> is across a junction's mouth on its widened stretch (no lines painted there).</summary>
		internal static bool Mouth(int road, double s)
		{
			foreach (Bay b in Bays)
			{
				// At the T, from the bay's stop end (the lines end there, at the stop line) through the side road's mouth.
				double stop = Math.Min(Math.Abs(b.Junction - b.S0), Math.Abs(b.Junction - b.S1));
				if (b.Road == road && (Math.Abs(s - b.Junction) < Math.Max(b.SideHalf + MouthClear, stop) || Math.Abs(s - b.Back) < b.BackHalf + MouthClear))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The lines stop this far past a side road's edge.</summary>
		private const double MouthClear = 6.0;

		/// <summary>Which edge of the middle the bay's white line is along at distance <paramref name="s"/> along road <paramref name="road"/>
		/// (+1 the left, -1 the right, as <see cref="Network.Point"/>): its cars' right; 0 where there is no bay (yellow both sides).</summary>
		internal static int WhiteSide(int road, double s)
		{
			foreach (Bay b in Bays)
			{
				if (b.Road == road && s >= b.S0 && s <= b.S1)
				{
					return -b.Dir;
				}
			}
			return 0;
		}

		/// <summary>Whether road <paramref name="road"/> has a bay.</summary>
		internal static bool Has(int road)
		{
			foreach (Bay b in Bays)
			{
				if (b.Road == road)
				{
					return true;
				}
			}
			return false;
		}
	}
}
