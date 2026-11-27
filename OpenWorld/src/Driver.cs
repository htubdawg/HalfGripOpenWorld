using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The one driver model of every micro car (the traffic rework's phase 4, <c>docs/TRAFFIC_REWORK.md</c> §4): how hard a driver
	/// accelerates or brakes for the road ahead, the car ahead, a stop line, and whether a lane change is safe and worth it. Following is
	/// the Intelligent Driver Model (Treiber, Hennecke and Helbing 2000): one formula that keeps a time gap behind the car ahead, closes up
	/// smoothly and stops a car's length and a little behind a standing one, which is also how a stop line is driven to (a standing
	/// obstacle there). Lane changes are MOBIL (Kesting, Treiber and Helbing 2007): safe if the new follower needn't brake harder than
	/// <see cref="SafeBrake"/>, worth it if the driver gains more than <see cref="Threshold"/> after a share (<see cref="Politeness"/>)
	/// of what the others lose, keeping right. The figures are a car's at real scale through 3.3 units a metre. Pure functions, no state:
	/// <see cref="Micro"/> calls them. No Unity types.
	/// </summary>
	internal static class Driver
	{
		/// <summary>The most a driver accelerates (1.5 m/s², an ordinary car pulling away briskly but not hard).</summary>
		internal const double Accel = 1.5 * 3.3;
		/// <summary>The braking a driver is comfortable with (2 m/s²): IDM's b, and what an amber light is judged on.</summary>
		internal const double Brake = 2.0 * 3.3;
		/// <summary>The hardest a car brakes (8 m/s², a full stop on a dry road): no driver brakes harder.</summary>
		internal const double MaxBrake = 8.0 * 3.3;
		/// <summary>The gap left standing behind the car ahead (units): about 0.75 m, the meso's <see cref="Meso.Jam"/> less a car.</summary>
		internal const double Gap0 = Meso.Jam - Traffic.CarLength;
		/// <summary>The time gap kept behind the car ahead (seconds): 1.5 s (PLAN.md: 2 s at most).</summary>
		internal const double TimeGap = 1.5;
		/// <summary>The safe braking a lane change may ask of the new follower (4 m/s²).</summary>
		internal const double SafeBrake = 4.0 * 3.3;
		/// <summary>MOBIL's politeness: how much of the others' loss a driver counts against its own gain.</summary>
		internal const double Politeness = 0.3;
		/// <summary>The least gain worth a lane change (0.2 m/s²), and the bias to keep right (passing on the left only when it pays).</summary>
		internal const double Threshold = 0.2 * 3.3, KeepRight = 0.3 * 3.3;
		/// <summary>How long a lane change takes, side to side (seconds).</summary>
		internal const double ChangeTime = 3.0;

		/// <summary>The acceleration on a free road at speed <paramref name="v"/> wanting <paramref name="v0"/>.</summary>
		internal static double Free(double v, double v0)
		{
			if (v0 <= 0.0)
			{
				return -MaxBrake;
			}
			double r = v / v0, r2 = r * r;
			return Math.Max(-MaxBrake, Accel * (1.0 - r2 * r2));
		}

		/// <summary>
		/// IDM's acceleration at speed <paramref name="v"/> wanting <paramref name="v0"/>, <paramref name="gap"/> (bumper to bumper) behind
		/// a car going <paramref name="vLead"/>, never braking harder than a car can.
		/// </summary>
		internal static double Follow(double v, double v0, double gap, double vLead)
		{
			double dv = v - vLead;
			double want = Gap0 + Math.Max(0.0, v * TimeGap + v * dv / (2.0 * Math.Sqrt(Accel * Brake)));
			double g = Math.Max(gap, 0.01);
			double r = want / g;
			return Math.Max(-MaxBrake, Free(v, v0) - Accel * r * r);
		}

		/// <summary>The acceleration that stops a car going <paramref name="v"/> at a line <paramref name="dist"/> ahead (a standing obstacle there, less the standing gap).</summary>
		internal static double StopAt(double v, double v0, double dist)
		{
			return Follow(v, v0, dist + Gap0, 0.0);
		}

		/// <summary>The acceleration that brings a car going <paramref name="v"/> down to <paramref name="vMax"/> by <paramref name="dist"/> ahead (a slower bend, a turn): 0 or less, only once it has to.</summary>
		internal static double SlowTo(double v, double vMax, double dist)
		{
			if (v <= vMax)
			{
				return double.MaxValue;
			}
			double need = (v * v - vMax * vMax) / (2.0 * Math.Max(dist, 0.5));
			return need < Brake * 0.5 ? double.MaxValue : -Math.Min(need, MaxBrake);
		}

		/// <summary>The distance a car going <paramref name="v"/> needs to stop braking at <paramref name="b"/>.</summary>
		internal static double Stopping(double v, double b)
		{
			return v * v / (2.0 * b);
		}

		/// <summary>
		/// The soonest a car going <paramref name="v"/> covers <paramref name="dist"/>, pulling away at <see cref="Accel"/> up to
		/// <paramref name="vMax"/> (or keeping its speed if already faster): when it can be somewhere at the earliest.
		/// </summary>
		internal static double Soonest(double dist, double v, double vMax)
		{
			if (dist <= 0.0)
			{
				return 0.0;
			}
			if (v >= vMax)
			{
				return dist / Math.Max(v, 0.1);
			}
			double tUp = (vMax - v) / Accel, dUp = (v + vMax) * 0.5 * tUp;
			if (dist <= dUp)
			{
				return (-v + Math.Sqrt(v * v + 2.0 * Accel * dist)) / Accel;
			}
			return tUp + (dist - dUp) / vMax;
		}

		/// <summary>
		/// The time a car alone on the road takes over <paramref name="dist"/>, coming on at <paramref name="vIn"/>, wanting <paramref
		/// name="vMax"/> and slowing to <paramref name="vOut"/> by the end (a turn, a slower road, its stop), driven as a micro car drives
		/// it: pulling away by <see cref="Free"/>, braking once it needs half its comfortable braking (<see cref="SlowTo"/>, which then holds
		/// that). <paramref name="vEnd"/>: its speed at the end. What the meso takes as a free road's time, so that a car takes as long
		/// watched as not (<c>docs/TRAFFIC_REWORK.md</c> §5, consistency): a flat speed limit leaves out the seconds lost slowing into turns
		/// and pulling away, which a micro car loses.
		/// </summary>
		internal static double Run(double dist, double vIn, double vMax, double vOut, out double vEnd)
		{
			return Drive(dist, vIn, vMax, vOut, double.MaxValue, out double _, out vEnd);
		}

		/// <summary>Where a car driven as in <see cref="Run"/> is <paramref name="time"/> after coming on (at most <paramref name="dist"/>), and its speed then.</summary>
		internal static double RunAt(double dist, double vIn, double vMax, double vOut, double time, out double vNow)
		{
			Drive(dist, vIn, vMax, vOut, Math.Max(0.0, time), out double x, out vNow);
			return x;
		}

		/// <summary><see cref="Run"/> and <see cref="RunAt"/>: the time it ends (at <paramref name="dist"/> or <paramref name="tCap"/>), where and how fast.</summary>
		private static double Drive(double dist, double vIn, double vMax, double vOut, double tCap, out double x, out double vEnd)
		{
			vMax = Math.Max(vMax, 0.5);
			vOut = Math.Min(Math.Max(vOut, 0.0), vMax);
			double v = Math.Min(Math.Max(0.0, vIn), vMax), t = 0.0;
			x = 0.0;
			const double dt = 0.1, b = Brake * 0.5;
			while (x < dist && t < tCap)
			{
				double left = dist - x;
				if (v > vOut && (v * v - vOut * vOut) >= 2.0 * b * left)
				{
					// Braking to the end at what it needs (from then on that holds).
					double need = (v * v - vOut * vOut) / (2.0 * Math.Max(left, 1e-6));
					double tb = 2.0 * left / Math.Max(v + vOut, 1e-6);
					if (t + tb <= tCap)
					{
						x = dist;
						vEnd = vOut;
						return t + tb;
					}
					double tau = tCap - t;
					x += v * tau - 0.5 * need * tau * tau;
					vEnd = v - need * tau;
					return tCap;
				}
				if (v >= vMax * 0.995)
				{
					// Cruising near the limit (IDM only creeps the last of the way up), on to where braking starts.
					double brakeAt = v > vOut ? (v * v - vOut * vOut) / (2.0 * b) : 0.0;
					double run = Math.Max(0.0, left - brakeAt), tc = run / v;
					if (t + tc >= tCap)
					{
						x += v * (tCap - t);
						vEnd = v;
						return tCap;
					}
					t += tc;
					x += run;
					if (brakeAt <= 0.0)
					{
						x = dist;
						break;
					}
					// Exactly at the braking point: nudge past rounding so the braking branch takes it.
					x = Math.Max(x, dist - brakeAt * (1.0 - 1e-9));
					continue;
				}
				double a = Free(v, vMax);
				double h = Math.Min(dt, tCap - t);
				double step = v * h + 0.5 * a * h * h;
				if (step >= left)
				{
					// The end within this step.
					double tt = a > 1e-9 ? (-v + Math.Sqrt(v * v + 2.0 * a * left)) / a : left / Math.Max(v, 1e-6);
					x = dist;
					vEnd = v + a * tt;
					return t + tt;
				}
				x += step;
				v += a * h;
				t += h;
			}
			vEnd = v;
			return t;
		}

		/// <summary>
		/// The latest a car going <paramref name="v"/> (at least <paramref name="vMin"/>: a car pulling away) is through <paramref
		/// name="dist"/> at its speed: when it is sure to be clear of somewhere.
		/// </summary>
		internal static double Latest(double dist, double v, double vMin)
		{
			if (dist <= 0.0)
			{
				return 0.0;
			}
			return dist / Math.Max(v, vMin);
		}
	}
}
