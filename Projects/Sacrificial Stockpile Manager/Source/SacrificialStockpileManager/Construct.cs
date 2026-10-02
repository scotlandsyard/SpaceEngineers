using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace SacrificialStockpileManager;

/// <summary>A block the plugin can see on a loaded construct, with its inventories and resolved role.</summary>
internal class LiveBlock
{
	public IMyTerminalBlock Block;

	public BlockKind Kind;

	public BlockRules Rules;

	public Effective Role;

	/// <summary>On a grid joined to the main grid only through connectors (a docked ship, or the station it docked to).</summary>
	public bool Docked;

	/// <summary>Owned by someone else (or nobody), and the grid doesn't include shared blocks: never touched.</summary>
	public bool NotYours;

	/// <summary>The mechanical group the block is on (see <see cref="Construct.Units"/>).</summary>
	public long Unit;

	/// <summary>For assemblers: the mode shared with our other plugins (Manual means no plugin touches it).</summary>
	public TimShared.AssemblerMode AssemblerMode;

	/// <summary>The block's own limits plus the grid's limits for its type (its own win for the same item).</summary>
	public List<ItemLimit> Limits = new List<ItemLimit>();

	public ItemLimit Limit(string item)
	{
		return Limits.FirstOrDefault(l => l.Item == item);
	}

	/// <summary>True when the limit was set on this block itself rather than for its whole type.</summary>
	public bool IsOwnLimit(ItemLimit limit)
	{
		return Rules != null && Rules.Limits.Contains(limit);
	}

	/// <summary>The main inventory (a machine's input), or null for blocks without one.</summary>
	public MyInventory Input;

	/// <summary>A production block's output inventory, or null.</summary>
	public MyInventory Output;

	public int Surfaces;

	public string Name => Block.CustomName;
}

/// <summary>
/// A loaded ship or station: every grid that shares one terminal system. Refresh reads its blocks and records a
/// <see cref="GridSnapshot"/>, which is what the menu and the LCD pages show.
/// </summary>
internal class Construct
{
	private const int MaxLogLines = 60;

	public long Key;

	public IMyGridTerminalSystem Terminal;

	public List<IMyCubeGrid> Grids = new List<IMyCubeGrid>();

	public IMyCubeGrid MainGrid;

	/// <summary>
	/// The mechanical groups in this terminal system, by unit id (lowest grid entity id), with their grids' ids. The
	/// main grid's is <see cref="CoreUnit"/>; the others are ships docked by connector. Quotas, the stock they count
	/// and the assemblers they use are per unit, so docking never mixes a ship's quotas with a station's.
	/// </summary>
	public Dictionary<long, List<long>> Units = new Dictionary<long, List<long>>();

	public long CoreUnit;

	public List<LiveBlock> Blocks = new List<LiveBlock>();

	public GridSnapshot Snapshot;

	public double NextRefresh;

	public double NextEngine;

	public double NextCraft;

	/// <summary>Newest first.</summary>
	public readonly List<string> Log = new List<string>();

	/// <summary>Problems found by the last stock pass, e.g. a minimum that can't be met.</summary>
	public List<string> Warnings = new List<string>();

	/// <summary>Autocraft state per item key, from the last autocraft pass.</summary>
	public Dictionary<string, string> QuotaNotes = new Dictionary<string, string>();

	/// <summary>Per item key: game time before which autocraft won't queue it again (waits for the server).</summary>
	public readonly Dictionary<string, double> CraftCooldown = new Dictionary<string, double>();

	/// <summary>Sort now: the engine runs as if Automation were on until a pass moves nothing, or until this time.</summary>
	public double RunOnceUntil;

	/// <summary>Unload docked ships: their cargo is moved into storage until a pass moves nothing, or until this time.</summary>
	public double UnloadUntil;

	public LiveBlock Find(long blockId)
	{
		return Blocks.FirstOrDefault(b => b.Block.EntityId == blockId);
	}

	public void AddLog(string text)
	{
		Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
		if (Log.Count > MaxLogLines)
		{
			Log.RemoveRange(MaxLogLines, Log.Count - MaxLogLines);
		}
	}

	public static Effective Resolve(BlockSnapshot block)
	{
		return Resolve(block.Kind, Store.BlockRules(block.Id), block.Docked, block.NotYours);
	}

