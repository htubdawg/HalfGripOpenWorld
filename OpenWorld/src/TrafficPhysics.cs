using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Stage 3 of the traffic plan, the physics cars: within <see cref="PhysicsReach"/> of the player a driving car of the day stops being
	/// a drawn body and becomes the game's own enemy car (its physics, damage, smoke and wreck) with our driver
	/// (<see cref="TrafficBody"/>, its arithmetic in <see cref="TrafficDrive"/>): it follows its leg, queues behind the car in front, gives
	/// way at junctions, can be pushed, shot and wrecked. Past <see cref="PhysicsLeave"/>, or on the last stretch of its trip, it goes back
	/// on its timetable through the lateness ledger (<see cref="TrafficLate"/>), late by what held it up, so no traffic is used up; a
	/// wreck, or a car knocked off its road, is retired from the day.
	///
	/// In a room (stage 4, the room's part of this class in <c>TrafficShared.cs</c>, and <see cref="TrafficRoom"/>) a car is made physical only once the
	/// room's host grants it to this machine (alone, at once), goes back through the room (its entry sent), and is handed to another
	/// player who is clearly nearer to it; cars other machines drive are stand-ins here.
	/// </summary>
	internal sealed partial class TrafficCars
	{
		/// <summary>A driving car this near the player (units, about 90 m) becomes a physics car; one this far goes back on its timetable.</summary>
		private const float PhysicsReach = 300f, PhysicsLeave = 400f;

		private readonly Dictionary<int, TrafficBody> physical = new Dictionary<int, TrafficBody>();
		private readonly Stack<TrafficBody> bodySpare = new Stack<TrafficBody>();
		private readonly List<TrafficBody> wrecks = new List<TrafficBody>();
		private readonly List<TrafficBody> bodyList = new List<TrafficBody>();
		private readonly List<int> leaving = new List<int>();
		private readonly Dictionary<(int, int), CarSkin> skins = new Dictionary<(int, int), CarSkin>();
		/// <summary>The drawn moving cars this frame (for the physics cars' drivers), then with the physics cars, prepared.</summary>
		private readonly List<TrafficDrive.Mover> drawnMovers = new List<TrafficDrive.Mover>();
		internal readonly List<TrafficDrive.Mover> Movers = new List<TrafficDrive.Mover>();
		internal readonly List<TrafficDrive.Obstacle> Obstacles = new List<TrafficDrive.Obstacle>();
		private GameObject bodyHolder;
		private bool physicsFailed;
		/// <summary>Counts for the log and the overlay: made physical, gone back on time, gone back late, retired (wrecked or knocked off), hidden for being where a physics car is.</summary>
		private int madePhysical, backOnTime, backLate, retiredWrecked, retiredOff, hiddenByPhysics, heldBack;

		internal TrafficDay Day => day;

		/// <summary>The day's clock now (for the drivers, in FixedUpdate).</summary>
		internal double Now => day != null && tm != null ? SimNow() : 0.0;

		private int MostPhysics => physicsFailed ? 0 : Math.Max(0, Mod.TrafficPhysics.Value);

		/// <summary>Contact physics: at most this many loose cars (drawn cars made real a moment before the player touches them), apart from <see cref="MostPhysics"/>.</summary>
		private int MostLoose => physicsFailed ? 0 : Math.Max(0, Mod.TrafficLoose.Value);

		/// <summary>An untouched loose car goes back to being drawn once the player is this far (units, about 18 m); a touched one is retired once the player is this far (about 150 m, past where parked cars are drawn).</summary>
		private const double LooseLeave = 60.0, LooseGone = 500.0;

		/// <summary>The player's car as a box for the touch test (half length and half width, units: the game's cars are about 14 by 5.3).</summary>
		private const double PlayerHalfLength = 7.5, PlayerHalfWidth = 2.7;

		/// <summary>
		/// The touch test's margin (units). Small: a car driving down a narrow street passes the cars at its kerb about half a unit off
		/// (lane middle to a kerb car's 5.8), and must not make each one loose; the sweep along the velocity gives the warning ahead.
		/// </summary>
		private const double TouchMargin = 0.2;

		/// <summary>Cars claimed to be loose, not yet granted (on a room's client the grant is a round trip away).</summary>
		private readonly HashSet<int> looseWanted = new HashSet<int>();
		private readonly List<int> looseDrop = new List<int>();
		/// <summary>Counts for the overlay: loose cars made, gone back untouched, retired touched.</summary>
		private int madeLoose, looseBack, looseRetired;
		/// <summary>The frame a physics car's body was last made (at most one a frame: each is an Instantiate and the game's Awakes).</summary>
		private int madeFrame = -1;

		/// <summary>How many of the physics cars here are loose.</summary>
		private int LooseCount
		{
			get
			{
				int n = 0;
				foreach (TrafficBody b in physical.Values)
				{
					n += b != null && b.Loose ? 1 : 0;
				}
				return n;
			}
		}

		/// <summary>Whether car <paramref name="c"/> is a physics car now, here or another machine's stand-in (not drawn by stage 2).</summary>
		private bool IsPhysical(int c) => physical.ContainsKey(c) || (remote.TryGetValue(c, out TrafficBody s) && s != null && s.gameObject.activeSelf);

		/// <summary>Whether any physics car (here or a stand-in) is about.</summary>
		private bool AnyBodies => physical.Count > 0 || remote.Count > 0;

		/// <summary>What the bubble does with a physics car after looking at them all (the room is told after the car has left the list).</summary>
		private enum Leave : byte { Back, Retire, Hand }

		private readonly List<(int car, TrafficBody body, Leave what, TrafficRoom.Op op, ulong to)> acts = new List<(int, TrafficBody, Leave, TrafficRoom.Op, ulong)>();

		/// <summary>
		/// The bubble's going back, done before the choice: each physics car that is out of reach or on the last stretch of its trip goes
		/// back on its timetable if it can (upright, on its lane, undamaged), late by where it is on its leg; otherwise it stays (stopped)
		/// until the player is out of reach, then is retired. One driving into a tunnel goes on into its dark and goes back there, out of
		/// sight, early or late as it is (or is retired there if it is damaged): never braking or waiting at the mouth.
		/// </summary>
		/// <remarks>
		/// With <paramref name="darkOnly"/> only the cars in a tunnel's dark are looked at: <see cref="TrafficCars"/> does that every frame
		/// between the choices, since at speed a car is through the tunnel's few units of dark well inside one choice's interval.
		/// </remarks>
		private void Bubble(double w, double cx, double cz, bool darkOnly = false)
		{
			wrecks.RemoveAll(b => b == null);
			if (physical.Count == 0)
			{
				return;
			}
			acts.Clear();
			double now = EventLink.Clock();
			bool others = EventLink.InRoom && room.PlayersKnown > 1;
			foreach (KeyValuePair<int, TrafficBody> kv in physical)
			{
				TrafficBody b = kv.Value;
				if (b == null)
				{
					acts.Add((kv.Key, null, Leave.Back, default, 0UL));
					continue;
				}
				if (darkOnly && (b.Loose || !TrafficDrive.InDark(day.Legs[b.Leg], b.P)))
				{
					continue;
				}
				if (b.Loose)
				{
					LooseBubble(kv.Key, b, w, cx, cz);
					continue;
				}
				TrafficDay.Leg leg = day.Legs[b.Leg];
				Origin.World(b.Rb.position, out double x, out double z);
				double dx = x - cx, dz = z - cz;
				bool far = dx * dx + dz * dz > (double)PhysicsLeave * PhysicsLeave || MostPhysics == 0;
				bool end = !leg.Vanishes && b.P > leg.Length - TrafficDrive.EndZone;
				bool tunnel = TrafficDrive.NearTunnel(leg, b.P + 20.0);
				bool dark = TrafficDrive.InDark(leg, b.P);
				if (others && b.Alive && !end && !tunnel && MostPhysics > 0)
				{
					// Another player clearly nearer (or within reach when this one is past it) drives it from here on: only they have the
					// ground under it.
					ulong to = room.HandTo(x, z, now, PhysicsReach, HandClose);
					if (to != 0UL)
					{
						acts.Add((kv.Key, b, Leave.Hand, default, to));
						continue;
					}
				}
				if (!far && !end && !dark)
				{
					b.Hold = false;
					continue;
				}
				Vector3 f = b.transform.forward;
				// In a tunnel's dark nobody sees it go, so it goes back however it is driving (a wreck is retired below).
				bool fit = b.Alive && !b.Hurt && (dark || (!b.GaveUp && b.transform.up.y > 0.7f && TrafficDrive.OnLane(leg, b.P, x, z, f.x, f.z)));
				if (fit)
				{
					double d = TrafficDrive.Lateness(leg, b.P, w);
					// On the last stretch it waits to be on time rather than be drawn jumping ahead to where the day has it. In a tunnel it
					// goes now, drawn where it is (early is a negative lateness).
					if (!far && !dark && d < -1.0)
					{
						continue;
					}
					if (d >= TrafficLate.Least)
					{
						backLate++;
					}
					else
					{
						backOnTime++;
					}
					Mod.Info("Traffic: car " + kv.Key + " back on its timetable " + (d >= TrafficLate.Least ? d.ToString("0.0") + " s late" : "on time") + (far ? " (out of reach)" : end ? " (its trip's end)" : " (in a tunnel)"));
					acts.Add((kv.Key, b, Leave.Back, LateOp(b.Leg, b.P, w), 0UL));
					continue;
				}
				if (!far && !dark)
				{
					// Can't go back here: it stops and waits for the player to go.
					b.Hold = true;
					continue;
				}
				retiredOff++;
				Mod.Info("Traffic: car " + kv.Key + " retired (" + (!b.Alive ? "wrecked" : b.Hurt ? "damaged" : "off its road") + ")");
				acts.Add((kv.Key, b, Leave.Retire, new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w }, 0UL));
			}
			// Off the list first, then the room told (its word comes back to DriverChanged, which must not find them).
			foreach ((int car, TrafficBody b, Leave what, TrafficRoom.Op op, ulong to) in acts)
			{
				physical.Remove(car);
				if (b == null)
				{
					room.Release(car, default);
					continue;
				}
				switch (what)
				{
					case Leave.Hand:
						b.ToRemote();
						remote[car] = b;
						handedOut++;
						Mod.Info("Traffic: car " + car + " handed to " + EventLink.Name(to) + " (nearer to it)");
						room.Hand(car, to);
						break;
					case Leave.Retire:
						if (b.Hurt)
						{
							Destroy(b.gameObject);
						}
						else
						{
							Release(b);
						}
						room.Release(car, op);
						break;
					default:
						Release(b);
						room.Release(car, op);
						break;
				}
			}
		}

		/// <summary>
		/// The bubble for a loose car: untouched with the player <see cref="LooseLeave"/> away (or loose cars switched off), back to being
		/// drawn (a parked one as it was; a moving one late by the time it stood); touched, it stays where it was knocked until the player is
		/// <see cref="LooseGone"/> away, then is retired (owner decision 1's default: its household goes without it for the rest of the race).
		/// Never handed to another player (it is the one who touched it that has it, and it is gone soon).
		/// </summary>
		private void LooseBubble(int car, TrafficBody b, double w, double cx, double cz)
		{
			Origin.World(b.Rb.position, out double x, out double z);
			double dx = x - cx, dz = z - cz, d2 = dx * dx + dz * dz;
			if (!b.Touched && !b.Hurt && b.Alive)
			{
				if (d2 < LooseLeave * LooseLeave && MostLoose > 0)
				{
					return;
				}
				if (LooseBack(car, b, x, z, w, out TrafficRoom.Op op))
				{
					looseBack++;
					Mod.Info("Traffic: loose car " + car + " back as it was (" + (op.Kind == TrafficRoom.OpKind.Late ? op.D.ToString("0.0") + " s late" : "untouched") + ")");
					acts.Add((car, b, Leave.Back, op, 0UL));
					return;
				}
				// Stood still off its lane (it braked across a junction, say): it can't be drawn from here, so it is treated as knocked.
				b.Touched = true;
			}
			if (d2 < LooseGone * LooseGone)
			{
				return;
			}
			looseRetired++;
			Mod.Info("Traffic: loose car " + car + " retired (" + (!b.Alive ? "wrecked" : b.Hurt ? "damaged" : "knocked") + ")");
			acts.Add((car, b, Leave.Retire, new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w }, 0UL));
		}

		/// <summary>
		/// The entry an untouched loose car goes back to being drawn with. Parked: none, it is still in its space, unless the day had it set
		/// off meanwhile (then it sets off now, late by that). Moving: late by the time it stood; along its lane by where it stopped, or,
		/// pulling in or out (off its lane), from where on its leg it was made. False if it stopped where it can't be drawn.
		/// </summary>
		private bool LooseBack(int car, TrafficBody b, double x, double z, double w, out TrafficRoom.Op op)
		{
			op = default;
			if (b.LooseLeg < 0)
			{
				TrafficDay.Place now = late.At(car, w, out bool _);
				if (now.State == TrafficDay.State.Driving && now.Leg >= 0)
				{
					TrafficDay.Leg l = day.Legs[now.Leg];
					op = new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Late, Leg = now.Leg, W = w, D = TrafficDrive.LegClock(l, w) - l.Start };
				}
				return true;
			}
			TrafficDay.Leg leg = day.Legs[b.LooseLeg];
			if (b.LoosePulling)
			{
				op = new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Late, Leg = b.LooseLeg, W = w, D = TrafficDrive.LegClock(leg, w) - b.LooseT };
				return true;
			}
			double p = TrafficDrive.Project(leg, b.P, x, z, out double side, 15.0, 40.0);
			Vector3 f = b.transform.forward;
			if (side > 8.0 || b.transform.up.y < 0.7f || !TrafficDrive.OnLane(leg, p, x, z, f.x, f.z))
			{
				return false;
			}
			op = LateOp(b.LooseLeg, p, w);
			return true;
		}

		/// <summary>
		/// Contact physics, each frame after the drawing: a drawn car (parked, or moving and not a physics car just then) that the player's
		/// car, swept along its velocity for a moment, would meet becomes a loose car (claimed through the room like any physics car; alone
		/// or on the host at once). On a room's client the sweep looks further ahead, for the grant's round trip.
		/// </summary>
		private void Contact(double w)
		{
			if (looseWanted.Count > 0)
			{
				looseDrop.Clear();
				foreach (int c in looseWanted)
				{
					if (!room.Claiming(c) && !physical.ContainsKey(c))
					{
						looseDrop.Add(c);
					}
				}
				foreach (int c in looseDrop)
				{
					looseWanted.Remove(c);
				}
			}
			int most = MostLoose;
			if (most == 0 || tm.enemyCarPrefab == null || madeFrame == Time.frameCount)
			{
				return;
			}
			CarControllerNew player = tm.playerCarController;
			Rigidbody prb = player != null ? player.GetComponent<Rigidbody>() : null;
			if (prb == null)
			{
				return;
			}
			int count = LooseCount + looseWanted.Count;
			if (count >= most)
			{
				return;
			}
			Vector3 pp = prb.position, pv = prb.linearVelocity, pf = player.transform.forward;
			float pfl = Mathf.Sqrt(pf.x * pf.x + pf.z * pf.z);
			if (pfl < 1e-3f)
			{
				return;
			}
			Origin.World(pp, out double px, out double pz);
			double ahead = EventLink.InRoom && !room.IsHost ? 0.5 : 0.3;
			double speed = Math.Sqrt(pv.x * pv.x + pv.z * pv.z);
			double near = 30.0 + speed * ahead;
			double now = EventLink.Clock();
			// A grant on the host comes back before Claim returns (BecomeLoose): the chosen list is put right after the walk.
			choosing = true;
			try
			{
				TouchWalk(px, pz, pp, pv, pf.x / pfl, pf.z / pfl, ahead, near, now, count, most);
			}
			finally
			{
				choosing = false;
			}
			for (int k = chosen.Count - 1; k >= 0; k--)
			{
				if (physical.ContainsKey(chosen[k]))
				{
					chosenMoving.Remove(chosen[k]);
					chosen.RemoveAt(k);
				}
			}
		}

		private void TouchWalk(double px, double pz, Vector3 pp, Vector3 pv, double fx, double fz, double ahead, double near, double now, int count, int most)
		{
			foreach (int c in chosen)
			{
				if (!live.TryGetValue(c, out View v) || !Drawn(v) || IsPhysical(c) || looseWanted.Contains(c))
				{
					continue;
				}
				Vector3 at = v.Root.position;
				if (Mathf.Abs(at.y - pp.y) > 8f)
				{
					continue;
				}
				Origin.World(at, out double vx, out double vz);
				double dx = vx - px, dz = vz - pz;
				if (dx * dx + dz * dz > near * near)
				{
					continue;
				}
				// The drawn car's box (its body's, as its collider), in the world.
				Vector3 h = v.Root.forward;
				float hl = Mathf.Sqrt(h.x * h.x + h.z * h.z);
				if (hl < 1e-3f)
				{
					continue;
				}
				Vector3 mid = v.Root.TransformPoint(v.Look.Box.center);
				Origin.World(mid, out double mx, out double mz);
				Vector3 e = Vector3.Scale(v.Look.Box.extents, v.Root.lossyScale);
				if (!TrafficDrive.Sweeps(px, pz, fx, fz, PlayerHalfLength, PlayerHalfWidth, pv.x, pv.z, ahead, 2.0, mx, mz, h.x / hl, h.z / hl, e.z, e.x, TouchMargin))
				{
					continue;
				}
				if (room.DriverOf(c) != 0UL || room.Claiming(c) || room.Handing(c))
				{
					continue;
				}
				looseWanted.Add(c);
				if (!room.Claim(c, now))
				{
					looseWanted.Remove(c);
					continue;
				}
				if (physicsFailed || ++count >= most || madeFrame == Time.frameCount)
				{
					return;
				}
			}
		}

		/// <summary>
		/// Car <paramref name="c"/>, granted to this machine as a loose car, becomes one exactly where and as it is drawn: parked at rest on its
		/// brakes, or moving at its drawn speed and braking to a stop. False if it can't be (not drawn here now, hidden, away).
		/// </summary>
		private bool BecomeLoose(int c, double w)
		{
			if (MostLoose == 0 || tm.enemyCarPrefab == null || !live.TryGetValue(c, out View v) || !Drawn(v))
			{
				return false;
			}
			TrafficDay.Place at = late.At(c, w, out bool hidden);
			bool moving = at.State == TrafficDay.State.Driving && at.Leg >= 0;
			if (hidden || (moving && at.InTunnel) || (!moving && at.State != TrafficDay.State.Parked))
			{
				return false;
			}
			TrafficBody b = TakeBody(c);
			if (b == null)
			{
				return false;
			}
			PlaceAsDrawn(b, v, 0.15f);
			Vector3 velocity = Vector3.zero;
			bool pulling = false;
			double legT = 0.0;
			if (moving)
			{
				TrafficDay.Leg leg = day.Legs[at.Leg];
				pulling = (!leg.Appears && at.T < leg.PullOut) || (!leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn);
				legT = leg.Start + at.T;
				Vector3 f = v.Root.forward;
				f.y = 0f;
				velocity = f.normalized * (float)Math.Max(0.0, at.Speed);
			}
			b.BeginLoose(this, c, moving ? at.Leg : -1, moving ? at.P : 0.0, legT, pulling, w, velocity);
			physical[c] = b;
			madeLoose++;
			Mod.Info("Traffic: car " + c + " loose (" + (moving ? "moving at " + (at.Speed / 3.3 * 3.6).ToString("0") + " km/h" : "parked") + "), the player about to touch it");
			int k = chosen.IndexOf(c);
			if (k >= 0 && !choosing)
			{
				chosen.RemoveAt(k);
			}
			if (!choosing)
			{
				chosenMoving.Remove(c);
			}
			v.Go.SetActive(false);
			v.Far = false;
			return true;
		}

		/// <summary>How high each body model stands over the ground at rest on its springs (its root over the ground under it): a sum and a count, from the physics cars seen standing or crawling.</summary>
		private readonly Dictionary<int, (double sum, int n)> restHeight = new Dictionary<int, (double, int)>();
		/// <summary>A model's rest height is used once this many standing cars have given it (until then a small drop).</summary>
		private const int RestKnown = 3, RestMost = 40;

		/// <summary>
		/// Learns each model's rest height (every 0.4 s, at the choice): a physics car of this machine's standing or crawling upright on the
		/// ground, a second or more after it was placed, gives its root's height over the ground under it. A body put down at that height
		/// (<see cref="RestPlace"/>) doesn't drop onto its springs and bounce, which a parked car turning loose did (the owner, 2026-11-23).
		/// </summary>
		private void LearnRest()
		{
			foreach (TrafficBody b in physical.Values)
			{
				if (b == null || !b.Alive || b.Remote || b.Loose || b.Hurt || b.Age < 1.2f || b.Rb.linearVelocity.sqrMagnitude > 9f || b.Rb.angularVelocity.sqrMagnitude > 0.04f || b.transform.up.y < 0.97f)
				{
					continue;
				}
				Vector3 p = b.Rb.position;
				if (!Ground(p, p.y + 3f, out float gy))
				{
					continue;
				}
				double h = p.y - gy;
				if (h < -3.0 || h > 6.0)
				{
					continue;
				}
				restHeight.TryGetValue(b.LookIndex, out (double sum, int n) r);
				if (r.n >= RestMost)
				{
					// A running mean of the last few dozen.
					r.sum -= r.sum / r.n;
					r.n--;
				}
				r.sum += h;
				r.n++;
				restHeight[b.LookIndex] = r;
				if (r.n == RestKnown)
				{
					// The worked-out rest beside what the game's physics did (on tarmac they should agree to a hundredth or so; the verges, the
					// ground and the tracks sit 0.1 lower).
					Rest rest = looks[b.LookIndex].Rest;
					Mod.Info("Traffic: body model " + b.LookIndex + " stands " + (r.sum / r.n).ToString("0.00") + " over the ground on its springs"
						+ (rest != null && rest.Ok ? " (worked out: " + rest.Height.ToString("0.00") + ")" : " (cars are put down there from now on)"));
				}
			}
		}

		/// <summary>
		/// Puts body <paramref name="b"/> where drawn car <paramref name="v"/> is. With the rest worked out (<see cref="Rest"/>) the drawn car
		/// already stands exactly as the physics car does at rest, so the body goes to the same pose with its springs and wheels at rest too:
		/// nothing drops, bounces or moves. Without it, as before: a little up (<paramref name="drop"/>) or at the learned height.
		/// </summary>
		private void PlaceAsDrawn(TrafficBody b, View v, float drop)
		{
			Rest rest = v.Look.Rest;
			if (rest != null && rest.Ok)
			{
				b.Place(v.Root.position, v.Root.rotation);
				b.Settle(rest);
				return;
			}
			b.Place(RestPlace(v.Root.position, b.LookIndex, drop), v.Root.rotation);
		}

		/// <summary>Where to put a body of model <paramref name="look"/> standing in for a drawn car at <paramref name="drawn"/>: over the ground at its rest height once that is known, else <paramref name="drop"/> up.</summary>
		private Vector3 RestPlace(Vector3 drawn, int look, float drop)
		{
			if (restHeight.TryGetValue(look, out (double sum, int n) r) && r.n >= RestKnown && Ground(drawn, drawn.y + 3f, out float gy))
			{
				return new Vector3(drawn.x, gy + (float)(r.sum / r.n), drawn.z);
			}
			return drawn + Vector3.up * drop;
		}

		/// <summary>
		/// The bubble's making, after the choice: the nearest drawn driving cars within reach become physics cars, up to
		/// <see cref="MostPhysics"/>, each where and as fast as it is drawn. A car the day puts on top of (or right behind) a physics car is
		/// hidden instead, until it is out of sight. Not while it pulls out or in (the day draws that), in a junction's box, near a tunnel or on
		/// its last stretch.
		/// </summary>
		private void MakePhysical(double w, double cx, double cz)
		{
			if (MostPhysics == 0 || tm.enemyCarPrefab == null)
			{
				return;
			}
			choosing = true;
			try
			{
				ClaimNearest(w, cx, cz);
			}
			finally
			{
				choosing = false;
			}
		}

		private void ClaimNearest(double w, double cx, double cz)
		{
			double now = EventLink.Clock();
			for (int k = 0; k < chosen.Count; k++)
			{
				if (physical.Count - LooseCount + room.Claims - looseWanted.Count >= MostPhysics)
				{
					return;
				}
				int c = chosen[k];
				if (!chosenMoving.Contains(c) || !live.TryGetValue(c, out View v) || !Drawn(v) || double.IsNaN(v.LastX))
				{
					continue;
				}
				if (room.DriverOf(c) != 0UL || room.Claiming(c) || room.Handing(c))
				{
					continue;
				}
				if (!Drivable(c, w, out TrafficDay.Place at))
				{
					continue;
				}
				double dx = v.LastX - cx, dz = v.LastZ - cz;
				if (dx * dx + dz * dz > (double)PhysicsReach * PhysicsReach)
				{
					continue;
				}
				if (OnAPhysicsCar(v.LastX, v.LastZ, at.Hx, at.Hz, 20.0))
				{
					late.Hide(c, w);
					hiddenByPhysics++;
					chosen.RemoveAt(k);
					chosenMoving.Remove(c);
					k--;
					continue;
				}
				// In a room the player nearest the car drives it (only they have the ground under it): the host grants the first claim
				// (alone, at once: DriverChanged makes it physical before Claim returns).
				if (!room.Nearest(v.LastX, v.LastZ, now))
				{
					continue;
				}
				room.Claim(c, now);
				if (physicsFailed)
				{
					return;
				}
				if (physical.ContainsKey(c))
				{
					chosen.RemoveAt(k);
					chosenMoving.Remove(c);
					k--;
				}
			}
		}

		/// <summary>
		/// Whether drawn car <paramref name="c"/> may become a physics car now: driving, not hidden or in a tunnel, not pulling out or in (the
		/// day draws that), not on its last stretch, not near a tunnel, not in a junction's box (the day let it in; the drivers didn't, and
		/// might send another physics car across it).
		/// </summary>
		private bool Drivable(int c, double w, out TrafficDay.Place at)
		{
			at = late.At(c, w, out bool hidden);
			if (hidden || at.State != TrafficDay.State.Driving || at.InTunnel || at.Leg < 0)
			{
				return false;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			if ((!leg.Appears && at.T < leg.PullOut + 0.5) || (!leg.Vanishes && (leg.End - leg.Start - at.T < leg.PullIn + 3.0 || at.P > leg.Length - TrafficDrive.EndZone - 25.0)) || TrafficDrive.NearTunnel(leg, at.P))
			{
				return false;
			}
			TrafficDrive.Mover m = new TrafficDrive.Mover { Car = c, Leg = at.Leg, P = at.P };
			TrafficDrive.Prepare(day, ref m);
			return !((m.Node >= 0 && m.ToBox < TrafficDrive.CarLength * 0.5) || m.Back >= 0);
		}

		/// <summary>
		/// Car <paramref name="c"/>, granted to this machine, becomes a physics car where and as fast as it is drawn. False if it can't be
		/// (not drawn here now, or not drivable any more: the grant came a moment late), and the caller gives it back.
		/// </summary>
		private bool Become(int c, double w)
		{
			if (MostPhysics == 0 || tm.enemyCarPrefab == null || !live.TryGetValue(c, out View v) || !Drawn(v) || double.IsNaN(v.LastX))
			{
				return false;
			}
			if (!Drivable(c, w, out TrafficDay.Place at))
			{
				return false;
			}
			TrafficBody b = TakeBody(c);
			if (b == null)
			{
				return false;
			}
			Vector3 fwd = new Vector3((float)at.Hx, 0f, (float)at.Hz);
			PlaceAsDrawn(b, v, 0.4f);
			b.Begin(this, c, at.Leg, at.P, fwd.normalized * (float)Math.Max(0.0, at.Speed));
			physical[c] = b;
			madePhysical++;
			// Not drawn by stage 2 from now on (a grant that came between choices: off the chosen list too).
			int k = chosen.IndexOf(c);
			if (k >= 0 && !choosing)
			{
				chosen.RemoveAt(k);
			}
			if (!choosing)
			{
				chosenMoving.Remove(c);
			}
			if (live.TryGetValue(c, out View drawn))
			{
				drawn.Go.SetActive(false);
				drawn.Far = false;
			}
			return true;
		}

		/// <summary>True while <see cref="MakePhysical"/> walks the chosen list (it takes a car off itself).</summary>
		private bool choosing;

		/// <summary>Whether a car drawn at (x, z) facing (hx, hz) would be on top of a physics car, within <paramref name="behind"/> behind one or <paramref name="before"/> in front of one.</summary>
		private bool OnAPhysicsCar(double x, double z, double hx, double hz, double behind, double before = 0.0)
		{
			return OnABody(physical, x, z, hx, hz, behind, before) || OnABody(remote, x, z, hx, hz, behind, before);
		}

		private static bool OnABody(Dictionary<int, TrafficBody> bodies, double x, double z, double hx, double hz, double behind, double before)
		{
			foreach (TrafficBody b in bodies.Values)
			{
				if (b == null || !b.gameObject.activeSelf)
				{
					continue;
				}
				Origin.World(b.Rb.position, out double bx, out double bz);
				double dx = bx - x, dz = bz - z;
				double along = dx * hx + dz * hz, side = Math.Abs(dx * hz - dz * hx);
				Vector3 f = b.transform.forward;
				float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
				// In its lane close behind or ahead, or in it whichever way either faces (a car pulling in across the road, one passing the
				// nose of a physics car stopped in a junction). A loose car still in its space only if in it (cars drive past parked ones).
				if ((!b.InSpace && side < 4.0 && along > -TrafficLate.Gap - before && along < TrafficLate.Gap + behind) || TrafficDrive.Overlap(x, z, hx, hz, bx, bz, f.x / fl, f.z / fl, 0.5))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Whether a drawn car is in a physics car, or would run into one within <paramref name="behind"/> (the day doesn't know a physics car
		/// is held up, and with the most physics cars made, cars coming up behind one stay drawn): it is hidden until out of sight.
		/// </summary>
		private bool HideUnderPhysics(int c, double x, double z, double hx, double hz, double behind, double w)
		{
			if (!AnyBodies || !OnAPhysicsCar(x, z, hx, hz, behind))
			{
				return false;
			}
			late.Hide(c, w);
			hiddenByPhysics++;
			return true;
		}

		/// <summary>
		/// A drawn car about to pull out (or pulling out, or waiting at its trip's start to set off) where a physics car stands, or right in front of one: held back in its space a
		/// little longer (late by it), rather than drawn pulling out into it.
		/// </summary>
		internal bool HoldBackFromPhysics(int c, TrafficDay.Place at, double w)
		{
			if (at.Leg < 0 || !AnyBodies || at.State != TrafficDay.State.Driving)
			{
				return false;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			if (leg.Appears || (at.T >= leg.PullOut + 1.0 && at.P > 1.0) || !OnAPhysicsCar(at.X, at.Z, at.Hx, at.Hz, 25.0, 45.0))
			{
				return false;
			}
			double d = late.HoldBack(c, at.Leg, w, 1.0);
			SendHold(c, at.Leg, w, d);
			heldBack++;
			return true;
		}

		/// <summary>
		/// The movers the drivers look at, every frame: the drawn driving cars (where the day has them) and the physics cars (where they are
		/// on their legs), each prepared; and what is in the way with no path: the player's car, wrecks, physics cars knocked off their road.
		/// </summary>
		private void BuildMovers()
		{
			Movers.Clear();
			Obstacles.Clear();
			foreach (TrafficDrive.Mover m in drawnMovers)
			{
				TrafficDrive.Mover p = m;
				TrafficDrive.Prepare(day, ref p);
				Movers.Add(p);
			}
			drawnMovers.Clear();
			foreach (TrafficBody b in physical.Values)
			{
				if (b == null)
				{
					continue;
				}
				if (b.Loose)
				{
					// A loose car in the road (stopped there, or knocked out of its space) is in the way; one still in its space is not.
					if (!b.InSpace)
					{
						Obstacles.Add(ObstacleOf(b.Rb));
					}
				}
				else if (b.Alive && !b.GaveUp)
				{
					Movers.Add(b.Me);
				}
				else
				{
					Obstacles.Add(ObstacleOf(b.Rb));
				}
			}
			foreach (TrafficBody b in remote.Values)
			{
				if (b == null || !b.gameObject.activeSelf)
				{
					continue;
				}
				// Another machine's physics car: a mover where its driver has it (its leg and place sent), a wall once stopped or wrecked.
				if (b.InSpace)
				{
					continue;
				}
				if (b.Alive && !b.Stopped && b.Me.Leg >= 0)
				{
					Movers.Add(b.Me);
				}
				else
				{
					Obstacles.Add(b.Obstacle());
				}
			}
			foreach (TrafficBody b in wrecks)
			{
				if (b != null)
				{
					Obstacles.Add(ObstacleOf(b.Rb));
				}
			}
			CarControllerNew player = tm.playerCarController;
			if (player != null)
			{
				Rigidbody rb = player.GetComponent<Rigidbody>();
				if (rb != null)
				{
					Obstacles.Add(ObstacleOf(rb));
				}
			}
		}

		private static TrafficDrive.Obstacle ObstacleOf(Rigidbody rb)
		{
			Origin.World(rb.position, out double x, out double z);
			Vector3 v = rb.linearVelocity;
			return new TrafficDrive.Obstacle { X = x, Z = z, Vx = v.x, Vz = v.z };
		}

		/// <summary>A drawn moving car, for the drivers.</summary>
		private void DrawnMover(int c, TrafficDay.Place at, double x, double z, double hx, double hz)
		{
			if (!AnyBodies)
			{
				return;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			bool pulling = (!leg.Appears && at.T < leg.PullOut) || (!leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn);
			drawnMovers.Add(new TrafficDrive.Mover { Car = c, Leg = at.Leg, P = at.P, V = at.Speed, X = x, Z = z, Hx = hx, Hz = hz, Pulling = pulling });
		}

		/// <summary>Headlights for the physics cars at night, the nearest first, out of the same few the drawn cars have.</summary>
		private void PhysicsLights(bool lights)
		{
			bodyList.Clear();
			bodyList.AddRange(physical.Values);
			bodyList.AddRange(remote.Values);
			foreach (TrafficBody b in bodyList)
			{
				if (b == null || b.Headlight == null)
				{
					continue;
				}
				bool on = lights && b.Alive && !b.Loose && lit < MostLit;
				if (on)
				{
					lit++;
				}
				if (b.Headlight.enabled != on)
				{
					b.Headlight.enabled = on;
				}
			}
		}

		/// <summary>A physics car was wrecked (its health gone): retired from the day, left to burn and explode where it is.</summary>
		internal void Wrecked(TrafficBody b)
		{
			if (b.Car >= 0 && physical.TryGetValue(b.Car, out TrafficBody found) && found == b)
			{
				physical.Remove(b.Car);
				wrecks.Add(b);
				if (day != null)
				{
					room.Release(b.Car, new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = SimNow(), D = TrafficRoom.Wrecked });
				}
				retiredWrecked++;
				Mod.Info("Traffic: car " + b.Car + " wrecked");
				return;
			}
			if (!wrecks.Contains(b))
			{
				wrecks.Add(b);
			}
		}

		// ---- the bodies ---------------------------------------------------------------------------------------------------------

		/// <summary>A physics car for car <paramref name="c"/>, from the pool or new, in its paint and model (inactive; placed by the caller).</summary>
		private TrafficBody TakeBody(int c)
		{
			long started = Perf.Now;
			try
			{
				return TakeBodyTimed(c);
			}
			finally
			{
				Perf.Add(Perf.Part.Switch, started);
			}
		}

		private TrafficBody TakeBodyTimed(int c)
		{
			Perf.Taken();
			Wears(c, out int look, out int paint);
			TrafficBody b = null;
			while (bodySpare.Count > 0 && b == null)
			{
				b = bodySpare.Pop();
			}
			try
			{
				if (b == null)
				{
					b = MakeBody(look, paint);
				}
				else if (b.Skin != null)
				{
					b.Skin.ApplySkin(SkinFor(look, paint));
				}
				b.LookIndex = look;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic's physics cars could not be made (drawn cars only from now on): " + e);
				physicsFailed = true;
				return null;
			}
			return b;
		}

		/// <summary>
		/// Bodies kept woken and spare ahead of need (topped up one a frame), and the one awake now (for one frame). A body is only ever put
		/// to use woken: the game's controller takes its wheels' places in its <c>Start</c>, the frame after the body is first switched on, so
		/// a body made and placed at once had its wheels moved to the rest pose first and the controller took those, widened again by 1.1
		/// (the wheels popped out) and lowered (the car bounced) (the owner, 2026-11-23).
		/// </summary>
		private const int WarmBodies = 3;
		private TrafficBody warming;

		/// <summary>
		/// At the start of the race, one frame at a time, the first few physics cars' bodies made and woken once far under the world (the
		/// Instantiate and the game's Awakes, other mods' too, paid for now), then put in the pool asleep: the first loose car, made the frame
		/// before the player touches it, then costs no more than a body taken from the pool.
		/// </summary>
		private void WarmPool()
		{
			if (warming == null && (bodySpare.Count >= WarmBodies || (MostLoose == 0 && MostPhysics == 0)))
			{
				return;
			}
			long started = Perf.Now;
			WarmPoolTimed();
			Perf.Add(Perf.Part.Switch, started);
		}

		private void WarmPoolTimed()
		{
			if (warming != null)
			{
				// Started now (its controller's Start ran as it woke): the rest is worked out from it, once.
				LearnRestFrom(warming);
				warming.gameObject.SetActive(false);
				warming.Rb.isKinematic = false;
				if (warming.Ec.audioSource != null)
				{
					warming.Ec.audioSource.Stop();
				}
				bodySpare.Push(warming);
				warming = null;
				return;
			}
			int bodies = physical.Count + remote.Count + bodySpare.Count;
			if (bodySpare.Count >= WarmBodies || bodies >= MostPhysics + MostLoose + WarmBodies || (MostLoose == 0 && MostPhysics == 0) || tm.enemyCarPrefab == null
				|| madeFrame == Time.frameCount || physicsFailed)
			{
				return;
			}
			TrafficBody b;
			try
			{
				b = MakeBody(0, 0);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic's physics cars could not be made (drawn cars only from now on): " + e);
				physicsFailed = true;
				return;
			}
			b.Rb.isKinematic = true;
			b.transform.SetPositionAndRotation(new Vector3(0f, -5000f, 0f), Quaternion.identity);
			b.gameObject.SetActive(true);
			warming = b;
		}

		/// <summary>
		/// A new physics car: an instance of the enemy car prefab made under an inactive holder, so nothing on it has woken yet, and
		/// prepared before it first wakes: the racing AI off (its Start would fit guns and look for a lap; with it off Start never runs), the
		/// nitro and the pit guard off, any gun off, the headlight ours to switch (the game's controller removed, so the sky's lights don't
		/// turn on every one), the skin given our paint and a name of its own (so the race's enemies' names are not used up).
		/// </summary>
		private TrafficBody MakeBody(int look, int paint)
		{
			if (bodyHolder == null)
			{
				bodyHolder = new GameObject("Traffic physics cars (asleep)");
				bodyHolder.SetActive(false);
				bodyHolder.transform.SetParent(transform, false);
			}
			madeFrame = Time.frameCount;
			GameObject go = Instantiate(tm.enemyCarPrefab, bodyHolder.transform);
			go.name = "Traffic physics car";
			EnemyController ec = go.GetComponent<EnemyController>();
			CarControllerNew cc = go.GetComponent<CarControllerNew>();
			Rigidbody rb = go.GetComponent<Rigidbody>();
			if (ec == null || cc == null || rb == null)
			{
				Destroy(go);
				throw new Exception("the enemy car has no EnemyController, CarControllerNew or Rigidbody");
			}
			ec.enabled = false;
			ec.canMove = false;
			ec.trackManager = tm;
			foreach (EnemyModule m in go.GetComponentsInChildren<EnemyModule>(true))
			{
				m.enabled = false;
			}
			foreach (EnemyGun g in go.GetComponentsInChildren<EnemyGun>(true))
			{
				g.enabled = false;
			}
			PitGuard guard = go.GetComponent<PitGuard>();
			if (guard != null)
			{
				guard.enabled = false;
			}
			Light headlight = null;
			HeadLightsController hl = go.GetComponentInChildren<HeadLightsController>(true);
			if (hl != null)
			{
				if (hl.spotlight != null)
				{
					headlight = hl.spotlight.GetComponent<Light>();
					hl.spotlight.SetActive(true);
					if (headlight != null)
					{
						headlight.enabled = false;
					}
				}
				DestroyImmediate(hl);
			}
			CarSkinController skin = go.GetComponent<CarSkinController>();
			if (skin != null)
			{
				skin.isPlayer = false;
				skin.carName = "Traffic";
				skin.carSkin = SkinFor(look, paint);
			}
			cc.isPlayer = false;
			cc.tm = tm;
			TrafficBody b = go.AddComponent<TrafficBody>();
			b.Cc = cc;
			b.Rb = rb;
			b.Ec = ec;
			b.Skin = skin;
			b.Headlight = headlight;
			b.FindLamps();
			go.SetActive(false);
			go.transform.SetParent(transform, false);
			Mod.Info("Traffic: a physics car's body made (" + (physical.Count + remote.Count + bodySpare.Count + 1) + " in use or spare)");
			return b;
		}

		/// <summary>Our paint as the game's skin: the model, the whole body the paint, the accents a shade of it, the trim dark (as stage 2's copies).</summary>
		private CarSkin SkinFor(int look, int paint)
		{
			if (skins.TryGetValue((look, paint), out CarSkin s) && s != null)
			{
				return s;
			}
			Color c = Paints[paint].colour;
			s = ScriptableObject.CreateInstance<CarSkin>();
			s.name = "Traffic skin " + look + "/" + paint;
			s.hideFlags = HideFlags.HideAndDontSave;
			s.skinIndex = look;
			s.color1 = c * 0.8f;
			s.color1.a = 1f;
			s.color2 = c;
			s.color3 = new Color(0.08f, 0.08f, 0.09f);
			skins[(look, paint)] = s;
			return s;
		}

		/// <summary>Back to the pool: asleep, still, its engine and light off.</summary>
		private void Release(TrafficBody b)
		{
			long started = Perf.Now;
			Perf.Given();
			b.End();
			b.gameObject.SetActive(false);
			bodySpare.Push(b);
			Perf.Add(Perf.Part.Switch, started);
		}

		/// <summary>Every physics car back on its timetable through the room (the traffic switched off, or stopped), and every stand-in gone.</summary>
		private void ClearPhysics()
		{
			if (warming != null)
			{
				// The body woken for the pool this frame: put to sleep in it.
				WarmPool();
			}
			double w = day != null ? SimNow() : 0.0;
			acts.Clear();
			foreach (KeyValuePair<int, TrafficBody> kv in physical)
			{
				TrafficBody b = kv.Value;
				bool back = day != null && b != null && b.Alive && !b.Hurt;
				TrafficRoom.Op op = default;
				if (back && b.Loose)
				{
					// A loose car: as it was if untouched, retired if knocked.
					Origin.World(b.Rb.position, out double x, out double z);
					back = !b.Touched && LooseBack(kv.Key, b, x, z, w, out op);
				}
				else if (back)
				{
					op = LateOp(b.Leg, b.P, w);
				}
				acts.Add((kv.Key, b, back ? Leave.Back : Leave.Retire, back ? op : new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w }, 0UL));
			}
			physical.Clear();
			foreach ((int car, TrafficBody b, Leave what, TrafficRoom.Op op, ulong _) in acts)
			{
				if (b != null)
				{
					if (what == Leave.Back)
					{
						Release(b);
					}
					else
					{
						Destroy(b.gameObject);
					}
				}
				if (room != null)
				{
					room.Release(car, op);
				}
				else if (late != null)
				{
					late.Apply(car, op);
				}
			}
			ClearStandIns();
		}

		/// <summary>The overlay's physics part.</summary>
		private string PhysicsStatus()
		{
			if (MostPhysics == 0 && madePhysical == 0 && MostLoose == 0 && madeLoose == 0)
			{
				return physicsFailed ? "; physics cars failed" : "";
			}
			int holding = 0, waiting = 0;
			foreach (TrafficBody b in physical.Values)
			{
				if (b != null && (b.Hold || b.GaveUp))
				{
					holding++;
				}
				if (b != null && b.Drive.Why == 2)
				{
					waiting++;
				}
			}
			int loose = LooseCount;
			return "; physics " + (physical.Count - loose) + "/" + MostPhysics + " (" + waiting + " giving way, " + holding + " stopped), made " + madePhysical + ", back " + backOnTime + " on time and " + backLate + " late, wrecked "
				+ retiredWrecked + ", retired " + retiredOff + ", hidden " + hiddenByPhysics + ", held back " + heldBack
				+ "; loose " + loose + "/" + MostLoose + " (made " + madeLoose + ", back " + looseBack + ", retired " + looseRetired + ")" + RoomStatus();
		}

	}

	/// <summary>
	/// One physics traffic car: the game's enemy car with our driver. Each physics step it finds where it is on its leg, works out the
	/// speed to aim for (<see cref="TrafficDrive.Target"/>) and steers for a point ahead on its lane, through the game's car controller
	/// (steering angle, engine and brake: the controller does the rest). Its health is its own (the game's enemy health is private), and a
	/// wreck is the game's wreck without a kill: no heal, ammo, kill count or combo (owner decision 1's default, see the handoff).
	///
	/// In a room the same object is also a stand-in (<see cref="Remote"/>) for a car another machine drives: kinematic, moved each physics
	/// step to its driver's states played back (<see cref="TrafficRoom.Playback"/>), its smoke and wreck shown from them, hits sent to the
	/// driver. A car handed between machines turns from one into the other in place, so it never vanishes or jumps.
	/// </summary>
	internal sealed class TrafficBody : MonoBehaviour
	{
		/// <summary>How long a wreck burns before it explodes and goes (the game's enemy car's).</summary>
		private const float ExplodeAfter = 5f;
		/// <summary>Gear changes: the gap in neutral and the least time in a gear.</summary>
		private const float GearGap = 0.2f, GearHold = 0.6f;

		internal TrafficCars Owner;
		internal CarControllerNew Cc;
		internal Rigidbody Rb;
		internal EnemyController Ec;
		internal CarSkinController Skin;
		internal Light Headlight;

		/// <summary>The day's car it is (-1: asleep), the leg it drives, how far along it is and the leg's speed profile.</summary>
		internal int Car = -1, Leg;
		internal double P;
		private float[] profile;

		/// <summary>The body model it wears (its look), and how long since it was last started (seconds of physics).</summary>
		internal int LookIndex;
		internal float Age;
		internal float Health = 100f;
		internal bool Alive = true, Hurt, GaveUp;
		/// <summary>Told by the bubble to stop and wait (it can't go back on its timetable where it is).</summary>
		internal bool Hold;
		/// <summary>A stand-in for a car another machine drives (kinematic, played back from its states).</summary>
		internal bool Remote;
		/// <summary>A stand-in whose driver has it stopped (held, or given up): a wall to the drivers here, not a mover.</summary>
		internal bool Stopped;
		/// <summary>
		/// A loose car (contact physics): no driver, on its brakes, made where a drawn car stood a moment before the player touched it.
		/// <see cref="Touched"/> once anything with a body has hit it, it has been damaged, or it has moved or turned since it settled.
		/// </summary>
		internal bool Loose, Touched;
		/// <summary>A loose car's leg if it was driving when it was made (-1: parked), the leg's clock then, whether it was pulling in or out, and the sim time.</summary>
		internal int LooseLeg = -1;
		internal double LooseT, LooseW;
		internal bool LoosePulling;

		/// <summary>A loose car still standing untouched in its parking space (a stand-in's: parked, its leg sent as -1): not in anybody's way on the road.</summary>
		internal bool InSpace => Loose && !Touched && Alive && (Remote ? Leg < 0 : LooseLeg < 0);
		/// <summary>Where a loose car settled (world x, z, scene y, its turn), the moment it first stood still: moved from there, it was touched.</summary>
		private bool settled;
		private double restX, restZ;
		private float restY, looseAge;
		private Quaternion restTurn;
		internal readonly TrafficRoom.Playback Play = new TrafficRoom.Playback();
		/// <summary>A car just handed on carries on as it was going until its new driver's states come (at most the playback's coast).</summary>
		private Vector3 coastVelocity;
		private float coastLeft;
		private bool smoking;
		private TrailRenderer[] trails;
		/// <summary>How long the tyre marks stay off and empty after the body was put somewhere new (seconds).</summary>
		private float trailsOffFor;
		private const float TrailsOff = 0.3f;

		/// <summary>
		/// Puts the body at scene <paramref name="pos"/> turned <paramref name="turn"/>, awake, its rigidbody moved there too (a teleport, so
		/// neither physics nor its interpolation starts from where it last was), and its tyre marks put out for a moment.
		/// </summary>
		internal void Place(Vector3 pos, Quaternion turn)
		{
			long started = TrafficCars.Perf.Now;
			// The tyre marks off and empty BEFORE the move (they paint from where the wheels were to where they are), kept off a moment after.
			ClearTrails();
			transform.SetPositionAndRotation(pos, turn);
			if (!gameObject.activeSelf)
			{
				gameObject.SetActive(true);
			}
			Rb.position = pos;
			Rb.rotation = turn;
			if (!Rb.isKinematic)
			{
				// Still where it is put (a moving car is given its speed after); a stand-in is kinematic and has none to set.
				Rb.linearVelocity = Vector3.zero;
				Rb.angularVelocity = Vector3.zero;
			}
			if (!interpHeld && Rb.interpolation != RigidbodyInterpolation.None)
			{
				// As Origin does over a shift: an interpolated body would be drawn from its old pose for a frame, sliding (and drawing its
				// tyre marks) the whole way. Off until the physics has stepped here.
				interpHeld = true;
				interpMode = Rb.interpolation;
				Rb.interpolation = RigidbodyInterpolation.None;
			}
			ClearTrails();
			TrafficCars.Perf.Add(TrafficCars.Perf.Part.Switch, started);
		}

		private bool interpHeld;
		private RigidbodyInterpolation interpMode;
		private ParticleSystem[] particles;

		/// <summary>
		/// The tyre marks (the game's <c>TireTrails</c>: trail renderers on the wheels, which its <c>EnemyCarEffectController</c> keeps emitting
		/// whenever the wheels are on the ground) forgotten after the body is put somewhere new (taken from the pool, made, a stand-in shown
		/// at its first state), and kept off and empty for <see cref="TrailsOff"/>. Clearing once was not enough (the owner still saw streaks
		/// from the scene's origin): for a frame or two after the move the body can still be drawn from the rigidbody's old pose, and the
		/// marks drew a new line from there across the country to here.
		/// </summary>
		internal void ClearTrails()
		{
			if (trails == null)
			{
				trails = GetComponentsInChildren<TrailRenderer>(true);
			}
			foreach (TrailRenderer t in trails)
			{
				if (t != null)
				{
					t.emitting = false;
					t.Clear();
				}
			}
			trailsOffFor = TrailsOff;
			trailsSettle = 0f;
			// The guard starts again from wherever the body is put.
			trailShifts = -1;
			ClearParticles();
		}

		/// <summary>
		/// The springs and the wheels as at rest (<see cref="TrafficCars.Rest"/>), after <see cref="Place"/>: each of the game's wheels' last
		/// length (so the damper sees no movement on the first step: a stale length from the pool kicked the body) and each wheel drawn where
		/// the game draws it at rest (it eases its wheels there a tenth a frame, so from the pool's pose they slid into place).
		/// </summary>
		internal void Settle(TrafficCars.Rest rest)
		{
			if (Cc == null || rest.Length == null)
			{
				return;
			}
			if (Cc.wheels == null || Cc.wheels.Length != rest.Length.Length || Cc.wheels[0] == null)
			{
				// Not started yet (made this frame: the warm pool ran short). Its controller's Start must take the wheels where the prefab has
				// them, so they are left alone until it has (LateUpdate).
				settleLater = rest;
				return;
			}
			settleLater = null;
			for (int i = 0; i < rest.Length.Length; i++)
			{
				if (Cc.wheels[i] != null)
				{
					Cc.wheels[i].lastLength = rest.Length[i];
				}
			}
			int front = Cc.frontWheels != null ? Cc.frontWheels.Count : 0;
			for (int i = 0; i < rest.Local.Length; i++)
			{
				Transform w = i < front ? Cc.frontWheels[i] : (Cc.backWheels != null && i - front < Cc.backWheels.Count ? Cc.backWheels[i - front] : null);
				if (w != null)
				{
					w.localPosition = rest.Local[i];
				}
			}
		}

		/// <summary>A rest to settle into once the controller has started (<see cref="Settle"/>).</summary>
		private TrafficCars.Rest settleLater;

		/// <summary>The body's particles (the tyres' smoke, the exhaust) emptied: anything emitted along a move would hang in a line across the country.</summary>
		private void ClearParticles()
		{
			if (particles == null)
			{
				particles = GetComponentsInChildren<ParticleSystem>(true);
			}
			foreach (ParticleSystem ps in particles)
			{
				if (ps != null)
				{
					ps.Clear(false);
				}
			}
		}

		/// <summary>The lamps (renderers with a glowing material that take no paint: head and tail lights), their glowing slots and colours.</summary>
		private Renderer[] lamps;
		private int[][] lampSlots;
		private Color[][] lampColours;
		private bool lampsDark;

		/// <summary>Finds the lamps, while the body is still the prefab's (before the controller first lights its brake lights).</summary>
		internal void FindLamps()
		{
			List<Renderer> found = new List<Renderer>();
			List<int[]> slots = new List<int[]>();
			List<Color[]> colours = new List<Color[]>();
			HashSet<Renderer> paint = new HashSet<Renderer>();
			if (Skin != null)
			{
				foreach (MeshRenderer r in Skin.carMeshRenderers ?? new MeshRenderer[0])
				{
					paint.Add(r);
				}
				foreach (MeshRenderer r in Skin.hoodsRenderers ?? new MeshRenderer[0])
				{
					paint.Add(r);
				}
			}
			foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
			{
				if (r is TrailRenderer || r is ParticleSystemRenderer || paint.Contains(r))
				{
					continue;
				}
				Material[] mats = r.sharedMaterials;
				List<int> at = new List<int>();
				List<Color> cs = new List<Color>();
				for (int m = 0; m < mats.Length; m++)
				{
					if (TrafficCars.Glows(mats[m]))
					{
						at.Add(m);
						cs.Add(mats[m].GetColor("_EmissionColor"));
					}
				}
				if (at.Count > 0)
				{
					found.Add(r);
					slots.Add(at.ToArray());
					colours.Add(cs.ToArray());
				}
			}
			lamps = found.ToArray();
			lampSlots = slots.ToArray();
			lampColours = colours.ToArray();
		}

		/// <summary>
		/// Darkens (a parked car) or lights the lamps. The game's car controller lights the tail lamps whenever the brake is on (it turns their
		/// material's emission keyword on and off), and a parked loose car holds its brake: its lamps' emission colour is made black instead,
		/// which the controller never touches, so they stay dark whatever it does with the keyword; lit again, the colours come back.
		/// </summary>
		internal void Lamps(bool on)
		{
			if (lamps == null || lampsDark == !on)
			{
				return;
			}
			lampsDark = !on;
			for (int i = 0; i < lamps.Length; i++)
			{
				if (lamps[i] == null)
				{
					continue;
				}
				// This body's own copies (the controller makes them for the tail lamps anyway).
				Material[] mats = lamps[i].materials;
				for (int k = 0; k < lampSlots[i].Length; k++)
				{
					int m = lampSlots[i][k];
					if (m < mats.Length && mats[m] != null)
					{
						mats[m].SetColor("_EmissionColor", on ? lampColours[i][k] : Color.black);
					}
				}
			}
		}

		/// <summary>
		/// After the body was placed: its tyre marks kept empty with interpolation held off (<see cref="TrailsOff"/>), then interpolation back
		/// on while the marks stay off a while longer (<see cref="TrailsSettle"/>), then the marks back on as the game has them (on unless
		/// airborne). And every frame the jump guard (<see cref="GuardTrails"/>).
		/// </summary>
		/// <remarks>
		/// Interpolation and the marks used to come back on the same frame: an interpolated body can be drawn for a frame or two from a pose it
		/// had before it was put here (the pool's, the scene's origin), and with its marks already on, that frame painted a streak from there
		/// to here, on the odd swapped car (the owner, after four fixes that each cleared the marks around the move itself).
		/// </remarks>
		private void LateUpdate()
		{
			if (settleLater != null && Cc != null && Cc.wheels != null && Cc.wheels.Length > 0 && Cc.wheels[0] != null)
			{
				Settle(settleLater);
			}
			if (trails == null)
			{
				return;
			}
			if (trailsOffFor > 0f)
			{
				trailsOffFor -= Time.deltaTime;
				ClearParticles();
				if (trailsOffFor <= 0f && interpHeld)
				{
					// Interpolation back first; the marks wait out a few physics steps more, so its history is all from here.
					interpHeld = false;
					Rb.interpolation = interpMode;
					trailsSettle = TrailsSettle;
				}
				TrailsOffNow();
			}
			else if (trailsSettle > 0f)
			{
				trailsSettle -= Time.deltaTime;
				TrailsOffNow();
				if (trailsSettle <= 0f)
				{
					foreach (TrailRenderer t in trails)
					{
						if (t != null)
						{
							t.emitting = !Cc.isAirborne;
							// Emptied again with it on: whatever it kept from before starts nowhere.
							t.Clear();
						}
					}
				}
			}
			GuardTrails();
		}

		/// <summary>How long the marks stay off after interpolation comes back (seconds: several physics steps).</summary>
		private const float TrailsSettle = 0.15f;
		private float trailsSettle;

		/// <summary>The marks off and empty this frame (the game's effect controller turns them on when the wheels land, so each frame).</summary>
		private void TrailsOffNow()
		{
			foreach (TrailRenderer t in trails)
			{
				if (t != null)
				{
					t.emitting = false;
					t.Clear();
				}
			}
		}

		/// <summary>Where each mark's wheel was drawn last frame, and the origin shift count then (a shift moves everything: no jump).</summary>
		private Vector3[] trailLast;
		private int trailShifts = -1;

		/// <summary>
		/// The jump guard, whatever moved the body. Runs in LateUpdate, after interpolation has placed the body and before Unity adds this
		/// frame's points to the marks. If the drawn body is away from its rigidbody (interpolating from a stale pose), or a marked wheel
		/// went further this frame than the car's speed allows, the marks are emptied and kept off as after a placing: no streak can be
		/// drawn from where it was to where it is.
		/// </summary>
		private void GuardTrails()
		{
			if (trailLast == null || trailLast.Length != trails.Length)
			{
				trailLast = new Vector3[trails.Length];
				trailShifts = -1;
			}
			bool shifted = trailShifts != Origin.Shifts;
			if (shifted && trailShifts >= 0 && trailsOffFor <= 0f)
			{
				// An origin shift: for a few frames after it an interpolated body can still be drawn part of the way back toward where it was
				// before (the owner's log, 2026-11-23: marks emptied with the wheel 104 to 146 from them, a tenth of the shift, even with
				// interpolation held off over the shift). The marks are kept off over that time instead, and emptied as they come back.
				TrailsOffNow();
				trailsSettle = Mathf.Max(trailsSettle, ShiftHold);
			}
			trailShifts = Origin.Shifts;
			// One physics step's travel is the most interpolation lags by; this is many times that.
			bool stale = !Rb.isKinematic && (transform.position - Rb.position).sqrMagnitude > JumpSlack * JumpSlack;
			// A stand-in is kinematic, moved by MovePosition, and may report no speed: allowed a fast car's travel instead.
			float speed = Rb.isKinematic ? Mathf.Max(Rb.linearVelocity.magnitude, 120f) : Rb.linearVelocity.magnitude;
			float most = speed * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime) * 2f + JumpSlack;
			bool jumped = stale;
			for (int i = 0; i < trails.Length; i++)
			{
				if (trails[i] == null)
				{
					continue;
				}
				Vector3 p = trails[i].transform.position;
				if (!shifted && (p - trailLast[i]).sqrMagnitude > most * most)
				{
					jumped = true;
				}
				trailLast[i] = p;
			}
			if (!jumped)
			{
				// The marks' own points: a segment no car draws in a frame, or a head far from both ends (a point kept from before, which
				// the drawn line would join to the wheel), is a streak, whatever made it. Emptied before it is drawn again.
				float seg = most + speed * Time.deltaTime * 2f + 6f;
				for (int i = 0; i < trails.Length; i++)
				{
					string why = Streak(trails[i], seg);
					if (why != null)
					{
						trails[i].Clear();
						if (!streakLogged)
						{
							streakLogged = true;
							Mod.Info("Traffic: car " + Car + "'s tyre mark emptied (" + why + ")");
						}
					}
				}
				return;
			}
			TrailsOffNow();
			ClearParticles();
			if (trailsOffFor <= 0f)
			{
				trailsSettle = Mathf.Max(trailsSettle, TrailsSettle);
			}
			if (!guardLogged)
			{
				guardLogged = true;
				Mod.Info("Traffic: car " + Car + "'s tyre marks cut at a jump (" + (stale ? "drawn away from its body" : "a wheel moved too far in a frame") + ")");
			}
		}

		private bool streakLogged;

		/// <summary>Why trail <paramref name="t"/> holds a streak (a segment at either end longer than <paramref name="seg"/>, or its head that far from both ends), or null.</summary>
		private static string Streak(TrailRenderer t, float seg)
		{
			if (t == null)
			{
				return null;
			}
			int n = t.positionCount;
			if (n == 0)
			{
				return null;
			}
			float seg2 = seg * seg;
			Vector3 head = t.transform.position;
			Vector3 first = t.GetPosition(0), last = t.GetPosition(n - 1);
			// Only while it draws: off (airborne), a mark stays behind as the car flies on.
			if (t.emitting && (head - first).sqrMagnitude > seg2 && (head - last).sqrMagnitude > seg2)
			{
				return "its wheel " + Mathf.Sqrt(Mathf.Min((head - first).sqrMagnitude, (head - last).sqrMagnitude)).ToString("0") + " from its ends";
			}
			if (n >= 2)
			{
				float a = (t.GetPosition(1) - first).sqrMagnitude, b = (last - t.GetPosition(n - 2)).sqrMagnitude;
				if (a > seg2 || b > seg2)
				{
					return "a segment " + Mathf.Sqrt(Mathf.Max(a, b)).ToString("0") + " long";
				}
			}
			return null;
		}

		/// <summary>How long the marks stay off after an origin shift (seconds): past the frames and physics steps the shift's interpolation pause lasts.</summary>
		private const float ShiftHold = 0.3f;

		/// <summary>Distance (units) past which a move in one frame, or the drawn body from its rigidbody, is a jump.</summary>
		private const float JumpSlack = 4f;
		private bool guardLogged;
		internal TrafficDrive.State Drive;
		internal TrafficDrive.Mover Me;
		private float offFor, steer;
		private int gear = -1, pending = -1;
		private float gearHold, gearNeutral;
		private bool woken;

		/// <summary>A driven physics car's lane (held while it drives) goes back to the day's from where it is.</summary>
		private void LetGoOfLane()
		{
			if (!Remote && !Loose && Owner != null && Owner.Day != null && Me.Leg >= 0 && Me.Leg < Owner.Day.Legs.Count)
			{
				TrafficDay.ReleaseLaneAt(Owner.Day.Legs[Me.Leg], Me.P);
			}
		}

		/// <summary>Starts driving car <paramref name="car"/> on leg <paramref name="leg"/> at <paramref name="p"/>, moving at <paramref name="velocity"/>.</summary>
		internal void Begin(TrafficCars owner, int car, int leg, double p, Vector3 velocity)
		{
			Wake(owner, car, leg, p, velocity);
			profile = TrafficDay.Profile(owner.Day.Legs[leg]);
			TrafficDay.HoldLaneAt(owner.Day.Legs[leg], p);
			Engine(car);
		}

		/// <summary>
		/// Starts as a loose car for car <paramref name="car"/> (contact physics): no driver, braking (to a stop if it was moving at
		/// <paramref name="velocity"/>), its engine and light off. <paramref name="leg"/> (-1: parked), <paramref name="t"/> (the leg's clock
		/// where it was drawn), <paramref name="pulling"/> and <paramref name="w"/> are for going back on its timetable untouched.
		/// </summary>
		internal void BeginLoose(TrafficCars owner, int car, int leg, double p, double t, bool pulling, double w, Vector3 velocity)
		{
			Wake(owner, car, leg, p, velocity);
			profile = null;
			Loose = true;
			LooseLeg = leg;
			LooseT = t;
			LooseW = w;
			LoosePulling = pulling;
			// A parked car's lamps are dark (it holds its brake, which would light the brake lights); a car braked to a stop keeps them.
			Lamps(leg >= 0);
			Cc.brakeAccel = Cc.carStats.brakeDeccel;
			if (Ec.audioSource != null)
			{
				Ec.audioSource.Stop();
			}
		}

		/// <summary>What starting to drive and starting loose share: a body of its own (not a stand-in), health full, everything still.</summary>
		private void Wake(TrafficCars owner, int car, int leg, double p, Vector3 velocity)
		{
			Remote = false;
			Stopped = false;
			Loose = Touched = settled = false;
			LooseLeg = -1;
			looseAge = 0f;
			Age = 0f;
			Lamps(true);
			Rb.isKinematic = false;
			Owner = owner;
			Car = car;
			Leg = leg;
			P = p;
			if (!woken)
			{
				// Its first waking ran the game's Awakes: the enemy AI set the player's stats on the controller. A plain car's instead.
				woken = true;
				Cc.carStats = new CarStats();
			}
			Health = Mathf.Max(1f, Cc.carStats.health);
			Alive = true;
			Hurt = GaveUp = Hold = false;
			Ec.alive = true;
			Drive = default;
			Drive.WaitNode = -1;
			offFor = 0f;
			steer = 0f;
			gear = pending = -1;
			gearHold = gearNeutral = 0f;
			Rb.linearVelocity = velocity;
			Rb.angularVelocity = Vector3.zero;
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
			Cc.steeringAngleDeg = 0f;
			Me = new TrafficDrive.Mover { Car = car, Leg = leg, P = p, V = velocity.magnitude, Physical = true, Edge = -1, Node = -1 };
		}

		/// <summary>The game's enemy cars play one engine loop each; so do these.</summary>
		private void Engine(int car)
		{
			if (Ec.audioSource != null && Ec.engineSounds != null && Ec.engineSounds.Length > 0)
			{
				AudioClip clip = Ec.engineSounds[(car & 0x7fffffff) % Ec.engineSounds.Length];
				if (Ec.audioSource.clip != clip || !Ec.audioSource.isPlaying)
				{
					Ec.audioSource.clip = clip;
					Ec.audioSource.Play();
				}
			}
		}

		/// <summary>Starts as a stand-in for car <paramref name="car"/>, which another machine drives (inactive until its first state comes).</summary>
		internal void BeginRemote(TrafficCars owner, int car)
		{
			if (!woken)
			{
				woken = true;
				Cc.carStats = new CarStats();
			}
			Owner = owner;
			Car = car;
			Leg = -1;
			P = 0.0;
			Remote = true;
			Stopped = false;
			Loose = Touched = false;
			LooseLeg = -1;
			Lamps(true);
			Rb.isKinematic = true;
			Health = 100f;
			Alive = true;
			Hurt = GaveUp = Hold = smoking = false;
			Ec.alive = true;
			Play.Clear();
			coastLeft = 0f;
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
			Cc.steeringAngleDeg = 0f;
			Me = new TrafficDrive.Mover { Car = car, Leg = -1, Physical = true, Edge = -1, Node = -1 };
			if (gameObject.activeSelf)
			{
				Engine(car);
			}
		}

		/// <summary>This machine handed the car on: a stand-in from here, carrying on as it was going until its new driver's states come.</summary>
		internal void ToRemote()
		{
			LetGoOfLane();
			coastVelocity = Rb.linearVelocity;
			coastLeft = (float)TrafficRoom.Playback.Coast;
			Rb.isKinematic = true;
			Remote = true;
			Stopped = false;
			Hold = false;
			Play.Clear();
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
		}

		/// <summary>The car was handed to this machine: its stand-in drives from here, from its newest state (leg, place, health, speed).</summary>
		internal void FromRemote(int leg, double p)
		{
			Vector3 velocity = Vector3.zero;
			float health = Health;
			bool hurt = Hurt;
			if (Play.Count > 0)
			{
				TrafficRoom.State s = Play.Newest;
				velocity = new Vector3(s.Vx, s.Vy, s.Vz);
				health = s.Health;
				hurt = s.Hurt;
			}
			else if (coastLeft > 0f)
			{
				velocity = coastVelocity;
			}
			Begin(Owner, Car, leg, p, velocity);
			Health = Mathf.Max(1f, health);
			Hurt = hurt;
			smoking = hurt;
		}

		/// <summary>A stand-in as something in the way (stopped, or wrecked): where it is, moving as its driver had it.</summary>
		internal TrafficDrive.Obstacle Obstacle()
		{
			Origin.World(Rb.position, out double x, out double z);
			return new TrafficDrive.Obstacle { X = x, Z = z, Vx = Stopped || !Alive ? 0.0 : Me.V * Me.Hx, Vz = Stopped || !Alive ? 0.0 : Me.V * Me.Hz };
		}

		/// <summary>Stops driving (back to the pool).</summary>
		internal void End()
		{
			LetGoOfLane();
			Car = -1;
			// Into the pool with its tyre marks off and empty, so nothing is painted when it is next put somewhere.
			ClearTrails();
			Loose = Touched = false;
			LooseLeg = -1;
			if (Remote)
			{
				Remote = false;
				Play.Clear();
				coastLeft = 0f;
				Rb.isKinematic = false;
			}
			Rb.linearVelocity = Vector3.zero;
			Rb.angularVelocity = Vector3.zero;
			if (Ec.audioSource != null)
			{
				Ec.audioSource.Stop();
			}
			if (Headlight != null)
			{
				Headlight.enabled = false;
			}
		}

		private void FixedUpdate()
		{
			if (Car < 0 || Owner == null || Owner.Day == null)
			{
				return;
			}
			long started = TrafficCars.Perf.Now;
			try
			{
				Step();
			}
			finally
			{
				TrafficCars.Perf.Add(TrafficCars.Perf.Part.Fixed, started);
			}
		}

		/// <summary>A physics step's driving (timed by <see cref="FixedUpdate"/>).</summary>
		private void Step()
		{
			Age += Time.fixedDeltaTime;
			if (Remote)
			{
				StandIn();
				return;
			}
			if (Loose)
			{
				LooseStep();
				return;
			}
			if (!Alive)
			{
				Cc.engineAccel = 0f;
				Cc.brakeAccel = 0f;
				return;
			}
			float dt = Time.fixedDeltaTime;
			TrafficDay day = Owner.Day;
			TrafficDay.Leg leg = day.Legs[Leg];
			Vector3 pos = Rb.position;
			Origin.World(pos, out double x, out double z);
			Vector3 f = transform.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			float v = Cc.forwardSpeed;
			// Searched a little either side of where it was (it moves under a unit a step); wider once it is off its line.
			P = offFor > 0f ? TrafficDrive.Project(leg, P, x, z, out double side) : TrafficDrive.Project(leg, P, x, z, out side, 6.0, 6.0 + Mathf.Abs(v) * 0.1);
			offFor = side > 8.0 ? offFor + dt : 0f;
			if (offFor > 10f)
			{
				// Knocked off its road and can't find its way back: it stands where it is until the player goes (then it is retired).
				GaveUp = true;
			}
			Me = new TrafficDrive.Mover { Car = Car, Leg = Leg, P = P, V = v, X = x, Z = z, Hx = f.x / fl, Hz = f.z / fl, Physical = true };
			TrafficDrive.Prepare(day, ref Me);
			if (GaveUp || Hold || transform.up.y < 0.5f)
			{
				Pedals(0.0, v, dt);
				Cc.steeringAngleDeg = 0f;
				return;
			}
			double w = Owner.Now;
			double onP = TrafficDrive.OnTime(leg, w, out double onV);
			double target = TrafficDrive.Target(day, Me, profile, Owner.Movers, Owner.Obstacles, onP, onV, w, dt, ref Drive);
			Steer(leg, pos, v, dt);
			Pedals(target, v, dt);
		}

		/// <summary>
		/// A loose car's physics step: the brakes full on, no engine, wheels straight. Once it first stands still (a moving one braked to a
		/// stop, a parked one settled on its springs) where it stands is kept, and moved half a unit or turned three degrees from there it
		/// has been touched.
		/// </summary>
		private void LooseStep()
		{
			Cc.engineAccel = 0f;
			Cc.brakeAccel = Alive ? Cc.carStats.brakeDeccel : 0f;
			Cc.steeringAngleDeg = 0f;
			looseAge += Time.fixedDeltaTime;
			if (Touched)
			{
				return;
			}
			Vector3 pos = Rb.position;
			Origin.World(pos, out double x, out double z);
			if (!settled)
			{
				if (looseAge >= 0.75f && Rb.linearVelocity.sqrMagnitude < 0.09f && Rb.angularVelocity.sqrMagnitude < 0.01f)
				{
					settled = true;
					restX = x;
					restZ = z;
					restY = pos.y;
					restTurn = Rb.rotation;
				}
				return;
			}
			double dx = x - restX, dz = z - restZ, dy = pos.y - restY;
			if (dx * dx + dz * dz + dy * dy > 0.25 || Quaternion.Angle(restTurn, Rb.rotation) > 3f)
			{
				Touched = true;
			}
		}

		/// <summary>A loose car hit by anything with a body (the player's car, another car, a drawn car's box) has been touched.</summary>
		private void OnCollisionEnter(Collision c)
		{
			if (Loose && !Remote && c.rigidbody != null)
			{
				Touched = true;
			}
		}

		/// <summary>
		/// A stand-in's physics step: moved (kinematic, so it pushes and is a wall as another player's car is) to its driver's states played
		/// back; its place on its leg kept for the drivers here; its smoke and wreck shown as its driver has them.
		/// </summary>
		private void StandIn()
		{
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
			float dt = Time.fixedDeltaTime;
			if (Play.Count == 0)
			{
				if (coastLeft > 0f)
				{
					coastLeft -= dt;
					Rb.MovePosition(Rb.position + coastVelocity * dt);
				}
				return;
			}
			if (!Play.Sample(Owner.FixedClock, out TrafficRoom.State s))
			{
				return;
			}
			Vector3 to = Origin.Local(s.X, s.Y, s.Z);
			Quaternion q = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
			if ((to - Rb.position).sqrMagnitude > 10f * 10f)
			{
				// A snap (states missed for a while): a jump, not a drive there, with no tyre marks drawn along it.
				Rb.position = to;
				Rb.rotation = q;
				transform.SetPositionAndRotation(to, q);
				ClearTrails();
			}
			else
			{
				Rb.MovePosition(to);
				Rb.MoveRotation(q);
			}
			Vector3 f = q * Vector3.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			Leg = s.Leg;
			P = s.P;
			Stopped = s.Stopped;
			Loose = s.Loose;
			Touched = s.Touched;
			// Another machine's parked loose car: dark, as there.
			Lamps(!(s.Loose && s.Leg < 0));
			if (Loose && Ec.audioSource != null && Ec.audioSource.isPlaying)
			{
				// Another machine's loose car: parked or stopped, its engine off.
				Ec.audioSource.Stop();
			}
			Me = new TrafficDrive.Mover { Car = Car, Leg = s.Leg, P = s.P, V = Mathf.Sqrt(s.Vx * s.Vx + s.Vz * s.Vz), X = s.X, Z = s.Z, Hx = f.x / fl, Hz = f.z / fl, Physical = true };
			if (s.Leg >= 0 && s.Leg < Owner.Day.Legs.Count)
			{
				TrafficDrive.Prepare(Owner.Day, ref Me);
			}
			else
			{
				Me.Leg = -1;
			}
			Health = s.Health;
			if (s.Hurt && !smoking && Alive)
			{
				smoking = true;
				Hurt = true;
				if (Ec.effectsController != null)
				{
					Ec.effectsController.StartSmokeEmission();
				}
			}
			if (!s.Alive && Alive)
			{
				Wreck(false);
			}
		}

		/// <summary>For a point ahead on its lane, further the faster it goes: the signed angle to it times a gain (more for small angles), smoothed.</summary>
		private void Steer(TrafficDay.Leg leg, Vector3 pos, float v, float dt)
		{
			double ahead = Math.Max(10.0, Math.Min(45.0, 8.0 + 0.6 * Math.Max(0f, v)));
			TrafficDrive.Toward(leg, P, ahead, out double tx, out double tz);
			Vector3 to = Origin.Local(tx, pos.y, tz) - pos;
			to.y = 0f;
			Vector3 f = transform.forward;
			f.y = 0f;
			if (to.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-6f)
			{
				return;
			}
			float angle = Vector3.SignedAngle(f, to, Vector3.up);
			float gain = Mathf.Lerp(0.7f, 1f, Mathf.InverseLerp(5f, 25f, Mathf.Abs(angle)));
			float want = Mathf.Clamp(angle * gain, -45f, 45f);
			steer = Mathf.Lerp(steer, want, Mathf.Clamp01(dt * 8f));
			Cc.steeringAngleDeg = steer;
		}

		/// <summary>Engine, brake and gear for target speed <paramref name="target"/> (the game's enemy idea in our own words: the gear that pulls hardest, a gap in neutral to change).</summary>
		private void Pedals(double target, float v, float dt)
		{
			CarStats stats = Cc.carStats;
			gearHold = Mathf.Max(0f, gearHold - dt);
			gearNeutral = Mathf.Max(0f, gearNeutral - dt);
			if (target < 0.3 && v < 1f)
			{
				// Standing: held on the brake.
				Cc.engineAccel = 0f;
				Cc.brakeAccel = stats.brakeDeccel;
				return;
			}
			if (v > target + 0.5)
			{
				Cc.engineAccel = 0f;
				Cc.brakeAccel = Mathf.Min(stats.brakeDeccel, (float)(v - target) * 3f + 2f);
				return;
			}
			if (v < target - 0.5)
			{
				int best = -1;
				float most = 0f;
				for (int i = 0; i < stats.gearbox.ForwardCount; i++)
				{
					float a = AccelInGear(stats, i, v);
					if (a > most)
					{
						most = a;
						best = i;
					}
				}
				if (best >= 0)
				{
					if (gear < 0)
					{
						gear = best;
						gearHold = GearHold;
					}
					else if (pending >= 0)
					{
						if (gearNeutral <= 0f)
						{
							gear = pending;
							pending = -1;
							gearHold = GearHold;
						}
					}
					else if (gearHold <= 0f && best != gear)
					{
						pending = best;
						gearNeutral = GearGap;
					}
				}
				float pedal = Mathf.Clamp01((float)(target - v) / 4f + 0.3f);
				Cc.engineAccel = gearNeutral > 0f ? 0f : AccelInGear(stats, gear, v) * pedal;
				Cc.brakeAccel = 0f;
				return;
			}
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
		}

		/// <summary>The engine's pull in gear <paramref name="g"/> (0-based) at speed <paramref name="v"/>: the torque curve at the engine's share of its top, times the ratio.</summary>
		private static float AccelInGear(CarStats stats, int g, float v)
		{
			if (g < 0 || stats.gearbox == null || stats.torqueCurve == null)
			{
				return 0f;
			}
			float ratio = stats.gearbox.Forward(g + 1);
			float rev = Mathf.Max(0f, v) / Mathf.Max(1f, stats.speedMultiplier) * ratio;
			if (rev >= 1f)
			{
				return 0f;
			}
			return Mathf.Max(0f, stats.torqueCurve.Evaluate(rev) * ratio * stats.engineAccel);
		}

		/// <summary>A hit (the player's guns, rockets, rams, crashes): smoke while it has health, the game's wreck when it has none.</summary>
		internal void Damage(float amount)
		{
			if (!Alive || amount <= 0f)
			{
				return;
			}
			Hurt = true;
			Touched |= Loose;
			Health -= amount;
			if (Health > 0f)
			{
				if (Ec.effectsController != null)
				{
					Ec.effectsController.StartSmokeEmission();
				}
				return;
			}
			Wreck(true);
		}

		/// <summary>A stand-in whose driver wrecked the car: the wreck shown here, where the stand-in is.</summary>
		internal void ShowWreck()
		{
			if (Alive)
			{
				Wreck(false);
			}
		}

		/// <summary>The game's wreck look (flames, the destroy sound), exploding and gone after <see cref="ExplodeAfter"/>; retired from the day if this machine drove it (<paramref name="own"/>).</summary>
		private void Wreck(bool own)
		{
			Alive = false;
			Ec.alive = false;
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
			if (Ec.damageVisuals != null)
			{
				Ec.damageVisuals.SetWrecked();
			}
			if (Ec.effectsController != null)
			{
				Ec.effectsController.StartFlamesEffect();
			}
			if (Ec.audioSource != null)
			{
				Ec.audioSource.Stop();
			}
			if (Headlight != null)
			{
				Headlight.enabled = false;
			}
			SoundController.PlaySoundAtPosition(Ec.destroySound, transform.position, 1f, 25f, 500f);
			if (own)
			{
				Owner?.Wrecked(this);
			}
			StartCoroutine(Explode());
		}

		private IEnumerator Explode()
		{
			yield return new WaitForSeconds(ExplodeAfter);
			try
			{
				if (Ec.effectsController != null)
				{
					Ec.effectsController.StartBigExplosionDestroyedEffect();
				}
				SoundController.PlayRandomSoundAtPosition(Ec.explodedSounds, transform.position, 1f, 25f, 500f);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("A traffic car's explosion failed: " + e.Message);
			}
			Destroy(gameObject);
		}
	}

	/// <summary>
	/// Every hit on an enemy car reaches it through <see cref="EnemyController.TakeDamage(float, bool)"/> (the guns, rockets, rams,
	/// spikes, blasts, the player's crashes). A traffic car takes it as its own (<see cref="TrafficBody.Damage"/>): it wrecks without
	/// counting as a kill (no <c>EnemyKilled</c> signal, so no heal, ammo, kill stats or feats; no lifesteal, combo or "WRECKED"). A hit on
	/// a stand-in goes to the machine that drives the car (in a room), which shows the smoke and the wreck to everybody through its states.
	/// </summary>
	[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.TakeDamage), new[] { typeof(float), typeof(bool) })]
	internal static class TrafficDamagePatch
	{
		private static bool Prefix(EnemyController __instance, float amount)
		{
			if (__instance == null || !__instance.TryGetComponent(out TrafficBody body))
			{
				return true;
			}
			if (body.Remote)
			{
				body.Owner?.RemoteHit(body.Car, amount);
			}
			else
			{
				body.Damage(amount);
			}
			return false;
		}
	}
}
