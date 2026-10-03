using System;

namespace Wilson;

/// <summary>
/// The characters Wilson knows: the id used in Wilson.txt triggers (barmaid.starved), each one's own name (shown in
/// chat unless the player renamed it, and used to address it on the personality channel), the plugin it belongs
/// to, which is also its folder under Projects, and the chat command that opens its window (said in its roll call
/// answer). Check-Personality.ps1 reads this list to check Wilson.txt, so keep each entry on one line as written.
/// </summary>
internal static class Cast
{
	public const string WilsonName = "Wilson";

	public const string WilsonCommand = "/wilson";

	/// <summary>Wilson's guest: pops up now and then when the player types /tim. Lives entirely in Wilson.</summary>
	public const string TimName = "Tim";

	public sealed class Member
	{
		public readonly string Id;

		public readonly string ChatName;

		public readonly string PluginName;

		public readonly string Command;

		public Member(string id, string chatName, string pluginName, string command)
		{
			Id = id;
			ChatName = chatName;
			PluginName = pluginName;
			Command = command;
		}
	}

	/// <summary>Every plugin character, in the order the window lists them and the roll call goes.</summary>
	public static readonly Member[] Others =
	{
		new Member("barmaid", "BaR Maid", "BaR Maid", "/barmaid"),
		new Member("fatalbert", "Fat Albert", "Fat Albert", "/fat"),
		new Member("stockpile", "Stockpile Manager", "Sacrificial Stockpile Manager", "/ssm"),
		new Member("scripttoplugin", "Script to Plugin", "Script to Plugin", "/stp"),
		new Member("orescout", "OreScout", "OreScout", "/scout")
	};

	public static Member ByChatName(string name)
	{
		return Array.Find(Others, m => m.ChatName.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	public static bool IsWilson(string name)
	{
		return WilsonName.Equals(name, StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsTim(string name)
	{
		return TimName.Equals(name, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Wilson and Tim: always there when Wilson is, since they live in his plugin.</summary>
	public static bool IsHome(string name)
	{
		return IsWilson(name) || IsTim(name);
	}
}
