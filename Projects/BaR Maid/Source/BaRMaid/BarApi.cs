using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game;
using VRage.Scripting.MemorySafeTypes;
using IngameEntity = VRage.Game.ModAPI.Ingame.IMyEntity;
using IngameSlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;

namespace NeedyBOB;

/// <summary>
/// Reads a Build and Repair block through the terminal properties the SKO Nanobot Build and Repair mod
/// publishes for programmable block scripts. The mod registers these on clients too and the server sends
/// every system's state to all players, so they work from a client. On a client the lists are capped at
/// 24 entries (the mod's sync limit).
/// </summary>
internal static class BarApi
{
	private const string Prefix = "BuildAndRepair.";

	/// <summary>
	/// True for a ship welder that is a Build and Repair system. The mod adds its properties to every ship
	/// welder, but only returns values for its own blocks, so a non-null answer identifies one.
	/// </summary>
	public static bool IsBar(IMyShipWelder welder)
	{
		return welder != null && !welder.Closed && Get<MemorySafeDictionary<MyDefinitionId, int>>(welder, "MissingComponents") != null;
	}

	/// <summary>Components the system needs but can't find, by component id.</summary>
	public static Dictionary<MyDefinitionId, int> MissingComponents(IMyTerminalBlock block)
	{
		return Get<MemorySafeDictionary<MyDefinitionId, int>>(block, "MissingComponents") ?? Get<Dictionary<MyDefinitionId, int>>(block, "MissingComponents");
	}

	public static List<IngameSlimBlock> WeldTargets(IMyTerminalBlock block)
	{
		return GetList<IngameSlimBlock>(block, "PossibleTargets");
	}

	public static List<IngameSlimBlock> GrindTargets(IMyTerminalBlock block)
	{
		return GetList<IngameSlimBlock>(block, "PossibleGrindTargets");
	}

	public static List<IngameEntity> CollectTargets(IMyTerminalBlock block)
	{
		return GetList<IngameEntity>(block, "PossibleCollectTargets");
	}

	public static IngameSlimBlock CurrentWeldTarget(IMyTerminalBlock block)
	{
		return Get<IngameSlimBlock>(block, "CurrentTarget");
	}

	public static IngameSlimBlock CurrentGrindTarget(IMyTerminalBlock block)
	{
		return Get<IngameSlimBlock>(block, "CurrentGrindTarget");
	}

	/// <summary>Priority list entries as "classNumber;True/False", highest priority first.</summary>
	public static List<string> WeldPriority(IMyTerminalBlock block)
	{
		return GetList<string>(block, "WeldPriorityList");
	}

	public static List<string> GrindPriority(IMyTerminalBlock block)
	{
		return GetList<string>(block, "GrindPriorityList");
	}

	public static long? SearchMode(IMyTerminalBlock block)
	{
		return GetValue<long>(block, "Mode");
	}

	public static long? WorkMode(IMyTerminalBlock block)
	{
		return GetValue<long>(block, "WorkMode");
	}

	public static bool? AllowBuild(IMyTerminalBlock block)
	{
		return GetValue<bool>(block, "AllowBuild");
	}

	public static bool? UseIgnoreColor(IMyTerminalBlock block)
	{
		return GetValue<bool>(block, "UseIgnoreColor");
	}

	public static bool? ScriptControlled(IMyTerminalBlock block)
	{
		return GetValue<bool>(block, "ScriptControlled");
	}

	public static string SearchModeName(long? mode)
	{
		switch (mode)
		{
		case 1:
			return "Walk mode";
		case 2:
			return "Fly mode";
		case null:
			return "?";
		default:
			return mode.ToString();
		}
	}

	public static string WorkModeName(long? mode)
	{
		switch (mode)
		{
		case 1:
			return "Weld before grind";
		case 2:
			return "Grind before weld";
		case 4:
			return "Grind if weld gets stuck";
		case 8:
			return "Welding only";
		case 16:
			return "Grinding only";
		case null:
			return "?";
		default:
			return mode.ToString();
		}
	}

	// Numbering matches the mod's BlockClass enum (it starts at 1).
	private static readonly string[] BlockClassNames =
	{
		"", "Build and Repair systems", "Ship controllers", "Thrusters", "Gyroscopes", "Cargo containers", "Conveyors",
		"Weapons", "Power blocks", "Programmable blocks", "Projectors", "Functional blocks", "Production blocks", "Doors",
		"Armor blocks", "Display panels", "Lights", "Sensors", "Communication blocks", "Connectors", "Merge blocks"
	};

	/// <summary>Turns a priority entry into a readable class name and its enabled flag.</summary>
	public static bool TryParsePriority(string entry, out string className, out bool enabled)
	{
		className = null;
		enabled = false;
		if (string.IsNullOrEmpty(entry))
		{
			return false;
		}
		string[] parts = entry.Split(';');
		if (parts.Length < 2 || !bool.TryParse(parts[1], out enabled))
		{
			return false;
		}
		className = int.TryParse(parts[0], out int number) && number > 0 && number < BlockClassNames.Length ? BlockClassNames[number] : parts[0];
		return true;
	}

	private static T Get<T>(IMyTerminalBlock block, string id) where T : class
	{
		try
		{
			return (block.GetProperty(Prefix + id) as ITerminalProperty<T>)?.GetValue(block);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static T? GetValue<T>(IMyTerminalBlock block, string id) where T : struct
	{
		try
		{
			if (block.GetProperty(Prefix + id) is ITerminalProperty<T> property)
			{
				return property.GetValue(block);
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	private static List<T> GetList<T>(IMyTerminalBlock block, string id)
	{
		return (List<T>)Get<MemorySafeList<T>>(block, id) ?? Get<List<T>>(block, id);
	}
}
