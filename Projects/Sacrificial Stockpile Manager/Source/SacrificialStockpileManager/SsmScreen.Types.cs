using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;

namespace SacrificialStockpileManager;

// Block type limits, fill priority, and the Sort now / Unload docked ships buttons.
public partial class SsmScreen
{
	/// <summary>The block types that can have limits for the whole type, with their names in the menu.</summary>
	private static readonly KeyValuePair<BlockKind, string>[] TypeChoices =
	{
		new KeyValuePair<BlockKind, string>(BlockKind.Reactor, "Reactors"),
		new KeyValuePair<BlockKind, string>(BlockKind.GasGenerator, "O2/H2 generators"),
		new KeyValuePair<BlockKind, string>(BlockKind.Weapon, "Turrets and guns"),
		new KeyValuePair<BlockKind, string>(BlockKind.Cockpit, "Cockpits and seats"),
		new KeyValuePair<BlockKind, string>(BlockKind.Tool, "Drills, welders, grinders"),
		new KeyValuePair<BlockKind, string>(BlockKind.GasTank, "Gas tanks"),
		new KeyValuePair<BlockKind, string>(BlockKind.Refinery, "Refineries"),
		new KeyValuePair<BlockKind, string>(BlockKind.Assembler, "Assemblers")
	};

	private static string TypeName(BlockKind kind)
	{
		return TypeChoices.FirstOrDefault(t => t.Key == kind).Value ?? kind.ToString();
	}

	private void CreateTypeControls()
	{
		if (!TypeChoices.Any(t => t.Key == s_typeKind))
		{
			s_typeKind = TypeChoices[0].Key;
		}
		AddLabel(Left, Row2Y, "Type");
		_blockCombo = AddCombo(-0.36f, Row2Y, 0.43f, TypeChoices.Length);
		for (int i = 0; i < TypeChoices.Length; i++)
		{
			_blockCombo.AddItem(i, TypeChoices[i].Value, i, null, sort: false);
		}
		_blockCombo.SelectItemByKey(Array.FindIndex(TypeChoices, t => t.Key == s_typeKind), sendEvent: false);
		_blockCombo.ItemSelected += () =>
		{
			int index = (int)_blockCombo.GetSelectedKey();
			if (!_suppressEvents && index >= 0 && index < TypeChoices.Length)
			{
				s_typeKind = TypeChoices[index].Key;
				_rowsSignature = null;
				RefreshAll();
			}
		};
	}

	/// <summary>
	/// The blocks a type limit applies to: that type, on the grid itself (for a docked ship's view, the ship), and
	/// yours.
	/// </summary>
	private static List<BlockSnapshot> TypeBlocks(GridSnapshot grid, BlockKind kind)
	{
		return grid.Blocks.Where(b => b.Kind == kind && (!b.Docked || grid.DockedTo != null) && Construct.Resolve(b) != Effective.Manual).ToList();
	}

	private List<RowData> TypeRows(GridSnapshot grid)
	{
		List<RowData> rows = new List<RowData>();
		TypeLimits type = Store.GridRules(grid)?.Type(s_typeKind);
		List<BlockSnapshot> blocks = TypeBlocks(grid, s_typeKind);
		if (type == null)
		{
			return rows;
		}
		foreach (ItemLimit limit in type.Limits.OrderBy(l => Items.Name(l.Item), StringComparer.OrdinalIgnoreCase))
		{
			// Blocks that can't hold the item (a turret of another calibre) don't count.
			List<BlockSnapshot> holders = blocks.Where(b => CanHold(b, limit.Item)).ToList();
			int own = holders.Count(b => Store.BlockRules(b.Id)?.Limit(limit.Item) != null);
			int below = holders.Count(b => Store.BlockRules(b.Id)?.Limit(limit.Item) == null && limit.HasMin && Have(b, limit.Item) < limit.Min);
			int above = holders.Count(b => Store.BlockRules(b.Id)?.Limit(limit.Item) == null && limit.HasMax && Have(b, limit.Item) > limit.Max);
			string state = below > 0 ? $"{below} below minimum" : above > 0 ? $"{above} above maximum" : holders.Count > 0 ? "All within limits" : "";
			if (own > 0)
			{
				state += $"{(state.Length > 0 ? "; " : "")}{own} with their own limit";
			}
			rows.Add(new RowData
			{
				Key = "i:" + limit.Item,
				Texts = new[] { Items.Name(limit.Item), limit.HasMin ? Items.Amount(limit.Min) : "", limit.HasMax ? Items.Amount(limit.Max) : "", holders.Count.ToString(), state },
				Color = holders.Count == 0 ? MutedColor : below + above > 0 ? WarningColor : GoodColor
			});
		}
		return rows;
	}

