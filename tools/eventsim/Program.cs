using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;
using static HalfGripOpenWorld.EventRoom;

// Drives the events in a room (EventRoom.cs) without the game: several pretend machines, each with its own EventRoom, over a pretend
// network that delays every message (50 to 250 ms, in order from each sender to each receiver, as Steam's reliable messages are).
// Every machine's own clock is off from the others' by up to 30 s either way (each game started at its own time).
//   dotnet run -c Release --project tools/eventsim -- [seed]
// Runs joins, leaves, the Host leaving, two creates at once, two Starts at once on one course, races to the finish, the cut-off after
// the first finisher, the time limit, a player leaving the room mid-race, a late joiner's snapshot, a full lobby, joining a race that
// is on, a lobby of one outside a room, the weapon rules, stale and bad messages, machines that stall (long frames) through a race,
// and players whose world is built up to 20 s after they join the room. After every step it checks that every machine's lobbies are
// the arbiter's, byte for byte, and that its clock is the arbiter's to within a message's delay. Exits 1 if a check fails.
internal static class Program
{
	private static int failures;
	private static int checks;

	private static int Main(string[] args)
	{
		int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
		Scenario("joins and leaves", seed, JoinsAndLeaves);
		Scenario("the Host leaves", seed, HostLeaves);
		Scenario("two creates at once", seed, TwoCreates);
		Scenario("two Starts at once", seed, TwoStarts);
		Scenario("a race to the finish", seed, RaceToFinish);
		Scenario("the cut-off", seed, CutoffEnds);
		Scenario("the time limit", seed, TimeLimitEnds);
		Scenario("leaving the room mid-race", seed, LeavesRoom);
		Scenario("leaving a race early", seed, LeavesRace);
		Scenario("a late joiner's snapshot", seed, LateJoiner);
		Scenario("full, and a race that is on", seed, FullAndRaceOn);
		Scenario("a lobby of one outside a room", seed, Solo);
		Scenario("the weapon rules", seed, Rules);
		Scenario("stale and bad messages", seed, Stale);
		Scenario("many players at random", seed, Random);
		Scenario("stalls and clocks off", seed, Stalls);
		Scenario("worlds built late", seed, LateWorlds);
		Console.WriteLine(checks + " checks, " + failures + " failed: " + (failures == 0 ? "ok" : "FAIL"));
		return failures == 0 ? 0 : 1;
	}

	// ---- the courses -------------------------------------------------------------------------------------------------

	/// <summary>Like the world's: the circuit, the rally stage, the hill climb and the highway, with their gates, grids and time limits.</summary>
	private static readonly CourseInfo[] Courses =
	{
		new CourseInfo("Circuit", 9, 12, 300),
		new CourseInfo("Rally", 12, 8, 420),
		new CourseInfo("Hill climb", 7, 8, 240),
		new CourseInfo("Highway", 33, 12, 930),
	};

	// ---- the pretend room --------------------------------------------------------------------------------------------

	private sealed class Packet
	{
		public double At;
		public long Order;
		public ulong From;
		public ulong To;
		public byte[] Data;
	}

	/// <summary>The most a message is delayed (in order, a later one never overtakes an earlier one, so this holds).</summary>
	private const double MaxDelay = 0.25;

	/// <summary>What a clock may be off besides a message's delay: an older message's offset marked down for its age, and a step of the sim.</summary>
	private const double Slack = EventRoom.Drift * EventRoom.SyncWindow + 0.03;

	/// <summary>The most messages a machine holds before its world is built (as <c>EventLink</c>); older ones are lost.</summary>
	private const int HoldMost = 64;

	private sealed class Room
	{
		/// <summary>True time (no machine reads it: each has its own clock).</summary>
		public double Now;
		public ulong Host;
		public readonly Dictionary<ulong, Machine> Machines = new Dictionary<ulong, Machine>();
		public readonly List<Packet> Queue = new List<Packet>();
		public readonly Dictionary<(ulong, ulong), double> Last = new Dictionary<(ulong, ulong), double>();
		public readonly System.Random Rng;
		/// <summary>Machines stall in this scenario: the clocks are checked against the stalls' own rule, not every step's.</summary>
		public bool Stalls;
		private long order;
		public int Sent;

		public Room(int seed)
		{
			Rng = new System.Random(seed);
		}

		public void Post(ulong from, ulong to, byte[] data)
		{
			if (!Machines.TryGetValue(to, out Machine m) || !m.Present)
			{
				return;
			}
			// In order from each sender to each receiver: never before the last one sent that way.
			double at = Now + 0.05 + Rng.NextDouble() * (MaxDelay - 0.05);
			if (Last.TryGetValue((from, to), out double last))
			{
				at = Math.Max(at, last);
			}
			Last[(from, to)] = at;
			Queue.Add(new Packet { At = at, Order = order++, From = from, To = to, Data = data });
			Sent++;
		}

		public Machine Arbiter => Machines.TryGetValue(Host, out Machine m) && m.Present ? m : null;

		/// <summary>Called each step after the machines have ticked (a scenario watching for greens).</summary>
		public Action Watch;

		/// <summary>Runs the clock on, in steps of 20 ms: worlds due are built, messages due are delivered, every machine ticks.</summary>
		public void Run(double seconds)
		{
			double until = Now + seconds;
			while (Now < until - 1e-9)
			{
				Now = Math.Min(until, Now + 0.02);
				foreach (Machine m in Machines.Values.ToList())
				{
					if (m.Present && !m.Built && Now >= m.BuildAt && !m.Stalled)
					{
						m.Build();
					}
				}
				Deliver();
				foreach (Machine m in Machines.Values.ToList())
				{
					if (m.Present && m.Built && !m.Stalled)
					{
						m.Events.Tick(m.Clock);
					}
				}
				Watch?.Invoke();
			}
		}

		/// <summary>Until nothing is on its way.</summary>
		public void Settle()
		{
			int guard = 0;
			while (Queue.Count > 0 && guard++ < 10000)
			{
				Run(0.02);
			}
		}

