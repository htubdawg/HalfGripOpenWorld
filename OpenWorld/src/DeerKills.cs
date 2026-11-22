using System;
using System.Collections.Generic;
using System.IO;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The deer killed in one race, by a car or a gun, and how each body moves after: thrown by a car it flies, tumbling, lands
	/// (bouncing once if it came down hard), slides to a stop and lies on its side; shot it falls over sideways where it stood. A kill
	/// is found by the machine whose car or gun did it (<c>Deer</c>) and settled by the room's host (<see cref="DeerRoom"/>: the first
	/// claim on a deer is its kill); every machine plays the same body from the same start on the race clock (<see cref="Body.At"/>),
	/// so a deer one player hits lies dead in the same place on every screen, also for a player who joins later.
	///
	/// No Unity types: the body's motion is in world coordinates and doubles, on fixed steps from its start, so it comes out the same
	/// however long the frames are (<c>tools/meshcheck</c> checks it lands on the ground and stays there).
	/// </summary>
	internal sealed class DeerKills
	{
		/// <summary>The first byte of a deer's message, told apart from the events' (which start with their version, a small number).</summary>
		internal const byte Tag = DeerRoom.Tag;

		/// <summary>The kill's layout. Raise it with any change to what it carries.</summary>
		internal const byte Version = 2;

		/// <summary>A grown doe's body centre above her hooves, and how far it is off the ground when she lies on her side (units).</summary>
		internal const double CentreHeight = 2.37, Lift = 0.55;

		/// <summary>The steps a body is moved on (seconds), and the longest it moves before it is laid still wherever it is.</summary>
		internal const double Step = 1.0 / 60.0, Longest = 20.0;

		/// <summary>How fast a sliding body loses speed (units a second, a second), and how long a landed body takes to roll onto its side.</summary>
		internal const double Friction = 22.0, Settle = 0.35;

		/// <summary>The fastest a body is thrown up (units a second; a deer goes over a bonnet and down the road, not into the air), the
		/// speed it must come down at to bounce, and what a bounce keeps of its way up and along.</summary>
		internal const double MostLaunch = 9.0, BounceFrom = 9.0, BounceUp = 0.18, BounceAlong = 0.45;

		/// <summary>How long a shot deer takes to fall over.</summary>
		internal const double FallSeconds = 0.75;

		internal enum How : byte
		{
			Car,
			Shot,
		}

		/// <summary>One kill, as sent: which deer, how, and where its body starts.</summary>
		internal struct Kill
		{
			public int Seed, Group, Member;
			public How How;
			/// <summary>The race time it was killed at (the room's race clock, the host's on every machine).</summary>
			public double T;
			/// <summary>The body's centre (world x and z, scene height), its velocity, the way it faced (radians from +x toward +z), its size.</summary>
			public double X, Y, Z, Vx, Vy, Vz, Yaw, Scale;
			/// <summary>How fast it tumbles about its length (radians a second; plus rolls it toward its right) and turns about the up axis.</summary>
			public double Spin, Turn;
			/// <summary>Shot: +1 it falls to its right, -1 to its left.</summary>
			public int Side;
		}

		/// <summary>Where a body is at a moment: its centre, the way it faces, how far it has rolled toward its right, and whether it is down.</summary>
		internal struct Pose
		{
			public double X, Y, Z, Yaw, Roll;
			/// <summary>On the ground (sliding or still), and from 0 (in the air or falling) to 1 (lying limp): for the legs' pose.</summary>
			public bool Down;
			public double Limp;
		}

		/// <summary>
		/// A dead deer's body: the kill and its motion, moved on in <see cref="Step"/>s from the race time it was killed at to the time
		/// asked (kept, so each frame moves it only on from the last).
		/// </summary>
		internal sealed class Body
		{
			public readonly Kill Kill;
			public readonly double Start;
			private double time, x, y, z, vx, vy, vz, yaw, roll, spin, turn, landed = -1.0, rollFrom, rollTo;
			private bool bounced, still;

			internal Body(Kill kill, double start)
			{
				Kill = kill;
				Start = start;
				x = kill.X;
				y = kill.Y;
				z = kill.Z;
				vx = kill.Vx;
				vy = kill.Vy;
				vz = kill.Vz;
				yaw = kill.Yaw;
				spin = kill.Spin;
				turn = kill.Turn;
			}

			/// <summary>Whether it has stopped (lies still for good).</summary>
			internal bool Still => still;

			/// <summary>
			/// Where the body is at race time <paramref name="t"/>, over the ground <paramref name="ground"/> (scene height at world x, z),
			/// falling at <paramref name="gravity"/> (units a second, a second; positive). Times before the last asked give the last.
			/// </summary>
			internal Pose At(double t, Func<double, double, double> ground, double gravity)
			{
				double s = Kill.Scale > 0.0 ? Kill.Scale : 1.0;
				if (Kill.How == How.Shot)
				{
					return Falling(t, ground, s);
				}
				double lift = Lift * s;
				double until = Math.Min(t - Start, Longest);
				while (!still && time + Step <= until + 1e-9)
				{
					time += Step;
					if (landed < 0.0)
					{
						vy -= gravity * Step;
						x += vx * Step;
						y += vy * Step;
						z += vz * Step;
						roll += spin * Step;
						yaw += turn * Step;
						double g = ground(x, z);
						if (y - lift <= g && vy >= 0.0)
						{
							// Still rising into ground rising faster (thrown up a bank): carried up over it.
							y = g + lift;
						}
						else if (y - lift <= g)
						{
							y = g + lift;
							if (!bounced && -vy > BounceFrom)
							{
								// Down hard: one low bounce, losing most of its way (a limp body, not a ball).
								bounced = true;
								vy = -BounceUp * vy;
								vx *= BounceAlong;
								vz *= BounceAlong;
								spin *= 0.35;
								turn *= 0.35;
							}
							else
							{
								landed = time;
								vy = 0.0;
								rollFrom = roll;
								rollTo = Side(roll);
							}
						}
					}
					else
					{
						double h = Math.Sqrt(vx * vx + vz * vz);
						double slow = Friction * Step;
						if (h <= slow)
						{
							vx = 0.0;
							vz = 0.0;
						}
						else
						{
							vx -= vx / h * slow;
							vz -= vz / h * slow;
						}
						x += vx * Step;
						z += vz * Step;
						y = ground(x, z) + lift;
						double k = Math.Min(1.0, (time - landed) / Settle);
						roll = rollFrom + (rollTo - rollFrom) * (k * k * (3.0 - 2.0 * k));
						if (vx == 0.0 && vz == 0.0 && k >= 1.0)
						{
							still = true;
						}
					}
				}
				if (!still && t - Start >= Longest)
				{
					// Never left moving: laid down where it is.
					still = true;
					y = ground(x, z) + lift;
					roll = Side(roll);
					landed = landed < 0.0 ? time : landed;
				}
				return new Pose
				{
					X = x, Y = y, Z = z, Yaw = yaw, Roll = roll,
					Down = landed >= 0.0,
					Limp = landed < 0.0 ? 0.0 : Math.Min(1.0, (time - landed) / (Settle * 2.0)),
				};
			}

			/// <summary>Shot: it falls over sideways about its hooves, quicker as it goes, and lies where it stood.</summary>
			private Pose Falling(double t, Func<double, double, double> ground, double s)
			{
				double k = Math.Max(0.0, Math.Min(1.0, (t - Start) / FallSeconds));
				double r = Kill.Side * (Math.PI / 2.0) * k * k;
				// The hooves stay where they were; the centre swings over them, down to lying height.
				double feetX = Kill.X, feetZ = Kill.Z;
				double rightX = DetMath.Sin(Kill.Yaw), rightZ = -DetMath.Cos(Kill.Yaw);
				double a = Math.Abs(r);
				double side = CentreHeight * s * DetMath.Sin(a) * Kill.Side;
				double cx = feetX + rightX * side, cz = feetZ + rightZ * side;
				// The ground under the body: under the hooves standing, under its middle lying.
				double sa = DetMath.Sin(a);
				double g = ground(feetX, feetZ) * (1.0 - sa) + ground(cx, cz) * sa;
				double y = g + CentreHeight * s * DetMath.Cos(a) + Lift * s * sa;
				if (k >= 1.0)
				{
					still = true;
				}
				return new Pose { X = cx, Y = y, Z = cz, Yaw = Kill.Yaw, Roll = r, Down = k >= 1.0, Limp = k * k };
			}

			/// <summary>The roll nearest to <paramref name="r"/> at which the body lies on one side or the other.</summary>
			private static double Side(double r)
			{
				double turn = 2.0 * Math.PI;
				double right = Math.PI / 2.0 + turn * Math.Floor((r - Math.PI / 2.0) / turn + 0.5);
				double left = -Math.PI / 2.0 + turn * Math.Floor((r + Math.PI / 2.0) / turn + 0.5);
				return Math.Abs(r - right) <= Math.Abs(r - left) ? right : left;
			}
		}

		/// <summary>The race's seed the kills are for: a kill for another race (a message late from the last) is dropped.</summary>
		internal readonly int Seed;

		private readonly Dictionary<int, Body> dead = new Dictionary<int, Body>();

		internal DeerKills(int seed)
		{
			Seed = seed;
		}

		internal static int Key(int group, int member)
		{
			return group * 64 + member;
		}

		/// <summary>The dead deer's bodies, by <see cref="Key"/>.</summary>
		internal IReadOnlyDictionary<int, Body> Dead => dead;

		internal int Count => dead.Count;

		internal bool IsDead(int group, int member)
		{
			return dead.ContainsKey(Key(group, member));
		}

		/// <summary>
		/// Takes a kill (its body starting at its race time), in place of any this deer had (the host's put in place of this machine's
		/// own); false (nothing changed) for another race's.
		/// </summary>
		internal bool Set(Kill kill)
		{
			if (kill.Seed != Seed || kill.Group < 0 || kill.Member < 0 || kill.Member >= 64)
			{
				return false;
			}
			dead[Key(kill.Group, kill.Member)] = new Body(kill, kill.T);
			return true;
		}

		// ---- the messages ----------------------------------------------------------------------------------------

		internal static byte[] Encode(Kill k)
		{
			using (MemoryStream m = new MemoryStream(96))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				w.Write(Tag);
				w.Write(Version);
				w.Write(k.Seed);
				w.Write((short)k.Group);
				w.Write((byte)k.Member);
				w.Write((byte)k.How);
				w.Write(k.T);
				w.Write(k.X);
				w.Write(k.Z);
				w.Write((float)k.Y);
				w.Write((float)k.Vx);
				w.Write((float)k.Vy);
				w.Write((float)k.Vz);
				w.Write((float)k.Yaw);
				w.Write((float)k.Scale);
				w.Write((float)k.Spin);
				w.Write((float)k.Turn);
				w.Write((sbyte)k.Side);
				return m.ToArray();
			}
		}

		internal static bool Decode(byte[] data, out Kill k)
		{
			k = default;
			if (data == null || data.Length < 2 || data[0] != Tag || data[1] != Version)
			{
				return false;
			}
			try
			{
				using (MemoryStream m = new MemoryStream(data, 2, data.Length - 2))
				using (BinaryReader r = new BinaryReader(m))
				{
					k.Seed = r.ReadInt32();
					k.Group = r.ReadInt16();
					k.Member = r.ReadByte();
					byte how = r.ReadByte();
					if (how > (byte)How.Shot)
					{
						return false;
					}
					k.How = (How)how;
					k.T = r.ReadDouble();
					k.X = r.ReadDouble();
					k.Z = r.ReadDouble();
					k.Y = r.ReadSingle();
					k.Vx = r.ReadSingle();
					k.Vy = r.ReadSingle();
					k.Vz = r.ReadSingle();
					k.Yaw = r.ReadSingle();
					k.Scale = r.ReadSingle();
					k.Spin = r.ReadSingle();
					k.Turn = r.ReadSingle();
					k.Side = r.ReadSByte() < 0 ? -1 : 1;
					return !(double.IsNaN(k.T) || double.IsNaN(k.X) || double.IsNaN(k.Z) || double.IsNaN(k.Y) || double.IsInfinity(k.X) || double.IsInfinity(k.Z));
				}
			}
			catch (EndOfStreamException)
			{
				return false;
			}
		}
	}
}
