using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic rework's microscopic level (phase 4, <c>docs/TRAFFIC_REWORK.md</c> §4): round each player, the meso's cars driven as lane
	/// agents by one driver model (<see cref="Driver"/>): following the car ahead, slowing for bends, stopping at red lights, giving way at
	/// the conflict areas by gap acceptance (the same areas and the same rules of who gives way as the meso's), keeping junction boxes clear,
	/// changing lanes where their way on needs it (and through cars passing on the trunk), pulling out of drives into a gap and slowing to
	/// turn into their stop.
	///
	/// <para>Where it drives: a junction is micro if a lane into or out of it comes within <see cref="NearR"/> of a player (and stays so until
	/// none is within <see cref="FarR"/>); junctions whose boxes touch (they share conflict areas, or a lane shorter than a car joins them),
	/// and the wrap's two tunnels, go together. A lane is micro if the junction at its end is, so every junction's cars are all micro or all
	/// meso and no lane within <see cref="NearR"/> of a player is meso: a car changes level only further away than that, out of sight.</para>
	///
	/// <para>The boundary (Burghout's rules): the same lane graph and routes in both; every car stays in exactly one lane's queue, micro or
	/// not, so storage and conservation hold across it. A car coming off a meso junction onto a micro lane is placed where the meso has it, at
	/// its speed through the junction (the meso only lets it go with room for it); one coming off a micro junction onto a meso lane goes into
	/// that lane's queue at its real position and time. When a junction changes level its cars change with it: into micro where the meso has
	/// them (standing in its queue or moving at the lane's speed), into meso in their order at their positions.</para>
	///
	/// <para>The cars are decided one by one in index order, each seeing the decisions already made this step, so two cars never both go
	/// thinking the other waits. No Unity types: <c>tools/trafficsim</c> runs it headless round pretend players; the game will drive its
	/// nearest cars as physics cars from the same agents.</para>
	/// </summary>
	internal sealed partial class Micro
	{
		// ---- the figures ---------------------------------------------------------------------------------------------------

		/// <summary>How far a player sees cars (units): the fog hides the world past about 420 m (TileRadius 5).</summary>
		internal const double Sight = 1400.0;
		/// <summary>Within this of a player a junction's lanes are micro; past <see cref="FarR"/> of every player they go back to the meso.</summary>
		internal double NearR = 1900.0, FarR = 2200.0;
		/// <summary>Micro steps per meso step.</summary>
		internal const int Subs = 2;
		internal const double Dt = Meso.Step / Subs;
		/// <summary>How far ahead a driver looks for the car ahead (units).</summary>
		private const double Look = 250.0;
		/// <summary>How far before its stop line a driver starts working out whether it may go.</summary>
		private const double DecideAt = 120.0;
		/// <summary>Under this speed a car is standing.</summary>
		internal const double Standing = 0.3;
		/// <summary>Over this speed a car is on its way (under it, one edging up behind a standing car isn't coming yet).</summary>
		private const double Rolling = 2.0;
		/// <summary>How far ahead of a car's front the cars it gives way to are looked for (along their lanes before the junction).</summary>
		private const double Approach = 600.0;
		/// <summary>How long the drawn car takes to settle onto its line where two pieces of the lane graph don't quite meet (seconds).</summary>
		private const double Settle = 0.75;
		/// <summary>How long a car waits to merge into a stream before the next driver in it lets it in (seconds).</summary>
		private const double Courtesy = 15.0;

		/// <summary>Why a car brakes (or stands): the reason chain trafficsim follows to tell a queue from a car standing for nothing.</summary>
		internal enum Why
		{
			None,
			/// <summary>The car ahead (<see cref="Agent.ReasonCar"/>).</summary>
			Leader,
			/// <summary>Its light is red.</summary>
			Light,
			/// <summary>Giving way to <see cref="Agent.ReasonCar"/> at a conflict area.</summary>
			Yield,
			/// <summary>No room past the junction (the box is kept clear): the last car on the lane out is <see cref="Agent.ReasonCar"/>.</summary>
			Room,
			/// <summary>Waiting for a gap to change into the lane its way on needs (<see cref="Agent.ReasonCar"/> in the way).</summary>
			Change,
			/// <summary>Slowing to turn into its stop.</summary>
			Stop,
			/// <summary>Slowing for a bend or a turn.</summary>
			Bend,
		}

		/// <summary>A micro car's state. Its identity, plans and route are its <see cref="Meso.Vehicle"/>.</summary>
		internal sealed class Agent
		{
			/// <summary>The lane it is on (-1 on a connector) and the connector it is on (-1 on a lane).</summary>
			public int Lane = -1, Conn = -1;
			/// <summary>On a lane: its front's distance from its carriageway's start. On a connector: how far its front is past the stop line (its middle that far along the connector's line).</summary>
			public double X;
			public double V, A;
			/// <summary>Changing lanes: the lane it is moving over from, how long the change has still to go and how long it takes.</summary>
			public int ChangeFrom = -1;
			public double ChangeLeft, ChangeSpan = Driver.ChangeTime;
			/// <summary>It has decided to go through the junction ahead and is too close to stop: others give way to it.</summary>
			public bool Committed;
			/// <summary>Its last decision about the junction ahead.</summary>
			public bool Go;
			public Why Reason;
			public int ReasonCar = -1;
			/// <summary>When it last came to a stand (-1 moving), and whether it has stood on the lane it is on.</summary>
			public double StoodSince = -1.0;
			public bool Stood;
			/// <summary>Where its drawn middle is off its line, settling to nothing over <see cref="Settle"/> (<see cref="OffLeft"/> to go).</summary>
			public double OffX, OffZ, OffLeft;
			/// <summary>Driven by a body (a physics car, <see cref="TakeBody"/>): its place and speed read from the body, not worked out; how long the body has strayed off its line.</summary>
			public bool Driven;
			public double StrayedFor;
			/// <summary>A body knocked off its line finding its way back to it (<see cref="MicroPhysics"/>): how far off it is (left +), the best it
			/// has got back to (<see cref="Micro.StrayScore"/>) and how long since it last got nearer, how long it has lain on its side.</summary>
			public bool Astray;
			public double Off, StrayBest, NoNearer, OnSide;
		}

		/// <summary>What happens at the boundary, for trafficsim's checks.</summary>
		internal enum Kind
		{
			/// <summary>A car off a meso junction placed on a micro lane.</summary>
			In,
			/// <summary>A car off a micro junction put into a meso lane's queue.</summary>
			Out,
			/// <summary>A car pulling out from where it was parked.</summary>
			PullOut,
			/// <summary>A car reaching its stop (parking).</summary>
			Arrived,
			/// <summary>A junction turned micro: its cars placed where the meso had them.</summary>
			FlipIn,
			/// <summary>A junction turned meso: its cars put into the queues.</summary>
			FlipOut,
			/// <summary>A physics car left the day (<see cref="LoseCar"/>): wrecked, on its side, or stuck off its line.</summary>
			Lost,
			/// <summary>A physics car knocked off its line set out to find its way back to it.</summary>
			Astray,
			/// <summary>A physics car knocked off its line got back to it.</summary>
			Rejoined,
		}

		internal struct Event
		{
			public Kind Kind;
			public int Car;
			public double T, X, Z;
		}

		// ---- state ---------------------------------------------------------------------------------------------------------

		internal readonly Meso M;
		/// <summary>Per lane, whether the micro level drives it (the junction at its end is micro).</summary>
		internal readonly bool[] LaneMicro;
		private readonly bool[] groupMicro;
		/// <summary>Per car, its agent while it is micro (null otherwise).</summary>
		internal readonly Agent[] Agents;
		/// <summary>The micro cars, in index order.</summary>
		internal readonly List<int> Active = new List<int>();
		private readonly List<int>[] onLane, onConn;
		private readonly List<int> laneTouched = new List<int>(), connTouched = new List<int>();
		/// <summary>The micro clock (day seconds), a step behind the meso's while a step runs.</summary>
		internal double T;
		private double nextArea = double.MinValue;
		private bool starting;

		/// <summary>The players' positions; trafficsim moves them through <see cref="Move"/> before each step.</summary>
		internal readonly List<double> PX = new List<double>(), PZ = new List<double>();
		internal Action<Micro> Move;
		/// <summary>Called after every micro step (trafficsim's checks).</summary>
		internal Action<Micro> After;
		internal readonly List<Event> Events = new List<Event>();

		// ---- what trafficsim reads (consistency) ------------------------------------------------------------------------------

		/// <summary>Per lane: cars over its stop line, and the seconds they lost on it over its free time.</summary>
		internal readonly int[] Crossings;
		internal readonly double[] Lost;
		/// <summary>The time between two cars over a stop line at a light, the second having stood in the queue (a saturated flow's headway): sum and count.</summary>
		internal double HeadwaySum;
		internal int Headways;
		/// <summary>The same from the fifth car of a standing queue on (HCM's saturation headway: the first four lose time pulling away).</summary>
		internal double SatSum;
		internal int Sats;
		private readonly double[] lastCross;
		private readonly int[] platoon;
		internal int Missed, Spawned, HandedOut, Flips;
		/// <summary>For trafficsim: the first cars that missed their lane (the car, the lane, when).</summary>
		internal readonly List<(int car, int lane, double t)> MissedAt = new List<(int, int, double)>();

		// ---- the static geometry: lane samples, junction groups ---------------------------------------------------------------

		private const double Cell = 500.0;
		/// <summary>Junctions joined by a lane shorter than this go micro together: a car coming in from the meso at its speed through the
		/// junction behind needs room to stop for the one ahead.</summary>
		private const double Couple = 100.0;
		private static Dictionary<long, List<(int lane, double x, double z)>> samples;
		/// <summary>Per node its group; per group its nodes and the lanes into or out of them.</summary>
		private static int[] groupOf;
		private static List<int>[] groupNodes, groupLanes;
		/// <summary>Per node, the lanes ending at it.</summary>
		private static List<int>[] into;
		/// <summary>Per lane, the bays opening off it.</summary>
		private static List<int>[] baysOf;
		private static readonly object gate = new object();
		private static int builtFor = -1;

		private static void Prepare()
		{
			lock (gate)
			{
				if (builtFor == Lanes.All.Count && samples != null)
				{
					return;
				}
				int nodes = Traffic.Nodes.Count;
				samples = new Dictionary<long, List<(int, double, double)>>();
				into = new List<int>[nodes];
				for (int n = 0; n < nodes; n++)
				{
					into[n] = new List<int>();
				}
				baysOf = new List<int>[Lanes.All.Count];
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					baysOf[l] = new List<int>();
				}
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					if (Lanes.All[l].Parent >= 0)
					{
						baysOf[Lanes.All[l].Parent].Add(l);
					}
				}
				int[] parent = new int[nodes];
				for (int n = 0; n < nodes; n++)
				{
					parent[n] = n;
				}
				int Find(int a)
				{
					while (parent[a] != a)
					{
						parent[a] = parent[parent[a]];
						a = parent[a];
					}
					return a;
				}
				void Union(int a, int b)
				{
					a = Find(a);
					b = Find(b);
					if (a != b)
					{
						// The lower index is the root, so groups come out the same every time.
						if (a < b)
						{
							parent[b] = a;
						}
						else
						{
							parent[a] = b;
						}
					}
				}
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					Lanes.Lane lane = Lanes.All[l];
					into[lane.To].Add(l);
					int from = FromNode(l);
					if (from >= 0 && lane.Length < Couple)
					{
						Union(from, lane.To);
					}
					double len = Math.Abs(lane.End - lane.Start);
					int n = Math.Max(1, (int)Math.Ceiling(len / 25.0));
					for (int k = 0; k <= n; k++)
					{
						Lanes.Point(lane, lane.Start + (lane.End - lane.Start) * k / n, out double x, out double z, out double _, out double _, out double _);
						long key = Key(x, z);
						if (!samples.TryGetValue(key, out List<(int, double, double)> list))
						{
							samples[key] = list = new List<(int, double, double)>();
						}
						list.Add((l, x, z));
					}
				}
				foreach (Conflicts.Area a in Conflicts.All)
				{
					Union(Lanes.Connectors[a.A].Node, Lanes.Connectors[a.B].Node);
				}
				foreach (Lanes.Connector c in Lanes.Connectors)
				{
					if (c.Turn == Lanes.Jump)
					{
						int far = FromNode(c.To);
						if (far >= 0)
						{
							Union(c.Node, far);
						}
					}
				}
				portals = new List<(double, double, double, double)>();
				foreach (Lanes.Connector c in Lanes.Connectors)
				{
					if (c.Turn == Lanes.Jump)
					{
						portals.Add((c.X[0], c.Z[0], c.X[c.X.Length - 1], c.Z[c.Z.Length - 1]));
					}
				}
				groupOf = new int[nodes];
				Dictionary<int, int> ids = new Dictionary<int, int>();
				for (int n = 0; n < nodes; n++)
				{
					int r = Find(n);
					if (!ids.TryGetValue(r, out int g))
					{
						ids[r] = g = ids.Count;
					}
					groupOf[n] = g;
				}
				groupNodes = new List<int>[ids.Count];
				groupLanes = new List<int>[ids.Count];
				for (int g = 0; g < ids.Count; g++)
				{
					groupNodes[g] = new List<int>();
					groupLanes[g] = new List<int>();
				}
				for (int n = 0; n < nodes; n++)
				{
					groupNodes[groupOf[n]].Add(n);
				}
				for (int l = 0; l < Lanes.All.Count; l++)
				{
					int gt = groupOf[Lanes.All[l].To];
					groupLanes[gt].Add(l);
					int from = FromNode(l);
					if (from >= 0 && groupOf[from] != gt)
					{
						groupLanes[groupOf[from]].Add(l);
					}
				}
				builtFor = Lanes.All.Count;
			}
		}

		/// <summary>The junction lane <paramref name="l"/> leaves (a bay's, its through lane's).</summary>
		private static int FromNode(int l)
		{
			Lanes.Lane lane = Lanes.All[l];
			return lane.From >= 0 ? lane.From : lane.Parent >= 0 ? Lanes.All[lane.Parent].From : -1;
		}

		private static long Key(double x, double z)
		{
			long cx = (long)Math.Floor(x / Cell), cz = (long)Math.Floor(z / Cell);
			return (cx << 32) ^ (cz & 0xffffffffL);
		}

		// ---- building --------------------------------------------------------------------------------------------------------

		/// <summary>The micro level over meso <paramref name="m"/> (which drives through it from now on). No junction is micro until there are players.</summary>
		internal Micro(Meso m)
		{
			Prepare();
			M = m;
			m.Near = this;
			LaneMicro = new bool[Lanes.All.Count];
			groupMicro = new bool[groupNodes.Length];
			Agents = new Agent[m.Cars.Length];
			onLane = new List<int>[Lanes.All.Count];
			for (int l = 0; l < onLane.Length; l++)
			{
				onLane[l] = new List<int>();
			}
			onConn = new List<int>[Lanes.Connectors.Count];
			for (int c = 0; c < onConn.Length; c++)
			{
				onConn[c] = new List<int>();
			}
			Crossings = new int[Lanes.All.Count];
			Lost = new double[Lanes.All.Count];
			lastCross = new double[Lanes.All.Count];
			platoon = new int[Lanes.All.Count];
			for (int l = 0; l < lastCross.Length; l++)
			{
				lastCross[l] = double.MinValue;
			}
			T = m.Now;
		}

		// ---- the meso asks -----------------------------------------------------------------------------------------------------

		/// <summary>
		/// Whether a meso car may go through connector <paramref name="c"/> (at a meso junction) onto micro lane <paramref name="lane"/> now:
		/// the last micro car on the lane it comes onto clear of where it will be placed by the standing gap and a second at its speed, and
		/// no other car coming onto it from the meso less than a little over a second before it.
		/// </summary>
		internal bool Room(int lane, int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			int phys = cn.To;
			double entry = Meso.JoinAt(c), speed = Meso.PassSpeed(cn);
			if (Tail(phys, out int j))
			{
				Agent t = Agents[j];
				double rear = t.X - Traffic.CarLength;
				if (rear - entry < Driver.Gap0 + speed * 1.0)
				{
					return false;
				}
			}
			double mine = M.Now + Meso.PassTime(cn);
			foreach (int i in M.Queues[phys].Cars)
			{
				Meso.Vehicle p = M.Cars[i];
				if (p.Micro || p.Conn < 0 || Lanes.Connectors[p.Conn].To != phys)
				{
					continue;
				}
				// Another car on its way in from the meso: where it will be when this one comes onto the lane.
				double xp = p.From + (mine - p.Entered) * Meso.PassSpeed(Lanes.Connectors[p.Conn]);
				if (xp - Traffic.CarLength - entry < Driver.Gap0 + speed * 1.0)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The micro car furthest back on lane <paramref name="lane"/> (changing into it counts), false if none.</summary>
		private bool Tail(int lane, out int car)
		{
			car = -1;
			double best = double.MaxValue;
			foreach (int i in M.Queues[lane].Cars)
			{
				Agent a = Agents[i];
				if (a != null && a.Conn < 0 && a.Lane == lane && a.X < best)
				{
					best = a.X;
					car = i;
				}
			}
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (a.ChangeFrom == lane && a.ChangeLeft > 0.0 && a.X < best)
				{
					best = a.X;
					car = i;
				}
			}
			return car >= 0;
		}

		/// <summary>
		/// Whether a parked car may pull out onto micro lane <paramref name="lane"/>'s carriageway <paramref name="along"/> from its start
		/// now: into its outer lane, clear of the car ahead and with the car behind able to slow for it comfortably.
		/// </summary>
		internal bool CanPullOut(int lane, double along)
		{
			int phys = Meso.LanesOf(Meso.CarriagewayOf(lane))[0];
			return PullGap(phys, along, -1) && !OneTrackBusy(phys, -1, out int _);
		}

		/// <summary>
		/// Whether lane <paramref name="lane"/> is a drive (one track: both ways run down its middle) with a car other than <paramref
		/// name="self"/> on it, or coming onto it, the other way (<paramref name="who"/>): a car going in waits at its mouth, one coming out
		/// waits by the house, as drivers do on a drive one car wide.
		/// </summary>
		private bool OneTrackBusy(int lane, int self, out int who)
		{
			who = -1;
			if (Network.Roads[Traffic.Edges[Lanes.All[lane].Edge].Road].Kind != RoadKind.Drive)
			{
				return false;
			}
			foreach (int l in Meso.LanesOf(Meso.CarriagewayOf(lane) ^ 1))
			{
				foreach (int j in onLane[l])
				{
					if (j != self && Agents[j] != null && Agents[j].Conn < 0 && Agents[j].Lane == l)
					{
						who = j;
						return true;
					}
				}
				foreach (int c in Lanes.All[l].In)
				{
					foreach (int j in onConn[c])
					{
						if (j != self)
						{
							who = j;
							return true;
						}
					}
				}
			}
			return false;
		}

		private bool PullGap(int phys, double along, int self, bool waved = false)
		{
			double limit = Lanes.All[phys].Limit;
			foreach (int i in Active)
			{
				if (i == self)
				{
					continue;
				}
				Agent a = Agents[i];
				bool there = (a.Conn < 0 && a.Lane == phys) || (a.ChangeFrom == phys && a.ChangeLeft > 0.0);
				double x = a.X;
				if (!there && a.Conn >= 0 && Lanes.Connectors[a.Conn].To == phys)
				{
					// Coming onto it through the junction behind.
					Lanes.Connector cn = Lanes.Connectors[a.Conn];
					x = Meso.JoinAt(a.Conn) - (cn.Length - a.X);
					there = true;
				}
				if (!there && a.Conn >= 0 && Lanes.Connectors[a.Conn].From == phys)
				{
					// Just gone over its stop line.
					x = Meso.Reach(phys) + a.X;
					there = true;
				}
				if (!there)
				{
					continue;
				}
				if (x >= along)
				{
					if (x - Traffic.CarLength - along < Driver.Gap0 + 2.0)
					{
						return false;
					}
				}
				else
				{
					double gap = along - Traffic.CarLength - x;
					if (waved && a.V < Rolling && gap > 0.5)
					{
						// Stopped short of it to let it out.
						continue;
					}
					if (gap < Driver.Gap0 || Driver.Follow(a.V, limit, gap, 0.0) < -Driver.Brake)
					{
						return false;
					}
				}
			}
			foreach (int i in M.Queues[phys].Cars)
			{
				Meso.Vehicle p = M.Cars[i];
				if (!p.Micro && i != self && p.Conn >= 0 && p.Entered < M.Now + 3.0 && Math.Abs(p.From - along) < Traffic.CarLength * 3.0)
				{
					return false;
				}
			}
			if (!RoomForBox(phys, along, self) || !ClearOfBox(phys, along, self))
			{
				return false;
			}
			// Cars coming through the junction behind from the lanes before it.
			foreach (int c in Lanes.All[phys].In)
			{
				Lanes.Connector cn = Lanes.Connectors[c];
				double baseX = Meso.JoinAt(c) - cn.Length - Meso.Reach(cn.From);
				foreach (int j in onLane[cn.From])
				{
					// (Listed at the last step: some may have gone since.)
					Agent b = Agents[j];
					if (b == null || j == self || b.Conn >= 0 || b.Lane != cn.From || NextConn(j, cn.From) != c)
					{
						continue;
					}
					double gap = along - Traffic.CarLength - (baseX + b.X);
					if (gap < Look && Driver.Follow(b.V, limit, gap, 0.0) < -Driver.Brake)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Where micro car <paramref name="i"/> is, as <see cref="Meso.Where"/> gives it.</summary>
		internal bool Where(int i, out int lane, out int conn, out double along)
		{
			Agent a = Agents[i];
			lane = M.Cars[i].Lane;
			conn = a.Conn;
			along = a.X;
			return true;
		}

		// ---- stepping ------------------------------------------------------------------------------------------------------

		/// <summary>One meso step's worth: the players moved, the areas worked out (every second), the cars coming in placed, then the micro steps.</summary>
		internal void Step()
		{
			T = M.Now - Meso.Step;
			Move?.Invoke(this);
			if (T >= nextArea)
			{
				starting = nextArea == double.MinValue;
				nextArea = Math.Floor(T) + 1.0;
				Areas();
				starting = false;
			}
			Spawn();
			for (int s = 0; s < Subs; s++)
			{
				LayBodies();
				Sub();
				T += Dt;
				After?.Invoke(this);
			}
		}

		private readonly List<int> scratch = new List<int>();

		private void Sub()
		{
			ReadBodies();
			Index();
			foreach (int i in Active)
			{
				Decide(i);
			}
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (a.Conn >= 0)
				{
					Integrate(a, 1.0);
					continue;
				}
				Integrate(a, double.NaN);
			}
			scratch.Clear();
			scratch.AddRange(Active);
			foreach (int i in scratch)
			{
				if (Agents[i] != null)
				{
					Transitions(i);
				}
			}
		}

		/// <summary>Who is on each lane (and changing out of it) and each connector, in order along it.</summary>
		private void Index()
		{
			foreach (int l in laneTouched)
			{
				onLane[l].Clear();
			}
			foreach (int c in connTouched)
			{
				onConn[c].Clear();
			}
			laneTouched.Clear();
			connTouched.Clear();
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (a.Conn >= 0)
				{
					if (onConn[a.Conn].Count == 0)
					{
						connTouched.Add(a.Conn);
					}
					onConn[a.Conn].Add(i);
					continue;
				}
				if (onLane[a.Lane].Count == 0)
				{
					laneTouched.Add(a.Lane);
				}
				onLane[a.Lane].Add(i);
				if (a.ChangeFrom >= 0 && a.ChangeLeft > 0.0)
				{
					if (onLane[a.ChangeFrom].Count == 0)
					{
						laneTouched.Add(a.ChangeFrom);
					}
					onLane[a.ChangeFrom].Add(i);
				}
			}
			// A car where its lane runs closer than a car's width to the one beside it (a bay's taper) is in that one's way too.
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (a.Conn >= 0)
				{
					continue;
				}
				Lanes.Lane l = Lanes.All[a.Lane];
				foreach (int m in new[] { l.LeftOf, l.RightOf, l.Parent })
				{
					Beside(i, a, m);
				}
				foreach (int m in baysOf[a.Lane])
				{
					Beside(i, a, m);
				}
			}
			foreach (int l in laneTouched)
			{
				onLane[l].Sort(ByX);
			}
			foreach (int c in connTouched)
			{
				onConn[c].Sort(ByX);
			}
		}

		/// <summary>Car <paramref name="i"/> listed on lane <paramref name="m"/> as well if that lane runs within a car's width of its own along its body.</summary>
		private void Beside(int i, Agent a, int m)
		{
			if (m < 0 || m == a.ChangeFrom && a.ChangeLeft > 0.0)
			{
				return;
			}
			if (a.X < Meso.StartOf(m) || a.X - Traffic.CarLength > Meso.Reach(m))
			{
				return;
			}
			int cw = Meso.CarriagewayOf(a.Lane);
			Lanes.Lane l = Lanes.All[a.Lane], o = Lanes.All[m];
			for (int k = 0; k < 3; k++)
			{
				double s = Meso.RoadS(cw, a.X - Traffic.CarLength * 0.5 * k);
				if (Math.Abs(Lanes.Offset(l, s) - Lanes.Offset(o, s)) < 2.0 * Conflicts.HalfWidth + 1.5)
				{
					if (onLane[m].Count == 0)
					{
						laneTouched.Add(m);
					}
					onLane[m].Add(i);
					return;
				}
			}
		}

		private int ByX(int a, int b)
		{
			int k = Agents[a].X.CompareTo(Agents[b].X);
			return k != 0 ? k : a.CompareTo(b);
		}

		/// <summary>
		/// How much longer lane <paramref name="lane"/> runs than its road's distance at <paramref name="x"/> (a car's middle there): more than
		/// 1 on the outside of a bend, less on the inside. A car moves along its lane at its speed, so its distance along the road by this less.
		/// </summary>
		private static double Stretch(int lane, double x)
		{
			int cw = Meso.CarriagewayOf(lane);
			Lanes.Lane l = Lanes.All[lane];
			double s = Meso.RoadS(cw, x - Lanes.Axle);
			Lanes.Point(l, s - 1.0, out double ax, out double az, out double _, out double _, out double _);
			Lanes.Point(l, s + 1.0, out double bx, out double bz, out double _, out double _, out double _);
			double k = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) * 0.5;
			return Math.Max(0.25, Math.Min(4.0, k));
		}

		private void Integrate(Agent a, double stretch)
		{
			if (a.Driven)
			{
				// A physics car: where it is and its speed were read from its body (ReadBodies).
				Tick(a);
				return;
			}
			double v1 = a.V + a.A * Dt;
			double dx;
			if (v1 < 0.0)
			{
				dx = a.A < 0.0 ? a.V * a.V / (-2.0 * a.A) : 0.0;
				v1 = 0.0;
			}
			else
			{
				dx = (a.V + v1) * 0.5 * Dt;
			}
			if (double.IsNaN(stretch))
			{
				// Along a lane: its distance along the road by the lane's stretch, a piece at a time where that changes (round a kink in
				// the road's line it changes within a car's length).
				double k0 = Stretch(a.Lane, a.X), k1 = Stretch(a.Lane, a.X + dx / k0);
				if (Math.Abs(k0 - 1.0) < 0.02 && Math.Abs(k1 - 1.0) < 0.02)
				{
					a.X += dx;
				}
				else
				{
					double left = dx;
					for (int guard = 0; guard < 64 && left > 1e-9; guard++)
					{
						double k = Stretch(a.Lane, a.X + 0.5);
						double piece = Math.Min(left, k);
						a.X += piece / k;
						left -= piece;
					}
				}
			}
			else
			{
				a.X += dx / stretch;
			}
			a.V = v1;
			Tick(a);
		}

		/// <summary>The rest of a step's moving: the drawn settling, a lane change's progress, standing.</summary>
		private void Tick(Agent a)
		{
			if (a.OffLeft > 0.0)
			{
				a.OffLeft = Math.Max(0.0, a.OffLeft - Dt);
			}
			if (a.ChangeLeft > 0.0)
			{
				a.ChangeLeft -= Dt;
				if (a.ChangeLeft <= 0.0)
				{
					a.ChangeLeft = 0.0;
					a.ChangeFrom = -1;
				}
			}
			if (a.V < Standing)
			{
				if (a.StoodSince < 0.0)
				{
					a.StoodSince = T;
				}
				a.Stood = true;
			}
			else
			{
				a.StoodSince = -1.0;
			}
		}

		// ---- the way on ------------------------------------------------------------------------------------------------------

		/// <summary>A through car's way on from lane <paramref name="lane"/>: straight on (or through the wrap) onto the trunk, in its own lane if it can.</summary>
		private static int ThroughFrom(int lane)
		{
			Lanes.Lane from = Lanes.All[lane];
			int best = -1, bestScore = int.MaxValue;
			foreach (int c in from.Out)
			{
				Lanes.Connector cn = Lanes.Connectors[c];
				Lanes.Lane to = Lanes.All[cn.To];
				if ((cn.Turn != Lanes.Straight && cn.Turn != Lanes.Jump) || Network.Roads[Traffic.Edges[to.Edge].Road].Kind != RoadKind.Trunk)
				{
					continue;
				}
				int score = to.Index == from.Index ? 0 : 1;
				if (score < bestScore)
				{
					bestScore = score;
					best = c;
				}
			}
			return best;
		}

		/// <summary>The connector car <paramref name="i"/> takes at the end of lane <paramref name="lane"/> (from that lane if it can: an equivalent of its route's), or -1 (its stop is on the lane).</summary>
		private int NextConn(int i, int lane)
		{
			Meso.Vehicle v = M.Cars[i];
			if (v.Through)
			{
				return ThroughFrom(lane);
			}
			if (v.Next >= v.Path.Count)
			{
				return -1;
			}
			return Pick(v, lane, v.Next);
		}

		/// <summary>
		/// The connector car <paramref name="v"/> takes from lane <paramref name="lane"/> for its route's connector <paramref name="idx"/>:
		/// one doing the same (the same turn onto the same carriageway) from which the car can still follow the rest of its route, from this
		/// lane if there is one (the route's own first), else from another lane of the carriageway (the car must move over to it first),
		/// else the route's own. The meso routes from lane to lane changing anywhere along a stretch, so a route's connector may land in the
		/// wrong lane for the turn after; where the lane after a junction is too short to change lanes on (two junctions close together),
		/// a car taking it missed its turn at the next.
		/// </summary>
		private static int Pick(Meso.Vehicle v, int lane, int idx)
		{
			int c = v.Path[idx];
			int cw = Meso.CarriagewayOf(Lanes.Connectors[c].To);
			int turn = Lanes.Connectors[c].Turn;
			int First(int from)
			{
				if (Lanes.Connectors[c].From == from && CanFollow(v, Lanes.Connectors[c].To, idx + 1, 0))
				{
					return c;
				}
				foreach (int o in Lanes.All[from].Out)
				{
					Lanes.Connector on = Lanes.Connectors[o];
					if (on.Turn == turn && Meso.CarriagewayOf(on.To) == cw && CanFollow(v, on.To, idx + 1, 0))
					{
						return o;
					}
				}
				return -1;
			}
			int e = First(lane);
			if (e >= 0)
			{
				return e;
			}
			e = First(Lanes.Connectors[c].From);
			if (e >= 0)
			{
				return e;
			}
			foreach (int l in Meso.LanesOf(Meso.CarriagewayOf(lane)))
			{
				e = First(l);
				if (e >= 0)
				{
					return e;
				}
			}
			return c;
		}

		/// <summary>
		/// Whether a car on lane <paramref name="lane"/> can follow its route from connector <paramref name="idx"/> on: by a connector from
		/// this lane doing the same from which it can go on, or with room to change over to a lane that has one (the lanes after short ones
		/// looked at too, <paramref name="depth"/> deep), <see cref="Meso.ChangeRoom"/> for each lane crossed: a driver gets into the lane
		/// for a turn a few junctions on early.
		/// </summary>
		private static bool CanFollow(Meso.Vehicle v, int lane, int idx, int depth)
		{
			if (idx >= v.Path.Count || depth > 6)
			{
				return true;
			}
			Lanes.Lane l = Lanes.All[lane];
			List<int> lanes = Meso.LanesOf(Meso.CarriagewayOf(lane));
			if (Meso.Reach(lane) - l.Hold >= (lanes.Count - 1) * Meso.ChangeRoom(l))
			{
				// Room to get into any of its carriageway's lanes.
				return true;
			}
			int c = v.Path[idx];
			int cw = Meso.CarriagewayOf(Lanes.Connectors[c].To);
			int turn = Lanes.Connectors[c].Turn;
			bool Does(int from)
			{
				foreach (int o in Lanes.All[from].Out)
				{
					Lanes.Connector on = Lanes.Connectors[o];
					if (on.Turn == turn && Meso.CarriagewayOf(on.To) == cw && CanFollow(v, on.To, idx + 1, depth + 1))
					{
						return true;
					}
				}
				return false;
			}
			if (Does(lane))
			{
				return true;
			}
			foreach (int m in lanes)
			{
				if (m == lane)
				{
					continue;
				}
				double room = Math.Min(Meso.Reach(lane), Meso.Reach(m)) - Math.Max(Meso.StartOf(lane), Meso.StartOf(m)) - l.Hold;
				if (room >= Math.Abs(Lanes.All[m].Index - l.Index) * Meso.ChangeRoom(l) && Does(m))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The lane car <paramref name="i"/> on lane <paramref name="lane"/> must be in for its way on (the lane itself if fine).</summary>
		private int WantLane(int i, int lane)
		{
			Meso.Vehicle v = M.Cars[i];
			Lanes.Lane l = Lanes.All[lane];
			if (v.Through)
			{
				return ThroughFrom(lane) >= 0 ? lane : l.Parent >= 0 ? l.Parent : lane;
			}
			int c = NextConn(i, lane);
			if (c < 0)
			{
				// Its stop is along this carriageway: the outer lane.
				return Meso.LanesOf(Meso.CarriagewayOf(lane))[0];
			}
			return Lanes.Connectors[c].From;
		}

		/// <summary>The lane next to <paramref name="lane"/> towards <paramref name="want"/>, or -1.</summary>
		private static int Toward(int lane, int want)
		{
			Lanes.Lane l = Lanes.All[lane], w = Lanes.All[want];
			if (w.Parent == lane || l.Parent == want)
			{
				return want;
			}
			if (l.Parent >= 0)
			{
				return l.Parent;
			}
			int target = w.Parent >= 0 ? w.Parent : want;
			if (target == lane)
			{
				return want;
			}
			Lanes.Lane t = Lanes.All[target];
			return t.Index > l.Index ? l.LeftOf : l.RightOf;
		}

		// ---- the car ahead ---------------------------------------------------------------------------------------------------

		/// <summary>
		/// The car ahead of car <paramref name="i"/> within <see cref="Look"/>: on its lane (or connector), else past the junction on its way
		/// (on its next connector, then on the lane after). Its gap (bumper to bumper), its speed, and who it is (-1: a meso car, or none).
		/// </summary>
		private bool Leader(int i, int lane, out double gap, out double vLead, out int who)
		{
			Agent a = Agents[i];
			double g0 = double.MaxValue, v0 = 0.0;
			int w0 = -1;
			void Consider(double g, double vl, int w)
			{
				if (g < g0)
				{
					g0 = g;
					v0 = vl;
					w0 = w;
				}
			}
			double base0;
			int c, k;
			bool onIt;
			if (a.Conn < 0)
			{
				foreach ((int wl, double wx) in waving)
				{
					double room = wx - Traffic.CarLength - a.X;
					if (wl == lane && room > 0.0 && Driver.Stopping(a.V, Driver.Brake) + Driver.Gap0 <= room)
					{
						// Letting a car out of a drive ahead.
						Consider(room, 0.0, -1);
					}
				}
				BodiesAhead(lane, -1, a.X, 0.0, Consider);
				if (Ahead(onLane[lane], i, a.X, out int j))
				{
					Consider(Agents[j].X - Traffic.CarLength - a.X, Agents[j].V, j);
					gap = g0;
					vLead = v0;
					who = w0;
					return g0 < Look;
				}
				base0 = Meso.Reach(lane) - a.X;
				c = NextConn(i, lane);
				if (c < 0 || Lanes.Connectors[c].From != lane)
				{
					gap = g0;
					vLead = v0;
					who = w0;
					return g0 < Look;
				}
				k = 0;
				onIt = false;
			}
			else
			{
				c = a.Conn;
				if (Ahead(onConn[c], i, a.X, out int j))
				{
					Consider(Agents[j].X - Traffic.CarLength - a.X, Agents[j].V, j);
				}
				BodiesAhead(-1, c, a.X, 0.0, Consider);
				base0 = -a.X;
				k = -1;
				onIt = true;
			}
			// Along its way ahead, junction by junction, to the first car.
			for (int hop = 0; hop < 6 && base0 < Look && c >= 0; hop++)
			{
				Siblings(c, -base0, i, Consider);
				if (!onIt)
				{
					BodiesAhead(-1, c, 0.0, base0, Consider);
				}
				if (!onIt && onConn[c].Count > 0)
				{
					int f = onConn[c][0];
					Consider(base0 + Agents[f].X - Traffic.CarLength, Agents[f].V, f);
				}
				if (g0 < double.MaxValue)
				{
					break;
				}
				onIt = false;
				Lanes.Connector cn = Lanes.Connectors[c];
				// (Committed, it is as good as in its box: at two junctions decided as one, through the first and on into the second.)
				Merging(c, base0 + cn.Length, i, a.V, a.Conn >= 0 || a.Committed, Consider);
				if (g0 < double.MaxValue)
				{
					break;
				}
				base0 += cn.Length;
				int to = cn.To;
				double entry = Meso.JoinAt(c);
				BodiesAhead(to, -1, entry, base0, Consider);
				if (!LaneMicro[to])
				{
					if (MesoTail(to, out double along, out double vt))
					{
						Consider(base0 + along - entry - Traffic.CarLength, vt, -1);
					}
					break;
				}
				if (onLane[to].Count > 0)
				{
					int f = onLane[to][0];
					Consider(base0 + Agents[f].X - entry - Traffic.CarLength, Agents[f].V, f);
					break;
				}
				base0 += Meso.Reach(to) - entry;
				k++;
				c = Upcoming(i, k, to);
			}
			gap = g0;
			vLead = v0;
			who = w0;
			return g0 < Look;
		}

		/// <summary>
		/// Cars on the other connectors onto the lane after connector <paramref name="c"/> that will be on it first (closer to their ends than
		/// this car, <paramref name="toEnd"/> from the end of its own): ahead of it once they merge, so it follows them in.
		/// </summary>
		private void Merging(int c, double toEnd, int self, double v, bool meIn, Action<double, double, int> consider)
		{
			double mine = toEnd / Math.Max(v, 1.0);
			Lanes.Connector cn = Lanes.Connectors[c];
			foreach (int m in Lanes.All[cn.To].In)
			{
				if (m == c)
				{
					continue;
				}
				double length = Lanes.Connectors[m].Length;
				foreach (int k in onConn[m])
				{
					double left = length - Agents[k].X;
					// Ahead once on the lane if it gets there first (by time: two cars from two junctions can arrive together).
					double theirs = left / Math.Max(Agents[k].V, 1.0);
					// A car in its box goes ahead of one still on its lane; two in their boxes (or committed to them), the one there first.
					if (k != self && (!meIn || (left < toEnd && (theirs < mine || (theirs == mine && k < self)))))
					{
						consider(toEnd - left - Traffic.CarLength, Agents[k].V, k);
					}
				}
			}
		}

		/// <summary>The connector car <paramref name="i"/> takes from lane <paramref name="lane"/> as its <paramref name="k"/>-th next (0: the next one from where it is), or -1.</summary>
		private int Upcoming(int i, int k, int lane)
		{
			Meso.Vehicle v = M.Cars[i];
			if (v.Through)
			{
				return ThroughFrom(lane);
			}
			int idx = v.Next + k;
			if (idx < 0 || idx >= v.Path.Count)
			{
				return -1;
			}
			return Pick(v, lane, idx);
		}

		/// <summary>
		/// Cars ahead of one <paramref name="d"/> along connector <paramref name="c"/> (negative: before its stop line) on the other connectors
		/// from the same lane, while they are still where the two ways share the road (their diverging area): ahead of it in the same lane.
		/// </summary>
		private void Siblings(int c, double d, int self, Action<double, double, int> consider)
		{
			foreach (int ai in Conflicts.Of[c])
			{
				Conflicts.Area area = Conflicts.All[ai];
				if (area.Yield != Conflicts.Follow)
				{
					continue;
				}
				bool meA = area.A == c;
				int o = meA ? area.B : area.A;
				double end = meA ? area.B1 : area.A1;
				foreach (int k in onConn[o])
				{
					double dk = Agents[k].X;
					if (k != self && dk > d && dk <= end)
					{
						consider(dk - Traffic.CarLength - d, Agents[k].V, k);
					}
				}
			}
		}

		/// <summary>
		/// Whether a car pulling out onto lane <paramref name="phys"/> at <paramref name="along"/> is clear of the cars in the box of the
		/// junction the lane starts at: a drive by the junction has it come on a car's length from the box, and a car turning through it
		/// (one not onto this lane, say a right turn round the corner beside it) can pass within a body of there, now or on its way on.
		/// </summary>
		private bool ClearOfBox(int phys, double along, int self)
		{
			int node = FromNode(phys);
			if (node < 0)
			{
				return true;
			}
			Line(new Agent { Lane = phys, X = along, ChangeFrom = -1 }, out double px, out double pz, out double _, out double _);
			const double Near = Traffic.CarLength + 2.0;
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (i == self || a == null)
				{
					continue;
				}
				int c = a.Conn;
				double from = a.X;
				if (c < 0)
				{
					// Committed at its line: in the box in a moment, the whole way through it looked at.
					c = a.Committed ? NextConn(i, a.Lane) : -1;
					from = 0.0;
				}
				if (c < 0 || Lanes.Connectors[c].Node != node)
				{
					continue;
				}
				Lanes.Connector cn = Lanes.Connectors[c];
				for (double d = from; ; d += 4.0)
				{
					OnConnector(cn, Math.Min(d, cn.Length), out double x, out double z, out double _, out double _);
					if ((x - px) * (x - px) + (z - pz) * (z - pz) < Near * Near)
					{
						return false;
					}
					if (d >= cn.Length)
					{
						break;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Whether a car pulling out onto lane <paramref name="phys"/> at <paramref name="along"/> leaves the cars committed through (or in)
		/// a junction's box behind onto it room to get clear of the box (<see cref="RoomPast"/>): else it would take the room they counted on
		/// and leave one standing in the box.
		/// </summary>
		private bool RoomForBox(int phys, double along, int self)
		{
			int coming = 0;
			double entry = double.MaxValue;
			foreach (int c in Lanes.All[phys].In)
			{
				Lanes.Connector cn = Lanes.Connectors[c];
				if (!cn.KeepClear || cn.Turn == Lanes.Jump)
				{
					continue;
				}
				int before = coming;
				foreach (int k in onConn[c])
				{
					coming += k != self ? 1 : 0;
				}
				foreach (int k in onLane[cn.From])
				{
					Agent b = Agents[k];
					if (k != self && b != null && b.Conn < 0 && b.Lane == cn.From && b.Committed && NextConn(k, cn.From) == c)
					{
						coming++;
					}
				}
				if (coming > before)
				{
					entry = Math.Min(entry, Meso.JoinAt(c));
				}
			}
			if (coming == 0 || along < entry)
			{
				return true;
			}
			// The first car standing or moving ahead of where it would come on: it and the cars from the box need room behind it.
			double ahead = Meso.Reach(phys) - Lanes.All[phys].Hold + Traffic.CarLength;
			foreach (int k in onLane[phys])
			{
				Agent b = Agents[k];
				if (k != self && b != null && b.Conn < 0 && b.Lane == phys && b.X > along)
				{
					ahead = Math.Min(ahead, b.X + Driver.Stopping(b.V, Driver.Brake));
				}
			}
			return ahead - Traffic.CarLength - entry >= Driver.Gap0 + Lanes.Axle + (coming + 1) * Meso.Jam;
		}

		/// <summary>The first car in <paramref name="list"/> (ordered along) ahead of <paramref name="x"/>, not car <paramref name="self"/>.</summary>
		private bool Ahead(List<int> list, int self, double x, out int car)
		{
			car = -1;
			foreach (int j in list)
			{
				if (j == self)
				{
					continue;
				}
				double xj = Agents[j].X;
				if (xj > x || (xj == x && j > self))
				{
					car = j;
					return true;
				}
			}
			return false;
		}

		/// <summary>The meso car furthest back on meso lane <paramref name="lane"/>: its front along its carriageway and its speed (the lane's limit if moving).</summary>
		private bool MesoTail(int lane, out double along, out double v)
		{
			along = double.MaxValue;
			v = 0.0;
			bool any = false;
			foreach (int i in M.Queues[lane].Cars)
			{
				Meso.Vehicle mv = M.Cars[i];
				if (mv.Micro)
				{
					continue;
				}
				if (!M.Where(i, out int _, out int conn, out double al) || conn >= 0)
				{
					continue;
				}
				if (al < along)
				{
					along = al;
					double free = Meso.FreeAlong(mv, M.Now, out double fs);
					v = al < free - 0.01 ? 0.0 : fs;
					any = true;
				}
			}
			return any;
		}

		// ---- deciding ------------------------------------------------------------------------------------------------------

		private void Decide(int i)
		{
			Agent a = Agents[i];
			Meso.Vehicle v = M.Cars[i];
			double acc;
			Why why = Why.None;
			int whyCar = -1;
			void Take(double x, Why w, int car)
			{
				if (x < acc)
				{
					acc = x;
					why = w;
					whyCar = car;
				}
			}
			if (a.Conn >= 0)
			{
				Lanes.Connector cn = Lanes.Connectors[a.Conn];
				acc = Driver.Free(a.V, cn.Limit);
				if (Leader(i, -1, out double gap, out double vl, out int who))
				{
					Take(Driver.Follow(a.V, cn.Limit, gap, vl), Why.Leader, who);
				}
				// The lane after: its limit, and if it is short, the junction at its end decided from here.
				Lanes.Lane to = Lanes.All[cn.To];
				double left = cn.Length - a.X;
				Take(Bends(i, a, -1, left), Why.Bend, -1);
				if (LaneMicro[cn.To] && !v.Ends)
				{
					int next = NextConn(i, cn.To);
					double dist = left + Meso.Reach(cn.To) - to.Hold - Meso.JoinAt(a.Conn);
					if (next >= 0 && dist < DecideAt)
					{
						Junction(i, a, cn.To, next, dist, cn.Limit, ref acc, ref why, ref whyCar, true);
					}
				}
				Set(a, acc, why, whyCar);
				return;
			}
			int lane = a.Lane;
			Lanes.Lane l = Lanes.All[lane];
			acc = Driver.Free(a.V, l.Limit);
			if (Leader(i, lane, out double g, out double vlead, out int lw))
			{
				Take(Driver.Follow(a.V, l.Limit, g, vlead), Why.Leader, lw);
			}
			if (a.ChangeFrom >= 0 && a.ChangeLeft > 0.0 && Ahead(onLane[a.ChangeFrom], i, a.X, out int was))
			{
				// Still partly in the lane it is leaving: the car ahead there too.
				Take(Driver.Follow(a.V, l.Limit, Agents[was].X - Traffic.CarLength - a.X, Agents[was].V), Why.Leader, was);
			}
			if (v.Ends)
			{
				// Slows to turn in: a standing obstacle a little past its stop.
				double dist = v.DestAlong - a.X;
				Take(Driver.StopAt(a.V, l.Limit, dist + 2.0), Why.Stop, -1);
			}
			// Lane changes: where its way on needs another lane, and through cars passing on the trunk.
			int want = WantLane(i, lane);
			if (want != lane && a.ChangeLeft <= 0.0)
			{
				int step = Toward(lane, want);
				int blocker = -1;
				if (step >= 0 && CanChange(i, lane, step, out blocker))
				{
					Change(i, a, lane, step);
					lane = step;
					l = Lanes.All[lane];
				}
				else if (a.StoodSince >= 0.0 && T - a.StoodSince > 3.0 && (step < 0 || blocker < 0) && Reroute(i, v, lane))
				{
					// It can't get over at all from where it stands (no gap would do): another way on from the lane it is in, as a driver
					// who missed the turn lane goes on and turns elsewhere.
				}
				else
				{
					// Not yet: stops short of where the lanes part if it must, waiting for a gap.
					double end = Math.Min(Meso.Reach(lane), step >= 0 ? Meso.Reach(step) : Meso.Reach(lane)) - Lanes.Axle - 1.0;
					double dist = end - a.X;
					if (dist < DecideAt)
					{
						Take(Driver.StopAt(a.V, l.Limit, dist), Why.Change, blocker);
					}
					Agent bk = blocker >= 0 ? Agents[blocker] : null;
					if (bk != null && bk.X > a.X - Traffic.CarLength - Driver.Gap0 && a.V > Rolling && dist < 2.0 * Meso.ChangeRoom(l))
					{
						// A car alongside or just ahead in the lane it wants, at much its speed (it could run beside it to the end): it
						// eases off to drop in behind, as a driver does.
						Take(-0.5 * Driver.Brake, Why.Change, blocker);
					}
				}
			}
			else if (v.Through && a.ChangeLeft <= 0.0)
			{
				Pass(i, a, lane);
				lane = a.Lane;
				l = Lanes.All[lane];
			}
			if (a.ChangeLeft > 0.0)
			{
				// A change still going on is finished before the stop line.
				double toEnd = Meso.Reach(lane) - a.X;
				if (toEnd < a.V * a.ChangeLeft + Lanes.Axle)
				{
					Take(Driver.StopAt(a.V, l.Limit, toEnd - 1.0), Why.Change, -1);
				}
			}
			if (!v.Ends)
			{
				int c = NextConn(i, lane);
				if (c >= 0)
				{
					double toLine = Meso.Reach(lane) - a.X;
					Take(Bends(i, a, lane, toLine), Why.Bend, -1);
					if (Lanes.Connectors[c].From == lane)
					{
						Junction(i, a, lane, c, toLine - l.Hold, l.Limit, ref acc, ref why, ref whyCar, true);
					}
				}
			}
			Set(a, acc, why, whyCar);
		}

		/// <summary>Car <paramref name="i"/>'s quickest way on from the end of lane <paramref name="lane"/> as it is (true if that starts from this lane).</summary>
		private bool Reroute(int i, Meso.Vehicle v, int lane)
		{
			if (v.Through)
			{
				return false;
			}
			List<int> old = new List<int>(v.Path);
			int oldNext = v.Next;
			if (M.RouteFrom(v, lane) && v.Path.Count > 0 && Lanes.Connectors[v.Path[0]].From == lane)
			{
				Missed++;
				if (MissedAt.Count < 20)
				{
					MissedAt.Add((i, lane, T));
				}
				return true;
			}
			v.Path.Clear();
			v.Path.AddRange(old);
			v.Next = oldNext;
			return false;
		}

		/// <summary>
		/// The braking car <paramref name="i"/> needs for the bends and slower roads along its way within its braking distance: each connector's
		/// and lane's limit from where it starts. <paramref name="lane"/>: the lane it is on (-1 on a connector), <paramref name="dist"/> to its
		/// end. double.MaxValue if none.
		/// </summary>
		private double Bends(int i, Agent a, int lane, double dist)
		{
			double acc = double.MaxValue;
			double reachFor = Driver.Stopping(a.V, Driver.Brake) + 20.0;
			int c, k;
			if (lane >= 0)
			{
				c = NextConn(i, lane);
				k = 0;
			}
			else
			{
				Lanes.Connector on = Lanes.Connectors[a.Conn];
				acc = Math.Min(acc, Driver.SlowTo(a.V, Lanes.All[on.To].Limit, dist));
				dist += Meso.Reach(on.To) - Meso.JoinAt(a.Conn);
				c = Upcoming(i, 0, on.To);
				k = 0;
			}
			for (int hop = 0; hop < 6 && c >= 0 && dist < reachFor; hop++)
			{
				Lanes.Connector cn = Lanes.Connectors[c];
				acc = Math.Min(acc, Driver.SlowTo(a.V, cn.Limit, dist));
				dist += cn.Length;
				acc = Math.Min(acc, Driver.SlowTo(a.V, Lanes.All[cn.To].Limit, dist));
				dist += Meso.Reach(cn.To) - Meso.JoinAt(c);
				k++;
				c = Upcoming(i, k, cn.To);
			}
			return acc;
		}

		private static void Set(Agent a, double acc, Why why, int car)
		{
			a.A = acc;
			a.Reason = why;
			a.ReasonCar = car;
		}

		/// <summary>
		/// The junction at the end of lane <paramref name="lane"/>, its stop line <paramref name="dist"/> ahead of car <paramref name="i"/>'s
		/// front: whether it may go through connector <paramref name="c"/> (its light, room past the box, the conflict areas), committing once
		/// too close to stop; if not, it stops at the line. <paramref name="mine"/>: the junction is the car's own (decided and committed
		/// here); false when looked at from the connector before a short lane.
		/// </summary>
		private void Junction(int i, Agent a, int lane, int c, double dist, double v0, ref double acc, ref Why why, ref int whyCar, bool mine)
		{
			bool inBox = a.Conn >= 0 && Lanes.Connectors[a.Conn].To == lane && OneBox(a.Conn);
			if (mine && a.Committed)
			{
				// Committed, but the room past the box gone (a queue backing up into it): it stops short if it still can, braking hard.
				int wcc = -1;
				if (!inBox && dist > 0.5 && Driver.Stopping(a.V, Driver.MaxBrake * 0.6) < dist
					&& (!RoomPast(i, c, out wcc, 0, dist) || !Through(i, c, dist, out wcc, out Why _) || !Clear(i, a, c, -(dist + Lanes.All[lane].Hold), out wcc)))
				{
					a.Committed = false;
					a.Go = false;
					double stop = Driver.StopAt(a.V, v0, Math.Max(0.0, dist));
					if (stop < acc)
					{
						acc = stop;
						why = Why.Room;
						whyCar = wcc;
					}
				}
				return;
			}
			if (dist > DecideAt && dist > Driver.Stopping(a.V, Driver.Brake) + 20.0)
			{
				if (mine)
				{
					a.Go = false;
				}
				return;
			}
			Lanes.Connector cn = Lanes.Connectors[c];
			bool go = true;
			Why w = Why.None;
			int wc = -1;
			if (cn.Turn != Lanes.Jump && TrafficSignals.Has(cn.Node) && !inBox)
			{
				// (A car in a box running straight into the next one's, past a lane too short to stand on, is in one junction: it clears it.)
				int state = TrafficSignals.State(cn.Node, cn.InArm, cn.OutArm, T - M.RaceStart);
				if (state == TrafficSignals.Red || (state == TrafficSignals.Amber && Driver.Stopping(a.V, Driver.Brake) <= dist))
				{
					go = false;
					w = Why.Light;
				}
			}
			if (go && !inBox && !RoomPast(i, c, out wc, 0, dist))
			{
				go = false;
				w = Why.Room;
			}
			if (go && !inBox && OneTrackBusy(cn.To, i, out int ow))
			{
				// Into a drive with a car on it coming out: it waits at the mouth for it.
				go = false;
				w = Why.Room;
				wc = ow;
			}
			if (go && mine && !inBox && !Clear(i, a, c, -(dist + Lanes.All[lane].Hold), out wc))
			{
				go = false;
				w = Why.Yield;
			}
			if (go && mine && !inBox && !Through(i, c, dist, out wc, out Why w2))
			{
				go = false;
				w = w2;
			}
			if (mine)
			{
				a.Go = go;
				if (go && (dist <= Driver.Stopping(a.V, Driver.Brake) + 1.0 || dist <= 1.0))
				{
					a.Committed = true;
				}
			}
			if (!go)
			{
				double stop = Driver.StopAt(a.V, v0, Math.Max(0.0, dist));
				if (stop < acc)
				{
					acc = stop;
					why = w;
					whyCar = wc;
				}
			}
		}

		/// <summary>
		/// Where the lane after connector <paramref name="c"/> is too short to stand on clear of both boxes, whether car <paramref name="i"/>
		/// can go on through the junction after it too (its light green, room past it): else it would stand in this one's box.
		/// </summary>
		private bool Through(int i, int c, double dist, out int who, out Why why)
		{
			who = -1;
			why = Why.None;
			Lanes.Connector cn = Lanes.Connectors[c];
			int to = cn.To;
			if (!cn.KeepClear || !LaneMicro[to] || Meso.Reach(to) - Lanes.All[to].Hold - Meso.JoinAt(c) >= Meso.Jam)
			{
				return true;
			}
			Meso.Vehicle v = M.Cars[i];
			int c2 = v.Through ? ThroughFrom(to) : v.Next + 1 < v.Path.Count ? v.Path[v.Next + 1] : -1;
			if (c2 >= 0 && !v.Through)
			{
				c2 = Pick(v, to, v.Next + 1);
			}
			if (c2 < 0)
			{
				return true;
			}
			Lanes.Connector n2 = Lanes.Connectors[c2];
			if (n2.Turn != Lanes.Jump && TrafficSignals.Has(n2.Node) && TrafficSignals.State(n2.Node, n2.InArm, n2.OutArm, T - M.RaceStart) != TrafficSignals.Green)
			{
				why = Why.Light;
				return false;
			}
			int ahead = 0;
			foreach (int k in onConn[c])
			{
				ahead += k != i ? 1 : 0;
			}
			foreach (int k in onLane[to])
			{
				ahead += k != i ? 1 : 0;
			}
			// Its front to the second junction's stop line: to this one's (dist, short of its hold), through it, along the short lane.
			double toLine2 = dist + cn.Length + Meso.Reach(to) - Meso.JoinAt(c);
			if (!RoomPast(i, c2, out who, ahead, toLine2))
			{
				why = Why.Room;
				return false;
			}
			// And clear of the cars at the second junction: it won't stop between them.
			if (!Clear(i, Agents[i], c2, -toLine2, out who))
			{
				why = Why.Yield;
				return false;
			}
			return true;
		}

		/// <summary>Whether there is room for car <paramref name="i"/> past connector <paramref name="c"/>'s box (kept clear: a car only goes in if it can get out), else the car in the way.</summary>
		private bool RoomPast(int i, int c, out int who, int extra = 0, double before = 0.0)
		{
			who = -1;
			Lanes.Connector cn = Lanes.Connectors[c];
			if (!cn.KeepClear || cn.Turn == Lanes.Jump)
			{
				return true;
			}
			// How long before this car would be through the box (its front <paramref name="before"/> short of its stop line): the last car
			// on the lane out gets at least as far as it would braking comfortably from now, or at its speed till then.
			Agent me = Agents[i];
			double through = Driver.Soonest(Math.Max(0.0, before) + cn.Length, me.V, cn.Limit);
			double Gone(double v, double x)
			{
				// No further than its own stop line, where it may have to stop.
				return Math.Max(0.0, Math.Min(Math.Min(v * through, Driver.Stopping(v, Driver.Brake)), Meso.Reach(cn.To) - Lanes.All[cn.To].Hold - x));
			}
			int to = cn.To;
			double entry = Meso.JoinAt(c);
			// Cars already through the box ahead of it onto the same lane take their room first.
			int going = 0;
			foreach (int o in Lanes.All[to].In)
			{
				foreach (int k in onConn[o])
				{
					going += k != i ? 1 : 0;
				}
			}
			// Its front at the connector's end has its middle at the lane's start: half a car on, it is clear of the box.
			double need = Driver.Gap0 + Lanes.Axle + (going + extra) * Meso.Jam;
			if (!BodiesLeaveRoom(c, to, entry, need, Gone))
			{
				return false;
			}
			if (LaneMicro[to])
			{
				if (onLane[to].Count == 0)
				{
					return true;
				}
				int k = onLane[to][0];
				Agent t = Agents[k];
				who = k;
				return t.X + Gone(t.V, t.X) - Traffic.CarLength - entry >= need;
			}
			if (M.Queues[to].Cars.Count >= M.Queues[to].Storage + (M.Queues[to].Cars.Contains(i) ? 1 : 0))
			{
				return false;
			}
			if (MesoTail(to, out double along, out double vt))
			{
				return along + Gone(vt, along) - Traffic.CarLength - entry >= need;
			}
			return true;
		}

		/// <summary>
		/// Whether connector <paramref name="c"/>'s conflict areas are clear for car <paramref name="i"/>, its front <paramref name="dist"/>
		/// before the stop line: no car in an area, or committed to it, while this one would be; and where it gives way (or the area is first
		/// come first served and the other came first), no car coming that would be in the area within <see cref="Meso.Margin"/> of it. A car
		/// standing at its line and not going is passed by (it will see this one going, as it decides after). Else the car in the way.
		/// </summary>
		private bool Clear(int i, Agent a, int c, double myD, out int who)
		{
			who = -1;
			Lanes.Connector cn = Lanes.Connectors[c];
			foreach (int ai in Conflicts.Of[c])
			{
				Conflicts.Area area = Conflicts.All[ai];
				if (area.Yield == Conflicts.Follow)
				{
					continue;
				}
				bool meA = area.A == c;
				int o = meA ? area.B : area.A;
				double my0 = meA ? area.A0 : area.B0, my1 = meA ? area.A1 : area.B1;
				double o0 = meA ? area.B0 : area.A0, o1 = meA ? area.B1 : area.A1;
				if (myD > my1)
				{
					continue;
				}
				double tIn = myD >= my0 ? 0.0 : Driver.Soonest(my0 - myD, a.V, cn.Limit);
				// Clear of it no sooner than at the turn's limit: a car faster now (coming off a faster connector, two junctions decided as one)
				// slows to it before it gets there.
				double tOut = Driver.Soonest(my1 - myD, Math.Min(a.V, cn.Limit), cn.Limit) + 0.5;
				int mine = meA ? Conflicts.YieldA : Conflicts.YieldB;
				Lanes.Connector oc = Lanes.Connectors[o];
				Coming(o, i, o0);
				foreach ((int j, double dj, bool on, bool direct) in coming)
				{
					Agent b = Agents[j];
					if (dj > o1)
					{
						continue;
					}
					bool inside = dj >= o0;
					// Committed, it is firm while it can move (one committed but held at its line by the queue ahead isn't coming yet).
					bool free = !Held(j, b);
					// (Or committed through the junction before on its way here: two junctions decided as one at the first line.)
					// (Or on a connector whose box runs straight on into this one's: it is in one junction and clears it without stopping.)
					bool firm = inside || on || (direct && b.Committed && free) || (b.Conn >= 0 && (b.Committed || (b.Conn != o && OneBox(b.Conn) && Lanes.Connectors[b.Conn].To == oc.From)));
					// Standing, it comes only if it has decided to and nothing (a queue, a lane change) holds it.
					// Standing at a red that has just turned green, it will go (it hasn't decided again yet: cars decide in turn).
					bool goes = firm || (b.V >= Rolling && free) || (direct && (b.Go || b.Reason == Why.Light) && free);
					if (!goes)
					{
						continue;
					}
					if (!firm && !LightLets(oc, b, -dj))
					{
						// Its light holds it.
						continue;
					}
					double tInJ = inside ? 0.0 : Driver.Soonest(o0 - dj, b.V, oc.Limit);
					double vOutJ = Math.Min(b.V, oc.Limit);
					double tOutJ = vOutJ >= 1.0 ? (o1 - dj) / vOutJ + 0.5 : Driver.Soonest(o1 - dj, b.V, oc.Limit) + 1.0;
					double behind = Meso.Margin;
					if (area.Kind == Conflicts.Merging)
					{
						// Merging ahead of it: room for it to come down to this car's speed comfortably and keep its time gap after.
						double vj = Math.Max(b.V, Math.Min(oc.Limit, b.V + Driver.Accel * tInJ));
						double vMe = Math.Min(cn.Limit, Math.Sqrt(a.V * a.V + 2.0 * Driver.Accel * Math.Max(0.0, my1 - myD)));
						double need = Driver.Gap0 + vMe * Driver.TimeGap + Math.Max(0.0, vj - vMe) * Math.Max(0.0, vj - vMe) / (2.0 * Driver.Brake);
						behind = Math.Max(behind, need / Math.Max(vj, 1.0));
					}
					bool overlap = tInJ < tOut + behind && tIn < tOutJ + Meso.Margin;
					if (!overlap)
					{
						continue;
					}
					if (area.Kind == Conflicts.Merging && !firm && direct && a.StoodSince >= 0.0 && T - a.StoodSince > Courtesy
						&& Driver.Stopping(b.V, Driver.Brake) + Driver.Gap0 < o0 - dj)
					{
						// Waiting long to merge into a stream: the next driver in it, on the lane just before the junction, lets it in (it
						// can slow comfortably), as in a zip merge; once this car is in the box, that one follows it (Merging). Not one a
						// junction or more back: it doesn't see this car as the car ahead in time to slow for it.
						continue;
					}
					if (firm)
					{
						who = j;
						return false;
					}
					if (area.Yield == Conflicts.Apart)
					{
						continue;
					}
					if (direct && b.Reason == Why.Yield && b.StoodSince >= 0.0 && a.StoodSince >= 0.0 && (a.StoodSince < b.StoodSince || (a.StoodSince == b.StoodSince && i < j)))
					{
						// It stands giving way itself (round a loop of drivers each giving way to the next, as at two junctions whose boxes
						// touch): the one waiting longer goes, as drivers sort out a standoff. Going, this one is firm to it.
						continue;
					}
					if (area.Yield == mine)
					{
						who = j;
						return false;
					}
					if (area.Yield == Conflicts.FirstCome)
					{
						// Whoever came first: the one standing longer at its line, else the sooner there; level, the rule of the right.
						bool jFirst;
						if (b.StoodSince >= 0.0 && a.StoodSince >= 0.0 && b.StoodSince != a.StoodSince)
						{
							jFirst = b.StoodSince < a.StoodSince;
						}
						else if (b.StoodSince >= 0.0 != a.StoodSince >= 0.0)
						{
							jFirst = b.StoodSince >= 0.0 && direct && b.Go;
						}
						else
						{
							double ej = -dj / Math.Max(b.V, 1.0), ei = -myD / Math.Max(a.V, 1.0);
							jFirst = Math.Abs(ej - ei) < 0.25 ? area.Tie == mine : ej < ei;
						}
						if (jFirst)
						{
							who = j;
							return false;
						}
					}
				}
			}
			return true;
		}

		/// <summary>Whether car <paramref name="b"/> stands held by something not moving (a light, a car standing ahead, no room, a gap it waits for): it isn't coming yet.</summary>
		private bool Held(int j, Agent b, int depth = 0)
		{
			if (b.V >= Rolling)
			{
				return false;
			}
			switch (b.Reason)
			{
				case Why.None:
				case Why.Bend:
				case Why.Stop:
					return false;
				case Why.Light:
					// Its reason is from its last decision: the light may have turned green since (each car deciding in turn at the change
					// would otherwise take the others as still held, and go together).
					if (b.Conn < 0)
					{
						int nc = NextConn(j, b.Lane);
						if (nc >= 0)
						{
							Lanes.Connector bc = Lanes.Connectors[nc];
							if (bc.Turn == Lanes.Jump || !TrafficSignals.Has(bc.Node) || TrafficSignals.State(bc.Node, bc.InArm, bc.OutArm, T - M.RaceStart) == TrafficSignals.Green)
							{
								return false;
							}
						}
					}
					return true;
				case Why.Leader:
					Agent lead = b.ReasonCar < 0 ? null : Agents[b.ReasonCar];
					if (lead != null && lead.V < Rolling && b.Conn < 0 && lead.Conn < 0 && lead.Lane == b.Lane && depth < 8 && Held(b.ReasonCar, lead, depth + 1))
					{
						// Behind a car standing on its own lane held itself (at the same line, by a light or giving way): it can't come before
						// that one goes, whatever it has decided about its own junction. (One standing beyond the junction is another matter:
						// a car that has decided to go follows it through.)
						return true;
					}
					// Behind a standing car, unless it has decided to go through its junction (following a slow car beyond it).
					return !b.Go && (lead == null || lead.V < Rolling);
				default:
					return true;
			}
		}

		private readonly List<(int car, double d, bool on, bool direct)> coming = new List<(int, double, bool, bool)>();

		/// <summary>
		/// The cars on connector <paramref name="o"/> or coming to it (in <see cref="coming"/>): how far each one's middle is along it
		/// (negative before its stop line), whether it is on it, and whether it is in the lane just before it (its decision there is its
		/// own); back along the carriageway in and, where that is short, through the junction before it, as far as <see cref="Approach"/>
		/// before the area at <paramref name="o0"/>.
		/// </summary>
		private void Coming(int o, int self, double o0)
		{
			coming.Clear();
			foreach (int k in onConn[o])
			{
				if (k != self)
				{
					coming.Add((k, Agents[k].X, true, true));
				}
			}
			// Just off it onto the lane after, the tail still where the areas reach.
			Lanes.Connector oc = Lanes.Connectors[o];
			double join = Meso.JoinAt(o);
			foreach (int k in onLane[oc.To])
			{
				Agent b = Agents[k];
				if (k != self && b != null && b.Conn < 0 && b.Lane == oc.To && M.Cars[k].Conn == o && b.X - join < Traffic.CarLength + 1.0)
				{
					coming.Add((k, oc.Length + b.X - join, true, true));
				}
			}
			via.Clear();
			via.Add(o);
			Back(oc.From, 0.0, self, o0);
		}

		private readonly List<int> via = new List<int>();

		/// <summary>Whether connector <paramref name="c"/>'s box runs straight on into the next junction's, past a lane too short to stand
		/// on: a car through it is in one junction, and clears the next without stopping (<see cref="Junction"/>'s inBox).</summary>
		private static bool OneBox(int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			return cn.KeepClear && Meso.Reach(cn.To) - Lanes.All[cn.To].Hold - Meso.JoinAt(c) < Meso.Jam;
		}

		/// <summary>
		/// The cars on lane <paramref name="lane"/>'s carriageway (its stop line <paramref name="endD"/> along the connector looked at, in
		/// <see cref="via"/>'s last) whose way on is <see cref="via"/>, then back through the junction before it and the cars in that, as far
		/// as <see cref="Approach"/> before the area.
		/// </summary>
		private void Back(int lane, double endD, int self, double o0)
		{
			double reachL = Meso.Reach(lane);
			foreach (int l in Meso.LanesOf(Meso.CarriagewayOf(lane)))
			{
				foreach (int k in onLane[l])
				{
					Agent b = Agents[k];
					if (k == self || b.Conn >= 0 || b.Lane != l)
					{
						continue;
					}
					double d = endD + b.X - reachL;
					if (d >= o0 - Approach && Follows(k, l, -1))
					{
						// Its decision is its own just before the connector looked at, or before the junction before where that one's box
						// runs straight on into this one's (decided as one, Junction's inBox).
						coming.Add((k, d, false, l == lane && (via.Count == 1 || (via.Count == 2 && OneBox(via[0])))));
					}
				}
			}
			if (endD - reachL < o0 - Approach || via.Count >= 4)
			{
				return;
			}
			foreach (int kc in Lanes.All[lane].In)
			{
				Lanes.Connector before = Lanes.Connectors[kc];
				double endK = endD - (reachL - Meso.JoinAt(kc)) - before.Length;
				foreach (int j in onConn[kc])
				{
					if (j != self && Follows(j, -1, kc))
					{
						coming.Add((j, endK + Agents[j].X, false, false));
					}
				}
				via.Insert(0, kc);
				Back(before.From, endK, self, o0);
				via.RemoveAt(0);
			}
		}

		/// <summary>Whether car <paramref name="j"/> (on lane <paramref name="lane"/>, or on connector <paramref name="on"/>) takes the connectors in <see cref="via"/> next, in order.</summary>
		private bool Follows(int j, int lane, int on)
		{
			int from = on >= 0 ? Lanes.Connectors[on].To : lane;
			for (int t = 0; t < via.Count; t++)
			{
				int up = Upcoming(j, t, from);
				if (up != via[t])
				{
					// Its own way on can't be taken from here (a lane too short to change out of): it will take another at the line, perhaps
					// this one, so it is looked for as if it would.
					bool missing = up >= 0 && Lanes.Connectors[up].From != from && Lanes.Connectors[via[t]].From == from;
					if (!missing)
					{
						return false;
					}
				}
				from = Lanes.Connectors[via[t]].To;
			}
			return true;
		}

		/// <summary>Whether connector <paramref name="cn"/>'s light lets car <paramref name="b"/>, <paramref name="dist"/> before the line, go.</summary>
		private bool LightLets(Lanes.Connector cn, Agent b, double dist)
		{
			if (cn.Turn == Lanes.Jump || !TrafficSignals.Has(cn.Node))
			{
				return true;
			}
			int state = TrafficSignals.State(cn.Node, cn.InArm, cn.OutArm, T - M.RaceStart);
			return state == TrafficSignals.Green || (state == TrafficSignals.Amber && Driver.Stopping(b.V, Driver.Brake) > dist);
		}

		// ---- lane changes ----------------------------------------------------------------------------------------------------

		/// <summary>Whether car <paramref name="i"/> on lane <paramref name="from"/> can move over into lane <paramref name="to"/> now: where both run, and safely (MOBIL's rule); else the car in the way.</summary>
		private bool CanChange(int i, int from, int to, out int blocker)
		{
			blocker = -1;
			Agent a = Agents[i];
			double start = Meso.StartOf(to) + Lanes.Axle;
			double room = Math.Min(Meso.Reach(to), Meso.Reach(from)) - a.X;
			if (a.X < start || room < a.V * Driver.ChangeTime + Lanes.Axle)
			{
				// Where both lanes run, and with room to finish the change before the stop line at its speed.
				return false;
			}
			return Safe(i, to, out blocker, out double _);
		}

		/// <summary>MOBIL's safety on lane <paramref name="to"/>: no car alongside, the new follower needn't brake harder than <see cref="Driver.SafeBrake"/>, nor this car behind its new leader; <paramref name="accNew"/> its acceleration there.</summary>
		private bool Safe(int i, int to, out int blocker, out double accNew)
		{
			Agent a = Agents[i];
			blocker = -1;
			double limit = Lanes.All[to].Limit;
			accNew = Driver.Free(a.V, limit);
			int lead = -1, follow = -1;
			foreach (int j in onLane[to])
			{
				if (j == i)
				{
					continue;
				}
				Agent b = Agents[j];
				if (Math.Abs(b.X - a.X) < Traffic.CarLength + Driver.Gap0)
				{
					blocker = j;
					return false;
				}
				if (b.X > a.X)
				{
					if (lead < 0)
					{
						lead = j;
					}
				}
				else
				{
					follow = j;
				}
			}
			foreach (int c in Lanes.All[to].In)
			{
				foreach (int j in onConn[c])
				{
					// Coming onto it through the junction behind: where its front will be on the lane, at its speed.
					Agent b = Agents[j];
					double x = Meso.JoinAt(c) - (Lanes.Connectors[c].Length - b.X);
					if (x > a.X - Traffic.CarLength - Driver.Gap0 || Driver.Follow(b.V, limit, a.X - Traffic.CarLength - x, a.V) < -Driver.SafeBrake)
					{
						blocker = j;
						return false;
					}
				}
			}
			if (lead >= 0)
			{
				Agent b = Agents[lead];
				accNew = Math.Min(accNew, Driver.Follow(a.V, limit, b.X - Traffic.CarLength - a.X, b.V));
				if (accNew < -Driver.SafeBrake)
				{
					blocker = lead;
					return false;
				}
			}
			if (!BodiesLetChange(to, a.X, a.V, limit, ref accNew))
			{
				// A body in the lane it wants (the player's car, a wreck): alongside, or too near ahead to change in front of.
				return false;
			}
			if (follow >= 0)
			{
				Agent b = Agents[follow];
				if (Driver.Follow(b.V, limit, a.X - Traffic.CarLength - b.X, a.V) < -Driver.SafeBrake)
				{
					blocker = follow;
					return false;
				}
			}
			return true;
		}

		private void Change(int i, Agent a, int from, int to)
		{
			a.ChangeFrom = from;
			a.ChangeLeft = a.ChangeSpan = Driver.ChangeTime;
			a.Lane = to;
			MoveQueue(i, from, to);
			// Seen in its new lane from now (it is still in its old one too until the change is done).
			onLane[to].Add(i);
			onLane[to].Sort(ByX);
			if (!laneTouched.Contains(to))
			{
				laneTouched.Add(to);
			}
		}

		/// <summary>A through car on the trunk passing (MOBIL): over to the other through lane if it gains enough, keeping right.</summary>
		private void Pass(int i, Agent a, int lane)
		{
			Lanes.Lane l = Lanes.All[lane];
			if (Network.Roads[Traffic.Edges[l.Edge].Road].Kind != RoadKind.Trunk || l.Kind != Lanes.Through)
			{
				return;
			}
			for (int side = 0; side < 2; side++)
			{
				int to = side == 0 ? l.LeftOf : l.RightOf;
				if (to < 0 || Lanes.All[to].Kind != Lanes.Through || a.X > Meso.Reach(lane) - Math.Max(150.0, a.V * Driver.ChangeTime * 2.0) || a.X < Meso.StartOf(to) + Lanes.Axle)
				{
					continue;
				}
				double accCur = Driver.Free(a.V, l.Limit);
				if (Ahead(onLane[lane], i, a.X, out int j))
				{
					accCur = Math.Min(accCur, Driver.Follow(a.V, l.Limit, Agents[j].X - Traffic.CarLength - a.X, Agents[j].V));
				}
				if (!Safe(i, to, out int _, out double accNew))
				{
					continue;
				}
				double bias = side == 0 ? -Driver.KeepRight : Driver.KeepRight;
				if (accNew - accCur + bias > Driver.Threshold)
				{
					Change(i, a, lane, to);
					return;
				}
			}
		}

		private void MoveQueue(int i, int from, int to)
		{
			if (from == to)
			{
				return;
			}
			M.Queues[from].Cars.Remove(i);
			M.Queues[to].Cars.Add(i);
			M.Cars[i].Lane = to;
		}

		// ---- moving on -------------------------------------------------------------------------------------------------------

		private void Transitions(int i)
		{
			Agent a = Agents[i];
			Meso.Vehicle v = M.Cars[i];
			// Where it is drawn before moving on (a short lane passed in the step isn't).
			Pose(i, out double drawnX, out double drawnZ, out double _, out double _);
			for (int guard = 0; guard < 8; guard++)
			{
				if (a.Conn < 0)
				{
					if (v.Ends)
					{
						if (a.X >= v.DestAlong - 0.5)
						{
							Arrive(i);
						}
						return;
					}
					double end = Meso.Reach(a.Lane);
					if (a.X < end)
					{
						return;
					}
					int c = NextConn(i, a.Lane);
					if (v.Through && c < 0)
					{
						a.X = end;
						a.V = 0.0;
						return;
					}
					if (c < 0 || Lanes.Connectors[c].From != a.Lane)
					{
						// Missed its lane: the quickest way on from where it is.
						Missed++;
						if (MissedAt.Count < 20)
						{
							MissedAt.Add((i, a.Lane, T));
						}
						if (!M.RouteFrom(v, a.Lane) || v.Path.Count == 0)
						{
							a.X = end;
							a.V = 0.0;
							return;
						}
						c = v.Path[0];
					}
					Crossed(a.Lane, a, v, c);
					M.Crossed(a.Lane, v, c);
					if (v.Through)
					{
						v.Path.Clear();
						v.Path.Add(c);
						v.Next = 0;
					}
					else
					{
						v.Path[v.Next] = c;
					}
					v.Next++;
					v.ConnAt = T;
					Lanes.Connector cn = Lanes.Connectors[c];
					double d0 = a.X - end;
					if (Lanes.All[a.Lane].Length < Lanes.Axle && v.Conn >= 0)
					{
						// Through a lane shorter than half a car (two boxes meeting): onto the connector where the car's middle is, as the two
						// connectors needn't meet exactly.
						Lanes.Connector prev = Lanes.Connectors[v.Conn];
						int n = prev.X.Length - 1;
						d0 = Nearest(cn, prev.X[n], prev.Z[n], d0);
					}
					bool settle = Lanes.All[a.Lane].Length < Lanes.Axle || (a.ChangeFrom >= 0 && a.ChangeLeft > 0.0);
					MoveQueue(i, a.Lane, cn.To);
					a.Conn = c;
					a.Lane = -1;
					a.X = d0;
					if (settle)
					{
						SettleFrom(a, drawnX, drawnZ);
					}
					a.ChangeFrom = -1;
					a.ChangeLeft = 0.0;
					a.Committed = false;
					a.Go = false;
					continue;
				}
				Lanes.Connector on = Lanes.Connectors[a.Conn];
				if (a.X < on.Length)
				{
					return;
				}
				int to = on.To;
				double x = Meso.JoinAt(a.Conn) + (a.X - on.Length);
				if (LaneMicro[to])
				{
					M.Counted(to);
					int bay = BayFor(i, to, x);
					if (bay != to)
					{
						MoveQueue(i, to, bay);
					}
					v.Conn = a.Conn;
					a.Conn = -1;
					a.Lane = to;
					a.X = x;
					v.Entered = T;
					v.From = x;
					v.V = a.V;
					a.Stood = false;
					if (bay != to)
					{
						// Turning straight into the bay: a short change over from the lane it came onto.
						a.ChangeFrom = to;
						a.ChangeLeft = a.ChangeSpan = Driver.ChangeTime * 0.5;
						a.Lane = bay;
						to = bay;
					}
					M.Aim(v, to, x);
					continue;
				}
				// Off a micro junction onto a meso lane: into its queue at its real place and time.
				int c0 = a.Conn;
				Log(Kind.Out, i);
				Remove(i, to);
				int exit = M.ExitLane(v, Meso.CarriagewayOf(to));
				M.Enter(v, exit, c0, Math.Min(x, Meso.Reach(exit)), T, true, a.V);
				HandedOut++;
				return;
			}
		}

		/// <summary>The drawn car was at (<paramref name="x"/>, <paramref name="z"/>): it settles from there onto its new line rather than jumping.</summary>
		private static void SettleFrom(Agent a, double x, double z)
		{
			a.OffLeft = 0.0;
			Line(a, out double nx, out double nz, out double _, out double _);
			double dx = x - nx, dz = z - nz;
			if (dx * dx + dz * dz < 20.0 * 20.0)
			{
				a.OffX = dx;
				a.OffZ = dz;
				a.OffLeft = Settle;
			}
		}

		/// <summary>The distance along connector <paramref name="cn"/> nearest (<paramref name="x"/>, <paramref name="z"/>), looked for no further than a little past <paramref name="guess"/>.</summary>
		private static double Nearest(Lanes.Connector cn, double x, double z, double guess)
		{
			double best = guess, bestD = double.MaxValue;
			for (int p = 0; p < cn.X.Length && cn.D[p] <= guess + 4.0; p++)
			{
				double dx = cn.X[p] - x, dz = cn.Z[p] - z, d2 = dx * dx + dz * dz;
				if (d2 < bestD)
				{
					bestD = d2;
					best = cn.D[p];
				}
			}
			return Math.Max(best, guess - 2.0);
		}

		/// <summary>The lane car <paramref name="i"/> comes onto off a connector onto lane <paramref name="to"/> with its front at <paramref name="x"/>: the bay its way on leaves from if that bay already runs there (it turns straight into it), else the lane.</summary>
		private int BayFor(int i, int to, double x)
		{
			Meso.Vehicle v = M.Cars[i];
			if (v.Through)
			{
				return to;
			}
			int c = NextConn(i, to);
			if (c < 0)
			{
				return to;
			}
			int from = Lanes.Connectors[c].From;
			Lanes.Lane f = Lanes.All[from];
			return f.Kind == Lanes.Bay && f.Parent == to && Meso.StartOf(from) <= x - Lanes.Axle ? from : to;
		}

		/// <summary>Car <paramref name="i"/>'s agent goes (handed to the meso, or parked): out of lane <paramref name="queue"/>'s queue and the micro cars.</summary>
		private void Remove(int i, int queue)
		{
			M.Queues[queue].Cars.Remove(i);
			M.Cars[i].Micro = false;
			if (Agents[i] != null && Agents[i].Driven)
			{
				// Its body is the game's to let go (it sees the agent gone).
				BodiesNow--;
			}
			Agents[i] = null;
			int k = Active.BinarySearch(i);
			if (k >= 0)
			{
				Active.RemoveAt(k);
			}
		}

		private void Arrive(int i)
		{
			Agent a = Agents[i];
			Log(Kind.Arrived, i);
			Remove(i, a.Lane);
			M.Arrive(M.Cars[i]);
		}

		/// <summary>The headway between two cars over a stop line at a light, the second having stood: the saturation flow micro drives at.</summary>
		private void Crossed(int lane, Agent a, Meso.Vehicle v, int c)
		{
			Crossings[lane]++;
			Lanes.Lane l = Lanes.All[lane];
			Lost[lane] += Math.Max(0.0, T - v.Entered - Meso.LaneFree(v, lane, c));
			bool queued = a.Stood && T - lastCross[lane] < 4.0;
			platoon[lane] = queued ? platoon[lane] + 1 : a.Stood ? 1 : 0;
			if (TrafficSignals.Has(l.To) && queued)
			{
				HeadwaySum += T - lastCross[lane];
				Headways++;
				if (platoon[lane] >= 5)
				{
					SatSum += T - lastCross[lane];
					Sats++;
				}
			}
			lastCross[lane] = T;
		}

		// ---- coming in -------------------------------------------------------------------------------------------------------

		/// <summary>The meso's cars due on micro lanes: off a meso junction (placed where the meso has them) or pulling out.</summary>
		/// <summary>Cars waiting to pull out (this step's and the last's): since when. One waiting longer than <see cref="Courtesy"/> is let
		/// out (<see cref="waving"/>).</summary>
		private Dictionary<int, double> pullSince = new Dictionary<int, double>(), pullNext = new Dictionary<int, double>();

		/// <summary>
		/// Where cars have waited long to pull out (a drive at the head of a queue that hardly empties): the cars coming up behind stop
		/// short of it if they can do so comfortably, as drivers wave one out, and it goes once the car ahead has moved off.
		/// </summary>
		private readonly List<(int lane, double x)> waving = new List<(int, double)>();

		private void Spawn()
		{
			(pullSince, pullNext) = (pullNext, pullSince);
			pullNext.Clear();
			waving.Clear();
			for (int l = 0; l < LaneMicro.Length; l++)
			{
				if (!LaneMicro[l] || M.Queues[l].Cars.Count == 0)
				{
					continue;
				}
				List<int> q = M.Queues[l].Cars;
				for (int k = 0; k < q.Count; k++)
				{
					int i = q[k];
					Meso.Vehicle v = M.Cars[i];
					if (v.Micro || v.Entered > M.Now)
					{
						continue;
					}
					int cw = Meso.CarriagewayOf(l);
					int phys;
					double x, speed;
					Kind kind;
					if (v.Conn >= 0 && Meso.CarriagewayOf(Lanes.Connectors[v.Conn].To) == cw)
					{
						Lanes.Connector cn = Lanes.Connectors[v.Conn];
						x = Meso.FreeAlong(v, T, out speed);
						phys = BayFor(i, cn.To, x);
						// No faster than it can still stop at the lane's end from (the junction there may hold it).
						speed = Math.Min(speed, Math.Sqrt(2.0 * Driver.Brake * Math.Max(0.0, Meso.Reach(l) - Lanes.All[l].Hold - x)));
						kind = Kind.In;
					}
					else
					{
						phys = Meso.LanesOf(cw)[0];
						// Onto the lane proper, not into a junction's box (a house by a junction pulls out just short of it).
						double hi = Meso.Reach(phys) - 1.0;
						x = Math.Min(hi, Math.Max(v.From, Math.Min(hi, Meso.StartOf(phys) + Traffic.CarLength)));
						speed = 0.0;
						kind = Kind.PullOut;
						if (!pullSince.TryGetValue(i, out double since))
						{
							since = T;
						}
						pullNext[i] = since;
						bool waved = T - since > Courtesy;
						if (!PullGap(phys, x, i, waved))
						{
							if (waved)
							{
								waving.Add((phys, x));
							}
							continue;
						}
						if (OneTrackBusy(phys, i, out int _))
						{
							// Out along a drive with a car on it coming in: it waits by the house.
							continue;
						}
						int want = v.Ends ? phys : WantLane(i, phys);
						if (want != phys && Lanes.All[want].Kind == Lanes.Bay && Lanes.All[want].Parent == phys && x >= Meso.StartOf(want) + Lanes.Axle)
						{
							// Its turn is from the bay beside it (a house short of a junction with a left-turn bay): it pulls out across into
							// the bay, as a driver would, rather than into the lane it couldn't get out of in time.
							if (!PullGap(want, x, i, waved))
							{
								continue;
							}
							phys = want;
						}
						pullNext.Remove(i);
					}
					Lanes.Lane pl = Lanes.All[phys];
					if (pl.Kind == Lanes.Bay && x < Meso.StartOf(phys) + Lanes.Axle)
					{
						phys = pl.Parent;
					}
					// At most up to its stop line: the junction is its driver's to decide.
					x = Math.Min(x, StopLine(phys));
					Agent a = new Agent { Lane = phys, X = x, V = speed };
					if (kind == Kind.In)
					{
						// No faster than it can stop behind the car ahead (the meso let it go with room; this is in case it closed up since).
						Index();
						Agents[i] = a;
						if (onLane[phys].Count == 0)
						{
							laneTouched.Add(phys);
						}
						onLane[phys].Add(i);
						onLane[phys].Sort(ByX);
						bool alongside = false;
						double behind = a.X;
						foreach (int j in onLane[phys])
						{
							if (j != i && Agents[j] != null && Math.Abs(Agents[j].X - a.X) < Traffic.CarLength + Driver.Gap0)
							{
								alongside = true;
								behind = Math.Min(behind, Agents[j].X - Meso.Jam);
							}
						}
						if (alongside)
						{
							// A car is where it would be (one came on beside it from the next lane and moved over in front of it): a standing gap
							// behind it if the lane has room there (the two can go on side by side the length of the road, so later may never
							// come), else it comes in a little later.
							bool room = behind >= Meso.StartOf(phys);
							foreach (int j in onLane[phys])
							{
								room &= j == i || Agents[j] == null || Math.Abs(Agents[j].X - behind) >= Traffic.CarLength + Driver.Gap0;
							}
							if (!room)
							{
								Agents[i] = null;
								onLane[phys].Remove(i);
								continue;
							}
							a.X = behind;
							onLane[phys].Sort(ByX);
						}
						if (Leader(i, phys, out double gap, out double vl, out int _))
						{
							if (gap < Driver.Gap0)
							{
								// A car has come in front of where it would be (changed lanes into it since the meso let it go): back to a
								// standing gap if the lane has room, else it comes in a little later (still with the meso, out of sight).
								double back = a.X - (Driver.Gap0 - gap);
								if (back < Meso.StartOf(phys))
								{
									Agents[i] = null;
									onLane[phys].Remove(i);
									continue;
								}
								a.X = back;
								gap = Driver.Gap0;
							}
							a.V = Math.Min(a.V, Math.Sqrt(vl * vl + 2.0 * Driver.Brake * Math.Max(0.0, gap - Driver.Gap0)));
						}
					}
					Agents[i] = a;
					v.Micro = true;
					if (phys != l)
					{
						q.RemoveAt(k);
						k--;
						M.Queues[phys].Cars.Add(i);
						v.Lane = phys;
					}
					Insert(i);
					Spawned++;
					Log(kind, i);
				}
			}
		}

		/// <summary>Where a car's front stands waiting at lane <paramref name="lane"/>'s end.</summary>
		private static double StopLine(int lane)
		{
			return Math.Max(Meso.StartOf(lane), Meso.Reach(lane) - Lanes.All[lane].Hold - 0.01);
		}

		private void Insert(int i)
		{
			int k = Active.BinarySearch(i);
			if (k < 0)
			{
				Active.Insert(~k, i);
			}
		}

		private void Log(Kind kind, int i)
		{
			if (starting)
			{
				// The race starting: the cars round the players are where the meso has them from the first frame.
				return;
			}
			Pose(i, out double x, out double z, out double _, out double _);
			Events.Add(new Event { Kind = kind, Car = i, T = T, X = x, Z = z });
		}

		// ---- the areas -------------------------------------------------------------------------------------------------------

		private bool[] nearLane, keepLane;
		private readonly List<(double x, double z)> seen = new List<(double, double)>();
		/// <summary>The wrap's jumps: where a lane goes into one tunnel and where it comes out of the other.</summary>
		private static List<(double ax, double az, double bx, double bz)> portals;
		private readonly List<int> marked = new List<int>();

		/// <summary>Works out which junctions are micro from the players' positions, and moves the cars of those that change.</summary>
		private void Areas()
		{
			if (nearLane == null)
			{
				nearLane = new bool[Lanes.All.Count];
				keepLane = new bool[Lanes.All.Count];
			}
			foreach (int l in marked)
			{
				nearLane[l] = false;
				keepLane[l] = false;
			}
			marked.Clear();
			bool all = NearR >= 1e8;
			// A player near one of the wrap's tunnels is about to be at the other: the junctions there go micro ahead, as its ground loads ahead.
			seen.Clear();
			for (int p = 0; p < PX.Count; p++)
			{
				seen.Add((PX[p], PZ[p]));
				foreach ((double ax, double az, double bx, double bz) in portals)
				{
					double dx = PX[p] - ax, dz = PZ[p] - az;
					if (dx * dx + dz * dz < FarR * FarR)
					{
						seen.Add((PX[p] + bx - ax, PZ[p] + bz - az));
					}
				}
			}
			for (int p = 0; p < seen.Count && !all; p++)
			{
				double px = seen[p].x, pz = seen[p].z;
				int r = (int)Math.Ceiling(FarR / Cell);
				long cx = (long)Math.Floor(px / Cell), cz = (long)Math.Floor(pz / Cell);
				for (long dx = -r; dx <= r; dx++)
				{
					for (long dz = -r; dz <= r; dz++)
					{
						if (!samples.TryGetValue(((cx + dx) << 32) ^ ((cz + dz) & 0xffffffffL), out List<(int lane, double x, double z)> list))
						{
							continue;
						}
						foreach ((int lane, double x, double z) s in list)
						{
							double ex = s.x - px, ez = s.z - pz, d2 = ex * ex + ez * ez;
							if (d2 > FarR * FarR)
							{
								continue;
							}
							if (!keepLane[s.lane])
							{
								keepLane[s.lane] = true;
								marked.Add(s.lane);
							}
							if (d2 <= NearR * NearR)
							{
								nearLane[s.lane] = true;
							}
						}
					}
				}
			}
			for (int g = 0; g < groupNodes.Length; g++)
			{
				bool want = all, keep = all;
				if (!all)
				{
					foreach (int l in groupLanes[g])
					{
						want |= nearLane[l];
						keep |= keepLane[l];
					}
				}
				bool micro = want || (groupMicro[g] && keep);
				if (micro == groupMicro[g])
				{
					deferred.Remove(g);
					continue;
				}
				if (micro && !starting && Passing(g))
				{
					// The meso has cars going through its boxes (it spaces them in time, not by their bodies): it turns micro once they are
					// through, or after a few seconds.
					if (!deferred.TryGetValue(g, out double since))
					{
						deferred[g] = T;
						continue;
					}
					if (T - since < 3.0)
					{
						continue;
					}
				}
				deferred.Remove(g);
				Flips++;
				if (micro)
				{
					FlipIn(g);
				}
				else
				{
					FlipOut(g);
				}
			}
		}

		private readonly List<(int car, double x, double v, int lane)> converting = new List<(int, double, double, int)>();
		private readonly Dictionary<int, double> deferred = new Dictionary<int, double>();
		private readonly List<int> inBoxes = new List<int>();

		/// <summary>Whether a meso car is going through one of group <paramref name="g"/>'s junctions now.</summary>
		private bool Passing(int g)
		{
			foreach (Meso.Vehicle v in M.Cars)
			{
				if (v.Mode == Meso.Mode.Driving && !v.Micro && v.Conn >= 0 && M.Now < v.Entered && groupOf[Lanes.Connectors[v.Conn].Node] == g)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Group <paramref name="g"/>'s junctions turn micro: the meso's cars on their lanes in, and those going through them, placed where the meso has them.</summary>
		private void FlipIn(int g)
		{
			groupMicro[g] = true;
			foreach (int n in groupNodes[g])
			{
				foreach (int l in into[n])
				{
					LaneMicro[l] = true;
				}
			}
			converting.Clear();
			inBoxes.Clear();
			for (int i = 0; i < M.Cars.Length; i++)
			{
				Meso.Vehicle v = M.Cars[i];
				if (v.Mode != Meso.Mode.Driving || v.Micro)
				{
					continue;
				}
				if (v.Conn >= 0 && M.Now < v.Entered)
				{
					Lanes.Connector cn = Lanes.Connectors[v.Conn];
					if (groupOf[cn.Node] != g)
					{
						continue;
					}
					// Going through one of its junctions: on the connector where the meso has it.
					M.Where(i, out int _, out int _, out double d);
					Driver.RunAt(cn.Length, v.VConn, cn.Limit, Lanes.All[cn.To].Limit, M.Now - v.ConnAt, out double vNow);
					M.Queues[v.Lane].Cars.Remove(i);
					M.Queues[cn.To].Cars.Add(i);
					v.Lane = cn.To;
					Agents[i] = new Agent { Conn = v.Conn, X = d, V = vNow };
					v.Micro = true;
					Insert(i);
					Log(Kind.FlipIn, i);
					inBoxes.Add(i);
					continue;
				}
				if (!LaneMicro[v.Lane] || groupOf[Lanes.All[v.Lane].To] != g)
				{
					continue;
				}
				M.Where(i, out int _, out int _, out double along);
				double free = Meso.FreeAlong(v, M.Now, out double fs);
				int phys = v.Lane;
				Lanes.Lane pl = Lanes.All[phys];
				if (pl.Kind == Lanes.Bay && along < Meso.StartOf(phys) + Lanes.Axle)
				{
					phys = pl.Parent;
				}
				converting.Add((i, along, along < free - 0.01 ? 0.0 : fs, phys));
			}
			// Front first, each kept a standing gap behind the one before in its lane.
			converting.Sort((p, q) => p.lane != q.lane ? p.lane.CompareTo(q.lane) : q.x != p.x ? q.x.CompareTo(p.x) : p.car.CompareTo(q.car));
			int lastLane = -1;
			double lastX = 0.0, lastV = 0.0;
			foreach ((int car, double x0, double sp0, int phys) in converting)
			{
				double x = Math.Min(x0, StopLine(phys)), sp = sp0;
				if (phys == lastLane)
				{
					// A standing gap behind the car ahead and half a time gap at its speed: the meso spaces cars in time, not by their
					// bodies, so two cars come onto one lane from two (out of the wrap's tunnels, a merge) can be side by side in its queue.
					x = Math.Min(x, lastX - Meso.Jam - Math.Min(sp, lastV + 10.0) * Driver.TimeGap * 0.5);
					if (x < Meso.StartOf(phys))
					{
						x = Math.Min(Meso.StartOf(phys), lastX - Meso.Jam);
					}
					// No faster than it can stop behind the car ahead (the meso has cars moving at the lane's speed up to its queue).
					double room = Math.Max(0.0, lastX - Traffic.CarLength - x - Driver.Gap0);
					sp = Math.Min(sp, Math.Sqrt(lastV * lastV + 2.0 * Driver.Brake * room));
				}
				int next = NextConn(car, phys);
				if (next >= 0 && !M.Cars[car].Ends && Lanes.Connectors[next].Turn != Lanes.Jump)
				{
					// Able to stop at its line: whether it may go is its driver's to work out from here (a light, a car to give way to).
					sp = Math.Min(sp, Math.Sqrt(2.0 * Driver.Brake * Math.Max(0.0, Meso.Reach(phys) - Lanes.All[phys].Hold - x)));
				}
				lastLane = phys;
				lastX = x;
				lastV = sp;
				Meso.Vehicle v = M.Cars[car];
				M.Queues[v.Lane].Cars.Remove(car);
				M.Queues[phys].Cars.Add(car);
				v.Lane = phys;
				Agents[car] = new Agent { Lane = phys, X = x, V = sp };
				v.Micro = true;
				Insert(car);
				Log(Kind.FlipIn, car);
			}
			// No faster than each can stop behind whatever is ahead of it now, past the junction too (twice, the cars ahead settled first).
			Index();
			for (int pass = 0; pass < 2; pass++)
			{
				foreach (int car in inBoxes)
				{
					Agent a = Agents[car];
					if (a != null && a.Conn >= 0 && Leader(car, -1, out double gap, out double vl, out int _))
					{
						if (gap < Driver.Gap0)
						{
							// Two cars where the meso has them on one connector closer than a car (the race's first picture of the roads
							// had two side by side): the one behind back to a standing gap.
							a.X = Math.Max(0.0, a.X - (Driver.Gap0 - gap));
							gap = Driver.Gap0;
						}
						a.V = Math.Min(a.V, Math.Sqrt(vl * vl + 2.0 * Driver.Brake * Math.Max(0.0, gap - Driver.Gap0)));
					}
				}
				int prevLane = -1;
				double prevX = 0.0, prevV = 0.0;
				foreach ((int car, double _, double _, int phys) in converting)
				{
					Agent a = Agents[car];
					if (a != null && a.Conn < 0 && Leader(car, a.Lane, out double gap, out double vl, out int _))
					{
						if (gap < Driver.Gap0)
						{
							// The meso's picture had it closer than a car (just behind one gone through the junction): back to a standing gap.
							a.X = Math.Max(Meso.StartOf(a.Lane), a.X - (Driver.Gap0 - gap));
							gap = Driver.Gap0;
						}
						a.V = Math.Min(a.V, Math.Sqrt(vl * vl + 2.0 * Driver.Brake * Math.Max(0.0, gap - Driver.Gap0)));
					}
					if (a != null && a.Conn < 0 && a.Lane == prevLane && a.X > prevX - Meso.Jam)
					{
						// Still behind the one before it in the queue (pulled back behind a car ahead of both, it would be measured against
						// that car too, the lanes' order not yet sorted again, and land on it).
						a.X = Math.Max(Meso.StartOf(a.Lane), prevX - Meso.Jam);
						a.V = Math.Min(a.V, prevV);
					}
					if (a != null && a.Conn < 0)
					{
						prevLane = a.Lane;
						prevX = a.X;
						prevV = a.V;
					}
				}
				Index();
			}
		}

		/// <summary>Group <paramref name="g"/>'s junctions go back to the meso: the micro cars on their lanes in and in them into the queues, in order, where they are.</summary>
		private void FlipOut(int g)
		{
			converting.Clear();
			List<int> onConnectors = new List<int>();
			foreach (int i in Active)
			{
				Agent a = Agents[i];
				if (a.Conn >= 0)
				{
					if (groupOf[Lanes.Connectors[a.Conn].Node] == g)
					{
						onConnectors.Add(i);
					}
				}
				else if (groupOf[Lanes.All[a.Lane].To] == g)
				{
					converting.Add((i, a.X, a.V, a.Lane));
				}
			}
			converting.Sort((p, q) => q.x != p.x ? q.x.CompareTo(p.x) : p.car.CompareTo(q.car));
			foreach ((int car, double x, double sp, int lane) in converting)
			{
				Meso.Vehicle v = M.Cars[car];
				Log(Kind.FlipOut, car);
				Remove(car, lane);
				int cw = Meso.CarriagewayOf(lane);
				int to = v.Through ? lane : M.ExitLane(v, cw);
				M.Enter(v, to, v.Conn, x, M.Now, false, sp);
			}
			foreach (int car in onConnectors)
			{
				Agent a = Agents[car];
				Meso.Vehicle v = M.Cars[car];
				int c = a.Conn;
				Lanes.Connector cn = Lanes.Connectors[c];
				double d = Math.Min(a.X, cn.Length);
				Log(Kind.FlipOut, car);
				Remove(car, cn.To);
				int target = M.Target(v, c, v.Next);
				double rest = Driver.Run(cn.Length - d, a.V, cn.Limit, Lanes.All[cn.To].Limit, out double vOff);
				M.Enter(v, target, c, Meso.JoinFrom(c), M.Now + rest, false, vOff);
				v.VConn = a.V;
				v.ConnAt = M.Now - Meso.PassAt(cn, a.V, d);
				M.Passed(c, v.ConnAt, car);
			}
			groupMicro[g] = false;
			foreach (int n in groupNodes[g])
			{
				foreach (int l in into[n])
				{
					LaneMicro[l] = false;
				}
			}
		}

		// ---- reading -----------------------------------------------------------------------------------------------------------

		/// <summary>Micro car <paramref name="i"/>'s middle on the ground and the way it faces (unit): on its lane (between two while changing) or along its connector.</summary>
		internal void Pose(int i, out double x, out double z, out double hx, out double hz)
		{
			Agent a = Agents[i];
			Line(a, out x, out z, out hx, out hz);
			if (a.OffLeft > 0.0)
			{
				double f = a.OffLeft / Settle;
				x += a.OffX * f;
				z += a.OffZ * f;
			}
		}

		/// <summary>The middle of a micro car on its line (lane or connector) and the way it faces.</summary>
		private static void Line(Agent a, out double x, out double z, out double hx, out double hz)
		{
			if (a.Conn >= 0)
			{
				Lanes.Connector cn = Lanes.Connectors[a.Conn];
				OnConnector(cn, a.X, out x, out z, out hx, out hz);
				return;
			}
			int cw = Meso.CarriagewayOf(a.Lane);
			double s = Meso.RoadS(cw, a.X - Lanes.Axle);
			Lanes.Point(Lanes.All[a.Lane], s, out x, out z, out double _, out hx, out hz);
			if (a.ChangeFrom >= 0 && a.ChangeLeft > 0.0)
			{
				Lanes.Point(Lanes.All[a.ChangeFrom], s, out double ox, out double oz, out double _, out double _, out double _);
				double f = Math.Min(1.0, a.ChangeLeft / a.ChangeSpan);
				f = f * f * (3.0 - 2.0 * f);
				x += (ox - x) * f;
				z += (oz - z) * f;
			}
		}

		/// <summary>The point <paramref name="d"/> along connector <paramref name="cn"/>'s line and its way there.</summary>
		internal static void OnConnector(Lanes.Connector cn, double d, out double x, out double z, out double hx, out double hz)
		{
			int n = cn.D.Length;
			if (n < 2 || cn.Length <= 0.0)
			{
				x = cn.X[n - 1];
				z = cn.Z[n - 1];
				hx = 1.0;
				hz = 0.0;
				if (n >= 2)
				{
					double ex = cn.X[n - 1] - cn.X[0], ez = cn.Z[n - 1] - cn.Z[0], el = Math.Sqrt(ex * ex + ez * ez);
					if (el > 1e-9)
					{
						hx = ex / el;
						hz = ez / el;
					}
				}
				return;
			}
			d = Math.Max(0.0, Math.Min(cn.Length, d));
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (cn.D[mid] <= d)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			double span = cn.D[hi] - cn.D[lo], f = span > 1e-12 ? (d - cn.D[lo]) / span : 0.0;
			x = cn.X[lo] + (cn.X[hi] - cn.X[lo]) * f;
			z = cn.Z[lo] + (cn.Z[hi] - cn.Z[lo]) * f;
			double dx = cn.X[hi] - cn.X[lo], dz = cn.Z[hi] - cn.Z[lo], len = Math.Sqrt(dx * dx + dz * dz);
			hx = len > 1e-9 ? dx / len : 1.0;
			hz = len > 1e-9 ? dz / len : 0.0;
		}

		/// <summary>
		/// The connector car <paramref name="i"/> is in or coming to and how far its middle is along it (negative before the stop line), for
		/// trafficsim's check of the conflict areas: its own connector, or within <see cref="Conflicts.Approach"/> of its lane's end the one it
		/// will take. False if neither.
		/// </summary>
		internal bool AtJunction(int i, out int c, out double d)
		{
			Agent a = Agents[i];
			if (a.Conn >= 0)
			{
				c = a.Conn;
				d = a.X;
				return true;
			}
			d = a.X - Meso.Reach(a.Lane);
			c = M.Cars[i].Ends ? -1 : NextConn(i, a.Lane);
			return c >= 0 && Lanes.Connectors[c].From == a.Lane && d >= -Conflicts.Approach;
		}

		/// <summary>Whether junction <paramref name="node"/> is micro.</summary>
		internal bool NodeMicro(int node)
		{
			return groupMicro[groupOf[node]];
		}

		/// <summary>Whether every micro car is in its lane's queue once and marked micro, and every car marked micro has an agent (trafficsim's conservation, with <see cref="Meso.Conserved"/>).</summary>
		internal bool Consistent(out string why)
		{
			why = "";
			for (int i = 0; i < M.Cars.Length; i++)
			{
				Meso.Vehicle v = M.Cars[i];
				Agent a = Agents[i];
				if (v.Micro != (a != null))
				{
					why = "car " + i + " micro " + v.Micro + " but agent " + (a != null);
					return false;
				}
				if (a == null)
				{
					continue;
				}
				int lane = a.Conn >= 0 ? Lanes.Connectors[a.Conn].To : a.Lane;
				if (v.Lane != lane || v.Mode != Meso.Mode.Driving)
				{
					why = "car " + i + " micro in lane " + lane + " but the meso has it " + v.Mode + " in lane " + v.Lane;
					return false;
				}
				if (Active.BinarySearch(i) < 0)
				{
					why = "car " + i + " has an agent but isn't listed";
					return false;
				}
			}
			return true;
		}
	}
}
