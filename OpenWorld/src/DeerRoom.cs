using System;
using System.Collections.Generic;
using System.IO;

namespace HalfGripOpenWorld
{
	/// <summary>What a group of deer does about a car (or a shot, or one of them killed), as decided by one machine.</summary>
	internal enum ReactKind : byte
	{
		/// <summary>Heads up and watching it, staying where they are (the calm deer go on underneath).</summary>
		Alert,
		/// <summary>Away at a gallop to one of the group's spots, single file, the nearest deer first.</summary>
		Flee,
	}

	/// <summary>Which way a group flees.</summary>
	internal enum FleeWay : byte
	{
		/// <summary>Away from the road to the cover (or the field) on the side they are on.</summary>
		Away,
		/// <summary>Over the road at the crossing to the far side: in front of the car.</summary>
		Across,
		/// <summary>Frozen, staring, for a few seconds, then away.</summary>
		Freeze,
	}

	/// <summary>One reaction of one group: what, which way, from when (race time), and where from (the car, the shot, the kill).</summary>
	internal struct Reaction
	{
		public int Seed, Group, Seq;
		public ReactKind Kind;
		public FleeWay Way;
		public double T0, FromX, FromZ;

		internal bool Same(Reaction o)
		{
			return Seed == o.Seed && Group == o.Group && Seq == o.Seq && Kind == o.Kind && Way == o.Way && T0 == o.T0 && FromX == o.FromX && FromZ == o.FromZ;
		}
	}

	/// <summary>
	/// The deer of a race in a room: which reactions each group has had, in order, and which deer are dead, the same on every machine.
	/// The room's host (which never changes: the room ends when it leaves) settles both.
	///
	/// A machine that sees a group react to its own car (or its gun, or a deer it killed) decides the reaction from what it sees exactly
	/// and plays it at once (provisional), and sends it to the host as a claim on the group's next reaction (<see cref="Reaction.Seq"/>,
	/// the count it has). The host takes the first claim for each number and sends it to everybody; a claim that came second (another
	/// machine's for the same number got there first) is dropped, and its sender, which gets the winner like everyone, puts the winner in
	/// place of its own. Kills likewise: the first claim on a deer the host gets is the kill, and replaces any other a machine showed.
	/// Alone, this machine is the host and nothing is sent.
	///
	/// A machine that joins a room late (or builds its world late) says Hello when its deer are ready, and the host sends it every
	/// reaction and kill so far. Messages come in order from each sender (Steam's reliable messages); a reaction from the host ahead of
	/// one still missing (the Hello's answer racing a new one) is held until those before it come.
	///
	/// No Unity types: <c>tools/deersim</c> drives it on pretend machines over a pretend network.
	/// </summary>
	internal sealed class DeerRoom
	{
		/// <summary>The first byte of every deer message (never an <see cref="EventRoom.Version"/>, which the events' start with).</summary>
		internal const byte Tag = 0xD3;

		/// <summary>The messages' layout. Raise it with any change to what they carry.</summary>
		internal const byte Version = 2;

		private enum Msg : byte
		{
			KillClaim = 1,
			Kill = 2,
			ReactionClaim = 3,
			Reaction = 4,
			Hello = 5,
		}

		/// <summary>How the room's messages go (the game's network, or a pretend one).</summary>
		internal interface ILink
		{
			/// <summary>This machine settles (the room's host, or alone).</summary>
			bool IsHost { get; }

			void ToHost(byte[] data);

			void ToAll(byte[] data);

			void ToOne(ulong to, byte[] data);
		}

		internal readonly int Seed;
		private readonly ILink link;

		private readonly Dictionary<int, List<Reaction>> settled = new Dictionary<int, List<Reaction>>();
		private readonly Dictionary<int, Reaction> mine = new Dictionary<int, Reaction>();
		private readonly Dictionary<long, Reaction> early = new Dictionary<long, Reaction>();
		private readonly Dictionary<int, DeerKills.Kill> kills = new Dictionary<int, DeerKills.Kill>();
		private readonly Dictionary<int, DeerKills.Kill> myKills = new Dictionary<int, DeerKills.Kill>();
		private readonly List<Reaction> scratch = new List<Reaction>();

