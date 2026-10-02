using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRageMath;

namespace SacrificialStockpileManager;

// The rows of each view, and what the buttons do.
public partial class SsmScreen
{
	private static List<Column> ColumnsFor(View view)
	{
		switch (view)
		{
		case View.AllGrids:
			return new List<Column>
			{
				new Column { Name = "Grid", Width = 0.25f },
				new Column { Name = "Type", Width = 0.09f },
				new Column { Name = "Last sync", Width = 0.13f },
				new Column { Name = "Distance", Width = 0.1f, RightAligned = true },
				new Column { Name = "Used", Width = 0.12f, RightAligned = true },
				new Column { Name = "Size", Width = 0.12f, RightAligned = true },
				new Column { Name = "Fill", Width = 0.08f, RightAligned = true },
				new Column { Name = "Items", Width = 0.11f, RightAligned = true }
			};
		case View.Overview:
			return new List<Column>
			{
				new Column { Name = "", Width = 0.3f, Sortable = false },
				new Column { Name = "", Width = 0.7f, Sortable = false }
			};
		case View.Items:
			return new List<Column>
			{
				new Column { Name = "Item", Width = 0.24f },
				new Column { Name = "Category", Width = 0.11f },
				new Column { Name = "Amount", Width = 0.12f, RightAligned = true },
				new Column { Name = "Quota", Width = 0.1f, RightAligned = true },
				new Column { Name = "Maximum", Width = 0.11f, RightAligned = true },
				new Column { Name = "Queued", Width = 0.1f, RightAligned = true },
				new Column { Name = "Autocraft", Width = 0.22f }
			};
		case View.Types:
			return new List<Column>
			{
				new Column { Name = "Item", Width = 0.3f },
				new Column { Name = "Minimum", Width = 0.13f, RightAligned = true },
				new Column { Name = "Maximum", Width = 0.13f, RightAligned = true },
				new Column { Name = "Blocks", Width = 0.1f, RightAligned = true },
				new Column { Name = "State", Width = 0.34f }
			};
		case View.Machine:
			return new List<Column>
			{
				new Column { Name = "Item", Width = 0.3f, Sortable = false },
				new Column { Name = "Amount", Width = 0.12f, RightAligned = true, Sortable = false },
				new Column { Name = "Needed", Width = 0.12f, RightAligned = true, Sortable = false },
				new Column { Name = "On grid", Width = 0.12f, RightAligned = true, Sortable = false },
				new Column { Name = "Missing", Width = 0.12f, RightAligned = true, Sortable = false },
				new Column { Name = "Note", Width = 0.22f, Sortable = false }
			};
		case View.Refining:
			return new List<Column>
			{
				new Column { Name = "Priority", Width = 0.1f, RightAligned = true, Sortable = false },
				new Column { Name = "Ore", Width = 0.3f, Sortable = false },
				new Column { Name = "In storage", Width = 0.15f, RightAligned = true, Sortable = false },
				new Column { Name = "In refineries", Width = 0.15f, RightAligned = true, Sortable = false },
				new Column { Name = "Being refined in", Width = 0.3f, Sortable = false }
			};
		case View.Blocks:
			return new List<Column>
			{
				new Column { Name = "Block", Width = 0.24f },
				new Column { Name = "Type", Width = 0.15f },
				new Column { Name = "Role", Width = 0.12f },
				new Column { Name = "Fill", Width = 0.07f, RightAligned = true },
				new Column { Name = "Used / size", Width = 0.2f, RightAligned = true },
				new Column { Name = "Settings", Width = 0.22f }
			};
		case View.Block:
			return new List<Column>
			{
				new Column { Name = "Item", Width = 0.3f },
				new Column { Name = "Here", Width = 0.15f, RightAligned = true },
				new Column { Name = "Minimum", Width = 0.13f, RightAligned = true },
				new Column { Name = "Maximum", Width = 0.13f, RightAligned = true },
				new Column { Name = "State", Width = 0.29f }
			};
		case View.Production:
			return new List<Column>
			{
				new Column { Name = "Block", Width = 0.25f },
				new Column { Name = "Type", Width = 0.14f },
				new Column { Name = "Mode", Width = 0.12f },
				new Column { Name = "State", Width = 0.09f },
				new Column { Name = "Queue / contents", Width = 0.4f }
			};
		case View.Displays:
			return new List<Column>
			{
				new Column { Name = "Block", Width = 0.3f },
				new Column { Name = "Screen", Width = 0.2f },
				new Column { Name = "Page", Width = 0.18f },
				new Column { Name = "Showing", Width = 0.32f }
			};
		default:
			return new List<Column>
			{
				new Column { Name = "", Width = 1f, Sortable = false }
			};
		}
	}

	private static string EmptyText(View view)
	{
		switch (view)
		{
		case View.Items:
			return "No items on this grid.";
		case View.Blocks:
			return "No blocks with an inventory that you can access.";
		case View.Block:
			return "Empty, and no limits set. Pick an item below to add one.";
		case View.Types:
			return "No limits for this type yet. Pick an item below, type an amount and set a minimum or maximum.";
		case View.Production:
			return "No production blocks.";
		case View.Machine:
			return "No assembler or refinery on this grid.";
		case View.Displays:
			return "No blocks with screens that you can access.";
		case View.Log:
			return Live == null ? "The log is only kept while the grid is loaded." : "Nothing done yet.";
		default:
			return "Nothing to show.";
		}
	}

