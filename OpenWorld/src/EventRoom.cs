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
	/// Times are the arbiter's clock: every machine passes in its own clock (real time since its game started, which a long frame never
	/// holds back), and the room adds the offset to the arbiter's (<see cref="Now"/>). The offset matters for one thing only, that green
	/// is the same moment on every machine, so it is taken once a race, where it is needed: the Start puts every entrant on the grid
	/// with no green yet; once a car is on its slot and its machine has settled (the game says when: its tiles built, its frames short
	/// again), it waits for the arbiter to have settled too if the arbiter races (its Ready, up to <see cref="ArbiterWaitMost"/>), then
	/// asks the arbiter for its clock (<see cref="Sync"/>, a ping every <see cref="PingEvery"/>) until it knows it to
	/// <see cref="SyncGood"/> (or <see cref="SyncMost"/> has gone) and tells the arbiter it is ready. The arbiter may still be in its
	/// own long frames, and a ping read at the end of one waited there an unknown time; so each answer says when
	/// the arbiter could have got the ping, from its frame before last to now, and the asker knows it was sent and answered between
	/// its ping going and the answer coming. Each answer bounds the offset; the bounds of every answer are put together and the middle
	/// taken, which a long frame on either side only ever widens, never moves. When every entrant is ready (or <see cref="GridMost"/> after the
	/// Start, so one machine that never answers holds nobody), the arbiter deals green <see cref="CountLead"/> on, and the countdown
	/// runs to it on every machine. From green each entrant times their own run on their own game time, so nothing drifts in a race
	/// and a slow motion, which slows every car in the room, counts against nobody.
	/// Until 0.26 the events ran on <c>TrackManager.TimeSinceRaceStart</c>, game time, which a long frame (a teleport, tiles building,
	/// a refit) holds back by up to two thirds of it: one player was held on the grid after everyone else had gone.
	///
	/// A machine asks the arbiter for every lobby (<see cref="Hello"/>) when its world is built and every <see cref="HelloEvery"/>
	/// seconds until a snapshot's end comes back (<see cref="Msg.SnapshotEnd"/>), which also takes away any lobby it holds that the
	/// arbiter no longer has, so a lost message never leaves it with the wrong lobbies. Until it has heard the arbiter at all
	/// (<see cref="Linked"/>) it cannot create, join or start.
	///
	/// Every message starts with <see cref="Version"/>, then its <see cref="Msg"/>. A message of
	/// another version, or one that does not read, is dropped (everybody in a room has the same mods, the Multiplayer mod checks, so
	/// that is only ever a fault).
	/// </summary>
	internal sealed class EventRoom
	{
		/// <summary>The messages' layout. Raise it with any change to what they carry.</summary>
		internal const byte Version = 3;

		/// <summary>
		/// A sync takes at least this many answers. Several, because each answer's bounds are as wide as its round trip (and any long
		/// frame in it): the one with the quickest way there and the one with the quickest way back set the bounds between them.
		/// </summary>
		internal const int SyncPings = 5;

		/// <summary>
		/// A sync goes on until the offset is known to this either way: the arbiter is often in its own teleport's long frames when the
		/// others ping it, and every answer read at the end of one is as wide as the frame, so it pings on until its frames are short.
		/// </summary>
		internal const double SyncGood = 0.1;

		/// <summary>Seconds between a sync's pings, so one long frame (on either machine) does not catch them all.</summary>
		internal const double PingEvery = 0.2;

		/// <summary>A sync goes on this long at most, then takes the middle of what it has (or the offset it had, with no answer).</summary>
		internal const double SyncMost = 5.0;

		/// <summary>
		/// When the arbiter races too it was put on the grid at the same moment, and pinged in its teleport's long frames every answer
		/// is as wide as the frame: so a sync waits for the arbiter to be ready (it is once it has settled) before it pings, this long
		/// at most.
		/// </summary>
		internal const double ArbiterWaitMost = 6.0;

		/// <summary>
		/// From the Start, the arbiter waits this long at most for every entrant to be on the grid and synced, then deals green anyway:
		/// a machine whose message was lost, or that is still building its tiles, holds nobody for longer.
		/// </summary>
		internal const double GridMost = 15.0;

		/// <summary>From every entrant ready to green: the countdown's three lights, and time for the lobby to reach every machine.</summary>
		internal const double CountLead = 4.0;

		/// <summary>A machine with no snapshot yet asks again this often.</summary>
		internal const double HelloEvery = 2.0;

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
			/// <summary>This player's world is built: send them every lobby (one that loads after the host's missed them).</summary>
			Hello = 7,
			/// <summary>On the grid: the arbiter's clock, please (this machine's clock when it asked goes with it, and comes back).</summary>
			Ping = 8,
			/// <summary>On the grid and synced: green may be dealt.</summary>
			Ready = 9,
			// From the arbiter.
			Lobby = 20,
			Gone = 21,
			Refused = 22,
			/// <summary>The end of a snapshot, with every lobby's id: a lobby not in it is gone.</summary>
			SnapshotEnd = 23,
			/// <summary>The answer to a <see cref="Ping"/>: the asker's clock it carried, and when on the arbiter's it could have come (from, to).</summary>
			Pong = 24,
		}

		internal enum LobbyState : byte
		{
			/// <summary>Entrants gather; the Host may start when the course is free.</summary>
			Waiting = 0,
			/// <summary>The race: the grid (until every entrant is ready and green is dealt), the countdown until <see cref="Lobby.StartAt"/>, then racing.</summary>
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
			/// <summary>An entrant is on the grid and synced.</summary>
			Ready = 10,
			/// <summary>Green is dealt: every entrant is ready (or <see cref="GridMost"/> ran out), the countdown runs.</summary>
			Counting = 11,
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
			/// <summary>On this machine: the arbiter has not been heard yet, so its lobbies are not here to choose from.</summary>
			Linking = 8,
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
			/// <summary>On the grid and synced in this race (<see cref="Msg.Ready"/>).</summary>
			public bool Ready;
		}

		/// <summary>A lobby's green and time limit before they are dealt.</summary>
		internal const double NotDealt = double.NegativeInfinity;

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
			/// <summary>Green, on the race clock; <see cref="NotDealt"/> on the grid until every entrant is ready (<see cref="Why.Counting"/>).</summary>
			public double StartAt = NotDealt;
			/// <summary>The time limit, on the race clock; <see cref="NotDealt"/> until green is dealt.</summary>
			public double EndsAt = NotDealt;
			/// <summary>True once green is dealt (<see cref="StartAt"/> and <see cref="EndsAt"/> are times).</summary>
			public bool Dealt => StartAt > NotDealt;
			/// <summary>The cut-off after the first finisher, on the race clock (negative before anybody has finished).</summary>
			public double CutoffAt = -1.0;
			public double ResultsUntil;
			/// <summary>The arbiter's alone (not sent): green is dealt by then whoever is ready.</summary>
			public double GridUntil;
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
			/// <summary>The arbiter's player id (this player's alone).</summary>
			ulong ArbiterId { get; }
			void ToArbiter(byte[] data);
			/// <summary>To every other player (the arbiter has already applied it to its own copy).</summary>
			void ToAll(byte[] data);
			void ToOne(ulong to, byte[] data);
		}

		private readonly ILink link;
		private readonly CourseInfo[] courses;
		private readonly SortedDictionary<int, Lobby> lobbies = new SortedDictionary<int, Lobby>();
		private int nextId;

		/// <summary>The arbiter's clock minus this machine's (0 on the arbiter), from the last sync.</summary>
		private double offset;
		private bool linked;
		private bool snapshot;
		private double helloAt = double.NegativeInfinity;

		// A sync on the grid (this machine, not the arbiter): the race it is for, its pings, and the bounds on the offset so far.
		private bool syncing;
		private int syncRace;
		private double syncFrom;
		/// <summary>When the pings began (after waiting for the arbiter); negative before.</summary>
		private double pingFrom;
		private double pingAt;
		private int pings;
		private int pongs;
		private double low;
		private double high;

		/// <summary>The arbiter's clock at its last two ticks: a message it reads now came after the one before last (see <see cref="Pong"/>).</summary>
		private double tickAt = double.NegativeInfinity;
		private double tickBefore = double.NegativeInfinity;

		/// <summary>A lobby was sent (created, changed, or in a snapshot): why, and who it was about (0 for nobody).</summary>
		internal event Action<Lobby, Why, ulong> Changed;
		/// <summary>A lobby was taken away (its last entrant left).</summary>
		internal event Action<int> Removed;
		/// <summary>This player's request was turned down.</summary>
		internal event Action<Refusal> Refused;
		/// <summary>What the room did with the network, for the log (snapshots, Hellos, a lobby taken away by a snapshot).</summary>
		internal event Action<string> Note;

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
			offset = 0.0;
			linked = false;
			snapshot = false;
			helloAt = double.NegativeInfinity;
			syncing = false;
		}

		// ---- the clock -----------------------------------------------------------------------------------------------

		/// <summary>The arbiter's clock now, from this machine's: what every lobby's times are on.</summary>
		internal double Now(double clock)
		{
			return link.Arbiter ? clock : clock + offset;
		}

		/// <summary>The arbiter's clock minus this machine's, as the last sync found it (0 on the arbiter, and before any sync).</summary>
		internal double Offset => link.Arbiter ? 0.0 : offset;

		/// <summary>True once the arbiter has been heard (always on the arbiter): its lobbies are here, so requests can be made.</summary>
		internal bool Linked => link.Arbiter || linked;

		/// <summary>True while this machine is syncing its clock on the grid.</summary>
		internal bool Syncing => syncing;

		/// <summary>True once a whole snapshot has come (always on the arbiter): this machine holds every lobby.</summary>
		internal bool Synced => link.Arbiter || snapshot;

		/// <summary>
		/// This car is on its grid slot and the teleport's long frames are over: its machine takes the arbiter's clock now (pings, the
		/// shortest round trip's offset), then tells the arbiter it is ready (<see cref="Tick"/> does both). On the arbiter (and alone)
		/// there is nothing to take: ready at once. Called by the game once a race, after the car is put on the grid.
		/// </summary>
		internal void Sync(double clock)
		{
			Lobby l = Mine;
			if (l == null || l.State != LobbyState.Racing)
			{
				return;
			}
			if (link.Arbiter)
			{
				Request(new Writer(Msg.Ready).I32(l.Race).Done(), clock);
				return;
			}
			syncing = true;
			syncRace = l.Race;
			syncFrom = clock;
			pingFrom = -1.0;
			pingAt = double.NegativeInfinity;
			pings = 0;
			pongs = 0;
			low = double.NegativeInfinity;
			high = double.PositiveInfinity;
			Note?.Invoke("syncing the clock with the arbiter on the grid");
		}

		/// <summary>
		/// Each frame while syncing: the next ping when due; once enough answers bound the offset to <see cref="SyncGood"/> (or
		/// <see cref="SyncMost"/> has run out), the offset and Ready.
		/// </summary>
		private void Syncs(double clock)
		{
			if (pingFrom < 0.0)
			{
				// The arbiter in this race and not settled yet: its answers would be as wide as its teleport's frames.
				Lobby l = Mine;
				Entrant arbiter = l != null && l.Race == syncRace ? l.In(link.ArbiterId) : null;
				bool waiting = arbiter != null && arbiter.Run == Run.Running && !arbiter.Ready && !l.Dealt;
				if (waiting && clock - syncFrom < ArbiterWaitMost)
				{
					return;
				}
				pingFrom = clock;
				Note?.Invoke(waiting ? "the arbiter has not settled on the grid in " + ArbiterWaitMost + " s: pinging it anyway"
					: arbiter != null ? "the arbiter has settled on the grid (" + (clock - syncFrom).ToString("0.00") + " s): pinging it" : "pinging the arbiter");
			}
			bool good = pongs >= SyncPings && high - low <= 2.0 * SyncGood;
			if (!good && clock - pingFrom < SyncMost)
			{
				if (clock - pingAt >= PingEvery)
				{
					pingAt = clock;
					pings++;
					link.ToArbiter(new Writer(Msg.Ping).F64(clock).Done());
				}
				return;
			}
			syncing = false;
			if (pongs > 0 && low <= high)
			{
				offset = (low + high) * 0.5;
				Note?.Invoke("synced with the arbiter: its clock " + offset.ToString("+0.000;-0.000") + " s from this machine's, give or take " + ((high - low) * 500.0).ToString("0") + " ms (" + pongs + " answers to " + pings + " pings, " + (clock - pingFrom).ToString("0.00") + " s)");
			}
			else
			{
				Note?.Invoke("no answer from the arbiter to the sync: its clock taken as " + offset.ToString("+0.000;-0.000") + " s from this machine's");
			}
			link.ToArbiter(new Writer(Msg.Ready).I32(syncRace).Done());
		}

		/// <summary>
		/// The arbiter answered a ping sent at <paramref name="sent"/> (this machine's clock), here at <paramref name="clock"/>: it got
		/// the ping somewhere from <paramref name="from"/> to <paramref name="to"/> on its clock (its frame before last to when it read
		/// it). That moment on this machine's clock was after the ping went and before the answer came, so the arbiter's clock minus this
		/// one's is at least <c>from - clock</c> and at most <c>to - sent</c>. The bounds of every answer together hold the offset.
		/// </summary>
		private void Pong(double sent, double from, double to, double clock)
		{
			if (!syncing || sent < syncFrom || clock < sent || double.IsNaN(sent) || double.IsNaN(from) || double.IsNaN(to) || double.IsInfinity(from) || double.IsInfinity(to) || from > to)
			{
				return;
			}
			pongs++;
			low = Math.Max(low, from - clock);
			high = Math.Min(high, to - sent);
		}

		// ---- requests ------------------------------------------------------------------------------------------------

		/// <summary>Creates a lobby at the course (leaving any other), its Host this player, with the weapons they allow.</summary>
		internal void Create(int course, bool guns, bool melee, double clock)
		{
			Request(new Writer(Msg.Create).U8(course).U8((guns ? 1 : 0) | (melee ? 2 : 0)).Done(), clock);
		}

		/// <summary>Joins a lobby (leaving any other).</summary>
		internal void Join(int lobby, double clock)
		{
			Request(new Writer(Msg.Join).I32(lobby).Done(), clock);
		}

		/// <summary>Leaves this player's lobby: at once while it waits; in its race, a DNF.</summary>
		internal void Leave(double clock)
		{
			Request(new Writer(Msg.Leave).Done(), clock);
		}

		/// <summary>The Host starts the lobby's race, when the course is free.</summary>
		internal void Start(double clock)
		{
			Request(new Writer(Msg.Start).Done(), clock);
		}

		/// <summary>
		/// The local player passed gate <paramref name="gate"/> (0 on; the last is the finish) of the race, at <paramref name="time"/>
		/// on their own clock from green, having missed <paramref name="missed"/> gates so far.
		/// </summary>
		internal void Passed(int race, int gate, float time, int missed, double clock)
		{
			Request(new Writer(Msg.Gate).I32(race).U16(gate).F32(time).U16(missed).Done(), clock);
		}

		/// <summary>The local player gave the race up (off the course too long, or a teleport): a DNF, still in the lobby.</summary>
		internal void Retire(int race, double clock)
		{
			Request(new Writer(Msg.Retire).I32(race).Done(), clock);
		}

		/// <summary>This machine's world is built (or the room changed): the arbiter sends every lobby. Repeated by <see cref="Tick"/> until it has.</summary>
		internal void Hello(double clock)
		{
			helloAt = clock;
			if (!link.Arbiter)
			{
				Note?.Invoke("asked the arbiter for every lobby");
			}
			Request(new Writer(Msg.Hello).Done(), clock);
		}

		/// <summary>
		/// To the arbiter (the arbiter's own, at once). A request other than Hello before the arbiter has been heard is turned down here:
		/// its lobbies are not here to choose from, and a link that has never answered would only lose it.
		/// </summary>
		private void Request(byte[] data, double clock)
		{
			if (link.Arbiter)
			{
				Handle(link.Local, data, clock);
			}
			else if (!linked && data[1] != (byte)Msg.Hello)
			{
				Refused?.Invoke(Refusal.Linking);
			}
			else
			{
				link.ToArbiter(data);
			}
		}

		// ---- the room ------------------------------------------------------------------------------------------------

		/// <summary>A message from another player (or the arbiter), come at <paramref name="clock"/> on this machine's clock.</summary>
		internal void Receive(ulong from, byte[] data, double clock)
		{
			if (data == null || data.Length < 2 || data[0] != Version)
			{
				return;
			}
			if (data[1] < (byte)Msg.Lobby)
			{
				if (link.Arbiter)
				{
					Handle(from, data, clock);
				}
				return;
			}
			if (link.Arbiter)
			{
				// Lobbies come only from the arbiter: this is one (the room's host changing, an old host's message still on its way).
				return;
			}
			try
			{
				// Read whole before anything is taken from it, so a message that does not read changes nothing.
				Reader r = new Reader(data);
				switch (r.Type)
				{
					case Msg.Lobby:
						Why why = (Why)r.U8();
						ulong who = r.U64();
						Lobby l = ReadLobby(r);
						linked = true;
						lobbies[l.Id] = l;
						Changed?.Invoke(l, why, who);
						break;
					case Msg.Gone:
						int id = r.I32();
						linked = true;
						if (lobbies.Remove(id))
						{
							Removed?.Invoke(id);
						}
						break;
					case Msg.Refused:
						Refusal refusal = (Refusal)r.U8();
						linked = true;
						Refused?.Invoke(refusal);
						break;
					case Msg.SnapshotEnd:
						int n = r.U16();
						HashSet<int> ids = new HashSet<int>();
						for (int i = 0; i < n; i++)
						{
							ids.Add(r.I32());
						}
						if (!linked)
						{
							Note?.Invoke("linked to the arbiter");
						}
						linked = true;
						foreach (int gone in new List<int>(lobbies.Keys))
						{
							if (!ids.Contains(gone))
							{
								lobbies.Remove(gone);
								Note?.Invoke("lobby " + gone + " is no longer the arbiter's: taken away");
								Removed?.Invoke(gone);
							}
						}
						if (!snapshot)
						{
							Note?.Invoke("every lobby is here (" + n + ")");
						}
						snapshot = true;
						break;
					case Msg.Pong:
						double sent = r.F64(), gotFrom = r.F64(), gotBy = r.F64();
						linked = true;
						Pong(sent, gotFrom, gotBy, clock);
						break;
				}
			}
			catch (Exception)
			{
				// A message that does not read is dropped (see the class's summary).
			}
		}

		/// <summary>The arbiter: a player joined the room, so they are sent every lobby.</summary>
		internal void PlayerJoined(ulong id, double clock)
		{
			if (!link.Arbiter || id == link.Local)
			{
				return;
			}
			Snapshot(id);
		}

		/// <summary>Every lobby to one player, then the snapshot's end with every lobby's id.</summary>
		private void Snapshot(ulong id)
		{
			Writer end = new Writer(Msg.SnapshotEnd).U16(lobbies.Count);
			foreach (Lobby l in lobbies.Values)
			{
				link.ToOne(id, LobbyMessage(l, Why.Snapshot, 0UL));
				end.I32(l.Id);
			}
			link.ToOne(id, end.Done());
			Note?.Invoke("sent every lobby (" + lobbies.Count + ") to player " + id);
		}

		/// <summary>The arbiter: a player left the room, so they leave every lobby (a DNF in a race).</summary>
		internal void PlayerLeft(ulong id, double clock)
		{
			if (!link.Arbiter)
			{
				return;
			}
			Lobby l = LobbyOf(id);
			if (l != null)
			{
				Leaving(l, id, clock);
			}
		}

		/// <summary>
		/// Each frame, on this machine's clock. The arbiter: green once the grid is ready, the cut-off and the time limit, the results'
		/// end. Any other machine: Hello again until a snapshot has come, and a sync on the grid.
		/// </summary>
		internal void Tick(double clock)
		{
			if (!link.Arbiter)
			{
				if (!snapshot && clock - helloAt >= HelloEvery)
				{
					Hello(clock);
				}
				if (syncing)
				{
					Syncs(clock);
				}
				return;
			}
			double now = clock;
			tickBefore = tickAt;
			tickAt = now;
			foreach (Lobby l in new List<Lobby>(lobbies.Values))
			{
				if (l.State == LobbyState.Racing)
				{
					Deal(l, now);
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
					case Msg.Hello:
						if (from != link.Local)
						{
							Snapshot(from);
						}
						break;
					case Msg.Ping:
						// Answered at once with when the ping could have come: after the tick before last (a message read now came after the
						// last frame's read, which was after that tick, however long the frames were) and by now. Not before two ticks:
						// there is no bound yet, and the asker pings again.
						if (from != link.Local && tickBefore > double.NegativeInfinity && tickBefore <= now)
						{
							link.ToOne(from, new Writer(Msg.Pong).F64(r.F64()).F64(tickBefore).F64(now).Done());
						}
						break;
					case Msg.Ready:
						Ready(from, r.I32(), now);
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

		/// <summary>
		/// Takes a player out of a lobby: gone at once while it waits; in its race or results, kept (a DNF if running) for the others'
		/// results. The Host passes to the next still in. A lobby nobody is left in closes at once, race or not, so it never holds the
		/// course for results nobody is there to see.
		/// </summary>
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
			if (l.Count == 0)
			{
				Remove(l);
				return;
			}
			Send(l, Why.Left, id);
			if (l.State == LobbyState.Racing)
			{
				Deal(l, now);
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
			// No green yet: it is dealt when every entrant is on the grid and synced (Deal).
			l.StartAt = NotDealt;
			l.EndsAt = NotDealt;
			l.GridUntil = now + GridMost;
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
				e.Ready = false;
			}
			Send(l, Why.Started, from);
		}

		/// <summary>An entrant is on the grid and synced: once every one is, green is dealt.</summary>
		private void Ready(ulong from, int race, double now)
		{
			Lobby l = LobbyOf(from);
			Entrant e = l != null ? l.In(from) : null;
			if (e == null || l.State != LobbyState.Racing || l.Race != race || e.Run != Run.Running || e.Ready || l.Dealt)
			{
				return;
			}
			e.Ready = true;
			Send(l, Why.Ready, from);
			Deal(l, now);
		}

		/// <summary>
		/// Green, when the grid is ready: every entrant still running is synced, or <see cref="GridMost"/> has run out since the Start.
		/// <see cref="CountLead"/> on, so the lobby reaches every machine before its countdown's first light.
		/// </summary>
		private void Deal(Lobby l, double now)
		{
			if (l.State != LobbyState.Racing || l.Dealt)
			{
				return;
			}
			bool ready = true;
			foreach (Entrant e in l.Entrants)
			{
				ready &= e.Out || e.Run != Run.Running || e.Ready;
			}
			if (!ready && now < l.GridUntil)
			{
				return;
			}
			l.StartAt = now + CountLead;
			l.EndsAt = l.StartAt + courses[l.Course].TimeLimit;
			Send(l, Why.Counting, 0UL);
		}

		private void Gate(ulong from, int race, int gate, float time, int missed, double now)
		{
			Lobby l = LobbyOf(from);
			Entrant e = l != null ? l.In(from) : null;
			int gates = l != null ? courses[l.Course].Gates : 0;
			if (e == null || l.State != LobbyState.Racing || !l.Dealt || l.Race != race || e.Run != Run.Running || gate < e.Passed || gate >= gates || float.IsNaN(time) || time < 0f)
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
			if (running && !(l.CutoffAt >= 0.0 && now >= l.CutoffAt) && (!l.Dealt || now < l.EndsAt))
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
			return e != null && l.State == LobbyState.Racing && l.Dealt && now >= l.StartAt && e.Run == Run.Running;
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
		/// Whether this player may use a weapon of this kind now (green read on the clock this machine synced on its last grid: a machine
		/// that has never been on one reads it on its own): anybody outside a lobby may; in a lobby, only while running in its
		/// race past green and only what its Host allowed (a player waiting in a lobby can hurt nobody, so firing is no use).
		/// </summary>
		internal bool MayUse(ulong id, HitKind kind, double clock)
		{
			Lobby l = LobbyOf(id);
			return l == null || (Racing(l, id, Now(clock)) && Allows(l, kind));
		}

		/// <summary>
		/// Whether a hit from one player on another counts. Between two players in no lobby, always (roaming as before). A player in a
		/// lobby or its race and anybody outside it never hurt each other, crashes included. Two entrants of the same race hurt each
		/// other only while both are running in it past green, by what its Host allowed; crashes always count there.
		/// </summary>
		internal bool MayHit(ulong attacker, ulong victim, HitKind kind, double clock)
		{
			Lobby a = LobbyOf(attacker), v = LobbyOf(victim);
			if (a == null && v == null)
			{
				return true;
			}
			double now = Now(clock);
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
				w.U64(e.Id).U8((int)e.Run).U8(e.Slot).U16(e.Passed).F32(e.Time).U16(e.Missed).U8((e.Out ? 1 : 0) | (e.Ready ? 2 : 0));
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
				Entrant e = new Entrant { Id = r.U64(), Run = (Run)r.U8(), Slot = r.U8(), Passed = r.U16(), Time = r.F32(), Missed = r.U16() };
				int flags = r.U8();
				e.Out = (flags & 1) != 0;
				e.Ready = (flags & 2) != 0;
				l.Entrants.Add(e);
			}
			return l;
		}

		/// <summary>A lobby as its message, for comparing copies (eventsim): two lobbies are the same if their messages are.</summary>
		internal static string Fingerprint(Lobby l)
		{
			return Convert.ToBase64String(LobbyMessage(l, Why.Snapshot, 0UL));
		}

		/// <summary>A message in a few words, for the log: its kind, and the lobby it is about if any.</summary>
		internal static string Describe(byte[] data)
		{
			if (data == null || data.Length < 2)
			{
				return "an empty message";
			}
			if (data[0] != Version)
			{
				return "a message of version " + data[0];
			}
			try
			{
				Reader r = new Reader(data);
				switch (r.Type)
				{
					case Msg.Lobby:
						Why why = (Why)r.U8();
						r.U64();
						Lobby l = ReadLobby(r);
						return "lobby " + l.Id + " (" + why + ", " + l.State + ", " + l.Count + " in)";
					case Msg.Gone:
						return "lobby " + r.I32() + " gone";
					case Msg.Refused:
						return "refused (" + (Refusal)r.U8() + ")";
					case Msg.SnapshotEnd:
						return "snapshot end (" + r.U16() + " lobbies)";
					case Msg.Join:
						return "join " + r.I32();
					case Msg.Ready:
						return "ready in race " + r.I32();
					default:
						return r.Type.ToString();
				}
			}
			catch (Exception)
			{
				return "a message that does not read";
			}
		}

		/// <summary>True for a sync's ping or pong, which come in bursts and are summed up in one line when the sync is done.</summary>
		internal static bool IsSync(byte[] data)
		{
			return data != null && data.Length >= 2 && (data[1] == (byte)Msg.Ping || data[1] == (byte)Msg.Pong);
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