		/// <summary>A group's reactions changed (one added, or this machine's own put right): the herds must be told.</summary>
		internal Action<int> ReactionsChanged;

		/// <summary>A deer's kill came or changed (the host's put in place of this machine's own): its body starts again from it.</summary>
		internal Action<DeerKills.Kill> KillChanged;

		internal DeerRoom(int seed, ILink link)
		{
			Seed = seed;
			this.link = link;
		}

		/// <summary>Group g's reactions as this machine shows them: the host's, then this machine's own claim if it has one waiting.</summary>
		internal IReadOnlyList<Reaction> Reactions(int g)
		{
			scratch.Clear();
			if (settled.TryGetValue(g, out List<Reaction> list))
			{
				scratch.AddRange(list);
			}
			if (mine.TryGetValue(g, out Reaction r))
			{
				scratch.Add(r);
			}
			return scratch;
		}

		/// <summary>How many reactions group g has had (the number its next is claimed as).</summary>
		internal int Count(int g)
		{
			return (settled.TryGetValue(g, out List<Reaction> list) ? list.Count : 0) + (mine.ContainsKey(g) ? 1 : 0);
		}

		/// <summary>Whether this machine has a claim on group g waiting for the host (no other is claimed meanwhile).</summary>
		internal bool Waiting(int g)
		{
			return mine.ContainsKey(g);
		}

		/// <summary>Every deer killed as this machine shows it (the host's, and this machine's own waiting).</summary>
		internal IEnumerable<DeerKills.Kill> Kills()
		{
			foreach (DeerKills.Kill k in kills.Values)
			{
				yield return k;
			}
			foreach (KeyValuePair<int, DeerKills.Kill> kv in myKills)
			{
				if (!kills.ContainsKey(kv.Key))
				{
					yield return kv.Value;
				}
			}
		}

		internal bool IsDead(int group, int member)
		{
			int key = DeerKills.Key(group, member);
			return kills.ContainsKey(key) || myKills.ContainsKey(key);
		}

		/// <summary>The settled reactions and kills, in one string, for the sim to compare machines.</summary>
		internal string Fingerprint()
		{
			var sb = new System.Text.StringBuilder();
			List<int> groups = new List<int>(settled.Keys);
			groups.Sort();
			foreach (int g in groups)
			{
				foreach (Reaction r in settled[g])
				{
					sb.Append('r').Append(g).Append(':').Append(r.Seq).Append(':').Append((int)r.Kind).Append((int)r.Way).Append(':').Append(r.T0.ToString("R")).Append(':').Append(r.FromX.ToString("R")).Append(';');
				}
			}
			List<int> dead = new List<int>(kills.Keys);
			dead.Sort();
			foreach (int key in dead)
			{
				DeerKills.Kill k = kills[key];
				sb.Append('k').Append(key).Append(':').Append(k.T.ToString("R")).Append(':').Append(k.X.ToString("R")).Append(';');
			}
			return sb.ToString();
		}

		/// <summary>How many claims this machine still waits on (none once the host has answered them all).</summary>
		internal int Pending => mine.Count + myKills.Count + early.Count;

		// ---- this machine's own --------------------------------------------------------------------------------------

		/// <summary>
		/// This machine's reaction for a group, played at once and claimed of the host as the group's next. False (nothing done) while
		/// an earlier claim of this machine's on the group waits.
		/// </summary>
		internal bool Claim(Reaction r)
		{
			if (mine.ContainsKey(r.Group))
			{
				return false;
			}
			r.Seed = Seed;
			r.Seq = Count(r.Group);
			if (link.IsHost)
			{
				Settle(r);
				link.ToAll(Write(Msg.Reaction, r));
			}
			else
			{
				mine[r.Group] = r;
				link.ToHost(Write(Msg.ReactionClaim, r));
			}
			ReactionsChanged?.Invoke(r.Group);
			return true;
		}