		/// <summary>
		/// Messages due go to their machines: a stalled machine takes nothing until its long frame is over (then all that came, read at
		/// its clock then); one whose world is not built yet holds them (as <c>EventLink</c> does) and reads them when it is.
		/// </summary>
		private void Deliver()
		{
			List<Packet> due = Queue.Where(p => p.At <= Now).OrderBy(p => p.At).ThenBy(p => p.Order).ToList();
			foreach (Packet p in due)
			{
				if (Machines.TryGetValue(p.To, out Machine m) && m.Present && m.Stalled)
				{
					continue;
				}
				Queue.Remove(p);
				if (m != null && m.Present && Machines.TryGetValue(p.From, out Machine f) && f.Present)
				{
					m.Take(p.From, p.Data);
				}
			}
		}

		/// <summary>A player joins the room; their world is built <paramref name="late"/> seconds on (at once by default).</summary>
		public Machine Add(ulong id, double late = 0.0)
		{
			Machine m = new Machine(this, id, (Rng.NextDouble() * 2.0 - 1.0) * 30.0);
			m.BuildAt = Now + late;
			Machines[id] = m;
			if (Host == 0UL)
			{
				Host = id;
			}
			if (late <= 0.0)
			{
				m.Build();
			}
			// The arbiter hears of the join only if its own world is built (its EventLink has no room to tell before that).
			Machine arbiter = Arbiter;
			if (arbiter != null && arbiter.Built)
			{
				arbiter.Events.PlayerJoined(id, arbiter.Clock);
			}
			// Settled, so a scenario's player has heard the arbiter (in the game a player is in the room a while before driving to a box).
			if (late <= 0.0)
			{
				Settle();
			}
			return m;
		}

		/// <summary>The player leaves the room: their copy forgets every lobby, what is on its way to or from them is lost, the arbiter takes them out.</summary>
		public void Remove(ulong id)
		{
			Machine m = Machines[id];
			m.Present = false;
			m.Events.Reset();
			Queue.RemoveAll(p => p.To == id || p.From == id);
			Machine arbiter = Arbiter;
			arbiter?.Events.PlayerLeft(id, arbiter.Clock);
		}
	}

	private sealed class Machine : ILink
	{
		private readonly Room room;
		public readonly EventRoom Events;
		public bool Present = true;
		public readonly List<Refusal> Refusals = new List<Refusal>();
		public readonly List<(Why why, ulong who)> Heard = new List<(Why, ulong)>();
		/// <summary>This machine's clock minus true time.</summary>
		public readonly double Skew;
		/// <summary>True time its long frame ends (it takes nothing and does nothing till then).</summary>
		public double StalledUntil = -1.0;
		public bool Built;
		public double BuildAt;
		public int Lost;
		private readonly List<(ulong from, byte[] data)> held = new List<(ulong, byte[])>();

		public Machine(Room room, ulong id, double skew)
		{
			this.room = room;
			Local = id;
			Skew = skew;
			Events = new EventRoom(this, Courses);
			Events.Refused += r => Refusals.Add(r);
			Events.Changed += (l, why, who) => Heard.Add((why, who));
		}

		public ulong Local { get; }
		public bool Arbiter => room.Host == Local;

		public void ToArbiter(byte[] data)
		{
			room.Post(Local, room.Host, data);
		}

		public void ToAll(byte[] data)
		{
			foreach (ulong id in room.Machines.Keys.ToList())
			{
				if (id != Local)
				{
					room.Post(Local, id, data);
				}
			}
		}

		public void ToOne(ulong to, byte[] data)
		{
			room.Post(Local, to, data);
		}

		/// <summary>This machine's own clock (real time since its game started).</summary>
		public double Clock => room.Now + Skew;
		/// <summary>The arbiter's clock as this machine has it: what the lobbies' times are on.</summary>
		public double Now => Events.Now(Clock);
		public bool Stalled => room.Now < StalledUntil;
		public Lobby Mine => Events.Mine;
		public void Create(int course, bool guns = true, bool melee = true) => Events.Create(course, guns, melee, Clock);
		public void Join(int lobby) => Events.Join(lobby, Clock);
		public void Leave() => Events.Leave(Clock);
		public void Start() => Events.Start(Clock);
		public void Gate(int gate, float time, int missed = 0) => Events.Passed(Mine.Race, gate, time, missed, Clock);
		public void Retire() => Events.Retire(Mine.Race, Clock);

		/// <summary>A message for this machine: read now, or held until its world is built.</summary>
		public void Take(ulong from, byte[] data)
		{
			if (Built)
			{
				Events.Receive(from, data, Clock);
			}
			else
			{
				// The newest kept, as EventLink does.
				if (held.Count >= HoldMost)
				{
					held.RemoveAt(0);
					Lost++;
				}
				held.Add((from, data));
			}
		}

		/// <summary>The world is built: what was held is read in order, then the arbiter is asked for every lobby (as EventRace.Setup).</summary>
		public void Build()
		{
			Built = true;
			foreach ((ulong from, byte[] data) in held)
			{
				Events.Receive(from, data, Clock);
			}
			held.Clear();
			Events.Hello(Clock);
		}
	}

	// ---- checking ----------------------------------------------------------------------------------------------------

	private static string current;

	private static void Scenario(string name, int seed, Action<Room> body)
	{
		current = name;
		int before = failures;
		Room room = new Room(seed * 7919 + name.Length);
		try
		{
			body(room);
		}
		catch (Exception e)
		{
			Fail("threw " + e);
		}
		Console.WriteLine((failures == before ? "ok    " : "FAIL  ") + name + " (" + room.Sent + " messages)");
	}

	private static void Check(bool ok, string what)
	{
		checks++;
		if (!ok)
		{
			Fail(what);
		}
	}

	private static void Fail(string what)
	{
		failures++;
		Console.WriteLine("  " + current + ": " + what);
	}

