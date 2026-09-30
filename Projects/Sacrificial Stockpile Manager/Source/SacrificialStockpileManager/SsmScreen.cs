using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>
/// The Stockpile Manager window. Pick a grid (loaded or last seen) and a view; the table below shows it live.
/// Every setting is made here and stored in the plugin's own per-world file, never in Custom Data.
/// </summary>
public partial class SsmScreen : MyGuiScreenBase
{
	private enum View
	{
		AllGrids,
		Overview,
		Items,
		Blocks,
		Block,
		Production,
		Displays,
		Log,
		Help
	}

	// In the same order as View.
	private static readonly string[] ViewNames = { "All grids", "Overview", "Items & quotas", "Blocks", "Block settings", "Production", "Displays (LCD)", "Log", "Help" };

	// In the same order as BlockRole.
	private static readonly string[] RoleNames = { "Auto", "Storage", "Intake", "Stock", "Manual" };

	private class Column
	{
		public string Name;

		public float Width;

		public bool RightAligned;

		public bool Sortable = true;
	}

	private class RowData
	{
		public string Key;

		public string[] Texts;

		/// <summary>Per column: a double sorts as a number, anything else as text.</summary>
		public object[] SortValues;

		public Color? Color;
	}

	private const int RefreshFrames = 30;

	private const float Left = -0.42f;

	private const float Row1Y = -0.375f;

	private const float Row2Y = -0.325f;

	private const float Row3Y = -0.28f;

	private const float EditorY = 0.255f;

	private const float StatusY = 0.315f;

	private const float ButtonsY = 0.39f;

	// Remembered between openings of the window.
	private static long s_gridKey;

	private static View s_view = View.Overview;

	private static View s_viewBeforeHelp = View.Overview;

	private static long s_blockId;

	private static string s_itemKey;

	private static string s_amount = "";

	private static int s_page;

	private static long s_source;

	private MyGuiControlCombobox _gridCombo;

	private MyGuiControlCombobox _viewCombo;

	private MyGuiControlCombobox _blockCombo;

	private MyGuiControlCombobox _roleCombo;

	private MyGuiControlCombobox _itemCombo;

	private MyGuiControlCombobox _pageCombo;

	private MyGuiControlCombobox _sourceCombo;

	private MyGuiControlTextbox _amountBox;

	private MyGuiControlTable _table;

	private MyGuiControlLabel _status;

	private MyGuiControlMultilineText _helpText;

	private readonly List<MyGuiControlCheckbox> _acceptBoxes = new List<MyGuiControlCheckbox>();

	private readonly List<long> _gridKeys = new List<long>();

	private readonly List<long> _blockKeys = new List<long>();

	private readonly List<string> _itemKeys = new List<string>();

	private readonly List<long> _sourceKeys = new List<long>();

	private MyGuiControlButton _autocraftButton;

	private MyGuiControlButton _forgetButton;

	/// <summary>Buttons that need the grid to be loaded.</summary>
	private readonly List<MyGuiControlButton> _liveButtons = new List<MyGuiControlButton>();

	private string _rowsSignature;

	private string _gridsSignature;

	private int _frames;

	private bool _recreatePending;

	private bool _suppressEvents;

	private string _message;

	private double _messageUntil;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color WarningColor => new Color(255, 190, 90);

	private static Color GoodColor => new Color(140, 230, 140);

	private static SsmSession Session => SsmSession.Instance;

	private static GridSnapshot Grid => Store.Grid(s_gridKey);

	private static Construct Live => Session?.FindConstruct(s_gridKey);

