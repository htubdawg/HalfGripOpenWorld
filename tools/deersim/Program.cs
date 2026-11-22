using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;

// Drives the deer's room (DeerRoom.cs) without the game: several pretend machines, each with its own DeerRoom, over a pretend network
// that delays every message (30 to 250 ms, in order from each sender to each receiver, as Steam's reliable messages are). Machine 0 is
// the room's host (it never changes: the room ends when the host leaves).
//   dotnet run -c Release --project tools/deersim -- [seed]
// Runs: claims of the same group's next reaction from several machines at the same moment, over and over (the host takes the first;
// every machine ends with the host's list, a loser's own claim put right); kills of the same deer from several machines at once (one
// kill each, the host's, on every machine, and each machine's body last started from it); the host's own claims mixed in; a machine
// joining late that says Hello while new reactions and kills keep coming (its answer racing them); messages of another race and bad
// messages (dropped); alone (nothing sent). After the network goes quiet it checks every machine shows exactly the host's reactions
// and kills and waits on nothing. Exits 1 if a check fails.
internal static class Program
{
	private static int failures, checks;

	private static int Main(string[] args)
	{
		int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
		Scenario("claims at once", seed, ClaimsAtOnce);
		Scenario("kills at once", seed, KillsAtOnce);
		Scenario("a late joiner", seed, LateJoiner);
		Scenario("twenty players at random", seed, Busy);
		Scenario("stale and bad messages", seed, Stale);
		Scenario("alone", seed, Alone);
		Console.WriteLine(checks + " checks, " + failures + " failed: " + (failures == 0 ? "ok" : "FAIL"));
		return failures == 0 ? 0 : 1;
	}

	private static void Scenario(string name, int seed, Action<Net, Random> run)
	{
		int before = failures;
		var rnd = new Random(seed * 7919 + name.Length);
		var net = new Net(rnd);
		run(net, rnd);
		Console.WriteLine((failures == before ? "ok    " : "FAIL  ") + name + " (" + net.Delivered + " messages)");
	}

	private static void Check(bool ok, string what)
	{
		checks++;
		if (!ok)
		{
			failures++;
			Console.WriteLine("  failed: " + what);
		}
	}

	// ---- the pretend room ------------------------------------------------------------------------------------------

	private const int RaceSeed = 42;

	private sealed class Machine : DeerRoom.ILink
	{
		public readonly ulong Id;
		public readonly Net Net;
		public DeerRoom Room;
		/// <summary>The kill each deer's body last started from, as the game would have it (KillChanged).</summary>
		public readonly Dictionary<int, DeerKills.Kill> Bodies = new Dictionary<int, DeerKills.Kill>();
		public int Changes;

		public Machine(ulong id, Net net)
		{
			Id = id;
			Net = net;
			Room = new DeerRoom(RaceSeed, this);
			Room.ReactionsChanged = g => Changes++;
			Room.KillChanged = k => Bodies[DeerKills.Key(k.Group, k.Member)] = k;
		}

		public bool IsHost => Id == 0;

		public void ToHost(byte[] data) => Net.Send(Id, 0, data);

		public void ToAll(byte[] data)
		{
			foreach (Machine m in Net.Machines)
			{
				if (m.Id != Id)
				{
					Net.Send(Id, m.Id, data);
				}
			}
		}

		public void ToOne(ulong to, byte[] data) => Net.Send(Id, to, data);
	}

	private sealed class Net
	{
		public readonly List<Machine> Machines = new List<Machine>();
		private readonly Random rnd;
		private readonly List<(double at, long order, ulong from, ulong to, byte[] data)> queue = new List<(double, long, ulong, ulong, byte[])>();
		private readonly Dictionary<(ulong, ulong), double> last = new Dictionary<(ulong, ulong), double>();
		private long order;
		public double Now;
		public int Delivered;

		public Net(Random rnd)
		{
			this.rnd = rnd;
		}

		public Machine Add()
		{
			var m = new Machine((ulong)Machines.Count, this);
			Machines.Add(m);
			return m;
		}

		public void Send(ulong from, ulong to, byte[] data)
		{
			double at = Now + 0.03 + rnd.NextDouble() * 0.22;
			// In order from each sender to each receiver.
			if (last.TryGetValue((from, to), out double prev) && at < prev)
			{
				at = prev;
			}
			last[(from, to)] = at;
			queue.Add((at, order++, from, to, (byte[])data.Clone()));
		}

