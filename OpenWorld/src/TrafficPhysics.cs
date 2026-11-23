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

		/// <summary>Whether car <paramref name="c"/> is a physics car now, here or another machine's stand-in (not drawn by stage 2).</summary>
		private bool IsPhysical(int c) => physical.ContainsKey(c) || (remote.TryGetValue(c, out TrafficBody s) && s != null && s.gameObject.activeSelf);

		/// <summary>Whether any physics car (here or a stand-in) is about.</summary>
		private bool AnyBodies => physical.Count > 0 || remote.Count > 0;

		/// <summary>What the bubble does with a physics car after looking at them all (the room is told after the car has left the list).</summary>
		private enum Leave : byte { Back, Retire, Hand }

		private readonly List<(int car, TrafficBody body, Leave what, TrafficRoom.Op op, ulong to)> acts = new List<(int, TrafficBody, Leave, TrafficRoom.Op, ulong)>();

		/// <summary>
		/// The bubble's going back, done before the choice: each physics car that is out of reach, on the last stretch of its trip or near a
		/// tunnel goes back on its timetable if it can (upright, on its lane, undamaged), late by where it is on its leg; otherwise it stays
		/// (stopped) until the player is out of reach, then is retired.
		/// </summary>
		private void Bubble(double w, double cx, double cz)
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
				TrafficDay.Leg leg = day.Legs[b.Leg];
				Origin.World(b.Rb.position, out double x, out double z);
				double dx = x - cx, dz = z - cz;
				bool far = dx * dx + dz * dz > (double)PhysicsLeave * PhysicsLeave || MostPhysics == 0;
				bool end = !leg.Vanishes && b.P > leg.Length - TrafficDrive.EndZone;
				bool tunnel = TrafficDrive.NearTunnel(leg, b.P + 20.0);
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
				if (!far && !end && !tunnel)
				{
					b.Hold = false;
					continue;
				}
				Vector3 f = b.transform.forward;
				bool fit = b.Alive && !b.Hurt && !b.GaveUp && b.transform.up.y > 0.7f && TrafficDrive.OnLane(leg, b.P, x, z, f.x, f.z);
				if (fit)
				{
					double d = TrafficDrive.Lateness(leg, b.P, w);
					// On the last stretch it waits to be on time rather than be drawn jumping ahead to where the day has it.
					if (!far && d < -1.0)
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
					Mod.Info("Traffic: car " + kv.Key + " back on its timetable " + (d >= TrafficLate.Least ? d.ToString("0.0") + " s late" : "on time") + (far ? " (out of reach)" : end ? " (its trip's end)" : " (a tunnel ahead)"));
					acts.Add((kv.Key, b, Leave.Back, LateOp(b.Leg, b.P, w), 0UL));
					continue;
				}
				if (!far)
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
				if (physical.Count + room.Claims >= MostPhysics)
				{
					return;
				}
				int c = chosen[k];
				if (!chosenMoving.Contains(c) || !live.TryGetValue(c, out View v) || !v.Go.activeSelf || double.IsNaN(v.LastX))
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
			if (MostPhysics == 0 || tm.enemyCarPrefab == null || !live.TryGetValue(c, out View v) || !v.Go.activeSelf || double.IsNaN(v.LastX))
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
			Transform t = b.transform;
			t.SetPositionAndRotation(v.Root.position + Vector3.up * 0.4f, v.Root.rotation);
			if (!b.gameObject.activeSelf)
			{
				b.gameObject.SetActive(true);
			}
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
				// nose of a physics car stopped in a junction).
				if ((side < 4.0 && along > -TrafficLate.Gap - before && along < TrafficLate.Gap + behind) || TrafficDrive.Overlap(x, z, hx, hz, bx, bz, f.x / fl, f.z / fl, 0.5))
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
				if (b.Alive && !b.GaveUp)
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
				bool on = lights && b.Alive && lit < MostLit;
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
			b.End();
			b.gameObject.SetActive(false);
			bodySpare.Push(b);
		}

		/// <summary>Every physics car back on its timetable through the room (the traffic switched off, or stopped), and every stand-in gone.</summary>
		private void ClearPhysics()
		{
			double w = day != null ? SimNow() : 0.0;
			acts.Clear();
			foreach (KeyValuePair<int, TrafficBody> kv in physical)
			{
				TrafficBody b = kv.Value;
				bool back = day != null && b != null && b.Alive && !b.Hurt;
				acts.Add((kv.Key, b, back ? Leave.Back : Leave.Retire, back ? LateOp(b.Leg, b.P, w) : new TrafficRoom.Op { Kind = TrafficRoom.OpKind.Retire, Leg = -1, W = w }, 0UL));
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
			if (MostPhysics == 0 && madePhysical == 0)
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
			return "; physics " + physical.Count + "/" + MostPhysics + " (" + waiting + " giving way, " + holding + " stopped), made " + madePhysical + ", back " + backOnTime + " on time and " + backLate + " late, wrecked "
				+ retiredWrecked + ", retired " + retiredOff + ", hidden " + hiddenByPhysics + ", held back " + heldBack + RoomStatus();
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

		internal float Health = 100f;
		internal bool Alive = true, Hurt, GaveUp;
		/// <summary>Told by the bubble to stop and wait (it can't go back on its timetable where it is).</summary>
		internal bool Hold;
		/// <summary>A stand-in for a car another machine drives (kinematic, played back from its states).</summary>
		internal bool Remote;
		/// <summary>A stand-in whose driver has it stopped (held, or given up): a wall to the drivers here, not a mover.</summary>
		internal bool Stopped;
		internal readonly TrafficRoom.Playback Play = new TrafficRoom.Playback();
		/// <summary>A car just handed on carries on as it was going until its new driver's states come (at most the playback's coast).</summary>
		private Vector3 coastVelocity;
		private float coastLeft;
		private bool smoking;
		internal TrafficDrive.State Drive;
		internal TrafficDrive.Mover Me;
		private float offFor, steer;
		private int gear = -1, pending = -1;
		private float gearHold, gearNeutral;
		private bool woken;

		/// <summary>Starts driving car <paramref name="car"/> on leg <paramref name="leg"/> at <paramref name="p"/>, moving at <paramref name="velocity"/>.</summary>
		internal void Begin(TrafficCars owner, int car, int leg, double p, Vector3 velocity)
		{
			Remote = false;
			Stopped = false;
			Rb.isKinematic = false;
			Owner = owner;
			Car = car;
			Leg = leg;
			P = p;
			profile = TrafficDay.Profile(owner.Day.Legs[leg]);
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
			Engine(car);
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
			Car = -1;
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
			if (Remote)
			{
				StandIn();
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
			double target = TrafficDrive.Target(day, Me, profile, Owner.Movers, Owner.Obstacles, onP, onV, dt, ref Drive);
			Steer(leg, pos, v, dt);
			Pedals(target, v, dt);
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
			Rb.MovePosition(Origin.Local(s.X, s.Y, s.Z));
			Quaternion q = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
			Rb.MoveRotation(q);
			Vector3 f = q * Vector3.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			Leg = s.Leg;
			P = s.P;
			Stopped = s.Stopped;
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
			TrafficDrive.Line(leg, P + ahead, out double tx, out double tz, out double _, out double _);
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
