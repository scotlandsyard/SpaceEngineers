using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Utils;

namespace SacrificialStockpileManager;

/// <summary>
/// Keeps the player's settings and the last-seen grid snapshots in the plugin's local storage, one pair of
/// files per world. Nothing is written to blocks, so settings are private to this player.
/// </summary>
internal static class Store
{
	private const string FilePrefix = "SacrificialStockpileManager_";

	private static readonly object s_writeLock = new object();

	private static SettingsFile s_settings = new SettingsFile();

	private static readonly Dictionary<long, BlockRules> s_blockRules = new Dictionary<long, BlockRules>();

	private static readonly Dictionary<long, GridRules> s_gridRules = new Dictionary<long, GridRules>();

	private static readonly Dictionary<long, GridSnapshot> s_grids = new Dictionary<long, GridSnapshot>();

	private static readonly Dictionary<long, GridSnapshot> s_dockedViews = new Dictionary<long, GridSnapshot>();

	private static string s_world;

	private static bool s_gridsDirty;

	public static IEnumerable<GridSnapshot> Grids => s_grids.Values;

	public static List<DisplayRule> Displays => s_settings.Displays;

	/// <summary>Assemblers the plugin switched to disassembly mode. Call <see cref="SaveSettings"/> after changing it.</summary>
	public static List<long> Disassemblers => s_settings.Disassemblers;

	public static void Load()
	{
		s_world = WorldName();
		s_settings = Read<SettingsFile>(SettingsFileName()) ?? new SettingsFile();
		s_settings.Blocks ??= new List<BlockRules>();
		s_settings.Grids ??= new List<GridRules>();
		s_settings.Displays ??= new List<DisplayRule>();
		s_settings.Disassemblers ??= new List<long>();
		bool upgraded = false;
		if (s_settings.Version < 2)
		{
			// Version 2 made "clean production blocks" (DrainOutputs) on by default. Grids saved before then only
			// had it off because that was the old default, so switch it on for them too.
			foreach (GridRules rules in s_settings.Grids)
			{
				rules.DrainOutputs = true;
			}
			s_settings.Version = 2;
			upgraded = true;
		}
		s_blockRules.Clear();
		foreach (BlockRules rules in s_settings.Blocks)
		{
			rules.Accept ??= new List<string>();
			rules.Limits ??= new List<ItemLimit>();
			s_blockRules[rules.BlockId] = rules;
		}
		s_gridRules.Clear();
		foreach (GridRules rules in s_settings.Grids)
		{
			rules.Quotas ??= new List<ItemLimit>();
			rules.OrePriority ??= new List<string>();
			rules.TypeLimits ??= new List<TypeLimits>();
			foreach (TypeLimits type in rules.TypeLimits)
			{
				type.Limits ??= new List<ItemLimit>();
			}
			s_gridRules[rules.GridId] = rules;
		}
		s_grids.Clear();
		s_dockedViews.Clear();
		GridsFile grids = Read<GridsFile>(GridsFileName());
		if (grids?.Grids != null)
		{
			foreach (GridSnapshot grid in grids.Grids)
			{
				grid.GridIds ??= new List<long>();
				grid.CoreGridIds ??= new List<long>();
				grid.Blocks ??= new List<BlockSnapshot>();
				s_grids[grid.Key] = grid;
			}
		}
		s_gridsDirty = false;
		if (upgraded && s_settings.Grids.Count > 0)
		{
			SaveSettings();
		}
	}

	public static void Unload()
	{
		if (s_world != null)
		{
			SaveGrids(background: false);
		}
		s_world = null;
		s_blockRules.Clear();
		s_gridRules.Clear();
		s_grids.Clear();
		s_dockedViews.Clear();
		s_settings = new SettingsFile();
	}

	// ---- Block rules ----

	public static BlockRules BlockRules(long blockId)
	{
		return s_blockRules.TryGetValue(blockId, out BlockRules rules) ? rules : null;
	}