		/// <summary>Delivers what is due by now + dt.</summary>
		public void Step(double dt)
		{
			Now += dt;
			while (true)
			{
				int best = -1;
				for (int i = 0; i < queue.Count; i++)
				{
					if (queue[i].at <= Now && (best < 0 || queue[i].at < queue[best].at || queue[i].at == queue[best].at && queue[i].order < queue[best].order))
					{
						best = i;
					}
				}
				if (best < 0)
				{
					return;
				}
				var q = queue[best];
				queue.RemoveAt(best);
				Machine to = Machines.FirstOrDefault(m => m.Id == q.to);
				if (to != null)
				{
					Delivered++;
					to.Room.Receive(q.from, q.data);
				}
			}
		}

		public void Quiet()
		{
			for (int i = 0; i < 400 && queue.Count > 0; i++)
			{
				Step(0.05);
			}
		}
	}

	private static Reaction React(int g, double t, Random rnd)
	{
		return new Reaction { Group = g, Kind = rnd.NextDouble() < 0.4 ? ReactKind.Alert : ReactKind.Flee, Way = (FleeWay)rnd.Next(3), T0 = t, FromX = rnd.NextDouble() * 1000.0, FromZ = rnd.NextDouble() * 1000.0 };
	}

	private static DeerKills.Kill KillOf(int g, int m, double t, Random rnd)
	{
		return new DeerKills.Kill { Group = g, Member = m, How = rnd.NextDouble() < 0.5 ? DeerKills.How.Car : DeerKills.How.Shot, T = t, X = rnd.NextDouble() * 1000.0, Z = rnd.NextDouble() * 1000.0, Y = 50.0, Vx = rnd.NextDouble() * 40.0, Scale = 1.0, Side = 1 };
	}

	/// <summary>Every machine shows the host's reactions and kills, waits on nothing, and its bodies started from the host's kills.</summary>
	private static void Agree(Net net, int groups, string when)
	{
		Machine host = net.Machines[0];
		string want = host.Room.Fingerprint();
		foreach (Machine m in net.Machines)
		{
			Check(m.Room.Fingerprint() == want, when + ": machine " + m.Id + " has the host's reactions and kills");
			Check(m.Room.Pending == 0, when + ": machine " + m.Id + " waits on nothing (" + m.Room.Pending + ")");
			for (int g = 0; g < groups; g++)
			{
				var a = host.Room.Reactions(g).ToList();
				var b = m.Room.Reactions(g).ToList();
				Check(a.Count == b.Count && a.Zip(b, (x, y) => x.Same(y)).All(s => s), when + ": machine " + m.Id + " shows the host's reactions for group " + g);
			}
			var hostKills = host.Room.Kills().ToDictionary(k => DeerKills.Key(k.Group, k.Member));
			var shown = m.Room.Kills().ToDictionary(k => DeerKills.Key(k.Group, k.Member));
			Check(shown.Count == hostKills.Count, when + ": machine " + m.Id + " shows " + shown.Count + " kills, the host " + hostKills.Count);
			foreach (var kv in hostKills)
			{
				Check(m.Bodies.TryGetValue(kv.Key, out DeerKills.Kill body) && body.T == kv.Value.T && body.X == kv.Value.X, when + ": machine " + m.Id + "'s body of deer " + kv.Key + " is the host's kill");
			}
		}
	}

	// ---- the scenarios -----------------------------------------------------------------------------------------------

	private static void ClaimsAtOnce(Net net, Random rnd)
	{
		for (int i = 0; i < 4; i++)
		{
			net.Add();
		}
		double t = 100.0;
		for (int round = 0; round < 60; round++)
		{
			int g = rnd.Next(5);
			// Two to four machines (the host among them now and then) claim the same group's next reaction in the same moment.
			foreach (Machine m in net.Machines.OrderBy(_ => rnd.Next()).Take(2 + rnd.Next(3)))
			{
				m.Room.Claim(React(g, t + rnd.NextDouble() * 0.1, rnd));
			}
			net.Step(rnd.NextDouble() * 0.3);
			t += 1.0;
		}
		net.Quiet();
		Agree(net, 5, "after claims at once");
		Check(Enumerable.Range(0, 5).Sum(g => net.Machines[0].Room.Count(g)) >= 30, "the host took a claim of most rounds");
	}

