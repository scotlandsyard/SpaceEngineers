using System;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>
/// One GPS marker per grid at its last known position. The description carries a hidden "SSM:grid:key" line,
/// so marking the same grid again moves the existing marker instead of adding another.
/// </summary>
internal static class GridGps
{
	private const string KeyLinePrefix = "SSM:grid:";

	private static readonly Color MarkerColor = new Color(255, 170, 60);

	/// <summary>Adds or moves the grid's marker. Returns true when an existing marker was updated.</summary>
	public static bool Mark(GridSnapshot grid)
	{
		IMyGpsCollection gpsCollection = MyAPIGateway.Session?.GPS;
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (gpsCollection == null || player == null)
		{
			return false;
		}
		string keyLine = KeyLinePrefix + grid.Key;
		string name = $"SSM - {grid.Name}";
		string description = $"Last seen {DateTime.Now - (DateTime.UtcNow - grid.LastSeenUtc):yyyy-MM-dd HH:mm}\n{keyLine}";
		IMyGps existing = gpsCollection.GetGpsList(player.IdentityId).FirstOrDefault(g => g.Description != null && g.Description.Split('\n').Any(l => l.Trim() == keyLine));
		if (existing != null)
		{
			// The game finds the marker by its old hash, so change the live object and let ModifyGps rehash it.
			existing.Name = name;
			existing.Description = description;
			existing.Coords = grid.Position;
			gpsCollection.ModifyGps(player.IdentityId, existing);
			return true;
		}
		IMyGps gps = gpsCollection.Create(name, description, grid.Position, showOnHud: true);
		gps.GPSColor = MarkerColor;
		gpsCollection.AddGps(player.IdentityId, gps);
		return false;
	}
}
