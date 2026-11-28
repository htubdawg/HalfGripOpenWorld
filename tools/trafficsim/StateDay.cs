using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// Phase 6 (rooms): the traffic's whole state saved and loaded (TrafficState.cs), as a room gives it to a player joining mid-race. The race's
// traffic run for a while round a pretend player, saved, loaded into a fresh traffic, then both run on together with the same player for
// ten minutes. Fails on: the loaded traffic's state not the saved one byte for byte, the two parting at any minute after (a field the save
// misses, or a simulation that isn't the same from the same state and inputs: lockstep needs both), the save over 2 MB compressed.
// Measured: the save's size, raw and compressed, and the time to save and to load.
internal static partial class Program
{
	private static void StateDay(int seed, double fromRace, double minutes, double from = -1.0)
	{
		double start = from >= 0.0 ? from : Schedules.StartOf(StartHourOf(seed));
		Meso.Ensure();
		Walker w = Walkers(seed)[1];
		TrafficLive a = TrafficLive.Build(seed, start, w.SX, w.SZ);
		a.PlayerX = w.SX;
		a.PlayerZ = w.SZ;
		a.HasPlayer = true;
		string where = "seed " + seed + " state from " + TrafficLive.Clock(start + fromRace);
		double race = 0.0;
		while (race < fromRace)
		{
			race += Meso.Step;
			w.Step(Meso.Step);
			a.PlayerX = w.SX;
			a.PlayerZ = w.SZ;
			a.WaitForNextDay();
			a.Advance(a.DayAt(race));
		}
		var watch = System.Diagnostics.Stopwatch.StartNew();
		byte[] raw = TrafficState.Raw(a);
		double rawMs = watch.Elapsed.TotalMilliseconds;
		watch.Restart();
		byte[] saved = TrafficState.Save(a);
		double saveMs = watch.Elapsed.TotalMilliseconds;
		watch.Restart();
		TrafficLive b = TrafficState.Load(saved);
		double loadMs = watch.Elapsed.TotalMilliseconds;
		Console.WriteLine(where + ": " + (raw.Length / 1024) + " KB raw (" + rawMs.ToString("0") + " ms), " + (saved.Length / 1024) + " KB saved ("
			+ saveMs.ToString("0") + " ms), loaded in " + loadMs.ToString("0") + " ms");
		Check(saved.Length <= 2 << 20, "state size", where + ": " + (saved.Length / 1024) + " KB compressed");
		int diff = FirstDiff(raw, TrafficState.Raw(b));
		Check(diff < 0, "state round trip", where + ": the loaded state differs from byte " + diff + " of " + raw.Length + (diff >= 0 ? " (" + TrafficState.PathAt(a, diff) + ")" : ""));
		int parted = -1;
		double partedAt = 0.0;
		double next = race + 60.0, end = race + minutes * 60.0;
		while (race < end && parted < 0)
		{
			race += Meso.Step;
			w.Step(Meso.Step);
			foreach (TrafficLive l in new[] { a, b })
			{
				l.PlayerX = w.SX;
				l.PlayerZ = w.SZ;
				l.WaitForNextDay();
				l.Advance(l.DayAt(race));
			}
			if (race >= next - 1e-9)
			{
				next += 60.0;
				parted = FirstDiff(TrafficState.Raw(a), TrafficState.Raw(b));
				partedAt = race;
			}
		}
		Check(parted < 0, "state carries on", where + ": the saved and loaded traffics part by " + TrafficLive.Clock(start + partedAt) + " (byte " + parted + ", " + (parted >= 0 ? TrafficState.PathAt(a, parted) + " " : "") + ")");
	}

	/// <summary>The first byte two arrays differ at (-1: the same).</summary>
	private static int FirstDiff(byte[] x, byte[] y)
	{
		int n = Math.Min(x.Length, y.Length);
		for (int i = 0; i < n; i++)
		{
			if (x[i] != y[i])
			{
				return i;
			}
		}
		return x.Length == y.Length ? -1 : n;
	}
}
