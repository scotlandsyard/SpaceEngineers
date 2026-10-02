using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Sandbox.ModAPI;
using VRage;
using VRage.Utils;
using VRageMath;

namespace FatAlbert;

/// <summary>
/// The player's own names for planets (servers often call them something other than the planet type), kept per
/// world in local storage by planet entity id. Names come from GPS text pasted in (each GPS is matched to the
/// planet whose gravity it's in) or are typed in the window.
/// </summary>
internal static class PlanetNames
{
	public class Entry
	{
		public string Name;

		/// <summary>Where the name came from: the GPS text, or "typed".</summary>
		public string Source;
	}

	public class ImportReport
	{
		public int Found;

		public readonly List<string> Named = new List<string>();

		public readonly List<string> NoPlanet = new List<string>();

		/// <summary>Planets that matched more than one GPS; the one nearest the planet's centre won.</summary>
		public readonly List<string> Clashes = new List<string>();
	}

	private const string FilePrefix = "FatAlbert_";

	public const int MaxLength = 40;

	// The game's own GPS pattern (MyGpsCollection.m_ScanPattern), so anything the GPS screen can paste works here.
	private static readonly Regex GpsPattern = new Regex("GPS:([^:]{0,32}):([\\d\\.-]*):([\\d\\.-]*):([\\d\\.-]*):");

	private static readonly Dictionary<long, Entry> s_names = new Dictionary<long, Entry>();

	private static string s_world;

	public static string Get(long planetId)
	{
		return s_names.TryGetValue(planetId, out Entry entry) ? entry.Name : null;
	}

	public static string Source(long planetId)
	{
		return s_names.TryGetValue(planetId, out Entry entry) ? entry.Source : null;
	}

	public static void Set(long planetId, string name, string source)
	{
		name = Clean(name);
		if (name.Length == 0)
		{
			s_names.Remove(planetId);
		}
		else
		{
			s_names[planetId] = new Entry { Name = name, Source = Clean(source) };
		}
		Save();
	}

	public static void Reset(long planetId)
	{
		if (s_names.Remove(planetId))
		{
			Save();
		}
	}

	/// <summary>Reads the clipboard the way the game's GPS screen does: on a short-lived STA thread.</summary>
	public static string ReadClipboard()
	{
		string text = null;
		Thread thread = new Thread(() =>
		{
			try
			{
				text = MyVRage.Platform.System.Clipboard;
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Fat Albert] Clipboard: {ex.Message}");
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join(2000);
		return text;
	}

	/// <summary>
	/// Names planets from every GPS in the text. A GPS belongs to the planet whose gravity it's inside, nearest that
	/// planet's surface first (so a moon's GPS inside a planet's gravity still goes to the moon). The coordinates
	/// only need to be roughly right: anywhere in the planet's gravity counts.
	/// </summary>
	public static ImportReport Import(string text, IList<PlanetInfo> planets)
	{
		ImportReport report = new ImportReport();
		Dictionary<long, KeyValuePair<string, double>> best = new Dictionary<long, KeyValuePair<string, double>>();
		foreach (Match match in GpsPattern.Matches(text ?? ""))
		{
			if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
				|| !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
				|| !double.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
			{
				continue;
			}
			string name = Clean(match.Groups[1].Value);
			if (name.Length == 0)
			{
				continue;
			}
			report.Found++;
			Vector3D point = new Vector3D(x, y, z);
			PlanetInfo planet = planets
				.Where(p => Vector3D.Distance(point, p.Center) <= p.GravityLimit)
				.OrderBy(p => Vector3D.Distance(point, p.Center) - p.AverageRadius)
				.FirstOrDefault();
			if (planet == null)
			{
				report.NoPlanet.Add(name);
				continue;
			}
			double fromCentre = Vector3D.Distance(point, planet.Center);
			if (best.TryGetValue(planet.Id, out KeyValuePair<string, double> previous))
			{
				report.Clashes.Add($"{previous.Key} / {name}");
				if (previous.Value <= fromCentre)
				{
					continue;
				}
			}
			best[planet.Id] = new KeyValuePair<string, double>(name, fromCentre);
		}
		foreach (KeyValuePair<long, KeyValuePair<string, double>> pick in best)
		{
			s_names[pick.Key] = new Entry { Name = pick.Value.Key, Source = "GPS " + pick.Value.Key };
			report.Named.Add(pick.Value.Key);
		}
		if (best.Count > 0)
		{
			Save();
		}
		return report;
	}

	private static string Clean(string text)
	{
		string clean = new string((text ?? "").Where(c => !char.IsControl(c) && c != '=' && c != '|').ToArray()).Trim();
		return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
	}

	private static string FileName => $"{FilePrefix}{s_world}_PlanetNames.txt";

	private static string WorldName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		return new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
	}

	/// <summary>Lines of "planet entity id=name|source".</summary>
	public static void Load()
	{
		s_names.Clear();
		s_world = WorldName();
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(PlanetNames)))
			{
				return;
			}
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(PlanetNames)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					int equals = line.IndexOf('=');
					if (equals <= 0 || !long.TryParse(line.Substring(0, equals), out long id))
					{
						continue;
					}
					string[] parts = line.Substring(equals + 1).Split('|');
					if (parts[0].Trim().Length > 0)
					{
						s_names[id] = new Entry { Name = parts[0].Trim(), Source = parts.Length > 1 ? parts[1].Trim() : "" };
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] Could not read planet names: {ex.Message}");
		}
	}

	public static void Unload()
	{
		s_names.Clear();
		s_world = null;
	}

	private static void Save()
	{
		if (s_world == null)
		{
			return;
		}
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(PlanetNames)))
			{
				foreach (KeyValuePair<long, Entry> entry in s_names)
				{
					writer.WriteLine($"{entry.Key}={entry.Value.Name}|{entry.Value.Source}");
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] Could not save planet names: {ex.Message}");
		}
	}
}
