using System;
using System.Collections.Generic;
using HalfGripTools;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// How <see cref="EventRoom"/>'s messages go: through HalfGripTools' <c>ctx.Net</c>, which the Multiplayer mod carries between this
	/// mod's copies in a room (bytes only reach the Open World on the other machines). In a room the room's host is the arbiter; outside
	/// one (solo, or no Multiplayer mod) this machine is, and nothing is sent: a lobby of one is a solo run.
	///
	/// The handlers are added once (<see cref="Hook"/>, in <c>Mod.Load</c>) and pass everything to the room of the world that is built
	/// now; what comes before the world is built is held (up to <see cref="HoldMost"/>) and read when it is (<see cref="Replay"/>):
	/// until 0.26 it was dropped, and a lobby opened then was never seen. The room is watched each frame (<see cref="Watch"/>): when
	/// this machine joins or leaves a room, or the room's host changes and stays changed for <see cref="HostHold"/>, every lobby is
	/// forgotten (they were the old arbiter's) and the new arbiter is asked for its own.
	///
	/// Every lobby message sent, received or held is logged (verbose), with the Hellos, snapshots and resets, so a player's report of
	/// a lobby they never saw can be followed in both machines' logs.
	/// </summary>
	internal sealed class EventLink : EventRoom.ILink
	{
		/// <summary>This player's id outside a room: never a Steam id.</summary>
		private const ulong Alone = 1UL;

		private static ModContext ctx;
		private static bool hooked;

		/// <summary>Messages held while no world is built, at most this many (the newest kept: a snapshot makes up for the rest).</summary>
		private const int HoldMost = 64;

		/// <summary>A held message older than this when the world is built is dropped (it was for a world long gone).</summary>
		private const double HoldSeconds = 30.0;

		/// <summary>A new host (or room) is taken only once it has held this long, in seconds of real time.</summary>
		private const double HostHold = 1.0;

		private static readonly List<(ulong from, byte[] data, double at)> held = new List<(ulong, byte[], double)>();
		private static int lost;

		private ulong seenHost = ulong.MaxValue;
		private ulong seenLocal = ulong.MaxValue;
		private ulong candidateHost = ulong.MaxValue;
		private ulong candidateLocal = ulong.MaxValue;
		private double candidateSince;

		/// <summary>The room being linked, or null between worlds.</summary>
		internal static EventRoom Room;

		internal static bool InRoom => HalfGripTools.Net.InRoom;

		public ulong Local => InRoom ? HalfGripTools.Net.LocalId : Alone;

		public bool Arbiter => !InRoom || HalfGripTools.Net.IsHost;

		/// <summary>This machine's own clock for the events: real time since the game started, which no long frame holds back.</summary>
		internal static double Clock()
		{
			return Time.realtimeSinceStartupAsDouble;
		}

		public void ToArbiter(byte[] data)
		{
			if (InRoom && ctx != null)
			{
				Sent(data, "the host");
				if (!ctx.Net.SendToHost(data))
				{
					Mod.Log.LogWarning("Events: a request could not be sent to the room's host");
				}
			}
		}

		public void ToAll(byte[] data)
		{
			if (InRoom && ctx != null)
			{
				Sent(data, "everybody");
				ctx.Net.Send(data);
			}
		}

		public void ToOne(ulong to, byte[] data)
		{
			if (InRoom && ctx != null)
			{
				Sent(data, Name(to));
				ctx.Net.Send(data, to);
			}
		}

		private static void Sent(byte[] data, string to)
		{
			if (!EventRoom.IsClock(data))
			{
				Mod.Info("Events: sent " + EventRoom.Describe(data) + " to " + to);
			}
		}

		/// <summary>Adds the network's handlers, once per game session.</summary>
		internal static void Hook(ModContext context)
		{
			ctx = context;
			if (hooked)
			{
				return;
			}
			hooked = true;
			try
			{
				ctx.Net.OnReceive(Received);
				HalfGripTools.Net.PlayerJoined += p =>
				{
					// Before this machine's world is built nothing is lost: the joiner asks with Hello until it has every lobby.
					Room?.PlayerJoined(p.Id, Clock());
				};
				HalfGripTools.Net.PlayerLeft += p =>
				{
					Room?.PlayerLeft(p.Id, Clock());
				};
				Mod.Info("Events: linked to the room's messages (" + (HalfGripTools.Net.Available ? "Multiplayer found" : "no Multiplayer: solo only") + ")");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Events: the room's messages could not be hooked (events are solo only): " + e.Message);
			}
		}

		private static void Received(ulong from, byte[] data)
		{
			try
			{
				bool clock = EventRoom.IsClock(data);
				if (Room == null)
				{
					if (held.Count >= HoldMost)
					{
						held.RemoveAt(0);
						lost++;
					}
					held.Add((from, data, Clock()));
					if (!clock)
					{
						Mod.Info("Events: held " + EventRoom.Describe(data) + " from " + Name(from) + " until the world is built");
					}
					return;
				}
				if (!clock)
				{
					Mod.Info("Events: received " + EventRoom.Describe(data) + " from " + Name(from));
				}
				Room.Receive(from, data, Clock());
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Events: a message could not be read: " + e.Message);
			}
		}

		/// <summary>The world is built and <see cref="Room"/> set: what came before is read, in the order it came.</summary>
		internal static void Replay()
		{
			if (held.Count == 0 && lost == 0)
			{
				return;
			}
			Mod.Info("Events: reading " + held.Count + " message(s) held while the world was built" + (lost > 0 ? " (" + lost + " more lost: the snapshot makes up for them)" : ""));
			List<(ulong from, byte[] data, double at)> list = new List<(ulong, byte[], double)>(held);
			held.Clear();
			lost = 0;
			double now = Clock();
			foreach ((ulong from, byte[] data, double at) in list)
			{
				if (now - at <= HoldSeconds)
				{
					Received(from, data);
				}
			}
		}

		/// <summary>
		/// Each frame: when the room this machine is in (or not) or its host has changed since the last look, and stayed so for
		/// <see cref="HostHold"/>, the lobbies are forgotten and the new arbiter asked for its own. True when that happened. A host of 0
		/// in a room (not known yet, or between hosts) is never taken: until 0.26 any flicker of the host's id reset every lobby.
		/// </summary>
		internal bool Watch(double clock)
		{
			ulong host = InRoom ? HalfGripTools.Net.HostId : 0UL, local = Local;
			bool first = seenHost == ulong.MaxValue;
			if (host == seenHost && local == seenLocal || (InRoom && host == 0UL))
			{
				candidateHost = ulong.MaxValue;
				return false;
			}
			if (!first)
			{
				if (host != candidateHost || local != candidateLocal)
				{
					candidateHost = host;
					candidateLocal = local;
					candidateSince = clock;
					return false;
				}
				if (clock - candidateSince < HostHold)
				{
					return false;
				}
			}
			candidateHost = ulong.MaxValue;
			seenHost = host;
			seenLocal = local;
			if (Room != null)
			{
				if (!first)
				{
					Room.Reset();
					Mod.Log.LogInfo("Events: the room or its host changed: every lobby forgotten, the new arbiter asked for its own");
				}
				Room.Hello(clock);
				Mod.Info("Events: " + (InRoom ? (Arbiter ? "the room's host, holding its lobbies" : "in a room, the host holds the lobbies") : "alone, a lobby of one is a solo run"));
			}
			return !first;
		}

		/// <summary>A player's name as the room has it ("You" for this player outside a room).</summary>
		internal static string Name(ulong id)
		{
			if (!InRoom)
			{
				return "You";
			}
			try
			{
				foreach (NetPlayer p in HalfGripTools.Net.Players)
				{
					if (p.Id == id)
					{
						return string.IsNullOrEmpty(p.Name) ? "Player" : p.Name;
					}
				}
			}
			catch (Exception)
			{
				// The list is a copy; a failure here only loses a name.
			}
			return "Player";
		}
	}
}