	public SsmScreen(long gridKey, IMyTerminalBlock fromBlock)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.95f))
	{
		if (gridKey != 0)
		{
			s_gridKey = gridKey;
		}
		if (fromBlock != null)
		{
			if (fromBlock.HasInventory)
			{
				s_blockId = fromBlock.EntityId;
				s_view = View.Block;
			}
			else
			{
				s_view = View.Displays;
			}
		}
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		CloseButtonEnabled = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "SacrificialStockpileManagerScreen";
	}

	protected override void OnClosed()
	{
		if (Session != null)
		{
			Session.ViewedKey = 0;
		}
		base.OnClosed();
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		_suppressEvents = true;
		try
		{
			CreateControls();
		}
		finally
		{
			_suppressEvents = false;
		}
		_rowsSignature = null;
		_gridsSignature = null;
		RefreshAll();
	}

	private void CreateControls()
	{
		AddCaption("Sacrificial Stockpile Manager");
		_autocraftButton = null;
		_forgetButton = null;
		_liveButtons.Clear();
		_acceptBoxes.Clear();
		_blockCombo = null;
		_roleCombo = null;
		_itemCombo = null;
		_amountBox = null;
		_pageCombo = null;
		_sourceCombo = null;
		_helpText = null;

		AddLabel(Left, Row1Y, "Grid");
		_gridCombo = AddCombo(-0.36f, Row1Y, 0.43f, 12);
		_gridCombo.ItemSelected += OnGridSelected;
		FillGridCombo();

		AddLabel(0.1f, Row1Y, "View");
		_viewCombo = AddCombo(0.16f, Row1Y, 0.26f, ViewNames.Length);
		for (int i = 0; i < ViewNames.Length; i++)
		{
			_viewCombo.AddItem(i, ViewNames[i], i, null, sort: false);
		}
		_viewCombo.SelectItemByKey((long)s_view, sendEvent: false);
		_viewCombo.ItemSelected += () => SwitchView((View)_viewCombo.GetSelectedKey());

		float tableTop = -0.335f;
		int rows = HasEditorRow(s_view) ? 13 : 15;
		if (s_view == View.Block)
		{
			CreateBlockControls();
			tableTop = -0.245f;
			rows = 10;
		}

		if (s_view == View.Help)
		{
			_helpText = new MyGuiControlMultilineText(new Vector2(0f, tableTop), new Vector2(0.84f, 0.6f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
			{
				// textBoxAlign above only places the text inside the control; this anchors the control itself by its top edge.
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
			};
			SsmHelp.Write(_helpText);
			Controls.Add(_helpText);
		}

		List<Column> columns = ColumnsFor(s_view);
		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, tableTop),
			Size = new Vector2(0.84f, 0.5f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = columns.Count,
			VisibleRowsCount = rows
		};
		_table.SetCustomColumnWidths(columns.Select(c => c.Width).ToArray());
		for (int i = 0; i < columns.Count; i++)
		{
			_table.SetColumnName(i, new StringBuilder(columns[i].Name));
			if (columns[i].RightAligned)
			{
				_table.SetColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
				_table.SetHeaderColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
			}
			if (columns[i].Sortable)
			{
				_table.SetColumnComparison(i, CompareCells);
			}
		}
		_table.Visible = s_view != View.Help;
		_table.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => OnRowSelected();
		_table.ItemDoubleClicked += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) => OnRowDoubleClicked();
		Controls.Add(_table);

		if (s_view == View.Items || s_view == View.Block)
		{
			CreateItemEditor();
		}
		else if (s_view == View.Displays)
		{
			CreateDisplayEditor();
		}

		_status = new MyGuiControlLabel(new Vector2(Left, StatusY), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		// Four button slots per view; the window closes with the X in the corner or Esc.
		switch (s_view)
		{
		case View.AllGrids:
			AddButton(-0.315f, "Open grid", OpenSelectedGrid);
			AddButton(-0.105f, "GPS marker", MarkGps);
			AddButton(0.105f, "Remove from list", ForgetGrid);
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Overview:
			AddButton(-0.315f, "Change setting", ChangeSelectedSetting);
			AddButton(-0.105f, "GPS marker", MarkGps);
			_forgetButton = AddButton(0.105f, "Remove from list", ForgetGrid);
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Items:
			AddButton(-0.315f, "Set quota", SetQuota);
			AddButton(-0.105f, "Clear quota", ClearQuota);
			_autocraftButton = AddButton(0.105f, "Autocraft", () => ToggleGridSetting("autocraft"));
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Blocks:
		case View.Production:
			AddButton(-0.315f, "Edit block", EditSelectedBlock);
			_liveButtons.Add(AddButton(-0.105f, "Turn on / off", ToggleSelectedBlock));
			_liveButtons.Add(AddButton(0.105f, "Rescan", Rescan));
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Block:
			AddButton(-0.315f, "Set minimum", () => SetLimit(minimum: true));
			AddButton(-0.105f, "Set maximum", () => SetLimit(minimum: false));
			AddButton(0.105f, "Clear limits", ClearLimits);
			AddButton(0.315f, "Back to blocks", () => SwitchView(View.Blocks));
			break;
		case View.Displays:
			AddButton(-0.315f, "Show page", AssignDisplay);
			AddButton(-0.105f, "Stop showing", ClearDisplay);
			_liveButtons.Add(AddButton(0.105f, "Rescan", Rescan));
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Log:
			AddButton(-0.315f, "Clear log", ClearLog);
			_liveButtons.Add(AddButton(0.105f, "Rescan", Rescan));
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Help:
			AddButton(0.315f, "Back", () => SwitchView(s_viewBeforeHelp));
			break;
		}
	}

	private static bool HasEditorRow(View view)
	{
		return view == View.Items || view == View.Block || view == View.Displays;
	}

	/// <summary>Block, role and accepts rows at the top of the Block settings view.</summary>
	private void CreateBlockControls()
	{
		GridSnapshot grid = Grid;
		List<BlockSnapshot> blocks = grid == null ? new List<BlockSnapshot>() : grid.Blocks.Where(b => b.HasInventory).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (blocks.Count > 0 && !blocks.Any(b => b.Id == s_blockId))
		{
			s_blockId = blocks[0].Id;
		}
		BlockSnapshot block = blocks.FirstOrDefault(b => b.Id == s_blockId);
		BlockRules rules = block == null ? null : Store.BlockRules(block.Id);

		AddLabel(Left, Row2Y, "Block");
		_blockCombo = AddCombo(-0.36f, Row2Y, 0.43f, 14);
		_blockKeys.Clear();
		foreach (BlockSnapshot entry in blocks)
		{
			_blockCombo.AddItem(_blockKeys.Count, entry.Name, _blockKeys.Count, null, sort: false);
			_blockKeys.Add(entry.Id);
		}
		if (block != null)
		{
			_blockCombo.SelectItemByKey(_blockKeys.IndexOf(block.Id), sendEvent: false);
		}
		_blockCombo.ItemSelected += OnBlockSelected;

		AddLabel(0.1f, Row2Y, "Role");
		_roleCombo = AddCombo(0.16f, Row2Y, 0.26f, RoleNames.Length);
		for (int i = 0; i < RoleNames.Length; i++)
		{
			string name = RoleNames[i];
			if (i == 0 && block != null)
			{
				name = $"Auto ({Construct.Resolve(block.Kind, null, block.Docked).ToString().ToLowerInvariant()}{(block.Docked ? ", docked" : "")})";
			}
			_roleCombo.AddItem(i, name, i, null, sort: false);
		}
		_roleCombo.SelectItemByKey((long)(rules?.Role ?? BlockRole.Auto), sendEvent: false);
		_roleCombo.ItemSelected += OnRoleSelected;
		_roleCombo.Enabled = block != null;

		AddLabel(Left, Row3Y, "Accepts");
		for (int i = 0; i < Items.Categories.Length; i++)
		{
			float x = -0.31f + i * 0.093f;
			ItemCategory category = Items.Categories[i];
			MyGuiControlCheckbox box = new MyGuiControlCheckbox(new Vector2(x, Row3Y), null, $"Sort {Items.CategoryNames[i].ToLowerInvariant()} into this block", rules != null && rules.Accept.Contains(category.ToString()));
			box.IsCheckedChanged = checkbox => OnAcceptChanged(category, checkbox.IsChecked);
			box.Enabled = block != null;
			Controls.Add(box);
			_acceptBoxes.Add(box);
			Controls.Add(new MyGuiControlLabel(new Vector2(x + 0.016f, Row3Y), null, Items.CategoryShort[i], null, 0.7f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		}
	}

	/// <summary>Item picker and amount box, used by Items & quotas and Block settings.</summary>
	private void CreateItemEditor()
	{
		_itemCombo = AddCombo(Left, EditorY, 0.4f, 14);
		_itemKeys.Clear();
		IEnumerable<string> keys = Items.Catalog;
		GridSnapshot grid = Grid;
		if (grid != null)
		{
			// Modded or unusual items that exist on the grid but aren't in the catalog.
			keys = keys.Concat(grid.Totals.Keys.Where(k => !Items.Catalog.Contains(k)));
		}
		foreach (string key in keys)
		{
			_itemCombo.AddItem(_itemKeys.Count, $"{Items.Name(key)}  ({Items.CategoryShort[(int)Items.Category(key)]})", _itemKeys.Count, null, sort: false);
			_itemKeys.Add(key);
		}
		if (s_itemKey != null && _itemKeys.Contains(s_itemKey))
		{
			_itemCombo.SelectItemByKey(_itemKeys.IndexOf(s_itemKey), sendEvent: false);
		}
		_itemCombo.ItemSelected += () =>
		{
			int index = (int)_itemCombo.GetSelectedKey();
			if (index >= 0 && index < _itemKeys.Count)
			{
				s_itemKey = _itemKeys[index];
				SelectRowForItem(s_itemKey);
			}
		};

		AddLabel(0.0f, EditorY, "Amount");
		_amountBox = new MyGuiControlTextbox(new Vector2(0.08f, EditorY), s_amount, 12)
		{
			Size = new Vector2(0.12f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		_amountBox.TextChanged += box => s_amount = box.Text;
		Controls.Add(_amountBox);

		if (s_view == View.Block)
		{
			AddButton(0.315f, "Accept item", ToggleAcceptItem, EditorY);
		}
	}

	private void CreateDisplayEditor()
	{
		AddLabel(Left, EditorY, "Page");
		_pageCombo = AddCombo(-0.36f, EditorY, 0.22f, Displays.Pages.Length);
		for (int i = 0; i < Displays.Pages.Length; i++)
		{
			_pageCombo.AddItem(i, Displays.Pages[i], i, null, sort: false);
		}
		_pageCombo.SelectItemByKey(Math.Max(0, Math.Min(s_page, Displays.Pages.Length - 1)), sendEvent: false);
		_pageCombo.ItemSelected += () => s_page = (int)_pageCombo.GetSelectedKey();

		AddLabel(-0.12f, EditorY, "Showing");
		_sourceCombo = AddCombo(-0.03f, EditorY, 0.45f, 12);
		_sourceKeys.Clear();
		_sourceCombo.AddItem(0, "The grid the display is on", 0, null, sort: false);
		_sourceKeys.Add(0);
		foreach (GridSnapshot grid in SortedGrids())
		{
			_sourceCombo.AddItem(_sourceKeys.Count, grid.Name, _sourceKeys.Count, null, sort: false);
			_sourceKeys.Add(grid.Key);
		}
		_sourceCombo.SelectItemByKey(Math.Max(0, _sourceKeys.IndexOf(s_source)), sendEvent: false);
		_sourceCombo.ItemSelected += () =>
		{
			int index = (int)_sourceCombo.GetSelectedKey();
			s_source = index >= 0 && index < _sourceKeys.Count ? _sourceKeys[index] : 0;
		};
	}

	private void AddLabel(float x, float y, string text)
	{
		Controls.Add(new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
	}

	private MyGuiControlCombobox AddCombo(float x, float y, float width, int openItems)
	{
		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(x, y), new Vector2(width, 0.04f), null, null, openItems, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(combo);
		return combo;
	}

	private MyGuiControlButton AddButton(float x, string text, Action onClick, float y = ButtonsY)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) =>
		{
			try
			{
				onClick();
			}
			catch (Exception ex)
			{
				ShowMessage("Something went wrong: " + ex.Message, WarningColor);
				MyLog.Default.WriteLineAndConsole($"[SSM] Button '{text}': {ex}");
			}
		});
		Controls.Add(button);
		return button;
	}

	public override bool Update(bool hasFocus)
	{
		bool result = base.Update(hasFocus);
		try
		{
			if (_recreatePending)
			{
				// Rebuilt here rather than inside a combobox event, which is still iterating the controls.
				_recreatePending = false;
				RecreateControls(constructor: false);
			}
			else if (++_frames >= RefreshFrames)
			{
				_frames = 0;
				RefreshAll();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Screen update: {ex}");
		}
		return result;
	}

	// ---- Grid list ----

	private static List<GridSnapshot> SortedGrids()
	{
		SsmSession session = Session;
		return Store.Grids
			.OrderBy(g => session?.FindConstruct(g.Key) == null ? 1 : 0)
			.ThenBy(g => session?.FindConstruct(g.Key) == null ? -g.LastSeenUtc.Ticks : 0)
			.ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static string GridLabel(GridSnapshot grid)
	{
		bool live = Session?.FindConstruct(grid.Key) != null;
		string kind = grid.IsStation ? "station" : "ship";
		return live ? $"{grid.Name}  ({kind})" : $"{grid.Name}  ({kind}, seen {Displays.Ago(grid.LastSeenUtc)})";
	}

	private void FillGridCombo()
	{
		List<GridSnapshot> grids = SortedGrids();
		string signature = string.Join("|", grids.Select(g => g.Key + ":" + GridLabel(g)));
		if (signature == _gridsSignature)
		{
			return;
		}
		_gridsSignature = signature;
		_gridCombo.ClearItems();
		_gridKeys.Clear();
		foreach (GridSnapshot grid in grids)
		{
			_gridCombo.AddItem(_gridKeys.Count, GridLabel(grid), _gridKeys.Count, null, sort: false);
			_gridKeys.Add(grid.Key);
		}
		int selected = _gridKeys.IndexOf(s_gridKey);
		if (selected < 0 && _gridKeys.Count > 0)
		{
			selected = 0;
			s_gridKey = _gridKeys[0];
			_rowsSignature = null;
			if (s_view == View.Block || HasEditorRow(s_view))
			{
				_recreatePending = true;
			}
		}
		if (selected >= 0)
		{
			_gridCombo.SelectItemByKey(selected, sendEvent: false);
		}
		if (Session != null)
		{
			Session.ViewedKey = s_gridKey;
		}
	}

	private void OnGridSelected()
	{
		if (_suppressEvents)
		{
			return;
		}
		int index = (int)_gridCombo.GetSelectedKey();
		if (index >= 0 && index < _gridKeys.Count && _gridKeys[index] != s_gridKey)
		{
			s_gridKey = _gridKeys[index];
			if (Session != null)
			{
				Session.ViewedKey = s_gridKey;
			}
			// The block list, item list and the rest depend on the grid, so rebuild everything.
			_recreatePending = true;
		}
	}

	private void OnBlockSelected()
	{
		if (_suppressEvents)
		{
			return;
		}
		int index = (int)_blockCombo.GetSelectedKey();
		if (index >= 0 && index < _blockKeys.Count && _blockKeys[index] != s_blockId)
		{
			s_blockId = _blockKeys[index];
			_recreatePending = true;
		}
	}

	private void SwitchView(View view)
	{
		if (view == s_view)
		{
			return;
		}
		if (view == View.Help)
		{
			s_viewBeforeHelp = s_view;
		}
		s_view = view;
		_recreatePending = true;
	}

	private void ShowHelp()
	{
		SwitchView(View.Help);
	}

	// ---- Refresh ----

	private void RefreshAll()
	{
		if (Session == null)
		{
			SetStatus("Stockpile Manager isn't running in this session.", WarningColor);
			return;
		}
		if (!_gridCombo.IsOpen)
		{
			FillGridCombo();
		}
		RefreshRows();
		RefreshButtons();
		if (_message != null && SsmSession.Now < _messageUntil)
		{
			return;
		}
		_message = null;
		SetStatus(HintFor(Grid), null);
	}

	private void RefreshButtons()
	{
		GridRules rules = Store.GridRules(Grid);
		bool live = Live != null;
		if (_autocraftButton != null)
		{
			_autocraftButton.Text = rules != null && rules.Autocraft ? "Autocraft: ON" : "Autocraft: OFF";
		}
		if (_forgetButton != null)
		{
			_forgetButton.Enabled = !live && Grid != null;
		}
		foreach (MyGuiControlButton button in _liveButtons)
		{
			button.Enabled = live;
		}
	}

	private string HintFor(GridSnapshot grid)
	{
		if (s_view == View.AllGrids)
		{
			return AllGridsHint();
		}
		if (grid == null)
		{
			return "No grids yet. Your ships and stations show up here once they've been in range.";
		}
		bool live = Live != null;
		string offline = live ? "" : " Not loaded: changes apply when it's back in range.";
		switch (s_view)
		{
		case View.Overview:
			return "Double-click a setting (or select it and press Change setting) to switch it." + offline;
		case View.Items:
			return "Select an item (or pick one below), type an amount, then Set quota. Autocraft queues the shortfall." + offline;
		case View.Blocks:
			return "Double-click a block to edit its role, what it accepts and its stock limits." + offline;
		case View.Block:
		{
			BlockSnapshot block = grid.Block(s_blockId);
			string volume = block != null && block.MaxVolume > 0.0 ? $"{Items.Litres(block.Volume)} used of {Items.Litres(block.MaxVolume)} ({block.Fill:P0}), {Items.Litres(block.MaxVolume - block.Volume)} free. " : "";
			return volume + "Pick an item, type an amount, set a minimum or maximum. Blank clears it.";
		}
		case View.Production:
			return live ? "Assemblers, refineries, reactors and generators. Double-click one to edit its limits." : "Last known state." + offline;
		case View.Displays:
			return "Select a screen, pick a page and what it shows, then Show page. The LCD updates by itself." + offline;
		case View.Log:
			return live ? "What the plugin did on this grid, newest first." : "The log is only kept while the grid is loaded.";
		case View.Help:
			return "Scroll for more. Back returns to the view you were on.";
		default:
			return "";
		}
	}

	private void SetStatus(string text, Color? color)
	{
		_status.Text = text;
		_status.ColorMask = (color ?? Color.White).ToVector4();
	}

	private void ShowMessage(string text, Color? color = null)
	{
		_message = text;
		_messageUntil = SsmSession.Now + 6.0;
		SetStatus(text, color);
	}

	private void RefreshRows()
	{
		GridSnapshot grid = Grid;
		List<RowData> rows = s_view == View.AllGrids ? AllGridRows() : grid == null ? new List<RowData>() : BuildRows(grid);
		if (s_view == View.AllGrids)
		{
			if (rows.Count == 0)
			{
				rows.Add(Row("none", MutedColor, "No grids yet. Your ships and stations show up here once they've been in range."));
			}
		}
		else if (grid == null)
		{
			rows.Add(Row("none", MutedColor, "No grids found yet."));
		}
		else if (rows.Count == 0)
		{
			rows.Add(Row("none", MutedColor, EmptyText(s_view)));
		}

		StringBuilder signature = new StringBuilder();
		foreach (RowData row in rows)
		{
			signature.Append(row.Key).Append('\u0001').Append(string.Join("\u0002", row.Texts)).Append(row.Color?.PackedValue ?? 0).Append('\u0003');
		}
		if (signature.ToString() == _rowsSignature)
		{
			return;
		}
		_rowsSignature = signature.ToString();

		// Rebuild, keeping the selection and scroll position.
		string selectedKey = _table.SelectedRow?.UserData as string;
		float scroll = _table.ScrollBar?.Value ?? 0f;
		_table.Clear();
		foreach (RowData data in rows)
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(data.Key);
			for (int i = 0; i < _table.ColumnsCount; i++)
			{
				// A cell draws every line of its text, so line breaks (from block or modded item names) would spill over the rows below.
				string text = (i < data.Texts.Length ? data.Texts[i] ?? "" : "").Replace("\r", "").Replace('\n', ' ');
				object sortValue = data.SortValues != null && i < data.SortValues.Length && data.SortValues[i] != null ? data.SortValues[i] : text;
				row.AddCell(new MyGuiControlTable.Cell(text, sortValue, null, data.Color));
			}
			_table.Add(row);
		}
		// Keeps the column the player clicked; does nothing until they click one.
		_table.Sort(switchSort: false);
		if (selectedKey != null)
		{
			SelectRow(selectedKey);
		}
		if (_table.ScrollBar != null)
		{
			_table.ScrollBar.Value = scroll;
		}
	}

	private bool SelectRow(string key)
	{
		for (int i = 0; i < _table.RowsCount; i++)
		{
			if (_table.GetRow(i).UserData as string == key)
			{
				_table.SelectedRowIndex = i;
				return true;
			}
		}
		return false;
	}

	private void SelectRowForItem(string itemKey)
	{
		if (!SelectRow("i:" + itemKey))
		{
			_table.SelectedRowIndex = null;
		}
	}

	private string SelectedKey => _table.SelectedRow?.UserData as string;

	private void OnRowSelected()
	{
		string key = SelectedKey;
		if (key == null)
		{
			return;
		}
		if (key.StartsWith("i:", StringComparison.Ordinal) && _itemCombo != null)
		{
			s_itemKey = key.Substring(2);
			int index = _itemKeys.IndexOf(s_itemKey);
			if (index >= 0)
			{
				_itemCombo.SelectItemByKey(index, sendEvent: false);
			}
		}
		else if (key.StartsWith("d:", StringComparison.Ordinal) && _pageCombo != null && TryParseDisplayKey(key, out long blockId, out int surface))
		{
			DisplayRule rule = Store.Display(blockId, surface);
			if (rule != null)
			{
				int page = Array.IndexOf(Displays.Pages, rule.Page);
				if (page >= 0)
				{
					s_page = page;
					_pageCombo.SelectItemByKey(page, sendEvent: false);
				}
				s_source = rule.SourceGrid;
				_sourceCombo.SelectItemByKey(Math.Max(0, _sourceKeys.IndexOf(rule.SourceGrid)), sendEvent: false);
			}
		}
	}

	private void OnRowDoubleClicked()
	{
		switch (s_view)
		{
		case View.AllGrids:
			OpenSelectedGrid();
			break;
		case View.Overview:
			ChangeSelectedSetting();
			break;
		case View.Blocks:
		case View.Production:
			EditSelectedBlock();
			break;
		case View.Displays:
			AssignDisplay();
			break;
		}
	}

	private static RowData Row(string key, Color? color, params string[] texts)
	{
		return new RowData { Key = key, Texts = texts, Color = color };
	}

	private static int CompareCells(MyGuiControlTable.Cell a, MyGuiControlTable.Cell b)
	{
		if (a.UserData is double x && b.UserData is double y)
		{
			return x.CompareTo(y);
		}
		return string.Compare(a.UserData?.ToString() ?? "", b.UserData?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseDisplayKey(string key, out long blockId, out int surface)
	{
		blockId = 0;
		surface = 0;
		string[] parts = key.Split(':');
		return parts.Length == 3 && long.TryParse(parts[1], out blockId) && int.TryParse(parts[2], out surface);
	}

	private static IMyEntity LiveEntity(long id)
	{
		IMyEntity entity = MyAPIGateway.Entities.GetEntityById(id);
		return entity == null || entity.Closed ? null : entity;
	}
}
