using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRageMath;
using IngameSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace SacrificialStockpileManager;

/// <summary>
/// Writes the plugin's pages to LCD surfaces. Text written on a client is sent to the server when it changes,
/// so other players see it too; it is only written when it differs from what's there.
/// </summary>
internal static class Displays
{
	public static readonly string[] Pages = { "Overview", "Ores", "Ingots", "Components", "Ammo", "Tools & bottles", "All items", "Stock limits", "Quotas", "Warnings", "Log" };

	private const string DefaultFont = "Monospace";

	private const float DefaultFontSize = 0.6f;

	private const int DisplaysPerCall = 4;

	private static int s_cursor;

	/// <summary>Sets a surface up for the pages (text mode, monospace font). Only called when a page is assigned, so later changes by the player stick.</summary>
	public static void Prepare(long blockId, int surfaceIndex)
	{
		IngameSurface surface = Surface(blockId, surfaceIndex);
		if (surface == null)
		{
			return;
		}
		surface.ContentType = ContentType.TEXT_AND_IMAGE;
		surface.Font = DefaultFont;
		surface.FontSize = DefaultFontSize;
		surface.Alignment = TextAlignment.LEFT;
	}

	public static IngameSurface Surface(long blockId, int surfaceIndex)
	{
		IMyEntity entity = MyAPIGateway.Entities.GetEntityById(blockId);
		if (entity == null || entity.Closed || !(entity is IngameSurfaceProvider provider) || surfaceIndex < 0 || surfaceIndex >= provider.SurfaceCount)
		{
			return null;
		}
		return provider.GetSurface(surfaceIndex);
	}

	/// <summary>Updates a few displays per call, going round all of them.</summary>
	public static void Update(SsmSession session)
	{
		List<DisplayRule> rules = Store.Displays;
		if (rules.Count == 0)
		{
			return;
		}
		int count = Math.Min(DisplaysPerCall, rules.Count);
		for (int i = 0; i < count; i++)
		{
			s_cursor = (s_cursor + 1) % rules.Count;
			DisplayRule rule = rules[s_cursor];
			IngameSurface surface = Surface(rule.BlockId, rule.Surface);
			if (surface == null)
			{
				continue;
			}
			IMyEntity entity = MyAPIGateway.Entities.GetEntityById(rule.BlockId);
			if (!(entity is IMyTerminalBlock block) || !block.HasLocalPlayerAccess())
			{
				continue;
			}
			Construct construct = session.ConstructOf(block);
			GridSnapshot grid = rule.SourceGrid != 0 ? Store.Grid(rule.SourceGrid) : construct?.Snapshot;
			Construct live = rule.SourceGrid != 0 ? session.FindConstruct(rule.SourceGrid) : construct;
			string text = Render(rule.Page, grid, live, Columns(surface));
			if (surface.GetText() != text)
			{
				surface.WriteText(text);
			}
		}
	}

	private static int Columns(IngameSurface surface)
	{
		try
		{
			Vector2 size = surface.SurfaceSize;
			Vector2 glyph = surface.MeasureStringInPixels(new StringBuilder("W"), surface.Font, surface.FontSize);
			if (glyph.X > 0f)
			{
				int columns = (int)(size.X * (1f - 2f * surface.TextPadding / 100f) / glyph.X);
				return Math.Max(16, Math.Min(120, columns));
			}
		}
		catch (Exception)
		{
		}
		return 40;
	}

