using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;
using Op = HalfGripOpenWorld.TrafficRoom.Op;
using OpKind = HalfGripOpenWorld.TrafficRoom.OpKind;

// Drives the traffic's room (TrafficRoom.cs) without the game: several pretend machines, each with its own TrafficRoom, over a pretend
// network. Reliable messages are delayed 30 to 250 ms, in order from each sender to each receiver (Steam's reliable messages);
// unreliable ones (the states) are delayed as much, out of order, and one in ten is lost. Machine 1 is the room's host (it never
// changes: the room ends when the host leaves). Ids start at 1 (0 is "nobody drives it").
//   dotnet run -c Release --project tools/trafficsim -- [seed]
// Runs: claims of the same car from several machines at once, with releases (late, early, retired) and hand-overs mixed in, checking at
// every step that no car is driven on two machines; a late joiner saying Hello while all that goes on; a player leaving with cars (the
// host gives them back from their last states); ledger changes (held back, caught up) from everybody, some for cars being driven;
// hits on stand-ins reaching only the driver of that drive; states after a hand-over dropped; the clock (machines 30 s apart, stalling)
// taking the host's without a jump; the stand-ins' playback over a lossy network; who is nearest and who to hand to; twenty players; bad
// messages; alone. After the network goes quiet it checks every machine has the host's drivers and ledger and waits on nothing.
// Exits 1 if a check fails.
internal static class Program
{
	private static int failures, checks;

	private static int Main(string[] args)
	{
		int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
		Scenario("claims at once, releases and hand-overs", seed, n => Churn(n, 5, 30, 400));
		Scenario("a late joiner", seed, LateJoiner);
		Scenario("a player leaves", seed, Leaves);
		Scenario("ledger changes from everybody", seed, LedgerFromAll);
		Scenario("hits and states after a hand-over", seed, HitsAndStates);
		Scenario("the clock", seed, Clock);
		Scenario("the stand-ins' playback", seed, PlaybackCheck);
		Scenario("nearest and hand to", seed, NearestCheck);
		Scenario("twenty players at random", seed, n => Churn(n, 20, 60, 1500));
		Scenario("bad messages", seed, Bad);
		Scenario("alone", seed, Alone);
		Console.WriteLine(checks + " checks, " + failures + " failed: " + (failures == 0 ? "ok" : "FAIL"));
		return failures == 0 ? 0 : 1;
	}

	private static Random rnd;

	private static void Scenario(string name, int seed, Action<Net> run)
	{
		int before = failures;
		rnd = new Random(seed * 7919 + name.Length);
		var net = new Net();
		run(net);
		Console.WriteLine((failures == before ? "ok    " : "FAIL  ") + name + " (" + net.Delivered + " messages, " + net.Lost + " lost)");
	}

	private static bool Check(bool ok, string what)
	{
		checks++;
		if (!ok)
		{
			failures++;
			if (failures < 60)
			{
				Console.WriteLine("  failed: " + what);
			}
		}
		return ok;
	}

	// ---- the pretend room ------------------------------------------------------------------------------------------

	private const int RaceSeed = 42;

	private sealed class Machine : TrafficRoom.ILink
	{
		public readonly ulong Id;
		public readonly Net Net;
		public TrafficRoom Room;
		/// <summary>What the game would hold: the cars it drives, the stand-ins it shows, its ledger (as TrafficLate keeps it).</summary>
		public readonly HashSet<int> Driving = new HashSet<int>();
		public readonly HashSet<int> StandIns = new HashSet<int>();
		public readonly Dictionary<int, Op> Ledger = new Dictionary<int, Op>();
		public readonly List<(int car, float amount)> Hits = new List<(int, float)>();
		public readonly List<TrafficRoom.State> States = new List<TrafficRoom.State>();
		/// <summary>This machine's own sim clock: the host's plus an offset (machines start their races apart) and its stalls.</summary>
		public double ClockOffset, Stalled;
		public double X, Z;

