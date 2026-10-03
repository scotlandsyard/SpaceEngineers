using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// The player's own settings, the same in every world, kept in the plugin's local storage. The scripts themselves
/// are kept per world (see ScriptStore).
/// </summary>
internal static class ScriptSettings
{
	private const string FileName = "ScriptToPlugin_Settings.txt";

	/// <summary>How much Script to Plugin talks in chat.</summary>
	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	/// <summary>The name Script to Plugin's chat lines show under; null for its own name.</summary>
	public static string DisplayName;

	public static void Load()
	{
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(ScriptSettings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(ScriptSettings)))
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
			if (values.TryGetValue("Name", out string name))
			{
				DisplayName = Personality.CleanName(name);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(ScriptSettings)))
			{
				writer.WriteLine("Personality=" + Chattiness);
				writer.WriteLine("Name=" + (DisplayName ?? ""));
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not save settings: {ex.Message}");
		}
	}
}