	/// <summary>The block's rules, created when missing. Call <see cref="SaveSettings"/> after changing them.</summary>
	public static BlockRules EditBlockRules(long blockId)
	{
		if (!s_blockRules.TryGetValue(blockId, out BlockRules rules))
		{
			rules = new BlockRules { BlockId = blockId };
			s_blockRules[blockId] = rules;
			s_settings.Blocks.Add(rules);
		}
		return rules;
	}

	// ---- Grid rules ----

	/// <summary>The rules stored against any of the grid's ids (the main grid can change when ships merge or split).</summary>
	public static GridRules GridRules(GridSnapshot grid)
	{
		// Only the grid's own grids: GridIds also lists ships that were docked to it. Snapshots saved before
		// CoreGridIds existed only match their own key until they're refreshed.
		return grid == null ? null : GridRules(grid.Key, grid.CoreGridIds ?? new List<long>());
	}

	public static GridRules GridRules(long key, IEnumerable<long> gridIds)
	{
		if (s_gridRules.TryGetValue(key, out GridRules rules))
		{
			return rules;
		}
		foreach (long id in gridIds)
		{
			if (s_gridRules.TryGetValue(id, out rules))
			{
				return rules;
			}
		}
		return null;
	}

	public static GridRules EditGridRules(GridSnapshot grid)
	{
		GridRules rules = GridRules(grid);
		if (rules == null)
		{
			rules = new GridRules { GridId = grid.Key };
			s_gridRules[grid.Key] = rules;
			s_settings.Grids.Add(rules);
		}
		return rules;
	}

	// ---- Displays ----

	public static DisplayRule Display(long blockId, int surface)
	{
		return s_settings.Displays.FirstOrDefault(d => d.BlockId == blockId && d.Surface == surface);
	}

	public static void SetDisplay(long blockId, int surface, string page, long sourceGrid)
	{
		s_settings.Displays.RemoveAll(d => d.BlockId == blockId && d.Surface == surface);
		if (page != null)
		{
			s_settings.Displays.Add(new DisplayRule { BlockId = blockId, Surface = surface, Page = page, SourceGrid = sourceGrid });
		}
		SaveSettings();
	}

	// ---- Grid snapshots ----

	/// <summary>A grid's snapshot, or the view of a ship docked to one (see <see cref="GridSnapshot.ForDockedShip"/>).</summary>
	public static GridSnapshot Grid(long key)
	{
		if (key == 0)
		{
			return null;
		}
		if (s_grids.TryGetValue(key, out GridSnapshot grid))
		{
			return grid;
		}
		foreach (GridSnapshot parent in s_grids.Values)
		{
			DockedShip ship = parent.DockedShips?.FirstOrDefault(d => d.Key == key);
			if (ship == null)
			{
				continue;
			}
			// Made once per snapshot of the grid it's docked to, so a view keeps its parsed item totals.
			if (!s_dockedViews.TryGetValue(key, out GridSnapshot view) || view.DockedTo != parent)
			{
				view = parent.ForDockedShip(ship);
				s_dockedViews[key] = view;
			}
			return view;
		}
		return null;
	}

	/// <summary>The ships docked to a grid, as views like <see cref="Grid"/> returns.</summary>
	public static List<GridSnapshot> DockedShips(GridSnapshot grid)
	{
		return grid?.DockedShips == null ? new List<GridSnapshot>() : grid.DockedShips.Select(d => Grid(d.Key)).Where(v => v != null && v.DockedTo == grid).ToList();
	}

	/// <summary>Stores a fresh snapshot and drops older ones that covered any of the same grids (docked, merged or renamed ships).</summary>
	public static void PutGrid(GridSnapshot grid)
	{
		HashSet<long> ids = new HashSet<long>(grid.GridIds);
		foreach (GridSnapshot old in s_grids.Values.Where(g => g.Key != grid.Key && g.GridIds.Any(ids.Contains)).ToList())
		{
			s_grids.Remove(old.Key);
		}
		s_grids[grid.Key] = grid;
		s_gridsDirty = true;
	}

