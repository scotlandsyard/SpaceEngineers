using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
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
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Could not save settings: {ex.Message}");
		}
	}

	public static string Hint(Level level)
	{
		switch (level)
		{
		case Level.Off:
			return "Wilson stays out of it: each plugin talks on its own, as if he weren't loaded.";
		case Level.Quiet:
			return "Wilson keeps the plugins to one voice at a time, says hello, and only now and then stages an exchange.";
		case Level.Chatty:
			return "Wilson stages exchanges often, breaks up arguments, and shares a proverb after a quiet spell.";
		default:
			return "Wilson stages an exchange every so often, sometimes breaks up an argument, and rarely shares a proverb.";
		}
	}
}
