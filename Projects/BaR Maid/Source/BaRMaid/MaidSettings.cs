using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace BaRMaid;

/// <summary>
/// The player's own BaR Maid settings, kept between sessions in the plugin's local storage file. Group settings
/// live in the blocks' Custom Data instead (see MaidConfig), because they belong to the grid, not the player.
/// </summary>
internal static class MaidSettings
{
	private const string FileName = "BaRMaid_Settings.txt";

	/// <summary>How much BaR Maid talks in chat.</summary>
	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	/// <summary>The name her chat lines show under; null means "BaR Maid".</summary>
	public static string DisplayName;

	public static void Load()
	{
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(MaidSettings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(MaidSettings)))
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
			if (values.TryGetValue("DisplayName", out string name))
			{
				DisplayName = Personality.CleanName(name);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(MaidSettings)))
			{
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("DisplayName=" + DisplayName);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] Could not save settings: {ex.Message}");
		}
	}
}
