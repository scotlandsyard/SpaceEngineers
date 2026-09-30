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
		return Resolve(block.Kind, Store.BlockRules(block.Id), block.Docked);
	}

	/// <summary>
	/// The role a block acts in. Auto blocks on a grid docked by connector count as machines: they keep their own
	/// limits (so a docked ship's lockers restock), but a docked ship's cargo isn't sorted into the station.
	/// </summary>
	public static Effective Resolve(BlockKind kind, BlockRules rules, bool docked)
	{
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

	public static bool IsProductionKind(BlockKind kind)
	{
		return kind == BlockKind.Assembler || kind == BlockKind.SurvivalKit || kind == BlockKind.Refinery;
	}

	/// <summary>Reads the construct's blocks and stores a fresh snapshot.</summary>
	public void Refresh()
	{
		List<IMyTerminalBlock> found = new List<IMyTerminalBlock>();
		Terminal.GetBlocksOfType(found, b => !b.Closed && b.HasLocalPlayerAccess() && (b.HasInventory || (b is IngameSurfaceProvider provider && provider.SurfaceCount > 0)));

		List<LiveBlock> blocks = new List<LiveBlock>(found.Count);
		List<BlockSnapshot> snapshots = new List<BlockSnapshot>(found.Count);
		HashSet<long> gridIds = new HashSet<long>(Grids.Select(g => g.EntityId));
		// The terminal system also spans grids docked by connector; only the main grid's mechanical group (rotors,
		// pistons, hinges) counts as this ship or station itself.
		List<IMyCubeGrid> mechanical = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(MainGrid, GridLinkTypeEnum.Mechanical, mechanical);
		HashSet<long> core = new HashSet<long>(mechanical.Select(g => g.EntityId)) { MainGrid.EntityId };
		foreach (IMyTerminalBlock block in found)
		{
			LiveBlock live = new LiveBlock
			{
				Block = block,
				Kind = KindOf(block),
				Rules = Store.BlockRules(block.EntityId),
				Docked = !core.Contains(block.CubeGrid.EntityId),
				Surfaces = block is IngameSurfaceProvider provider ? provider.SurfaceCount : 0
			};
			live.Role = Resolve(live.Kind, live.Rules, live.Docked);
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
			Docked = live.Docked
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
				text.Append(item.Blueprint?.DisplayNameText ?? "?").Append(" x").Append(Items.Amount((double)item.Amount));
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

	/// <summary>How many of the item are in the assembler queues (blueprint runs times what one run makes).</summary>
	public double QueuedAmount(string key)
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
			if ((live.Kind == BlockKind.Assembler || live.Kind == BlockKind.SurvivalKit) && live.Block is MyProductionBlock production && !production.Closed && ((IMyAssembler)live.Block).Mode == MyAssemblerMode.Assembly)
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
