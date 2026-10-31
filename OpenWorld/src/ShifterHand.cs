using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The hand on the shifter: whether it holds the car's active shifter, and the frame it lets go of it. The garage and the events'
	/// start boxes act on that letting go in neutral, not on the gearbox merely standing in neutral: a shift through neutral never lets
	/// go, and a car put in a circle or a box in neutral (a respawn, a closed garage) opens nothing until the player takes the shifter
	/// and lets it go again.
	///
	/// Every kind of shifter (H-pattern, sequential, automatic, Lenco) keeps its knobs and levers, the <see cref="Grabbable"/>s the hand
	/// takes, under its own object, so the shifter is held when the grabber's held object is under the active shifter's.
	/// </summary>
	internal static class ShifterHand
	{
		private static Grabber grabber;
		private static int frame = -1;
		private static bool held;
		private static bool released;

		/// <summary>The active shifter (every kind reports here) in no gear.</summary>
		internal static bool Neutral(CarLink link)
		{
			return link.sa != null && (link.sa.gear == 0 || !link.sa.inGear);
		}

		/// <summary>True while the hand holds the car's active shifter.</summary>
		internal static bool Holding(CarLink link)
		{
			Look(link);
			return held;
		}

		/// <summary>True on the one frame the hand let go of the active shifter with it in neutral.</summary>
		internal static bool ReleasedInNeutral(CarLink link)
		{
			Look(link);
			return released && Neutral(link);
		}

		/// <summary>
		/// Once a frame, whoever asks first: whether the hand holds the shifter now, and whether it did the frame before. A frame nobody
		/// asked in breaks the chain, so a hold seen long ago is never taken for a letting go now.
		/// </summary>
		private static void Look(CarLink link)
		{
			if (frame == Time.frameCount)
			{
				return;
			}
			bool chained = frame == Time.frameCount - 1;
			frame = Time.frameCount;
			if (grabber == null && frame % 30 == 0)
			{
				grabber = Object.FindFirstObjectByType<Grabber>();
			}
			Grabbable g = grabber != null ? grabber.Held : null;
			bool now = g != null && link != null && link.sa != null && g.transform.IsChildOf(link.sa.transform);
			released = chained && held && !now;
			held = now;
		}
	}
}
