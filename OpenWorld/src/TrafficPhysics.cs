using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Part of <see cref="TrafficCars"/>: physics cars (the rework's phase 5; the standing decision: every moving car near a player a physics
	/// car). The nearest micro cars drawn near the player (at most <see cref="Mod.TrafficPhysics"/>, within <see cref="PhysicsNear"/>) become
	/// the game's enemy car woken without its AI or guns, driven by code: each stays its micro agent (<see cref="MicroPhysics"/>), so it
	/// decides as every other car does, but the micro reads where it is and how fast it goes back from the body each frame. Its pedals are
	/// worked from the driver's acceleration and its steering follows the way ahead (<see cref="Micro.WayAhead"/>). Nothing is reconciled:
	/// pushed back, slowed on a hill or held up by the player, it is where it is and the cars behind see it there.
	///
	/// <para>A body knocked off its line, on its side or wrecked leaves the day (its household goes without it) and stays where it is as a
	/// loose car on its brakes until the player is well away. One the player drives away from is let go past <see cref="PhysicsFar"/>: its
	/// drawn copy takes over where it is. A body is never put down where it would be in another car.</para>
	///
	/// <para>The body's making, the warm pool, the skin, the lamps and the tyre marks' guards are carried over from the old traffic on
	/// <c>traffic-test-branch</c> (each of its guards fixed something the owner saw); its timetable logic is not (there is none now).</para>
	/// </summary>
	internal sealed partial class TrafficCars
	{
		/// <summary>Within this of the player (units, about 90 m) the nearest micro cars become physics cars; past <see cref="PhysicsFar"/> they are let go.</summary>
		private const float PhysicsNear = 300f, PhysicsFar = 400f;
		/// <summary>A loose car (gone from the day, not a wreck) is taken away once the player is this far from it: past what the fog lets the player
		/// see (<see cref="Micro.Sight"/>), so no car vanishes in sight (PLAN.md: no car is ever hidden).</summary>
		private const float LooseGone = (float)Micro.Sight + 100f;
		/// <summary>How often cars are made physics cars (seconds).</summary>
		private const float TakeEvery = 0.2f;
		/// <summary>Bodies kept woken and spare ahead of need (topped up one a frame).</summary>
		private const int WarmBodies = 3;

		private readonly Dictionary<int, TrafficBody> bodies = new Dictionary<int, TrafficBody>();
		private readonly Stack<TrafficBody> bodySpare = new Stack<TrafficBody>();
		private readonly List<TrafficBody> loose = new List<TrafficBody>();
		private readonly List<int> letGo = new List<int>();
		private readonly List<(float d, int car)> candidates = new List<(float, int)>();
		private readonly Dictionary<(int, int), CarSkin> skins = new Dictionary<(int, int), CarSkin>();
		private readonly Dictionary<int, (double sum, int n)> restHeight = new Dictionary<int, (double, int)>();
		private GameObject bodyHolder;
		private TrafficBody warming;
		private bool physicsFailed;
		private float nextTake;
		private int madeFrame = -1, madePhysical, lostToDay;

		private int MostPhysics => physicsFailed || Mod.TrafficPhysics == null ? 0 : Math.Max(0, Mod.TrafficPhysics.Value);

		/// <summary>Whether car <paramref name="car"/> is a physics car now (its drawn copy hidden).</summary>
		private bool IsPhysical(int car)
		{
			return bodies.ContainsKey(car);
		}

		/// <summary>
		/// Every physics car's body read into its micro agent, before the simulation steps (the reading stands for the time the other cars are
		/// drawn at); every loose car near the player given to the micro's drivers as a body in the road, as the player's car is (they queue
		/// behind a wreck in their lane rather than drive through it).
		/// </summary>
		private void ReadBodies(double now)
		{
			live.OtherBodies.Clear();
			foreach (TrafficBody b in loose)
			{
				if (b == null)
				{
					continue;
				}
				Micro.Reading r = b.Reading(now);
				if ((r.X - live.PlayerX) * (r.X - live.PlayerX) + (r.Z - live.PlayerZ) * (r.Z - live.PlayerZ) < (double)LooseGone * LooseGone)
				{
					live.OtherBodies.Add(new Micro.Body { X = r.X, Z = r.Z, Hx = r.Hx, Hz = r.Hz, Vx = r.Vx, Vz = r.Vz, HalfLong = Traffic.CarLength * 0.5, HalfWide = TrafficParking.CarWide * 0.5 });
				}
			}
			foreach (KeyValuePair<int, TrafficBody> kv in bodies)
			{
				if (kv.Value != null)
				{
					live.Mi.Read(kv.Key, kv.Value.Reading(now - Meso.Step));
				}
			}
		}

		/// <summary>
		/// After the simulation's step: each physics car given its way ahead and the driver's acceleration, or let go (its agent parked or gone
		/// from the day, the player far off, the way into the wrap's tunnel); loose cars taken away once far; the nearest drawn cars made
		/// physics cars.
		/// </summary>
		private void Bodies(double now, double cx, double cz, bool show)
		{
			WarmPool();
			LearnRest();
			letGo.Clear();
			foreach (KeyValuePair<int, TrafficBody> kv in bodies)
			{
				int c = kv.Key;
				TrafficBody b = kv.Value;
				if (b == null)
				{
					// Its body was destroyed under it: the car can't drive on without one.
					letGo.Add(c);
					if (live.Mi.Agents[c] != null && live.Mi.Agents[c].Driven)
					{
						live.Mi.LoseCar(c);
					}
					continue;
				}
				Micro.Agent a = live.Mi.Agents[c];
				Meso.Vehicle v = live.M.Cars[c];
				Origin.World(b.Rb.position, out double x, out double z);
				if (a == null || !a.Driven)
				{
					letGo.Add(c);
					if (v.Mode == Meso.Mode.Gone)
					{
						// Knocked off its line or wrecked: out of the day, left where it is.
						b.Loosen();
						loose.Add(b);
						lostToDay++;
						Mod.Info("Traffic: car " + c + " left its day (" + (b.Alive ? "knocked off the road" : "wrecked") + ") at " + x.ToString("0") + ", " + z.ToString("0"));
					}
					else
					{
						// Parked (its drawn copy slides into its spot from the body), or handed on.
						HandBack(c, b, x, z);
						Release(b);
					}
					continue;
				}
				double dx = x - cx, dz = z - cz;
				int n = live.Mi.WayAhead(c, 4.0, b.WayX, b.WayZ, out bool jump);
				if (!show || jump || dx * dx + dz * dz > (double)PhysicsFar * PhysicsFar)
				{
					live.Mi.GiveBody(c, x, z);
					HandBack(c, b, x, z);
					Release(b);
					letGo.Add(c);
					continue;
				}
				b.Ways = n;
				b.Want = (float)a.A;
			}
			foreach (int c in letGo)
			{
				bodies.Remove(c);
			}
			for (int i = loose.Count - 1; i >= 0; i--)
			{
				TrafficBody b = loose[i];
				if (b == null)
				{
					loose.RemoveAt(i);
					continue;
				}
				Origin.World(b.Rb.position, out double x, out double z);
				if (!show || (x - cx) * (x - cx) + (z - cz) * (z - cz) > (double)LooseGone * LooseGone)
				{
					Destroy(b.gameObject);
					loose.RemoveAt(i);
				}
			}
			if (show && Time.time >= nextTake && MostPhysics > bodies.Count)
			{
				nextTake = Time.time + TakeEvery;
				TakeNearest(now, cx, cz);
			}
		}

		/// <summary>The nearest drawn micro cars within reach made physics cars, each where and as fast as it is drawn, up to <see cref="MostPhysics"/>.</summary>
		private void TakeNearest(double now, double cx, double cz)
		{
			candidates.Clear();
			foreach (int c in live.Mi.Active)
			{
				Micro.Agent a = live.Mi.Agents[c];
				if (a.Driven || (a.Conn >= 0 && Lanes.Connectors[a.Conn].Turn == Lanes.Jump))
				{
					continue;
				}
				if (!drawn.TryGetValue(c, out View v) || !v.Go.activeSelf || v.Blend > 0f || v.LastKind != TrafficLive.Driving || double.IsNaN(v.LastX))
				{
					continue;
				}
				double dx = v.LastX - cx, dz = v.LastZ - cz, d2 = dx * dx + dz * dz;
				if (d2 < (double)PhysicsNear * PhysicsNear)
				{
					candidates.Add(((float)Math.Sqrt(d2), c));
				}
			}
			candidates.Sort((p, q) => p.d != q.d ? p.d.CompareTo(q.d) : p.car.CompareTo(q.car));
			foreach ((float _, int c) in candidates)
			{
				if (bodies.Count >= MostPhysics || bodySpare.Count == 0)
				{
					break;
				}
				View v = drawn[c];
				if (Crowded(c, v))
				{
					continue;
				}
				Micro.Agent a = live.Mi.Agents[c];
				TrafficBody b = bodySpare.Pop();
				Wears(c, out int look, out int paint);
				if (b.Skin != null)
				{
					b.Skin.ApplySkin(SkinFor(look, paint));
				}
				b.LookIndex = look;
				Vector3 fwd = v.Root.forward;
				b.Place(RestPlace(v.Root.position, look, 0.15f), v.Root.rotation);
				b.Begin(this, c, fwd * (float)a.V);
				LearnStats(b);
				int n = live.Mi.WayAhead(c, 4.0, b.WayX, b.WayZ, out bool _);
				b.Ways = n;
				b.Want = (float)a.A;
				if (!live.Mi.TakeBody(c, b.Reading(now - Meso.Step)))
				{
					Release(b);
					continue;
				}
				v.Go.SetActive(false);
				if (v.Light != null)
				{
					v.Light.enabled = false;
				}
				bodies[c] = b;
				madePhysical++;
			}
		}

		/// <summary>Whether car <paramref name="c"/>'s drawn pose is in (or right against) another car drawn or a body: a body put there would burst out of it.</summary>
		private bool Crowded(int c, View v)
		{
			TrafficParking.Spot me = new TrafficParking.Spot { X = v.LastX, Z = v.LastZ, Hx = v.LastHx, Hz = v.LastHz };
			foreach (KeyValuePair<int, View> kv in drawn)
			{
				View o = kv.Value;
				if (kv.Key == c || double.IsNaN(o.LastX) || !o.Go.activeSelf || (o.LastX - me.X) * (o.LastX - me.X) + (o.LastZ - me.Z) * (o.LastZ - me.Z) > 40.0 * 40.0)
				{
					continue;
				}
				if (TrafficParking.Overlap(me, new TrafficParking.Spot { X = o.LastX, Z = o.LastZ, Hx = o.LastHx, Hz = o.LastHz }, 0.5))
				{
					return true;
				}
			}
			foreach (TrafficBody b in Neighbours())
			{
				Origin.World(b.Rb.position, out double x, out double z);
				Vector3 f = b.transform.forward;
				float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
				if ((x - me.X) * (x - me.X) + (z - me.Z) * (z - me.Z) < 40.0 * 40.0
					&& TrafficParking.Overlap(me, new TrafficParking.Spot { X = x, Z = z, Hx = f.x / fl, Hz = f.z / fl }, 0.5))
				{
					return true;
				}
			}
			return false;
		}

		private IEnumerable<TrafficBody> Neighbours()
		{
			foreach (TrafficBody b in bodies.Values)
			{
				if (b != null)
				{
					yield return b;
				}
			}
			foreach (TrafficBody b in loose)
			{
				if (b != null)
				{
					yield return b;
				}
			}
		}

		/// <summary>The drawn copy of car <paramref name="c"/> takes over from its body: drawn last where the body is, so pulling into its spot slides from there.</summary>
		private void HandBack(int c, TrafficBody b, double x, double z)
		{
			if (!drawn.TryGetValue(c, out View v))
			{
				return;
			}
			Vector3 f = b.transform.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			v.LastX = x;
			v.LastZ = z;
			v.LastHx = f.x / fl;
			v.LastHz = f.z / fl;
			v.LastKind = TrafficLive.Driving;
			v.PlacedX = double.NaN;
		}

		/// <summary>A body wrecked (its health gone): out of the day at once.</summary>
		internal void Wrecked(TrafficBody b)
		{
			if (live != null && b.Car >= 0 && bodies.TryGetValue(b.Car, out TrafficBody mine) && mine == b && live.Mi.Agents[b.Car] != null)
			{
				live.Mi.LoseCar(b.Car);
			}
		}

		/// <summary>Every physics car let go (the traffic switched off, or stopped): its drawn copy takes over, and every loose car goes.</summary>
		private void ClearPhysics()
		{
			foreach (KeyValuePair<int, TrafficBody> kv in bodies)
			{
				TrafficBody b = kv.Value;
				if (b == null)
				{
					continue;
				}
				Origin.World(b.Rb.position, out double x, out double z);
				if (live != null)
				{
					live.Mi.GiveBody(kv.Key, x, z);
				}
				HandBack(kv.Key, b, x, z);
				Release(b);
			}
			bodies.Clear();
			foreach (TrafficBody b in loose)
			{
				if (b != null)
				{
					Destroy(b.gameObject);
				}
			}
			loose.Clear();
		}

		/// <summary>The overlay's physics part.</summary>
		private string PhysicsStatus()
		{
			if (MostPhysics == 0 && madePhysical == 0)
			{
				return physicsFailed ? "; physics cars failed" : "";
			}
			return "; physics " + bodies.Count + "/" + MostPhysics + " (made " + madePhysical + ", " + loose.Count + " loose, " + lostToDay + " gone from their day)";
		}

		// ---- the bodies --------------------------------------------------------------------------------------------------------

		/// <summary>
		/// At the start and as bodies are used, one frame at a time, a body made and woken far under the world (the Instantiate and the game's
		/// Awakes and Starts paid for now), then put in the pool asleep. A body is only ever put to use woken: the game's controller takes its
		/// wheels' places in its <c>Start</c>, the frame after the body is first switched on, so a body made and placed at once had its wheels
		/// moved first, widened again by 1.1 and lowered (the owner, 2026-11-23, on the old traffic).
		/// </summary>
		private void WarmPool()
		{
			if (warming != null)
			{
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
			if (MostPhysics == 0 || bodySpare.Count >= WarmBodies || bodies.Count + bodySpare.Count >= MostPhysics + WarmBodies || tm.enemyCarPrefab == null || madeFrame == Time.frameCount)
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

		/// <summary>The first body driven: what its controller has, for the log (the owner's log says what the pedals and steering work against).</summary>
		private bool statsLogged;

		private void LearnStats(TrafficBody b)
		{
			if (statsLogged || b.Cc == null || b.Cc.carStats == null)
			{
				return;
			}
			statsLogged = true;
			CarStats s = b.Cc.carStats;
			Mod.Log.LogInfo("Traffic: physics car bodies: mass " + b.Rb.mass.ToString("0") + ", engine " + s.engineAccel.ToString("0.0") + ", brakes " + s.brakeDeccel.ToString("0.0") + ", wheelbase " + s.wheelbase.ToString("0.00"));
		}

		/// <summary>
		/// A new physics car: an instance of the enemy car prefab made under an inactive holder, so nothing on it has woken yet, and prepared
		/// before it first wakes: the racing AI off (its Start would fit guns and look for a lap; with it off Start never runs), any module or
		/// gun off, the pit guard off, the headlight ours to switch (the game's controller removed, so the sky's lights don't turn on every one),
		/// the skin given our paint and a name of its own (so the race's enemies' names are not used up).
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
			Mod.Info("Traffic: a physics car's body made (" + (bodies.Count + bodySpare.Count + 1) + " in use or spare)");
			return b;
		}

		/// <summary>Our paint as the game's skin: the model, the whole body the paint, the accents a shade of it, the trim dark (as the drawn copies).</summary>
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

		/// <summary>
		/// Each body model's height over the ground standing on its springs, learned from the physics cars driving slowly upright: a body put
		/// down at that height doesn't drop onto its springs and bounce (the owner saw it on the old traffic). Until known, a little up.
		/// </summary>
		private void LearnRest()
		{
			foreach (TrafficBody b in bodies.Values)
			{
				if (b == null || !b.Alive || b.Age < 1.2f || b.Rb.linearVelocity.sqrMagnitude > 9f || b.Rb.angularVelocity.sqrMagnitude > 0.04f || b.transform.up.y < 0.97f)
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
				if (r.n >= 40)
				{
					r.sum -= r.sum / r.n;
					r.n--;
				}
				r.sum += h;
				r.n++;
				restHeight[b.LookIndex] = r;
			}
		}

		/// <summary>Where to put a body of model <paramref name="look"/> standing in for a drawn car at <paramref name="drawn"/>: over the ground at its rest height once known, else <paramref name="drop"/> up.</summary>
		private Vector3 RestPlace(Vector3 at, int look, float drop)
		{
			if (restHeight.TryGetValue(look, out (double sum, int n) r) && r.n >= 3 && Ground(at, at.y + 3f, out float gy))
			{
				return new Vector3(at.x, gy + (float)(r.sum / r.n), at.z);
			}
			return at + Vector3.up * drop;
		}
	}

	/// <summary>
	/// One physics traffic car: the game's enemy car driven by its micro agent. Each physics step it steers by pure pursuit for a point along
	/// its way ahead (further the faster it goes) and works its engine and brake from the driver's acceleration, less what the hill and the
	/// rolling take; the micro reads back where it got to. Its health is its own (the game's enemy health is private), and a wreck is the
	/// game's wreck without a kill: no heal, ammo, kill count or combo (wrecks aren't kills: the owner's standing decision).
	/// </summary>
	internal sealed class TrafficBody : MonoBehaviour
	{
		/// <summary>How long a wreck burns before it explodes and goes (the game's enemy car's).</summary>
		private const float ExplodeAfter = 5f;

		internal TrafficCars Owner;
		internal CarControllerNew Cc;
		internal Rigidbody Rb;
		internal EnemyController Ec;
		internal CarSkinController Skin;
		internal Light Headlight;

		/// <summary>The day's car it is (-1: asleep), its body model, how long since it was put down (seconds of physics), its health.</summary>
		internal int Car = -1, LookIndex;
		internal float Age, Health = 100f;
		internal bool Alive = true;
		/// <summary>Out of the day (knocked off the road or wrecked): no driver, on its brakes.</summary>
		internal bool Loose;

		/// <summary>Its way ahead (world x, z, a point every 4 units from its middle) and how many points; the driver's acceleration (units a second²).</summary>
		internal readonly double[] WayX = new double[16], WayZ = new double[16];
		internal int Ways;
		internal float Want;

		private float steer;
		private bool woken, smoking;

		/// <summary>Its reading for the micro: middle, way faced, velocity, upright; standing for day second <paramref name="at"/>.</summary>
		internal Micro.Reading Reading(double at)
		{
			Origin.World(Rb.position, out double x, out double z);
			Vector3 f = transform.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			Vector3 vel = Rb.linearVelocity;
			return new Micro.Reading { X = x, Z = z, Hx = f.x / fl, Hz = f.z / fl, Vx = vel.x, Vz = vel.z, At = at, Upright = Alive && transform.up.y > 0.5f };
		}

		/// <summary>
		/// Puts the body at scene <paramref name="pos"/> turned <paramref name="turn"/>, awake, its rigidbody moved there too (a teleport, so
		/// neither physics nor its interpolation starts from where it last was), and its tyre marks put out for a moment.
		/// </summary>
		internal void Place(Vector3 pos, Quaternion turn)
		{
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
		}

		/// <summary>Starts driving car <paramref name="car"/>, moving at <paramref name="velocity"/>.</summary>
		internal void Begin(TrafficCars owner, int car, Vector3 velocity)
		{
			Owner = owner;
			Car = car;
			Loose = false;
			Age = 0f;
			Rb.isKinematic = false;
			if (!woken)
			{
				// Its first waking ran the game's Awakes: the enemy AI set the player's stats on the controller. A plain car's instead.
				woken = true;
				Cc.carStats = new CarStats();
			}
			Health = Mathf.Max(1f, Cc.carStats.health);
			Alive = true;
			smoking = false;
			Ec.alive = true;
			steer = 0f;
			Rb.linearVelocity = velocity;
			Rb.angularVelocity = Vector3.zero;
			Cc.engineAccel = 0f;
			Cc.brakeAccel = 0f;
			Cc.steeringAngleDeg = 0f;
			Lamps(true);
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

		/// <summary>Out of the day: no driver from now on, its brakes on, its engine off (lamps still lit: it stopped where it was).</summary>
		internal void Loosen()
		{
			Loose = true;
			if (Ec.audioSource != null)
			{
				Ec.audioSource.Stop();
			}
			if (Headlight != null)
			{
				Headlight.enabled = false;
			}
		}

		/// <summary>Stops driving (back to the pool).</summary>
		internal void End()
		{
			Car = -1;
			Loose = false;
			ClearTrails();
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
			if (Car < 0 && !Loose)
			{
				return;
			}
			float dt = Time.fixedDeltaTime;
			Age += dt;
			if (Loose || !Alive)
			{
				Cc.engineAccel = 0f;
				Cc.brakeAccel = Alive ? Cc.carStats.brakeDeccel : 0f;
				Cc.steeringAngleDeg = 0f;
				return;
			}
			Steer(dt);
			Pedals(dt);
		}

		/// <summary>
		/// Pure pursuit: the point along its way ahead a look-ahead off (10 units standing, more the faster), the arc through it from the rear
		/// axle, the front wheels' angle for that arc (the bicycle model on the car's wheelbase), smoothed.
		/// </summary>
		private void Steer(float dt)
		{
			if (Ways == 0)
			{
				steer = Mathf.Lerp(steer, 0f, Mathf.Clamp01(dt * 8f));
				Cc.steeringAngleDeg = steer;
				return;
			}
			Origin.World(Rb.position, out double x, out double z);
			Vector3 f = transform.forward;
			float fl = Mathf.Max(1e-4f, Mathf.Sqrt(f.x * f.x + f.z * f.z));
			double hx = f.x / fl, hz = f.z / fl;
			float v = Mathf.Max(0f, Cc.forwardSpeed);
			double look = Math.Max(10.0, Math.Min(45.0, 8.0 + 0.6 * v));
			int k = 0;
			while (k + 1 < Ways && (WayX[k] - x) * (WayX[k] - x) + (WayZ[k] - z) * (WayZ[k] - z) < look * look)
			{
				k++;
			}
			double tx = WayX[k] - x, tz = WayZ[k] - z, tl = Math.Sqrt(tx * tx + tz * tz);
			if (tl < 1.0)
			{
				return;
			}
			// Signed: the game steers right with a positive angle (Unity's left-handed y up), as SignedAngle about up measures it.
			double cross = hz * tx - hx * tz, dot = hx * tx + hz * tz;
			double alpha = Math.Atan2(cross / tl, dot / tl);
			double wheelbase = Math.Max(2.0, Cc.carStats != null ? Cc.carStats.wheelbase : 5.25);
			float want = (float)(Math.Atan2(2.0 * wheelbase * Math.Sin(alpha), Math.Max(tl, 6.0)) * 180.0 / Math.PI);
			want = Mathf.Clamp(want, -40f, 40f);
			steer = Mathf.Lerp(steer, want, Mathf.Clamp01(dt * 10f));
			Cc.steeringAngleDeg = steer;
		}

		/// <summary>
		/// Engine and brake for the driver's acceleration: what the car needs beyond the hill (gravity along it) and the rolling (the game's
		/// controller always takes about a unit a second² off a rolling car); standing and asked for none, held on the brake.
		/// </summary>
		private void Pedals(float dt)
		{
			CarStats stats = Cc.carStats;
			float v = Cc.forwardSpeed;
			float a = Want;
			if (a <= 0.05f && v < 0.6f)
			{
				Cc.engineAccel = 0f;
				Cc.brakeAccel = stats.brakeDeccel;
				return;
			}
			float hill = -Vector3.Dot(Physics.gravity, transform.forward);
			float need = a + hill + (v > 0.3f ? 1f : 0f);
			if (need > 0f)
			{
				Cc.engineAccel = Mathf.Min(need, (float)Driver.Accel * 3f);
				Cc.brakeAccel = 0f;
			}
			else
			{
				Cc.engineAccel = 0f;
				Cc.brakeAccel = Mathf.Min(stats.brakeDeccel, -need);
			}
		}

		/// <summary>A hit (the player's guns, rockets, rams, crashes): smoke while it has health, the game's wreck when it has none.</summary>
		internal void Damage(float amount)
		{
			if (!Alive || amount <= 0f)
			{
				return;
			}
			Health -= amount;
			if (Health > 0f)
			{
				if (!smoking && Ec.effectsController != null)
				{
					smoking = true;
					Ec.effectsController.StartSmokeEmission();
				}
				return;
			}
			Wreck();
		}

		/// <summary>The game's wreck look (flames, the destroy sound), exploding and gone after <see cref="ExplodeAfter"/>; out of the day.</summary>
		private void Wreck()
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
			Owner?.Wrecked(this);
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

		// ---- the lamps -----------------------------------------------------------------------------------------------------------

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
		/// Darkens or lights the lamps. The game's car controller lights the tail lamps whenever the brake is on (it turns their material's
		/// emission keyword on and off); the emission colour is set instead, which the controller never touches.
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

		/// <summary>Its headlight on at night (the nearest few: <see cref="TrafficCars"/> decides).</summary>
		internal void Headlamp(bool on)
		{
			if (Headlight != null && Headlight.enabled != on)
			{
				Headlight.enabled = on;
			}
		}

		// ---- the tyre marks ------------------------------------------------------------------------------------------------------

		private bool interpHeld;
		private RigidbodyInterpolation interpMode;
		private ParticleSystem[] particles;
		private TrailRenderer[] trails;
		/// <summary>How long the tyre marks stay off and empty after the body was put somewhere new, and after interpolation comes back (seconds).</summary>
		private const float TrailsOff = 0.3f, TrailsSettle = 0.15f, ShiftHold = 0.3f;
		/// <summary>Distance (units) past which a move in one frame, or the drawn body from its rigidbody, is a jump.</summary>
		private const float JumpSlack = 4f;
		private float trailsOffFor, trailsSettle;
		private Vector3[] trailLast;
		private int trailShifts = -1;

		/// <summary>
		/// The tyre marks (the game's trail renderers on the wheels, which its effect controller keeps emitting whenever the wheels are on the
		/// ground) forgotten after the body is put somewhere new, and kept off and empty for <see cref="TrailsOff"/>: for a frame or two after
		/// the move the body can still be drawn from the rigidbody's old pose, and the marks drew a line from there across the country.
		/// </summary>
		internal void ClearTrails()
		{
			trails ??= GetComponentsInChildren<TrailRenderer>(true);
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
			trailShifts = -1;
			particles ??= GetComponentsInChildren<ParticleSystem>(true);
			foreach (ParticleSystem ps in particles)
			{
				if (ps != null)
				{
					ps.Clear(false);
				}
			}
		}

		/// <summary>
		/// After the body was placed: its tyre marks kept empty with interpolation held off, then interpolation back on while the marks stay
		/// off a while longer, then the marks back on as the game has them. And every frame the jump guard: over an origin shift, or if the
		/// drawn body is away from its rigidbody or a wheel moved further than the car's speed allows, the marks are emptied and kept off.
		/// </summary>
		private void LateUpdate()
		{
			if (trails == null)
			{
				return;
			}
			if (trailsOffFor > 0f)
			{
				trailsOffFor -= Time.deltaTime;
				if (trailsOffFor <= 0f && interpHeld)
				{
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
							t.Clear();
						}
					}
				}
			}
			if (trailLast == null || trailLast.Length != trails.Length)
			{
				trailLast = new Vector3[trails.Length];
				trailShifts = -1;
			}
			bool shifted = trailShifts != Origin.Shifts;
			if (shifted && trailShifts >= 0 && trailsOffFor <= 0f)
			{
				TrailsOffNow();
				trailsSettle = Mathf.Max(trailsSettle, ShiftHold);
			}
			trailShifts = Origin.Shifts;
			bool stale = !Rb.isKinematic && (transform.position - Rb.position).sqrMagnitude > JumpSlack * JumpSlack;
			float most = Rb.linearVelocity.magnitude * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime) * 2f + JumpSlack;
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
			if (jumped)
			{
				TrailsOffNow();
				if (trailsOffFor <= 0f)
				{
					trailsSettle = Mathf.Max(trailsSettle, TrailsSettle);
				}
			}
		}

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
	}

	/// <summary>
	/// Every hit on an enemy car reaches it through <see cref="EnemyController.TakeDamage(float, bool)"/> (the guns, rockets, rams, spikes,
	/// blasts, the player's crashes). A traffic car takes it as its own (<see cref="TrafficBody.Damage"/>): it wrecks without counting as a
	/// kill (no <c>EnemyKilled</c> signal, so no heal, ammo, kill stats or feats; no lifesteal, combo or "WRECKED").
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
			body.Damage(amount);
			return false;
		}
	}
}
