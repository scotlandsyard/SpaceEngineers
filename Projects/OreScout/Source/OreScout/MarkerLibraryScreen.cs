using System;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace OreScout;

/// <summary>
/// In-game window listing the saved markers. Click a column header to sort; Export moves OreScout markers
/// out of the GPS list into the library, Import puts the selected one back.
/// </summary>
public class MarkerLibraryScreen : MyGuiScreenBase
{
	private const int ColumnType = 0;

	private const int ColumnName = 1;

	private const int ColumnAmount = 2;

	private const int ColumnInfo = 3;

	private const int ColumnDistance = 4;

	private MyGuiControlTable _table;

	private MyGuiControlLabel _status;

	private Vector3D _playerPosition;

	private string _statusText = "";

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
		AddCaption("OreScout Marker Library");

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.33f),
			Size = new Vector2(0.84f, 0.6f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 5,
			VisibleRowsCount = 16
		};
		_table.SetCustomColumnWidths(new float[] { 0.15f, 0.25f, 0.2f, 0.22f, 0.18f });
		_table.SetColumnName(ColumnType, new StringBuilder("Type"));
		_table.SetColumnName(ColumnName, new StringBuilder("Ore / Name"));
		_table.SetColumnName(ColumnAmount, new StringBuilder("Amount"));
		_table.SetColumnName(ColumnInfo, new StringBuilder("Info"));
		_table.SetColumnName(ColumnDistance, new StringBuilder("Distance"));
		_table.SetColumnComparison(ColumnType, (a, b) => CompareBy(a, b, e => e.Prefix, byName: true));
		// Sorting by name groups each ore together with the biggest deposits first.
		_table.SetColumnComparison(ColumnName, (a, b) => CompareBy(a, b, e => e.Label, byName: true));
		_table.SetColumnComparison(ColumnAmount, (a, b) => Entry(a).MassKg.CompareTo(Entry(b).MassKg));
		_table.SetColumnComparison(ColumnInfo, (a, b) => CompareBy(a, b, e => e.Info, byName: false));
		_table.SetColumnComparison(ColumnDistance, (a, b) => Distance(Entry(a)).CompareTo(Distance(Entry(b))));
		_table.SetColumnAlign(ColumnAmount, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.SetColumnAlign(ColumnDistance, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.ItemDoubleClicked += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => ImportSelected();
		Controls.Add(_table);

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.30f), null, _statusText, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, "Export from GPS", ExportFromGps);
		AddButton(-0.105f, "Import to GPS", ImportSelected);
		AddButton(0.105f, "Delete", DeleteSelected);
		AddButton(0.315f, "Close", () => CloseScreen());

		RefreshRows();
	}

	private void AddButton(float x, string text, Action onClick)
	{
		Controls.Add(new MyGuiControlButton(new Vector2(x, 0.37f), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick()));
	}

	private void RefreshRows()
	{
		_playerPosition = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		_table.Clear();
		foreach (LibraryEntry entry in MarkerLibrary.Entries)
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(entry);
			row.AddCell(new MyGuiControlTable.Cell(entry.Prefix ?? "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Label ?? "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.MassKg > 0 ? $"{entry.MassKg:N0} kg" : "", entry));
			row.AddCell(new MyGuiControlTable.Cell(entry.Info, entry));
			row.AddCell(new MyGuiControlTable.Cell(GpsMarkers.FormatDistance(Distance(entry)), entry));
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
		SetStatus(_statusText.Length > 0 ? _statusText : $"{MarkerLibrary.Entries.Count} saved marker(s). Click a column header to sort; double-click a row to import it.");
	}

	private bool _sortedOnce;

	private void ExportFromGps()
	{
		int moved = MarkerLibrary.ExportFromGps();
		_statusText = moved == 0 ? "No OreScout ore markers in your GPS list to export." : $"Moved {moved} marker(s) from GPS into the library.";
		RefreshRows();
	}

	private void ImportSelected()
	{
		LibraryEntry entry = _table.SelectedRow?.UserData as LibraryEntry;
		if (entry == null)
		{
			SetStatus("Select a marker first.");
			return;
		}
		SetStatus(MarkerLibrary.ImportToGps(entry) ? $"Added '{MarkerLibrary.BuildName(entry, _playerPosition)}' to GPS." : $"'{entry.Label}' is already in your GPS list.");
	}

	private void DeleteSelected()
	{
		LibraryEntry entry = _table.SelectedRow?.UserData as LibraryEntry;
		if (entry == null)
		{
			SetStatus("Select a marker first.");
			return;
		}
		MarkerLibrary.Delete(entry);
		_statusText = $"Deleted '{entry.Prefix} - {entry.Label}'.";
		RefreshRows();
	}

	private void SetStatus(string text)
	{
		_statusText = "";
		_status.Text = text;
	}

	private double Distance(LibraryEntry entry)
	{
		return Vector3D.Distance(_playerPosition, entry.Position);
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
