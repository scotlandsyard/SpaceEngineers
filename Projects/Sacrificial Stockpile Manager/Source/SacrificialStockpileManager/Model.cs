using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
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

	/// <summary>Fill priority for storage: among blocks that accept an item equally well, a higher number fills first.</summary>
	public int Priority;

	public bool IsEmpty => Role == BlockRole.Auto && Accept.Count == 0 && Limits.Count == 0 && Priority == 0;

	public ItemLimit Limit(string item)
	{
		return Limits.FirstOrDefault(l => l.Item == item);
	}
}

/// <summary>Limits that apply to every block of one type on a grid.</summary>
public class TypeLimits
{
	public BlockKind Kind;

	public List<ItemLimit> Limits = new List<ItemLimit>();

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

	/// <summary>
	/// Also manage blocks that someone else owns (or nobody owns) but that are shared with this player. Off by
	/// default, so the plugin never takes from or fills a faction-mate's block on a shared grid.
	/// </summary>
	public bool IncludeShared;

	/// <summary>Limits for every block of a type on the grid (all reactors, all turrets...). A block's own limit for an item wins.</summary>
	public List<TypeLimits> TypeLimits = new List<TypeLimits>();

	public TypeLimits Type(BlockKind kind)
	{
		return TypeLimits.FirstOrDefault(t => t.Kind == kind);
	}

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

	/// <summary>Owned by someone else (or nobody) and the grid doesn't include shared blocks: the plugin leaves it alone.</summary>
	public bool NotYours;

	/// <summary>
	/// Which mechanical group (the grid itself, or one docked ship) the block is on: the lowest entity id among that
	/// group's grids. Quotas and assemblers are worked out per group, so a docked ship keeps its own.
	/// </summary>
	public long Unit;

	/// <summary>For assemblers: the shared assembler mode (Main, Coop, Manual), or null when not set.</summary>
	public string AssemblerMode;

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

	/// <summary>
	/// The grids of the ship or station itself (its mechanical group), without ships docked to it. Its settings are
	/// looked up only among these, so a docked ship's settings are never applied to the station, or the other way.
	/// </summary>
	public List<long> CoreGridIds = new List<long>();

	public bool IsStation;

	public double X;

	public double Y;

	public double Z;

	public DateTime LastSeenUtc;

	public int BlockCount;

	public List<BlockSnapshot> Blocks = new List<BlockSnapshot>();

	/// <summary>Ships docked to it by connector when it was last seen. Their blocks are in <see cref="Blocks"/> too.</summary>
	public List<DockedShip> DockedShips = new List<DockedShip>();

	/// <summary>
	/// Set on the view of one docked ship (see <see cref="ForDockedShip"/>): the snapshot of the grid it's docked to.
	/// Such a view isn't stored; it's made from that snapshot whenever it's needed.
	/// </summary>
	[XmlIgnore]
	public GridSnapshot DockedTo;

	/// <summary>For the view of a docked ship: its unit (see <see cref="BlockSnapshot.Unit"/>).</summary>
	[XmlIgnore]
	public long Unit;

	private Dictionary<string, double> _totals;

	public Vector3D Position => new Vector3D(X, Y, Z);

	/// <summary>A view of one docked ship: only its own blocks, keyed by its main grid like the ship is when undocked.</summary>
	public GridSnapshot ForDockedShip(DockedShip ship)
	{
		return new GridSnapshot
		{
			Key = ship.Key,
			Name = ship.Name,
			GridIds = ship.GridIds.ToList(),
			CoreGridIds = ship.GridIds.ToList(),
			IsStation = false,
			X = X,
			Y = Y,
			Z = Z,
			LastSeenUtc = LastSeenUtc,
			BlockCount = ship.BlockCount,
			Blocks = Blocks.Where(b => b.Unit == ship.Unit).ToList(),
			DockedTo = this,
			Unit = ship.Unit
		};
	}

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

/// <summary>A ship docked by connector to a grid, as part of that grid's snapshot.</summary>
public class DockedShip
{
	/// <summary>The ship's main grid: the key its own snapshot has once it undocks.</summary>
	public long Key;

	/// <summary>Its unit (see <see cref="BlockSnapshot.Unit"/>).</summary>
	public long Unit;

	public string Name;

	public List<long> GridIds = new List<long>();

	public int BlockCount;
}

/// <summary>Settings for the plugin itself, the same in every world.</summary>
public class PreferencesFile
{
	/// <summary>How much Stockpile Manager says in chat: Off, Quiet, Normal or Chatty.</summary>
	public string Personality = "Normal";
}

public class GridsFile
{
	public List<GridSnapshot> Grids = new List<GridSnapshot>();
}