		public Machine(ulong id, Net net)
		{
			Id = id;
			Net = net;
			Room = new TrafficRoom(RaceSeed, this) { HostId = 1 };
			Room.DriverChanged = (car, driver, was) =>
			{
				if (driver == Id)
				{
					Driving.Add(car);
				}
				else
				{
					Driving.Remove(car);
				}
				if (driver != 0UL && driver != Id)
				{
					StandIns.Add(car);
				}
				else
				{
					StandIns.Remove(car);
				}
			};
			Room.LedgerChanged = (car, op) =>
			{
				// As TrafficLate does: a retired car stays retired; on time drops the entry.
				if (Ledger.TryGetValue(car, out Op old) && old.Kind == OpKind.Retire)
				{
					return;
				}
				if (op.Kind == OpKind.OnTime)
				{
					Ledger.Remove(car);
				}
				else if (op.Kind != OpKind.None)
				{
					Ledger[car] = op;
				}
			};
			Room.Damaged = (car, amount) => Hits.Add((car, amount));
			Room.StateCame = s => States.Add(s);
		}

		public bool IsHost => Id == 1;

		public ulong Local => Id;

		public double LocalW => Net.Now + ClockOffset - Stalled;

		public void ToHost(byte[] data)
		{
			if (!IsHost)
			{
				Net.Send(Id, 1, data, true);
			}
		}

		public void ToAll(byte[] data, bool reliable)
		{
			foreach (Machine m in Net.Machines)
			{
				if (m.Id != Id)
				{
					Net.Send(Id, m.Id, data, reliable);
				}
			}
		}

		public void ToOne(ulong to, byte[] data) => Net.Send(Id, to, data, true);

		/// <summary>Sends this machine's states (its place, its clock and a state of each car it drives).</summary>
		public void SendStates()
		{
			var list = new List<TrafficRoom.State>();
			foreach (int car in Driving.OrderBy(c => c))
			{
				list.Add(new TrafficRoom.State { Car = car, Gen = Room.GenOf(car), Leg = car * 3, X = (float)(X + car), Z = (float)Z, Qw = 1f, Vx = 10f, P = 5f, Health = 80f, Flags = TrafficRoom.FlagAlive });
			}
			Room.SendStates(Net.Now, LocalW, X, Z, list);
		}
	}

	private sealed class Net
	{
		public readonly List<Machine> Machines = new List<Machine>();
		private readonly List<(double at, long order, ulong from, ulong to, byte[] data)> queue = new List<(double, long, ulong, ulong, byte[])>();
		private readonly Dictionary<(ulong, ulong), double> last = new Dictionary<(ulong, ulong), double>();
		private long order;
		public double Now = 1000.0;
		public int Delivered, Lost;
		/// <summary>Called after every delivery and every step (the safety check).</summary>
		public Action After;
		public double LossRate = 0.1;

		public Machine Add()
		{
			var m = new Machine((ulong)Machines.Count + 1, this);
			Machines.Add(m);
			return m;
		}

		public Machine Get(ulong id) => Machines.FirstOrDefault(m => m.Id == id);

		public void Send(ulong from, ulong to, byte[] data, bool reliable)
		{
			if (data.Length > (reliable ? TrafficRoom.MostReliable : TrafficRoom.MostUnreliable))
			{
				Check(false, "a message of " + data.Length + " bytes is too big to send " + (reliable ? "reliably" : "unreliably"));
			}
			if (!reliable && rnd.NextDouble() < LossRate)
			{
				Lost++;
				return;
			}
			double at = Now + 0.03 + rnd.NextDouble() * 0.22;
			if (reliable)
			{
				// In order from each sender to each receiver.
				if (last.TryGetValue((from, to), out double prev) && at < prev)
				{
					at = prev;
				}
				last[(from, to)] = at;
			}
			queue.Add((at, order++, from, to, (byte[])data.Clone()));
		}

