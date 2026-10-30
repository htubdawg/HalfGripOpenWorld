namespace HalfGripOpenWorld
{
	/// <summary>
	/// Every name the world shows: on its road signs (<see cref="Signs"/>), the labelled map (tools/preview), the teleport's list and
	/// the log. Change any of them here and the signs follow: each sign's lettering is drawn from these strings when the world is built,
	/// and its board is sized to fit, so nothing is painted by hand.
	///
	/// A name never shapes the land or the roads (only how many there are of each does: <see cref="Towns"/> and <see cref="Villages"/>
	/// take places in order of how good a site is, the first name to the best site), so renaming keeps the world; but a longer or
	/// shorter name changes the size of its boards, and a house may move to make room, so everyone in a Multiplayer room still needs
	/// the same version. The signs' letters: A to Z, a to z, 0 to 9, space and . , ' - &amp; ( ) / : ! ?; accented letters are drawn
	/// without their accents, anything else as a gap.
	/// </summary>
	internal static class Names
	{
		/// <summary>The towns, the main town (where the race starts) first.</summary>
		internal static readonly string[] Towns = { "Kingsbridge", "Westhaven" };

		/// <summary>The villages, the best site first.</summary>
		internal static readonly string[] Villages =
		{
			"Ashby", "Brookfield", "Carrow", "Dunmore", "Elmsted", "Fenwick", "Glenford", "Harrow", "Ivybridge", "Kestrel", "Lowmoor", "Millbrook",
			"Northcote", "Oakhurst", "Pinewick", "Redford",
		};

		/// <summary>The race courses, as their brown signs give them.</summary>
		internal const string Circuit = "Circuit";
		internal const string Rally = "Rally stage";
		internal const string HillClimb = "Hill climb";
	}
}
