using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Traffic signals: the junctions where gap rules alone cannot work (two busy roads meeting, so the side road's cars never find a
	/// gap in the main road's and queue back through the town) get a light, as real engineers put lights where gap acceptance fails.
	/// No Unity types. Which junctions have one is fixed with the map; each light is actuated, as most real ones are: the main line
	/// keeps its green until a car waits on a side arm (and has had a minimum green), the side green lasts while side cars keep coming
	/// (to a maximum), with amber and a moment of all red between. The day of traffic runs the lights (<see cref="Lights.Step"/>, from
	/// its own cars: deterministic like the rest of the day) and records every change, so the physics cars' driver
	/// (<see cref="TrafficDrive"/>), the drawn lights to come and every machine in a room read the same light from the day's clock.
	///
	/// Phases: the main line's two arms green together (going on and turning right), then, where the main line is the trunk, its left
	/// turns on a green arrow (protected: both left-turn bays together, every through lane red; the owner, 2026-11-25: "dedicated
	/// left-turn lanes with lights that have left-turn arrows"), then the side arms (one, or two facing each other at a crossroads).
	/// The arrow phase and the side phase only come up when a car waits for them. A left turn off the trunk at a light goes only on
	/// its arrow; elsewhere a left on green is permissive (it gives way to the cars coming the other way on the same green). A left-turn
	/// bay off the trunk (<see cref="TurnBays"/>, Kingsbridge's node 6) has an arrow too, protected and permissive: its cars turn on the
	/// main line's green giving way (drawn as a flashing yellow arrow), and one that has waited <see cref="BayArrowDelay"/> without a
	/// gap calls the arrow. A car may go on green, and on amber only if it could not stop comfortably; never on red (but a right turn
	/// from a side road after a stop: <see cref="TrafficDay"/>'s right on red).
	/// </summary>
	internal static class TrafficSignals
	{
		internal const int Green = 0, Amber = 1, Red = 2;

		/// <summary>Seconds: the main line's least green before a waiting side car turns it, the side's least and most green, how long
		/// with no side car coming ends the side's green, amber, and all red after each amber.</summary>
		internal const double MainMin = 22.0, SideMin = 7.0, SideMax = 28.0, SideGapOut = 2.5, AmberTime = 3.5, AllRed = 2.0;

		/// <summary>Seconds: the left arrow's least and most green (the bays hold about five cars each).</summary>
		internal const double ArrowMin = 6.0, ArrowMax = 20.0;

		/// <summary>
		/// Seconds a left turner must have stood waiting in a bay off the trunk (its left permissive on the main line's green) before it calls
		/// the arrow: the detector's delay, as at real permissive bays, so the arrow (which holds the main line red both ways) comes up only
		/// for a car that cannot find a gap, not for every car that slows for one. The trunk's arrows are called at once (no permissive left).
		/// </summary>
		internal static double BayArrowDelay = 8.0;

		/// <summary>
		/// A lead left at a bay off the trunk (<see cref="BayArm"/>): when its arrow is called, only the oncoming main-line arm stops (its
		/// amber, step 9, and all red, step 10); the bay's own arm keeps its through green beside the arrow, as at a real lead-left signal,
		/// instead of both main-line arms held red for the arrow. Off: both held (to measure; trafficview TV_LEAD=1).
		/// </summary>
		internal static bool LeadLeft = false;

		/// <summary>Whether junction <paramref name="node"/>'s bay has a lead left (<see cref="LeadLeft"/>).</summary>
		private static bool Lead(int node)
		{
			return LeadLeft && BayArm(node) >= 0 && !TrunkArrows(node);
		}

		/// <summary>The phases a movement goes on: the main line going on or turning right, the side arms, the main line's left turns on their arrow.</summary>
		internal const int MainPhase = 0, SidePhase = 1, ArrowPhase = 2;

		/// <summary>How near its line a side car is seen by the light (a loop in the road), and keeps the side's green going.</summary>
		internal const double Detect = 70.0;

		/// <summary>The hardest braking a driver takes for an amber (about 3 m/s²): one who would need more goes through.</summary>
		internal const double AmberBrake = 0.6 * TrafficDay.Brake;

		/// <summary>Per node: its light's index in <see cref="Nodes"/> (-1: no light).</summary>
		private static int[] index = new int[0];

		/// <summary>The junctions with a light, in node order (for trafficview, and the lights to be drawn).</summary>
		internal static readonly List<int> Nodes = new List<int>();

		/// <summary>Per node: the main-line arm with a left-turn bay off the trunk up to it (<see cref="TurnBays"/>), or -1.</summary>
		private static int[] bayArm = new int[0];

		/// <summary>
		/// Which junctions get a light: three or more arms with a main line, where the main line is a primary road or the trunk and the
		/// busiest side arm a secondary road or bigger (the links into the towns meeting the trunk, the towns' main roads meeting each
		/// other). Lanes, streets and drives joining a main road keep their give-way rules: few cars, gaps enough.
		/// </summary>
		internal static void Build()
		{
			int n = Traffic.Nodes.Count;
			index = new int[n];
			bayArm = new int[n];
			Nodes.Clear();
			for (int node = 0; node < n; node++)
			{
				index[node] = -1;
				bayArm[node] = -1;
				List<int> links = Traffic.Links[node];
				TrafficDay.MainOf(node, out int a, out int b);
				if (links.Count < 3 || a < 0)
				{
					continue;
				}
				RoadKind main = (RoadKind)Math.Max((int)KindOf(links[a]), (int)KindOf(links[b]));
				RoadKind side = RoadKind.Drive;
				for (int arm = 0; arm < links.Count; arm++)
				{
					if (arm != a && arm != b && KindOf(links[arm]) < side)
					{
						side = KindOf(links[arm]);
					}
				}
				if (main > RoadKind.Primary || side > RoadKind.Secondary)
				{
					continue;
				}
				index[node] = Nodes.Count;
				Nodes.Add(node);
				// A left-turn bay on a main arm, ending at this junction, for the cars coming in along it.
				foreach (int arm in new[] { a, b })
				{
					Traffic.Edge e = Traffic.Edges[links[arm]];
					double js = e.B == node ? e.Sb : e.Sa, from = e.B == node ? e.Sa : e.Sb;
					if (TurnBays.At(e.Road, js > from ? 1 : -1, js) >= 0)
					{
						bayArm[node] = arm;
					}
				}
			}
		}

		/// <summary>The main-line arm of junction <paramref name="node"/> with a left-turn bay off the trunk up to it, or -1.</summary>
		internal static int BayArm(int node)
		{
			return node >= 0 && node < bayArm.Length ? bayArm[node] : -1;
		}

		private static RoadKind KindOf(int edge)
		{
			return Network.Roads[Traffic.Edges[edge].Road].Kind;
		}

		/// <summary>Whether junction <paramref name="node"/> has a light.</summary>
		internal static bool Has(int node)
		{
			return node >= 0 && node < index.Length && index[node] >= 0;
		}

		/// <summary>Its light's index (-1: none).</summary>
		internal static int IndexOf(int node)
		{
			return node >= 0 && node < index.Length ? index[node] : -1;
		}

		/// <summary>The phase arm <paramref name="arm"/> of node <paramref name="node"/> goes on: 0 the main line, 1 the side arms.</summary>
		internal static int PhaseOf(int node, int arm)
		{
			TrafficDay.MainOf(node, out int a, out int b);
			return arm == a || arm == b ? 0 : 1;
		}

		/// <summary>Whether junction <paramref name="node"/>'s light has left arrows: its main line is the trunk (which has left-turn bays), or a
		/// main arm has a bay off the trunk (<see cref="BayArm"/>).</summary>
		internal static bool Arrows(int node)
		{
			return TrunkArrows(node) || (Has(node) && BayArm(node) >= 0);
		}

		/// <summary>Whether junction <paramref name="node"/>'s main line is the trunk, with arrows for its bays (protected only).</summary>
		private static bool TrunkArrows(int node)
		{
			if (!Has(node))
			{
				return false;
			}
			TrafficDay.MainOf(node, out int a, out int b);
			List<int> links = Traffic.Links[node];
			return a >= 0 && b >= 0 && KindOf(links[a]) == RoadKind.Trunk && KindOf(links[b]) == RoadKind.Trunk;
		}

		/// <summary>The phase the movement in along <paramref name="inArm"/> and out along <paramref name="outArm"/> goes on (outArm -1: going on).</summary>
		internal static int PhaseOf(int node, int inArm, int outArm)
		{
			int p = PhaseOf(node, inArm);
			if (p != MainPhase || outArm < 0 || !TrafficDay.LeftTurn(node, inArm, outArm))
			{
				return p;
			}
			return TrunkArrows(node) || (Has(node) && BayArm(node) == inArm) ? ArrowPhase : p;
		}

		/// <summary>
		/// Whether the movement is a left turn from a bay off the trunk: on its arrow, and also on the main line's green, giving way to
		/// the cars coming the other way (protected and permissive, as most American left-turn bays with arrows at a T: the arrow only
		/// comes up when a car waits in the bay). The trunk's lefts go only on their arrow (four lanes to cross).
		/// </summary>
		internal static bool Permissive(int node, int inArm, int outArm)
		{
			return !TrunkArrows(node) && Has(node) && BayArm(node) == inArm && outArm >= 0 && TrafficDay.LeftTurn(node, inArm, outArm);
		}

		/// <summary>Whether two movements at a junction with a light are green at different times (their cars never meet but in the all red).</summary>
		internal static bool Apart(int node, int inA, int outA, int inB, int outB)
		{
			if (!Has(node))
			{
				return false;
			}
			int a = PhaseOf(node, inA, outA), b = PhaseOf(node, inB, outB);
			// A permissive left is green with the main line too.
			if ((Permissive(node, inA, outA) && b == MainPhase) || (Permissive(node, inB, outB) && a == MainPhase))
			{
				return false;
			}
			return a != b;
		}

		/// <summary>
		/// Whether a car at speed <paramref name="v"/>, <paramref name="toLine"/> from the stop line, may go into the junction on a light
		/// showing <paramref name="state"/>: on green; on amber only if it could not stop at the line braking comfortably; never on red.
		/// </summary>
		internal static bool MayGo(int state, double v, double toLine)
		{
			return state == Green || (state == Amber && toLine < v * v / (2.0 * AmberBrake));
		}

		/// <summary>
		/// One day's lights: each one's step in its cycle (0 main green, 1 main amber, 2 all red, 3 left arrow green, 4 its amber, 5 all
		/// red, 6 side green, 7 side amber, 8 all red; 9 and 10 a lead left's oncoming amber and all red before 3; the arrow's and the
		/// side's steps skipped when nobody waits for them), run from the
		/// day's cars, and every change recorded (the day's clock, and the step it went to), so the light at any time can be read back.
		/// </summary>
		internal sealed class Lights
		{
			private readonly int[] step;
			private readonly double[] since, lastSeen, leftSeen, leftSince;
			private readonly List<double>[] changes;
			private readonly List<byte>[] steps;

			internal Lights()
			{
				int n = Nodes.Count;
				step = new int[n];
				since = new double[n];
				lastSeen = new double[n];
				leftSeen = new double[n];
				leftSince = new double[n];
				changes = new List<double>[n];
				steps = new List<byte>[n];
				for (int i = 0; i < n; i++)
				{
					since[i] = double.MinValue;
					lastSeen[i] = double.MinValue;
					leftSeen[i] = double.MinValue;
					leftSince[i] = double.MaxValue;
					changes[i] = new List<double>();
					steps[i] = new List<byte>();
				}
			}

			/// <summary>Changes made in the day, all lights.</summary>
			internal int Changes
			{
				get
				{
					int c = 0;
					foreach (List<double> l in changes)
					{
						c += l.Count;
					}
					return c;
				}
			}

			/// <summary>
			/// The lights a step on at day clock <paramref name="w"/>: <paramref name="waiting"/>[i] whether a car waits at light i's side
			/// arm (near its line), <paramref name="coming"/>[i] whether one is coming up to it within <see cref="Detect"/>; the same for
			/// cars turning left off its main line (<paramref name="leftWaiting"/>, <paramref name="leftComing"/>: only lights with arrows).
			/// </summary>
			internal void Step(double w, bool[] waiting, bool[] coming, bool[] leftWaiting, bool[] leftComing)
			{
				for (int i = 0; i < step.Length; i++)
				{
					if (coming[i] || waiting[i])
					{
						lastSeen[i] = w;
					}
					if (leftComing[i] || leftWaiting[i])
					{
						leftSeen[i] = w;
					}
					// A bay off the trunk's left turner calls its arrow only once it has stood waiting for the detector's delay.
					leftSince[i] = !leftWaiting[i] ? double.MaxValue : Math.Min(leftSince[i], w);
					bool leftCalls = leftWaiting[i] && (BayArm(Nodes[i]) < 0 || w - leftSince[i] >= BayArrowDelay);
					double t = w - since[i];
					int to = -1;
					switch (step[i])
					{
						case 0:
							if ((waiting[i] || leftCalls) && t >= MainMin)
							{
								// A lead left goes straight to its arrow, the oncoming arm alone stopped; the side follows it if called.
								to = leftCalls && Lead(Nodes[i]) ? 9 : 1;
							}
							break;
						case 9:
							if (t >= AmberTime)
							{
								to = 10;
							}
							break;
						case 10:
							if (t >= AllRed)
							{
								to = 3;
							}
							break;
						case 3:
							if (t >= ArrowMax || (t >= ArrowMin && w - leftSeen[i] >= SideGapOut))
							{
								to = 4;
							}
							break;
						case 6:
							if (t >= SideMax || (t >= SideMin && w - lastSeen[i] >= SideGapOut))
							{
								to = 7;
							}
							break;
						case 1:
						case 4:
						case 7:
							if (t >= AmberTime)
							{
								to = step[i] + 1;
							}
							break;
						case 2:
							if (t >= AllRed)
							{
								// The arrow if a left turner waits, else the side (a side car turned the main line; as the lights without
								// arrows always did).
								to = leftCalls && !Lead(Nodes[i]) ? 3 : 6;
							}
							break;
						case 5:
							if (t >= AllRed)
							{
								// After the arrow, the side only if a side car is there (the arrow alone may have turned the main line).
								to = waiting[i] || w - lastSeen[i] < SideGapOut ? 6 : 0;
							}
							break;
						default:
							if (t >= AllRed)
							{
								to = 0;
							}
							break;
					}
					if (to >= 0)
					{
						step[i] = to;
						since[i] = w;
						changes[i].Add(w);
						steps[i].Add((byte)to);
					}
				}
			}

			/// <summary>
			/// The light the movement in along arm <paramref name="arm"/> of junction <paramref name="node"/> and out along
			/// <paramref name="outArm"/> (-1: the through heads) shows at day clock <paramref name="w"/> (Green, Amber, Red; Green where
			/// there is no light), as the day ran it.
			/// </summary>
			internal int State(int node, int arm, int outArm, double w)
			{
				int i = IndexOf(node);
				if (i < 0)
				{
					return Green;
				}
				// Changes up to w: the step is the last one's.
				int st = StepAt(i, w);
				switch (PhaseOf(node, arm, outArm))
				{
					case MainPhase:
						if (Lead(node) && arm == BayArm(node))
						{
							// The bay's own arm goes on through its arrow (a lead left).
							return st == 0 || st == 9 || st == 10 || st == 3 ? Green : st == 1 || st == 4 ? Amber : Red;
						}
						return st == 0 ? Green : st == 1 || st == 9 ? Amber : Red;
					case ArrowPhase:
						if (Permissive(node, arm, outArm) && (st <= 1 || st == 9 || st == 10))
						{
							// On the main line's green (giving way), and its amber; still giving way while the oncoming arm clears.
							return st == 1 ? Amber : Green;
						}
						return st == 3 ? Green : st == 4 ? Amber : Red;
					default: return st == 6 ? Green : st == 7 ? Amber : Red;
				}
			}

			/// <summary>Whether the movement's green or amber at day clock <paramref name="w"/> is its protected arrow (false: permissive on the
			/// main line's green, drawn as a flashing yellow arrow, or not green at all).</summary>
			internal bool Protected(int node, int arm, int outArm, double w)
			{
				int i = IndexOf(node);
				if (i < 0 || PhaseOf(node, arm, outArm) != ArrowPhase)
				{
					return false;
				}
				int st = StepAt(i, w);
				return st == 3 || st == 4;
			}

			/// <summary>Light <paramref name="i"/>'s step at day clock <paramref name="w"/>: the last change's up to then.</summary>
			private int StepAt(int i, double w)
			{
				List<double> c = changes[i];
				int lo = 0, hi = c.Count;
				while (lo < hi)
				{
					int mid = (lo + hi) >> 1;
					if (c[mid] <= w)
					{
						lo = mid + 1;
					}
					else
					{
						hi = mid;
					}
				}
				return lo == 0 ? 0 : steps[i][lo - 1];
			}
		}
	}
}