	private static void KillsAtOnce(Net net, Random rnd)
	{
		for (int i = 0; i < 4; i++)
		{
			net.Add();
		}
		for (int round = 0; round < 40; round++)
		{
			int g = rnd.Next(10), d = rnd.Next(4);
			foreach (Machine m in net.Machines.OrderBy(_ => rnd.Next()).Take(2 + rnd.Next(3)))
			{
				m.Room.Claim(KillOf(g, d, 50.0 + round, rnd));
			}
			net.Step(rnd.NextDouble() * 0.2);
		}
		net.Quiet();
		Agree(net, 10, "after kills at once");
	}

	private static void LateJoiner(Net net, Random rnd)
	{
		for (int i = 0; i < 3; i++)
		{
			net.Add();
		}
		double t = 10.0;
		for (int i = 0; i < 40; i++)
		{
			net.Machines[rnd.Next(3)].Room.Claim(React(rnd.Next(6), t, rnd));
			if (i % 4 == 0)
			{
				net.Machines[rnd.Next(3)].Room.Claim(KillOf(rnd.Next(6), rnd.Next(4), t, rnd));
			}
			net.Step(0.1);
			t += 0.5;
		}
		// A machine joins, says Hello, and reactions and kills go on meanwhile (the host's answer racing new ones).
		Machine late = net.Add();
		late.Room.Hello();
		for (int i = 0; i < 30; i++)
		{
			net.Machines[rnd.Next(4)].Room.Claim(React(rnd.Next(6), t, rnd));
			if (i % 3 == 0)
			{
				net.Machines[rnd.Next(4)].Room.Claim(KillOf(rnd.Next(6), rnd.Next(4), t, rnd));
			}
			net.Step(rnd.NextDouble() * 0.15);
			t += 0.5;
		}
		net.Quiet();
		Agree(net, 6, "after a late joiner");
	}

	private static void Busy(Net net, Random rnd)
	{
		for (int i = 0; i < 20; i++)
		{
			net.Add();
		}
		double t = 0.0;
		for (int i = 0; i < 400; i++)
		{
			Machine m = net.Machines[rnd.Next(20)];
			if (rnd.NextDouble() < 0.8)
			{
				m.Room.Claim(React(rnd.Next(12), t, rnd));
			}
			else
			{
				m.Room.Claim(KillOf(rnd.Next(12), rnd.Next(6), t, rnd));
			}
			if (rnd.NextDouble() < 0.5)
			{
				net.Step(rnd.NextDouble() * 0.1);
			}
			t += 0.05;
		}
		net.Quiet();
		Agree(net, 12, "after twenty players");
	}

	private static void Stale(Net net, Random rnd)
	{
		Machine host = net.Add(), a = net.Add();
		// Another race's messages and junk: dropped.
		var other = new DeerRoom(RaceSeed + 1, new Machine(9, new Net(rnd)));
		var r = React(1, 5.0, rnd);
		var sink = new Machine(8, net);
		sink.Room = new DeerRoom(RaceSeed + 1, sink);
		net.Machines.Add(sink);
		sink.Room.Claim(KillOf(1, 1, 5.0, rnd)); // not the host: a claim to the host, of another race
		host.Room.Receive(8, new byte[] { DeerRoom.Tag, DeerRoom.Version, 3, 1, 2 });
		host.Room.Receive(8, new byte[] { DeerRoom.Tag, 99, 4 });
		a.Room.Receive(8, null);
		net.Quiet();
		net.Machines.Remove(sink);
		Check(host.Room.Kills().Count() == 0 && host.Room.Fingerprint() == "", "another race's claim and bad messages change nothing");
		a.Room.Claim(r);
		net.Quiet();
		Agree(net, 2, "after stale messages");
	}

	private static void Alone(Net net, Random rnd)
	{
		Machine host = net.Add();
		host.Room.Claim(React(0, 1.0, rnd));
		host.Room.Claim(KillOf(0, 0, 1.0, rnd));
		Check(net.Delivered == 0 && host.Room.Count(0) == 1 && host.Room.IsDead(0, 0), "alone: claims taken at once, nothing sent anywhere");
		Check(!host.Room.Claim(KillOf(0, 0, 2.0, rnd)), "alone: a deer is killed once");
	}
}