	/// <summary>
	/// Removes a grid from the list together with its settings (grid settings, block settings and displays on its
	/// blocks), for grids that were deleted or destroyed. Returns how many block and display settings went with it.
	/// </summary>
	public static int ForgetGrid(long key)
	{
		if (!s_grids.TryGetValue(key, out GridSnapshot grid))
		{
			return 0;
		}
		s_grids.Remove(key);
		HashSet<long> blockIds = new HashSet<long>(grid.Blocks.Select(b => b.Id));
		int removed = s_settings.Blocks.RemoveAll(r => blockIds.Contains(r.BlockId));
		foreach (long id in blockIds)
		{
			s_blockRules.Remove(id);
		}
		removed += s_settings.Displays.RemoveAll(d => blockIds.Contains(d.BlockId));
		// Other grids' displays that showed this grid go back to showing their own grid.
		foreach (DisplayRule display in s_settings.Displays.Where(d => d.SourceGrid == key || grid.GridIds.Contains(d.SourceGrid)))
		{
			display.SourceGrid = 0;
		}
		HashSet<long> gridIds = new HashSet<long>(grid.GridIds) { key };
		s_settings.Grids.RemoveAll(r => gridIds.Contains(r.GridId));
		foreach (long id in gridIds)
		{
			s_gridRules.Remove(id);
		}
		SaveSettings();
		s_gridsDirty = true;
		SaveGrids(background: true);
		return removed;
	}

	// ---- Saving ----

	public static void SaveSettings()
	{
		if (s_world == null)
		{
			return;
		}
		// Drop rules that no longer say anything, so the file doesn't collect empty entries.
		s_settings.Blocks.RemoveAll(r => r.IsEmpty);
		foreach (long id in s_blockRules.Where(e => e.Value.IsEmpty).Select(e => e.Key).ToList())
		{
			s_blockRules.Remove(id);
		}
		try
		{
			// Settings are small and change from the menu, so they are serialised here and written in the background.
			string xml = MyAPIGateway.Utilities.SerializeToXML(s_settings);
			string fileName = SettingsFileName();
			long sequence = ++s_saveSequence;
			MyAPIGateway.Parallel.StartBackground(() => Write(fileName, xml, sequence));
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Could not save settings: {e}");
		}
	}

	public static void SaveGridsIfDirty()
	{
		if (s_gridsDirty)
		{
			SaveGrids(background: true);
		}
	}

	private static void SaveGrids(bool background)
	{
		if (s_world == null)
		{
			return;
		}
		s_gridsDirty = false;
		// Snapshots are never changed after they are built, so a copy of the list is safe to serialise off-thread.
		GridsFile file = new GridsFile { Grids = s_grids.Values.ToList() };
		string fileName = GridsFileName();
		long sequence = ++s_saveSequence;
		if (!background)
		{
			Write(fileName, MyAPIGateway.Utilities.SerializeToXML(file), sequence);
			return;
		}
		MyAPIGateway.Parallel.StartBackground(() =>
		{
			try
			{
				Write(fileName, MyAPIGateway.Utilities.SerializeToXML(file), sequence);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[SSM] Could not save grids: {e}");
			}
		});
	}

	private static long s_saveSequence;

	private static readonly Dictionary<string, long> s_writtenSequence = new Dictionary<string, long>();

	/// <summary>Writes a file unless a newer save of it has already been written (background saves can finish out of order).</summary>
	private static void Write(string fileName, string xml, long sequence)
	{
		lock (s_writeLock)
		{
			if (s_writtenSequence.TryGetValue(fileName, out long written) && written > sequence)
			{
				return;
			}
			s_writtenSequence[fileName] = sequence;
			try
			{
				using TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(Store));
				writer.Write(xml);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[SSM] Could not write {fileName}: {e}");
			}
		}
	}

	private static T Read<T>(string fileName) where T : class
	{
		try
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(Store)))
			{
				using TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(fileName, typeof(Store));
				return MyAPIGateway.Utilities.SerializeFromXML<T>(reader.ReadToEnd());
			}
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Could not read {fileName}: {e}");
		}
		return null;
	}

	private static string WorldName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		return new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
	}

	private static string SettingsFileName() => $"{FilePrefix}{s_world}_Settings.xml";

	private static string GridsFileName() => $"{FilePrefix}{s_world}_Grids.xml";
}