		/// <summary>Delivers what is due by now + dt, then ticks every machine.</summary>
		public void Step(double dt)
		{
			double end = Now + dt;
			while (true)
			{
				int best = -1;
				for (int i = 0; i < queue.Count; i++)
				{
					if (queue[i].at <= end && (best < 0 || queue[i].at < queue[best].at || queue[i].at == queue[best].at && queue[i].order < queue[best].order))
					{
						best = i;
					}
				}
				if (best < 0)
				{
					break;
				}
				var q = queue[best];
				queue.RemoveAt(best);
				Now = Math.Max(Now, q.at);
				Machine to = Get(q.to);
				if (to != null)
				{
					Delivered++;
					to.Room.Receive(q.from, q.data, Now, to.LocalW);
					After?.Invoke();
				}
			}
			Now = end;
			foreach (Machine m in Machines)
			{
				m.Room.Tick(Now, dt);
			}
			After?.Invoke();
		}

		public int Waiting => queue.Count;

		public void Quiet()
		{
			for (int i = 0; i < 400 && queue.Count > 0; i++)
			{
				Step(0.05);
			}
			// Claims the host never answered (it says nothing to a claim it refuses) are given up.
			Step(TrafficRoom.ClaimWait + 0.1);
		}
	}

	/// <summary>No car is driven on two machines (checked after every delivery).</summary>
	private static void Safety(Net net)
	{
		net.After = () =>
		{
			var seen = new Dictionary<int, ulong>();
			foreach (Machine m in net.Machines)
			{
				foreach (int car in m.Driving)
				{
					if (seen.TryGetValue(car, out ulong other))
					{
						Check(false, "car " + car + " driven on machines " + other + " and " + m.Id + " at once");
					}
					seen[car] = m.Id;
				}
			}
		};
	}

