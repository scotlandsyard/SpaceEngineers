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
		// The plugin switcher sits in the top-left corner (its list drops down over the left of the filter row), so the filter lives on the right.
		PluginSwitcher.AddSwitcher(this, AddCaption("OreScout Marker Library"));

		Controls.Add(new MyGuiControlLabel(new Vector2(0.16f, -0.345f), null, "Show:", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER));
		_filterBox = new MyGuiControlCombobox(new Vector2(0.42f, -0.345f), new Vector2(0.25f, 0.04f), originAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER, openAreaItemsCount: 12);
		_filterBox.ItemSelected += OnFilterSelected;
		Controls.Add(_filterBox);

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

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.265f), null, _statusText, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, 0.315f, "Export from GPS", ExportFromGps);
		AddButton(-0.105f, 0.315f, "Mark / Unmark", ToggleMarkSelected);
		AddButton(0.105f, 0.315f, "Import Marked", ImportMarked);
		AddButton(0.315f, 0.315f, "Import All Shown", ImportAllShown);
		AddButton(-0.315f, 0.375f, "Measure From Row", MeasureFromSelected);
		AddButton(-0.105f, 0.375f, "Measure From Me", MeasureFromMe);
		AddButton(0.105f, 0.375f, "Delete", DeleteSelected);
		AddButton(0.315f, 0.375f, "Close", () => CloseScreen());

		RefreshRows(null);
	}

	private void AddButton(float x, float y, string text, Action onClick)
	{
		Controls.Add(new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick()));
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
		int moved = MarkerLibrary.ExportFromGps();
		_statusText = moved == 0 ? "No OreScout ore markers in your GPS list to export." : $"Moved {moved} marker(s) from GPS into the library.";
		RefreshRows(SelectedEntry());
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
}
