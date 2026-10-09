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

	/// <summary>
	/// What this marker is of, e.g. the ore name; must be the last ":"-separated part of <see cref="Key"/>.
	/// Used to tell when a kept marker has been replaced by a newer one nearby.
	/// </summary>
	public string Group;
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
	/// <param name="keepMissingUnlessWithin">
	/// When set, markers that weren't found again are kept (asteroids get reset, so mined-out ore comes
	/// back) unless this scan has a marker of the same <see cref="Marker.Group"/> within this many metres,
	/// which means the old one has been replaced (for example when a merged group's biggest asteroid changes).
	/// </param>
	/// <param name="folder">
	/// GPS Folders folder for new markers, or null for none. Markers that are already in a folder stay there, so a
	/// rescan never undoes the player moving one.
	/// </param>
	public static Summary Apply(string kind, string legacyNamePrefix, List<Marker> markers, string detectedBy, Vector3D origin, double minDistance, double maxDistance, double? keepMissingUnlessWithin = null, string folder = null)
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
				string folderLine = SectorFolder.FolderLine(gps.Description);
				// The game finds the marker by its old hash, so edit the live object and let ModifyGps rehash it.
				gps.Name = marker.Name;
				gps.Description = folderLine.Length > 0 ? folderLine + description : SectorFolder.AddTo(description, folder);
				gps.Coords = marker.Position;
				gps.GPSColor = marker.Color;
				gpsCollection.ModifyGps(identityId, gps);
				summary.Updated++;
			}
			else
			{
				IMyGps created = gpsCollection.Create(marker.Name, SectorFolder.AddTo(description, folder), marker.Position, showOnHud: true);
				created.GPSColor = marker.Color;
				gpsCollection.AddGps(identityId, created);
				summary.Added++;
			}
		}
		foreach (KeyValuePair<string, IMyGps> leftover in existing)
		{
			IMyGps gps = leftover.Value;
			double distance = Vector3D.Distance(origin, gps.Coords);
			if (distance < minDistance || distance > maxDistance)
			{
				continue;
			}
			if (keepMissingUnlessWithin.HasValue && !IsReplaced(leftover.Key, gps.Coords, markers, keepMissingUnlessWithin.Value))
			{
				continue;
			}
			toRemove.Add(gps);
		}
		foreach (IMyGps gps in toRemove)
		{
			gpsCollection.RemoveGps(identityId, gps);
			summary.Removed++;
		}
		return summary;
	}

	/// <summary>True if this scan has a marker of the same group as <paramref name="key"/> within <paramref name="radius"/> of it.</summary>
	private static bool IsReplaced(string key, Vector3D position, List<Marker> markers, double radius)
	{
		string group = key.Substring(key.LastIndexOf(':') + 1);
		foreach (Marker marker in markers)
		{
			if (string.Equals(marker.Group, group, StringComparison.OrdinalIgnoreCase) && Vector3D.DistanceSquared(marker.Position, position) <= radius * radius)
			{
				return true;
			}
		}
		return false;
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
