using System;
using System.Collections.Generic;
using System.IO;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events in a room: lobbies a player creates or joins at a course's start box, one race per course at a time, the standings,
	/// the cut-off and the time limit, the results, and the weapon rules the lobby's Host set. No Unity types, so <c>tools/eventsim</c>
	/// drives several copies of it over a pretend network.
	///
	/// One machine holds every lobby: the arbiter, which is the Multiplayer room's host (the room closes when its host leaves, so the
	/// arbiter never changes while a room lasts), or this machine alone outside a room, where a lobby of one is a solo run. Every
	/// change is a request to the arbiter (the arbiter's own go through the same handler, at once); it applies the requests in the order
	/// they arrive, so two players creating, joining or starting at once can never clash, and sends each lobby whole to everybody after
	/// each change. Every machine keeps the copy it was sent, and reads its lobbies, its standings and the rules from it. A player who
	/// joins the room is sent every lobby; one who leaves is taken out of every lobby by the arbiter (a DNF in a race).
	///
	/// Times are the race clock (<c>TrackManager.TimeSinceRaceStart</c>, which the Multiplayer mod starts on the host's go for
	/// everybody), so the start time the arbiter deals is the same moment on every machine, give or take the room's latency. Each
	/// entrant times their own run from their own green, so that latency never counts against anybody.
	///
	/// Every message starts with <see cref="Version"/>, then its <see cref="Msg"/>. A message of another version, or one that does not
	/// read, is dropped (everybody in a room has the same mods, the Multiplayer mod checks, so that is only ever a fault).
	/// </summary>
	internal sealed class EventRoom
	{
		/// <summary>The messages' layout. Raise it with any change to what they carry.</summary>
		internal const byte Version = 1;

		/// <summary>From the Start to green, on the race clock: time for every entrant's car to be put on its slot and settle.</summary>
		internal const double StartLead = 5.0;

		/// <summary>After the first entrant finishes, the others have this long; those still running then are out (DNF).</summary>
		internal const double Cutoff = 45.0;

		/// <summary>How long the results stand before the course is free and the lobby waits again.</summary>
		internal const double ResultsSeconds = 15.0;

		internal enum Msg : byte
		{
			// To the arbiter.
			Create = 1,
			Join = 2,
			Leave = 3,
			Start = 4,
			Gate = 5,
			Retire = 6,
			// From the arbiter.
			Lobby = 20,
			Gone = 21,
			Refused = 22,
		}

		internal enum LobbyState : byte
		{
			/// <summary>Entrants gather; the Host may start when the course is free.</summary>
			Waiting = 0,
			/// <summary>The race: the grid and the countdown until <see cref="Lobby.StartAt"/>, then racing.</summary>
			Racing = 1,
			/// <summary>The race is over and its results stand until <see cref="Lobby.ResultsUntil"/>; the course is still taken.</summary>
			Results = 2,
		}

		internal enum Run : byte
		{
			/// <summary>In the lobby, not in a race.</summary>
			Waiting = 0,
			Running = 1,
			Finished = 2,
			/// <summary>Out of the race: retired, left, or still running at the cut-off or the time limit.</summary>
			Dnf = 3,
		}

		/// <summary>Why a lobby was sent: what the UI calls out.</summary>
		internal enum Why : byte
		{
			/// <summary>Sent to a player who joined the room, or after the arbiter changed: nothing happened.</summary>
			Snapshot = 0,
			Created = 1,
			Joined = 2,
			Left = 3,
			Started = 4,
			Passed = 5,
			Finished = 6,
			Retired = 7,
			/// <summary>The race ended: everybody finished or is out, the cut-off, or the time limit.</summary>
			Ended = 8,
			/// <summary>The results are done: the course is free and the lobby waits again.</summary>
			Freed = 9,
		}

		internal enum Refusal : byte
		{
			NoLobby = 1,
			Full = 2,
			/// <summary>The lobby's race is on: join it when its results are done.</summary>
			RaceOn = 3,
			NotHost = 4,
			/// <summary>Another lobby's race has the course.</summary>
			CourseTaken = 5,
			NotWaiting = 6,
			NoCourse = 7,
		}

		/// <summary>
		/// What a hit is, as the Multiplayer mod's <c>Combat</c> numbers them (gun 0, explosion 1, item 2, tire 3), and a crash, which
		/// it sends as an item but the rules tell apart.
		/// </summary>
		internal enum HitKind
		{
			Gun = 0,
			Explosion = 1,
			Melee = 2,
			Tire = 3,
			Crash = 4,
		}

		/// <summary>What the lobbies need of a course: the same on every machine, since every machine has the same layout.</summary>
		internal struct CourseInfo
		{
			public string Name;
			/// <summary>Checkpoints to the finish; the last is the finish line.</summary>
			public int Gates;
			public int MaxEntrants;
			/// <summary>Seconds from green.</summary>
			public double TimeLimit;

			/// <summary>In the game, from a course of <see cref="HalfGripOpenWorld.Courses"/> (its name, gate count, slot count and time limit).</summary>
			public CourseInfo(string name, int gates, int maxEntrants, double timeLimit)
			{
				Name = name;
				Gates = gates;
				MaxEntrants = maxEntrants;
				TimeLimit = timeLimit;
			}
		}

		internal sealed class Entrant
		{
			public ulong Id;
			public Run Run;
			/// <summary>The grid slot dealt at the Start: join order.</summary>
			public int Slot;
			/// <summary>Checkpoints passed (the last gate passed, plus one): the finish is the course's gate count.</summary>
			public int Passed;
			/// <summary>The entrant's own clock at the last gate passed (the finish time once finished), from their green.</summary>
			public float Time;
			/// <summary>Gates missed on the way (a finish with any is ranked after the clean ones).</summary>
			public int Missed;
			/// <summary>Left the lobby while its race or results were on: kept for the results, then taken out.</summary>
			public bool Out;
		}

		internal sealed class Lobby
		{
			public int Id;
			public int Course;
			/// <summary>The lobby's Host: who created it, then the next in if they leave (0 while nobody is left in a race's results).</summary>
			public ulong Host;
			public bool Guns;
			public bool Melee;
			public LobbyState State;
			/// <summary>Counts the lobby's races, so a gate passed in an earlier one is never counted in the next.</summary>
			public int Race;
			/// <summary>Green, on the race clock.</summary>
			public double StartAt;
			/// <summary>The time limit, on the race clock.</summary>
			public double EndsAt;
			/// <summary>The cut-off after the first finisher, on the race clock (negative before anybody has finished).</summary>
			public double CutoffAt = -1.0;
			public double ResultsUntil;
			/// <summary>In join order.</summary>
			public readonly List<Entrant> Entrants = new List<Entrant>();

			/// <summary>The entrant with this id still in the lobby (not one who left during the race), or null.</summary>
			public Entrant In(ulong id)
			{
				foreach (Entrant e in Entrants)
				{
					if (e.Id == id && !e.Out)
					{
						return e;
					}
				}
				return null;
			}

			/// <summary>Entrants still in the lobby.</summary>
			public int Count
			{
				get
				{
					int n = 0;
					foreach (Entrant e in Entrants)
					{
						n += e.Out ? 0 : 1;
					}
					return n;
				}
			}
		}

		/// <summary>How the room's messages go. In a room it is the Multiplayer mod's (<c>ctx.Net</c>); alone, nowhere.</summary>
		internal interface ILink
		{
			/// <summary>This player's id.</summary>
			ulong Local { get; }
			/// <summary>True on the machine that holds the lobbies: the room's host, or this machine alone outside a room.</summary>
			bool Arbiter { get; }
			void ToArbiter(byte[] data);
			/// <summary>To every other player (the arbiter has already applied it to its own copy).</summary>
			void ToAll(byte[] data);
			void ToOne(ulong to, byte[] data);
		}

		private readonly ILink link;
		private readonly CourseInfo[] courses;
		private readonly SortedDictionary<int, Lobby> lobbies = new SortedDictionary<int, Lobby>();
		private int nextId;

		/// <summary>A lobby was sent (created, changed, or in a snapshot): why, and who it was about (0 for nobody).</summary>
		internal event Action<Lobby, Why, ulong> Changed;
		/// <summary>A lobby was taken away (its last entrant left).</summary>
		internal event Action<int> Removed;
		/// <summary>This player's request was turned down.</summary>
		internal event Action<Refusal> Refused;

		internal EventRoom(ILink link, CourseInfo[] courses)
		{
			this.link = link;
			this.courses = courses;
		}

		internal IReadOnlyList<CourseInfo> CourseList => courses;

		/// <summary>Every lobby, by id (the order they were created in).</summary>
		internal IEnumerable<Lobby> Lobbies => lobbies.Values;

		internal Lobby Find(int id)
		{
			return lobbies.TryGetValue(id, out Lobby l) ? l : null;
		}

		/// <summary>The lobby this player is in (not one they left during its race), or null.</summary>
		internal Lobby LobbyOf(ulong id)
		{
			foreach (Lobby l in lobbies.Values)
			{
				if (l.In(id) != null)
				{
					return l;
				}
			}
			return null;
		}

		/// <summary>The local player's lobby, or null.</summary>
		internal Lobby Mine => LobbyOf(link.Local);

		/// <summary>The lobby whose race or results have the course, or null when it is free.</summary>
		internal Lobby Taking(int course)
		{
			foreach (Lobby l in lobbies.Values)
			{
				if (l.Course == course && l.State != LobbyState.Waiting)
				{
					return l;
				}
			}
			return null;
		}

		/// <summary>Forgets every lobby: the room was left or another joined, so the arbiter is another machine (or this one alone).</summary>
		internal void Reset()
		{
			lobbies.Clear();
			nextId = 0;
		}

		// ---- requests ------------------------------------------------------------------------------------------------

		/// <summary>Creates a lobby at the course (leaving any other), its Host this player, with the weapons they allow.</summary>
		internal void Create(int course, bool guns, bool melee, double now)
		{
			Request(new Writer(Msg.Create).U8(course).U8((guns ? 1 : 0) | (melee ? 2 : 0)).Done(), now);
		}

		/// <summary>Joins a lobby (leaving any other).</summary>
		internal void Join(int lobby, double now)
		{
			Request(new Writer(Msg.Join).I32(lobby).Done(), now);
		}

		/// <summary>Leaves this player's lobby: at once while it waits; in its race, a DNF.</summary>
		internal void Leave(double now)
		{
			Request(new Writer(Msg.Leave).Done(), now);
		}

		/// <summary>The Host starts the lobby's race, when the course is free.</summary>
		internal void Start(double now)
		{
			Request(new Writer(Msg.Start).Done(), now);
		}

		/// <summary>
		/// The local player passed gate <paramref name="gate"/> (0 on; the last is the finish) of the race, at <paramref name="time"/>
		/// on their own clock from green, having missed <paramref name="missed"/> gates so far.
		/// </summary>
		internal void Passed(int race, int gate, float time, int missed, double now)
		{
			Request(new Writer(Msg.Gate).I32(race).U16(gate).F32(time).U16(missed).Done(), now);
		}

		/// <summary>The local player gave the race up (off the course too long, or a teleport): a DNF, still in the lobby.</summary>
		internal void Retire(int race, double now)
		{
			Request(new Writer(Msg.Retire).I32(race).Done(), now);
		}

		private void Request(byte[] data, double now)
		{
			if (link.Arbiter)
			{
				Handle(link.Local, data, now);
			}
			else
			{
				link.ToArbiter(data);
			}
		}

		// ---- the room ------------------------------------------------------------------------------------------------

		/// <summary>A message from another player (or the arbiter).</summary>
		internal void Receive(ulong from, byte[] data, double now)
		{
			if (data == null || data.Length < 2 || data[0] != Version)
			{
				return;
			}
			if (data[1] < (byte)Msg.Lobby)
			{
				if (link.Arbiter)
				{
					Handle(from, data, now);
				}
				return;
			}
			try
			{
				Reader r = new Reader(data);
				switch (r.Type)
				{
					case Msg.Lobby:
						Why why = (Why)r.U8();
						ulong who = r.U64();
						Lobby l = ReadLobby(r);
						lobbies[l.Id] = l;
						Changed?.Invoke(l, why, who);
						break;
					case Msg.Gone:
						int id = r.I32();
						if (lobbies.Remove(id))
						{
							Removed?.Invoke(id);
						}
						break;
					case Msg.Refused:
						Refused?.Invoke((Refusal)r.U8());
						break;
				}
			}
			catch (Exception)
			{
				// A message that does not read is dropped (see the class's summary).
			}
		}

		/// <summary>The arbiter: a player joined the room, so they are sent every lobby.</summary>
		internal void PlayerJoined(ulong id)
		{
			if (!link.Arbiter || id == link.Local)
			{
				return;
			}
			foreach (Lobby l in lobbies.Values)
			{
				link.ToOne(id, LobbyMessage(l, Why.Snapshot, 0UL));
			}
		}

		/// <summary>The arbiter: a player left the room, so they leave every lobby (a DNF in a race).</summary>
		internal void PlayerLeft(ulong id, double now)
		{
			if (!link.Arbiter)
			{
				return;
			}
			Lobby l = LobbyOf(id);
			if (l != null)
			{
				Leaving(l, id, now);
			}
		}

		/// <summary>The arbiter, each frame: the cut-off and the time limit, and the results' end.</summary>
		internal void Tick(double now)
		{
			if (!link.Arbiter)
			{
				return;
			}
			foreach (Lobby l in new List<Lobby>(lobbies.Values))
			{
				if (l.State == LobbyState.Racing)
				{
					Ending(l, now);
				}
				else if (l.State == LobbyState.Results && now >= l.ResultsUntil)
				{
					Free(l);
				}
			}
		}

		// ---- the arbiter ---------------------------------------------------------------------------------------------

		private void Handle(ulong from, byte[] data, double now)
		{
			Reader r;
			try
			{
				r = new Reader(data);
			}
			catch (Exception)
			{
				return;
			}
			try
			{
				switch (r.Type)
				{
					case Msg.Create:
						Create(from, r.U8(), r.U8(), now);
						break;
					case Msg.Join:
						Join(from, r.I32(), now);
						break;
					case Msg.Leave:
						Lobby left = LobbyOf(from);
						if (left != null)
						{
							Leaving(left, from, now);
						}
						break;
					case Msg.Start:
						Start(from, now);
						break;
					case Msg.Gate:
						Gate(from, r.I32(), r.U16(), r.F32(), r.U16(), now);
						break;
					case Msg.Retire:
						Retire(from, r.I32(), now);
						break;
				}
			}
			catch (Exception)
			{
				// A request that does not read is dropped.
			}
		}

		private void Create(ulong from, int course, int rules, double now)
		{
			if (course < 0 || course >= courses.Length || courses[course].MaxEntrants < 1)
			{
				Refuse(from, Refusal.NoCourse);
				return;
			}
			Lobby old = LobbyOf(from);
			if (old != null)
			{
				Leaving(old, from, now);
			}
			Lobby l = new Lobby { Id = ++nextId, Course = course, Host = from, Guns = (rules & 1) != 0, Melee = (rules & 2) != 0 };
			l.Entrants.Add(new Entrant { Id = from });
			lobbies[l.Id] = l;
			Send(l, Why.Created, from);
		}

		private void Join(ulong from, int id, double now)
		{
			Lobby l = Find(id);
			if (l == null)
			{
				Refuse(from, Refusal.NoLobby);
				return;
			}
			if (l.In(from) != null)
			{
				return;
			}
			if (l.State == LobbyState.Racing)
			{
				Refuse(from, Refusal.RaceOn);
				return;
			}
			if (l.Count >= Math.Min(255, courses[l.Course].MaxEntrants))
			{
				Refuse(from, Refusal.Full);
				return;
			}
			Lobby old = LobbyOf(from);
			if (old != null)
			{
				Leaving(old, from, now);
			}
			// A player who left during this lobby's race or results and comes back takes up a new place in the order.
			l.Entrants.RemoveAll(e => e.Id == from);
			l.Entrants.Add(new Entrant { Id = from });
			if (l.Host == 0UL)
			{
				l.Host = from;
			}
			Send(l, Why.Joined, from);
		}

		/// <summary>Takes a player out of a lobby: gone at once while it waits; in its race or results, kept (a DNF if running) until they are done.</summary>
		private void Leaving(Lobby l, ulong id, double now)
		{
			Entrant e = l.In(id);
			if (e == null)
			{
				return;
			}
			if (l.State == LobbyState.Waiting)
			{
				l.Entrants.Remove(e);
			}
			else
			{
				e.Out = true;
				if (e.Run == Run.Running)
				{
					e.Run = Run.Dnf;
				}
			}
			if (l.Host == id)
			{
				l.Host = 0UL;
				foreach (Entrant other in l.Entrants)
				{
					if (!other.Out)
					{
						l.Host = other.Id;
						break;
					}
				}
			}
			if (l.Entrants.Count == 0)
			{
				Remove(l);
				return;
			}
			Send(l, Why.Left, id);
			if (l.State == LobbyState.Racing)
			{
				Ending(l, now);
			}
		}

		private void Start(ulong from, double now)
		{
			Lobby l = LobbyOf(from);
			if (l == null)
			{
				Refuse(from, Refusal.NoLobby);
				return;
			}
			if (l.Host != from)
			{
				Refuse(from, Refusal.NotHost);
				return;
			}
			if (l.State != LobbyState.Waiting)
			{
				Refuse(from, Refusal.NotWaiting);
				return;
			}
			if (Taking(l.Course) != null)
			{
				Refuse(from, Refusal.CourseTaken);
				return;
			}
			l.Race++;
			l.State = LobbyState.Racing;
			l.StartAt = now + StartLead;
			l.EndsAt = l.StartAt + courses[l.Course].TimeLimit;
			l.CutoffAt = -1.0;
			l.ResultsUntil = 0.0;
			// Grid slots in join order.
			for (int i = 0; i < l.Entrants.Count; i++)
			{
				Entrant e = l.Entrants[i];
				e.Slot = i;
				e.Run = Run.Running;
				e.Passed = 0;
				e.Time = 0f;
				e.Missed = 0;
			}
			Send(l, Why.Started, from);
		}

		private void Gate(ulong from, int race, int gate, float time, int missed, double now)
		{
			Lobby l = LobbyOf(from);
			Entrant e = l != null ? l.In(from) : null;
			int gates = l != null ? courses[l.Course].Gates : 0;
			if (e == null || l.State != LobbyState.Racing || l.Race != race || e.Run != Run.Running || gate < e.Passed || gate >= gates || float.IsNaN(time) || time < 0f)
			{
				return;
			}
			e.Passed = gate + 1;
			e.Time = time;
			e.Missed = missed;
			bool finished = e.Passed == gates;
			if (finished)
			{
				e.Run = Run.Finished;
				if (l.CutoffAt < 0.0)
				{
					l.CutoffAt = now + Cutoff;
				}
			}
			Send(l, finished ? Why.Finished : Why.Passed, from);
			if (finished)
			{
				Ending(l, now);
			}
		}

		private void Retire(ulong from, int race, double now)
		{
			Lobby l = LobbyOf(from);
			Entrant e = l != null ? l.In(from) : null;
			if (e == null || l.State != LobbyState.Racing || l.Race != race || e.Run != Run.Running)
			{
				return;
			}
			e.Run = Run.Dnf;
			Send(l, Why.Retired, from);
			Ending(l, now);
		}

		/// <summary>Ends the race when nobody is running, at the cut-off, or at the time limit: those still running are out.</summary>
		private void Ending(Lobby l, double now)
		{
			bool running = false;
			foreach (Entrant e in l.Entrants)
			{
				running |= e.Run == Run.Running;
			}
			if (running && !(l.CutoffAt >= 0.0 && now >= l.CutoffAt) && now < l.EndsAt)
			{
				return;
			}
			foreach (Entrant e in l.Entrants)
			{
				if (e.Run == Run.Running)
				{
					e.Run = Run.Dnf;
				}
			}
			l.State = LobbyState.Results;
			l.ResultsUntil = now + ResultsSeconds;
			Send(l, Why.Ended, 0UL);
		}

		/// <summary>The results are done: those who left go, the rest wait again, and the course is free.</summary>
		private void Free(Lobby l)
		{
			l.Entrants.RemoveAll(e => e.Out);
			if (l.Entrants.Count == 0)
			{
				Remove(l);
				return;
			}
			l.State = LobbyState.Waiting;
			l.CutoffAt = -1.0;
			foreach (Entrant e in l.Entrants)
			{
				e.Run = Run.Waiting;
			}
			if (l.In(l.Host) == null)
			{
				l.Host = l.Entrants[0].Id;
			}
			Send(l, Why.Freed, 0UL);
		}

		private void Remove(Lobby l)
		{
			lobbies.Remove(l.Id);
			link.ToAll(new Writer(Msg.Gone).I32(l.Id).Done());
			Removed?.Invoke(l.Id);
		}

		private void Send(Lobby l, Why why, ulong who)
		{
			link.ToAll(LobbyMessage(l, why, who));
			Changed?.Invoke(l, why, who);
		}

		private void Refuse(ulong to, Refusal why)
		{
			if (to == link.Local)
			{
				Refused?.Invoke(why);
			}
			else
			{
				link.ToOne(to, new Writer(Msg.Refused).U8((int)why).Done());
			}
		}

		// ---- standings and rules -------------------------------------------------------------------------------------

		/// <summary>
		/// The lobby's entrants in race order: the finishers by time (clean runs before those that missed a gate), then those still
		/// running by gates passed and their time at the last, then those out the same way; ties in join order. The same on every machine.
		/// </summary>
		internal static List<Entrant> Standings(Lobby l)
		{
			List<Entrant> list = new List<Entrant>(l.Entrants);
			Dictionary<Entrant, int> order = new Dictionary<Entrant, int>();
			for (int i = 0; i < list.Count; i++)
			{
				order[list[i]] = i;
			}
			int Group(Entrant e)
			{
				return e.Run == Run.Finished ? (e.Missed == 0 ? 0 : 1) : e.Run == Run.Running ? 2 : e.Run == Run.Waiting ? 3 : 4;
			}
			list.Sort((a, b) =>
			{
				int c = Group(a).CompareTo(Group(b));
				if (c == 0 && Group(a) > 1)
				{
					c = b.Passed.CompareTo(a.Passed);
				}
				if (c == 0)
				{
					c = a.Time.CompareTo(b.Time);
				}
				return c != 0 ? c : order[a].CompareTo(order[b]);
			});
			return list;
		}

		/// <summary>This player's place in the lobby's standings, 1 on, or 0 if they are not in it.</summary>
		internal static int Position(Lobby l, ulong id)
		{
			List<Entrant> list = Standings(l);
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Id == id)
				{
					return i + 1;
				}
			}
			return 0;
		}

		/// <summary>True while the lobby's race is past green and this entrant is running in it.</summary>
		private static bool Racing(Lobby l, ulong id, double now)
		{
			Entrant e = l != null ? l.In(id) : null;
			return e != null && l.State == LobbyState.Racing && now >= l.StartAt && e.Run == Run.Running;
		}

		private static bool Allows(Lobby l, HitKind kind)
		{
			switch (kind)
			{
				case HitKind.Crash:
					return true;
				case HitKind.Melee:
					return l.Melee;
				default:
					return l.Guns;
			}
		}

		/// <summary>
		/// Whether this player may use a weapon of this kind now: anybody outside a lobby may; in a lobby, only while running in its
		/// race past green and only what its Host allowed (a player waiting in a lobby can hurt nobody, so firing is no use).
		/// </summary>
		internal bool MayUse(ulong id, HitKind kind, double now)
		{
			Lobby l = LobbyOf(id);
			return l == null || (Racing(l, id, now) && Allows(l, kind));
		}

		/// <summary>
		/// Whether a hit from one player on another counts. Between two players in no lobby, always (roaming as before). A player in a
		/// lobby or its race and anybody outside it never hurt each other, crashes included. Two entrants of the same race hurt each
		/// other only while both are running in it past green, by what its Host allowed; crashes always count there.
		/// </summary>
		internal bool MayHit(ulong attacker, ulong victim, HitKind kind, double now)
		{
			Lobby a = LobbyOf(attacker), v = LobbyOf(victim);
			if (a == null && v == null)
			{
				return true;
			}
			return a == v && Racing(a, attacker, now) && Racing(a, victim, now) && Allows(a, kind);
		}

		// ---- the messages --------------------------------------------------------------------------------------------

		internal static byte[] LobbyMessage(Lobby l, Why why, ulong who)
		{
			Writer w = new Writer(Msg.Lobby).U8((int)why).U64(who);
			w.I32(l.Id).U8(l.Course).U64(l.Host).U8((l.Guns ? 1 : 0) | (l.Melee ? 2 : 0)).U8((int)l.State).I32(l.Race);
			w.F64(l.StartAt).F64(l.EndsAt).F64(l.CutoffAt).F64(l.ResultsUntil).U8(l.Entrants.Count);
			foreach (Entrant e in l.Entrants)
			{
				w.U64(e.Id).U8((int)e.Run).U8(e.Slot).U16(e.Passed).F32(e.Time).U16(e.Missed).U8(e.Out ? 1 : 0);
			}
			return w.Done();
		}

		private static Lobby ReadLobby(Reader r)
		{
			Lobby l = new Lobby { Id = r.I32(), Course = r.U8(), Host = r.U64() };
			int rules = r.U8();
			l.Guns = (rules & 1) != 0;
			l.Melee = (rules & 2) != 0;
			l.State = (LobbyState)r.U8();
			l.Race = r.I32();
			l.StartAt = r.F64();
			l.EndsAt = r.F64();
			l.CutoffAt = r.F64();
			l.ResultsUntil = r.F64();
			int n = r.U8();
			for (int i = 0; i < n; i++)
			{
				l.Entrants.Add(new Entrant { Id = r.U64(), Run = (Run)r.U8(), Slot = r.U8(), Passed = r.U16(), Time = r.F32(), Missed = r.U16(), Out = r.U8() != 0 });
			}
			return l;
		}

		/// <summary>A lobby as its message, for comparing copies (eventsim): two lobbies are the same if their messages are.</summary>
		internal static string Fingerprint(Lobby l)
		{
			return Convert.ToBase64String(LobbyMessage(l, Why.Snapshot, 0UL));
		}

		private sealed class Writer
		{
			private readonly MemoryStream stream = new MemoryStream(64);
			private readonly BinaryWriter w;

			internal Writer(Msg type)
			{
				w = new BinaryWriter(stream);
				w.Write(Version);
				w.Write((byte)type);
			}

			internal Writer U8(int v) { w.Write((byte)Math.Max(0, Math.Min(255, v))); return this; }
			internal Writer U16(int v) { w.Write((ushort)Math.Max(0, Math.Min(65535, v))); return this; }
			internal Writer I32(int v) { w.Write(v); return this; }
			internal Writer U64(ulong v) { w.Write(v); return this; }
			internal Writer F32(float v) { w.Write(v); return this; }
			internal Writer F64(double v) { w.Write(v); return this; }

			internal byte[] Done()
			{
				w.Flush();
				return stream.ToArray();
			}
		}

		private sealed class Reader
		{
			private readonly BinaryReader r;

			internal readonly Msg Type;

			internal Reader(byte[] data)
			{
				r = new BinaryReader(new MemoryStream(data, false));
				if (r.ReadByte() != Version)
				{
					throw new InvalidDataException("another version");
				}
				Type = (Msg)r.ReadByte();
			}

			internal int U8() { return r.ReadByte(); }
			internal int U16() { return r.ReadUInt16(); }
			internal int I32() { return r.ReadInt32(); }
			internal ulong U64() { return r.ReadUInt64(); }
			internal float F32() { return r.ReadSingle(); }
			internal double F64() { return r.ReadDouble(); }
		}
	}
}
