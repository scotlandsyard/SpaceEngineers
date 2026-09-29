using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace OreScout;

public class Marker
{
	/// <summary>Identifies the same thing across scans, e.g. "123456:Gold" for gold on one asteroid.</summary>
	public string Key;

	public string Name;

	public Vector3D Position;

	public Color Color;
}

/// <summary>
/// Keeps one GPS marker per scanned thing. Each marker's description carries a hidden "OS:kind:key"
/// line, so a later scan updates it in place instead of adding a duplicate.
/// </summary>
public static class GpsMarkers
{
	private const string KeyLinePrefix = "OS:";

	private const string DetectedByPrefix = "Detected by ";

	public struct Summary
	{
		public int Added;

		public int Updated;

		public int Removed;

		public override string ToString()
		{
			return $"{Added} new, {Updated} updated, {Removed} removed";
		}
	}

	/// <summary>
	/// Adds or updates markers for this scan. Markers of the same kind that sit inside the scanned
	/// range but were not found again (mined out, harvested, moved away) are removed, as are markers
	/// of this kind left by older versions of the plugin (<paramref name="legacyNamePrefix"/>).
	/// </summary>
	public static Summary Apply(string kind, string legacyNamePrefix, List<Marker> markers, string detectedBy, Vector3D origin, double minDistance, double maxDistance)
	{
		Summary summary = default;
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return summary;
		}
		long identityId = player.IdentityId;
		string kindPrefix = kind + ":";
		Dictionary<string, IMyGps> existing = new Dictionary<string, IMyGps>();
		List<IMyGps> toRemove = new List<IMyGps>();
		foreach (IMyGps gps in new List<IMyGps>(gpsCollection.GetGpsList(identityId)))
		{
			string key = ReadKey(gps.Description);
			if (key == null)
			{
				if (legacyNamePrefix != null && gps.Name != null && gps.Name.StartsWith(legacyNamePrefix, StringComparison.Ordinal) && gps.Description != null && gps.Description.StartsWith(DetectedByPrefix, StringComparison.Ordinal))
				{
					toRemove.Add(gps);
				}
			}
			else if (key.StartsWith(kindPrefix, StringComparison.Ordinal))
			{
				if (existing.ContainsKey(key))
				{
					toRemove.Add(gps);
				}
				else
				{
					existing[key] = gps;
				}
			}
		}
		foreach (Marker marker in markers)
		{
			string key = kindPrefix + marker.Key;
			string description = DetectedByPrefix + detectedBy + "\n" + KeyLinePrefix + key;
			if (existing.TryGetValue(key, out IMyGps gps))
			{
				existing.Remove(key);
				// The game finds the marker by its old hash, so edit the live object and let ModifyGps rehash it.
				gps.Name = marker.Name;
				gps.Description = description;
				gps.Coords = marker.Position;
				gps.GPSColor = marker.Color;
				gpsCollection.ModifyGps(identityId, gps);
				summary.Updated++;
			}
			else
			{
				IMyGps created = gpsCollection.Create(marker.Name, description, marker.Position, showOnHud: true);
				created.GPSColor = marker.Color;
				gpsCollection.AddGps(identityId, created);
				summary.Added++;
			}
		}
		foreach (IMyGps gps in existing.Values)
		{
			double distance = Vector3D.Distance(origin, gps.Coords);
			if (distance >= minDistance && distance <= maxDistance)
			{
				toRemove.Add(gps);
			}
		}
		foreach (IMyGps gps in toRemove)
		{
			gpsCollection.RemoveGps(identityId, gps);
			summary.Removed++;
		}
		return summary;
	}

	/// <summary>Removes every marker of one kind that this plugin created. Returns how many were removed.</summary>
	public static int RemoveAll(string kind)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return 0;
		}
		string kindPrefix = kind + ":";
		int removed = 0;
		foreach (IMyGps gps in new List<IMyGps>(gpsCollection.GetGpsList(player.IdentityId)))
		{
			string key = ReadKey(gps.Description);
			if (key != null && key.StartsWith(kindPrefix, StringComparison.Ordinal))
			{
				gpsCollection.RemoveGps(player.IdentityId, gps);
				removed++;
			}
		}
		return removed;
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

	public static string FormatDistance(double meters)
	{
		return meters >= 1000.0 ? $"{meters / 1000.0:F1} km" : $"{meters:F0} m";
	}
}
