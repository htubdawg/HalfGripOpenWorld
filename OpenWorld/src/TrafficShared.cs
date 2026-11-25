using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Stage 4 of the traffic plan, the traffic in a room (part of <see cref="TrafficCars"/>): the room (<see cref="TrafficRoom"/>) read
	/// each frame, this machine's physics cars' states sent, the stand-ins of cars other machines drive, and the room's word acted on: a
	/// car granted to this machine becomes a physics car (from where it is drawn, or from its stand-in when it was handed over), a car
	/// another drives is a stand-in, a car given back is drawn again with its ledger entry. Alone (solo, or alone in a room) this machine
	/// is the host: every claim is granted at once and nothing is sent, so stage 3 runs as it did.
	/// </summary>
	internal sealed partial class TrafficCars
	{
		/// <summary>How often this machine sends its states (seconds): 15 a second while it drives cars, 5 while it only says where its player is.</summary>
		private const double StatesEvery = 1.0 / 15.0, PlaceEvery = 1.0 / 5.0;

		/// <summary>A car this far from its driver (units) is handed to a player under half as far (see <see cref="TrafficRoom.HandTo"/>).</summary>
		private const double HandClose = 120.0;

		/// <summary>A drawn car held back in its space is sent to the room at most this often (seconds): each send holds it a second more.</summary>
		private const double HoldSendEvery = 0.5;

		private TrafficRoom room;
		/// <summary>Stand-ins of the cars other machines drive (each an enemy car made kinematic, played back from its driver's states).</summary>
		private readonly Dictionary<int, TrafficBody> remote = new Dictionary<int, TrafficBody>();
		private readonly List<TrafficRoom.State> outStates = new List<TrafficRoom.State>();
		private readonly Dictionary<int, double> holdSent = new Dictionary<int, double>();
		private readonly List<(int, TrafficRoom.Op)> opsOut = new List<(int, TrafficRoom.Op)>();
		/// <summary>Cars just retired as wrecks, whose stand-ins are to burn rather than go.</summary>
		private readonly HashSet<int> wreckedNow = new HashSet<int>();
		private double nextStates;
		private bool helloSent;
		/// <summary>A clock that runs on fixed steps (kept to real time), for the stand-ins' playback in FixedUpdate.</summary>
		private double fixedClock = double.NaN;
		/// <summary>Counts for the overlay: cars handed in and out, stand-ins made.</summary>
		private int handedIn, handedOut, standInsMade;

		/// <summary>The room's clock for the stand-ins (this machine's real time, in fixed steps).</summary>
		internal double FixedClock => double.IsNaN(fixedClock) ? EventLink.Clock() : fixedClock;

		/// <summary>The room's messages over HalfGripTools' <c>ctx.Net</c> (alone: this machine is the host and nothing is sent).</summary>
		private sealed class Link : TrafficRoom.ILink
		{
			public bool IsHost => !EventLink.InRoom || HalfGripTools.Net.IsHost;

			public ulong Local => EventLink.InRoom ? HalfGripTools.Net.LocalId : 1UL;

			public void ToHost(byte[] data) => EventLink.TrafficToHost(data);

			public void ToAll(byte[] data, bool reliable) => EventLink.TrafficToAll(data, reliable);

			public void ToOne(ulong to, byte[] data) => EventLink.TrafficToOne(to, data);
		}

		/// <summary>The room, made once the day is built (so a grant always finds the day).</summary>
		private void MakeRoom(int seed)
		{
			room = new TrafficRoom(seed, new Link());
			room.DriverChanged = DriverChanged;
			room.LedgerChanged = (car, op) =>
			{
				late.Apply(car, op);
				if (op.Kind == TrafficRoom.OpKind.Retire && op.D == TrafficRoom.Wrecked)
				{
					// Told before the driver's change: its stand-in here burns where it is.
					wreckedNow.Add(car);
				}
				if (op.Kind != TrafficRoom.OpKind.None && !room.IsHost)
				{
					Mod.Info("Traffic: car " + car + " " + op);
				}
			};
			room.Damaged = (car, amount) =>
			{
				if (physical.TryGetValue(car, out TrafficBody b) && b != null)
				{
					b.Damage(amount);
				}
			};
			room.StateCame = StateCame;
			room.LastOp = LastOp;
			// States held while the day was built are stale; the rest (grants, entries) are read in order.
			TrafficRoom.DropStates();
			Mod.Info("Traffic: " + (EventLink.InRoom ? (room.IsHost ? "the room's host, settling who drives each car" : "in a room, the host settles who drives each car") : "alone, this machine drives every car"));
		}

		/// <summary>Each frame, before the traffic's step: the room's messages read, this machine's player and clock told, the states sent.</summary>
		private void Pump()
		{
			if (room == null)
			{
				return;
			}
			double now = EventLink.Clock();
			bool inRoom = EventLink.InRoom;
			room.HostId = inRoom ? HalfGripTools.Net.HostId : room.Local;
			double localW = LocalSim();
			List<(ulong from, byte[] data, int player)> came = TrafficRoom.Take();
			if (came != null)
			{
				foreach ((ulong from, byte[] data, int player) in came)
				{
					if (player > 0)
					{
						room.PlayerJoined(from);
					}
					else if (player < 0)
					{
						room.PlayerLeft(from);
						Mod.Info("Traffic: " + EventLink.Name(from) + " left: their cars go back on their timetables");
					}
					else
					{
						room.Receive(from, data, now, localW);
					}
				}
			}
			if (inRoom && !room.IsHost && !helloSent)
			{
				helloSent = true;
				room.Hello();
			}
			CarControllerNew car = tm.playerCarController;
			if (car != null)
			{
				Origin.World(car.transform.position, out double px, out double pz);
				room.Me(px, pz, now);
				if (inRoom && now >= nextStates)
				{
					nextStates = now + (physical.Count > 0 ? StatesEvery : PlaceEvery);
					room.SendStates(now, localW, px, pz, States());
				}
			}
			room.Tick(now, Time.unscaledDeltaTime);
		}

		private void FixedUpdate()
		{
			double real = EventLink.Clock();
			fixedClock = double.IsNaN(fixedClock) || Math.Abs(fixedClock + Time.fixedDeltaTime - real) > 0.25 ? real : fixedClock + Time.fixedDeltaTime;
		}

		/// <summary>This machine's physics cars' states, in world coordinates.</summary>
		private List<TrafficRoom.State> States()
		{
			outStates.Clear();
			foreach (KeyValuePair<int, TrafficBody> kv in physical)
			{
				TrafficBody b = kv.Value;
				if (b == null)
				{
					continue;
				}
				Vector3 p = b.Rb.position;
				Origin.World(p, out double x, out double z);
				Quaternion q = b.Rb.rotation;
				Vector3 v = b.Rb.linearVelocity;
				outStates.Add(new TrafficRoom.State
				{
					Car = kv.Key,
					Gen = room.GenOf(kv.Key),
					Leg = b.Leg,
					X = (float)x,
					Y = p.y,
					Z = (float)z,
					Qx = q.x,
					Qy = q.y,
					Qz = q.z,
					Qw = q.w,
					Vx = v.x,
					Vy = v.y,
					Vz = v.z,
					P = (float)b.P,
					Health = Mathf.Clamp(b.Health, 0f, 100f),
					// A loose car is stopped (a wall, not a mover), with its leg -1 when parked.
					Flags = (byte)((b.Alive ? TrafficRoom.FlagAlive : 0) | (b.Hurt ? TrafficRoom.FlagHurt : 0) | (b.Hold || b.GaveUp || b.Loose ? TrafficRoom.FlagStopped : 0)
						| (b.Loose ? TrafficRoom.FlagLoose : 0) | (b.Touched ? TrafficRoom.FlagTouched : 0)),
				});
			}
			return outStates;
		}

		/// <summary>
		/// The room's word on a car's driver. This machine: a physics car (its stand-in taken over when it was handed here, else from where
		/// it is drawn; if it can't be, given straight back). Another: a stand-in (shown once its first state comes). Nobody: drawn again
		/// (a burning stand-in left to explode; one this machine is handing on carries on until the grant comes).
		/// </summary>
		private void DriverChanged(int car, ulong driver, ulong was)
		{
			if (day == null)
			{
				return;
			}
			if (driver == room.Local)
			{
				if (physical.ContainsKey(car))
				{
					return;
				}
				if (remote.TryGetValue(car, out TrafficBody s))
				{
					if (s == null || !s.Alive || !s.gameObject.activeSelf || s.Me.Leg < 0 || s.Loose || MostPhysics == 0)
					{
						// Can't take it over here: back on its timetable from where its stand-in is.
						TrafficRoom.Op back = LastOp(car);
						remote.Remove(car);
						DropStandIn(s);
						room.Release(car, back);
						return;
					}
					remote.Remove(car);
					s.FromRemote(s.Me.Leg, s.Me.P);
					physical[car] = s;
					madePhysical++;
					handedIn++;
					Mod.Info("Traffic: car " + car + " handed here by " + EventLink.Name(was) + " (" + physical.Count + " driven here)");
					return;
				}
				// Claimed to be loose (the player about to touch it), or to drive.
				bool asLoose = looseWanted.Remove(car);
				if (!(asLoose ? BecomeLoose(car, SimNow()) : Become(car, SimNow())))
				{
					room.Release(car, default);
				}
				return;
			}
			if (driver != 0UL)
			{
				if (physical.TryGetValue(car, out TrafficBody b))
				{
					// The host gave it to another (this machine's hand-over crossed a grant): a stand-in from now on.
					physical.Remove(car);
					if (b != null)
					{
						b.ToRemote();
						remote[car] = b;
					}
					return;
				}
				if (!remote.ContainsKey(car))
				{
					TrafficBody n = TakeBody(car);
					if (n == null)
					{
						return;
					}
					n.BeginRemote(this, car);
					remote[car] = n;
					standInsMade++;
					Mod.Info("Traffic: car " + car + " driven by " + EventLink.Name(driver) + ": a stand-in here (" + remote.Count + " now)");
				}
				return;
			}
			if (room.Handing(car))
			{
				return;
			}
			if (remote.TryGetValue(car, out TrafficBody r))
			{
				remote.Remove(car);
				if (wreckedNow.Contains(car) && r != null && r.Alive && r.gameObject.activeSelf)
				{
					r.ShowWreck();
				}
				DropStandIn(r);
			}
			wreckedNow.Remove(car);
			if (physical.TryGetValue(car, out TrafficBody own))
			{
				// Taken back by the host (it never is in a working room: a guard).
				physical.Remove(car);
				if (own != null)
				{
					Release(own);
				}
			}
		}

		/// <summary>A stand-in no longer needed: a burning one left to explode where it is, a hurt one destroyed (its smoke can't be stopped), the rest back to the pool.</summary>
		private void DropStandIn(TrafficBody b)
		{
			if (b == null)
			{
				return;
			}
			if (!b.Alive)
			{
				wrecks.Add(b);
			}
			else if (b.Hurt)
			{
				Destroy(b.gameObject);
			}
			else
			{
				Release(b);
			}
		}

		/// <summary>A state of a car another machine drives: into its stand-in's playback (shown at its first).</summary>
		private void StateCame(TrafficRoom.State s)
		{
			if (!remote.TryGetValue(s.Car, out TrafficBody b) || b == null)
			{
				return;
			}
			b.Play.Add(s, EventLink.Clock());
			if (!b.gameObject.activeSelf)
			{
				b.Place(Origin.Local(s.X, s.Y, s.Z), new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw));
			}
		}

		/// <summary>On the host, a player left: the entry each of their cars goes back with, from its stand-in's newest state (retired if it was hurt or off its road).</summary>
		private TrafficRoom.Op LastOp(int car)
		{
			double w = SimNow();
			if (!remote.TryGetValue(car, out TrafficBody b) || b == null || b.Play.Count == 0)
			{
				return default;
			}
			TrafficRoom.State s = b.Play.Newest;
			if (s.Loose && s.Alive && !s.Hurt && !s.Touched)
			{
				// An untouched loose car: drawn as the day has it again.
				return default;
			}
			if (!s.Alive || s.Hurt || s.Leg < 0 || s.Leg >= day.Legs.Count)
			{
				return new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w };
			}
			TrafficDay.Leg leg = day.Legs[s.Leg];
			if (!TrafficDrive.OnLane(leg, s.P, s.X, s.Z, 2f * (s.Qx * s.Qz + s.Qw * s.Qy), 1f - 2f * (s.Qx * s.Qx + s.Qy * s.Qy)))
			{
				return new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w };
			}
			return LateOp(s.Leg, s.P, w);
		}

		/// <summary>The entry a car going back on its timetable at <paramref name="p"/> along leg <paramref name="leg"/> takes: late (or early) by where it is, on time if by almost nothing.</summary>
		private TrafficRoom.Op LateOp(int leg, double p, double w)
		{
			double d = TrafficDrive.Lateness(day.Legs[leg], p, w);
			// Even a little lateness (or earliness) is kept: at speed half a second is a jump to see.
			if (Math.Abs(d) < 0.05)
			{
				return new TrafficRoom.Op { Kind = TrafficRoom.OpKind.OnTime, Leg = -1, W = w };
			}
			return new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Late, Leg = leg, W = w, D = d };
		}

		/// <summary>A hit on a stand-in (another machine drives the car): sent to its driver.</summary>
		internal void RemoteHit(int car, float amount)
		{
			room?.Damage(car, amount);
		}

		/// <summary>A drawn car held back in its space here: sent to the room (at most every <see cref="HoldSendEvery"/>), so every machine holds it.</summary>
		private void SendHold(int car, int leg, double w, double d)
		{
			if (room == null || !EventLink.InRoom)
			{
				return;
			}
			double now = EventLink.Clock();
			if (holdSent.TryGetValue(car, out double at) && now - at < HoldSendEvery)
			{
				return;
			}
			holdSent[car] = now;
			opsOut.Clear();
			opsOut.Add((car, new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Late, Leg = leg, W = w, D = d }));
			room.Ledger(opsOut);
		}

		/// <summary>A late car's lateness cut here (<see cref="TrafficLate.SkipStand"/>): sent to the room, so every machine draws it so.</summary>
		private void SendLate(int car, int leg, double w, double d)
		{
			if (room == null || !EventLink.InRoom)
			{
				return;
			}
			opsOut.Clear();
			opsOut.Add((car, Math.Abs(d) < TrafficLate.Least ? new TrafficRoom.Op { Kind = TrafficRoom.OpKind.OnTime, Leg = -1, W = w } : new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Late, Leg = leg, W = w, D = d }));
			room.Ledger(opsOut);
		}

		/// <summary>On the host in a room: the cars it caught up in the background, sent so every machine puts them back on the day.</summary>
		private void SendCaughtUp(List<int> cars, double w)
		{
			if (room == null || !EventLink.InRoom || !room.IsHost || cars.Count == 0)
			{
				return;
			}
			opsOut.Clear();
			foreach (int car in cars)
			{
				opsOut.Add((car, new TrafficRoom.Op { Kind = TrafficRoom.OpKind.OnTime, Leg = -1, W = w }));
			}
			room.Ledger(opsOut);
		}

		/// <summary>Whether another player (on the host in a room) is within <paramref name="reach"/> of a place, for catching up.</summary>
		private bool OthersNear(double x, double z, float reach)
		{
			return room != null && EventLink.InRoom && room.IsHost && room.OthersNear(x, z, reach, EventLink.Clock());
		}

		/// <summary>Every stand-in back (between races, or the traffic switched off).</summary>
		private void ClearStandIns()
		{
			foreach (TrafficBody b in remote.Values)
			{
				if (b != null)
				{
					Destroy(b.gameObject);
				}
			}
			remote.Clear();
		}

		/// <summary>The overlay's room part.</summary>
		private string RoomStatus()
		{
			if (room == null || !EventLink.InRoom)
			{
				return "";
			}
			return "; room: " + (room.IsHost ? "host" : "clock " + (room.ClockKnown ? (room.Offset >= 0 ? "+" : "") + room.Offset.ToString("0.00") + " s" : "not heard")) + ", " + room.PlayersKnown + " players heard, stand-ins "
				+ remote.Count + ", claims " + room.Claims + " (granted " + room.Granted + ", lost " + room.GaveUp + "), handed in " + handedIn + " and out " + handedOut + ", states in " + room.StatesIn + " (" + room.StatesStale + " stale)";
		}
	}
}