	public static string Render(string page, GridSnapshot grid, Construct live, int columns)
	{
		StringBuilder text = new StringBuilder();
		if (grid == null)
		{
			return "Sacrificial Stockpile Manager\n\nNo data for this grid yet.";
		}
		text.Append(Fit(grid.Name, columns)).Append('\n');
		text.Append(Fit($"{page}{(live == null ? " - last seen " + Ago(grid.LastSeenUtc) : "")}", columns)).Append('\n');
		text.Append(new string('-', columns)).Append('\n');
		GridRules rules = Store.GridRules(grid);
		switch (page)
		{
		case "Ores":
			ItemList(text, grid, rules, columns, k => Items.Category(k) == ItemCategory.Ore);
			break;
		case "Ingots":
			ItemList(text, grid, rules, columns, k => Items.Category(k) == ItemCategory.Ingot);
			break;
		case "Components":
			ItemList(text, grid, rules, columns, k => Items.Category(k) == ItemCategory.Component);
			break;
		case "Ammo":
			ItemList(text, grid, rules, columns, k => Items.Category(k) == ItemCategory.Ammo);
			break;
		case "Tools & bottles":
			ItemList(text, grid, rules, columns, k => Items.Category(k) == ItemCategory.Tool || Items.Category(k) == ItemCategory.Bottle);
			break;
		case "All items":
			foreach (ItemCategory category in Items.Categories)
			{
				if (grid.Totals.Keys.Any(k => Items.Category(k) == category))
				{
					text.Append(Items.CategoryNames[(int)category]).Append('\n');
					ItemList(text, grid, rules, columns, k => Items.Category(k) == category);
				}
			}
			break;
		case "Stock limits":
			StockList(text, grid, columns);
			break;
		case "Quotas":
			QuotaList(text, grid, rules, live, columns);
			break;
		case "Warnings":
			Lines(text, live?.Warnings, columns, live == null ? "Not loaded: warnings show while the grid is in range." : "No warnings.");
			break;
		case "Log":
			Lines(text, live?.Log, columns, live == null ? "Not loaded." : "Nothing done yet.");
			break;
		default:
			Overview(text, grid, rules, live, columns);
			break;
		}
		return text.ToString();
	}

	private static void Overview(StringBuilder text, GridSnapshot grid, GridRules rules, Construct live, int columns)
	{
		Construct.StorageVolume(grid, out double used, out double max);
		text.Append(Row("Storage", max > 0.0 ? $"{used / max:P0}" : "-", columns)).Append('\n');
		text.Append(Bar(max > 0.0 ? used / max : 0.0, columns)).Append('\n');
		if (max > 0.0)
		{
			text.Append(Row("Used / size", $"{Items.Litres(used)} / {Items.Litres(max)}", columns)).Append('\n');
		}
		foreach (ItemCategory category in Items.Categories)
		{
			List<KeyValuePair<string, double>> items = grid.Totals.Where(t => Items.Category(t.Key) == category).ToList();
			if (items.Count > 0)
			{
				text.Append(Row(Items.CategoryNames[(int)category], Items.Amount(items.Sum(i => i.Value)), columns)).Append('\n');
			}
		}
		text.Append('\n');
		text.Append(Row("Automation", rules != null && rules.Automation ? "On" : "Off", columns)).Append('\n');
		text.Append(Row("Autocraft", rules != null && rules.Autocraft ? "On" : "Off", columns)).Append('\n');
		if (live != null)
		{
			text.Append(Row("Warnings", live.Warnings.Count.ToString(), columns)).Append('\n');
			foreach (string warning in live.Warnings.Take(5))
			{
				text.Append(Fit("! " + warning, columns)).Append('\n');
			}
		}
	}

	private static void ItemList(StringBuilder text, GridSnapshot grid, GridRules rules, int columns, Func<string, bool> filter)
	{
		IEnumerable<string> keys = grid.Totals.Keys.Where(filter);
		if (rules != null)
		{
			keys = keys.Union(rules.Quotas.Where(q => q.HasMin).Select(q => q.Item).Where(filter));
		}
		List<string> sorted = keys.OrderBy(k => Items.Name(k), StringComparer.OrdinalIgnoreCase).ToList();
		if (sorted.Count == 0)
		{
			text.Append("  (none)\n");
			return;
		}
		foreach (string key in sorted)
		{
			grid.Totals.TryGetValue(key, out double amount);
			ItemLimit quota = rules?.Quota(key);
			string value = quota != null && quota.HasMin ? $"{Items.Amount(amount)}/{Items.Amount(quota.Min)}" : Items.Amount(amount);
			text.Append(Row(Items.Name(key), value, columns)).Append('\n');
		}
	}

