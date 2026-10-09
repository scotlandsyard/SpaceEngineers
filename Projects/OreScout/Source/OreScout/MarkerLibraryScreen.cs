using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using TimShared;

namespace OreScout;

/// <summary>
/// In-game window listing the saved markers. A filter narrows the list to one ore or marker type, two
/// distance columns measure from the player and from a chosen marker, and markers can be ticked to
/// import several at once. Export moves OreScout ore markers out of the GPS list into the library.
/// </summary>
public class MarkerLibraryScreen : MyGuiScreenBase
{
	private const int ColumnMark = 0;

	private const int ColumnType = 1;

	private const int ColumnName = 2;

	private const int ColumnAmount = 3;

	private const int ColumnInfo = 4;

	private const int ColumnFromYou = 5;

	private const int ColumnFromReference = 6;

	private const string AllFilter = "All markers";

	private class Filter
	{
		public string Name;

		public Func<LibraryEntry, bool> Matches;
	}

	private MyGuiControlTable _table;

	private MyGuiControlCombobox _filterBox;

	private MyGuiControlLabel _status;

	private Vector3D _playerPosition;

	private string _statusText = "";

	private bool _sortedOnce;

	private List<Filter> _filters = new List<Filter>();

	// Remembered by name so it survives the option list being rebuilt after an export or delete.
	private string _filterName = AllFilter;

	/// <summary>The marker the "From marker" column measures from, or null to leave that column empty.</summary>
	private LibraryEntry _reference;

	private readonly HashSet<LibraryEntry> _marked = new HashSet<LibraryEntry>();

	/// <summary>Everything that belongs to the library view; hidden while the Help page shows.</summary>
	private readonly List<MyGuiControlBase> _libraryControls = new List<MyGuiControlBase>();

	/// <summary>The Help page's text and its Back button; hidden until Help is pressed.</summary>
	private readonly List<MyGuiControlBase> _helpControls = new List<MyGuiControlBase>();

	private MyGuiControlMultilineText _helpText;

	private bool _showHelp;

	public MarkerLibraryScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.86f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "OreScoutMarkerLibrary";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		// The plugin switcher sits in the top-left corner and the shared chat setting in the top-right corner, both level
		// with the caption. The filter row under them keeps clear of both: the switcher's list drops down over the left of
		// it, and the Show dropdown sits just below the chat setting (y -0.34 puts its top edge on the chat setting's bottom).
		MyGuiControlLabel caption = AddCaption("OreScout Marker Library");
		PluginSwitcher.AddSwitcher(this, caption);
		Personality.AddChatSetting(this, caption);

		_libraryControls.Clear();
		_helpControls.Clear();
		_showHelp = false;

