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
	/// its arrow; elsewhere a left on green is permissive (it gives way to the cars coming the other way on the same green). A car
	/// may go on green, and on amber only if it could not stop comfortably; never on red.
	/// </summary>
	internal static class TrafficSignals
	{
		internal const int Green = 0, Amber = 1, Red = 2;

		/// <summary>Seconds: the main line's least green before a waiting side car turns it, the side's least and most green, how long
		/// with no side car coming ends the side's green, amber, and all red after each amber.</summary>
		internal const double MainMin = 22.0, SideMin = 7.0, SideMax = 28.0, SideGapOut = 2.5, AmberTime = 3.5, AllRed = 2.0;

		/// <summary>Seconds: the left arrow's least and most green (the bays hold about five cars each).</summary>
		internal const double ArrowMin = 6.0, ArrowMax = 20.0;

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

		/// <summary>
		/// Which junctions get a light: three or more arms with a main line, where the main line is a primary road or the trunk and the
		/// busiest side arm a secondary road or bigger (the links into the towns meeting the trunk, the towns' main roads meeting each
		/// other). Lanes, streets and drives joining a main road keep their give-way rules: few cars, gaps enough.
		/// </summary>
		internal static void Build()
		{
			int n = Traffic.Nodes.Count;
			index = new int[n];
			Nodes.Clear();
			for (int node = 0; node < n; node++)
			{
				index[node] = -1;
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
			}
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

		/// <summary>Whether junction <paramref name="node"/>'s light has left arrows: its main line is the trunk (which has left-turn bays).</summary>
		internal static bool Arrows(int node)
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
			return p == MainPhase && outArm >= 0 && Arrows(node) && TrafficDay.LeftTurn(node, inArm, outArm) ? ArrowPhase : p;
		}

		/// <summary>Whether two movements at a junction with a light are green at different times (their cars never meet but in the all red).</summary>
		internal static bool Apart(int node, int inA, int outA, int inB, int outB)
		{
			return Has(node) && PhaseOf(node, inA, outA) != PhaseOf(node, inB, outB);
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
		/// red, 6 side green, 7 side amber, 8 all red; the arrow's and the side's steps skipped when nobody waits for them), run from the
		/// day's cars, and every change recorded (the day's clock, and the step it went to), so the light at any time can be read back.
		/// </summary>
		internal sealed class Lights
		{
			private readonly int[] step;
			private readonly double[] since, lastSeen, leftSeen;
			private readonly List<double>[] changes;
			private readonly List<byte>[] steps;

			internal Lights()
			{
				int n = Nodes.Count;
				step = new int[n];
				since = new double[n];
				lastSeen = new double[n];
				leftSeen = new double[n];
				changes = new List<double>[n];
				steps = new List<byte>[n];
				for (int i = 0; i < n; i++)
				{
					since[i] = double.MinValue;
					lastSeen[i] = double.MinValue;
					leftSeen[i] = double.MinValue;
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
					double t = w - since[i];
					int to = -1;
					switch (step[i])
					{
						case 0:
							if ((waiting[i] || leftWaiting[i]) && t >= MainMin)
							{
								to = 1;
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
								// The arrow if a left turner waits, else the side (which turned the main line), else back to the main line.
								to = leftWaiting[i] ? 3 : waiting[i] || w - lastSeen[i] < SideGapOut ? 6 : 0;
							}
							break;
						case 5:
							if (t >= AllRed)
							{
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
				int st = lo == 0 ? 0 : steps[i][lo - 1];
				switch (PhaseOf(node, arm, outArm))
				{
					case MainPhase: return st == 0 ? Green : st == 1 ? Amber : Red;
					case ArrowPhase: return st == 3 ? Green : st == 4 ? Amber : Red;
					default: return st == 6 ? Green : st == 7 ? Amber : Red;
				}
			}
		}
	}
}
