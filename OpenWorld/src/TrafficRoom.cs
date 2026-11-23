using System;
using System.Collections.Generic;
using System.IO;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Stage 4 of the traffic plan: the traffic in a room. Every machine has the same day (from the room's seed); what differs is which
	/// cars are physics cars and where those are, so the room agrees on three things, the room's host (which never changes: the room ends
	/// when it leaves) settling each:
	///
	/// - **Who drives each physics car.** Only a machine with the ground loaded under a car can drive it, and only its player's
	///   surroundings are loaded, so the player nearest a car drives it. A machine asks the host for a car (<see cref="Claim"/>) when its
	///   player is the nearest to it; the host grants the first claim on a car nobody drives (the car's <see cref="Car.Gen"/> must match,
	///   so a claim made before a change is refused) and tells everybody. The driver gives the car back to its timetable with a ledger
	///   entry (<see cref="Release"/>: late by D, or retired), or hands it to a player now clearly nearer (<see cref="Hand"/>); the host
	///   settles both and tells everybody. A machine never drives a car before the host's grant reaches it (alone, the host is this
	///   machine and grants at once), so two machines never drive one car. A player who leaves gives back every car they drove.
	/// - **The lateness ledger** (<see cref="TrafficLate"/>): its entries are the room's (a release's entry, a car held back in its space,
	///   a late car caught up in the background). A machine applies its own at once and sends it; the host applies it and sends it to
	///   everybody (the sender's own put right, if it differs). Catching up in the background is the host's alone, from where every
	///   player is (a car nobody can see). Hiding a drawn car is only what one machine draws, so it is not sent.
	/// - **The clock.** The day runs on the race's clock, which each machine starts as its race starts and which long frames hold back, so
	///   machines drift apart; each takes the host's (<see cref="ClockOf"/>), learnt from the host's states.
	///
	/// Each driver sends its cars' states about 15 times a second, unreliably (<see cref="WriteStates"/>), with its player's place and its
	/// clocks; the others show a stand-in played back a little behind them (<see cref="Playback"/>). Hits on a stand-in go to its driver
	/// (<see cref="Damage"/>). Reliable messages come in order from each sender (Steam's), so the host's come in the order it settled them.
	///
	/// No Unity types: <c>tools/trafficsim</c> drives it on pretend machines over a pretend network.
	/// </summary>
	internal sealed class TrafficRoom
	{
		/// <summary>The first byte of every traffic message (never an <see cref="EventRoom.Version"/> or <see cref="DeerRoom.Tag"/>).</summary>
		internal const byte Tag = 0x7A;

		/// <summary>The messages' layout. Raise it with any change to what they carry.</summary>
		internal const byte Version = 2;

		/// <summary>A claim not answered in this long (seconds) is given up (the host refused it by saying nothing: another drives the car).</summary>
		internal const double ClaimWait = 2.0;

		/// <summary>A player's place not heard for this long (seconds) is not counted (a machine stalled, or gone).</summary>
		internal const double PlayerStale = 3.0;

		/// <summary>The most bytes in one unreliable message (HalfGripTools' <c>Net.MaxUnreliable</c> is 1100) and one reliable (3800).</summary>
		internal const int MostUnreliable = 1100, MostReliable = 3800;

		private enum Msg : byte
		{
			Claim = 1,
			Grant = 2,
			Release = 3,
			Released = 4,
			Hand = 5,
			Damage = 6,
			Ledger = 7,
			LedgerSet = 8,
			Hello = 9,
			Snapshot = 10,
			States = 11,
		}

		/// <summary>What happens to a car's ledger entry.</summary>
		internal enum OpKind : byte
		{
			/// <summary>Nothing (a release that leaves the car as it is).</summary>
			None = 0,
			/// <summary>Late by D seconds on leg Leg from sim time W (negative: a little early). Also a car held back in its space.</summary>
			Late = 1,
			/// <summary>Gone for the rest of the race (wrecked, or knocked off its road). D is <see cref="Wrecked"/> for a wreck (its stand-ins burn where they are).</summary>
			Retire = 2,
			/// <summary>Back on the day (caught up in the background, or late by too little to keep).</summary>
			OnTime = 3,
		}

		/// <summary>A retirement's D for a wreck: the stand-ins elsewhere burn and explode where they are rather than go.</summary>
		internal const double Wrecked = 1.0;

		/// <summary>One change to a car's ledger entry.</summary>
		internal struct Op
		{
			public OpKind Kind;
			public int Leg;
			public double W, D;

			internal bool Same(Op o) => Kind == o.Kind && Leg == o.Leg && W == o.W && D == o.D;

			public override string ToString()
			{
				switch (Kind)
				{
					case OpKind.Late:
						return (D >= 0.0 ? D.ToString("0.0") + " s late" : (-D).ToString("0.0") + " s early") + " on leg " + Leg;
					case OpKind.Retire:
						return D == Wrecked ? "wrecked" : "retired";
					case OpKind.OnTime:
						return "on time";
					default:
						return "as it was";
				}
			}
		}

		/// <summary>What the room knows of a car: who drives it (0: nobody, it is on its timetable), how many times that has changed, its ledger entry.</summary>
		internal struct Car
		{
			public ulong Driver;
			public int Gen;
			public Op Op;
		}

		/// <summary>A physics car's state as its driver sends it: world coordinates, the driver's real clock.</summary>
		internal struct State
		{
			public int Car, Gen, Leg;
			public float X, Y, Z, Qx, Qy, Qz, Qw, Vx, Vy, Vz, P, Health;
			public byte Flags;
			/// <summary>The driver's real clock when it was sent (for the playback; each machine's own clock, not shared).</summary>
			public double Stamp;

			internal bool Alive => (Flags & 1) != 0;
			internal bool Hurt => (Flags & 2) != 0;
			/// <summary>Stopped by its driver (held, or given up): a wall, not a mover.</summary>
			internal bool Stopped => (Flags & 4) != 0;
			/// <summary>A loose car (contact physics: no driver, on its brakes where it was drawn), and whether it has been knocked since.</summary>
			internal bool Loose => (Flags & 8) != 0;
			internal bool Touched => (Flags & 16) != 0;
		}

		/// <summary>The state's flags. Loose and touched came with layout <see cref="Version"/> 2.</summary>
		internal const byte FlagAlive = 1, FlagHurt = 2, FlagStopped = 4, FlagLoose = 8, FlagTouched = 16;

		/// <summary>How the room's messages go (the game's network, or a pretend one).</summary>
		internal interface ILink
		{
			/// <summary>This machine settles (the room's host, or alone).</summary>
			bool IsHost { get; }

			/// <summary>This machine's player id (alone: anything but 0).</summary>
			ulong Local { get; }

			void ToHost(byte[] data);

			void ToAll(byte[] data, bool reliable);

			void ToOne(ulong to, byte[] data);
		}

		internal readonly int Seed;
		private readonly ILink link;
		private readonly Dictionary<int, Car> cars = new Dictionary<int, Car>();
		/// <summary>This machine's claims waiting for the host: car, and the real time it was made.</summary>
		private readonly Dictionary<int, double> claims = new Dictionary<int, double>();
		/// <summary>Where each player is (world x, z), as last heard, and when (this machine's real clock).</summary>
		private readonly Dictionary<ulong, (double x, double z, double at)> players = new Dictionary<ulong, (double, double, double)>();
		/// <summary>The host's clock: the best (least delayed) recent reading of host sim time minus this machine's, and when it was taken.</summary>
		private readonly List<(double offset, double at)> clockSamples = new List<(double, double)>();
		private double offset, offsetTarget;
		private bool offsetKnown;
		private readonly List<int> scratch = new List<int>();

		/// <summary>A car's driver changed (car, the new driver, the old). The new driver makes it a physics car; others a stand-in.</summary>
		internal Action<int, ulong, ulong> DriverChanged;

		/// <summary>A car's ledger entry changed: apply it (car, entry).</summary>
		internal Action<int, Op> LedgerChanged;

		/// <summary>A hit on a car this machine drives, from a stand-in on another machine (car, amount).</summary>
		internal Action<int, float> Damaged;

		/// <summary>A state of a car another machine drives (checked: from its driver, of its present drive).</summary>
		internal Action<State> StateCame;

		/// <summary>On the host, a player left: the entry each of their cars goes back to its timetable with, from its last state (car).</summary>
		internal Func<int, Op> LastOp;

		/// <summary>Counts for the log and the overlay: claims made, granted to this machine, refused (given up), hand-overs in and out.</summary>
		internal int Claimed, Granted, GaveUp, HandedIn, HandedOut, StatesIn, StatesStale;

		internal TrafficRoom(int seed, ILink link)
		{
			Seed = seed;
			this.link = link;
		}

		internal bool IsHost => link.IsHost;

		internal ulong Local => link.Local;

		/// <summary>Who drives car <paramref name="car"/> (0: nobody).</summary>
		internal ulong DriverOf(int car) => cars.TryGetValue(car, out Car c) ? c.Driver : 0UL;

		internal bool Drives(int car) => DriverOf(car) == link.Local;

		/// <summary>Driven by another machine (shown here as a stand-in).</summary>
		internal bool Remote(int car)
		{
			ulong d = DriverOf(car);
			return d != 0UL && d != link.Local;
		}

		internal int GenOf(int car) => cars.TryGetValue(car, out Car c) ? c.Gen : 0;

		internal bool Claiming(int car) => claims.ContainsKey(car);

		/// <summary>This machine handed car <paramref name="car"/> on and waits to hear it settled (its stand-in carries on meanwhile).</summary>
		internal bool Handing(int car) => handing.Contains(car);

		internal int Claims => claims.Count;

		/// <summary>The cars every machine knows a driver for, with them (for the sim).</summary>
		internal IEnumerable<KeyValuePair<int, Car>> Cars => cars;

		// ---- the players --------------------------------------------------------------------------------------------------

		/// <summary>This machine's player is at (x, z) (world), at real time <paramref name="now"/>.</summary>
		internal void Me(double x, double z, double now)
		{
			players[link.Local] = (x, z, now);
		}

		/// <summary>A player left the room. On the host, every car they drove goes back on its timetable (from its last state, if known).</summary>
		internal void PlayerLeft(ulong id)
		{
			players.Remove(id);
			gone.Add(id);
			if (!link.IsHost)
			{
				return;
			}
			scratch.Clear();
			foreach (KeyValuePair<int, Car> kv in cars)
			{
				if (kv.Value.Driver == id)
				{
					scratch.Add(kv.Key);
				}
			}
			scratch.Sort();
			foreach (int car in scratch)
			{
				Op op = LastOp != null ? LastOp(car) : default;
				Settle(car, cars[car].Gen, 0UL, op, true);
			}
		}

		/// <summary>A player joined (or came back): their claims are heard again.</summary>
		internal void PlayerJoined(ulong id)
		{
			gone.Remove(id);
		}

		/// <summary>Players who left: what still comes from them (sent before they went) is not settled.</summary>
		private readonly HashSet<ulong> gone = new HashSet<ulong>();

		/// <summary>
		/// Whether this machine's player is the nearest to (x, z) of the players heard from lately (the lower id at the same distance), so
		/// the machine that should drive a car is the one that claims it. Each machine knows the others' places a little late, so two may
		/// both think so for a moment: the host takes the first claim.
		/// </summary>
		internal bool Nearest(double x, double z, double now)
		{
			if (!players.TryGetValue(link.Local, out var me))
			{
				return false;
			}
			double mine = Dist(me.x, me.z, x, z);
			foreach (KeyValuePair<ulong, (double x, double z, double at)> kv in players)
			{
				if (kv.Key == link.Local || now - kv.Value.at > PlayerStale)
				{
					continue;
				}
				double d = Dist(kv.Value.x, kv.Value.z, x, z);
				if (d < mine || (d == mine && kv.Key < link.Local))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// For a car this machine drives at (x, z): the player to hand it to (0: keep it). Another player within <paramref name="reach"/> of it
		/// when this one is past it, or one under half as far when this one is more than <paramref name="close"/> away: loose, so a car is not
		/// handed back and forth (hits don't need the hitter to drive).
		/// </summary>
		internal ulong HandTo(double x, double z, double now, double reach, double close)
		{
			if (!players.TryGetValue(link.Local, out var me))
			{
				return 0UL;
			}
			double mine = Dist(me.x, me.z, x, z);
			ulong best = 0UL;
			double bestD = double.MaxValue;
			foreach (KeyValuePair<ulong, (double x, double z, double at)> kv in players)
			{
				if (kv.Key == link.Local || now - kv.Value.at > PlayerStale)
				{
					continue;
				}
				double d = Dist(kv.Value.x, kv.Value.z, x, z);
				if (d < bestD || (d == bestD && kv.Key < best))
				{
					bestD = d;
					best = kv.Key;
				}
			}
			if (best == 0UL)
			{
				return 0UL;
			}
			if ((mine > reach && bestD < reach) || (mine > close && bestD < mine * 0.5))
			{
				return best;
			}
			return 0UL;
		}

		/// <summary>Whether a place is within <paramref name="reach"/> of any player heard from lately but this machine's (the host's catching up asks).</summary>
		internal bool OthersNear(double x, double z, double reach, double now)
		{
			foreach (KeyValuePair<ulong, (double x, double z, double at)> kv in players)
			{
				if (kv.Key != link.Local && now - kv.Value.at <= PlayerStale && Dist(kv.Value.x, kv.Value.z, x, z) <= reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>How many players' places are known (this machine's among them).</summary>
		internal int PlayersKnown => players.Count;

		private static double Dist(double ax, double az, double bx, double bz)
		{
			double dx = ax - bx, dz = az - bz;
			return Math.Sqrt(dx * dx + dz * dz);
		}

		// ---- the clock ----------------------------------------------------------------------------------------------------

		/// <summary>
		/// The room's sim time for this machine's own <paramref name="local"/> (the host's clock: the host's own, everyone else's moved by
		/// what they learnt from the host's states).
		/// </summary>
		internal double ClockOf(double local) => link.IsHost ? local : local + offset;

		/// <summary>What this machine adds to its own sim time to have the host's (0 on the host).</summary>
		internal double Offset => link.IsHost ? 0.0 : offset;

		internal bool ClockKnown => link.IsHost || offsetKnown;

		/// <summary>The host's sim time <paramref name="hostW"/> as it sent it, read when this machine's was <paramref name="localW"/>, at real time <paramref name="now"/>.</summary>
		private void ClockSample(double hostW, double localW, double now)
		{
			// A sample is behind by however long it took to come: the largest of the last few seconds' is the least delayed.
			clockSamples.Add((hostW - localW, now));
			clockSamples.RemoveAll(s => now - s.at > 8.0);
			double best = double.MinValue;
			foreach ((double o, double _) in clockSamples)
			{
				best = Math.Max(best, o);
			}
			offsetTarget = best;
			if (!offsetKnown || Math.Abs(offsetTarget - offset) > 5.0)
			{
				// The first reading, or far off (a long stall): taken at once.
				offset = offsetTarget;
				offsetKnown = true;
			}
		}

		/// <summary>Each frame: the offset eased toward the latest reading (at most 0.2 s a second, so drawn cars never jump), and claims that waited too long given up.</summary>
		internal void Tick(double now, double dt)
		{
			if (offsetKnown)
			{
				double most = 0.2 * dt;
				offset += Math.Max(-most, Math.Min(most, offsetTarget - offset));
			}
			scratch.Clear();
			foreach (KeyValuePair<int, double> kv in claims)
			{
				if (now - kv.Value > ClaimWait)
				{
					scratch.Add(kv.Key);
				}
			}
			foreach (int car in scratch)
			{
				claims.Remove(car);
				GaveUp++;
			}
		}

		// ---- this machine's own -------------------------------------------------------------------------------------------

		/// <summary>
		/// Asks for car <paramref name="car"/> (nobody drives it). On the host it is settled at once (<see cref="DriverChanged"/> is called
		/// before this returns); elsewhere it waits for the host. False if it is driven, or this machine already waits on it.
		/// </summary>
		internal bool Claim(int car, double now)
		{
			if (DriverOf(car) != 0UL || claims.ContainsKey(car) || handing.Contains(car) || (cars.TryGetValue(car, out Car c) && c.Op.Kind == OpKind.Retire))
			{
				return false;
			}
			Claimed++;
			int gen = GenOf(car);
			if (link.IsHost)
			{
				Settle(car, gen, link.Local, default, false);
				return true;
			}
			claims[car] = now;
			link.ToHost(Write(Msg.Claim, car, gen, 0UL, default));
			return true;
		}

		/// <summary>
		/// This machine gives back car <paramref name="car"/>, which it drives, to its timetable with entry <paramref name="op"/>. Applied here
		/// at once (<see cref="LedgerChanged"/>, <see cref="DriverChanged"/>), then settled by the host for everybody.
		/// </summary>
		internal void Release(int car, Op op)
		{
			if (!Drives(car))
			{
				return;
			}
			int gen = GenOf(car);
			if (link.IsHost)
			{
				Settle(car, gen, 0UL, op, true);
				return;
			}
			// Provisional: no longer driven here, and its entry as this machine has it (the host's comes back the same).
			Car c = cars[car];
			c.Driver = 0UL;
			c.Gen = gen + 1;
			if (op.Kind != OpKind.None)
			{
				c.Op = op;
			}
			cars[car] = c;
			if (op.Kind != OpKind.None)
			{
				LedgerChanged?.Invoke(car, op);
			}
			DriverChanged?.Invoke(car, 0UL, link.Local);
			link.ToHost(Write(Msg.Release, car, gen, 0UL, op));
		}

		/// <summary>
		/// This machine hands car <paramref name="car"/>, which it drives, to player <paramref name="to"/> (nearer to it). Here it stops being
		/// driven at once (a stand-in until the host's grant to the other comes; <see cref="DriverChanged"/> with driver 0 until then).
		/// </summary>
		internal void Hand(int car, ulong to)
		{
			if (!Drives(car) || to == 0UL || to == link.Local)
			{
				return;
			}
			HandedOut++;
			int gen = GenOf(car);
			if (link.IsHost)
			{
				Settle(car, gen, to, default, false);
				return;
			}
			Car c = cars[car];
			c.Driver = 0UL;
			cars[car] = c;
			handing.Add(car);
			DriverChanged?.Invoke(car, 0UL, link.Local);
			// The gen stays: the host's grant to the other is gen + 1, and no claim can be made meanwhile (this machine knows the gen it sent).
			link.ToHost(Write(Msg.Hand, car, gen, to, default));
		}

		/// <summary>A hit on a stand-in here: sent to the car's driver (or taken here, if this machine drives it).</summary>
		internal void Damage(int car, float amount)
		{
			ulong d = DriverOf(car);
			if (d == 0UL)
			{
				return;
			}
			if (d == link.Local)
			{
				Damaged?.Invoke(car, amount);
				return;
			}
			using (MemoryStream m = new MemoryStream(24))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				Head(w, Msg.Damage);
				w.Write(car);
				w.Write(GenOf(car));
				w.Write(amount);
				link.ToOne(d, m.ToArray());
			}
		}

		/// <summary>
		/// A change to the ledger made here for a car nobody drives (one held back in its space; on the host, cars caught up): applied here at
		/// once, then settled by the host for everybody.
		/// </summary>
		internal void Ledger(List<(int car, Op op)> ops)
		{
			if (ops.Count == 0)
			{
				return;
			}
			if (link.IsHost)
			{
				LedgerFrom(link.Local, ops);
				return;
			}
			foreach ((int car, Op op) in ops)
			{
				if (DriverOf(car) == 0UL)
				{
					// Provisional (the host's comes back): applied here now so the car is drawn as it should be.
					LedgerChanged?.Invoke(car, op);
				}
			}
			for (int i = 0; i < ops.Count; i += 100)
			{
				link.ToHost(WriteOps(Msg.Ledger, ops, i, Math.Min(ops.Count, i + 100)));
			}
		}

		/// <summary>Asks the host for every car's driver and entry so far (a machine that is not the host, once its traffic is ready).</summary>
		internal void Hello()
		{
			if (!link.IsHost)
			{
				using (MemoryStream m = new MemoryStream(8))
				using (BinaryWriter w = new BinaryWriter(m))
				{
					Head(w, Msg.Hello);
					link.ToHost(m.ToArray());
				}
			}
		}

		// ---- the host's settling --------------------------------------------------------------------------------------------

		/// <summary>
		/// The host settles a change of car <paramref name="car"/>'s driver (from generation <paramref name="gen"/>, which must still be its
		/// present one) to <paramref name="driver"/>, with entry <paramref name="op"/> when it goes back to its timetable, and tells everybody.
		/// </summary>
		private bool Settle(int car, int gen, ulong driver, Op op, bool release)
		{
			cars.TryGetValue(car, out Car c);
			if (c.Gen != gen || c.Op.Kind == OpKind.Retire && !release)
			{
				return false;
			}
			ulong was = c.Driver;
			c.Driver = driver;
			c.Gen = gen + 1;
			if (op.Kind != OpKind.None)
			{
				c.Op = op;
			}
			cars[car] = c;
			link.ToAll(Write(release ? Msg.Released : Msg.Grant, car, c.Gen, driver, op), true);
			if (op.Kind != OpKind.None)
			{
				LedgerChanged?.Invoke(car, op);
			}
			Changed(car, driver, was);
			return true;
		}

		/// <summary>A car's drive changed (a new generation): the game is told whatever it was (its handler only looks at the new driver).</summary>
		private void Changed(int car, ulong driver, ulong was)
		{
			claims.Remove(car);
			handing.Remove(car);
			if (driver == link.Local && driver != was)
			{
				Granted++;
				if (was != 0UL)
				{
					HandedIn++;
				}
			}
			DriverChanged?.Invoke(car, driver, was);
		}

		/// <summary>
		/// The host takes ledger changes from player <paramref name="from"/> and tells everybody; one it can't take (the car is driven, or
		/// retired) is answered to the sender alone with the car's entry as the host has it, so the sender's provisional one is put right.
		/// </summary>
		private void LedgerFrom(ulong from, List<(int car, Op op)> ops)
		{
			List<(int, Op)> done = new List<(int, Op)>();
			List<(int, Op)> refused = new List<(int, Op)>();
			foreach ((int car, Op op) in ops)
			{
				if (SetLedger(car, op))
				{
					done.Add((car, op));
				}
				else if (from != link.Local)
				{
					cars.TryGetValue(car, out Car c);
					refused.Add((car, c.Op.Kind == OpKind.None ? new Op { Kind = OpKind.OnTime, Leg = -1 } : c.Op));
				}
			}
			SendLedgerSet(done, 0UL);
			SendLedgerSet(refused, from);
		}

		/// <summary>The host takes a ledger change for a car nobody drives (a driven car's entry is its driver's to give, on release).</summary>
		private bool SetLedger(int car, Op op)
		{
			cars.TryGetValue(car, out Car c);
			if (c.Driver != 0UL || c.Op.Kind == OpKind.Retire)
			{
				return false;
			}
			c.Op = op;
			cars[car] = c;
			LedgerChanged?.Invoke(car, op);
			return true;
		}

		private void SendLedgerSet(List<(int car, Op op)> done, ulong to)
		{
			for (int i = 0; i < done.Count; i += 100)
			{
				byte[] data = WriteOps(Msg.LedgerSet, done, i, Math.Min(done.Count, i + 100));
				if (to == 0UL)
				{
					link.ToAll(data, true);
				}
				else
				{
					link.ToOne(to, data);
				}
			}
		}

		// ---- messages ---------------------------------------------------------------------------------------------------

		/// <summary>A traffic message from another machine. <paramref name="now"/> is this machine's real clock, <paramref name="localW"/> its own sim time.</summary>
		internal void Receive(ulong from, byte[] data, double now, double localW)
		{
			if (data == null || data.Length < 7 || data[0] != Tag || data[1] != Version || BitConverter.ToInt32(data, 3) != Seed)
			{
				return;
			}
			Msg type = (Msg)data[2];
			try
			{
				using (MemoryStream m = new MemoryStream(data, 7, data.Length - 7))
				using (BinaryReader r = new BinaryReader(m))
				{
					switch (type)
					{
						case Msg.States:
							ReadStates(from, r, now, localW);
							return;
						case Msg.Hello:
							if (link.IsHost)
							{
								SendSnapshot(from);
							}
							return;
						case Msg.Damage:
							{
								int car = r.ReadInt32(), gen = r.ReadInt32();
								float amount = r.ReadSingle();
								if (Drives(car) && GenOf(car) == gen && amount > 0f && amount < 1e6f)
								{
									Damaged?.Invoke(car, amount);
								}
								return;
							}
						case Msg.Ledger:
						case Msg.LedgerSet:
							{
								List<(int car, Op op)> ops = ReadOps(r);
								if (type == Msg.Ledger)
								{
									if (link.IsHost)
									{
										LedgerFrom(from, ops);
									}
									return;
								}
								if (link.IsHost)
								{
									return;
								}
								foreach ((int car, Op op) in ops)
								{
									cars.TryGetValue(car, out Car c);
									c.Op = op;
									cars[car] = c;
									LedgerChanged?.Invoke(car, op);
								}
								return;
							}
						case Msg.Snapshot:
							{
								if (link.IsHost)
								{
									return;
								}
								int n = r.ReadInt32();
								for (int i = 0; i < n; i++)
								{
									int car = r.ReadInt32();
									Car c = new Car { Gen = r.ReadInt32(), Driver = r.ReadUInt64(), Op = ReadOp(r) };
									Apply(car, c, true);
								}
								return;
							}
						default:
							{
								int car = r.ReadInt32(), gen = r.ReadInt32();
								ulong driver = r.ReadUInt64();
								Op op = ReadOp(r);
								if (car < 0)
								{
									return;
								}
								switch (type)
								{
									case Msg.Claim:
										if (link.IsHost && DriverOf(car) == 0UL && !gone.Contains(from))
										{
											Settle(car, gen, from, default, false);
										}
										return;
									case Msg.Release:
										if (link.IsHost && DriverOf(car) == from)
										{
											Settle(car, gen, 0UL, op, true);
										}
										return;
									case Msg.Hand:
										if (link.IsHost && DriverOf(car) == from && driver != from && players.ContainsKey(driver) && !gone.Contains(driver))
										{
											Settle(car, gen, driver, default, false);
										}
										else if (link.IsHost && DriverOf(car) == from)
										{
											// The other player is gone: back to its timetable from its last state.
											Settle(car, gen, 0UL, LastOp != null ? LastOp(car) : default, true);
										}
										return;
									case Msg.Grant:
									case Msg.Released:
										if (!link.IsHost)
										{
											Apply(car, new Car { Gen = gen, Driver = driver, Op = op }, op.Kind != OpKind.None);
										}
										return;
								}
								return;
							}
					}
				}
			}
			catch (EndOfStreamException)
			{
				// A short message: dropped.
			}
		}

		/// <summary>The host's word on a car (a grant, a release, a snapshot's entry), applied here if it is newer than what this machine has.</summary>
		private void Apply(int car, Car got, bool withOp)
		{
			cars.TryGetValue(car, out Car c);
			if (got.Gen < c.Gen)
			{
				return;
			}
			ulong was = c.Driver;
			if (got.Gen == c.Gen && got.Driver == c.Driver && (!withOp || got.Op.Same(c.Op)))
			{
				// Had it (this machine's own release, as the host settled it).
				return;
			}
			c.Gen = got.Gen;
			c.Driver = got.Driver;
			if (withOp)
			{
				bool changed = !got.Op.Same(c.Op);
				c.Op = got.Op;
				cars[car] = c;
				if (changed && got.Op.Kind != OpKind.None)
				{
					LedgerChanged?.Invoke(car, got.Op);
				}
			}
			cars[car] = c;
			Changed(car, got.Driver, was);
		}

		/// <summary>Cars this machine handed away and waits to hear settled (not claimed again meanwhile).</summary>
		private readonly HashSet<int> handing = new HashSet<int>();

		private void SendSnapshot(ulong to)
		{
			List<KeyValuePair<int, Car>> all = new List<KeyValuePair<int, Car>>(cars);
			all.Sort((a, b) => a.Key.CompareTo(b.Key));
			const int Each = 90;
			for (int i = 0; i < all.Count; i += Each)
			{
				int end = Math.Min(all.Count, i + Each);
				using (MemoryStream m = new MemoryStream(64 + (end - i) * 40))
				using (BinaryWriter w = new BinaryWriter(m))
				{
					Head(w, Msg.Snapshot);
					w.Write(end - i);
					for (int k = i; k < end; k++)
					{
						w.Write(all[k].Key);
						w.Write(all[k].Value.Gen);
						w.Write(all[k].Value.Driver);
						WriteOp(w, all[k].Value.Op);
					}
					link.ToOne(to, m.ToArray());
				}
			}
		}

		private void Head(BinaryWriter w, Msg type)
		{
			w.Write(Tag);
			w.Write(Version);
			w.Write((byte)type);
			w.Write(Seed);
		}

		private byte[] Write(Msg type, int car, int gen, ulong driver, Op op)
		{
			using (MemoryStream m = new MemoryStream(56))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				Head(w, type);
				w.Write(car);
				w.Write(gen);
				w.Write(driver);
				WriteOp(w, op);
				return m.ToArray();
			}
		}

		private byte[] WriteOps(Msg type, List<(int car, Op op)> ops, int from, int to)
		{
			using (MemoryStream m = new MemoryStream(16 + (to - from) * 30))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				Head(w, type);
				w.Write(to - from);
				for (int i = from; i < to; i++)
				{
					w.Write(ops[i].car);
					WriteOp(w, ops[i].op);
				}
				return m.ToArray();
			}
		}

		private static List<(int car, Op op)> ReadOps(BinaryReader r)
		{
			int n = r.ReadInt32();
			List<(int, Op)> ops = new List<(int, Op)>();
			for (int i = 0; i < n && i < 1000; i++)
			{
				int car = r.ReadInt32();
				Op op = ReadOp(r);
				if (car >= 0)
				{
					ops.Add((car, op));
				}
			}
			return ops;
		}

		private static void WriteOp(BinaryWriter w, Op op)
		{
			w.Write((byte)op.Kind);
			w.Write(op.Leg);
			w.Write(op.W);
			w.Write(op.D);
		}

		private static Op ReadOp(BinaryReader r)
		{
			byte kind = r.ReadByte();
			Op op = new Op { Kind = kind <= (byte)OpKind.OnTime ? (OpKind)kind : OpKind.None, Leg = r.ReadInt32(), W = r.ReadDouble(), D = r.ReadDouble() };
			if (double.IsNaN(op.W) || double.IsNaN(op.D) || double.IsInfinity(op.W) || double.IsInfinity(op.D) || (op.Kind == OpKind.Late && op.Leg < 0))
			{
				op.Kind = OpKind.None;
			}
			return op;
		}

		// ---- states (unreliable) --------------------------------------------------------------------------------------------

		/// <summary>Bytes in one car's state.</summary>
		private const int StateBytes = 4 + 4 + 4 + 4 * 3 + 2 * 4 + 4 * 3 + 4 + 1 + 1;
		private const int StatesHead = 7 + 8 + 8 + 4 + 4 + 1;

		/// <summary>The most car states in one message.</summary>
		internal const int StatesEach = (MostUnreliable - StatesHead) / StateBytes;

		/// <summary>
		/// This machine's states message(s): its clocks (real <paramref name="now"/>, sim <paramref name="localW"/>), its player's place and its
		/// physics cars' states, at most <see cref="StatesEach"/> a message (always one, so the others hear where this player is).
		/// </summary>
		internal List<byte[]> WriteStates(double now, double localW, double px, double pz, List<State> states)
		{
			List<byte[]> list = new List<byte[]>();
			int i = 0;
			do
			{
				int end = Math.Min(states.Count, i + StatesEach);
				using (MemoryStream m = new MemoryStream(StatesHead + (end - i) * StateBytes))
				using (BinaryWriter w = new BinaryWriter(m))
				{
					Head(w, Msg.States);
					w.Write(now);
					w.Write(ClockOf(localW));
					w.Write((float)px);
					w.Write((float)pz);
					w.Write((byte)(end - i));
					for (int k = i; k < end; k++)
					{
						State s = states[k];
						w.Write(s.Car);
						w.Write(s.Gen);
						w.Write(s.Leg);
						w.Write(s.X);
						w.Write(s.Y);
						w.Write(s.Z);
						w.Write(Pack(s.Qx));
						w.Write(Pack(s.Qy));
						w.Write(Pack(s.Qz));
						w.Write(Pack(s.Qw));
						w.Write(s.Vx);
						w.Write(s.Vy);
						w.Write(s.Vz);
						w.Write(s.P);
						w.Write((byte)Math.Max(0, Math.Min(255, (int)Math.Round(s.Health * 2.55f))));
						w.Write(s.Flags);
					}
					list.Add(m.ToArray());
				}
				i = end;
			}
			while (i < states.Count);
			return list;
		}

		/// <summary>Sends <see cref="WriteStates"/>' messages to everybody, unreliably.</summary>
		internal void SendStates(double now, double localW, double px, double pz, List<State> states)
		{
			foreach (byte[] data in WriteStates(now, localW, px, pz, states))
			{
				link.ToAll(data, false);
			}
		}

		private void ReadStates(ulong from, BinaryReader r, double now, double localW)
		{
			double stamp = r.ReadDouble(), w = r.ReadDouble();
			float px = r.ReadSingle(), pz = r.ReadSingle();
			int n = r.ReadByte();
			if (float.IsNaN(px) || float.IsNaN(pz) || double.IsNaN(stamp) || double.IsNaN(w))
			{
				return;
			}
			players[from] = (px, pz, now);
			if (!link.IsHost && from == HostId)
			{
				ClockSample(w, localW, now);
			}
			for (int i = 0; i < n; i++)
			{
				State s = new State
				{
					Car = r.ReadInt32(),
					Gen = r.ReadInt32(),
					Leg = r.ReadInt32(),
					X = r.ReadSingle(),
					Y = r.ReadSingle(),
					Z = r.ReadSingle(),
					Qx = Unpack(r.ReadInt16()),
					Qy = Unpack(r.ReadInt16()),
					Qz = Unpack(r.ReadInt16()),
					Qw = Unpack(r.ReadInt16()),
					Vx = r.ReadSingle(),
					Vy = r.ReadSingle(),
					Vz = r.ReadSingle(),
					P = r.ReadSingle(),
					Health = r.ReadByte() / 2.55f,
					Flags = r.ReadByte(),
					Stamp = stamp,
				};
				if (float.IsNaN(s.X + s.Y + s.Z + s.Vx + s.Vy + s.Vz + s.P) || Math.Abs(s.X) > 1e6f || Math.Abs(s.Z) > 1e6f)
				{
					continue;
				}
				// Only from the car's driver, of its present drive: a state from before a hand-over, or one racing the grant, is dropped.
				if (DriverOf(s.Car) != from || GenOf(s.Car) != s.Gen)
				{
					StatesStale++;
					continue;
				}
				StatesIn++;
				StateCame?.Invoke(s);
			}
		}

		// ---- the network's thread ----------------------------------------------------------------------------------

		/// <summary>Traffic messages and players coming and going, held until the traffic reads them each frame (they may come on the network's own thread).</summary>
		private static readonly List<(ulong from, byte[] data, int player)> inbox = new List<(ulong, byte[], int)>();

		/// <summary>The most held unread (a world not built yet reads none): the oldest go, and a Hello makes up for them.</summary>
		private const int InboxMost = 4096;

		/// <summary>A traffic message came (from any thread).</summary>
		internal static void Received(ulong from, byte[] data)
		{
			Hold(from, data, 0);
		}

		/// <summary>A player joined (<paramref name="joined"/>) or left the room (from any thread).</summary>
		internal static void PlayerEvent(ulong id, bool joined)
		{
			Hold(id, null, joined ? 1 : -1);
		}

		private static void Hold(ulong from, byte[] data, int player)
		{
			lock (inbox)
			{
				if (inbox.Count >= InboxMost)
				{
					inbox.RemoveAt(0);
				}
				inbox.Add((from, data, player));
			}
		}

		/// <summary>
		/// What came since the last call, in order (null if nothing): a message (player 0), or a player who joined (1) or left (-1).
		/// </summary>
		internal static List<(ulong from, byte[] data, int player)> Take()
		{
			lock (inbox)
			{
				if (inbox.Count == 0)
				{
					return null;
				}
				List<(ulong, byte[], int)> list = new List<(ulong, byte[], int)>(inbox);
				inbox.Clear();
				return list;
			}
		}

		/// <summary>Drops the states held (between races, or before the day is built), keeping the rest for the room to read.</summary>
		internal static void DropStates()
		{
			lock (inbox)
			{
				inbox.RemoveAll(e => e.data != null && e.data.Length > 2 && e.data[2] == (byte)Msg.States);
			}
		}

		/// <summary>The room's host's id, set by the game (the sim: machine 1's), for reading the host's clock.</summary>
		internal ulong HostId;

		private static short Pack(float q) => (short)Math.Max(-32767, Math.Min(32767, (int)Math.Round(q * 32767f)));

		private static float Unpack(short v) => v / 32767f;

		/// <summary>The settled drivers and entries, in one string, for the sim to compare machines.</summary>
		internal string Fingerprint()
		{
			List<int> keys = new List<int>(cars.Keys);
			keys.Sort();
			var sb = new System.Text.StringBuilder();
			foreach (int k in keys)
			{
				Car c = cars[k];
				// On time and no entry are the same.
				Op op = c.Op.Kind == OpKind.OnTime ? default : c.Op;
				if (c.Gen == 0 && c.Driver == 0UL && op.Kind == OpKind.None)
				{
					continue;
				}
				sb.Append(k).Append(':').Append(c.Gen).Append(':').Append(c.Driver).Append(':').Append((int)op.Kind).Append(':').Append(op.Leg).Append(':').Append(op.W.ToString("R")).Append(':').Append(op.D.ToString("R")).Append(';');
			}
			return sb.ToString();
		}

		/// <summary>
		/// A stand-in's playback: the states of a car another machine drives, played back a little behind the newest (<see cref="Delay"/>
		/// and however unevenly they come), smoothly between two states on the driver's clock (mapped to this machine's by the least delay
		/// seen, the playback's clock eased, never stepped, when that changes), carried on with its velocity for up to <see cref="Coast"/>
		/// past the newest if states stop coming, then held. Pure arithmetic (the sim checks it with drops and jitter).
		/// </summary>
		internal sealed class Playback
		{
			/// <summary>The least it plays behind the newest state (seconds): a state or two at 15 a second, so one lost is not seen.</summary>
			internal const double Delay = 0.12;
			/// <summary>The most it plays behind, however uneven the states come (seconds).</summary>
			internal const double MostDelay = 0.45;
			/// <summary>How long it carries on past the newest state with its velocity (seconds).</summary>
			internal const double Coast = 0.6;
			/// <summary>How much faster or slower than real time the playback may run to close on where it should be (so it never jumps).</summary>
			private const double Ease = 0.08;

			private readonly List<State> states = new List<State>();
			/// <summary>The least (this machine's clock − the driver's) seen lately: the driver's clock is this machine's minus it, at the fastest delivery.</summary>
			private double lag = double.MaxValue;
			/// <summary>How much later than the fastest the states come (decaying slowly): the playback stays that far further behind.</summary>
			private double spread;
			/// <summary>The driver's clock the playback is at, and this machine's real time when it was (NaN: not started).</summary>
			private double play = double.NaN, playedAt;

			internal int Count => states.Count;

			internal State Newest => states[states.Count - 1];

			internal void Clear()
			{
				states.Clear();
				lag = double.MaxValue;
				spread = 0.0;
				play = double.NaN;
			}

			/// <summary>A state came at this machine's real time <paramref name="now"/>.</summary>
			internal void Add(State s, double now)
			{
				double l = now - s.Stamp;
				if (l < lag)
				{
					spread = Math.Max(0.0, spread + (lag == double.MaxValue ? 0.0 : l - lag));
					lag = l;
				}
				spread = Math.Max(spread * 0.98, l - lag);
				// In the driver's order (unreliable messages can come out of order or twice).
				int i = states.Count;
				while (i > 0 && states[i - 1].Stamp > s.Stamp)
				{
					i--;
				}
				if (i > 0 && states[i - 1].Stamp == s.Stamp)
				{
					return;
				}
				states.Insert(i, s);
				// A second of states is plenty.
				while (states.Count > 2 && Newest.Stamp - states[0].Stamp > 1.0)
				{
					states.RemoveAt(0);
				}
			}

			/// <summary>
			/// The driver's clock to play at, at this machine's real time <paramref name="now"/>: real time on from the last call, eased by at
			/// most <see cref="Ease"/> toward the fastest delivery's clock less the delay (taken at once if it is over a second off).
			/// </summary>
			internal double PlayAt(double now)
			{
				double want = now - lag - Math.Min(MostDelay, Delay + spread);
				if (double.IsNaN(play) || Math.Abs(want - play) > 1.0 || now < playedAt)
				{
					play = want;
				}
				else
				{
					double dt = now - playedAt;
					play += dt + Math.Max(-Ease * dt, Math.Min(Ease * dt, want - (play + dt)));
				}
				playedAt = now;
				return play;
			}

			/// <summary>Where the stand-in is at this machine's real time <paramref name="now"/> (call once a frame): false if no state has come.</summary>
			internal bool Sample(double now, out State at)
			{
				at = default;
				if (states.Count == 0)
				{
					return false;
				}
				double t = PlayAt(now);
				if (t <= states[0].Stamp)
				{
					// Before the first state (just started, or an older one may still come): it, moved back along its velocity, so an older
					// state coming late lands where the car is drawn.
					at = states[0];
					float back = (float)Math.Max(-Coast, t - at.Stamp);
					if (!at.Stopped)
					{
						at.X += at.Vx * back;
						at.Y += at.Vy * back;
						at.Z += at.Vz * back;
					}
					at.Stamp = t;
					return true;
				}
				for (int i = 1; i < states.Count; i++)
				{
					if (t <= states[i].Stamp)
					{
						State a = states[i - 1], b = states[i];
						double span = b.Stamp - a.Stamp;
						float u = span > 1e-6 ? (float)((t - a.Stamp) / span) : 1f;
						at = Lerp(a, b, u);
						at.Stamp = t;
						return true;
					}
				}
				State n = Newest;
				float ahead = (float)Math.Min(Coast, t - n.Stamp);
				at = n;
				if (!n.Stopped)
				{
					at.X += n.Vx * ahead;
					at.Y += n.Vy * ahead;
					at.Z += n.Vz * ahead;
					at.P += (float)Math.Sqrt(n.Vx * n.Vx + n.Vz * n.Vz) * ahead;
				}
				at.Stamp = n.Stamp + ahead;
				return true;
			}

			private static State Lerp(State a, State b, float u)
			{
				State s = b;
				s.X = a.X + (b.X - a.X) * u;
				s.Y = a.Y + (b.Y - a.Y) * u;
				s.Z = a.Z + (b.Z - a.Z) * u;
				s.Vx = a.Vx + (b.Vx - a.Vx) * u;
				s.Vy = a.Vy + (b.Vy - a.Vy) * u;
				s.Vz = a.Vz + (b.Vz - a.Vz) * u;
				s.P = a.Leg == b.Leg ? a.P + (b.P - a.P) * u : b.P;
				// The shorter way round (q and -q are the same turn), then normalised.
				float dot = a.Qx * b.Qx + a.Qy * b.Qy + a.Qz * b.Qz + a.Qw * b.Qw;
				float sgn = dot < 0f ? -1f : 1f;
				s.Qx = a.Qx * sgn + (b.Qx - a.Qx * sgn) * u;
				s.Qy = a.Qy * sgn + (b.Qy - a.Qy * sgn) * u;
				s.Qz = a.Qz * sgn + (b.Qz - a.Qz * sgn) * u;
				s.Qw = a.Qw * sgn + (b.Qw - a.Qw * sgn) * u;
				float len = (float)Math.Sqrt(s.Qx * s.Qx + s.Qy * s.Qy + s.Qz * s.Qz + s.Qw * s.Qw);
				if (len > 1e-6f)
				{
					s.Qx /= len;
					s.Qy /= len;
					s.Qz /= len;
					s.Qw /= len;
				}
				return s;
			}
		}
	}
}
