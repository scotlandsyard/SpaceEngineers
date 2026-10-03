using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace OreScout;

/// <summary>
/// The player's own OreScout settings, kept between sessions in the plugin's local storage file. Scan settings
/// live in each ore detector's Custom Data instead, because they belong to the grid, not the player.
/// </summary>
internal static class OreScoutSettings
{
	private const string FileName = "OreScout_Settings.txt";

	/// <summary>How much OreScout talks in chat.</summary>
	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	/// <summary>The name OreScout's chat lines show under, or null for "OreScout".</summary>
	public static string DisplayName;

	public static void Load()
	{
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(OreScoutSettings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(OreScoutSettings)))
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
			if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
			{
				Chattiness = parsed;
			}
			if (values.TryGetValue("Name", out string name) && !string.IsNullOrWhiteSpace(name))
			{
				DisplayName = name;
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(OreScoutSettings)))
			{
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("Name=" + (DisplayName ?? ""));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] Could not save settings: {ex.Message}");
		}
	}

	/// <summary>
	/// Hooked to Personality.Changed: saves the current display name, however it was changed ("/scout name" or
	/// Wilson's window). Personality.DisplayName falls back to "OreScout" when unset, so that case is saved as blank.
	/// </summary>
	public static void SaveDisplayName()
	{
		string name = Personality.DisplayName;
		DisplayName = string.IsNullOrWhiteSpace(name) || name == "OreScout" ? null : name;
		Save();
	}

	/// <summary>Changes the chat personality, applies it right away and saves it.</summary>
	public static void SetChattiness(Personality.Chattiness level)
	{
		Chattiness = level;
		Personality.Level = level;
		Save();
	}
}