	private static void StockList(StringBuilder text, GridSnapshot grid, int columns)
	{
		bool any = false;
		foreach (BlockSnapshot block in grid.Blocks.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
		{
			BlockRules rules = Store.BlockRules(block.Id);
			if (rules == null || rules.Limits.Count == 0)
			{
				continue;
			}
			any = true;
			text.Append(Fit(block.Name, columns)).Append('\n');
			foreach (ItemLimit limit in rules.Limits)
			{
				block.ItemAmounts.TryGetValue(limit.Item, out double have);
				string range = (limit.HasMin ? Items.Amount(limit.Min) : "0") + "-" + (limit.HasMax ? Items.Amount(limit.Max) : "any");
				bool low = limit.HasMin && have < limit.Min;
				bool high = limit.HasMax && have > limit.Max;
				text.Append(Row((low ? " ! " : high ? " ^ " : "   ") + Items.Name(limit.Item), $"{Items.Amount(have)} ({range})", columns)).Append('\n');
			}
		}
		if (!any)
		{
			text.Append("No blocks have stock limits.\n");
		}
	}

	private static void QuotaList(StringBuilder text, GridSnapshot grid, GridRules rules, Construct live, int columns)
	{
		if (rules == null || rules.Quotas.Count == 0)
		{
			text.Append("No quotas set.\n");
			return;
		}
		foreach (ItemLimit quota in rules.Quotas.Where(q => q.HasMin).OrderBy(q => Items.Name(q.Item), StringComparer.OrdinalIgnoreCase))
		{
			grid.Totals.TryGetValue(quota.Item, out double have);
			text.Append(Row(Items.Name(quota.Item), $"{Items.Amount(have)}/{Items.Amount(quota.Min)}", columns)).Append('\n');
			if (live != null && live.QuotaNotes.TryGetValue(quota.Item, out string note) && note != "Stocked")
			{
				text.Append(Fit("   " + note, columns)).Append('\n');
			}
		}
	}

	private static void Lines(StringBuilder text, List<string> lines, int columns, string empty)
	{
		if (lines == null || lines.Count == 0)
		{
			text.Append(empty).Append('\n');
			return;
		}
		foreach (string line in lines.Take(40))
		{
			text.Append(Fit(line, columns)).Append('\n');
		}
	}

	/// <summary>"Name ...... value", exactly <paramref name="columns"/> wide in a monospace font.</summary>
	private static string Row(string name, string value, int columns)
	{
		// Room for the name with at least " . " between it and the value.
		int room = columns - value.Length - 3;
		if (room < 3)
		{
			return Fit(name + " " + value, columns);
		}
		if (name.Length > room)
		{
			name = name.Substring(0, room - 1) + "~";
		}
		return name + " " + new string('.', columns - value.Length - name.Length - 2) + " " + value;
	}

	private static string Fit(string text, int columns)
	{
		return text.Length <= columns ? text : text.Substring(0, columns - 1) + "~";
	}

	private static string Bar(double fraction, int columns)
	{
		int width = Math.Max(4, columns - 2);
		int filled = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, fraction)) * width);
		return "[" + new string('|', filled) + new string('\'', width - filled) + "]";
	}

	public static string Ago(DateTime utc)
	{
		TimeSpan span = DateTime.UtcNow - utc;
		if (span.TotalMinutes < 1.0)
		{
			return "just now";
		}
		if (span.TotalHours < 1.0)
		{
			return $"{(int)span.TotalMinutes} min ago";
		}
		if (span.TotalDays < 1.0)
		{
			return $"{(int)span.TotalHours} h ago";
		}
		return $"{(int)span.TotalDays} d ago";
	}
}