	private List<RowData> BuildRows(GridSnapshot grid)
	{
		switch (s_view)
		{
		case View.Overview:
			return OverviewRows(grid);
		case View.Items:
			return ItemRows(grid);
		case View.Blocks:
			return BlockRows(grid);
		case View.Block:
			return BlockItemRows(grid);
		case View.Production:
			return ProductionRows(grid);
		case View.Machine:
			return MachineRows(grid);
		case View.Types:
			return TypeRows(grid);
		case View.Refining:
			return RefiningRows(grid);
		case View.Displays:
			return DisplayRows(grid);
		case View.Log:
			return (Live?.Log ?? new List<string>()).Select((line, i) => Row("log" + i, null, line)).ToList();
		default:
			return new List<RowData> { Row("help", null, "") };
		}
	}

	// ---- Overview ----

	private List<RowData> OverviewRows(GridSnapshot grid)
	{
		GridRules rules = Store.GridRules(grid);
		Construct live = Live;
		List<RowData> rows = new List<RowData>();
		if (grid.DockedTo != null)
		{
			// While docked, the ship's blocks are part of the grid it's docked to and items move by that grid's settings.
			rows.Add(Row("docked", WarningColor, "Docked to", $"{grid.DockedTo.Name}. Its settings move items; this ship's quotas and autocraft still run"));
		}
		bool automation = rules != null && rules.Automation;
		rows.Add(Row("set:automation", automation ? GoodColor : WarningColor, "Automation", automation ? "On: limits, sorting and draining are applied" : "Off: nothing is moved (settings are kept)"));
		rows.Add(Row("set:autocraft", rules != null && rules.Autocraft ? GoodColor : (Color?)null, "Autocraft", rules != null && rules.Autocraft ? AutoCraft.YieldsToBaRMaid ? "On: queued in batches when assemblers are free (BaR Maid first)" : "On: quotas are queued in assemblers" : "Off"));
		bool clean = rules?.DrainOutputs ?? new GridRules().DrainOutputs;
		rows.Add(Row("set:drain", clean ? GoodColor : (Color?)null, "Clean production blocks", clean ? "On: output emptied, assembler inputs keep only what their queue needs" : "Off"));
		rows.Add(Row("set:kits", rules != null && rules.UseSurvivalKits ? GoodColor : (Color?)null, "Survival kits autocraft", rules != null && rules.UseSurvivalKits ? "On" : "Off"));
		rows.Add(Row("set:disassemble", rules != null && rules.Disassemble ? GoodColor : (Color?)null, "Disassemble surplus", rules != null && rules.Disassemble ? "On: items above their maximum are disassembled" : "Off"));
		rows.Add(Row("set:bottles", rules != null && rules.FillBottles ? GoodColor : (Color?)null, "Keep bottles filled", rules != null && rules.FillBottles ? "On: bottles go to a tank to refill (needs Automation)" : "Off"));
		bool shared = rules != null && rules.IncludeShared;
		int notYours = grid.Blocks.Count(b => b.NotYours);
		rows.Add(Row("set:shared", shared ? WarningColor : (Color?)null, "Blocks shared with me", shared ? "Included: faction-shared, shared-with-all and unowned blocks are managed too" : notYours > 0 ? $"Left alone: only your own blocks are touched ({notYours} aren't yours)" : "Left alone: only your own blocks are touched"));

		Vector3D player = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		string distance = FormatDistance(Vector3D.Distance(player, grid.Position));
		rows.Add(Row("status", live != null ? GoodColor : MutedColor, "Status", live != null ? $"Loaded ({distance} away), updating live" : $"Last seen {Displays.Ago(grid.LastSeenUtc)}, {distance} away"));
		rows.Add(Row("type", null, "Type", $"{(grid.IsStation ? "Station" : "Ship")}, {grid.GridIds.Count} grid(s), {grid.BlockCount:N0} blocks"));

		Construct.StorageVolume(grid, out double used, out double max);
		rows.Add(Row("cargo", max > 0.0 && used / max > 0.9 ? WarningColor : (Color?)null, "Storage", max > 0.0 ? $"{used / max:P0} full: {Items.Litres(used)} used of {Items.Litres(max)}, {Items.Litres(max - used)} free" : "No storage blocks"));

		Dictionary<Effective, int> roles = new Dictionary<Effective, int>();
		foreach (BlockSnapshot block in grid.Blocks.Where(b => b.HasInventory))
		{
			Effective role = Construct.Resolve(block);
			roles.TryGetValue(role, out int count);
			roles[role] = count + 1;
		}
		rows.Add(Row("roles", null, "Inventories", string.Join(", ", roles.OrderBy(r => r.Key).Select(r => $"{r.Value} {r.Key.ToString().ToLowerInvariant()}"))));

		foreach (ItemCategory category in Items.Categories)
		{
			List<KeyValuePair<string, double>> items = grid.Totals.Where(t => Items.Category(t.Key) == category).ToList();
			if (items.Count > 0)
			{
				rows.Add(Row("cat:" + category, null, Items.CategoryNames[(int)category], $"{items.Count} kind(s), {Items.Amount(items.Sum(i => i.Value))} total"));
			}
		}

		int blockRules = grid.Blocks.Count(b => Store.BlockRules(b.Id) != null);
		int displays = Store.Displays.Count(d => grid.Blocks.Any(b => b.Id == d.BlockId));
		rows.Add(Row("rules", null, "Settings", $"{blockRules} block(s) set up, {rules?.Quotas.Count ?? 0} quota(s), {displays} display(s)"));

		if (live != null)
		{
			rows.Add(Row("warnings", live.Warnings.Count > 0 ? WarningColor : GoodColor, "Warnings", live.Warnings.Count == 0 ? "None" : $"{live.Warnings.Count}"));
			for (int i = 0; i < live.Warnings.Count; i++)
			{
				rows.Add(Row("w" + i, WarningColor, "", live.Warnings[i]));
			}
			for (int i = 0; i < Math.Min(5, live.Log.Count); i++)
			{
				rows.Add(Row("log" + i, MutedColor, i == 0 ? "Recent" : "", live.Log[i]));
			}
		}
		return rows;
	}