		MyGuiControlLabel showLabel = new MyGuiControlLabel(new Vector2(0.16f, -0.34f), null, "Show:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		Controls.Add(showLabel);
		_libraryControls.Add(showLabel);
		_filterBox = new MyGuiControlCombobox(new Vector2(0.42f, -0.34f), new Vector2(0.25f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, openAreaItemsCount: 12);
		_filterBox.ItemSelected += OnFilterSelected;
		Controls.Add(_filterBox);
		_libraryControls.Add(_filterBox);

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.315f),
			Size = new Vector2(0.84f, 0.55f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 7,
			VisibleRowsCount = 14
		};
		_table.SetCustomColumnWidths(new float[] { 0.05f, 0.13f, 0.2f, 0.17f, 0.15f, 0.14f, 0.16f });
		_table.SetColumnName(ColumnMark, new StringBuilder("X"));
		_table.SetColumnName(ColumnType, new StringBuilder("Type"));
		_table.SetColumnName(ColumnName, new StringBuilder("Ore / Name"));
		_table.SetColumnName(ColumnAmount, new StringBuilder("Amount"));
		_table.SetColumnName(ColumnInfo, new StringBuilder("Info"));
		_table.SetColumnName(ColumnFromYou, new StringBuilder("From you"));
		_table.SetColumnName(ColumnFromReference, new StringBuilder("From marker"));
		_table.SetColumnComparison(ColumnMark, (a, b) => _marked.Contains(Entry(b)).CompareTo(_marked.Contains(Entry(a))));
		_table.SetColumnComparison(ColumnType, (a, b) => CompareBy(a, b, e => e.Prefix, byName: true));
		// Sorting by name groups each ore together with the biggest deposits first.
		_table.SetColumnComparison(ColumnName, (a, b) => CompareBy(a, b, e => e.Label, byName: true));
		_table.SetColumnComparison(ColumnAmount, (a, b) => Entry(a).MassKg.CompareTo(Entry(b).MassKg));
		_table.SetColumnComparison(ColumnInfo, (a, b) => CompareBy(a, b, e => e.Info, byName: false));
		_table.SetColumnComparison(ColumnFromYou, (a, b) => DistanceFromYou(Entry(a)).CompareTo(DistanceFromYou(Entry(b))));
		_table.SetColumnComparison(ColumnFromReference, (a, b) => DistanceFromReference(Entry(a)).CompareTo(DistanceFromReference(Entry(b))));
		_table.SetColumnAlign(ColumnMark, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
		_table.SetColumnAlign(ColumnAmount, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.SetColumnAlign(ColumnFromYou, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.SetColumnAlign(ColumnFromReference, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.ItemDoubleClicked += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => ToggleMarkSelected();
		Controls.Add(_table);
		_libraryControls.Add(_table);

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.265f), null, _statusText, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);
		_libraryControls.Add(_status);

		AddButton(-0.315f, 0.315f, "Export from GPS", ExportFromGps);
		AddButton(-0.105f, 0.315f, "Mark / Unmark", ToggleMarkSelected);
		AddButton(0.105f, 0.315f, "Import Marked", ImportMarked);
		AddButton(0.315f, 0.315f, "Import All Shown", ImportAllShown);
		AddButton(-0.315f, 0.375f, "Measure From Row", MeasureFromSelected);
		AddButton(-0.105f, 0.375f, "Measure From Me", MeasureFromMe);
		AddButton(0.105f, 0.375f, "Delete", DeleteSelected);
		// Help / Back share the bottom-right spot, like the other plugins. Esc closes the window.
		AddButton(0.315f, 0.375f, "Help", () => SetHelp(true));

		_helpText = new MyGuiControlMultilineText(new Vector2(0f, -0.315f), new Vector2(0.84f, 0.55f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
		{
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			Visible = false
		};
		_helpText.AppendText(HelpText);
		Controls.Add(_helpText);
		_helpControls.Add(_helpText);
		AddButton(0.315f, 0.375f, "Back", () => SetHelp(false), inHelp: true);

		RefreshRows(null);
	}

	/// <summary>
	/// Shows the Help page in place of the library, or goes back. Controls are hidden and shown rather than the
	/// window being rebuilt, so the table keeps its rows, sort and selection, and nothing is rebuilt from inside a click.
	/// </summary>
	private void SetHelp(bool show)
	{
		_showHelp = show;
		foreach (MyGuiControlBase control in _libraryControls)
		{
			control.Visible = !show;
		}
		foreach (MyGuiControlBase control in _helpControls)
		{
			control.Visible = show;
		}
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick, bool inHelp = false)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
		button.Visible = !inHelp;
		Controls.Add(button);
		(inHelp ? _helpControls : _libraryControls).Add(button);
		return button;
	}

	/// <summary>Rebuilds the filter options and table rows, then reselects <paramref name="keepSelected"/> if it's still shown.</summary>
	private void RefreshRows(LibraryEntry keepSelected)
	{
		_playerPosition = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		List<LibraryEntry> entries = MarkerLibrary.Entries;
		_marked.RemoveWhere(e => !entries.Contains(e));
		if (_reference != null && !entries.Contains(_reference))
		{
			_reference = null;
		}
		RebuildFilters(entries);
		Filter filter = _filters.FirstOrDefault(f => f.Name == _filterName) ?? _filters[0];
		_filterName = filter.Name;

		_table.Clear();
		foreach (LibraryEntry entry in entries.Where(filter.Matches))
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(entry);
			row.AddCell(new MyGuiControlTable.Cell(_marked.Contains(entry) ? "X" : "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Prefix ?? "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Label ?? "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.MassKg > 0 ? $"{entry.MassKg:N0} kg" : "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Info, entry));
			row.AddCell(new MyGuiControlTable.Cell(GpsMarkers.FormatDistance(DistanceFromYou(entry)), entry));
			string fromReference = _reference == null ? "" : entry == _reference ? "(this one)" : GpsMarkers.FormatDistance(DistanceFromReference(entry));
			row.AddCell(new MyGuiControlTable.Cell(fromReference, entry));
			_table.Add(row);
		}
		// Start alphabetical by ore; after that keep whatever column and direction the player picked.
		// (Passing an explicit sort state would pin it and stop header clicks from flipping direction.)
		if (_sortedOnce)
		{
			_table.Sort(switchSort: false);
		}
		else
		{
			_table.SortByColumn(ColumnName);
			_sortedOnce = true;
		}
		if (keepSelected != null)
		{
			int index = _table.FindIndex(r => r.UserData == keepSelected);
			if (index >= 0)
			{
				_table.SetSelectedRow(index);
				_table.ScrollToSelection();
			}
		}
		// The header names the marker being measured from, e.g. "From Ice"; its own row says "(this one)".
		_table.SetColumnName(ColumnFromReference, new StringBuilder(_reference == null ? "From marker" : $"From {_reference.Label}"));
		string shown = filter == _filters[0] ? $"{entries.Count} saved marker(s)" : $"{_table.RowsCount} of {entries.Count} marker(s) shown";
		string marked = _marked.Count > 0 ? $", {_marked.Count} marked" : "";
		SetStatus(_statusText.Length > 0 ? _statusText : $"{shown}{marked}. Click a header to sort; double-click a row to mark it.");
	}

	/// <summary>"All markers", then one option per ore, then one per non-ore marker type (ships, stores and so on).</summary>
	private void RebuildFilters(List<LibraryEntry> entries)
	{
		_filters = new List<Filter>
		{
			new Filter { Name = AllFilter, Matches = _ => true }
		};
		foreach (string ore in entries.Where(e => e.MassKg > 0).Select(e => e.Label).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o, StringComparer.OrdinalIgnoreCase))
		{
			_filters.Add(new Filter { Name = ore, Matches = e => e.MassKg > 0 && string.Equals(e.Label, ore, StringComparison.OrdinalIgnoreCase) });
		}
		foreach (string type in entries.Where(e => e.MassKg <= 0).Select(e => e.Prefix ?? "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
		{
			_filters.Add(new Filter { Name = type.Length == 0 ? "Other" : type, Matches = e => e.MassKg <= 0 && string.Equals(e.Prefix ?? "", type, StringComparison.OrdinalIgnoreCase) });
		}
		_filterBox.ItemSelected -= OnFilterSelected;
		_filterBox.ClearItems();
		for (int i = 0; i < _filters.Count; i++)
		{
			_filterBox.AddItem(i, _filters[i].Name, sort: false);
		}
		int selected = Math.Max(0, _filters.FindIndex(f => f.Name == _filterName));
		_filterBox.SelectItemByKey(selected, sendEvent: false);
		_filterBox.ItemSelected += OnFilterSelected;
	}

	private void OnFilterSelected()
	{
		int index = (int)_filterBox.GetSelectedKey();
		if (index >= 0 && index < _filters.Count)
		{
			_filterName = _filters[index].Name;
			RefreshRows(SelectedEntry());
		}
	}

	private LibraryEntry SelectedEntry()
	{
		return _table.SelectedRow?.UserData as LibraryEntry;
	}

	private void ExportFromGps()
	{
		DateTime started = DateTime.Now;
		int moved = MarkerLibrary.ExportFromGps();
		_statusText = moved == 0 ? "No OreScout ore markers in your GPS list to export." : $"Moved {moved} marker(s) from GPS into the library.";
		RefreshRows(SelectedEntry());
		// OreScout only (personality): comment on the best marker just saved, rare ores first, then the biggest.
		LibraryEntry best = MarkerLibrary.Entries.Where(e => e.Saved >= started).OrderByDescending(e => OreScoutSession.RareOres.Contains(e.Label ?? "")).ThenByDescending(e => e.MassKg).FirstOrDefault();
		if (best != null)
		{
			Personality.Say("marker_saved", "ore", best.Label);
		}
	}

	private void ToggleMarkSelected()
	{
		LibraryEntry entry = SelectedEntry();
		if (entry == null)
		{
			SetStatus("Select a marker first.");
			return;
		}
		if (!_marked.Remove(entry))
		{
			_marked.Add(entry);
		}
		RefreshRows(entry);
	}

	private void ImportMarked()
	{
		if (_marked.Count == 0)
		{
			SetStatus("Mark some markers first: select a row and press Mark / Unmark, or double-click it.");
			return;
		}
		_statusText = Import(_marked.ToList());
		_marked.Clear();
		RefreshRows(SelectedEntry());
	}

	private void ImportAllShown()
	{
		List<LibraryEntry> shown = new List<LibraryEntry>();
		for (int i = 0; i < _table.RowsCount; i++)
		{
			if (_table.GetRow(i)?.UserData is LibraryEntry entry)
			{
				shown.Add(entry);
			}
		}
		if (shown.Count == 0)
		{
			SetStatus("Nothing is shown to import.");
			return;
		}
		SetStatus(Import(shown));
	}

	/// <summary>Imports the given markers and returns a status line saying how it went.</summary>
	private string Import(List<LibraryEntry> entries)
	{
		int added = entries.Count(MarkerLibrary.ImportToGps);
		int skipped = entries.Count - added;
		if (added == 0)
		{
			return skipped == 1 ? "That marker is already in your GPS list." : "All of those markers are already in your GPS list.";
		}
		return skipped == 0 ? $"Added {added} marker(s) to GPS." : $"Added {added} marker(s) to GPS; {skipped} were already there.";
	}

	private void MeasureFromSelected()
	{
		LibraryEntry entry = SelectedEntry();
		if (entry == null)
		{
			SetStatus("Select the marker to measure from first.");
			return;
		}
		_reference = entry;
		_statusText = $"Measuring from {entry.Prefix} - {entry.Label}. Click the From {entry.Label} header to sort by it.";
		RefreshRows(entry);
	}

	private void MeasureFromMe()
	{
		_reference = null;
		_statusText = "Stopped measuring from a marker. From you still shows distances from you.";
		RefreshRows(SelectedEntry());
	}

	private void DeleteSelected()
	{
		LibraryEntry entry = SelectedEntry();
		if (entry == null)
		{
			SetStatus("Select a marker first.");
			return;
		}
		MarkerLibrary.Delete(entry);
		_statusText = $"Deleted '{entry.Prefix} - {entry.Label}'.";
		RefreshRows(null);
	}

	private void SetStatus(string text)
	{
		_statusText = "";
		_status.Text = text;
	}

	private double DistanceFromYou(LibraryEntry entry)
	{
		return Vector3D.Distance(_playerPosition, entry.Position);
	}

	/// <summary>Distance from the reference marker; with no reference, every row compares equal.</summary>
	private double DistanceFromReference(LibraryEntry entry)
	{
		return _reference == null ? 0.0 : Vector3D.Distance(_reference.Position, entry.Position);
	}

	private static LibraryEntry Entry(MyGuiControlTable.Cell cell)
	{
		return (LibraryEntry)cell.UserData;
	}

	/// <summary>Compares by text, then (for type and name columns) puts the biggest amount first.</summary>
	private static int CompareBy(MyGuiControlTable.Cell a, MyGuiControlTable.Cell b, Func<LibraryEntry, string> text, bool byName)
	{
		LibraryEntry x = Entry(a);
		LibraryEntry y = Entry(b);
		int result = string.Compare(text(x), text(y), StringComparison.OrdinalIgnoreCase);
		if (result == 0 && byName)
		{
			result = y.MassKg.CompareTo(x.MassKg);
		}
		if (result == 0)
		{
			result = string.Compare(x.Label, y.Label, StringComparison.OrdinalIgnoreCase);
		}
		return result;
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"OreScout turns what an ore detector can see into GPS markers you can keep. Every scan starts at an ore detector block and reaches only as far as that detector's own range: set it with the Range slider in the detector's terminal (modded detectors with very long ranges are capped at 3 km). The detector must be intact, switched on and powered. Nothing is needed on the server, and the markers are only in your own GPS list.\n\n" +
		"THE ACTIONS\n" +
		"Drag an ore detector onto your toolbar and pick one of its OreScout actions. Scout Ore puts one marker on each ore of each asteroid or planet in range, showing the distance and about how much ore a ship drill would collect, in kg. Asteroid markers for the same ore within 1,500 m of each other share one marker, placed on the biggest deposit and marked with how many asteroids it covers. Scout Deposits puts a purple marker on every separate deposit in range, right on the ore, so you can choose which pocket to drill first. Clear Deposit Markers removes every Scout Deposits marker. Marker Library opens this window. Only one scan runs at a time, and stone is never reported.\n\n" +
		"MARKERS\n" +
		"Pink markers are ore from Scout Ore, purple ones are deposits. Scanning again updates markers instead of adding duplicates. Ore markers stay after the ore is mined out, because asteroids get reset; one is only replaced when a newer marker for the same ore appears close by. Deposit markers whose deposit is gone are removed. OreScout only ever changes the markers it made itself, so your own GPS points are safe. With the GPS Folders plugin loaded, new markers go into a GPS folder named after the sector (server) you're in; a marker already in a folder stays there.\n\n" +
		"DETECTOR SETTINGS\n" +
		"The first time you open a detector's toolbar actions, its Custom Data gets two sections, OreScout and OreScout Deposits. Ores is a comma-separated list (blank means every ore in the world). MinDepositVoxels is the smallest deposit to report, in cubic metres. YieldBonusPercent adds yield for modded drills, for example 50. MergeRadius (OreScout section) is how close asteroids must be to share a marker, in metres; 0 turns merging off. DepositSpacing (OreScout Deposits section) is how far apart ore can be and still count as one deposit. CreateGps and ShowChat turn the markers and the chat lines on or off.\n\n" +
		"THE LIBRARY\n" +
		"This window keeps ore markers out of your GPS list until you need them. Export from GPS moves your Scout Ore markers here (deposit markers stay in GPS). Show, at the top right, filters the list to one ore. Click a column header to sort, and click it again to reverse. From you is each marker's distance from where you are. Measure From Row makes the From marker column show every marker's distance from the selected one (its header takes that marker's name), which finds the ore nearest a spot; Measure From Me clears it. To bring markers back, select a row and press Mark / Unmark (or double-click it) to tick it with an X, then Import Marked; Import All Shown imports everything the filter shows. Markers already in your GPS list are skipped, and the distance is worked out again from where you are. Delete removes the selected row. Esc closes the window. The library is saved for each world in OreScout_Markers_<world>.xml in %AppData%\\SpaceEngineers\\Storage.\n\n" +
		"COMMANDS\n" +
		"/scout opens this window. /scout name and a name (up to 24 characters) changes what OreScout's chat lines show under; /scout name on its own puts OreScout back. /scout sector shows which GPS folder new markers go into. /tim opens whichever of our plugin windows you used last, and the list at the top left of this window switches to another of our plugins.\n\n" +
		"CHAT\n" +
		"The Chat dropdown at the top right sets how much OreScout talks: Off, Quiet, Normal or Chatty. It's a cheerful prospector whose name shows in gold. It comments when you start a scan, when a scan finds ore (one line about the best find: rare ores, which are platinum, uranium, gold and silver, beat common ones, and nearer beats farther) or nothing, when you open the library, and when you export markers. It has no urgent lines, so on Quiet it only answers Wilson's roll call when Wilson is loaded. Our plugins take turns: after any of them speaks, the next ordinary line waits 2 minutes on Chatty, 5 on Normal and 15 on Quiet, and the same comment isn't repeated within 10 minutes. Only you see the lines. The chat level and name are saved for you in OreScout_Settings.txt and apply in every world.";
}
