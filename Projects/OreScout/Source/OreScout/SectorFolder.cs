using System;
using System.Linq;
using System.Reflection;
using Sandbox.Engine.Multiplayer;
using Sandbox.ModAPI;
using VRage.Utils;

namespace OreScout;

/// <summary>
/// Puts OreScout's markers into a GPS Folders folder (StarCpt/SE-GpsFolders) named after the current sector.
/// That plugin keeps a marker's folder as a first description line "f:&lt;name&gt;"; without it the line would show
/// as plain text, so folders are only set while GPS Folders is loaded.
/// </summary>
internal static class SectorFolder
{
	private const string Tag = "f:";

	// GPS Folders' own limits: 1 to 32 characters, no ":", no line breaks, not ending in "GPS".
	private const int MaxLength = 32;

	private static PropertyInfo s_serverProperty;

	private static bool? s_gpsFoldersLoaded;

	/// <summary>
	/// True if the GPS Folders plugin is loaded. Pulsar builds GitHub plugins as "plugin.dll", so it's found by its
	/// entry type rather than its assembly name. Plugins load before any world, so the answer is kept once known.
	/// </summary>
	public static bool GpsFoldersLoaded
	{
		get
		{
			if (s_gpsFoldersLoaded == null)
			{
				try
				{
					s_gpsFoldersLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && a.GetType("GpsFolders.Plugin", false) != null);
				}
				catch (Exception ex)
				{
					MyLog.Default.WriteLineAndConsole($"[OreScout] Could not check for GPS Folders: {ex.Message}");
					s_gpsFoldersLoaded = false;
				}
			}
			return s_gpsFoldersLoaded.Value;
		}
	}

	/// <summary>The folder for new markers: the current sector while GPS Folders is loaded, otherwise null.</summary>
	public static string ForNewMarkers()
	{
		return GpsFoldersLoaded ? CurrentSectorName() : null;
	}

	/// <summary>
	/// The sector the player is in. On a Nexus cluster every sector is its own server, and SeamlessClient swaps in a
	/// new multiplayer client named after the target server on each transfer, while the session keeps the cluster's
	/// world name. So the server name comes first; the world name is the fallback (single player).
	/// Read on every scan, never cached, because a seamless transfer changes it without reloading the session.
	/// </summary>
	public static string CurrentSectorName()
	{
		string name = null;
		try
		{
			MyMultiplayerBase multiplayer = MyMultiplayer.Static;
			if (multiplayer != null)
			{
				// MyMultiplayerClient is internal; its Server item carries the name from the server list or Nexus transfer.
				Type type = multiplayer.GetType();
				if (s_serverProperty == null || s_serverProperty.DeclaringType != type)
				{
					s_serverProperty = type.GetProperty("Server", BindingFlags.Instance | BindingFlags.Public);
				}
				object server = s_serverProperty?.GetValue(multiplayer);
				name = server?.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(server) as string;
				if (string.IsNullOrWhiteSpace(name))
				{
					name = multiplayer.HostName;
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] Could not read the server name: {ex.Message}");
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			name = MyAPIGateway.Session?.Name;
		}
		return Clean(name);
	}

	/// <summary>A folder name GPS Folders accepts, or null if nothing usable is left.</summary>
	public static string Clean(string name)
	{
		if (name == null)
		{
			return null;
		}
		name = new string(name.Select(c => c == ':' || c == '\r' || c == '\n' ? ' ' : c).ToArray()).Trim();
		if (name.Length > MaxLength)
		{
			name = name.Substring(0, MaxLength).TrimEnd();
		}
		while (name.EndsWith("GPS", StringComparison.Ordinal))
		{
			name = name.Substring(0, name.Length - 3).TrimEnd();
		}
		return name.Length == 0 ? null : name;
	}

	/// <summary>True if the description already starts with a folder line, set by OreScout or by the player.</summary>
	public static bool HasFolder(string description)
	{
		return description != null && description.StartsWith(Tag, StringComparison.Ordinal);
	}

	/// <summary>The folder line of <paramref name="description"/> including its line break, or "" if it has none.</summary>
	public static string FolderLine(string description)
	{
		if (!HasFolder(description))
		{
			return "";
		}
		int end = description.IndexOf('\n');
		return end < 0 ? description + "\n" : description.Substring(0, end + 1);
	}

	/// <summary>Puts <paramref name="description"/> into <paramref name="folder"/> unless it already has a folder.</summary>
	public static string AddTo(string description, string folder)
	{
		if (folder == null || HasFolder(description))
		{
			return description;
		}
		return Tag + folder + "\n" + (description ?? "");
	}
}
