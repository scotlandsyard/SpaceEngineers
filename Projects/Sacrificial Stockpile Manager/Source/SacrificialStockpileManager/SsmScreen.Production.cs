using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace SacrificialStockpileManager;

// Production details (one assembler or refinery) and the refinery ore priority list.
public partial class SsmScreen
{
	// ---- Production details ----

	private void ShowSelectedMachine()
	{
		long id = SelectedBlockId();
		BlockSnapshot block = id == 0 ? null : Grid?.Block(id);
		if (block == null)
		{
			ShowMessage("Select a block first.", WarningColor);
			return;
		}
		if (!Construct.IsProductionKind(block.Kind))
		{
			// Reactors and generators have no queue; their limits are what can be set.
			EditSelectedBlock();
			return;
		}
		s_machineId = id;
		SwitchView(View.Machine);
	}

	private List<RowData> MachineRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		BlockSnapshot block = grid.Block(s_machineId);
		if (block == null)
		{
			return rows;
		}
		if (Live == null || !(LiveEntity(block.Id) is MyProductionBlock production))
		{
			rows.Add(Row("info", MutedColor, "Last known state", "", "", "", "", string.IsNullOrEmpty(block.Status) ? "-" : block.Status));
			return rows;
		}
		Dictionary<string, double> onGrid = Construct.StationTotals(Live.Snapshot ?? grid);
		if (production is MyAssembler assembler)
		{
			AssemblerRows(rows, assembler, onGrid);
		}
		else
		{
			RefineryRows(rows, production, grid);
		}
		return rows;
	}

	private void AssemblerRows(List<RowData> rows, MyAssembler assembler, Dictionary<string, double> onGrid)
	{
		bool disassembling = ((IMyAssembler)assembler).Mode == MyAssemblerMode.Disassembly;
		string state = AssemblerState(assembler);
		string mode = TimShared.AssemblerModes.Describe(TimShared.AssemblerModes.Get(assembler));
		rows.Add(Row("info", state == "Working" ? GoodColor : state == "Idle" ? (Color?)null : WarningColor, (disassembling ? "Disassembling" : "Assembling") + $" ({mode}{(assembler.IsSlave ? ", cooperating" : "")})", "", "", "", "", state));

		List<MyProductionBlock.QueueItem> queue = assembler.Queue.ToList();
		if (queue.Count == 0)
		{
			rows.Add(Row("empty", MutedColor, "Queue is empty"));
			return;
		}
		// What the whole queue needs, and what just the item being made now needs (for one run).
		Dictionary<string, double> needed = new Dictionary<string, double>();
		Dictionary<string, double> needNow = new Dictionary<string, double>();
		for (int i = 0; i < queue.Count; i++)
		{
			MyBlueprintDefinitionBase blueprint = queue[i].Blueprint;
			if (blueprint == null)
			{
				continue;
			}
			double runs = (double)queue[i].Amount;
			string name = BlueprintLabel(blueprint, out double perRun);
			string note = i == 0 ? (assembler.IsProducing ? $"Making now, {assembler.CurrentProgress:P0}" : "Next") : "";
			rows.Add(new RowData { Key = "q:" + i, Texts = new[] { name, Items.Amount(runs * perRun), "", "", "", note }, Color = i == 0 && assembler.IsProducing ? GoodColor : (Color?)null });

			if (disassembling)
			{
				// Disassembly takes the finished items themselves.
				foreach (MyBlueprintDefinitionBase.Item result in blueprint.Results)
				{
					Add(needed, Items.Key(result.Id), (double)result.Amount * runs);
					if (i == 0)
					{
						Add(needNow, Items.Key(result.Id), (double)result.Amount);
					}
				}
				continue;
			}
			// Assemblers use less material when the world's assembler efficiency is raised.
			double efficiency = Math.Max(0.0001, assembler.GetEfficiencyMultiplierForBlueprint(blueprint));
			foreach (MyBlueprintDefinitionBase.Item prerequisite in blueprint.Prerequisites)
			{
				double perUnit = (double)prerequisite.Amount / efficiency;
				Add(needed, Items.Key(prerequisite.Id), perUnit * runs);
				if (i == 0)
				{
					Add(needNow, Items.Key(prerequisite.Id), perUnit);
				}
			}
		}

		rows.Add(Row("hdr", MutedColor, disassembling ? "Items to disassemble" : "Materials for the whole queue"));
		foreach (KeyValuePair<string, double> need in needed.OrderByDescending(n => Missing(n, onGrid) > 0.0).ThenBy(n => Items.Name(n.Key), StringComparer.OrdinalIgnoreCase))
		{
			onGrid.TryGetValue(need.Key, out double have);
			double missing = Missing(need, onGrid);
			string note;
			Color? color;
			if (missing <= 0.0)
			{
				note = "Enough";
				color = null;
			}
			else if (needNow.TryGetValue(need.Key, out double now) && have < now)
			{
				note = "Blocks the current item";
				color = new Color(255, 120, 100);
			}
			else
			{
				note = "Runs out partway";
				color = WarningColor;
			}
			rows.Add(new RowData
			{
				Key = "m:" + need.Key,
				Texts = new[] { Items.Name(need.Key), "", Items.Amount(need.Value), Items.Amount(have), missing > 0.0 ? Items.Amount(missing) : "", note },
				Color = color
			});
		}
	}

	private static double Missing(KeyValuePair<string, double> need, Dictionary<string, double> onGrid)
	{
		onGrid.TryGetValue(need.Key, out double have);
		return Math.Max(0.0, need.Value - have);
	}

	private static void Add(Dictionary<string, double> amounts, string key, double amount)
	{
		amounts.TryGetValue(key, out double existing);
		amounts[key] = existing + amount;
	}

	/// <summary>The item a blueprint makes (and how many per run), or its one-line name when it makes several.</summary>
	private static string BlueprintLabel(MyBlueprintDefinitionBase blueprint, out double perRun)
	{
		perRun = 1.0;
		if (blueprint.Results != null && blueprint.Results.Length == 1)
		{
			string key = Items.Key(blueprint.Results[0].Id);
			perRun = Items.BlueprintYield(blueprint, key);
			return Items.Name(key);
		}
		return Construct.OneLine(blueprint.DisplayNameText ?? blueprint.Id.SubtypeName);
	}

	private static string AssemblerState(MyAssembler assembler)
	{
		switch (assembler.CurrentState)
		{
		case MyAssembler.StateEnum.Disabled:
			return "Off";
		case MyAssembler.StateEnum.NotWorking:
			return "Damaged";
		case MyAssembler.StateEnum.NotEnoughPower:
			return "No power";
		case MyAssembler.StateEnum.MissingItems:
			return ((IMyAssembler)assembler).Mode == MyAssemblerMode.Disassembly ? "Waiting for items" : "Missing materials";
		case MyAssembler.StateEnum.InventoryFull:
			return "Inventory full";
		default:
			return assembler.IsProducing ? "Working" : "Idle";
		}
	}

	private void RefineryRows(List<RowData> rows, MyProductionBlock refinery, GridSnapshot grid)
	{
		string state = !refinery.IsFunctional ? "Damaged" : !refinery.Enabled ? "Off" : !refinery.IsWorking ? "No power" : refinery.IsProducing ? "Working" : "Idle";
		rows.Add(Row("info", state == "Working" ? GoodColor : state == "Idle" ? (Color?)null : WarningColor, "Refining", "", "", "", "", state));
		List<string> priority = Store.GridRules(grid)?.OrePriority ?? new List<string>();
		rows.Add(Row("hdr", MutedColor, "Input, refined top to bottom"));
		int index = 0;
		foreach (MyPhysicalInventoryItem item in refinery.InputInventory.GetItems())
		{
			string key = Items.Key(item.Content.GetId());
			int rank = priority.IndexOf(key);
			string note = index == 0 && refinery.IsProducing ? "Refining now" : rank >= 0 ? $"Priority {rank + 1}" : "";
			rows.Add(new RowData { Key = "in:" + index, Texts = new[] { Items.Name(key), Items.Amount((double)item.Amount), "", "", "", note }, Color = index == 0 && refinery.IsProducing ? GoodColor : (Color?)null });
			index++;
		}
		if (index == 0)
		{
			rows.Add(Row("in:none", MutedColor, "No ore"));
		}
		rows.Add(Row("hdr2", MutedColor, "Output"));
		index = 0;
		foreach (MyPhysicalInventoryItem item in refinery.OutputInventory.GetItems())
		{
			rows.Add(Row("out:" + index++, null, Items.Name(Items.Key(item.Content.GetId())), Items.Amount((double)item.Amount)));
		}
		if (index == 0)
		{
			rows.Add(Row("out:none", MutedColor, "Empty"));
		}
	}

	private void RemoveSelectedQueueItem()
	{
		string key = SelectedKey;
		if (key == null || !key.StartsWith("q:", StringComparison.Ordinal) || !int.TryParse(key.Substring(2), out int index))
		{
			ShowMessage("Select an item in the queue first.", WarningColor);
			return;
		}
		if (!(LiveEntity(s_machineId) is MyProductionBlock production) || !((IMyTerminalBlock)production).HasLocalPlayerAccess())
		{
			ShowMessage("That block isn't loaded.", WarningColor);
			return;
		}
		List<MyProductionBlock.QueueItem> queue = production.Queue.ToList();
		if (index >= queue.Count)
		{
			return;
		}
		production.RemoveQueueItemRequest(index, queue[index].Amount);
		ShowMessage($"Removed {BlueprintLabel(queue[index].Blueprint, out _)} from the queue. In multiplayer the server confirms it in a moment.", null);
		Live?.AddLog($"Removed {BlueprintLabel(queue[index].Blueprint, out _)} from {production.CustomName}'s queue (from the menu)");
		_rowsSignature = null;
	}

	/// <summary>
	/// Steps the selected assembler's mode, shared with our other plugins: Main (takes the orders), then Manual (no
	/// plugin touches it), then Co-op (helps a Main assembler; the default), then Main again.
	/// </summary>
	private void CycleAssemblerMode()
	{
		long id = SelectedBlockId();
		if (id == 0 || !(LiveEntity(id) is IMyAssembler assembler) || !assembler.HasLocalPlayerAccess())
		{
			ShowMessage("Select an assembler first (the grid has to be loaded).", WarningColor);
			return;
		}
		if (Grid?.Block(id)?.NotYours == true)
		{
			ShowMessage($"{assembler.CustomName} isn't yours, so its mode is left alone.", WarningColor);
			return;
		}
		TimShared.AssemblerMode next;
		switch (TimShared.AssemblerModes.Get(assembler))
		{
		case TimShared.AssemblerMode.Main:
			next = TimShared.AssemblerMode.Manual;
			break;
		case TimShared.AssemblerMode.Manual:
			next = TimShared.AssemblerMode.Coop;
			break;
		default:
			next = TimShared.AssemblerMode.Main;
			break;
		}
		TimShared.AssemblerModes.Set(assembler, next);
		// Keep the plugin's own role in step, so Manual only has to be set in one place.
		BlockRules rules = Store.BlockRules(id);
		if (next != TimShared.AssemblerMode.Manual && rules != null && rules.Role == BlockRole.Manual)
		{
			rules.Role = BlockRole.Auto;
			Store.SaveSettings();
		}
		Session?.RunNow(Live);
		string text;
		switch (next)
		{
		case TimShared.AssemblerMode.Main:
			text = "Main: takes the orders; Co-op assemblers help with them.";
			break;
		case TimShared.AssemblerMode.Manual:
			text = "Manual: none of our plugins queues on it or changes it.";
			break;
		default:
			text = TimShared.AssemblerModes.SupportsCoop(assembler) ? "Co-op: helps a Main assembler with its queue." : "Co-op, but this assembler can't cooperate, so it takes orders like a Main one.";
			break;
		}
		ShowMessage($"{assembler.CustomName} is now {text}", next == TimShared.AssemblerMode.Manual ? (Color?)null : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ToggleMachine()
	{
		if (!(LiveEntity(s_machineId) is IMyFunctionalBlock block) || !block.HasLocalPlayerAccess())
		{
			ShowMessage("That block isn't loaded.", WarningColor);
			return;
		}
		block.Enabled = !block.Enabled;
		ShowMessage($"{block.CustomName} switched {(block.Enabled ? "on" : "off")}.", block.Enabled ? GoodColor : (Color?)null);
		_rowsSignature = null;
	}

	// ---- Refinery priority ----

	private List<RowData> RefiningRows(GridSnapshot grid)
	{
		List<string> priority = Store.GridRules(grid)?.OrePriority ?? new List<string>();
		Dictionary<string, double> inStorage = new Dictionary<string, double>();
		Dictionary<string, double> inRefineries = new Dictionary<string, double>();
		Dictionary<string, List<string>> refining = new Dictionary<string, List<string>>();
		foreach (BlockSnapshot block in grid.Blocks.Where(b => !b.Docked))
		{
			bool isRefinery = block.Kind == BlockKind.Refinery;
			foreach (KeyValuePair<string, double> item in block.ItemAmounts)
			{
				if (Items.Category(item.Key) == ItemCategory.Ore)
				{
					Add(isRefinery ? inRefineries : inStorage, item.Key, item.Value);
				}
			}
			if (isRefinery)
			{
				// The ore a refinery works on is the first stack in its input.
				string first = FirstOre(block);
				if (first != null)
				{
					if (!refining.TryGetValue(first, out List<string> names))
					{
						names = new List<string>();
						refining[first] = names;
					}
					names.Add(block.Name);
				}
			}
		}
		IEnumerable<string> ores = Items.Catalog.Where(k => Items.Category(k) == ItemCategory.Ore)
			.Union(inStorage.Keys).Union(inRefineries.Keys).Union(priority)
			.Where(k => Items.IsRefinable(k) || priority.Contains(k));
		List<RowData> rows = new List<RowData>();
		foreach (string key in ores.OrderBy(k => priority.Contains(k) ? priority.IndexOf(k) : int.MaxValue).ThenBy(k => Items.Name(k), StringComparer.OrdinalIgnoreCase))
		{
			int rank = priority.IndexOf(key);
			inStorage.TryGetValue(key, out double stored);
			inRefineries.TryGetValue(key, out double refiningAmount);
			string where = refining.TryGetValue(key, out List<string> names) ? string.Join(", ", names) : "";
			rows.Add(new RowData
			{
				Key = "o:" + key,
				Texts = new[] { rank >= 0 ? (rank + 1).ToString() : "-", Items.Name(key), stored > 0.0 ? Items.Amount(stored) : "", refiningAmount > 0.0 ? Items.Amount(refiningAmount) : "", where },
				Color = rank >= 0 ? GoodColor : stored > 0.0 || refiningAmount > 0.0 ? (Color?)null : MutedColor
			});
		}
		return rows;
	}

	/// <summary>The first ore in a refinery's input: live when loaded, else the order stored in the snapshot.</summary>
	private static string FirstOre(BlockSnapshot block)
	{
		if (LiveEntity(block.Id) is MyProductionBlock refinery)
		{
			MyPhysicalInventoryItem? first = refinery.InputInventory.GetItems().Cast<MyPhysicalInventoryItem?>().FirstOrDefault();
			return first.HasValue ? Items.Key(first.Value.Content.GetId()) : null;
		}
		return block.ItemAmounts.Keys.FirstOrDefault();
	}

	private string SelectedOre()
	{
		string key = SelectedKey;
		return key != null && key.StartsWith("o:", StringComparison.Ordinal) ? key.Substring(2) : null;
	}

	/// <summary>Moves the selected ore up (-1) or down (+1) the priority list. Raising an ore with no priority adds it at the bottom.</summary>
	private void MoveOre(int direction)
	{
		GridSnapshot grid = Grid;
		string ore = SelectedOre();
		if (grid == null || ore == null)
		{
			ShowMessage("Select an ore first.", WarningColor);
			return;
		}
		GridRules rules = Store.EditGridRules(grid);
		List<string> priority = rules.OrePriority;
		int index = priority.IndexOf(ore);
		if (index < 0)
		{
			if (direction > 0)
			{
				ShowMessage($"{Items.Name(ore)} has no priority. Raise priority adds it.", null);
				return;
			}
			priority.Add(ore);
		}
		else
		{
			int target = Math.Max(0, Math.Min(priority.Count - 1, index + direction));
			if (target == index)
			{
				ShowMessage(direction < 0 ? $"{Items.Name(ore)} is already first." : $"{Items.Name(ore)} is already last. Use No priority to take it off the list.", null);
				return;
			}
			priority.RemoveAt(index);
			priority.Insert(target, ore);
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		string off = rules.Automation ? "" : " Automation is off for this grid, so refineries aren't changed yet.";
		ShowMessage($"{Items.Name(ore)} is priority {priority.IndexOf(ore) + 1}.{off}", off.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void RemoveOrePriority()
	{
		GridSnapshot grid = Grid;
		string ore = SelectedOre();
		if (grid == null || ore == null)
		{
			ShowMessage("Select an ore first.", WarningColor);
			return;
		}
		GridRules rules = Store.GridRules(grid);
		if (rules == null || !rules.OrePriority.Remove(ore))
		{
			ShowMessage($"{Items.Name(ore)} has no priority.", null);
			return;
		}
		Store.SaveSettings();
		ShowMessage($"{Items.Name(ore)} no longer has a priority. Refineries take it in the order it arrives.", null);
		_rowsSignature = null;
		RefreshAll();
	}
}