	/// <summary>
	/// The role a block acts in. A block that isn't yours (unless the grid includes shared blocks) is left alone
	/// whatever its role. Auto blocks on a grid docked by connector count as machines: they keep their own limits
	/// (so a docked ship's lockers restock), but a docked ship's cargo isn't sorted into the station. An
	/// assembler's mode shared with our other plugins only decides whether it gets orders (see AutoCraft), not how
	/// its inventory is handled, because every assembler is Manual until the player gives it a mode.
	/// </summary>
	public static Effective Resolve(BlockKind kind, BlockRules rules, bool docked, bool notYours = false)
	{
		if (notYours)
		{
			return Effective.Manual;
		}
		switch (rules?.Role ?? BlockRole.Auto)
		{
		case BlockRole.Storage:
			return Effective.Storage;
		case BlockRole.Intake:
			return Effective.Intake;
		case BlockRole.Stock:
			return Effective.Stock;
		case BlockRole.Manual:
			return Effective.Manual;
		}
		if (docked)
		{
			return Effective.Machine;
		}
		switch (kind)
		{
		case BlockKind.Cargo:
			return Effective.Storage;
		case BlockKind.Connector:
		case BlockKind.Collector:
			return Effective.Intake;
		default:
			return Effective.Machine;
		}
	}

	public static BlockKind KindOf(IMyTerminalBlock block)
	{
		if (block is IMyCargoContainer)
		{
			return BlockKind.Cargo;
		}
		if (block is IMyShipConnector)
		{
			return BlockKind.Connector;
		}
		if (block is IMyCollector)
		{
			return BlockKind.Collector;
		}
		if (block is IMyAssembler)
		{
			return block.BlockDefinition.TypeIdString.Contains("SurvivalKit") ? BlockKind.SurvivalKit : BlockKind.Assembler;
		}
		if (block is IMyRefinery)
		{
			return BlockKind.Refinery;
		}
		if (block is IMyReactor)
		{
			return BlockKind.Reactor;
		}
		if (block is IMyGasGenerator)
		{
			return BlockKind.GasGenerator;
		}
		if (block is IMyGasTank)
		{
			return BlockKind.GasTank;
		}
		if (block is IMyShipController)
		{
			return BlockKind.Cockpit;
		}
		if (block is IMyShipToolBase || block is IMyShipDrill)
		{
			return BlockKind.Tool;
		}
		if (block is IMyUserControllableGun)
		{
			return BlockKind.Weapon;
		}
		if (!block.HasInventory)
		{
			return BlockKind.Display;
		}
		return BlockKind.Other;
	}

	/// <summary>
	/// Litres used and available in the grid's storage: blocks acting as Storage or Stock, not counting docked
	/// ships. Machines, cockpits and tools are left out so they don't inflate the totals.
	/// </summary>
	public static void StorageVolume(GridSnapshot grid, out double used, out double max)
	{
		used = 0.0;
		max = 0.0;
		foreach (BlockSnapshot block in grid.Blocks)
		{
			if (block.Docked || !block.HasInventory)
			{
				continue;
			}
			Effective role = Resolve(block);
			if (role == Effective.Storage || role == Effective.Stock)
			{
				used += block.Volume;
				max += block.MaxVolume;
			}
		}
	}

	/// <summary>Everything the ship or station itself holds, in every inventory; a docked ship's cargo isn't counted.</summary>
	/// <param name="unit">A unit (mechanical group) to count instead, for a docked ship's own quotas.</param>
	public static Dictionary<string, double> StationTotals(GridSnapshot grid, long? unit = null)
	{
		Dictionary<string, double> totals = new Dictionary<string, double>();
		foreach (BlockSnapshot block in grid.Blocks.Where(b => unit.HasValue ? b.Unit == unit.Value : !b.Docked))
		{
			foreach (KeyValuePair<string, double> item in block.ItemAmounts.Concat(block.OutputAmounts))
			{
				totals.TryGetValue(item.Key, out double existing);
				totals[item.Key] = existing + item.Value;
			}
		}
		return totals;
	}

	public static bool IsProductionKind(BlockKind kind)
	{
		return kind == BlockKind.Assembler || kind == BlockKind.SurvivalKit || kind == BlockKind.Refinery;
	}

