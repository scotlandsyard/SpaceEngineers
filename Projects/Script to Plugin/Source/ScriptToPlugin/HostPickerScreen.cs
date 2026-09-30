using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ScriptToPlugin;

/// <summary>
/// Picks the block a script runs on (its Me). Lists the loaded constructs near the player that have blocks the
/// player can access, then the blocks of the chosen construct.
/// </summary>
public class HostPickerScreen : MyGuiScreenBase
{
	private class Construct
	{
		public IMyGridTerminalSystem Terminal;

		public MyCubeGrid MainGrid;

		public double Distance;
	}

	private readonly Action<MyTerminalBlock> _onPicked;

	private readonly long _currentHostId;

	private readonly List<Construct> _constructs = new List<Construct>();

	private readonly Dictionary<long, MyTerminalBlock> _rowBlocks = new Dictionary<long, MyTerminalBlock>();

	private MyGuiControlCombobox _gridCombo;

	private MyGuiControlTextbox _searchBox;

	private MyGuiControlTable _table;

	private MyGuiControlLabel _status;

	private string _search = "";

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color GoodColor => new Color(140, 230, 140);

	public HostPickerScreen(MyTerminalBlock currentHost, Action<MyTerminalBlock> onPicked)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.72f, 0.8f))
	{
		_onPicked = onPicked;
		_currentHostId = currentHost?.EntityId ?? 0;
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		CloseButtonEnabled = true;
		FindConstructs(currentHost);
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "ScriptToPluginHostPicker";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		AddCaption("Choose the host block");

		Controls.Add(new MyGuiControlLabel(new Vector2(-0.33f, -0.3f), null, "Grid", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_gridCombo = new MyGuiControlCombobox(new Vector2(-0.24f, -0.3f), new Vector2(0.57f, 0.04f), null, null, 12, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		for (int i = 0; i < _constructs.Count; i++)
		{
			_gridCombo.AddItem(i, $"{_constructs[i].MainGrid.DisplayName}  ({FormatDistance(_constructs[i].Distance)})", i, null, sort: false);
		}
		if (_constructs.Count > 0)
		{
			_gridCombo.SelectItemByKey(0, sendEvent: false);
		}
		_gridCombo.ItemSelected += FillTable;
		Controls.Add(_gridCombo);

		Controls.Add(new MyGuiControlLabel(new Vector2(-0.33f, -0.245f), null, "Search", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_searchBox = new MyGuiControlTextbox(new Vector2(0.045f, -0.245f), _search, 60)
		{
			Size = new Vector2(0.57f, 0.04f)
		};
		_searchBox.TextChanged += box =>
		{
			_search = box.Text ?? "";
			FillTable();
		};
		Controls.Add(_searchBox);

		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.21f),
			Size = new Vector2(0.66f, 0.46f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = 3,
			VisibleRowsCount = 12
		};
		_table.SetCustomColumnWidths(new[] { 0.5f, 0.35f, 0.15f });
		_table.SetColumnName(0, new StringBuilder("Block"));
		_table.SetColumnName(1, new StringBuilder("Type"));
		_table.SetColumnName(2, new StringBuilder("Screens"));
		_table.SetColumnAlign(2, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.SetHeaderColumnAlign(2, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		_table.ItemDoubleClicked += (table, args) => UseSelected();
		Controls.Add(_table);

		_status = new MyGuiControlLabel(new Vector2(-0.33f, 0.27f), null, "", null, 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.11f, "Use as host", UseSelected);
		AddButton(0.11f, "Cancel", () => CloseScreen());
		FillTable();
	}

	private void AddButton(float x, string text, Action onClick)
	{
		Controls.Add(new MyGuiControlButton(new Vector2(x, 0.33f), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ => onClick()));
	}

	/// <summary>
	/// The constructs to offer, one per terminal system: the one with the current host first, then the one the
	/// player controls, then the rest by distance.
	/// </summary>
	private void FindConstructs(MyTerminalBlock currentHost)
	{
		Vector3D position = MyAPIGateway.Session.Player.GetPosition();
		Dictionary<IMyGridTerminalSystem, Construct> byTerminal = new Dictionary<IMyGridTerminalSystem, Construct>();
		MyAPIGateway.Entities.GetEntities(null, delegate (IMyEntity entity)
		{
			if (entity is MyCubeGrid grid && !grid.Closed && grid.Projector == null && !grid.IsPreview && grid.Physics != null)
			{
				IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
				if (terminal != null)
				{
					double distance = Math.Sqrt(grid.PositionComp.WorldAABB.DistanceSquared(position));
					if (!byTerminal.TryGetValue(terminal, out Construct construct))
					{
						construct = new Construct { Terminal = terminal, MainGrid = grid, Distance = distance };
						byTerminal[terminal] = construct;
					}
					construct.Distance = Math.Min(construct.Distance, distance);
					if (grid.BlocksCount > construct.MainGrid.BlocksCount)
					{
						construct.MainGrid = grid;
					}
				}
			}
			return false;
		});

		IMyGridTerminalSystem hostTerminal = currentHost?.CubeGrid == null ? null : MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(currentHost.CubeGrid);
		MyTerminalBlock controlled = ScriptSession.ControlledBlock();
		IMyGridTerminalSystem controlledTerminal = controlled?.CubeGrid == null ? null : MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(controlled.CubeGrid);
		List<Sandbox.ModAPI.IMyTerminalBlock> blocks = new List<Sandbox.ModAPI.IMyTerminalBlock>();
		foreach (Construct construct in byTerminal.Values
			.OrderBy(c => c.Terminal == hostTerminal ? 0 : c.Terminal == controlledTerminal ? 1 : 2)
			.ThenBy(c => c.Distance))
		{
			construct.Terminal.GetBlocks(blocks);
			if (blocks.Any(b => b.HasLocalPlayerAccess()))
			{
				_constructs.Add(construct);
			}
		}
	}

	private void FillTable()
	{
		_table.Clear();
		_rowBlocks.Clear();
		int index = (int)_gridCombo.GetSelectedKey();
		if (index < 0 || index >= _constructs.Count)
		{
			_status.Text = "No grids with blocks you can use are loaded near you.";
			return;
		}
		List<Sandbox.ModAPI.IMyTerminalBlock> blocks = new List<Sandbox.ModAPI.IMyTerminalBlock>();
		_constructs[index].Terminal.GetBlocks(blocks);
		string search = _search.Trim();
		int selectIndex = -1;
		foreach (MyTerminalBlock block in blocks
			.OfType<MyTerminalBlock>()
			.Where(b => !b.Closed && ((Sandbox.ModAPI.IMyTerminalBlock)b).HasLocalPlayerAccess())
			.Where(b => search.Length == 0 || b.CustomName.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
			// Programmable blocks and blocks with screens first: they make the most natural hosts.
			.OrderBy(b => b is Sandbox.ModAPI.IMyProgrammableBlock ? 0 : b is Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider provider && provider.SurfaceCount > 0 ? 1 : 2)
			.ThenBy(b => b.CustomName.ToString(), StringComparer.OrdinalIgnoreCase))
		{
			int surfaces = block is Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider screens ? screens.SurfaceCount : 0;
			bool isCurrent = block.EntityId == _currentHostId;
			Color? color = isCurrent ? GoodColor : (Color?)null;
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(block.EntityId);
			row.AddCell(new MyGuiControlTable.Cell(block.CustomName.ToString() + (isCurrent ? "  (current host)" : ""), null, null, color));
			row.AddCell(new MyGuiControlTable.Cell(block.BlockDefinition?.DisplayNameText ?? "", null, null, color));
			row.AddCell(new MyGuiControlTable.Cell(surfaces > 0 ? surfaces.ToString() : "-", null, null, surfaces > 0 ? color : MutedColor));
			_table.Add(row);
			_rowBlocks[block.EntityId] = block;
			if (isCurrent)
			{
				selectIndex = _table.RowsCount - 1;
			}
		}
		if (selectIndex >= 0)
		{
			_table.SelectedRowIndex = selectIndex;
		}
		_status.Text = _table.RowsCount == 0
			? "No matching blocks you can access on this grid."
			: "Me is this block: its grid, position and screens. Pick one with screens if the script draws on Me.";
	}

	private void UseSelected()
	{
		if (!(_table.SelectedRow?.UserData is long id) || !_rowBlocks.TryGetValue(id, out MyTerminalBlock block) || block.Closed)
		{
			_status.Text = "Select a block first.";
			return;
		}
		CloseScreen();
		try
		{
			_onPicked(block);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Host picked: {ex}");
		}
	}

	private static string FormatDistance(double metres)
	{
		return metres >= 1000.0 ? $"{metres / 1000.0:0.0} km" : $"{metres:0} m";
	}
}
