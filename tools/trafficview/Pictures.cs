using System;
using System.IO;
using HalfGripOpenWorld;

/// <summary>
/// The day's pictures (PPM): the roads coloured by their traffic (passes a game day x 36, as a counter's AADT), and the cars at four
/// hours (moving ones bright, parked ones faint).
/// </summary>
internal static class Pictures
{
	private const int Size = 1600;
	private const double Half = 16400.0;

	internal static void Draw(TrafficDay day, int[] passes, string path)
	{
		byte[] img = Blank();
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			double aadt = passes[e] * 36.0;
			// 0 grey, then blue (a few hundred) through green and yellow to red (10,000 and more).
			byte r, g, b;
			if (aadt < 1.0)
			{
				r = g = b = 90;
			}
			else
			{
				double k = Math.Max(0.0, Math.Min(1.0, Math.Log10(aadt / 100.0) / 2.0));
				Ramp(k, out r, out g, out b);
			}
			Edge(img, e, r, g, b, aadt > 2000.0 ? 2 : 1);
		}
		Write(img, path);
		string stem = path.EndsWith(".ppm") ? path.Substring(0, path.Length - 4) : path;
		foreach (double hour in new[] { 3.0, 8.0, 12.0, 17.0 })
		{
			byte[] frame = Blank();
			for (int e = 0; e < Traffic.Edges.Count; e++)
			{
				Edge(frame, e, 70, 70, 70, 1);
			}
			double w = TrafficDay.SimOfHour(hour + 0.5);
			int moving = 0;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State == TrafficDay.State.Parked)
				{
					Dot(frame, at.X, at.Z, 0, 60, 70, 110);
				}
			}
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State == TrafficDay.State.Driving)
				{
					moving++;
					Dot(frame, at.X, at.Z, 2, 255, 230, 60);
				}
			}
			Write(frame, stem + "-" + hour.ToString("00") + "h.ppm");
			Console.WriteLine("  frame at " + hour.ToString("00") + ":30: " + moving + " cars moving, written");
		}
	}

	private static void Ramp(double k, out byte r, out byte g, out byte b)
	{
		// blue -> cyan -> green -> yellow -> red
		double[][] stops = { new[] { 40.0, 80, 220 }, new[] { 40.0, 200, 220 }, new[] { 60.0, 210, 70 }, new[] { 240.0, 220, 40 }, new[] { 240.0, 40, 30 } };
		double f = k * (stops.Length - 1);
		int i = Math.Min(stops.Length - 2, (int)Math.Floor(f));
		double t = f - i;
		r = (byte)(stops[i][0] + (stops[i + 1][0] - stops[i][0]) * t);
		g = (byte)(stops[i][1] + (stops[i + 1][1] - stops[i][1]) * t);
		b = (byte)(stops[i][2] + (stops[i + 1][2] - stops[i][2]) * t);
	}

	private static byte[] Blank()
	{
		byte[] img = new byte[Size * Size * 3];
		for (int i = 0; i < img.Length; i += 3)
		{
			img[i] = 18;
			img[i + 1] = 22;
			img[i + 2] = 26;
		}
		return img;
	}

	private static void Px(double x, double z, out int i, out int j)
	{
		i = (int)((x + Half) / (2.0 * Half) * Size);
		j = (int)((Half - z) / (2.0 * Half) * Size);
	}

	private static void Put(byte[] img, int i, int j, byte r, byte g, byte b)
	{
		if (i < 0 || j < 0 || i >= Size || j >= Size)
		{
			return;
		}
		int k = (j * Size + i) * 3;
		img[k] = r;
		img[k + 1] = g;
		img[k + 2] = b;
	}

	private static void Edge(byte[] img, int e, byte r, byte g, byte b, int width)
	{
		Traffic.Edge edge = Traffic.Edges[e];
		Network.Road road = Network.Roads[edge.Road];
		for (double s = edge.Sa; s <= edge.Sb; s += 8.0)
		{
			Traffic.At(road, s, out double x, out double z, out double _);
			Px(x, z, out int i, out int j);
			for (int dj = 0; dj < width; dj++)
			{
				for (int di = 0; di < width; di++)
				{
					Put(img, i + di, j + dj, r, g, b);
				}
			}
		}
	}

	private static void Dot(byte[] img, double x, double z, int radius, byte r, byte g, byte b)
	{
		Px(x, z, out int i, out int j);
		for (int dj = -radius; dj <= radius; dj++)
		{
			for (int di = -radius; di <= radius; di++)
			{
				Put(img, i + di, j + dj, r, g, b);
			}
		}
	}

	private static void Write(byte[] img, string path)
	{
		using (FileStream f = File.Create(path))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + Size + " " + Size + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(img, 0, img.Length);
		}
	}

	/// <summary>
	/// A close look at the parking: <paramref name="span"/> units square round (x, z) at world hour <paramref name="hour"/>, the drawn
	/// paving, the lots (their buildings darker) and the water, and every car there as its footprint: parked on a drive pad, by a
	/// cottage or in a farmyard green, at the kerb orange, on a lot cyan; moving cars yellow, where the game draws them (pulled between
	/// the space and the lane at a trip's ends).
	/// </summary>
	internal static void Closeup(TrafficDay day, TrafficParking parking, string path, double x0, double z0, double span, double hour)
	{
		const int n = 1000;
		byte[] img = new byte[n * n * 3];
		double px = span / n;
		for (int j = 0; j < n; j++)
		{
			for (int i = 0; i < n; i++)
			{
				double x = x0 - span * 0.5 + (i + 0.5) * px, z = z0 + span * 0.5 - (j + 0.5) * px;
				byte r = 34, g = 52, b = 30;
				if (Rivers.Water(x, z, out double _))
				{
					r = 30; g = 60; b = 110;
				}
				Town.Lot lot = Town.At(x, z, out bool building);
				if (lot != null)
				{
					if (building)
					{
						r = 60; g = 50; b = 50;
					}
					else
					{
						r = 78; g = 80; b = 86;
					}
				}
				if (Network.OnOtherPaving(x, z, -1))
				{
					r = 105; g = 105; b = 108;
				}
				else if (InHouse(x, z))
				{
					r = 120; g = 70; b = 60;
				}
				int k = (j * n + i) * 3;
				img[k] = r;
				img[k + 1] = g;
				img[k + 2] = b;
			}
		}
		double w = TrafficDay.SimOfHour(hour);
		int parked = 0, moving = 0;
		for (int c = 0; c < day.Cars.Count; c++)
		{
			TrafficDay.Place at = day.At(c, w);
			TrafficParking.Spot s;
			byte r, g, b;
			if (at.State == TrafficDay.State.Driving)
			{
				if (at.InTunnel)
				{
					continue;
				}
				s = new TrafficParking.Spot { X = at.X, Z = at.Z, Hx = at.Hx, Hz = at.Hz };
				TrafficDay.Leg leg = day.Legs[at.Leg];
				// As the game pulls a car between its space and its lane (TrafficCars.Draw).
				TrafficParking.Spot sp = default;
				double kk = 1.0;
				if (at.P < 150.0 && !leg.Appears)
				{
					sp = parking.Before(at.Leg);
					kk = at.P / PullOver(leg, 0.0, sp);
				}
				if ((kk >= 1.0 || sp.Away) && leg.Length - at.P < 150.0 && !leg.Vanishes)
				{
					sp = parking.Parked(c, at.Leg);
					kk = (leg.Length - at.P) / PullOver(leg, leg.Length, sp);
				}
				if (kk < 1.0 && !sp.Away)
				{
					kk = kk * kk * (3.0 - 2.0 * kk);
					s.X = sp.X + (s.X - sp.X) * kk;
					s.Z = sp.Z + (s.Z - sp.Z) * kk;
					double hx = sp.Hx + (s.Hx - sp.Hx) * kk, hz = sp.Hz + (s.Hz - sp.Hz) * kk, l = Math.Sqrt(hx * hx + hz * hz);
					if (l > 1e-6)
					{
						s.Hx = hx / l;
						s.Hz = hz / l;
					}
				}
				r = 250; g = 220; b = 40;
				moving++;
			}
			else if (at.State == TrafficDay.State.Parked)
			{
				s = parking.Parked(c, at.Leg);
				if (s.Away)
				{
					continue;
				}
				bool kerb = parking.IsKerb(c, at.Leg, out int _);
				bool lot = !kerb && at.Leg >= 0 && !day.Legs[at.Leg].Return && day.Legs[at.Leg].Host < 0;
				if (kerb)
				{
					r = 240; g = 140; b = 40;
				}
				else if (lot)
				{
					r = 60; g = 210; b = 230;
				}
				else
				{
					r = 90; g = 220; b = 90;
				}
				parked++;
			}
			else
			{
				continue;
			}
			if (Math.Abs(s.X - x0) > span * 0.5 + 20.0 || Math.Abs(s.Z - z0) > span * 0.5 + 20.0)
			{
				continue;
			}
			// The footprint, filled.
			for (double u = -7.0; u <= 7.0; u += px * 0.5)
			{
				for (double v = -2.7; v <= 2.7; v += px * 0.5)
				{
					double x = s.X + s.Hx * u + s.Hz * v, z = s.Z + s.Hz * u - s.Hx * v;
					int i = (int)((x - (x0 - span * 0.5)) / px), j = (int)(((z0 + span * 0.5) - z) / px);
					if (i < 0 || j < 0 || i >= n || j >= n)
					{
						continue;
					}
					// The nose a shade lighter, to show the way it faces.
					bool nose = u > 4.5;
					int k = (j * n + i) * 3;
					img[k] = (byte)Math.Min(255, r + (nose ? 60 : 0));
					img[k + 1] = (byte)Math.Min(255, g + (nose ? 60 : 0));
					img[k + 2] = (byte)Math.Min(255, b + (nose ? 60 : 0));
				}
			}
		}
		using (FileStream f = File.Create(path))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + n + " " + n + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(img, 0, img.Length);
		}
		Console.WriteLine("  close look at (" + x0.ToString("0") + ", " + z0.ToString("0") + "), " + span.ToString("0") + " units across, at " + hour.ToString("0.0") + ": " + parked + " parked and " + moving + " moving cars in the world, written to " + path);
	}

	/// <summary>Whether (x, z) is in a house (the plots trafficview found).</summary>
	private static bool InHouse(double x, double z)
	{
		int gx = (int)Math.Floor(x / 64.0), gz = (int)Math.Floor(z / 64.0);
		for (int ox = -1; ox <= 1; ox++)
		{
			for (int oz = -1; oz <= 1; oz++)
			{
				if (Program.houses == null || !Program.houses.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out var list))
				{
					continue;
				}
				foreach (var h in list)
				{
					double dx = x - h.x, dz = z - h.z;
					if (Math.Abs(dx * h.ux + dz * h.uz) <= h.hw && Math.Abs(dx * h.uz - dz * h.ux) <= h.hd)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static double PullOver(TrafficDay.Leg leg, double p, TrafficParking.Spot spot)
	{
		if (spot.Away)
		{
			return 30.0;
		}
		TrafficDay.Place end = default;
		TrafficDay.OnPath(leg, p, ref end);
		double dx = spot.X - end.X, dz = spot.Z - end.Z;
		return Math.Max(30.0, Math.Min(150.0, Math.Sqrt(dx * dx + dz * dz)));
	}
}
