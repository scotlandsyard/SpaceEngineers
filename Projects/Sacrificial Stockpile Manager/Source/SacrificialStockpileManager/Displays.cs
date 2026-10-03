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
	public static readonly string[] Pages = { "Overview", "Ores", "Ingots", "Components", "Ammo", "Tools & bottles", "All items", "Containers", "Containers + docked", "Stock limits", "Quotas", "Warnings", "Log" };

	/// <summary>Seconds each page of a list shows when it doesn't fit on the screen.</summary>
	private const int PageSeconds = 6;

	/// <summary>Lines at the top of every page: grid name, page name, rule.</summary>
	private const int HeaderLines = 3;

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
			// Like everything else, only your own screens are written to unless the grid includes shared blocks.
			if (construct == null || construct.Find(block.EntityId)?.NotYours != false)
			{
				continue;
			}
			GridSnapshot grid = rule.SourceGrid != 0 ? Store.Grid(rule.SourceGrid) : OwnGrid(construct, block);
			Construct live = rule.SourceGrid != 0 ? session.FindConstruct(rule.SourceGrid) : construct;
			Measure(surface, out int columns, out int rows);
			string text = Render(rule.Page, grid, live, columns, rows);
			if (surface.GetText() != text)
			{
				surface.WriteText(text);
			}
		}
	}

	/// <summary>
	/// The grid a screen shows by default: the ship or station its block is on. A screen on a ship docked to a
	/// station is in the station's terminal system, but shows the ship.
	/// </summary>
	private static GridSnapshot OwnGrid(Construct construct, IMyTerminalBlock block)
	{
		long unit = construct.Find(block.EntityId)?.Unit ?? construct.CoreUnit;
		if (unit != construct.CoreUnit)
		{
			DockedShip ship = construct.Snapshot?.DockedShips?.FirstOrDefault(d => d.Unit == unit);
			GridSnapshot view = ship != null ? Store.Grid(ship.Key) : null;
			if (view != null)
			{
				return view;
			}
		}
		return construct.Snapshot;
	}

	/// <summary>How many characters fit across the screen and how many lines fit down it, in its font and size.</summary>
	private static void Measure(IngameSurface surface, out int columns, out int rows)
	{
		columns = 40;
		rows = 18;
		try
		{
			Vector2 size = surface.SurfaceSize;
			Vector2 glyph = surface.MeasureStringInPixels(new StringBuilder("W"), surface.Font, surface.FontSize);
			float usable = 1f - 2f * surface.TextPadding / 100f;
			if (glyph.X > 0f)
			{
				columns = Math.Max(12, Math.Min(120, (int)(size.X * usable / glyph.X)));
			}
			if (glyph.Y > 0f)
			{
				rows = Math.Max(HeaderLines + 2, Math.Min(200, (int)(size.Y * usable / glyph.Y)));
			}
		}
		catch (Exception)
		{
		}
	}

	/// <summary>
	/// The page's text for a screen <paramref name="columns"/> wide and <paramref name="rows"/> high. A page longer
	/// than the screen is shown a screenful at a time, changing every few seconds, with "1/3" after its name.
	/// </summary>
	public static string Render(string page, GridSnapshot grid, Construct live, int columns, int rows)
	{
		if (grid == null)
		{
			return "Sacrificial Stockpile Manager\n\nNo data for this grid yet.";
		}
		StringBuilder body = new StringBuilder();
		RenderBody(body, page, grid, live, columns, rows - HeaderLines);
		List<string> lines = body.ToString().TrimEnd('\n').Split('\n').ToList();
		int perPage = Math.Max(1, rows - HeaderLines);
		int pages = Math.Max(1, (lines.Count + perPage - 1) / perPage);
		int current = pages == 1 ? 0 : (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / PageSeconds % pages);
		string pageLabel = pages > 1 ? $" {current + 1}/{pages}" : "";
		StringBuilder text = new StringBuilder();
		text.Append(Fit(grid.Name, columns)).Append('\n');
		text.Append(Fit($"{page}{pageLabel}{(live == null ? " - last seen " + Ago(grid.LastSeenUtc) : "")}", columns)).Append('\n');
		text.Append(new string('-', columns)).Append('\n');
		foreach (string line in lines.Skip(current * perPage).Take(perPage))
		{
			text.Append(line).Append('\n');
		}
		return text.ToString();
	}

	private static void RenderBody(StringBuilder text, string page, GridSnapshot grid, Construct live, int columns, int rows)
	{
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
		case "Containers":
			ContainerList(text, grid, columns, rows, withDocked: false);
			break;
		case "Containers + docked":
			ContainerList(text, grid, columns, rows, withDocked: true);
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

	/// <summary>
	/// Each storage and stock block with its fill. With room, a line for the name and percentage and one for a bar;
	/// otherwise one line each (Render pages through them when there are more than fit). Only the screen's own ship
	/// or station, unless <paramref name="withDocked"/>: then ships docked to it too, marked with "&gt;".
	/// </summary>
	private static void ContainerList(StringBuilder text, GridSnapshot grid, int columns, int rows, bool withDocked)
	{
		// The view of a docked ship holds only that ship's blocks; they count as its own storage.
		bool dockedView = grid.DockedTo != null;
		List<BlockSnapshot> blocks = grid.Blocks
			.Where(b => b.MaxVolume > 0.0 && (!b.Docked || dockedView || withDocked))
			.Where(b => IsStorage(b, asOwn: dockedView || b.Docked))
			.OrderBy(b => b.Docked && !dockedView)
			.ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		double used = blocks.Sum(b => b.Volume);
		double max = blocks.Sum(b => b.MaxVolume);
		text.Append(Row(withDocked && !dockedView ? "All, docked too" : "All storage", max > 0.0 ? $"{used / max:P0}" : "-", columns)).Append('\n');
		text.Append(Bar(max > 0.0 ? used / max : 0.0, columns)).Append('\n');
		if (blocks.Count == 0)
		{
			text.Append("No storage blocks.\n");
			return;
		}
		bool twoLines = 2 + 2 * blocks.Count <= rows;
		foreach (BlockSnapshot block in blocks)
		{
			string name = (block.Docked && !dockedView ? "> " : "") + block.Name;
			if (twoLines)
			{
				text.Append(Row(name, $"{block.Fill:P0}", columns)).Append('\n');
				text.Append(Bar(block.Fill, columns)).Append('\n');
			}
			else
			{
				string value = columns >= 28 ? $"{MiniBar(block.Fill, 6)} {block.Fill,4:P0}" : $"{block.Fill:P0}";
				text.Append(Row(name, value, columns)).Append('\n');
			}
		}
	}

	/// <summary>A storage or stock block; <paramref name="asOwn"/> resolves a docked ship's block as if undocked.</summary>
	private static bool IsStorage(BlockSnapshot block, bool asOwn)
	{
		Effective role = asOwn ? Construct.Resolve(block.Kind, Store.BlockRules(block.Id), false, block.NotYours) : Construct.Resolve(block);
		return role == Effective.Storage || role == Effective.Stock;
	}

	private static string MiniBar(double fraction, int width)
	{
		int filled = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, fraction)) * width);
		return "[" + new string('|', filled) + new string('\'', width - filled) + "]";
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
			if (live != null && live.QuotaNotesFor(grid.Key).TryGetValue(quota.Item, out string note) && note != "Stocked")
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
