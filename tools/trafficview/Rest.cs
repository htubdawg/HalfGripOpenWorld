using System;
using HalfGripOpenWorld;

// The cars at rest on their springs (TrafficRest), checked against the game's own suspension run over time: a car body free to rise and
// pitch, dropped onto its four springs and stepped at the game's 50 a second with CarControllerNew's law (the ray leaning out, the
// surface's offset, k (rest - length) - damper * rate, no less than zero, at most ten quarter-weights), until it stands still. Where it
// stands must be where TrafficRest says, or a drawn parked car and the physics car that takes its place would not stand alike and the
// swap would show (the owner, 2026-11-23). The game's numbers (from the Tokyo dump): wheels at (±1.65, 0.5, 2.75) and (±1.65, 0.5,
// -2.5) in the root's frame (x already widened), root scale 1.5, mass 1000, springs 15000 and 4000 of 0.7, radius 0.5. Gravity and the
// centre of mass are read in the game; here a spread of them.
internal static partial class Program
{
	private static bool Rest()
	{
		bool ok = true;
		double[] x = { -1.65, 1.65, -1.65, 1.65 };
		double[] y = { 0.5, 0.5, 0.5, 0.5 };
		double[] z = { 2.75, 2.75, -2.5, -2.5 };
		const double Scale = 1.5, Mass = 1000.0, K = 15000.0, Damper = 4000.0, RestLength = 0.7, Radius = 0.5;
		double worst = 0.0;
		int cases = 0;
		foreach (double g in new[] { 9.81, 20.0, 30.0 })
		{
			foreach ((double cy, double cz) in new[] { (0.0, 0.0), (0.3, 0.4), (-0.2, -0.6), (0.6, 0.15) })
			{
				TrafficRest.Result r = TrafficRest.Solve(x, y, z, Scale, cy, cz, Mass, g, K, RestLength, Radius);
				if (!r.Ok)
				{
					Console.WriteLine("  rest: g " + g + ", centre of mass (" + cy + ", " + cz + "): not found  FAIL");
					ok = false;
					continue;
				}
				foreach (double offset in new[] { 0.0, -0.1 })
				{
					cases++;
					Settle(x, y, z, Scale, cy, cz, Mass, g, K, Damper, RestLength, Radius, offset, out double height, out double pitch, out double[] length);
					double want = r.Height + offset * r.Sink;
					double off = Math.Max(Math.Abs(height - want), Math.Abs(pitch - r.Pitch) * 4.0);
					for (int i = 0; i < 4; i++)
					{
						off = Math.Max(off, Math.Abs(length[i] - r.Length[i]));
					}
					worst = Math.Max(worst, off);
					if (off > 2e-3)
					{
						ok = false;
						Console.WriteLine("  rest: g " + g + ", centre of mass (" + cy + ", " + cz + "), offset " + offset + ": settles at " + height.ToString("0.0000") + " pitched "
							+ pitch.ToString("0.0000") + ", worked out " + want.ToString("0.0000") + " pitched " + r.Pitch.ToString("0.0000") + "  FAIL");
					}
				}
				if (g == 20.0 && cy == 0.0 && cz == 0.0)
				{
					Console.WriteLine("  rest (g 20, centre of mass at the root): springs " + string.Join(", ", Array.ConvertAll(r.Length, l => l.ToString("0.000"))) + ", root "
						+ r.Height.ToString("0.000") + " over the ground, pitched " + (r.Pitch * 180.0 / Math.PI).ToString("0.00") + " deg");
				}
			}
		}
		Console.WriteLine("Rest on the springs: " + cases + " cases against the game's suspension run to a standstill, worst off by " + worst.ToString("0.00000") + (ok ? "" : "  FAIL"));
		return ok;
	}

	/// <summary>The game's suspension run over time on flat ground until the car stands still: its root's height, its pitch and its springs.</summary>
	private static void Settle(double[] x, double[] y, double[] z, double scale, double cy, double cz, double mass, double g, double k, double damper, double rest,
		double radius, double offset, out double height, out double pitch, out double[] length)
	{
		int n = x.Length;
		const double Dt = 0.02;
		// A box's pitch inertia (the car about 20 long and 4 high): any will do for where it comes to rest.
		double inertia = mass * (20.0 * 20.0 + 4.0 * 4.0) / 12.0;
		double cap = 10.0 * mass * 0.25 * g;
		// Dropped from a little high, level, the springs at full length.
		double comY = 2.5, vy = 0.0, th = 0.0, w = 0.0;
		double[] last = new double[n];
		length = new double[n];
		for (int i = 0; i < n; i++)
		{
			last[i] = rest;
		}
		height = 0.0;
		for (int step = 0; step < 3000; step++)
		{
			double cos = Math.Cos(th), sin = Math.Sin(th);
			double rootY = comY - scale * (cy * cos + cz * sin);
			double comAlong = scale * (cz * cos - cy * sin);
			double force = -mass * g, turn = 0.0;
			for (int i = 0; i < n; i++)
			{
				double place = rootY + scale * (y[i] * cos + z[i] * sin);
				double lean = Math.Sqrt(1.0 + 0.01 * x[i] * x[i]);
				double l = (place + cos) * lean / cos - radius - 1.0 - offset;
				length[i] = l;
				if (l > rest)
				{
					last[i] = rest;
					continue;
				}
				double rate = (l - last[i]) / Dt;
				last[i] = l;
				double f = Math.Min(cap, Math.Max(0.0, k * (rest - l) - rate * damper));
				force += f;
				turn += f * (scale * (z[i] * cos - y[i] * sin) - comAlong);
			}
			vy += force / mass * Dt;
			w += turn / inertia * Dt;
			comY += vy * Dt;
			th += w * Dt;
			height = rootY;
		}
		pitch = th;
	}
}
