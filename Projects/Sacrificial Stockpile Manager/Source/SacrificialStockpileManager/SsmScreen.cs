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
using TimShared;

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
		Types,
		Production,
		Machine,
		Refining,
		Displays,
		Log,
		Help
	}

	// In the same order as View.
	private static readonly string[] ViewNames = { "All grids", "Overview", "Items & quotas", "Blocks", "Block settings", "Block type limits", "Production","Production details", "Refinery priority", "Displays (LCD)", "Log", "Help" };

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

	/// <summary>Text typed in the Find box; filters the item list and the item rows.</summary>
	private static string s_search = "";

	/// <summary>The assembler or refinery shown in Production details.</summary>
	private static long s_machineId;

	/// <summary>The block type shown in Block type limits.</summary>
	private static BlockKind s_typeKind = BlockKind.Reactor;

	private static int s_page;

	private static long s_source;

	private MyGuiControlCombobox _gridCombo;

	private MyGuiControlCombobox _viewCombo;

	private MyGuiControlCombobox _blockCombo;

	private MyGuiControlCombobox _roleCombo;

	private MyGuiControlCombobox _priorityCombo;

	private MyGuiControlCombobox _modeCombo;

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

	/// <summary>Buttons that need the grid to be loaded.</summary>
	private readonly List<MyGuiControlButton> _liveButtons = new List<MyGuiControlButton>();

	/// <summary>Space between a right-aligned column's text and the column's right edge (the table's own default margin).</summary>
	private const float ColumnGap = 0.01f;

	/// <summary>Cell margin for right-aligned cells: with the column's -ColumnGap it leaves the text ColumnGap from the edge, like the header.</summary>
	private static readonly Thickness RightCellMargin = new Thickness(2f * ColumnGap, 0f, 0f, 0f);

	private bool[] _rightAligned = new bool[0];

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
		PluginSwitcher.AddSwitcher(this, AddCaption("Sacrificial Stockpile Manager"));
		_autocraftButton = null;
		_liveButtons.Clear();
		_acceptBoxes.Clear();
		_blockCombo = null;
		_roleCombo = null;
		_priorityCombo = null;
		_modeCombo = null;
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
		else if (s_view == View.Machine)
		{
			CreateMachineControls();
			tableTop = -0.29f;
			rows = 14;
		}
		else if (s_view == View.Types)
		{
			CreateTypeControls();
			tableTop = -0.29f;
			rows = 12;
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
		_rightAligned = columns.Select(c => c.RightAligned).ToArray();
		for (int i = 0; i < columns.Count; i++)
		{
			_table.SetColumnName(i, new StringBuilder(columns[i].Name));
			if (columns[i].RightAligned)
			{
				_table.SetColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
				_table.SetHeaderColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
				// The table always adds the column's left margin to a header, even a right-aligned one, but subtracts
				// it from a right-aligned cell. A negative left margin pulls the header in from the column's edge, and
				// each cell's own margin (see RightCellMargin) puts the cell text at the same place.
				_table.SetHeaderColumnMargin(i, new Thickness(-ColumnGap, 0f, ColumnGap, 0f));
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

		if (s_view == View.Items || s_view == View.Block || s_view == View.Types)
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
			// Remove from list lives in All grids; Help is in the View list.
			AddButton(-0.315f, "Change setting", ChangeSelectedSetting);
			_liveButtons.Add(AddButton(-0.105f, "Sort now", () => StartOneShot(unload: false)));
			_liveButtons.Add(AddButton(0.105f, "Unload docked ships", () => StartOneShot(unload: true)));
			AddButton(0.315f, "GPS marker", MarkGps);
			break;
		case View.Types:
			AddButton(-0.315f, "Set minimum", () => SetTypeLimit(minimum: true));
			AddButton(-0.105f, "Set maximum", () => SetTypeLimit(minimum: false));
			AddButton(0.105f, "Clear limits", ClearTypeLimits);
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Items:
			AddButton(-0.315f, "Set quota", () => SetQuota(minimum: true));
			AddButton(-0.105f, "Set maximum", () => SetQuota(minimum: false));
			AddButton(0.105f, "Clear quota", ClearQuota);
			_autocraftButton = AddButton(0.315f, "Autocraft", () => ToggleGridSetting("autocraft"));
			break;
		case View.Blocks:
			AddButton(-0.315f, "Edit block", EditSelectedBlock);
			_liveButtons.Add(AddButton(-0.105f, "Turn on / off", ToggleSelectedBlock));
			_liveButtons.Add(AddButton(0.105f, "Rescan", Rescan));
			AddButton(0.315f, "Help", ShowHelp);
			break;
		case View.Production:
			AddButton(-0.315f, "Details", ShowSelectedMachine);
			AddButton(-0.105f, "Edit block", EditSelectedBlock);
			_liveButtons.Add(AddButton(0.105f, "Turn on / off", ToggleSelectedBlock));
			_liveButtons.Add(AddButton(0.315f, "Assembler mode", CycleAssemblerMode));
			break;
		case View.Machine:
			_liveButtons.Add(AddButton(-0.315f, "Remove from queue", RemoveSelectedQueueItem));
			_liveButtons.Add(AddButton(-0.105f, "Turn on / off", ToggleMachine));
			AddButton(0.105f, "Edit block", () =>
			{
				s_blockId = s_machineId;
				SwitchView(View.Block);
			});
			AddButton(0.315f, "Back", () => SwitchView(View.Production));
			break;
		case View.Refining:
			AddButton(-0.315f, "Raise priority", () => MoveOre(-1));
			AddButton(-0.105f, "Lower priority", () => MoveOre(+1));
			AddButton(0.105f, "No priority", RemoveOrePriority);
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
		return view == View.Items || view == View.Block || view == View.Types || view == View.Displays;
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
		_blockCombo = AddCombo(-0.36f, Row2Y, 0.33f, 14);
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

		AddLabel(0.0f, Row2Y, "Role");
		_roleCombo = AddCombo(0.055f, Row2Y, 0.2f, RoleNames.Length);
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

		AddLabel(0.265f, Row2Y, "Priority");
		_priorityCombo = AddCombo(0.345f, Row2Y, 0.075f, 10, "Fill priority: among storage that accepts an item equally well, a higher number fills first");
		for (int i = 0; i <= 9; i++)
		{
			_priorityCombo.AddItem(i, i.ToString(), i, null, sort: false);
		}
		_priorityCombo.SelectItemByKey(Math.Max(0, Math.Min(9, rules?.Priority ?? 0)), sendEvent: false);
		_priorityCombo.ItemSelected += OnPrioritySelected;
		_priorityCombo.Enabled = block != null;

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

	/// <summary>Production block picker at the top of Production details.</summary>
	private void CreateMachineControls()
	{
		GridSnapshot grid = Grid;
		List<BlockSnapshot> machines = grid == null ? new List<BlockSnapshot>() : grid.Blocks.Where(b => Construct.IsProductionKind(b.Kind)).OrderBy(b => b.Kind).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (machines.Count > 0 && !machines.Any(b => b.Id == s_machineId))
		{
			s_machineId = machines[0].Id;
		}
		AddLabel(Left, Row2Y, "Block");
		_blockCombo = AddCombo(-0.36f, Row2Y, 0.48f, 14);
		_blockKeys.Clear();
		foreach (BlockSnapshot machine in machines)
		{
			_blockCombo.AddItem(_blockKeys.Count, $"{machine.Name}  ({machine.Type})", _blockKeys.Count, null, sort: false);
			_blockKeys.Add(machine.Id);
		}
		if (_blockKeys.Contains(s_machineId))
		{
			_blockCombo.SelectItemByKey(_blockKeys.IndexOf(s_machineId), sendEvent: false);
		}
		_blockCombo.ItemSelected += () =>
		{
			int index = (int)_blockCombo.GetSelectedKey();
			if (!_suppressEvents && index >= 0 && index < _blockKeys.Count)
			{
				s_machineId = _blockKeys[index];
				UpdateModeCombo();
				_rowsSignature = null;
				RefreshAll();
			}
		};

		// The assembler mode shared with our other plugins.
		AddLabel(0.135f, Row2Y, "Mode");
		_modeCombo = AddCombo(0.2f, Row2Y, 0.22f, 3, "Assembler mode, shared with our other plugins: Main takes autocraft orders, Co-op helps a Main assembler, Manual (the default) is never used by any of them.");
		_modeCombo.AddItem((long)TimShared.AssemblerMode.Main, "Main", 0, null, sort: false);
		_modeCombo.AddItem((long)TimShared.AssemblerMode.Coop, "Co-op", 1, null, sort: false);
		_modeCombo.AddItem((long)TimShared.AssemblerMode.Manual, "Manual", 2, null, sort: false);
		UpdateModeCombo();
		_modeCombo.ItemSelected += () =>
		{
			if (!_suppressEvents)
			{
				SetAssemblerMode(s_machineId, (TimShared.AssemblerMode)_modeCombo.GetSelectedKey());
			}
		};
	}

	/// <summary>Shows the selected machine's assembler mode; disabled for anything that isn't a loaded assembler of yours.</summary>
	private void UpdateModeCombo()
	{
		if (_modeCombo == null)
		{
			return;
		}
		bool wasSuppressed = _suppressEvents;
		_suppressEvents = true;
		try
		{
			IMyAssembler assembler = LiveEntity(s_machineId) as IMyAssembler;
			BlockSnapshot block = Grid?.Block(s_machineId);
			TimShared.AssemblerMode mode = assembler != null ? TimShared.AssemblerModes.Get(assembler)
				: block != null && Enum.TryParse(block.AssemblerMode, out TimShared.AssemblerMode saved) ? saved : TimShared.AssemblerMode.Unset;
			_modeCombo.SelectItemByKey((long)(mode == TimShared.AssemblerMode.Unset ? TimShared.AssemblerMode.Manual : mode), sendEvent: false);
			_modeCombo.Enabled = assembler != null && assembler.HasLocalPlayerAccess() && block?.NotYours != true;
		}
		finally
		{
			_suppressEvents = wasSuppressed;
		}
	}

	/// <summary>Find box, item picker and amount box, used by Items & quotas and Block settings.</summary>
	private void CreateItemEditor()
	{
		AddLabel(Left, EditorY, "Find");
		MyGuiControlTextbox searchBox = new MyGuiControlTextbox(new Vector2(-0.375f, EditorY), s_search, 30)
		{
			Size = new Vector2(0.11f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		searchBox.TextChanged += box =>
		{
			s_search = box.Text ?? "";
			FillItemCombo();
			_rowsSignature = null;
			RefreshRows();
		};
		Controls.Add(searchBox);

		_itemCombo = AddCombo(-0.255f, EditorY, 0.245f, 14);
		FillItemCombo();
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

	/// <summary>Fills the item picker with every item matching the Find box, keeping the picked item selected if it's still there.</summary>
	private void FillItemCombo()
	{
		if (_itemCombo == null)
		{
			return;
		}
		_itemCombo.ClearItems();
		_itemKeys.Clear();
		IEnumerable<string> keys = Items.Catalog;
		GridSnapshot grid = Grid;
		if (grid != null)
		{
			// Modded or unusual items that exist on the grid but aren't in the catalog.
			keys = keys.Concat(grid.Totals.Keys.Where(k => !Items.Catalog.Contains(k)));
		}
		foreach (string key in keys.Where(MatchesSearch))
		{
			_itemCombo.AddItem(_itemKeys.Count, $"{Items.Name(key)}  ({Items.CategoryShort[(int)Items.Category(key)]})", _itemKeys.Count, null, sort: false);
			_itemKeys.Add(key);
		}
		int selected = s_itemKey == null ? -1 : _itemKeys.IndexOf(s_itemKey);
		if (selected < 0 && _itemKeys.Count > 0 && s_search.Trim().Length > 0)
		{
			// Typing in Find picks the first match, so the buttons act on what's shown.
			selected = 0;
			s_itemKey = _itemKeys[0];
		}
		if (selected >= 0)
		{
			_itemCombo.SelectItemByKey(selected, sendEvent: false);
		}
	}

	/// <summary>True when the item's name, category or key contains the Find text (or Find is empty).</summary>
	private static bool MatchesSearch(string key)
	{
		string search = s_search.Trim();
		if (search.Length == 0)
		{
			return true;
		}
		return Items.Name(key).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
			|| key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
			|| Items.CategoryNames[(int)Items.Category(key)].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
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

	private MyGuiControlCombobox AddCombo(float x, float y, float width, int openItems, string toolTip = null)
	{
		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(x, y), new Vector2(width, 0.04f), null, null, openItems, null, false, toolTip, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
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
			return "Double-click a setting to switch it. Sort now runs everything once, even with Automation off." + offline;
		case View.Types:
			return "Limits for every block of this type on the grid (not docked ships). A block's own limit for the item wins." + offline;
		case View.Items:
			return "Pick an item, type an amount: Set quota to keep at least that many (autocraft), Set maximum to disassemble above it." + offline;
		case View.Blocks:
			return "Double-click a block to edit its role, what it accepts and its stock limits." + offline;
		case View.Block:
		{
			BlockSnapshot block = grid.Block(s_blockId);
			string volume = block != null && block.MaxVolume > 0.0 ? $"{Items.Litres(block.Volume)} used of {Items.Litres(block.MaxVolume)} ({block.Fill:P0}), {Items.Litres(block.MaxVolume - block.Volume)} free. " : "";
			return volume + "Pick an item, type an amount, set a minimum or maximum. Blank clears it.";
		}
		case View.Production:
			return live ? "Double-click an assembler or refinery for its queue and what it's missing. Assembler mode steps Manual (default) > Main > Co-op; autocraft only uses Main and Co-op." : "Last known state." + offline;
		case View.Machine:
			return live ? "Queue first, then the materials the whole queue needs. Missing = what the grid doesn't have." : "Not loaded: queue details show while the grid is in range.";
		case View.Refining:
			return "Refineries work on ores with a priority first, in this order. Double-click or Raise priority to add one. Needs Automation.";
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
		if ((s_view == View.Items || s_view == View.Block || s_view == View.Types) && s_search.Trim().Length > 0)
		{
			rows = rows.Where(r => !r.Key.StartsWith("i:", StringComparison.Ordinal) || MatchesSearch(r.Key.Substring(2))).ToList();
		}
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
				MyGuiControlTable.Cell cell = new MyGuiControlTable.Cell(text, sortValue, null, data.Color);
				if (i < _rightAligned.Length && _rightAligned[i])
				{
					cell.Margin = RightCellMargin;
				}
				row.AddCell(cell);
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
			EditSelectedBlock();
			break;
		case View.Production:
			ShowSelectedMachine();
			break;
		case View.Refining:
			MoveOre(-1);
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