	private void ChangeSelectedSetting()
	{
		string key = SelectedKey;
		if (key == null || !key.StartsWith("set:", StringComparison.Ordinal))
		{
			ShowMessage("Select one of the settings at the top of the list first.", WarningColor);
			return;
		}
		ToggleGridSetting(key.Substring(4));
	}

	private void ToggleGridSetting(string setting)
	{
		GridSnapshot grid = Grid;
		if (grid == null)
		{
			return;
		}
		GridRules rules = Store.EditGridRules(grid);
		string text;
		bool on;
		switch (setting)
		{
		case "automation":
			on = rules.Automation = !rules.Automation;
			text = "Automation";
			break;
		case "autocraft":
			on = rules.Autocraft = !rules.Autocraft;
			text = "Autocraft";
			break;
		case "drain":
			on = rules.DrainOutputs = !rules.DrainOutputs;
			text = "Cleaning production blocks";
			break;
		case "kits":
			on = rules.UseSurvivalKits = !rules.UseSurvivalKits;
			text = "Autocraft in survival kits";
			break;
		case "disassemble":
			on = rules.Disassemble = !rules.Disassemble;
			text = "Disassembly above maximums";
			break;
		case "bottles":
			on = rules.FillBottles = !rules.FillBottles;
			text = "Keeping bottles filled";
			break;
		case "shared":
			on = rules.IncludeShared = !rules.IncludeShared;
			text = "Managing blocks shared with you";
			break;
		default:
			return;
		}
		Store.SaveSettings();
		Live?.AddLog($"{text} switched {(on ? "on" : "off")}");
		Session?.RunNow(Live);
		ShowMessage($"{text} {(on ? "on" : "off")} for {grid.Name}.", on ? GoodColor : (Color?)null);
		_rowsSignature = null;
		RefreshAll();
	}

	/// <summary>The grid a grid button acts on: the selected row in All grids, else the grid picked at the top.</summary>
	private GridSnapshot TargetGrid()
	{
		if (s_view != View.AllGrids)
		{
			return Grid;
		}
		string key = SelectedKey;
		return key != null && key.StartsWith("g:", StringComparison.Ordinal) && long.TryParse(key.Substring(2), out long gridKey) ? Store.Grid(gridKey) : null;
	}

	private void MarkGps()
	{
		GridSnapshot grid = TargetGrid();
		if (grid == null)
		{
			ShowMessage("Select a grid first.", WarningColor);
			return;
		}
		bool updated = GridGps.Mark(grid);
		ShowMessage(updated ? $"Moved the GPS marker for {grid.Name} to where it was last seen." : $"Added a GPS marker for {grid.Name}.", GoodColor);
	}

	private void ForgetGrid()
	{
		GridSnapshot grid = TargetGrid();
		if (grid == null)
		{
			ShowMessage("Select a grid first.", WarningColor);
			return;
		}
		if (grid.DockedTo != null)
		{
			ShowMessage($"{grid.Name} is docked to {grid.DockedTo.Name}, so it's listed with it. Remove {grid.DockedTo.Name} instead.", WarningColor);
			return;
		}
		if (Session?.FindConstruct(grid.Key) != null)
		{
			ShowMessage($"{grid.Name} is loaded right now, so it would come straight back.", WarningColor);
			return;
		}
		int settings = Store.ForgetGrid(grid.Key);
		ShowMessage($"Removed {grid.Name}{(settings > 0 ? $" and its settings ({settings} block(s) and displays)" : "")}. If it still exists, it comes back the next time it's in range.", null);
		_gridsSignature = null;
		_rowsSignature = null;
		if (grid.Key == s_gridKey || s_view != View.AllGrids)
		{
			_recreatePending = true;
		}
	}

	// ---- All grids ----