	/// <summary>Settles the room and checks every machine still in it holds exactly the arbiter's lobbies.</summary>
	private static void Agree(Room room, string when)
	{
		room.Settle();
		Machine arbiter = room.Arbiter;
		if (arbiter == null)
		{
			return;
		}
		string Of(Machine m) => string.Join("|", m.Events.Lobbies.Select(l => l.Id + ":" + Fingerprint(l)));
		string truth = Of(arbiter);
		foreach (Machine m in room.Machines.Values.Where(x => x.Present && x != arbiter && x.Built))
		{
			Check(Of(m) == truth, "player " + m.Local + "'s lobbies differ from the arbiter's " + when);
			Check(m.Events.Linked && m.Events.Synced, "player " + m.Local + " has not heard the arbiter " + when);
			if (!room.Stalls)
			{
				ClockCheck(room, m, when);
			}
		}
	}

	/// <summary>
	/// The machine's clock is the arbiter's to within a message's delay: the offset it took is the true one less the delay of the least
	/// delayed message it read, never more.
	/// </summary>
	private static void ClockCheck(Room room, Machine m, string when)
	{
		Machine arbiter = room.Arbiter;
		double error = m.Events.Offset - (arbiter.Skew - m.Skew);
		Check(error <= 1e-6 && error >= -MaxDelay - Slack, "player " + m.Local + "'s clock is " + error.ToString("0.000") + " s off the arbiter's " + when);
	}

	private static Lobby Only(Room room)
	{
		return room.Arbiter.Events.Lobbies.Single();
	}

	private static string Ids(Lobby l) => string.Join(",", l.Entrants.Where(e => !e.Out).Select(e => e.Id));

	// ---- the scenarios -----------------------------------------------------------------------------------------------

