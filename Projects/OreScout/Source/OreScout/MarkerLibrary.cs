using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace OreScout;

/// <summary>A GPS marker saved in the library, split into the parts needed to rebuild its name later.</summary>
public class LibraryEntry
{
	/// <summary>The marker's hidden key, e.g. "Asteroid:123456:Gold". Unique within the library.</summary>
	public string Key;

	/// <summary>Name prefix such as "Asteroid", "Planet" or "Deposit".</summary>
	public string Prefix;

	/// <summary>Ore name for ore markers, block or grid name for the others.</summary>
	public string Label;

	/// <summary>Everything after the distance in the original name, e.g. ", ~12,000 kg, 3 asteroids" or ", Enemy".</summary>
	public string Details;

	public string Description;

	public double X;

	public double Y;

	public double Z;

	public uint Color;

	/// <summary>Estimated ore mass, or 0 for markers without an amount.</summary>
	public double MassKg;

	public DateTime Saved;

	public Vector3D Position => new Vector3D(X, Y, Z);

	/// <summary>Details without the ore amount, e.g. "3 asteroids" or "Enemy".</summary>
	public string Info => MassPattern.Replace(Details ?? "", "").Trim(' ', ',');

	internal static readonly Regex MassPattern = new Regex(@",?\s*~([0-9][0-9.,\s  ]*)\s*kg", RegexOptions.Compiled);
}

public class LibraryFile
{
	public List<LibraryEntry> Entries = new List<LibraryEntry>();
}

/// <summary>
/// Stores exported GPS markers per world in the plugin's local storage, so they can be kept out of
/// the GPS list and brought back one at a time.
/// </summary>
public static class MarkerLibrary
{
	/// <summary>Marker kinds that Export pulls out of the GPS list. Deposit markers stay in GPS.</summary>
	public static readonly string[] ExportKinds = { "Ore" };

	private const string KeyLinePrefix = "OS:";

	// "<prefix> - <label> (<distance><details>)", e.g. "Asteroid - Gold (8.2 km, ~12,000 kg, 3 asteroids)".
	private static readonly Regex NamePattern = new Regex(@"^(?<prefix>.+?) - (?<label>.+) \((?<distance>[0-9][0-9.,]*\s*k?m)(?<details>.*)\)$", RegexOptions.Compiled);

	private static LibraryFile _file;

	private static string _fileWorld;

	public static List<LibraryEntry> Entries
	{
		get
		{
			EnsureLoaded();
			return _file.Entries;
		}
	}

	/// <summary>Moves every exportable OreScout marker from the player's GPS list into the library. Returns how many moved.</summary>
	public static int ExportFromGps()
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return 0;
		}
		EnsureLoaded();
		int moved = 0;
		foreach (IMyGps gps in new List<IMyGps>(gpsCollection.GetGpsList(player.IdentityId)))
		{
			string key = ReadKey(gps.Description);
			if (key == null || !ExportKinds.Any(kind => key.StartsWith(kind + ":", StringComparison.Ordinal)))
			{
				continue;
			}
			LibraryEntry entry = ToEntry(key, gps);
			_file.Entries.RemoveAll(e => e.Key == key);
			_file.Entries.Add(entry);
			gpsCollection.RemoveGps(player.IdentityId, gps);
			moved++;
		}
		if (moved > 0)
		{
			Save();
		}
		return moved;
	}

	/// <summary>Adds a library entry back to the GPS list. Returns false if a marker with the same key is already there.</summary>
	public static bool ImportToGps(LibraryEntry entry)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return false;
		}
		foreach (IMyGps existing in gpsCollection.GetGpsList(player.IdentityId))
		{
			if (ReadKey(existing.Description) == entry.Key)
			{
				return false;
			}
		}
		IMyGps gps = gpsCollection.Create(BuildName(entry, player.GetPosition()), entry.Description, entry.Position, showOnHud: true);
		gps.GPSColor = new Color(entry.Color);
		gpsCollection.AddGps(player.IdentityId, gps);
		return true;
	}

	public static void Delete(LibraryEntry entry)
	{
		EnsureLoaded();
		if (_file.Entries.Remove(entry))
		{
			Save();
		}
	}

	/// <summary>Deletes several entries and saves once. Returns how many were removed.</summary>
	public static int DeleteMany(IEnumerable<LibraryEntry> entries)
	{
		EnsureLoaded();
		HashSet<LibraryEntry> doomed = new HashSet<LibraryEntry>(entries);
		int removed = _file.Entries.RemoveAll(doomed.Contains);
		if (removed > 0)
		{
			Save();
		}
		return removed;
	}

	/// <summary>Rebuilds the marker's name with the distance from <paramref name="from"/> instead of the stale one.</summary>
	public static string BuildName(LibraryEntry entry, Vector3D from)
	{
		if (string.IsNullOrEmpty(entry.Prefix))
		{
			return entry.Label;
		}
		return $"{entry.Prefix} - {entry.Label} ({GpsMarkers.FormatDistance(Vector3D.Distance(from, entry.Position))}{entry.Details})";
	}

	private static LibraryEntry ToEntry(string key, IMyGps gps)
	{
		LibraryEntry entry = new LibraryEntry
		{
			Key = key,
			Label = gps.Name,
			Details = "",
			Description = gps.Description,
			X = gps.Coords.X,
			Y = gps.Coords.Y,
			Z = gps.Coords.Z,
			Color = gps.GPSColor.PackedValue,
			Saved = DateTime.Now
		};
		Match match = NamePattern.Match(gps.Name ?? "");
		if (match.Success)
		{
			entry.Prefix = match.Groups["prefix"].Value;
			entry.Label = match.Groups["label"].Value;
			entry.Details = match.Groups["details"].Value;
			Match mass = LibraryEntry.MassPattern.Match(entry.Details);
			// Amounts are written with thousands separators and no decimals, so the digits alone are the value.
			if (mass.Success && double.TryParse(new string(mass.Groups[1].Value.Where(char.IsDigit).ToArray()), out double kg))
			{
				entry.MassKg = kg;
			}
		}
		return entry;
	}

	private static string ReadKey(string description)
	{
		if (string.IsNullOrEmpty(description))
		{
			return null;
		}
		foreach (string line in description.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith(KeyLinePrefix, StringComparison.Ordinal))
			{
				return trimmed.Substring(KeyLinePrefix.Length);
			}
		}
		return null;
	}

	private static string FileName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"OreScout_Markers_{safe}.xml";
	}

	private static void EnsureLoaded()
	{
		string fileName = FileName();
		if (_file != null && _fileWorld == fileName)
		{
			return;
		}
		_fileWorld = fileName;
		_file = new LibraryFile();
		try
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(MarkerLibrary)))
			{
				using TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(fileName, typeof(MarkerLibrary));
				_file = MyAPIGateway.Utilities.SerializeFromXML<LibraryFile>(reader.ReadToEnd()) ?? new LibraryFile();
			}
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] Could not read {fileName}: {e}");
		}
	}

	private static void Save()
	{
		try
		{
			using TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(_fileWorld, typeof(MarkerLibrary));
			writer.Write(MyAPIGateway.Utilities.SerializeToXML(_file));
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] Could not save {_fileWorld}: {e}");
		}
	}

	/// <summary>Forgets the loaded library so the next world starts fresh.</summary>
	public static void Unload()
	{
		_file = null;
		_fileWorld = null;
	}
}
