using System;

namespace Wilson;

/// <summary>
/// The characters Wilson knows: the id used in Wilson.txt triggers (barmaid.starved), the name each one shows in
/// chat (and registers with Shared/Personality.cs), and the plugin it belongs to, which is also its folder under
/// Projects. Check-Personality.ps1 reads this list to check Wilson.txt, so keep each entry on one line as written.
/// </summary>
internal static class Cast
{
	public const string WilsonName = "Wilson";

	public sealed class Member
	{
		public readonly string Id;

		public readonly string ChatName;

		public readonly string PluginName;

		public Member(string id, string chatName, string pluginName)
		{
			Id = id;
			ChatName = chatName;
			PluginName = pluginName;
		}
	}

	/// <summary>Every character but Wilson, in the order the window lists them.</summary>
	public static readonly Member[] Others =
	{
		new Member("barmaid", "BaR Maid", "BaR Maid"),
		new Member("fatalbert", "Fat Albert", "Fat Albert"),
		new Member("stockpile", "Stockpile Manager", "Sacrificial Stockpile Manager"),
		new Member("scripttoplugin", "Script to Plugin", "Script to Plugin"),
		new Member("orescout", "OreScout", "OreScout")
	};

	public static Member ById(string id)
	{
		return Array.Find(Others, m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
	}

	public static Member ByChatName(string name)
	{
		return Array.Find(Others, m => m.ChatName.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	public static bool IsWilson(string name)
	{
		return WilsonName.Equals(name, StringComparison.OrdinalIgnoreCase);
	}
}