		/// <summary>This machine's kill of a deer, shown at once and claimed of the host. False if the deer is already dead here.</summary>
		internal bool Claim(DeerKills.Kill k)
		{
			k.Seed = Seed;
			if (IsDead(k.Group, k.Member))
			{
				return false;
			}
			// As it will be read on the other machines (sent at float precision), so every machine's body starts the same.
			DeerKills.Decode(DeerKills.Encode(k), out k);
			int key = DeerKills.Key(k.Group, k.Member);
			if (link.IsHost)
			{
				kills[key] = k;
				link.ToAll(WriteKill(Msg.Kill, k));
			}
			else
			{
				myKills[key] = k;
				link.ToHost(WriteKill(Msg.KillClaim, k));
			}
			KillChanged?.Invoke(k);
			return true;
		}

		/// <summary>Asks the host for every reaction and kill so far (a machine that is not the host, once its deer are ready).</summary>
		internal void Hello()
		{
			if (!link.IsHost)
			{
				link.ToHost(new byte[] { Tag, Version, (byte)Msg.Hello });
			}
		}

		// ---- messages ---------------------------------------------------------------------------------------------------

		/// <summary>A deer message from another machine.</summary>
		internal void Receive(ulong from, byte[] data)
		{
			if (data == null || data.Length < 3 || data[0] != Tag || data[1] != Version)
			{
				return;
			}
			Msg type = (Msg)data[2];
			switch (type)
			{
				case Msg.Hello:
					if (link.IsHost)
					{
						foreach (List<Reaction> list in settled.Values)
						{
							foreach (Reaction r in list)
							{
								link.ToOne(from, Write(Msg.Reaction, r));
							}
						}
						foreach (DeerKills.Kill k in kills.Values)
						{
							link.ToOne(from, WriteKill(Msg.Kill, k));
						}
					}
					return;
				case Msg.ReactionClaim:
				case Msg.Reaction:
					{
						if (!Read(data, out Reaction r) || r.Seed != Seed || r.Group < 0)
						{
							return;
						}
						if (type == Msg.ReactionClaim)
						{
							// The first claim for each number wins; one that came second is dropped (its sender gets the winner).
							if (link.IsHost && r.Seq == SettledCount(r.Group))
							{
								Settle(r);
								link.ToAll(Write(Msg.Reaction, r));
								ReactionsChanged?.Invoke(r.Group);
							}
							return;
						}
						if (link.IsHost)
						{
							return;
						}
						int have = SettledCount(r.Group);
						if (r.Seq < have)
						{
							return; // had it (a Hello's answer)
						}
						if (r.Seq > have)
						{
							early[Key(r.Group, r.Seq)] = r;
							return;
						}
						Settle(r);
						while (early.TryGetValue(Key(r.Group, SettledCount(r.Group)), out Reaction next))
						{
							early.Remove(Key(r.Group, next.Seq));
							Settle(next);
						}
						// This machine's own claim: had now, or lost to another's.
						if (mine.TryGetValue(r.Group, out Reaction own) && own.Seq < SettledCount(r.Group))
						{
							mine.Remove(r.Group);
						}
						ReactionsChanged?.Invoke(r.Group);
						return;
					}
				case Msg.KillClaim:
				case Msg.Kill:
					{
						if (!ReadKill(data, out DeerKills.Kill k) || k.Seed != Seed || k.Group < 0 || k.Member < 0 || k.Member >= 64)
						{
							return;
						}
						int key = DeerKills.Key(k.Group, k.Member);
						if (type == Msg.KillClaim)
						{
							if (link.IsHost && !kills.ContainsKey(key))
							{
								kills[key] = k;
								link.ToAll(WriteKill(Msg.Kill, k));
								KillChanged?.Invoke(k);
							}
							return;
						}
						if (link.IsHost || kills.ContainsKey(key))
						{
							return;
						}
						kills[key] = k;
						bool shown = myKills.TryGetValue(key, out DeerKills.Kill own);
						myKills.Remove(key);
						if (!shown || !SameKill(own, k))
						{
							KillChanged?.Invoke(k);
						}
						return;
					}
			}
		}