	private static void JoinsAndLeaves(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3);
		a.Create(0, true, false);
		Agree(room, "after a create");
		Lobby l = Only(room);
		Check(l.Host == 1 && Ids(l) == "1" && l.Guns && !l.Melee && l.Course == 0, "the new lobby is A's with A in it and A's rules");
		Check(b.Heard.Any(h => h.why == Why.Created && h.who == 1), "B heard the lobby open");
		b.Join(l.Id);
		room.Run(0.3);
		c.Join(l.Id);
		Agree(room, "after two joins");
		l = Only(room);
		Check(Ids(l) == "1,2,3", "the entrants in join order: " + Ids(l));
		Check(c.Mine != null && c.Mine.Id == l.Id, "C's own copy has C in the lobby");
		c.Leave();
		Agree(room, "after a leave");
		Check(Ids(Only(room)) == "1,2" && c.Mine == null, "C is out of the waiting lobby at once");
		b.Leave();
		a.Leave();
		Agree(room, "after the last leave");
		Check(!a.Events.Lobbies.Any() && !b.Events.Lobbies.Any() && !c.Events.Lobbies.Any(), "an empty lobby is gone on every machine");
	}

	private static void HostLeaves(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3), d = room.Add(4);
		b.Create(1);
		Agree(room, "after a create");
		int id = Only(room).Id;
		c.Join(id);
		Agree(room, "C in");
		d.Join(id);
		Agree(room, "D in");
		b.Leave();
		Agree(room, "after the Host left");
		Check(Only(room).Host == 3, "the next in (C) is the Host: " + Only(room).Host);
		c.Start();
		Agree(room, "after the new Host's Start");
		Check(Only(room).State == LobbyState.Racing, "the new Host can start");
		// The Host leaving mid-race: the next still in takes over; the leaver stays in the results as a DNF.
		room.Run(EventRoom.StartLead + 1.0);
		c.Leave();
		Agree(room, "after the Host left mid-race");
		Lobby l = Only(room);
		Check(l.Host == 4 && l.Entrants.Any(e => e.Id == 3 && e.Out && e.Run == Run.Dnf), "D is the Host, C a DNF who left");
		Check(c.Events.Mine == null, "C is in no lobby");
		Check(a.Events.Mine == null, "A was never in one");
	}

	private static void TwoCreates(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3);
		a.Create(2);
		Agree(room, "A's lobby");
		int first = Only(room).Id;
		b.Join(first);
		Agree(room, "B in A's lobby");
		// B and C create in the same instant, B leaving A's lobby by it.
		b.Create(2);
		c.Create(2);
		Agree(room, "after two creates at once");
		List<Lobby> all = room.Arbiter.Events.Lobbies.ToList();
		Check(all.Count == 3 && all.Select(l => l.Id).Distinct().Count() == 3, "three lobbies with their own ids");
		Check(all.Count(l => l.Host == 2) == 1 && all.Count(l => l.Host == 3) == 1, "B and C each Host one");
		Check(Ids(room.Arbiter.Events.Find(first)) == "1", "B left A's lobby by creating");
		Check(b.Mine.Host == 2 && c.Mine.Host == 3, "each is in their own");
	}

	private static void TwoStarts(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3), d = room.Add(4);
		a.Create(0);
		c.Create(0);
		Agree(room, "two lobbies on the circuit");
		b.Join(a.Mine.Id);
		d.Join(c.Mine.Id);
		Agree(room, "two in each");
		c.Start();
		a.Start();
		Agree(room, "after two Starts at once");
		List<Lobby> all = room.Arbiter.Events.Lobbies.ToList();
		Check(all.Count(l => l.State == LobbyState.Racing) == 1, "one race on the course");
		Machine winner = a.Mine.State == LobbyState.Racing ? a : c, loser = winner == a ? c : a;
		Check(loser.Refusals.Contains(Refusal.CourseTaken), "the other Host is told the course is taken");
		Check(room.Arbiter.Events.Taking(0) == winner.Mine, "the course is the winner's");
		// A race on another course is not held up.
		Machine e = room.Add(5);
		Agree(room, "E in the room");
		e.Create(3);
		Agree(room, "E's lobby");
		e.Start();
		Agree(room, "E's start");
		Check(e.Mine.State == LobbyState.Racing, "the highway races alongside");
		// The loser can start once the course is free.
		FinishAll(room, winner.Mine);
		room.Run(EventRoom.ResultsSeconds + 0.5);
		Agree(room, "after the results");
		Check(winner.Mine.State == LobbyState.Waiting && room.Arbiter.Events.Taking(0) == null, "the course is free after the results");
		loser.Start();
		Agree(room, "after the other Start");
		Check(loser.Mine.State == LobbyState.Racing, "the other lobby starts now");
	}

	/// <summary>Every entrant still in runs every gate, a second apart, in join order.</summary>
	private static void FinishAll(Room room, Lobby lobby)
	{
		int gates = Courses[lobby.Course].Gates;
		List<Machine> runners = lobby.Entrants.Where(e => !e.Out && e.Run == Run.Running).Select(e => room.Machines[e.Id]).ToList();
		room.Run(Math.Max(0.0, lobby.StartAt - room.Arbiter.Clock) + 0.1);
		for (int g = 0; g < gates; g++)
		{
			for (int i = 0; i < runners.Count; i++)
			{
				runners[i].Gate(g, 10f * (g + 1) + i);
			}
			room.Run(0.3);
		}
		Agree(room, "after every finish");
	}

	private static void RaceToFinish(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3);
		a.Create(2);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		Agree(room, "B in");
		c.Join(a.Mine.Id);
		Agree(room, "C in");
		double asked = a.Clock;
		a.Start();
		Agree(room, "the Start");
		Lobby l = Only(room);
		Check(l.Race == 1 && l.StartAt - asked >= EventRoom.StartLead - 1e-6 && l.StartAt - asked < EventRoom.StartLead + 0.5, "green " + EventRoom.StartLead + " s after the Start reached the arbiter: " + (l.StartAt - asked).ToString("0.00"));
		Check(string.Join(",", l.Entrants.Select(e => e.Slot)) == "0,1,2", "slots dealt in join order");
		Check(b.Mine.StartAt == l.StartAt && c.Mine.EndsAt == l.StartAt + Courses[2].TimeLimit, "every machine has the start time and time limit");
		room.Run(EventRoom.StartLead);
		// C leads after two gates, then B passes; standings on every machine.
		int gates = Courses[2].Gates;
		c.Gate(0, 9f);
		a.Gate(0, 10f);
		b.Gate(0, 11f);
		c.Gate(1, 19f);
		Agree(room, "after the first gates");
		Check(Position(c.Mine, 3) == 1 && Position(a.Mine, 1) == 2 && Position(b.Mine, 2) == 3, "C leads, A second, B third");
		// A missed gate 2 (passed 3 straight away): ranked by gates passed, finishing with a miss ranks after a clean finish.
		a.Gate(3, 35f, 1);
		Agree(room, "after A skipped a gate");
		Check(Position(b.Mine, 1) == 1, "A leads on gates passed");
		for (int g = 1; g < gates; g++)
		{
			b.Gate(g, 11f + 10f * g);
		}
		for (int g = 2; g < gates; g++)
		{
			c.Gate(g, 9f + 10f * g);
		}
		for (int g = 4; g < gates; g++)
		{
			a.Gate(g, 5f + 10f * g, 1);
		}
		Agree(room, "after every finish");
		l = Only(room);
		Check(l.State == LobbyState.Results, "the race ends when everybody has finished");
		Check(string.Join(",", Standings(l).Select(e => e.Id)) == "3,2,1", "clean finishers by time, then A who missed one: " + string.Join(",", Standings(l).Select(e => e.Id)));
		Check(room.Arbiter.Events.Taking(2) == l, "the results still hold the course");
		room.Run(EventRoom.ResultsSeconds + 0.5);
		Agree(room, "after the results");
		l = Only(room);
		Check(l.State == LobbyState.Waiting && Ids(l) == "1,2,3" && l.Entrants.All(e => e.Run == Run.Waiting), "the lobby waits again with everybody in it");
		a.Start();
		Agree(room, "the second race");
		Check(Only(room).Race == 2 && Only(room).Entrants.All(e => e.Passed == 0 && e.Run == Run.Running), "the second race starts afresh");
	}

	private static void CutoffEnds(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3);
		a.Create(0);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		c.Join(a.Mine.Id);
		Agree(room, "all in");
		a.Start();
		Agree(room, "the Start");
		room.Run(EventRoom.StartLead);
		for (int g = 0; g < Courses[0].Gates; g++)
		{
			a.Gate(g, 20f * (g + 1));
		}
		b.Gate(0, 25f);
		c.Retire();
		Agree(room, "A finished, B running, C retired");
		Lobby l = Only(room);
		Check(l.State == LobbyState.Racing && l.CutoffAt > 0 && Math.Abs(l.CutoffAt - a.Clock - EventRoom.Cutoff) < 1.0, "the cut-off is set " + EventRoom.Cutoff + " s after the first finish");
		Check(l.Entrants.First(e => e.Id == 3).Run == Run.Dnf && c.Mine != null, "C retired is a DNF and still in the lobby");
		room.Run(EventRoom.Cutoff - 2.0);
		Agree(room, "just before the cut-off");
		Check(Only(room).State == LobbyState.Racing, "still racing before the cut-off");
		room.Run(3.0);
		Agree(room, "after the cut-off");
		l = Only(room);
		Check(l.State == LobbyState.Results && l.Entrants.First(e => e.Id == 2).Run == Run.Dnf, "B still running at the cut-off is a DNF");
		Check(string.Join(",", Standings(l).Select(e => e.Id)) == "1,2,3", "A, then B (one gate), then C");
	}

	private static void TimeLimitEnds(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2);
		a.Create(2);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		Agree(room, "B in");
		a.Start();
		Agree(room, "the Start");
		double limit = Only(room).EndsAt;
		room.Run(limit - a.Clock - 1.0);
		a.Gate(0, 100f);
		Agree(room, "a second before the limit");
		Check(Only(room).State == LobbyState.Racing, "racing until the limit");
		room.Run(2.0);
		Agree(room, "after the limit");
		Lobby l = Only(room);
		Check(l.State == LobbyState.Results && l.Entrants.All(e => e.Run == Run.Dnf), "everybody still running at the limit is a DNF");
		Check(Position(l, 1) == 1, "A, who got further, is first of the DNFs");
	}

	private static void LeavesRoom(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3);
		a.Create(1);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		c.Join(a.Mine.Id);
		Agree(room, "all in");
		a.Start();
		Agree(room, "the Start");
		room.Run(EventRoom.StartLead + 2.0);
		b.Gate(0, 12f);
		room.Run(0.1);
		// C drops out of the room with a gate on its way; B's gate is still counted.
		c.Gate(0, 13f);
		room.Remove(3);
		Agree(room, "after C left the room");
		Lobby l = Only(room);
		Entrant ce = l.Entrants.FirstOrDefault(e => e.Id == 3);
		Check(ce != null && ce.Out && ce.Run == Run.Dnf, "C is a DNF in the results");
		Check(l.Entrants.First(e => e.Id == 2).Passed == 1, "B's gate counted");
		Check(!c.Events.Lobbies.Any(), "C's own copy is empty");
		for (int g = 0; g < Courses[1].Gates; g++)
		{
			a.Gate(g, 10f * (g + 1));
			b.Gate(g, 10f * (g + 1) + 1f);
		}
		Agree(room, "after A and B finished");
		Check(Only(room).State == LobbyState.Results, "the race ends: C out, A and B in");
		room.Run(EventRoom.ResultsSeconds + 0.5);
		Agree(room, "after the results");
		Check(Ids(Only(room)) == "1,2" && Only(room).Entrants.Count == 2, "C is gone after the results");
		// Everybody leaves the room: the arbiter's lobby goes when its last player leaves.
		room.Remove(2);
		a.Leave();
		Agree(room, "the last left");
		Check(!a.Events.Lobbies.Any(), "no lobby left");
	}

	/// <summary>
	/// The owner's report on 0.23.1: the Host gave a race up early and the lobby came back after the results with them still its Host.
	/// Giving up now leaves the lobby: the Host passes on, and a lobby nobody is left in closes at once, freeing the course.
	/// </summary>
	private static void LeavesRace(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2);
		a.Create(0);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		Agree(room, "B in");
		a.Start();
		Agree(room, "the Start");
		room.Run(EventRoom.StartLead + 2.0);
		a.Leave();
		Agree(room, "after the Host left the race");
		Lobby l = Only(room);
		Check(l.Host == 2 && a.Mine == null, "B is the Host and A is in no lobby");
		Check(l.State == LobbyState.Racing && l.Entrants.Any(e => e.Id == 1 && e.Out && e.Run == Run.Dnf), "B races on; A is a DNF in its results");
		room.Run(EventRoom.ResultsSeconds + Courses[0].TimeLimit + 1.0);
		Agree(room, "after the race and its results");
		Check(Only(room).Host == 2 && Ids(Only(room)) == "2" && a.Mine == null, "after the results the lobby is B's alone; A did not come back");
		// The last one out mid-race: the lobby closes at once and the course is free.
		b.Start();
		Agree(room, "B's second race");
		room.Run(EventRoom.StartLead + 2.0);
		b.Leave();
		Agree(room, "after the last one left mid-race");
		Check(!room.Arbiter.Events.Lobbies.Any() && !b.Events.Lobbies.Any() && room.Arbiter.Events.Taking(0) == null, "the empty lobby closed at once, the course free");
		// Alone (a solo run on the arbiter): leaving closes it at once too.
		a.Create(1);
		a.Start();
		Agree(room, "A's solo run");
		room.Run(EventRoom.StartLead + 1.0);
		a.Leave();
		Check(!a.Events.Lobbies.Any(), "a lobby of one closes when its one entrant leaves the race");
	}

	private static void LateJoiner(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2);
		a.Create(0);
		b.Create(3, false, true);
		Agree(room, "two lobbies");
		b.Start();
		Agree(room, "B racing");
		Machine c = room.Add(3);
		Agree(room, "after C joined the room");
		Check(c.Events.Lobbies.Count() == 2, "C was sent both lobbies");
		Check(c.Heard.All(h => h.why == Why.Snapshot), "a snapshot is no news to call out");
		Check(c.Events.Taking(3) != null && !c.Events.Taking(3).Guns, "C sees the highway taken, guns off");
		// D joined the room before its world was built: what it was sent then was lost. Its Hello brings every lobby.
		Machine d = room.Add(4);
		room.Settle();
		d.Events.Reset();
		Check(!d.Events.Lobbies.Any(), "D's copy is empty before its Hello");
		d.Events.Hello(d.Clock);
		Agree(room, "after D's Hello");
		Check(d.Events.Lobbies.Count() == 2, "D was sent both lobbies on its Hello");
		a.Events.Hello(a.Clock);
		Agree(room, "after the arbiter's own Hello");
	}

	private static void FullAndRaceOn(Room room)
	{
		List<Machine> all = new List<Machine>();
		for (ulong i = 1; i <= 10; i++)
		{
			all.Add(room.Add(i));
		}
		all[0].Create(2);
		Agree(room, "a hill climb lobby");
		int id = all[0].Mine.Id;
		for (int i = 1; i < 10; i++)
		{
			all[i].Join(id);
			room.Run(0.01);
		}
		Agree(room, "nine joins");
		Check(Only(room).Count == Courses[2].MaxEntrants, "the lobby takes " + Courses[2].MaxEntrants + ": " + Only(room).Count);
		List<Machine> outside = all.Where(m => m.Mine == null).ToList();
		Check(outside.Count == 2 && outside.All(m => m.Refusals.Contains(Refusal.Full)), "the two whose joins came last are told it is full");
		all[0].Start();
		Agree(room, "racing");
		outside[0].Join(id);
		Agree(room, "a join while racing");
		Check(outside[0].Refusals.Contains(Refusal.RaceOn) && outside[0].Mine == null, "no joining a race that is on");
		Machine other = all.First(m => m != all[0] && m.Mine != null);
		other.Start();
		Agree(room, "a Start by another");
		Check(other.Refusals.Contains(Refusal.NotHost), "only the Host starts");
		all[9].Join(999);
		Agree(room, "a join of no lobby");
		Check(all[9].Refusals.Contains(Refusal.NoLobby), "no such lobby");
		all[9].Create(17);
		Agree(room, "a create at no course");
		Check(all[9].Refusals.Contains(Refusal.NoCourse), "no such course");
	}

	private static void Solo(Room room)
	{
		// No room: this machine alone is the arbiter, and a lobby of one is a solo run.
		Machine a = room.Add(1);
		a.Create(3);
		Check(a.Mine != null && a.Mine.Host == 1, "the lobby is there at once (no network)");
		a.Start();
		Check(a.Mine.State == LobbyState.Racing, "racing at once");
		room.Run(EventRoom.StartLead);
		for (int g = 0; g < Courses[3].Gates; g++)
		{
			a.Gate(g, 30f * (g + 1));
		}
		Check(a.Mine.State == LobbyState.Results && a.Mine.Entrants[0].Run == Run.Finished, "finished: the results at once, no cut-off to wait for");
		room.Run(EventRoom.ResultsSeconds + 0.1);
		Check(a.Mine.State == LobbyState.Waiting, "waiting again after the results");
		a.Leave();
		Check(!a.Events.Lobbies.Any(), "gone when left");
		Check(room.Sent == 0, "nothing was sent: " + room.Sent);
	}

	private static void Rules(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2), c = room.Add(3), d = room.Add(4), e = room.Add(5);
		Check(a.Events.MayHit(1, 2, HitKind.Gun, a.Clock) && a.Events.MayUse(1, HitKind.Gun, a.Clock), "roaming players hit each other as before");
		a.Create(0, false, true);
		Agree(room, "A's lobby: melee only");
		b.Join(a.Mine.Id);
		Agree(room, "B in");
		c.Create(1, true, false);
		Agree(room, "C's lobby: guns only");
		d.Join(c.Mine.Id);
		Agree(room, "D in");
		foreach (Machine m in new[] { a, e })
		{
			EventRoom r = m.Events;
			double now = m.Clock;
			Check(!r.MayHit(1, 2, HitKind.Melee, now), "no hits in a lobby before its race");
			Check(!r.MayUse(1, HitKind.Melee, now), "no weapons in a lobby before its race");
			Check(!r.MayHit(5, 1, HitKind.Crash, now) && !r.MayHit(1, 5, HitKind.Gun, now), "an entrant and a roamer never hurt each other, crashes included");
			Check(!r.MayHit(1, 3, HitKind.Gun, now), "entrants of two lobbies never hurt each other");
			Check(r.MayUse(5, HitKind.Gun, now), "the roamer may still fire");
		}
		a.Start();
		c.Start();
		Agree(room, "both racing");
		Check(!a.Events.MayHit(1, 2, HitKind.Melee, a.Clock), "no hits on the grid before green");
		room.Run(EventRoom.StartLead + 0.5);
		Agree(room, "past green");
		foreach (Machine m in new[] { a, b, e })
		{
			EventRoom r = m.Events;
			double now = m.Clock;
			Check(r.MayHit(1, 2, HitKind.Melee, now) && r.MayHit(2, 1, HitKind.Crash, now), "melee and crashes count in A's race");
			Check(!r.MayHit(1, 2, HitKind.Gun, now) && !r.MayHit(1, 2, HitKind.Explosion, now) && !r.MayHit(1, 2, HitKind.Tire, now), "guns, explosions and tire shots do not in A's race");
			Check(r.MayHit(3, 4, HitKind.Gun, now) && !r.MayHit(3, 4, HitKind.Melee, now), "guns only in C's race");
			Check(!r.MayUse(1, HitKind.Gun, now) && r.MayUse(1, HitKind.Melee, now) && r.MayUse(3, HitKind.Tire, now), "each entrant may use what their Host allowed");
			Check(!r.MayHit(1, 3, HitKind.Crash, now), "racers of two races never hurt each other");
		}
		// B finished: no longer running, so out of the fight.
		for (int g = 0; g < Courses[0].Gates; g++)
		{
			b.Gate(g, 10f * (g + 1));
		}
		Agree(room, "B finished");
		Check(!a.Events.MayHit(1, 2, HitKind.Crash, a.Clock) && !a.Events.MayUse(2, HitKind.Melee, a.Clock), "a finisher neither hurts nor is hurt");
	}

	private static void Stale(Room room)
	{
		Machine a = room.Add(1), b = room.Add(2);
		a.Create(2);
		Agree(room, "A's lobby");
		b.Join(a.Mine.Id);
		Agree(room, "B in");
		a.Start();
		Agree(room, "the Start");
		room.Run(EventRoom.StartLead);
		int race = a.Mine.Race;
		// A gate from an earlier race, gates going backwards, past the finish, a NaN time, then a bad version and junk.
		a.Events.Passed(race - 1, 0, 10f, 0, a.Clock);
		a.Events.Passed(race, 3, 10f, 0, a.Clock);
		a.Events.Passed(race, 1, 11f, 0, a.Clock);
		a.Events.Passed(race, 99, 12f, 0, a.Clock);
		b.Events.Passed(race, 0, float.NaN, 0, b.Clock);
		Agree(room, "after stale gates");
		Lobby l = Only(room);
		Check(l.Entrants.First(e => e.Id == 1).Passed == 4 && l.Entrants.First(e => e.Id == 2).Passed == 0, "only gate 3 counted for A, nothing for B: " + l.Entrants[0].Passed + ", " + l.Entrants[1].Passed);
		string before = Fingerprint(room.Arbiter.Events.Find(l.Id));
		room.Arbiter.Events.Receive(2, new byte[] { 99, (byte)Msg.Leave }, a.Clock);
		room.Arbiter.Events.Receive(2, new byte[] { EventRoom.Version, (byte)Msg.Join }, a.Clock);
		room.Arbiter.Events.Receive(2, new byte[] { EventRoom.Version }, a.Clock);
		room.Arbiter.Events.Receive(2, null, a.Clock);
		double offset = b.Events.Offset;
		b.Events.Receive(1, new byte[] { EventRoom.Version, (byte)Msg.Lobby, 1, 2 }, b.Clock);
		b.Events.Receive(1, new byte[] { EventRoom.Version, (byte)Msg.Clock, 0, 0, 0, 0, 0, 0, 0xF8, 0x7F }, b.Clock);
		Check(b.Events.Offset == offset, "a broken message and a NaN clock left B's clock as it was");
		Agree(room, "after bad messages");
		Check(Fingerprint(room.Arbiter.Events.Find(l.Id)) == before, "bad messages changed nothing");
		Check(b.Mine != null, "B's copy survived a broken lobby message");
		// A retires, comes back later in the same race: no new gates.
		a.Retire();
		a.Events.Passed(race, 5, 50f, 0, a.Clock);
		Agree(room, "after a retire");
		Check(Only(room).Entrants.First(e => e.Id == 1).Passed == 4, "a retired entrant's gates no longer count");
	}

	/// <summary>Twenty players doing random things for ten minutes; every copy must agree with the arbiter throughout, and the rules hold.</summary>
	private static void Random(Room room)
	{
		List<Machine> all = new List<Machine>();
		for (ulong i = 1; i <= 20; i++)
		{
			all.Add(room.Add(i));
		}
		System.Random rng = new System.Random(room.Rng.Next());
		for (int step = 0; step < 3000; step++)
		{
			Machine m = all[rng.Next(all.Count)];
			if (!m.Present)
			{
				if (rng.Next(20) == 0)
				{
					all[all.IndexOf(m)] = room.Add(m.Local + 100);
				}
				continue;
			}
			Lobby mine = m.Mine;
			int pick = rng.Next(100);
			if (pick < 8)
			{
				m.Create(rng.Next(Courses.Length), rng.Next(2) == 0, rng.Next(2) == 0);
			}
			else if (pick < 25)
			{
				List<Lobby> open = m.Events.Lobbies.ToList();
				if (open.Count > 0)
				{
					m.Join(open[rng.Next(open.Count)].Id);
				}
			}
			else if (pick < 30)
			{
				m.Leave();
			}
			else if (pick < 40)
			{
				m.Start();
			}
			else if (pick < 85 && mine != null && mine.State == LobbyState.Racing && m.Now >= mine.StartAt)
			{
				Entrant e = mine.In(m.Local);
				if (e != null && e.Run == Run.Running)
				{
					m.Gate(Math.Min(Courses[mine.Course].Gates - 1, e.Passed + (rng.Next(10) == 0 ? 2 : 0)), (float)(m.Now - mine.StartAt));
				}
			}
			else if (pick < 86 && mine != null && mine.State == LobbyState.Racing)
			{
				m.Retire();
			}
			else if (pick == 99 && m.Local != room.Host)
			{
				room.Remove(m.Local);
			}
			room.Run(rng.NextDouble() * 0.4);
			if (step % 100 == 99)
			{
				Agree(room, "at step " + (step + 1));
				Machine arbiter = room.Arbiter;
				foreach (Lobby l in arbiter.Events.Lobbies)
				{
					Check(l.Count <= Courses[l.Course].MaxEntrants, "lobby " + l.Id + " over its course's grid");
					Check(l.Count == 0 || l.In(l.Host) != null, "lobby " + l.Id + "'s Host is in it");
					Check(l.State != LobbyState.Racing || arbiter.Clock <= l.EndsAt + 0.1, "lobby " + l.Id + " racing past its time limit");
				}
				for (int k = 0; k < Courses.Length; k++)
				{
					Check(arbiter.Events.Lobbies.Count(l => l.Course == k && l.State != LobbyState.Waiting) <= 1, "one race per course");
				}
				HashSet<ulong> seen = new HashSet<ulong>();
				foreach (Lobby l in arbiter.Events.Lobbies)
				{
					foreach (Entrant e in l.Entrants.Where(x => !x.Out))
					{
						Check(seen.Add(e.Id), "player " + e.Id + " in two lobbies");
					}
				}
			}
		}
		Agree(room, "at the end");
		int races = room.Arbiter.Events.Lobbies.Sum(l => l.Race);
		Console.WriteLine("        " + room.Arbiter.Events.Lobbies.Count() + " lobbies left, " + races + " races in them");
	}

	/// <summary>
	/// The two-player report after 0.26.2: one player was held on the grid after the others had gone, because the race clock was game
	/// time and their long frames held it back. Six machines, clocks up to 30 s apart, each stalling now and then for up to 1.5 s (a
	/// teleport, tiles building, a refit), race three times: every machine's green is the arbiter's to within a message's delay, every
	/// gate time read from a machine's clock is what the arbiter's clock says to within the same, and the lobbies agree.
	/// </summary>
	private static void Stalls(Room room)
	{
		room.Stalls = true;
		List<Machine> all = new List<Machine>();
		for (ulong i = 1; i <= 6; i++)
		{
			all.Add(room.Add(i));
		}
		System.Random rng = new System.Random(room.Rng.Next());
		Machine a = all[0];
		Dictionary<(ulong, int), double> greens = new Dictionary<(ulong, int), double>();
		Dictionary<ulong, int> nextGate = new Dictionary<ulong, int>();
		double worstGreen = 0.0, worstTime = 0.0;
		int gates = Courses[0].Gates;
		room.Watch = () =>
		{
			foreach (Machine m in all)
			{
				if (!m.Stalled && rng.NextDouble() < 0.004)
				{
					m.StalledUntil = room.Now + 0.1 + rng.NextDouble() * 1.4;
				}
			}
			Lobby truth = a.Mine;
			if (truth == null || truth.State != LobbyState.Racing)
			{
				return;
			}
			foreach (Machine m in all)
			{
				Lobby l = m.Mine;
				if (l == null || l.State != LobbyState.Racing || l.Race != truth.Race || m.Now < l.StartAt || greens.ContainsKey((m.Local, l.Race)))
				{
					continue;
				}
				// The moment this machine's clock says green (whether or not its frame is long then: that is not the clock's doing).
				greens[(m.Local, l.Race)] = room.Now;
				nextGate[m.Local] = 0;
			}
			if (!greens.TryGetValue((a.Local, truth.Race), out double green))
			{
				return;
			}
			foreach (Machine m in all)
			{
				Lobby l = m.Mine;
				if (m.Stalled || l == null || !nextGate.TryGetValue(m.Local, out int g) || g >= gates || l.In(m.Local)?.Run != Run.Running)
				{
					continue;
				}
				// Everybody drives each gate 10 s apart from the arbiter's green: the time each machine reads is its clock's.
				if (room.Now >= green + 10.0 * (g + 1))
				{
					float time = (float)(m.Now - l.StartAt);
					double truly = room.Now - green;
					worstTime = Math.Max(worstTime, Math.Abs(time - truly));
					Check(Math.Abs(time - truly) <= MaxDelay + Slack, "player " + m.Local + " timed gate " + g + " at " + time.ToString("0.000") + " s, the arbiter's clock says " + truly.ToString("0.000"));
					m.Gate(g, time);
					nextGate[m.Local] = g + 1;
				}
			}
		};
		for (int r = 0; r < 3; r++)
		{
			if (a.Mine == null)
			{
				a.Create(0);
				Agree(room, "A's lobby");
			}
			foreach (Machine m in all.Skip(1).Where(x => x.Mine == null))
			{
				m.Join(a.Mine.Id);
				room.Run(0.1);
			}
			Agree(room, "everybody in");
			room.Run(rng.NextDouble() * 20.0);
			a.Start();
			Agree(room, "the Start");
			room.Run(EventRoom.StartLead + 10.0 * gates + 3.0);
			Agree(room, "after race " + (r + 1));
			Lobby l = a.Mine;
			Check(l.State == LobbyState.Results && l.Entrants.All(e => e.Run == Run.Finished), "race " + (r + 1) + ": everybody finished");
			double arbiterGreen = greens[(a.Local, l.Race)];
			foreach (Machine m in all.Skip(1))
			{
				Check(greens.TryGetValue((m.Local, l.Race), out double g), "player " + m.Local + " saw green in race " + (r + 1));
				worstGreen = Math.Max(worstGreen, Math.Abs(g - arbiterGreen));
				Check(Math.Abs(g - arbiterGreen) <= MaxDelay + Slack, "player " + m.Local + "'s green is " + (g - arbiterGreen).ToString("0.000") + " s from the arbiter's in race " + (r + 1));
			}
			room.Run(EventRoom.ResultsSeconds + 1.0);
		}
		room.Watch = null;
		foreach (Machine m in all)
		{
			m.StalledUntil = -1.0;
		}
		room.Run(EventRoom.SyncWindow + EventRoom.ClockEvery);
		room.Stalls = false;
		Agree(room, "after the stalls");
		Console.WriteLine("        greens within " + (worstGreen * 1000.0).ToString("0") + " ms of the arbiter's, gate times within " + (worstTime * 1000.0).ToString("0") + " ms");
	}

	/// <summary>
	/// The report's point 6: lobby notices unreliable. Players join the room and their worlds are built up to 20 s later, while lobbies
	/// open, race and close; what came before the world was held (up to 64, the rest lost), and the Hellos go on until a snapshot comes.
	/// Every machine ends with exactly the arbiter's lobbies.
	/// </summary>
	private static void LateWorlds(Room room)
	{
		System.Random rng = new System.Random(room.Rng.Next());
		Machine a = room.Add(1), b = room.Add(2);
		List<Machine> late = new List<Machine>();
		a.Create(0);
		b.Create(3);
		Agree(room, "two lobbies");
		for (ulong i = 3; i <= 10; i++)
		{
			late.Add(room.Add(i, rng.NextDouble() * 20.0));
			room.Run(0.5);
		}
		Check(late.Any(m => !m.Built), "some worlds are still being built");
		Check(late.Where(m => !m.Built).All(m => !m.Events.Linked), "no world, no link to the arbiter");
		// While they build: lobbies open, race and close; every machine's notices are held.
		b.Start();
		a.Leave();
		room.Run(1.0);
		a.Create(2, false, true);
		for (int k = 0; k < 40; k++)
		{
			a.Leave();
			a.Create(k % 4);
			room.Run(0.05);
		}
		room.Run(25.0);
		Check(late.All(m => m.Built), "every world built");
		Agree(room, "after every world was built");
		Check(late.All(m => m.Events.Lobbies.Count() == a.Events.Lobbies.Count()), "every machine has the arbiter's " + a.Events.Lobbies.Count() + " lobbies");
		Console.WriteLine("        " + late.Sum(m => m.Lost) + " messages lost before worlds were built, made up by the snapshots");
		// One of them joins a lobby straight away: it has heard the arbiter.
		late[0].Join(a.Mine.Id);
		Agree(room, "a late player joins");
		Check(late[0].Mine != null && late[0].Mine.Id == a.Mine.Id, "the late player is in A's lobby");
		// A machine that has not heard the arbiter is turned down here, at once.
		Machine fresh = room.Add(20, 30.0);
		fresh.Built = true;
		fresh.Create(1);
		Check(fresh.Refusals.Contains(Refusal.Linking) && fresh.Mine == null, "a create before the arbiter is heard is turned down");
	}
}
