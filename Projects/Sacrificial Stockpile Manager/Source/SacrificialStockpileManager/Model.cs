using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>What the player chose for a block. Auto picks by block type (see <see cref="Effective"/>).</summary>
public enum BlockRole
{
	Auto,
	Storage,
	Intake,
	Stock,
	Manual
}

/// <summary>How the stock engine treats a block once Auto is resolved.</summary>
public enum Effective
{
	/// <summary>General or sorted storage: a source and a destination.</summary>
	Storage,
	/// <summary>Only emptied into storage (connectors, collectors).</summary>
	Intake,
	/// <summary>Holds only its limited items, kept between min and max; everything else is moved out.</summary>
	Stock,
	/// <summary>Machines, cockpits, turrets and so on: left alone except for their own limits (and output draining).</summary>
	Machine,
	/// <summary>Player only: never touched.</summary>
	Manual
}

public enum BlockKind
{
	Cargo,
	Connector,
	Collector,
	Assembler,
	SurvivalKit,
	Refinery,
	Reactor,
	GasGenerator,
	GasTank,
	Cockpit,
	Tool,
	Weapon,
	Display,
	Other
}

/// <summary>A minimum and/or maximum for one item. -1 means not set. For grid quotas only Min is used.</summary>
public class ItemLimit
{
	public string Item;

	public double Min = -1.0;

	public double Max = -1.0;

	public bool HasMin => Min >= 0.0;

	public bool HasMax => Max >= 0.0;
}

/// <summary>The player's settings for one block, keyed by its entity id.</summary>
public class BlockRules
{
	public long BlockId;

	public BlockRole Role;

	/// <summary>Categories (e.g. "Ingot") and item keys (e.g. "Ingot/Iron") this block is a sort target for.</summary>
	public List<string> Accept = new List<string>();

	public List<ItemLimit> Limits = new List<ItemLimit>();

	public bool IsEmpty => Role == BlockRole.Auto && Accept.Count == 0 && Limits.Count == 0;

	public ItemLimit Limit(string item)
	{
		return Limits.FirstOrDefault(l => l.Item == item);
	}
}

/// <summary>An LCD surface that shows one of the plugin's pages.</summary>
public class DisplayRule
{
	public long BlockId;

	public int Surface;

	public string Page;

	/// <summary>Key of the grid whose data is shown; 0 = the grid the display is on.</summary>
	public long SourceGrid;
}

/// <summary>Settings for a whole ship or station, keyed by the entity id of one of its grids.</summary>
public class GridRules
{
	public long GridId;

	/// <summary>Master switch for moving items. Off by default: nothing moves until the player switches it on.</summary>
	public bool Automation;

	public bool Autocraft;

	/// <summary>
	/// Keep production blocks clear: empty refinery and assembler output into storage, and take anything out of an
	/// assembler's input that its queue doesn't need. On by default (it still needs Automation).
	/// </summary>
	public bool DrainOutputs = true;

	public bool UseSurvivalKits;

	/// <summary>Disassemble what's above a quota's maximum, using one idle assembler at a time.</summary>
	public bool Disassemble;

	/// <summary>Take bottles that aren't full to a gas tank with Auto-Refill on, and put them back once full.</summary>
	public bool FillBottles;

	/// <summary>Ore keys refineries work on first, highest priority first.</summary>
	public List<string> OrePriority = new List<string>();

	public List<ItemLimit> Quotas = new List<ItemLimit>();

	public ItemLimit Quota(string item)
	{
		return Quotas.FirstOrDefault(q => q.Item == item);
	}
}

public class SettingsFile
{
	/// <summary>Format version; see Store.Load for what changed. 0 in files written before it existed.</summary>
	public int Version;

	public List<BlockRules> Blocks = new List<BlockRules>();

	public List<GridRules> Grids = new List<GridRules>();

	public List<DisplayRule> Displays = new List<DisplayRule>();

	/// <summary>Assemblers the plugin switched to disassembly, so it can switch them back (and only those) when done.</summary>
	public List<long> Disassemblers = new List<long>();
}

/// <summary>
/// What a block looked like when the grid was last loaded. Never changed once built, so saving can serialise it
/// on a background thread.
/// </summary>
public class BlockSnapshot
{
	public long Id;

	public string Name;

	public string Type;

	public BlockKind Kind;

	public bool Enabled = true;

	public bool Functional = true;

	/// <summary>Litres used and available, over all of the block's inventories.</summary>
	public double Volume;

	public double MaxVolume;

	/// <summary>Contents of the main (input) inventory, <see cref="Items.Encode"/> format.</summary>
	public string Items;

	/// <summary>Contents of a machine's output inventory, if it has one.</summary>
	public string Output;

	/// <summary>Short live state, e.g. "Iron Ore" for a refinery or "Steel Plate x40, Motor x8" for an assembler queue.</summary>
	public string Status;

	public int Surfaces;

	/// <summary>On a grid docked by connector rather than part of the ship or station itself.</summary>
	public bool Docked;

	private Dictionary<string, double> _items;

	private Dictionary<string, double> _output;

	public Dictionary<string, double> ItemAmounts => _items ??= SacrificialStockpileManager.Items.Decode(Items);

	public Dictionary<string, double> OutputAmounts => _output ??= SacrificialStockpileManager.Items.Decode(Output);

	public bool HasInventory => MaxVolume > 0.0 || !string.IsNullOrEmpty(Items) || !string.IsNullOrEmpty(Output);

	public double Fill => MaxVolume > 0.0 ? Volume / MaxVolume : 0.0;
}

/// <summary>A ship or station (grids sharing one terminal system), as last seen.</summary>
public class GridSnapshot
{
	public long Key;

	public string Name;

	public List<long> GridIds = new List<long>();

	public bool IsStation;

	public double X;

	public double Y;

	public double Z;

	public DateTime LastSeenUtc;

	public int BlockCount;

	public List<BlockSnapshot> Blocks = new List<BlockSnapshot>();

	private Dictionary<string, double> _totals;

	public Vector3D Position => new Vector3D(X, Y, Z);

	/// <summary>Every item on the grid, all inventories included.</summary>
	public Dictionary<string, double> Totals
	{
		get
		{
			if (_totals == null)
			{
				_totals = new Dictionary<string, double>();
				foreach (BlockSnapshot block in Blocks)
				{
					Add(_totals, block.ItemAmounts);
					Add(_totals, block.OutputAmounts);
				}
			}
			return _totals;
		}
	}

	public BlockSnapshot Block(long id)
	{
		return Blocks.FirstOrDefault(b => b.Id == id);
	}

	private static void Add(Dictionary<string, double> totals, Dictionary<string, double> amounts)
	{
		foreach (KeyValuePair<string, double> entry in amounts)
		{
			totals.TryGetValue(entry.Key, out double existing);
			totals[entry.Key] = existing + entry.Value;
		}
	}
}

public class GridsFile
{
	public List<GridSnapshot> Grids = new List<GridSnapshot>();
}
