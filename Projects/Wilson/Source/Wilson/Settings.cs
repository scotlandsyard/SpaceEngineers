using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace Wilson;

/// <summary>How much Wilson stages and says. Off means he doesn't direct at all, so the plugins talk on their own.</summary>
internal enum Level
{
	Off,
	Quiet,
	Normal,
	Chatty
}

/// <summary>Wilson's settings, kept between sessions in his own local storage file.</summary>
internal static class Settings
{
	private const string FileName = "Wilson_Settings.txt";

	public static Level Level = Level.Normal;

	/// <summary>The player's names for Wilson and Tim; null for their own. The plugins keep their own names.</summary>
	public static string WilsonName;

	public static string TimName;

	public static void Load()
	{
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(Settings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>();
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(Settings)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					int equals = line.IndexOf('=');
					if (equals > 0)
					{
						values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
					}
				}
			}
			if (values.TryGetValue("Level", out string level) && Enum.TryParse(level, true, out Level parsed) && Enum.IsDefined(typeof(Level), parsed))
			{
				Level = parsed;
			}
			values.TryGetValue("WilsonName", out string wilson);
			WilsonName = Personality.CleanName(wilson);
			values.TryGetValue("TimName", out string tim);
			TimName = Personality.CleanName(tim);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(Settings)))
			{
				writer.WriteLine("Level=" + Level);
				writer.WriteLine("WilsonName=" + (WilsonName ?? ""));
				writer.WriteLine("TimName=" + (TimName ?? ""));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Could not save settings: {ex.Message}");
		}
	}

	/// <summary>The same rhythm as the plugins on their own: one turn per cycle.</summary>
	public static TimeSpan Cycle => Personality.Cycle(ToChattiness(Level));

	public static Personality.Chattiness ToChattiness(Level level)
	{
		return (Personality.Chattiness)(int)level;
	}

	public static string Hint(Level level)
	{
		switch (level)
		{
		case Level.Off:
			return "Wilson stays out of it: each plugin talks on its own, as if he weren't loaded.";
		case Level.Quiet:
			return "One turn every 15 minutes, important moments only, no exchanges. Roll call on load.";
		case Level.Chatty:
			return "One turn every 2 minutes; about half are exchanges. Roll call on load.";
		default:
			return "One turn every 5 minutes; about a third are exchanges. Roll call on load.";
		}
	}
}