	private List<RowData> AllGridRows()
	{
		Vector3D player = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		List<RowData> rows = new List<RowData>();
		foreach (GridSnapshot grid in SortedGrids())
		{
			bool live = Session?.FindConstruct(grid.Key) != null;
			Construct.StorageVolume(grid, out double used, out double max);
			double distance = Vector3D.Distance(player, grid.Position);
			double fill = max > 0.0 ? used / max : 0.0;
			rows.Add(new RowData
			{
				Key = "g:" + grid.Key,
				Texts = new[] { grid.Name, grid.IsStation ? "Station" : "Ship", live ? "Live" : Displays.Ago(grid.LastSeenUtc), FormatDistance(distance), max > 0.0 ? Items.Litres(used) : "-", max > 0.0 ? Items.Litres(max) : "-", max > 0.0 ? $"{fill:P0}" : "", grid.Totals.Count.ToString() },
				// Live grids sort as the newest.
				SortValues = new object[] { null, null, live ? double.MaxValue : (double)grid.LastSeenUtc.Ticks, distance, used, max, fill, (double)grid.Totals.Count },
				Color = grid.Key == s_gridKey ? GoodColor : live ? (Color?)null : MutedColor
			});
		}
		return rows;
	}

	private static string AllGridsHint()
	{
		List<GridSnapshot> grids = Store.Grids.ToList();
		if (grids.Count == 0)
		{
			return "No grids yet. Your ships and stations show up here once they've been in range.";
		}
		double used = 0.0;
		double max = 0.0;
		foreach (GridSnapshot grid in grids)
		{
			Construct.StorageVolume(grid, out double gridUsed, out double gridMax);
			used += gridUsed;
			max += gridMax;
		}
		int live = grids.Count(g => Session?.FindConstruct(g.Key) != null);
		return $"{grids.Count} grid(s), {live} loaded. Storage: {Items.Litres(used)} used of {Items.Litres(max)}, {Items.Litres(max - used)} free. Double-click a grid to open it.";
	}

	private void OpenSelectedGrid()
	{
		GridSnapshot grid = TargetGrid();
		if (grid == null)
		{
			ShowMessage("Select a grid first.", WarningColor);
			return;
		}
		s_gridKey = grid.Key;
		if (Session != null)
		{
			Session.ViewedKey = s_gridKey;
		}
		_gridsSignature = null;
		SwitchView(View.Overview);
	}

	// ---- Items & quotas ----

	private List<RowData> ItemRows(GridSnapshot grid)
	{
		GridRules rules = Store.GridRules(grid);
		Construct live = Live;
		IEnumerable<string> keys = grid.Totals.Keys;
		if (rules != null)
		{
			keys = keys.Union(rules.Quotas.Select(q => q.Item));
		}
		List<RowData> rows = new List<RowData>();
		foreach (string key in keys.OrderBy(k => (int)Items.Category(k)).ThenBy(k => Items.Name(k), StringComparer.OrdinalIgnoreCase))
		{
			grid.Totals.TryGetValue(key, out double amount);
			ItemLimit quota = rules?.Quota(key);
			double queued = live?.QueuedAmount(key, live.UnitFor(grid.Key)) ?? 0.0;
			string note = live != null && live.QuotaNotesFor(grid.Key).TryGetValue(key, out string text) ? text : "";
			Color? color = null;
			if (quota != null && ((quota.HasMin && amount < quota.Min) || (quota.HasMax && amount > quota.Max)))
			{
				color = WarningColor;
			}
			else if (quota != null)
			{
				color = GoodColor;
			}
			rows.Add(new RowData
			{
				Key = "i:" + key,
				Texts = new[] { Items.Name(key), Items.CategoryNames[(int)Items.Category(key)], Items.Amount(amount), quota != null && quota.HasMin ? Items.Amount(quota.Min) : "", quota != null && quota.HasMax ? Items.Amount(quota.Max) : "", queued > 0.0 ? Items.Amount(queued) : "", note },
				SortValues = new object[] { null, ((int)Items.Category(key)).ToString("D2") + Items.Name(key), amount, quota?.Min ?? -1.0, quota?.Max ?? -1.0, queued, null },
				Color = color
			});
		}
		return rows;
	}

	private string SelectedItem(out string problem)
	{
		problem = null;
		if (string.IsNullOrEmpty(s_itemKey))
		{
			problem = "Pick an item first: select a row, or choose one in the list below.";
			return null;
		}
		return s_itemKey;
	}

