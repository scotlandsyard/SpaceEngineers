using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;
using VRageMath;

namespace OreScout;

/// <summary>
/// Adds scan actions to ore detector blocks. Every scan starts at the detector and reaches only as far as
/// the detector's own range, so it reveals nothing the vanilla ore detector couldn't already show.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class OreScoutSession : MySessionComponentBase
{
	private class ScanSettings
	{
		public int MinDepositVoxels;

		public bool CreateGps = true;

		public bool ShowChat = true;

		public double YieldBonusPercent;

		public double MergeRadius;

		public double DepositSpacing;

		public HashSet<string> Ores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Describes one scan: its Custom Data section and defaults.</summary>
	private class ScanType
	{
		public string Section;

		public string MarkerKind;

		public string Title;

		public int DefaultMinVoxels;

		/// <summary>Default for MergeRadius, or null when this scan has no such setting.</summary>
		public double? DefaultMergeRadius;

		/// <summary>Set for the deposit scan: default for DepositSpacing, and marks every deposit separately.</summary>
		public double? DefaultDepositSpacing;
	}

	private const string ChatSender = "OreScout";

	private const string ChatCommand = "/scout";

	// Modded detectors can have very long ranges; beyond this the voxel reads get too slow to be useful.
	private const double MaxScanRadius = 3000.0;

	private static readonly ScanType OreScan = new ScanType
	{
		Section = "OreScout",
		MarkerKind = "Ore",
		Title = "Ore scan",
		DefaultMinVoxels = 8,
		DefaultMergeRadius = 1500.0
	};

	private static readonly ScanType DepositScan = new ScanType
	{
		Section = "OreScout Deposits",
		MarkerKind = "Deposit",
		Title = "Deposit scan",
		DefaultMinVoxels = 64,
		DefaultDepositSpacing = 8.0
	};

	private static Color OreColor => new Color(255, 105, 180);

	private static Color DepositColor => new Color(180, 120, 255);

	private static readonly HashSet<string> FallbackOres = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Iron", "Nickel", "Cobalt", "Silicon", "Silver", "Gold", "Platinum", "Uranium", "Magnesium", "Ice" };

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

	private bool _scanRunning;

	private volatile bool _unloading;

	private HashSet<string> _cachedWorldOres;

	public override void BeforeStart()
	{
		try
		{
			CreateActions();
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		_unloading = true;
		try
		{
			MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
		}
		catch
		{
		}
		MarkerLibrary.Unload();
	}

	private void CreateActions()
	{
		if (_actions.Count > 0)
		{
			return;
		}
		AddAction("OreScoutOreScan", "Scout Ore", b => StartScan(b, OreScan));
		AddAction("OreScoutDepositScan", "Scout Deposits", b => StartScan(b, DepositScan));
		AddAction("OreScoutClearDeposits", "Clear Deposit Markers", _ => ClearDepositMarkers());
		AddAction("OreScoutMarkerLibrary", "Marker Library", _ => OpenMarkerLibrary());
	}

	private void AddAction(string id, string name, Action<IMyOreDetector> run)
	{
		IMyTerminalAction action = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>(id);
		action.Name = new StringBuilder(name);
		action.Icon = "Textures\\GUI\\Icons\\Actions\\Start.dds";
		action.Enabled = (IMyTerminalBlock b) => b is IMyOreDetector;
		action.ValidForGroups = false;
		action.Action = (IMyTerminalBlock b) =>
		{
			if (b is IMyOreDetector detector)
			{
				run(detector);
			}
		};
		_actions.Add(action);
	}

	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if (block is IMyOreDetector)
			{
				EnsureCustomData(block);
				foreach (IMyTerminalAction action in _actions)
				{
					if (!actions.Contains(action))
					{
						actions.Add(action);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] CustomActionGetter: {ex}");
		}
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		if (messageText != null && messageText.Trim().Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			sendToOthers = false;
			OpenMarkerLibrary();
		}
	}

	private static bool SessionReady()
	{
		return MyAPIGateway.Session?.Player != null && MyAPIGateway.Utilities != null;
	}

	/// <summary>
	/// The detector's range in metres. IMyOreDetector.Range is a percentage of the block's maximum range,
	/// so it has to be scaled by the definition, exactly as the terminal's range slider does.
	/// </summary>
	private static double DetectorRangeMeters(IMyOreDetector detector)
	{
		if (detector is MyCubeBlock block && block.BlockDefinition is MyOreDetectorDefinition definition)
		{
			return detector.Range / 100.0 * definition.MaximumRange;
		}
		return 0.0;
	}

	/// <summary>Returns why the detector can't scan, or null if it can.</summary>
	private static string DetectorProblem(IMyOreDetector detector)
	{
		if (!detector.IsFunctional)
		{
			return "is damaged";
		}
		if (!detector.Enabled)
		{
			return "is turned off";
		}
		if (!detector.IsWorking)
		{
			return "has no power";
		}
		return null;
	}

	private void StartScan(IMyOreDetector detector, ScanType type)
	{
		if (!SessionReady())
		{
			return;
		}
		string problem = DetectorProblem(detector);
		if (problem != null)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{detector.CustomName} {problem}.");
			return;
		}
		if (_scanRunning)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "A scan is already running. Wait for it to finish.");
			return;
		}
		try
		{
			ScanSettings settings = ReadSettings(detector, type);
			double radius = Math.Min(DetectorRangeMeters(detector), MaxScanRadius);
			if (radius <= 0.0)
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Could not read the range of {detector.CustomName}.");
				return;
			}
			Vector3D origin = detector.GetPosition();
			string detectedBy = detector.CustomName;
			List<VoxelTarget> targets = VoxelOreScanner.FindTargets(origin, radius, VoxelScanKind.All);
			float worldHarvestMultiplier = MyAPIGateway.Session.SessionSettings?.HarvestRatioMultiplier ?? 1f;
			Dictionary<byte, OreMaterial> materials = VoxelOreScanner.BuildMaterialMap(settings.Ores, worldHarvestMultiplier * (1.0 + settings.YieldBonusPercent / 100.0));
			if (settings.ShowChat)
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{type.Title} within {GpsMarkers.FormatDistance(radius)} of {detectedBy}...");
			}
			if (targets.Count == 0 || materials.Count == 0)
			{
				FinishScan(type, settings, origin, radius, detectedBy, new List<OreDeposit>());
				return;
			}
			_scanRunning = true;
			List<OreDeposit> deposits = null;
			Exception error = null;
			MyAPIGateway.Parallel.StartBackground(() =>
			{
				try
				{
					deposits = type.DefaultDepositSpacing.HasValue
						? VoxelOreScanner.ScanDeposits(targets, origin, radius, materials, settings.DepositSpacing, () => _unloading)
						: VoxelOreScanner.Scan(targets, origin, radius, materials, () => _unloading);
				}
				catch (Exception ex)
				{
					error = ex;
				}
			}, () =>
			{
				_scanRunning = false;
				if (_unloading)
				{
					return;
				}
				if (error != null)
				{
					MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{type.Title} failed: {error.Message}");
					MyLog.Default.WriteLineAndConsole($"[OreScout] {error}");
					return;
				}
				FinishScan(type, settings, origin, radius, detectedBy, deposits);
			});
		}
		catch (Exception ex)
		{
			_scanRunning = false;
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{type.Title} failed: {ex.Message}");
			MyLog.Default.WriteLineAndConsole($"[OreScout] {ex}");
		}
	}

	private void FinishScan(ScanType type, ScanSettings settings, Vector3D origin, double radius, string detectedBy, List<OreDeposit> deposits)
	{
		try
		{
			List<OreDeposit> kept = deposits.Where(d => d.VolumeM3 >= settings.MinDepositVoxels && Vector3D.Distance(origin, d.Position) <= radius).ToList();
			if (settings.ShowChat)
			{
				if (kept.Count == 0)
				{
					MyAPIGateway.Utilities.ShowMessage(ChatSender, "No ore deposits in range.");
				}
				else
				{
					string byOre = string.Join(", ", from d in kept
						group d by d.OreName into g
						orderby g.Key
						select g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
					MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Found {kept.Count} deposit(s): {byOre}");
				}
			}
			if (!settings.CreateGps)
			{
				return;
			}
			List<Marker> markers = new List<Marker>();
			bool separateDeposits = type.DefaultDepositSpacing.HasValue;
			foreach (DepositGroup group in GroupDeposits(kept, settings.MergeRadius))
			{
				OreDeposit main = group.Main;
				string distance = GpsMarkers.FormatDistance(Vector3D.Distance(origin, main.Position));
				string prefix = separateDeposits ? "Deposit" : main.IsPlanet ? "Planet" : "Asteroid";
				string extra = group.SourceCount > 1 ? $", {group.SourceCount} asteroids" : "";
				markers.Add(new Marker
				{
					// One asteroid has many deposits of the same ore, so deposit markers are told apart by position (to 10 m).
					Key = separateDeposits
						? $"{main.VoxelEntityId}:{main.OreName}:{Math.Round(main.Position.X / 10.0)},{Math.Round(main.Position.Y / 10.0)},{Math.Round(main.Position.Z / 10.0)}"
						: $"{main.VoxelEntityId}:{main.OreName}",
					Name = $"{prefix} - {main.OreName} ({distance}, ~{group.MassKg:N0} kg{extra})",
					Position = main.Position,
					Color = separateDeposits ? DepositColor : OreColor
				});
			}
			GpsMarkers.Summary summary = GpsMarkers.Apply(type.MarkerKind, null, markers, detectedBy, origin, 0.0, radius);
			if (settings.ShowChat && (summary.Added + summary.Updated + summary.Removed) > 0)
			{
				MyAPIGateway.Utilities.ShowMessage(ChatSender, "GPS: " + summary);
			}
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"{type.Title} failed: {ex.Message}");
			MyLog.Default.WriteLineAndConsole($"[OreScout] {ex}");
		}
	}

	private class DepositGroup
	{
		public OreDeposit Main;

		public double MassKg;

		public int SourceCount;
	}

	/// <summary>
	/// Merges asteroid deposits of the same ore within <paramref name="radius"/> of the group's largest
	/// deposit, so a cluster of small asteroids gets one marker. Planet deposits are never merged with
	/// asteroid ones. A radius of 0 keeps every deposit separate.
	/// </summary>
	private static List<DepositGroup> GroupDeposits(List<OreDeposit> deposits, double radius)
	{
		List<DepositGroup> groups = new List<DepositGroup>();
		foreach (IGrouping<string, OreDeposit> ore in deposits.GroupBy(d => (d.IsPlanet ? "P:" : "A:") + d.OreName, StringComparer.OrdinalIgnoreCase))
		{
			List<OreDeposit> remaining = ore.OrderByDescending(d => d.MassKg).ToList();
			while (remaining.Count > 0)
			{
				OreDeposit main = remaining[0];
				List<OreDeposit> members = remaining.Where(d => d == main || (radius > 0.0 && Vector3D.DistanceSquared(d.Position, main.Position) <= radius * radius)).ToList();
				remaining.RemoveAll(members.Contains);
				groups.Add(new DepositGroup
				{
					Main = main,
					MassKg = members.Sum(d => d.MassKg),
					SourceCount = members.Select(d => d.VoxelEntityId).Distinct().Count()
				});
			}
		}
		return groups;
	}

	private static void ClearDepositMarkers()
	{
		if (!SessionReady())
		{
			return;
		}
		int removed = GpsMarkers.RemoveAll(DepositScan.MarkerKind);
		MyAPIGateway.Utilities.ShowMessage(ChatSender, removed == 0 ? "No deposit markers to clear." : $"Removed {removed} deposit marker(s).");
	}

	private static void OpenMarkerLibrary()
	{
		if (!SessionReady())
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new MarkerLibraryScreen());
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the marker library: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[OreScout] {ex}");
		}
	}

	private static MyIni ParseCustomData(IMyTerminalBlock block)
	{
		MyIni ini = new MyIni();
		ini.TryParse(block.CustomData ?? "", out var _);
		return ini;
	}

	/// <summary>Adds any missing OreScout sections or keys to the detector's Custom Data without touching values already there.</summary>
	private static void EnsureCustomData(IMyTerminalBlock block)
	{
		try
		{
			MyIni ini = ParseCustomData(block);
			bool changed = false;
			void Default(string section, string key, object value)
			{
				if (!ini.ContainsKey(section, key))
				{
					ini.Set(section, key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
					changed = true;
				}
			}
			foreach (ScanType type in new[] { OreScan, DepositScan })
			{
				Default(type.Section, "Ores", "");
				Default(type.Section, "MinDepositVoxels", type.DefaultMinVoxels);
				Default(type.Section, "YieldBonusPercent", 0);
				if (type.DefaultMergeRadius.HasValue)
				{
					Default(type.Section, "MergeRadius", type.DefaultMergeRadius.Value);
				}
				if (type.DefaultDepositSpacing.HasValue)
				{
					Default(type.Section, "DepositSpacing", type.DefaultDepositSpacing.Value);
				}
				Default(type.Section, "CreateGps", true);
				Default(type.Section, "ShowChat", true);
			}
			if (changed)
			{
				block.CustomData = ini.ToString();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[OreScout] EnsureCustomData: {ex}");
		}
	}

	private ScanSettings ReadSettings(IMyTerminalBlock block, ScanType type)
	{
		EnsureCustomData(block);
		MyIni ini = ParseCustomData(block);
		ScanSettings settings = new ScanSettings
		{
			MinDepositVoxels = Math.Max(1, ini.Get(type.Section, "MinDepositVoxels").ToInt32(type.DefaultMinVoxels)),
			CreateGps = ini.Get(type.Section, "CreateGps").ToBoolean(defaultValue: true),
			ShowChat = ini.Get(type.Section, "ShowChat").ToBoolean(defaultValue: true),
			// A drill can't yield less than nothing, so -100% is the floor.
			YieldBonusPercent = Math.Max(-100.0, ini.Get(type.Section, "YieldBonusPercent").ToDouble()),
			MergeRadius = type.DefaultMergeRadius.HasValue ? Math.Max(0.0, ini.Get(type.Section, "MergeRadius").ToDouble(type.DefaultMergeRadius.Value)) : 0.0,
			DepositSpacing = type.DefaultDepositSpacing.HasValue ? Math.Max(0.0, ini.Get(type.Section, "DepositSpacing").ToDouble(type.DefaultDepositSpacing.Value)) : 0.0
		};
		string configured = ini.Get(type.Section, "Ores").ToString(null);
		if (!string.IsNullOrWhiteSpace(configured))
		{
			foreach (string ore in configured.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (ore.Trim().Length > 0)
				{
					settings.Ores.Add(ore.Trim());
				}
			}
		}
		if (settings.Ores.Count == 0)
		{
			settings.Ores = GetAllWorldOres();
		}
		settings.Ores.Remove(VoxelOreScanner.StoneOre);
		return settings;
	}

	/// <summary>Every ore the world's voxel materials can yield: the rare materials, plus ice.</summary>
	private HashSet<string> GetAllWorldOres()
	{
		if (_cachedWorldOres == null)
		{
			_cachedWorldOres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (MyVoxelMaterialDefinition def in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
				{
					if (def != null && !string.IsNullOrWhiteSpace(def.MinedOre) && (def.IsRare || def.MinedOre.Trim().Equals("Ice", StringComparison.OrdinalIgnoreCase)))
					{
						_cachedWorldOres.Add(def.MinedOre.Trim());
					}
				}
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[OreScout] GetAllWorldOres failed: {ex}");
			}
			if (_cachedWorldOres.Count == 0)
			{
				_cachedWorldOres.UnionWith(FallbackOres);
			}
		}
		return new HashSet<string>(_cachedWorldOres, StringComparer.OrdinalIgnoreCase);
	}
}
