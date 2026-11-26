using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The junctions of the traffic's map (<see cref="Traffic"/>) as a driver reads them: each arm's way out, and the main line (the
	/// through road drivers know without a sign). Taken out of the old day of traffic so the lights (<see cref="TrafficSignals"/>,
	/// <see cref="SignalPoles"/>) stand without it; the traffic rework's lane graph will read the same. Worked out once with the map,
	/// with + - * / only and ties by index, so every machine agrees. No Unity types.
	/// </summary>
	internal static class Junctions
	{
		private static readonly object gate = new object();
		private static volatile bool prepared;
		/// <summary>Per node, per link (as <see cref="Traffic.Links"/>): the way out along it (unit).</summary>
		private static double[][] armX, armZ;
		/// <summary>Per node, the two arms of its main line (-1: none).</summary>
		private static int[] mainA, mainB;

		/// <summary>Works the junctions out once (after <see cref="Traffic.Ensure"/>, which it calls). Safe from any thread.</summary>
		internal static void Ensure()
		{
			if (prepared)
			{
				return;
			}
			lock (gate)
			{
				if (prepared)
				{
					return;
				}
				Traffic.Ensure();
				int n = Traffic.Nodes.Count;
				armX = new double[n][];
				armZ = new double[n][];
				for (int node = 0; node < n; node++)
				{
					List<int> links = Traffic.Links[node];
					int m = links.Count;
					double[] ax = new double[m], az = new double[m];
					for (int k = 0; k < m; k++)
					{
						Traffic.Edge e = Traffic.Edges[links[k]];
						Network.Road road = Network.Roads[e.Road];
						double into = Math.Min(15.0, e.Length * 0.5);
						Traffic.At(road, e.A == node ? e.Sa + into : e.Sb - into, out double x, out double z, out double _);
						double dx = x - Traffic.Nodes[node].X, dz = z - Traffic.Nodes[node].Z, len = Math.Sqrt(dx * dx + dz * dz);
						if (len < 1e-9)
						{
							Traffic.Heading(road, e.A == node ? e.Sa : e.Sb, out dx, out dz);
							if (e.A != node)
							{
								dx = -dx;
								dz = -dz;
							}
							len = 1.0;
						}
						ax[k] = dx / len;
						az[k] = dz / len;
					}
					armX[node] = ax;
					armZ[node] = az;
				}
				mainA = new int[n];
				mainB = new int[n];
				for (int node = 0; node < n; node++)
				{
					MainLine(node);
				}
				TrafficSignals.Build();
				prepared = true;
			}
		}

		/// <summary>The arms of node <paramref name="node"/>'s main line (-1, -1: none).</summary>
		internal static void MainOf(int node, out int a, out int b)
		{
			a = mainA[node];
			b = mainB[node];
		}

		/// <summary>The way out of node <paramref name="node"/> along its arm <paramref name="arm"/> (unit).</summary>
		internal static void ArmOf(int node, int arm, out double x, out double z)
		{
			x = armX[node][arm];
			z = armZ[node][arm];
		}

		/// <summary>Whether the way in along arm <paramref name="inA"/> and out along <paramref name="outA"/> turns left (across the oncoming lane: traffic keeps right).</summary>
		internal static bool LeftTurn(int node, int inA, int outA)
		{
			double tx = -armX[node][inA], tz = -armZ[node][inA];
			return tx * armZ[node][outA] - tz * armX[node][outA] > 0.3;
		}

		/// <summary>
		/// The main line of a junction, worked out once with the map: drivers know which road is the through road without a sign. Among
		/// the arms of the highest class (if only one arm is of it, that arm and those of the next class): the two arms of one road that
		/// carries on through the node; else the pair nearest a straight line, if it is clearly straighter than the next pair (a T has
		/// one, a fork of two equal branches or a crossroads of equal roads does not). None at a dead end, a bend (two arms) or a hub of
		/// five arms or more: there every junction stays first come first served.
		/// </summary>
		private static void MainLine(int node)
		{
			mainA[node] = mainB[node] = -1;
			List<int> links = Traffic.Links[node];
			int m = links.Count;
			if (m < 3 || m > 4)
			{
				return;
			}
			int[] kind = new int[m];
			int best = int.MaxValue;
			for (int k = 0; k < m; k++)
			{
				kind[k] = (int)Network.Roads[Traffic.Edges[links[k]].Road].Kind;
				best = Math.Min(best, kind[k]);
			}
			int top = 0, only = -1, next = int.MaxValue;
			for (int k = 0; k < m; k++)
			{
				if (kind[k] == best)
				{
					top++;
					only = k;
				}
				else
				{
					next = Math.Min(next, kind[k]);
				}
			}
			if (top == 2)
			{
				// Two arms of a higher class than the rest: they are the main line, however they bend.
				for (int k = 0; k < m; k++)
				{
					if (kind[k] == best)
					{
						if (mainA[node] < 0)
						{
							mainA[node] = k;
						}
						else
						{
							mainB[node] = k;
						}
					}
				}
				return;
			}
			int pa = -1, pb = -1, qa = -1, qb = -1;
			double pd = double.MaxValue, qd = double.MaxValue;
			bool same = false;
			for (int i = 0; i < m; i++)
			{
				for (int j = i + 1; j < m; j++)
				{
					bool ok = top >= 3 ? kind[i] == best && kind[j] == best : (i == only && kind[j] == next) || (j == only && kind[i] == next);
					if (!ok)
					{
						continue;
					}
					bool carries = Traffic.Edges[links[i]].Road == Traffic.Edges[links[j]].Road;
					double d = armX[node][i] * armX[node][j] + armZ[node][i] * armZ[node][j];
					if (carries && !same)
					{
						// A road carrying on through beats any pair that does not.
						same = true;
						pa = pb = qa = qb = -1;
						pd = qd = double.MaxValue;
					}
					else if (same && !carries)
					{
						continue;
					}
					if (d < pd)
					{
						qa = pa;
						qb = pb;
						qd = pd;
						pa = i;
						pb = j;
						pd = d;
					}
					else if (d < qd)
					{
						qa = i;
						qb = j;
						qd = d;
					}
				}
			}
			if (pa < 0)
			{
				return;
			}
			if (!same && (pd > -0.7 || (qa >= 0 && qd - pd < 0.15)))
			{
				// No pair near enough straight, or two about as straight as each other: no clear main line.
				return;
			}
			mainA[node] = pa;
			mainB[node] = pb;
		}
	}
}
