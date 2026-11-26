using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Traffic signals: the junctions where gap rules alone cannot work (two busy roads meeting, so the side road's cars never find a
	/// gap in the main road's and queue back through the town) get a light, as real engineers put lights where gap acceptance fails.
	/// No Unity types. Which junctions have one is fixed with the map.
	///
	/// Until the traffic rework's lights (docs/TRAFFIC_REWORK.md, phase 4) each runs a fixed-time plan from the race clock, so every
	/// machine in a room shows the same light with no messages: the main line green (going on and turning right), where the main line
	/// is the trunk its left turns on a protected green arrow (both left-turn bays together), then the side arms (one, or two facing
	/// each other at a crossroads), each with amber and a moment of all red. A left-turn bay off the trunk on a main road
	/// (<see cref="TurnBays"/>, Kingsbridge's node 6) turns on the main line's green giving way, shown as a flashing yellow arrow (the
	/// owner, 2026-11-26: the flashing yellow only). Each light's cycle starts at its own offset so neighbouring lights are not in step.
	/// </summary>
	internal static class TrafficSignals
	{
		internal const int Green = 0, Amber = 1, Red = 2;

		/// <summary>Seconds of the fixed-time plan: the main line's green, the trunk's left arrows' green, the side's green, amber, and all red after each amber.</summary>
		internal const double MainGreen = 30.0, ArrowGreen = 10.0, SideGreen = 18.0, AmberTime = 3.5, AllRed = 2.0;

		/// <summary>The phases a movement goes on: the main line going on or turning right, the side arms, the main line's left turns on their arrow.</summary>
		internal const int MainPhase = 0, SidePhase = 1, ArrowPhase = 2;

		/// <summary>Per node: its light's index in <see cref="Nodes"/> (-1: no light).</summary>
		private static int[] index = new int[0];

		/// <summary>The junctions with a light, in node order.</summary>
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
				Junctions.MainOf(node, out int a, out int b);
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
			Junctions.MainOf(node, out int a, out int b);
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
			Junctions.MainOf(node, out int a, out int b);
			List<int> links = Traffic.Links[node];
			return a >= 0 && b >= 0 && KindOf(links[a]) == RoadKind.Trunk && KindOf(links[b]) == RoadKind.Trunk;
		}

		/// <summary>The phase the movement in along <paramref name="inArm"/> and out along <paramref name="outArm"/> goes on (outArm -1: going on).</summary>
		internal static int PhaseOf(int node, int inArm, int outArm)
		{
			int p = PhaseOf(node, inArm);
			if (p != MainPhase || outArm < 0 || !Junctions.LeftTurn(node, inArm, outArm))
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
			return !TrunkArrows(node) && Has(node) && BayArm(node) == inArm && outArm >= 0 && Junctions.LeftTurn(node, inArm, outArm);
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

		/// <summary>The length of junction <paramref name="node"/>'s cycle (seconds): the arrow phase only where the trunk is its main line.</summary>
		internal static double Cycle(int node)
		{
			double c = MainGreen + SideGreen + 2.0 * (AmberTime + AllRed);
			return TrunkArrows(node) ? c + ArrowGreen + AmberTime + AllRed : c;
		}

		/// <summary>Where junction <paramref name="node"/>'s cycle starts (seconds into the race clock): from its node by integer hashing, so neighbouring lights are out of step the same way on every machine.</summary>
		private static double Offset(int node)
		{
			ulong h = Noise.Mix((ulong)node * 0x9E3779B97F4A7C15UL + 0x51A7UL);
			return (h % 10000UL) / 10000.0 * Cycle(node);
		}

		/// <summary>The state phase <paramref name="phase"/> of junction <paramref name="node"/> shows at race clock <paramref name="t"/> (seconds).</summary>
		private static int PhaseState(int node, int phase, double t)
		{
			double cycle = Cycle(node), u = t + Offset(node);
			u -= Math.Floor(u / cycle) * cycle;
			bool arrows = TrunkArrows(node);
			// Main, then (on the trunk) the arrow, then the side: each green, amber, all red.
			double start = 0.0;
			for (int p = 0; p < 3; p++)
			{
				int ph = p == 0 ? MainPhase : p == 1 ? ArrowPhase : SidePhase;
				if (ph == ArrowPhase && !arrows)
				{
					continue;
				}
				double green = ph == MainPhase ? MainGreen : ph == ArrowPhase ? ArrowGreen : SideGreen;
				if (u < start + green + AmberTime + AllRed)
				{
					if (ph != phase)
					{
						return Red;
					}
					return u < start + green ? Green : u < start + green + AmberTime ? Amber : Red;
				}
				start += green + AmberTime + AllRed;
			}
			return Red;
		}

		/// <summary>
		/// The light the movement in along arm <paramref name="arm"/> of junction <paramref name="node"/> and out along
		/// <paramref name="outArm"/> (-1: the through heads) shows at race clock <paramref name="t"/> (Green where there is no light).
		/// A permissive left (a bay off the trunk) shows the main line's state.
		/// </summary>
		internal static int State(int node, int arm, int outArm, double t)
		{
			if (!Has(node))
			{
				return Green;
			}
			if (Permissive(node, arm, outArm))
			{
				return PhaseState(node, MainPhase, t);
			}
			return PhaseState(node, PhaseOf(node, arm, outArm), t);
		}

		/// <summary>Whether the movement's green or amber at race clock <paramref name="t"/> is its protected arrow (false: permissive on the main line's green, drawn as a flashing yellow arrow, or not green at all).</summary>
		internal static bool Protected(int node, int arm, int outArm, double t)
		{
			return Has(node) && !Permissive(node, arm, outArm) && outArm >= 0 && PhaseOf(node, arm, outArm) == ArrowPhase && PhaseState(node, ArrowPhase, t) != Red;
		}
	}
}
