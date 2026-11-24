using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where the game's car stands at rest on its springs, worked out from the game's own suspension (<c>CarControllerNew.FixedUpdate</c>,
	/// read from the decompile) rather than learned or guessed, so a drawn car and the physics car that takes its place stand exactly
	/// alike and the swap shows nothing: no drop onto the springs, no bounce, no wheels moving. No Unity types (<c>tools/trafficview</c>
	/// checks it).
	/// </summary>
	/// <remarks>
	/// The game's model, per wheel: a ray from one unit above the wheel's place (along the car's up) going along <c>-up + right * 0.1 x</c>
	/// (not normalised by the game; Unity normalises it, so the ray leans out a little); the spring's length is the hit distance less the
	/// radius, less one, less the surface's <c>suspensionOffset</c>; grounded if that is no more than the rest length; the force
	/// <c>k (rest - length) - damper * its rate</c>, no less than zero and no more than ten times a quarter of the car's weight, along the
	/// ground's normal at the wheel's place. At rest the rate is zero, so the car stands where these forces carry its weight and turn it
	/// about its centre of mass no way. The wheel is drawn at its place less (length - 0.3) in the car's frame (unscaled, as the game does).
	/// </remarks>
	internal static class TrafficRest
	{
		internal struct Result
		{
			/// <summary>Whether a rest was found (the car's weight carried, no wheel off the ground).</summary>
			public bool Ok;
			/// <summary>Each wheel's spring length at rest (world units).</summary>
			public double[] Length;
			/// <summary>The car's root over the ground (perpendicular to it), on a surface with no offset.</summary>
			public double Height;
			/// <summary>The car's pitch over the ground at rest, nose up (radians).</summary>
			public double Pitch;
			/// <summary>How much lower the car stands for each unit of a surface's offset (it sinks by -offset times this).</summary>
			public double Sink;
		}

		/// <summary>
		/// The rest of a car whose wheels' places are (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) in its root's
		/// frame (as the game keeps them: x already widened by 1.1), its root scaled <paramref name="scale"/>, its centre of mass at
		/// (<paramref name="cy"/>, <paramref name="cz"/>) in the same frame, of <paramref name="mass"/> under gravity <paramref name="g"/>
		/// (positive), springs <paramref name="k"/> with rest length <paramref name="rest"/>, wheels of <paramref name="radius"/>, on a flat
		/// surface with no offset.
		/// </summary>
		internal static Result Solve(double[] x, double[] y, double[] z, double scale, double cy, double cz, double mass, double g, double k, double rest, double radius)
		{
			int n = x.Length;
			Result r = new Result { Length = new double[n] };
			if (n == 0 || y.Length != n || z.Length != n || scale <= 0.0 || mass <= 0.0 || g <= 0.0 || k <= 0.0 || rest <= 0.0)
			{
				return r;
			}
			double[] lean = new double[n];
			double leanMean = 0.0;
			for (int i = 0; i < n; i++)
			{
				lean[i] = Math.Sqrt(1.0 + 0.01 * x[i] * x[i]);
				leanMean += lean[i] / n;
			}
			double weight = mass * g;
			double cap = 10.0 * mass * 0.25 * g;
			// Start level, every spring carrying an equal share.
			double meanY = 0.0;
			for (int i = 0; i < n; i++)
			{
				meanY += y[i] / n;
			}
			double share = rest - weight / (n * k);
			double h0 = (share + radius + 1.0) / leanMean - 1.0;
			double rootY = h0 - scale * meanY, pitch = 0.0;
			double[] f = new double[2];
			for (int it = 0; it < 60; it++)
			{
				Residual(x, y, z, lean, scale, cy, cz, k, rest, radius, cap, weight, rootY, pitch, null, f);
				if (Math.Abs(f[0]) < weight * 1e-6 && Math.Abs(f[1]) < weight * scale * 1e-6)
				{
					break;
				}
				// Newton's step on (height, pitch), the slopes found by small differences.
				const double eh = 1e-5, ep = 1e-6;
				double[] fh = new double[2], fp = new double[2];
				Residual(x, y, z, lean, scale, cy, cz, k, rest, radius, cap, weight, rootY + eh, pitch, null, fh);
				Residual(x, y, z, lean, scale, cy, cz, k, rest, radius, cap, weight, rootY, pitch + ep, null, fp);
				double a = (fh[0] - f[0]) / eh, b = (fp[0] - f[0]) / ep;
				double c = (fh[1] - f[1]) / eh, d = (fp[1] - f[1]) / ep;
				double det = a * d - b * c;
				if (Math.Abs(det) < 1e-12)
				{
					return r;
				}
				double dh = (d * f[0] - b * f[1]) / det, dp = (a * f[1] - c * f[0]) / det;
				// No step longer than a tenth of a unit or a few degrees: a wheel leaving the ground makes the forces kink.
				rootY -= Math.Max(-0.1, Math.Min(0.1, dh));
				pitch -= Math.Max(-0.05, Math.Min(0.05, dp));
			}
			Residual(x, y, z, lean, scale, cy, cz, k, rest, radius, cap, weight, rootY, pitch, r.Length, f);
			r.Ok = Math.Abs(f[0]) < weight * 1e-4 && Math.Abs(f[1]) < weight * scale * 1e-4 && Math.Abs(pitch) < 0.3;
			for (int i = 0; i < n && r.Ok; i++)
			{
				// Every wheel on the ground and its spring not past the bump.
				r.Ok = r.Length[i] > 0.0 && r.Length[i] < rest;
			}
			r.Height = rootY;
			r.Pitch = pitch;
			r.Sink = Math.Cos(pitch) / leanMean;
			return r;
		}

		/// <summary>
		/// The forces' sum less the weight, and their turn about the centre of mass, with the root <paramref name="rootY"/> over the ground
		/// and pitched <paramref name="pitch"/>; each spring's length into <paramref name="lengths"/> if given.
		/// </summary>
		private static void Residual(double[] x, double[] y, double[] z, double[] lean, double scale, double cy, double cz, double k, double rest, double radius, double cap,
			double weight, double rootY, double pitch, double[] lengths, double[] f)
		{
			double cos = Math.Cos(pitch), sin = Math.Sin(pitch);
			// Nose up: a point (y, z) of the car goes to height y cos + z sin, and along the ground z cos - y sin.
			double comAlong = scale * (cz * cos - cy * sin);
			double sum = 0.0, turn = 0.0;
			for (int i = 0; i < x.Length; i++)
			{
				double place = rootY + scale * (y[i] * cos + z[i] * sin);
				// The ray starts one unit up the car's up (cos of it upward) and drops cos / lean a unit along itself.
				double length = (place + cos) * lean[i] / cos - radius - 1.0;
				if (lengths != null)
				{
					lengths[i] = length;
				}
				double force = length > rest ? 0.0 : Math.Min(cap, Math.Max(0.0, k * (rest - length)));
				sum += force;
				turn += force * (scale * (z[i] * cos - y[i] * sin) - comAlong);
			}
			f[0] = sum - weight;
			f[1] = turn;
		}
	}
}