	private static double Have(BlockSnapshot block, string key)
	{
		return block.ItemAmounts.TryGetValue(key, out double amount) ? amount : 0.0;
	}

	/// <summary>Whether the block's inventory accepts the item (checked live; a block that isn't loaded is assumed to).</summary>
	private static bool CanHold(BlockSnapshot block, string key)
	{
		IMyEntity entity = LiveEntity(block.Id);
		if (entity == null || !entity.HasInventory || !Items.TryParse(key, out MyDefinitionId id))
		{
			return true;
		}
		return !(entity.GetInventory(0) is MyInventory inventory) || inventory.CheckConstraint(id);
	}

	private void SetTypeLimit(bool minimum)
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
		if (text.Trim().Length > 0 && !Items.TryParseAmount(text, out amount))
		{
			ShowMessage("Type an amount like 50 or 2.5k, or leave it blank to clear.", WarningColor);
			return;
		}
		if (amount >= 0.0 && Items.IsIntegral(key))
		{
			amount = Math.Ceiling(amount);
		}
		GridRules rules = Store.EditGridRules(grid);
		TypeLimits type = rules.Type(s_typeKind);
		if (type == null)
		{
			if (amount < 0.0)
			{
				ShowMessage($"{TypeName(s_typeKind)} have no limit for {Items.Name(key)}.", null);
				return;
			}
			type = new TypeLimits { Kind = s_typeKind };
			rules.TypeLimits.Add(type);
		}
		ItemLimit limit = type.Limit(key);
		if (limit == null)
		{
			if (amount < 0.0)
			{
				ShowMessage($"{TypeName(s_typeKind)} have no limit for {Items.Name(key)}.", null);
				return;
			}
			limit = new ItemLimit { Item = key };
			type.Limits.Add(limit);
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
			type.Limits.Remove(limit);
		}
		if (type.Limits.Count == 0)
		{
			rules.TypeLimits.Remove(type);
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		int count = TypeBlocks(grid, s_typeKind).Count(b => CanHold(b, key));
		string which = minimum ? "Minimum" : "Maximum";
		string off = rules.Automation ? "" : " Automation is off for this grid (see Overview).";
		ShowMessage(amount < 0.0 ? $"{which} for {Items.Name(key)} in {TypeName(s_typeKind).ToLowerInvariant()} cleared." : $"{which} for {Items.Name(key)}: {Items.Amount(amount)} in each of {count} {TypeName(s_typeKind).ToLowerInvariant()}.{off}", off.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void ClearTypeLimits()
	{
		GridSnapshot grid = Grid;
		string key = SelectedItem(out string problem);
		if (grid == null || key == null)
		{
			ShowMessage(problem ?? "No grid selected.", WarningColor);
			return;
		}
		GridRules rules = Store.GridRules(grid);
		TypeLimits type = rules?.Type(s_typeKind);
		if (type == null || type.Limits.RemoveAll(l => l.Item == key) == 0)
		{
			ShowMessage($"{TypeName(s_typeKind)} have no limit for {Items.Name(key)}.", null);
			return;
		}
		if (type.Limits.Count == 0)
		{
			rules.TypeLimits.Remove(type);
		}
		Store.SaveSettings();
		Session?.RunNow(Live);
		ShowMessage($"Cleared the limits for {Items.Name(key)} in {TypeName(s_typeKind).ToLowerInvariant()}. Blocks with their own limit keep it.", null);
		_rowsSignature = null;
		RefreshAll();
	}

	private void OnPrioritySelected()
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
		int priority = (int)_priorityCombo.GetSelectedKey();
		BlockRules rules = Store.EditBlockRules(block.Id);
		rules.Priority = priority;
		Store.SaveSettings();
		Session?.RunNow(Live);
		string note = Construct.Resolve(block) != Effective.Storage ? " Priority only matters for storage blocks." : "";
		ShowMessage($"{block.Name}: fill priority {priority}. Among storage that accepts an item equally well, higher fills first.{note}", note.Length > 0 ? WarningColor : GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void StartOneShot(bool unload)
	{
		Construct live = Live;
		if (live == null)
		{
			ShowMessage("This grid isn't loaded.", WarningColor);
			return;
		}
		if (unload && !live.Blocks.Any(b => b.Docked && !b.NotYours))
		{
			ShowMessage("No ship of yours is docked to this grid.", WarningColor);
			return;
		}
		Session.StartOneShot(live, unload);
		ShowMessage(unload ? "Unloading docked ships into this grid's storage. The Log view shows each move." : "Sorting once, even with Automation off. It stops when there's nothing left to move; see the Log view.", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}
}