		private int SettledCount(int g)
		{
			return settled.TryGetValue(g, out List<Reaction> list) ? list.Count : 0;
		}

		private void Settle(Reaction r)
		{
			if (!settled.TryGetValue(r.Group, out List<Reaction> list))
			{
				list = new List<Reaction>();
				settled[r.Group] = list;
			}
			list.Add(r);
		}

		private static long Key(int g, int seq)
		{
			return (long)g << 32 | (uint)seq;
		}

		private static bool SameKill(DeerKills.Kill a, DeerKills.Kill b)
		{
			return a.How == b.How && a.T == b.T && a.X == b.X && a.Z == b.Z && a.Vx == b.Vx && a.Vz == b.Vz;
		}

		private static byte[] Write(Msg type, Reaction r)
		{
			using (MemoryStream m = new MemoryStream(48))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				w.Write(Tag);
				w.Write(Version);
				w.Write((byte)type);
				w.Write(r.Seed);
				w.Write((short)r.Group);
				w.Write((short)r.Seq);
				w.Write((byte)r.Kind);
				w.Write((byte)r.Way);
				w.Write(r.T0);
				w.Write(r.FromX);
				w.Write(r.FromZ);
				return m.ToArray();
			}
		}

		private static bool Read(byte[] data, out Reaction r)
		{
			r = default;
			try
			{
				using (MemoryStream m = new MemoryStream(data, 3, data.Length - 3))
				using (BinaryReader b = new BinaryReader(m))
				{
					r.Seed = b.ReadInt32();
					r.Group = b.ReadInt16();
					r.Seq = b.ReadInt16();
					byte kind = b.ReadByte(), way = b.ReadByte();
					if (kind > (byte)ReactKind.Flee || way > (byte)FleeWay.Freeze)
					{
						return false;
					}
					r.Kind = (ReactKind)kind;
					r.Way = (FleeWay)way;
					r.T0 = b.ReadDouble();
					r.FromX = b.ReadDouble();
					r.FromZ = b.ReadDouble();
					return r.Seq >= 0 && !double.IsNaN(r.T0) && !double.IsNaN(r.FromX) && !double.IsNaN(r.FromZ);
				}
			}
			catch (EndOfStreamException)
			{
				return false;
			}
		}

		private static byte[] WriteKill(Msg type, DeerKills.Kill k)
		{
			byte[] body = DeerKills.Encode(k);
			byte[] data = new byte[body.Length + 3];
			data[0] = Tag;
			data[1] = Version;
			data[2] = (byte)type;
			Array.Copy(body, 0, data, 3, body.Length);
			return data;
		}

		private static bool ReadKill(byte[] data, out DeerKills.Kill k)
		{
			byte[] body = new byte[data.Length - 3];
			Array.Copy(data, 3, body, 0, body.Length);
			return DeerKills.Decode(body, out k);
		}

		// ---- the network's thread ----------------------------------------------------------------------------------

		/// <summary>Deer messages come on the network's own thread or the main one: held here until the deer read them each frame.</summary>
		private static readonly List<(ulong from, byte[] data)> inbox = new List<(ulong, byte[])>();

		/// <summary>The most messages held unread (a world not built yet reads none; a Hello's answer is one per reaction): the oldest go.</summary>
		private const int InboxMost = 8192;

		internal static void Received(ulong from, byte[] data)
		{
			lock (inbox)
			{
				if (inbox.Count >= InboxMost)
				{
					inbox.RemoveAt(0);
				}
				inbox.Add((from, data));
			}
		}

		/// <summary>The messages come since the last call, in the order they came (null if none).</summary>
		internal static List<(ulong from, byte[] data)> Take()
		{
			lock (inbox)
			{
				if (inbox.Count == 0)
				{
					return null;
				}
				List<(ulong, byte[])> list = new List<(ulong, byte[])>(inbox);
				inbox.Clear();
				return list;
			}
		}
	}
}