	/// <summary>Sets the quota (minimum, autocrafted) or the maximum (disassembled above). A blank amount clears that one.</summary>
	private void SetQuota(bool minimum)
	{
		GridSnapshot grid = Grid;
		string key = SelectedItem(out string problem);
		if (grid == null || key == null)
		{
			ShowMessage(problem ?? "No grid selected.", WarningColor);
			return;
		}
		string text = _amountBox?.Text ?? "";
		double amount = -1.0;
		if (text.Trim().Length > 0 && (!Items.TryParseAmount(text, out amount) || (minimum && amount <= 0.0)))
		{
			ShowMessage("Type an amount like 500 or 2.5k, or leave it blank to clear.", WarningColor);
			return;
		}
		if (amount >= 0.0 && Items.IsIntegral(key))
		{
			amount = minimum ? Math.Ceiling(amount) : Math.Floor(amount);
		}
		GridRules rules = Store.EditGridRules(grid);
		ItemLimit quota = rules.Quota(key);
		if (quota == null)
		{
			if (amount < 0.0)
			{
				ShowMessage($"{Items.Name(key)} has no {(minimum ? "quota" : "maximum")} to clear.", null);
				return;
			}
			quota = new ItemLimit { Item = key };
			rules.Quotas.Add(quota);
		}
		if (minimum)
		{
			quota.Min = amount;
		}
		else
		{
			quota.Max = amount;
		}
		if (quota.HasMin && quota.HasMax && quota.Min > quota.Max)
		{
			// Keep them consistent, or autocraft and disassembly would fight: the one just typed wins.
			if (minimum)
			{
				quota.Max = quota.Min;
			}
			else
			{
				quota.Min = quota.Max;
			}
		}
		if (!quota.HasMin && !quota.HasMax)
		{
			rules.Quotas.Remove(quota);
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		string name = Items.Name(key);
		if (amount < 0.0)
		{
			ShowMessage($"{(minimum ? "Quota" : "Maximum")} for {name} cleared.", null);
		}
		else if (Items.Blueprint(key) == null)
		{
			ShowMessage($"No blueprint makes {name}, so it can't be {(minimum ? "assembled" : "disassembled")}. The setting is kept for the LCD pages.", WarningColor);
		}
		else if (minimum)
		{
			ShowMessage($"Quota for {name}: {Items.Amount(amount)}.{(rules.Autocraft ? "" : " Autocraft is off; switch it on to queue it.")}", rules.Autocraft ? GoodColor : WarningColor);
		}
		else
		{
			ShowMessage($"Maximum for {name}: {Items.Amount(amount)}.{(rules.Disassemble ? " Anything above it is disassembled." : " Disassembly is off; switch it on in the Overview.")}", rules.Disassemble ? GoodColor : WarningColor);
		}
		_rowsSignature = null;
		RefreshAll();
	}

	private void ClearQuota()
	{
		GridSnapshot grid = Grid;
		string key = SelectedItem(out string problem);
		if (grid == null || key == null)
		{
			ShowMessage(problem ?? "No grid selected.", WarningColor);
			return;
		}
		GridRules rules = Store.GridRules(grid);
		if (rules == null || rules.Quotas.RemoveAll(q => q.Item == key) == 0)
		{
			ShowMessage($"{Items.Name(key)} has no quota.", null);
			return;
		}
		Store.SaveSettings();
		ShowMessage($"Cleared the quota and maximum for {Items.Name(key)}. Anything already queued stays queued.", null);
		_rowsSignature = null;
		RefreshAll();
	}

	// ---- Blocks ----

	private List<RowData> BlockRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		foreach (BlockSnapshot block in grid.Blocks.Where(b => b.HasInventory))
		{
			BlockRules rules = Store.BlockRules(block.Id);
			Effective role = Construct.Resolve(block);
			rows.Add(new RowData
			{
				Key = "b:" + block.Id,
				Texts = new[] { block.Name, block.Type, (block.NotYours ? "not yours" : RoleText(rules, role)) + (block.Docked ? " (docked)" : ""), block.MaxVolume > 0.0 ? $"{block.Fill:P0}" : "", block.MaxVolume > 0.0 ? $"{Items.Litres(block.Volume)} / {Items.Litres(block.MaxVolume)}" : "", SettingsText(rules) },
				SortValues = new object[] { null, null, null, block.Fill, block.MaxVolume, null },
				Color = role == Effective.Manual ? MutedColor : rules != null && !rules.IsEmpty ? GoodColor : (Color?)null
			});
		}
		return rows.OrderBy(r => r.Texts[0], StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string RoleText(BlockRules rules, Effective role)
	{
		return rules == null || rules.Role == BlockRole.Auto ? role.ToString().ToLowerInvariant() : role.ToString();
	}

	private static string SettingsText(BlockRules rules)
	{
		if (rules == null)
		{
			return "";
		}
		List<string> parts = new List<string>();
		if (rules.Accept.Count > 0)
		{
			parts.Add("accepts " + string.Join(", ", rules.Accept.Select(AcceptName)));
		}
		if (rules.Limits.Count > 0)
		{
			parts.Add($"{rules.Limits.Count} limit(s)");
		}
		if (rules.Priority > 0)
		{
			parts.Add($"priority {rules.Priority}");
		}
		return string.Join("; ", parts);
	}

	private static string AcceptName(string token)
	{
		return Enum.TryParse(token, out ItemCategory category) ? Items.CategoryShort[(int)category] : Items.Name(token);
	}

	private long SelectedBlockId()
	{
		string key = SelectedKey;
		return key != null && key.StartsWith("b:", StringComparison.Ordinal) && long.TryParse(key.Substring(2), out long id) ? id : 0;
	}

	private void EditSelectedBlock()
	{
		long id = SelectedBlockId();
		if (id == 0)
		{
			ShowMessage("Select a block first.", WarningColor);
			return;
		}
		s_blockId = id;
		SwitchView(View.Block);
	}

	private void ToggleSelectedBlock()
	{
		long id = SelectedBlockId();
		if (id == 0)
		{
			ShowMessage("Select a block first.", WarningColor);
			return;
		}
		if (!(LiveEntity(id) is IMyFunctionalBlock block) || !block.HasLocalPlayerAccess())
		{
			ShowMessage("That block isn't loaded or can't be switched.", WarningColor);
			return;
		}
		block.Enabled = !block.Enabled;
		ShowMessage($"{block.CustomName} switched {(block.Enabled ? "on" : "off")}.", block.Enabled ? GoodColor : (Color?)null);
		Session?.RunNow(Live);
		_rowsSignature = null;
		RefreshAll();
	}

	private void Rescan()
	{
		Session?.RefreshNow();
		ShowMessage($"Rescanned: {Session?.Constructs.Count ?? 0} grid(s) of yours loaded.");
		_rowsSignature = null;
		RefreshAll();
	}

	// ---- Block settings ----

	private List<RowData> BlockItemRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		BlockSnapshot block = grid.Block(s_blockId);
		if (block == null)
		{
			return rows;
		}
		BlockRules rules = Store.BlockRules(block.Id);
		Effective role = Construct.Resolve(block);
		// Limits set for the block's whole type apply where the block has none of its own (not on docked ships).
		TypeLimits typeLimits = block.Docked ? null : Store.GridRules(grid)?.Type(block.Kind);
		IEnumerable<string> keys = block.ItemAmounts.Keys.Union(block.OutputAmounts.Keys);
		if (rules != null)
		{
			keys = keys.Union(rules.Limits.Select(l => l.Item)).Union(rules.Accept.Where(a => a.Contains("/")));
		}
		if (typeLimits != null)
		{
			keys = keys.Union(typeLimits.Limits.Select(l => l.Item).Where(k => CanHold(block, k)));
		}
		foreach (string key in keys.OrderBy(k => (int)Items.Category(k)).ThenBy(k => Items.Name(k), StringComparer.OrdinalIgnoreCase))
		{
			block.ItemAmounts.TryGetValue(key, out double have);
			block.OutputAmounts.TryGetValue(key, out double output);
			ItemLimit limit = rules?.Limit(key);
			string from = "";
			if (limit == null && typeLimits?.Limit(key) is ItemLimit typeLimit)
			{
				limit = typeLimit;
				from = $" (all {TypeName(block.Kind).ToLowerInvariant()})";
			}
			string state = "";
			Color? color = null;
			if (block.NotYours)
			{
				state = "Not yours: left alone";
				color = MutedColor;
			}
			else if (role == Effective.Manual)
			{
				state = "Manual: left alone";
				color = MutedColor;
			}
			else if (limit != null && limit.HasMin && have < limit.Min)
			{
				state = "Below minimum";
				color = WarningColor;
			}
			else if (limit != null && limit.HasMax && have > limit.Max)
			{
				state = "Above maximum";
				color = WarningColor;
			}
			else if (limit != null)
			{
				state = "Within limits";
				color = GoodColor;
			}
			else if (rules != null && rules.Accept.Contains(key))
			{
				state = "Accepted here";
				color = GoodColor;
			}
			else if (role == Effective.Stock && have > 0.0)
			{
				state = "No limit: moved out";
			}
			else if (role == Effective.Storage && rules != null && rules.Accept.Count > 0 && have > 0.0 && !rules.Accept.Contains(Items.Category(key).ToString()))
			{
				state = "Doesn't belong: sorted out";
			}
			else if (output > 0.0)
			{
				state = "In output";
			}
			state += from;
			string here = Items.Amount(have) + (output > 0.0 ? $" (+{Items.Amount(output)} out)" : "");
			rows.Add(new RowData
			{
				Key = "i:" + key,
				Texts = new[] { Items.Name(key), here, limit != null && limit.HasMin ? Items.Amount(limit.Min) : "", limit != null && limit.HasMax ? Items.Amount(limit.Max) : "", state },
				SortValues = new object[] { null, have + output, limit?.Min ?? -1.0, limit?.Max ?? -1.0, null },
				Color = color
			});
		}
		return rows;
	}

	private BlockSnapshot CurrentBlock()
	{
		return Grid?.Block(s_blockId);
	}

	private void OnRoleSelected()
	{
		if (_suppressEvents)
		{
			return;
		}
		BlockSnapshot block = CurrentBlock();
		if (block == null)
		{
			return;
		}
		BlockRole role = (BlockRole)_roleCombo.GetSelectedKey();
		Store.EditBlockRules(block.Id).Role = role;
		Store.SaveSettings();
		// For an assembler, the Manual role also sets the mode shared with our other plugins to Manual, so nothing
		// queues on it either. Leaving Manual doesn't opt it back in: that's the assembler mode's job.
		string shared = "";
		if (LiveEntity(block.Id) is IMyAssembler assembler)
		{
			if (role == BlockRole.Manual && !TimShared.AssemblerModes.IsManual(assembler))
			{
				TimShared.AssemblerModes.Set(assembler, TimShared.AssemblerMode.Manual);
				shared = " Its assembler mode is Manual too, so none of our plugins queues on it.";
			}
			else if (role != BlockRole.Manual && TimShared.AssemblerModes.IsManual(assembler))
			{
				shared = " Its assembler mode is still Manual: set Main or Co-op in Production details to let autocraft use it.";
			}
		}
		else if (block.Kind == BlockKind.Assembler && role == BlockRole.Manual)
		{
			shared = " The grid isn't loaded, so the assembler mode shared with our other plugins wasn't changed.";
		}
		Session?.RunNow(Live);
		block = CurrentBlock() ?? block;
		ShowMessage($"{block.Name} is now {Construct.Resolve(block).ToString().ToLowerInvariant()}. {RoleHelp(Construct.Resolve(block))}{shared}", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private static string RoleHelp(Effective role)
	{
		switch (role)
		{
		case Effective.Storage:
			return "Items are stored here and taken from here.";
		case Effective.Intake:
			return "Emptied into storage.";
		case Effective.Stock:
			return "Keeps only its limited items, between min and max.";
		case Effective.Manual:
			return "The plugin never touches it.";
		default:
			return "Only its own limits apply.";
		}
	}

	private void OnAcceptChanged(ItemCategory category, bool accept)
	{
		if (_suppressEvents)
		{
			return;
		}
		BlockSnapshot block = CurrentBlock();
		if (block == null)
		{
			return;
		}
		BlockRules rules = Store.EditBlockRules(block.Id);
		rules.Accept.Remove(category.ToString());
		if (accept)
		{
			rules.Accept.Add(category.ToString());
		}
		Store.SaveSettings();
		string warning = accept && Construct.Resolve(block) != Effective.Storage ? " Only storage blocks receive sorted items; set Role to Storage." : "";
		ShowMessage($"{block.Name} {(accept ? "accepts" : "no longer accepts")} {Items.CategoryNames[(int)category].ToLowerInvariant()}.{warning}", warning.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ToggleAcceptItem()
	{
		BlockSnapshot block = CurrentBlock();
		string key = SelectedItem(out string problem);
		if (block == null || key == null)
		{
			ShowMessage(problem ?? "Pick a block first.", WarningColor);
			return;
		}
		BlockRules rules = Store.EditBlockRules(block.Id);
		bool accept = !rules.Accept.Contains(key);
		rules.Accept.Remove(key);
		if (accept)
		{
			rules.Accept.Add(key);
		}
		Store.SaveSettings();
		string warning = accept && Construct.Resolve(block) != Effective.Storage ? " Only storage blocks receive sorted items; set Role to Storage." : "";
		ShowMessage($"{block.Name} {(accept ? "accepts" : "no longer accepts")} {Items.Name(key)}.{warning}", warning.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void SetLimit(bool minimum)
	{
		BlockSnapshot block = CurrentBlock();
		string key = SelectedItem(out string problem);
		if (block == null || key == null)
		{
			ShowMessage(problem ?? "Pick a block first.", WarningColor);
			return;
		}
		string text = _amountBox?.Text ?? "";
		double amount = -1.0;
		if (text.Trim().Length > 0 && !Items.TryParseAmount(text, out amount))
		{
			ShowMessage("Type an amount like 500 or 2.5k, or leave it blank to clear.", WarningColor);
			return;
		}
		if (amount >= 0.0 && Items.IsIntegral(key))
		{
			amount = Math.Ceiling(amount);
		}
		BlockRules rules = Store.EditBlockRules(block.Id);
		ItemLimit limit = rules.Limit(key);
		if (limit == null)
		{
			if (amount < 0.0)
			{
				ShowMessage($"{Items.Name(key)} has no limit to clear.", null);
				return;
			}
			limit = new ItemLimit { Item = key };
			rules.Limits.Add(limit);
		}
		if (minimum)
		{
			limit.Min = amount;
		}
		else
		{
			limit.Max = amount;
		}
		if (limit.HasMin && limit.HasMax && limit.Min > limit.Max)
		{
			// Keep them consistent: the one just typed wins.
			if (minimum)
			{
				limit.Max = limit.Min;
			}
			else
			{
				limit.Min = limit.Max;
			}
		}
		if (!limit.HasMin && !limit.HasMax)
		{
			rules.Limits.Remove(limit);
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		string which = minimum ? "Minimum" : "Maximum";
		string role = Construct.Resolve(block) == Effective.Manual ? " The block is Manual, so limits are ignored." : "";
		GridRules gridRules = Store.GridRules(Grid);
		string off = gridRules == null || !gridRules.Automation ? " Automation is off for this grid (see Overview)." : "";
		ShowMessage(amount < 0.0 ? $"{which} for {Items.Name(key)} cleared." : $"{which} for {Items.Name(key)} in {block.Name}: {Items.Amount(amount)}.{role}{off}", role.Length + off.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ClearLimits()
	{
		BlockSnapshot block = CurrentBlock();
		string key = SelectedItem(out string problem);
		if (block == null || key == null)
		{
			ShowMessage(problem ?? "Pick a block first.", WarningColor);
			return;
		}
		BlockRules rules = Store.BlockRules(block.Id);
		if (rules == null || rules.Limits.RemoveAll(l => l.Item == key) == 0)
		{
			ShowMessage($"{Items.Name(key)} has no limits here.", null);
			return;
		}
		Store.SaveSettings();
		ShowMessage($"Cleared the limits for {Items.Name(key)} in {block.Name}.", null);
		_rowsSignature = null;
		RefreshAll();
	}

	// ---- Production ----

	private List<RowData> ProductionRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		foreach (BlockSnapshot block in grid.Blocks)
		{
			if (!Construct.IsProductionKind(block.Kind) && block.Kind != BlockKind.Reactor && block.Kind != BlockKind.GasGenerator)
			{
				continue;
			}
			string state = !block.Functional ? "Damaged" : !block.Enabled ? "Off" : block.Status == "No power" ? "No power" : "On";
			string detail = Construct.IsProductionKind(block.Kind) && block.Status != "No power" && block.Status != "Off" && block.Status != "Damaged" ? block.Status : "";
			if (block.Kind == BlockKind.Refinery || block.Kind == BlockKind.Reactor || block.Kind == BlockKind.GasGenerator)
			{
				string contents = string.Join(", ", block.ItemAmounts.OrderByDescending(i => i.Value).Take(3).Select(i => $"{Items.Name(i.Key)} {Items.Amount(i.Value)}"));
				detail = block.Kind == BlockKind.Refinery && detail.Length > 0 ? $"{detail}; in: {contents}" : contents.Length > 0 ? contents : detail.Length > 0 ? detail : "Empty";
			}
			string mode = "";
			if (block.Kind == BlockKind.Assembler || block.Kind == BlockKind.SurvivalKit)
			{
				mode = Enum.TryParse(block.AssemblerMode, out TimShared.AssemblerMode parsed) ? TimShared.AssemblerModes.Describe(parsed) : TimShared.AssemblerModes.Describe(TimShared.AssemblerMode.Unset);
			}
			rows.Add(new RowData
			{
				Key = "b:" + block.Id,
				Texts = new[] { block.Name, block.Type, mode, state, detail },
				Color = mode.StartsWith("Manual") ? MutedColor : state == "On" ? (Color?)null : WarningColor
			});
		}
		return rows.OrderBy(r => r.Texts[1], StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Texts[0], StringComparer.OrdinalIgnoreCase).ToList();
	}

	// ---- Displays ----

	private List<RowData> DisplayRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		foreach (BlockSnapshot block in grid.Blocks.Where(b => b.Surfaces > 0).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
		{
			for (int i = 0; i < block.Surfaces; i++)
			{
				DisplayRule rule = Store.Display(block.Id, i);
				string screen = Displays.Surface(block.Id, i)?.DisplayName;
				if (string.IsNullOrEmpty(screen))
				{
					screen = block.Surfaces == 1 ? "Screen" : $"Screen {i + 1}";
				}
				string showing = rule == null ? "" : rule.SourceGrid == 0 ? "This grid" : Store.Grid(rule.SourceGrid)?.Name ?? "(forgotten grid)";
				rows.Add(new RowData
				{
					Key = $"d:{block.Id}:{i}",
					Texts = new[] { block.Name, screen, rule?.Page ?? "", showing },
					Color = rule != null ? GoodColor : (Color?)null
				});
			}
		}
		return rows;
	}

	private void AssignDisplay()
	{
		string key = SelectedKey;
		if (key == null || !TryParseDisplayKey(key, out long blockId, out int surface))
		{
			ShowMessage("Select a screen first.", WarningColor);
			return;
		}
		string page = Displays.Pages[Math.Max(0, Math.Min(s_page, Displays.Pages.Length - 1))];
		bool isNew = Store.Display(blockId, surface) == null;
		Store.SetDisplay(blockId, surface, page, s_source);
		if (isNew)
		{
			// Switch the screen to text mode with a monospace font once; later changes by the player stick.
			Displays.Prepare(blockId, surface);
		}
		bool loaded = Displays.Surface(blockId, surface) != null;
		ShowMessage($"Showing {page} on {Grid?.Block(blockId)?.Name ?? "the screen"}.{(loaded ? "" : " It starts when the block is loaded.")}", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ClearDisplay()
	{
		string key = SelectedKey;
		if (key == null || !TryParseDisplayKey(key, out long blockId, out int surface))
		{
			ShowMessage("Select a screen first.", WarningColor);
			return;
		}
		if (Store.Display(blockId, surface) == null)
		{
			ShowMessage("That screen isn't showing a page.", null);
			return;
		}
		Store.SetDisplay(blockId, surface, null, 0);
		ShowMessage("The screen keeps its last text; the plugin no longer updates it.", null);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ClearLog()
	{
		Live?.Log.Clear();
		_rowsSignature = null;
		RefreshAll();
	}

	private static string FormatDistance(double metres)
	{
		return metres >= 1000.0 ? $"{metres / 1000.0:0.0} km" : $"{metres:0} m";
	}
}