	private static string LedgerPrint(Machine m)
	{
		return string.Join(";", m.Ledger.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + (int)kv.Value.Kind + ":" + kv.Value.Leg + ":" + kv.Value.W.ToString("R") + ":" + kv.Value.D.ToString("R")));
	}

	/// <summary>Every machine has the host's drivers and ledger, drives what the host says it drives, shows stand-ins for the rest and waits on nothing.</summary>
	private static void Agree(Net net, string when)
	{
		Machine host = net.Machines[0];
		string want = host.Room.Fingerprint(), ledger = LedgerPrint(host);
		foreach (Machine m in net.Machines)
		{
			Check(m.Room.Fingerprint() == want, when + ": machine " + m.Id + " has the host's drivers and entries");
			Check(LedgerPrint(m) == ledger, when + ": machine " + m.Id + "'s ledger is the host's (" + m.Ledger.Count + " entries, the host " + host.Ledger.Count + ")");
			Check(m.Room.Claims == 0, when + ": machine " + m.Id + " waits on no claim");
			var drives = host.Room.Cars.Where(kv => kv.Value.Driver == m.Id).Select(kv => kv.Key).OrderBy(c => c).ToList();
			Check(drives.SequenceEqual(m.Driving.OrderBy(c => c)), when + ": machine " + m.Id + " drives the cars the host says (" + m.Driving.Count + ", the host says " + drives.Count + ")");
			var remote = host.Room.Cars.Where(kv => kv.Value.Driver != 0UL && kv.Value.Driver != m.Id).Select(kv => kv.Key).OrderBy(c => c).ToList();
			Check(remote.SequenceEqual(m.StandIns.OrderBy(c => c)), when + ": machine " + m.Id + " shows a stand-in for every car another drives");
		}
	}

	private static Op RandomOp(double w)
	{
		double r = rnd.NextDouble();
		if (r < 0.06)
		{
			return new Op { Kind = OpKind.Retire, Leg = -1, W = w };
		}
		if (r < 0.15)
		{
			return new Op { Kind = OpKind.OnTime, Leg = -1, W = w };
		}
		return new Op { Kind = OpKind.Late, Leg = rnd.Next(500), W = w, D = Math.Round(rnd.NextDouble() * 80.0 - 5.0, 2) };
	}

	/// <summary>One random thing a machine does: claim a car (often one others claim at the same moment), release one it drives, or hand one on.</summary>
	private static void Act(Net net, int cars)
	{
		Machine m = net.Machines[rnd.Next(net.Machines.Count)];
		double r = rnd.NextDouble();
		if (r < 0.5)
		{
			int car = rnd.Next(cars);
			foreach (Machine c in net.Machines.OrderBy(_ => rnd.Next()).Take(1 + rnd.Next(Math.Min(4, net.Machines.Count))))
			{
				c.Room.Claim(car, net.Now);
			}
		}
		else if (m.Driving.Count > 0)
		{
			int car = m.Driving.ElementAt(rnd.Next(m.Driving.Count));
			if (r < 0.8 || net.Machines.Count == 1)
			{
				m.Room.Release(car, RandomOp(m.Room.ClockOf(m.LocalW)));
			}
			else
			{
				Machine to = net.Machines[rnd.Next(net.Machines.Count)];
				m.Room.Hand(car, to.Id);
			}
		}
	}

	// ---- the scenarios -----------------------------------------------------------------------------------------------

	private static void Churn(Net net, int machines, int cars, int rounds)
	{
		for (int i = 0; i < machines; i++)
		{
			net.Add();
		}
		Safety(net);
		// Everybody hears where everybody is (hand-overs need the target to be known to the host).
		foreach (Machine m in net.Machines)
		{
			m.SendStates();
		}
		net.Quiet();
		int granted = 0;
		for (int round = 0; round < rounds; round++)
		{
			Act(net, cars);
			if (rnd.NextDouble() < 0.6)
			{
				net.Step(rnd.NextDouble() * 0.15);
			}
		}
		net.Quiet();
		Agree(net, "after " + rounds + " rounds");
		granted = net.Machines.Sum(m => m.Room.Granted);
		Check(granted > rounds / 10, "cars were granted (" + granted + ")");
		Check(net.Machines.Sum(m => m.Room.HandedIn) > 0 || machines == 1, "hand-overs happened (" + net.Machines.Sum(m => m.Room.HandedIn) + ")");
		Check(net.Machines.Sum(m => m.Room.Claimed) > granted, "claims lost to another's (" + net.Machines.Sum(m => m.Room.Claimed) + " made, " + granted + " granted)");
	}

	private static void LateJoiner(Net net)
	{
		for (int i = 0; i < 3; i++)
		{
			net.Add();
		}
		Safety(net);
		foreach (Machine m in net.Machines)
		{
			m.SendStates();
		}
		for (int i = 0; i < 200; i++)
		{
			Act(net, 20);
			net.Step(rnd.NextDouble() * 0.1);
		}
		// A machine joins and says Hello while claims, releases and hand-overs go on (the host's answer racing new ones).
		Machine late = net.Add();
		late.Room.Hello();
		late.SendStates();
		for (int i = 0; i < 200; i++)
		{
			Act(net, 20);
			net.Step(rnd.NextDouble() * 0.1);
		}
		net.Quiet();
		Agree(net, "after a late joiner");
		Check(late.Room.Fingerprint().Length > 0, "the late joiner learnt the cars from before it came");
	}

	private static void Leaves(Net net)
	{
		for (int i = 0; i < 4; i++)
		{
			net.Add();
		}
		Safety(net);
		Machine host = net.Machines[0], leaver = net.Machines[2];
		var lastOps = new Dictionary<int, Op>();
		host.Room.LastOp = car => lastOps.TryGetValue(car, out Op op) ? op : default;
		foreach (Machine m in net.Machines)
		{
			m.SendStates();
		}
		for (int car = 0; car < 10; car++)
		{
			leaver.Room.Claim(car, net.Now);
		}
		for (int car = 10; car < 14; car++)
		{
			net.Machines[1].Room.Claim(car, net.Now);
		}
		net.Quiet();
		Check(leaver.Driving.Count == 10, "the leaver drives its 10 cars");
		for (int car = 0; car < 10; car++)
		{
			lastOps[car] = car % 4 == 0 ? new Op { Kind = OpKind.Retire, Leg = -1, W = 50.0 } : new Op { Kind = OpKind.Late, Leg = car, W = 50.0, D = car * 1.5 };
		}
		// It goes (with a claim and a release still on the way to the host).
		leaver.Room.Release(3, new Op { Kind = OpKind.Late, Leg = 3, W = 49.0, D = 2.0 });
		leaver.Room.Claim(20, net.Now);
		net.Step(0.01);
		net.Machines.Remove(leaver);
		foreach (Machine m in net.Machines)
		{
			m.Room.PlayerLeft(leaver.Id);
		}
		net.Quiet();
		Agree(net, "after a player left");
		Check(!host.Room.Cars.Any(kv => kv.Value.Driver == leaver.Id), "no car is left driven by the player who left");
		Check(host.Ledger.TryGetValue(4, out Op four) && four.Kind == OpKind.Retire && host.Ledger.TryGetValue(5, out Op five) && five.Kind == OpKind.Late && five.D == 7.5, "the leaver's cars went back with their last states' entries");
		Check(host.Room.DriverOf(11) == net.Machines[1].Id, "the other player's cars are untouched");
	}

	private static void LedgerFromAll(Net net)
	{
		for (int i = 0; i < 4; i++)
		{
			net.Add();
		}
		Safety(net);
		foreach (Machine m in net.Machines)
		{
			m.SendStates();
		}
		var ops = new List<(int, Op)>();
		for (int round = 0; round < 300; round++)
		{
			Machine m = net.Machines[rnd.Next(net.Machines.Count)];
			double r = rnd.NextDouble();
			if (r < 0.15)
			{
				Act(net, 25);
			}
			else
			{
				ops.Clear();
				int n = 1 + rnd.Next(4);
				for (int k = 0; k < n; k++)
				{
					int car = rnd.Next(25);
					// Held back (late), or caught up: some for cars being driven (refused, put right).
					ops.Add((car, rnd.NextDouble() < 0.7 ? new Op { Kind = OpKind.Late, Leg = car, W = round, D = 1.0 + rnd.Next(30) } : new Op { Kind = OpKind.OnTime, Leg = -1, W = round }));
				}
				m.Room.Ledger(ops);
			}
			if (rnd.NextDouble() < 0.5)
			{
				net.Step(rnd.NextDouble() * 0.1);
			}
		}
		// A big catch-up from the host (more than one message's worth).
		ops.Clear();
		for (int car = 0; car < 260; car++)
		{
			ops.Add((car, new Op { Kind = OpKind.Late, Leg = car, W = 999.0, D = 3.0 }));
		}
		net.Machines[0].Room.Ledger(ops);
		net.Quiet();
		Agree(net, "after ledger changes from everybody");
	}

	private static void HitsAndStates(Net net)
	{
		Machine a = net.Add(), b = net.Add(), c = net.Add();
		a.Id.ToString();
		Safety(net);
		net.LossRate = 0.0;
		foreach (Machine m in net.Machines)
		{
			m.SendStates();
		}
		net.Quiet();
		b.Room.Claim(7, net.Now);
		net.Quiet();
		Check(b.Driving.Contains(7) && c.StandIns.Contains(7) && a.StandIns.Contains(7), "machine 2 drives car 7, the others show a stand-in");
		c.Room.Damage(7, 12.5f);
		a.Room.Damage(7, 30f);
		net.Quiet();
		Check(b.Hits.Count == 2 && b.Hits.Sum(h => h.amount) == 42.5f && a.Hits.Count == 0 && c.Hits.Count == 0, "hits on the stand-ins reached the driver only");
		b.SendStates();
		net.Quiet();
		Check(a.States.Count(s => s.Car == 7) == 1 && c.States.Count(s => s.Car == 7) == 1, "the driver's state reached the others");
		// Hand-over to machine 3; machine 2 sends a state and machine 1 a hit at the same moment (both of the old drive).
		int staleBefore = a.Room.StatesStale + c.Room.StatesStale;
		b.Driving.Add(7); // as if its last state went out with the hand
		b.SendStates();
		b.Driving.Remove(7);
		b.Room.Hand(7, c.Id);
		net.Quiet();
		Check(c.Driving.Contains(7) && b.StandIns.Contains(7) && !b.Driving.Contains(7), "after the hand-over machine 3 drives car 7 and machine 2 shows a stand-in");
		int hitsBefore = c.Hits.Count;
		// A hit made with the old drive's generation (sent before the grant came): dropped by the new driver.
		a.Room.Damage(7, 5f);
		net.Quiet();
		Check(c.Hits.Count == hitsBefore + 1, "a hit after the hand-over reaches the new driver");
		b.Driving.Add(7);
		int before = a.States.Count(s => s.Car == 7);
		b.SendStates(); // machine 2 still thinks it drives (a bug, or a late message): dropped everywhere
		b.Driving.Remove(7);
		net.Quiet();
		Check(a.States.Count(s => s.Car == 7) == before && a.Room.StatesStale + c.Room.StatesStale > staleBefore, "states from the old driver are dropped");
		c.SendStates();
		net.Quiet();
		Check(a.States.Count(s => s.Car == 7) == before + 1, "the new driver's states are taken");
		Agree(net, "after hits and states");
	}

	private static void Clock(Net net)
	{
		Machine host = net.Add();
		var others = new List<Machine>();
		for (int i = 0; i < 4; i++)
		{
			Machine m = net.Add();
			m.ClockOffset = rnd.NextDouble() * 60.0 - 30.0;
			others.Add(m);
		}
		double worstJump = 0.0;
		var lastClock = new Dictionary<ulong, double>();
		for (int step = 0; step < 15 * 40; step++)
		{
			foreach (Machine m in net.Machines)
			{
				m.SendStates();
			}
			if (step == 15 * 20)
			{
				// Machine 3 stalls for 2 s (its race clock held back) and machine 4 for 7 s (past what is eased: taken at once).
				others[1].Stalled += 2.0;
				others[2].Stalled += 7.0;
			}
			net.Step(1.0 / 15.0);
			foreach (Machine m in others)
			{
				if (!m.Room.ClockKnown)
				{
					continue;
				}
				double clock = m.Room.ClockOf(m.LocalW);
				if (lastClock.TryGetValue(m.Id, out double prev) && step != 15 * 20 && !(m == others[2] && step > 15 * 20 && step < 15 * 21))
				{
					// The room's clock moves on with time (1/15 s a step), give or take what is eased (0.2 s a second).
					worstJump = Math.Max(worstJump, Math.Abs(clock - prev - 1.0 / 15.0));
				}
				lastClock[m.Id] = clock;
			}
		}
		foreach (Machine m in others)
		{
			double err = m.Room.ClockOf(m.LocalW) - host.LocalW;
			Check(Math.Abs(err) < 0.3, "machine " + m.Id + " has the host's clock (" + err.ToString("0.000") + " s off, after a start " + m.ClockOffset.ToString("0.0") + " s apart" + (m.Stalled > 0 ? " and a " + m.Stalled + " s stall" : "") + ")");
			Check(err <= 0.01, "machine " + m.Id + "'s clock is never ahead of the host's (a reading is late, never early)");
		}
		Check(worstJump < 0.2 / 15.0 + 1e-6, "no machine's clock jumped (worst " + worstJump.ToString("0.0000") + " s a step, at most " + (0.2 / 15.0).ToString("0.0000") + ")");
	}

	private static void PlaybackCheck(Net net)
	{
		// One car at 25 units a second along x (and round a bend), states 15 a second with 30 to 250 ms of jitter, one in ten lost,
		// out of order; played back 60 frames a second.
		var p = new TrafficRoom.Playback();
		var incoming = new List<(double at, TrafficRoom.State s)>();
		double t0 = 500.0, worst = 0.0, lagSum = 0.0;
		int frames = 0;
		Func<double, (double x, double z)> path = t => t < 4.0 ? (25.0 * t, 0.0) : (100.0 + 40.0 * Math.Sin((t - 4.0) * 25.0 / 40.0), 40.0 - 40.0 * Math.Cos((t - 4.0) * 25.0 / 40.0));
		for (int k = 0; k < 15 * 8; k++)
		{
			double sent = k / 15.0;
			if (rnd.NextDouble() < 0.1)
			{
				continue;
			}
			var (x, z) = path(sent);
			var (x2, z2) = path(sent + 0.01);
			incoming.Add((t0 + sent + 0.03 + rnd.NextDouble() * 0.22, new TrafficRoom.State { Car = 1, X = (float)x, Z = (float)z, Vx = (float)((x2 - x) * 100.0), Vz = (float)((z2 - z) * 100.0), Qw = 1f, Flags = TrafficRoom.FlagAlive, Stamp = 2000.0 + sent }));
		}
		double prevX = double.NaN, prevZ = 0.0;
		for (double now = t0; now < t0 + 7.5; now += 1.0 / 60.0)
		{
			foreach (var e in incoming.Where(e => e.at <= now).ToList())
			{
				p.Add(e.s, e.at);
				incoming.Remove(e);
			}
			if (!p.Sample(now, out TrafficRoom.State at))
			{
				continue;
			}
			if (!double.IsNaN(prevX))
			{
				double step = Math.Sqrt((at.X - prevX) * (at.X - prevX) + (at.Z - prevZ) * (at.Z - prevZ));
				worst = Math.Max(worst, step);
				if (step > 0.9 && Environment.GetEnvironmentVariable("TS_DEBUG") == "1")
				{
					Console.WriteLine("  jump " + step.ToString("0.00") + " at " + (now - t0).ToString("0.000") + " playing " + (at.Stamp - 2000.0).ToString("0.000") + " newest " + (p.Newest.Stamp - 2000.0).ToString("0.000") + " states " + p.Count);
				}
			}
			prevX = at.X;
			prevZ = at.Z;
			if (now > t0 + 1.0)
			{
				var (tx, tz) = path(at.Stamp - 2000.0);
				lagSum += now - t0 - (at.Stamp - 2000.0);
				frames++;
				Check(Math.Abs(tx - at.X) < 1.5 && Math.Abs(tz - at.Z) < 1.5, "the stand-in is on the car's path (" + at.X.ToString("0.0") + ", " + at.Z.ToString("0.0") + " against " + tx.ToString("0.0") + ", " + tz.ToString("0.0") + ")");
			}
		}
		Check(worst < 25.0 / 60.0 * 2.0, "the stand-in never jumps (most moved in a frame " + worst.ToString("0.00") + " units, 25 a second is " + (25.0 / 60.0).ToString("0.00") + ")");
		double lag = lagSum / Math.Max(1, frames);
		Check(lag > 0.15 && lag < 0.5, "it plays " + lag.ToString("0.00") + " s behind the driver");
		// States stop: it coasts a little, then holds.
		var q = new TrafficRoom.Playback();
		for (int k = 0; k < 10; k++)
		{
			q.Add(new TrafficRoom.State { X = k * 2f, Vx = 30f, Qw = 1f, Stamp = k / 15.0, Flags = TrafficRoom.FlagAlive }, 10.0 + k / 15.0 + 0.05);
		}
		q.Sample(30.0, out TrafficRoom.State held);
		Check(Math.Abs(held.X - (18f + 30f * (float)TrafficRoom.Playback.Coast)) < 0.01, "with no more states it carries on " + TrafficRoom.Playback.Coast + " s and holds (" + held.X.ToString("0.0") + ")");
	}

	private static void NearestCheck(Net net)
	{
		var ms = new List<Machine>();
		for (int i = 0; i < 5; i++)
		{
			Machine m = net.Add();
			m.X = rnd.NextDouble() * 2000.0;
			m.Z = rnd.NextDouble() * 2000.0;
			ms.Add(m);
		}
		// Two players at the same spot: only one may claim.
		ms[4].X = ms[3].X + 2.0;
		ms[4].Z = ms[3].Z;
		net.LossRate = 0.0;
		foreach (Machine m in ms)
		{
			m.Room.Me(m.X, m.Z, net.Now);
			m.SendStates();
		}
		net.Quiet();
		foreach (Machine m in ms)
		{
			m.Room.Me(m.X, m.Z, net.Now);
		}
		int bad = 0;
		for (int k = 0; k < 2000; k++)
		{
			double x = rnd.NextDouble() * 2000.0, z = rnd.NextDouble() * 2000.0;
			if (k % 10 == 0)
			{
				x = ms[3].X + rnd.NextDouble() * 4.0 - 2.0;
				z = ms[3].Z + rnd.NextDouble() * 4.0;
			}
			int nearest = ms.Count(m => m.Room.Nearest(x, z, net.Now));
			bad += nearest == 1 ? 0 : 1;
		}
		Check(bad == 0, "exactly one machine is the nearest to each place (" + bad + " of 2000 places had none or two)");
		// Hand to: a player past reach gives a car to one within it; a car between two players is kept (no back and forth).
		Machine a = ms[0];
		double cx = a.X + 350.0, cz = a.Z;
		ms[1].X = cx + 100.0;
		ms[1].Z = cz;
		ms[1].SendStates();
		net.Quiet();
		a.Room.Me(a.X, a.Z, net.Now);
		Check(a.Room.HandTo(cx, cz, net.Now, 300.0, 120.0) == ms[1].Id, "a car past this player's reach goes to the player within it");
		Check(a.Room.HandTo(a.X + 150.0, a.Z, net.Now, 300.0, 120.0) == 0UL, "a car between two players stays with its driver");
		Check(a.Room.HandTo(a.X + 50.0, a.Z, net.Now, 300.0, 120.0) == 0UL, "a car close to its driver stays");
		Check(a.Room.HandTo(cx, cz, net.Now + 10.0, 300.0, 120.0) == 0UL, "a player not heard from lately is not handed anything");
	}

	private static void Bad(Net net)
	{
		Machine host = net.Add(), a = net.Add();
		Safety(net);
		var other = new TrafficRoom(RaceSeed + 1, a);
		// Another race's claim, junk and short messages: dropped.
		host.Room.Receive(2, new byte[] { TrafficRoom.Tag, TrafficRoom.Version, 1, 43, 0, 0, 0, 5, 0, 0, 0 }, net.Now, 0.0);
		host.Room.Receive(2, new byte[] { TrafficRoom.Tag, TrafficRoom.Version, 1, 42, 0, 0, 0, 5 }, net.Now, 0.0);
		host.Room.Receive(2, new byte[] { TrafficRoom.Tag, 99, 1 }, net.Now, 0.0);
		host.Room.Receive(2, null, net.Now, 0.0);
		a.Room.Receive(1, new byte[] { TrafficRoom.Tag, TrafficRoom.Version, 11, 42, 0, 0, 0, 1, 2, 3 }, net.Now, 0.0);
		other.Claim(5, net.Now);
		net.Quiet();
		Check(host.Room.Fingerprint() == "" && host.Driving.Count == 0, "another race's claim and bad messages change nothing");
		a.Room.Claim(5, net.Now);
		net.Quiet();
		Agree(net, "after bad messages");
		Check(a.Driving.Contains(5), "a good claim after them is granted");
	}

	private static void Alone(Net net)
	{
		Machine host = net.Add();
		Safety(net);
		Check(host.Room.Claim(3, net.Now) && host.Driving.Contains(3), "alone: a claim is granted at once");
		Check(!host.Room.Claim(3, net.Now), "alone: a car is claimed once");
		host.Room.Release(3, new Op { Kind = OpKind.Late, Leg = 2, W = 10.0, D = 4.0 });
		Check(!host.Driving.Contains(3) && host.Ledger.TryGetValue(3, out Op op) && op.D == 4.0, "alone: a release goes back to the timetable at once, late");
		host.Room.Claim(4, net.Now);
		host.Room.Release(4, new Op { Kind = OpKind.Retire, Leg = -1, W = 11.0 });
		Check(!host.Room.Claim(4, net.Now), "alone: a retired car is never claimed again");
		host.Room.Ledger(new List<(int, Op)> { (9, new Op { Kind = OpKind.Late, Leg = 1, W = 5.0, D = 2.0 }) });
		Check(host.Ledger.ContainsKey(9), "alone: a ledger change is taken at once");
		Check(host.Room.ClockOf(123.0) == 123.0, "alone: the clock is this machine's");
		host.SendStates();
		net.Quiet();
		Check(net.Delivered == 0, "alone: nothing is sent anywhere");
	}
}