	/// <summary>The unit (mechanical group) of a grid: the lowest entity id among its grids. Fills <see cref="Units"/>.</summary>
	private long UnitOf(IMyCubeGrid grid, Dictionary<long, long> unitOfGrid)
	{
		if (unitOfGrid.TryGetValue(grid.EntityId, out long unit))
		{
			return unit;
		}
		List<IMyCubeGrid> group = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, group);
		if (!group.Contains(grid))
		{
			group.Add(grid);
		}
		unit = group.Min(g => g.EntityId);
		List<long> ids = group.Select(g => g.EntityId).ToList();
		foreach (long id in ids)
		{
			unitOfGrid[id] = unit;
		}
		Units[unit] = ids;
		return unit;
	}

	/// <summary>Reads the construct's blocks and stores a fresh snapshot.</summary>
	public void Refresh()
	{
		List<IMyTerminalBlock> found = new List<IMyTerminalBlock>();
		Terminal.GetBlocksOfType(found, b => !b.Closed && b.HasLocalPlayerAccess() && (b.HasInventory || (b is IngameSurfaceProvider provider && provider.SurfaceCount > 0)));

		List<LiveBlock> blocks = new List<LiveBlock>(found.Count);
		List<BlockSnapshot> snapshots = new List<BlockSnapshot>(found.Count);
		HashSet<long> gridIds = new HashSet<long>(Grids.Select(g => g.EntityId));
		// The terminal system also spans grids docked by connector. Each mechanical group (grids joined by rotors,
		// pistons, hinges) is a unit: the main grid's unit is this ship or station itself, any other is docked.
		Dictionary<long, long> unitOfGrid = new Dictionary<long, long>();
		Units = new Dictionary<long, List<long>>();
		CoreUnit = UnitOf(MainGrid, unitOfGrid);
		// Settings come from the grid's own grids only; a docked ship's settings stay with the ship.
		GridRules gridRules = Store.GridRules(Key, Units[CoreUnit]);
		long me = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
		bool includeShared = gridRules != null && gridRules.IncludeShared;
		foreach (IMyTerminalBlock block in found)
		{
			long unit = UnitOf(block.CubeGrid, unitOfGrid);
			LiveBlock live = new LiveBlock
			{
				Block = block,
				Kind = KindOf(block),
				Rules = Store.BlockRules(block.EntityId),
				Unit = unit,
				Docked = unit != CoreUnit,
				// The game lets you use blocks shared with your faction or with everyone, and blocks nobody owns. The
				// plugin only manages your own unless the grid's "include shared blocks" setting is on.
				NotYours = block.OwnerId != me && !includeShared,
				Surfaces = block is IngameSurfaceProvider provider ? provider.SurfaceCount : 0
			};
			if (block is IMyAssembler)
			{
				live.AssemblerMode = TimShared.AssemblerModes.Get(block);
			}
			live.Role = Resolve(live.Kind, live.Rules, live.Docked, live.NotYours);
			live.Limits = live.Rules?.Limits.ToList() ?? new List<ItemLimit>();
			TypeLimits typeLimits = live.Docked ? null : gridRules?.Type(live.Kind);
			if (typeLimits != null)
			{
				live.Limits.AddRange(typeLimits.Limits.Where(t => !live.Limits.Any(l => l.Item == t.Item)));
			}
			if (block.HasInventory)
			{
				live.Input = block.GetInventory(0) as MyInventory;
				if (block is IMyProductionBlock && block.InventoryCount > 1)
				{
					live.Output = block.GetInventory(1) as MyInventory;
				}
			}
			blocks.Add(live);
			snapshots.Add(Snap(live));
			gridIds.Add(block.CubeGrid.EntityId);
		}
		Blocks = blocks;

		Snapshot = new GridSnapshot
		{
			Key = Key,
			Name = MainGrid.CustomName,
			GridIds = gridIds.ToList(),
			CoreGridIds = Units[CoreUnit].ToList(),
			IsStation = MainGrid.IsStatic,
			X = MainGrid.WorldAABB.Center.X,
			Y = MainGrid.WorldAABB.Center.Y,
			Z = MainGrid.WorldAABB.Center.Z,
			LastSeenUtc = DateTime.UtcNow,
			BlockCount = Grids.Sum(g => ((MyCubeGrid)g).BlocksCount),
			Blocks = snapshots
		};
		Store.PutGrid(Snapshot);
	}

	private static BlockSnapshot Snap(LiveBlock live)
	{
		IMyTerminalBlock block = live.Block;
		BlockSnapshot snapshot = new BlockSnapshot
		{
			Id = block.EntityId,
			Name = block.CustomName,
			Type = block.DefinitionDisplayNameText,
			Kind = live.Kind,
			Enabled = !(block is IMyFunctionalBlock functional) || functional.Enabled,
			Functional = block.IsFunctional,
			Surfaces = live.Surfaces,
			Docked = live.Docked,
			NotYours = live.NotYours,
			Unit = live.Unit,
			AssemblerMode = live.AssemblerMode == TimShared.AssemblerMode.Unset ? null : live.AssemblerMode.ToString()
		};
		for (int i = 0; i < block.InventoryCount; i++)
		{
			if (block.GetInventory(i) is MyInventory inventory)
			{
				snapshot.Volume += (double)inventory.CurrentVolume * 1000.0;
				snapshot.MaxVolume += (double)inventory.MaxVolume * 1000.0;
			}
		}
		if (live.Input != null)
		{
			snapshot.Items = Items.Encode(Amounts(live.Input));
		}
		if (live.Output != null)
		{
			snapshot.Output = Items.Encode(Amounts(live.Output));
		}
		snapshot.Status = StatusOf(live);
		return snapshot;
	}

	public static Dictionary<string, double> Amounts(MyInventory inventory)
	{
		Dictionary<string, double> amounts = new Dictionary<string, double>();
		foreach (MyPhysicalInventoryItem item in inventory.GetItems())
		{
			string key = Items.Key(item.Content.GetId());
			amounts.TryGetValue(key, out double existing);
			amounts[key] = existing + (double)item.Amount;
		}
		return amounts;
	}

	private static string StatusOf(LiveBlock live)
	{
		IMyTerminalBlock block = live.Block;
		if (!block.IsFunctional)
		{
			return "Damaged";
		}
		if (block is IMyFunctionalBlock functional && !functional.Enabled)
		{
			return "Off";
		}
		if (!(block is MyProductionBlock production))
		{
			return block.IsWorking || !(block is IMyFunctionalBlock) ? "" : "No power";
		}
		if (!block.IsWorking)
		{
			return "No power";
		}
		StringBuilder text = new StringBuilder();
		if (block is IMyAssembler assembler && assembler.Mode == MyAssemblerMode.Disassembly)
		{
			text.Append("Disassembling: ");
		}
		int shown = 0;
		int more = 0;
		foreach (MyProductionBlock.QueueItem item in production.Queue)
		{
			if (shown < 3)
			{
				if (shown > 0)
				{
					text.Append(", ");
				}
				text.Append(BlueprintName(item.Blueprint)).Append(" x").Append(Items.Amount((double)item.Amount));
				shown++;
			}
			else
			{
				more++;
			}
		}
		if (more > 0)
		{
			text.Append($" +{more} more");
		}
		if (shown == 0)
		{
			return production.IsProducing ? "Working" : "Idle";
		}
		return (production.IsProducing ? "" : "Waiting: ") + text;
	}

	/// <summary>
	/// A short name for a queued blueprint: the item it makes when it makes one thing, else its display name on one
	/// line. Some modded blueprints have multi-line display names with a description, which spill out of a table cell.
	/// </summary>
	private static string BlueprintName(Sandbox.Definitions.MyBlueprintDefinitionBase blueprint)
	{
		if (blueprint == null)
		{
			return "?";
		}
		if (blueprint.Results != null && blueprint.Results.Length == 1)
		{
			return Items.Name(Items.Key(blueprint.Results[0].Id));
		}
		return OneLine(blueprint.DisplayNameText ?? blueprint.Id.SubtypeName);
	}

	/// <summary>The first line of a text, trimmed.</summary>
	public static string OneLine(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		int end = text.IndexOfAny(new[] { '\r', '\n' });
		return (end < 0 ? text : text.Substring(0, end)).Trim();
	}

	/// <summary>How many of the item are in the assembler queues (blueprint runs times what one run makes).</summary>
	/// <param name="unit">Whose assemblers to count: a unit (mechanical group); the grid itself when left out.</param>
	public double QueuedAmount(string key, long? unit = null)
	{
		Sandbox.Definitions.MyBlueprintDefinitionBase blueprint = Items.Blueprint(key);
		if (blueprint == null)
		{
			return 0.0;
		}
		double runs = 0.0;
		foreach (LiveBlock live in Blocks)
		{
			// Disassembly queues take items away, so they don't count toward what's coming.
			// Every assembler of the unit counts, Main or Co-op: co-op assemblers take work out of a Main one's queue.
			if (live.Unit == (unit ?? CoreUnit) && (live.Kind == BlockKind.Assembler || live.Kind == BlockKind.SurvivalKit) && live.Block is MyProductionBlock production && !production.Closed && ((IMyAssembler)live.Block).Mode == MyAssemblerMode.Assembly)
			{
				foreach (MyProductionBlock.QueueItem item in production.Queue)
				{
					if (item.Blueprint != null && item.Blueprint.Id == blueprint.Id)
					{
						runs += (double)item.Amount;
					}
				}
			}
		}
		return runs * Items.BlueprintYield(blueprint, key);
	}
}
