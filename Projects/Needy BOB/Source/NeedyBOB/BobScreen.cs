using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IngameEntity = VRage.Game.ModAPI.Ingame.IMyEntity;
using IngameSlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace NeedyBOB;

/// <summary>
/// The Needy BOB window: pick a group and a view, and the table below shows it live. It replaces the LCD
/// pages of the original script, plus a setup view for sorting blocks into groups.
/// </summary>
public class BobScreen : MyGuiScreenBase
{
	private enum View
	{
		Status,
		WeldTargets,
		GrindTargets,
		CollectTargets,
		MissingComponents,
		WeldPriority,
		GrindPriority,
		Setup
	}

	private static readonly string[] ViewNames = { "Status", "Weld targets", "Grind targets", "Collect targets", "Missing components", "Weld priority", "Grind priority", "Setup: groups" };

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

	// Remembered between openings of the window.
	private static string s_groupKey;

	private static View s_view = View.Status;

	private static string s_assignName = "";

	private MyGuiControlCombobox _groupCombo;

	private MyGuiControlCombobox _viewCombo;

	private MyGuiControlTable _table;

	private MyGuiControlLabel _status;

	private MyGuiControlButton _autoQueueButton;

	private MyGuiControlTextbox _groupNameBox;

	private readonly List<string> _comboKeys = new List<string>();

	private readonly Dictionary<string, IMyTerminalBlock> _rowBlocks = new Dictionary<string, IMyTerminalBlock>();

	private string _rowsSignature;

	private int _groupsVersion = -1;

	private int _frames;

	private bool _recreatePending;

	private string _message;

	private double _messageUntil;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color WarningColor => new Color(255, 190, 90);

	private static Color GoodColor => new Color(140, 230, 140);

	public BobScreen(string groupKey)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.86f))
	{
		if (groupKey != null)
		{
			s_groupKey = groupKey;
		}
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "NeedyBobScreen";
	}

	private static NeedyBobSession Session => NeedyBobSession.Instance;

	private BobGroup CurrentGroup => Session?.FindGroup(s_groupKey);

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		AddCaption("Needy BOB");

		Controls.Add(new MyGuiControlLabel(new Vector2(-0.42f, -0.325f), null, "Group", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_groupCombo = new MyGuiControlCombobox(new Vector2(-0.35f, -0.325f), new Vector2(0.43f, 0.04f), null, null, 12, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		_groupCombo.ItemSelected += OnGroupSelected;
		Controls.Add(_groupCombo);

		Controls.Add(new MyGuiControlLabel(new Vector2(0.105f, -0.325f), null, "View", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_viewCombo = new MyGuiControlCombobox(new Vector2(0.17f, -0.325f), new Vector2(0.25f, 0.04f), null, null, ViewNames.Length, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		for (int i = 0; i < ViewNames.Length; i++)
		{
			_viewCombo.AddItem(i, ViewNames[i], i, null, sort: false);
		}
		_viewCombo.SelectItemByKey((long)s_view, sendEvent: false);
		_viewCombo.ItemSelected += OnViewSelected;
		Controls.Add(_viewCombo);

		List<Column> columns = ColumnsFor(s_view);
		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, -0.29f),
			Size = new Vector2(0.84f, 0.52f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = columns.Count,
			VisibleRowsCount = 14
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
		Controls.Add(_table);

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.285f), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		_autoQueueButton = null;
		_groupNameBox = null;
		if (s_view == View.Setup)
		{
			_groupNameBox = new MyGuiControlTextbox(new Vector2(-0.315f, 0.37f), s_assignName, 40)
			{
				Size = new Vector2(0.19f, 0.045f)
			};
			_groupNameBox.TextChanged += box => s_assignName = box.Text;
			Controls.Add(_groupNameBox);
			AddButton(-0.105f, "Assign to group", AssignSelected);
			AddButton(0.105f, "Remove from groups", RemoveSelected);
		}
		else
		{
			_autoQueueButton = AddButton(-0.315f, "Auto-queue", ToggleAutoQueue);
			AddButton(-0.105f, "Queue now", QueueNow);
			AddButton(0.105f, "Rescan", Rescan);
		}
		AddButton(0.315f, "Close", () => CloseScreen());

		_rowsSignature = null;
		_groupsVersion = -1;
		RefreshAll();
	}

	private MyGuiControlButton AddButton(float x, string text, Action onClick)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, 0.37f), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) => onClick());
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
				// Rebuilt here rather than inside the combobox event, which is still iterating the controls.
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
			MyLog.Default.WriteLineAndConsole($"[NeedyBOB] Screen update: {ex}");
		}
		return result;
	}

	private void OnGroupSelected()
	{
		int index = (int)_groupCombo.GetSelectedKey();
		if (index >= 0 && index < _comboKeys.Count)
		{
			s_groupKey = _comboKeys[index];
			_rowsSignature = null;
			RefreshAll();
		}
	}

	private void OnViewSelected()
	{
		View view = (View)_viewCombo.GetSelectedKey();
		if (view != s_view)
		{
			s_view = view;
			_recreatePending = true;
		}
	}

	private void RefreshAll()
	{
		if (Session == null)
		{
			SetStatus("Needy BOB isn't running in this session.", WarningColor);
			return;
		}
		if (_groupsVersion != Session.GroupsVersion && !_groupCombo.IsOpen)
		{
			FillGroupCombo();
		}
		RefreshRows();
		BobGroup group = CurrentGroup;
		if (_autoQueueButton != null)
		{
			_autoQueueButton.Text = group == null ? "Auto-queue" : group.AutoQueue ? "Auto-queue: ON" : "Auto-queue: OFF";
			_autoQueueButton.Enabled = group != null;
		}
		if (_message != null && NeedyBobSession.Now < _messageUntil)
		{
			return;
		}
		_message = null;
		SetStatus(HintFor(group), null);
	}

	private void FillGroupCombo()
	{
		_groupsVersion = Session.GroupsVersion;
		_groupCombo.ClearItems();
		_comboKeys.Clear();
		foreach (BobGroup group in Session.Groups)
		{
			int systems = group.LiveSystems.Count();
			_groupCombo.AddItem(_comboKeys.Count, $"{group.Label}  ({systems} system{(systems == 1 ? "" : "s")})", _comboKeys.Count, null, sort: false);
			_comboKeys.Add(group.Key);
		}
		int selected = _comboKeys.IndexOf(s_groupKey);
		if (selected < 0 && _comboKeys.Count > 0)
		{
			selected = 0;
			s_groupKey = _comboKeys[0];
			_rowsSignature = null;
		}
		if (selected >= 0)
		{
			_groupCombo.SelectItemByKey(selected, sendEvent: false);
		}
	}

	private string HintFor(BobGroup group)
	{
		if (group == null)
		{
			return "No Build and Repair systems you can access are in range. Is the SKO Nanobot Build and Repair mod loaded?";
		}
		switch (s_view)
		{
		case View.MissingComponents:
			return group.AutoQueue ? "Auto-queue is on: missing components are queued in the group's assemblers every few seconds." : "Auto-queue is off. Queue now queues everything listed once.";
		case View.Setup:
			return "Select a block, type a group name (blank = Default), then Assign. Remove takes it out of every group.";
		case View.WeldTargets:
		case View.GrindTargets:
		case View.CollectTargets:
			return "Click a column header to sort. In multiplayer the mod only sends the first 24 targets to players.";
		default:
			return $"{group.LiveSystems.Count()} system(s), {group.Assemblers.Count(a => !a.Closed)} assembler(s). Updates live.";
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
		_messageUntil = NeedyBobSession.Now + 6.0;
		SetStatus(text, color);
	}

	private void ToggleAutoQueue()
	{
		BobGroup group = CurrentGroup;
		if (group == null)
		{
			return;
		}
		Session.SetAutoQueue(group, !group.AutoQueue);
		ShowMessage(group.AutoQueue ? $"Auto-queue on for {group.Name}." : $"Auto-queue off for {group.Name}.", group.AutoQueue ? GoodColor : (Color?)null);
		_rowsSignature = null;
		RefreshAll();
	}

	private void QueueNow()
	{
		BobGroup group = CurrentGroup;
		if (group == null)
		{
			return;
		}
		if (group.UsableAssemblers().Count == 0)
		{
			ShowMessage("This group has no assembler that is on, working, in assembly mode and accessible to you.", WarningColor);
			return;
		}
		int kinds = group.QueueMissing(NeedyBobSession.Now);
		ShowMessage(kinds > 0 ? $"Queued {kinds} kind(s) of component." : "Nothing new to queue. Anything queued in the last 15 s is waiting for the server to confirm it.", kinds > 0 ? GoodColor : (Color?)null);
		_rowsSignature = null;
		RefreshAll();
	}

	private void Rescan()
	{
		Session.RefreshNow();
		ShowMessage($"Found {Session.Groups.Count} group(s).");
		RefreshAll();
	}

	private IMyTerminalBlock SelectedBlock()
	{
		return _table.SelectedRow?.UserData is string key && _rowBlocks.TryGetValue(key, out IMyTerminalBlock block) ? block : null;
	}

	private void AssignSelected()
	{
		IMyTerminalBlock block = SelectedBlock();
		if (block == null)
		{
			ShowMessage("Select a block first.", WarningColor);
			return;
		}
		string name = (_groupNameBox?.Text ?? "").Trim();
		if (BobConfig.IsNoGroup(name))
		{
			RemoveSelected();
			return;
		}
		if (name.IndexOfAny(new[] { '[', ']', '\n', '=' }) >= 0)
		{
			ShowMessage("Group names can't contain [ ] or =.", WarningColor);
			return;
		}
		Session.AssignBlock(block, name);
		string shown = name.Length == 0 ? BobConfig.DefaultGroup : name;
		// Keep showing the same construct: follow the group the block went into when it's a Build and Repair system.
		if (block is IMyShipWelder)
		{
			BobGroup target = Session.Groups.FirstOrDefault(g => g.Systems.Contains(block));
			if (target != null)
			{
				s_groupKey = target.Key;
			}
		}
		ShowMessage($"{block.CustomName} is now in group {shown}.", GoodColor);
		_rowsSignature = null;
		RefreshAll();
	}

	private void RemoveSelected()
	{
		IMyTerminalBlock block = SelectedBlock();
		if (block == null)
		{
			ShowMessage("Select a block first.", WarningColor);
			return;
		}
		BobGroup previous = CurrentGroup;
		Session.AssignBlock(block, BobConfig.NoGroup);
		if (previous != null && Session.FindGroup(previous.Key) == null)
		{
			// The group is gone; show another one on the same construct if there is one.
			BobGroup sibling = Session.Groups.FirstOrDefault(g => g.TerminalSystem == previous.TerminalSystem);
			if (sibling != null)
			{
				s_groupKey = sibling.Key;
			}
		}
		ShowMessage($"{block.CustomName} is no longer in any group. Assign it to bring it back.", null);
		_rowsSignature = null;
		RefreshAll();
	}

	private static List<Column> ColumnsFor(View view)
	{
		switch (view)
		{
		case View.Status:
			return new List<Column>
			{
				new Column { Name = "", Width = 0.3f, Sortable = false },
				new Column { Name = "", Width = 0.7f, Sortable = false }
			};
		case View.WeldTargets:
		case View.GrindTargets:
			return new List<Column>
			{
				new Column { Name = "Block", Width = 0.32f },
				new Column { Name = "Grid", Width = 0.26f },
				new Column { Name = "Integrity", Width = 0.13f, RightAligned = true },
				new Column { Name = "Distance", Width = 0.13f, RightAligned = true },
				new Column { Name = "Now", Width = 0.16f }
			};
		case View.CollectTargets:
			return new List<Column>
			{
				new Column { Name = "Item", Width = 0.5f },
				new Column { Name = "Amount", Width = 0.25f, RightAligned = true },
				new Column { Name = "Distance", Width = 0.25f, RightAligned = true }
			};
		case View.MissingComponents:
			return new List<Column>
			{
				new Column { Name = "Component", Width = 0.25f },
				new Column { Name = "Missing", Width = 0.11f, RightAligned = true },
				new Column { Name = "Queued", Width = 0.11f, RightAligned = true },
				new Column { Name = "Built", Width = 0.11f, RightAligned = true },
				new Column { Name = "Auto-queue", Width = 0.42f }
			};
		case View.WeldPriority:
		case View.GrindPriority:
			return new List<Column>
			{
				new Column { Name = "Order", Width = 0.14f, RightAligned = true, Sortable = false },
				new Column { Name = "Block class", Width = 0.56f, Sortable = false },
				new Column { Name = "Enabled", Width = 0.3f, Sortable = false }
			};
		default:
			return new List<Column>
			{
				new Column { Name = "Block", Width = 0.34f },
				new Column { Name = "Type", Width = 0.2f },
				new Column { Name = "Group", Width = 0.24f },
				new Column { Name = "State", Width = 0.22f }
			};
		}
	}

	private void RefreshRows()
	{
		BobGroup group = CurrentGroup;
		_rowBlocks.Clear();
		List<RowData> rows = group == null ? new List<RowData>() : BuildRows(group);
		if (group == null)
		{
			rows.Add(Row("none", MutedColor, "No groups found."));
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
				string text = i < data.Texts.Length ? data.Texts[i] ?? "" : "";
				object sortValue = data.SortValues != null && i < data.SortValues.Length && data.SortValues[i] != null ? data.SortValues[i] : text;
				row.AddCell(new MyGuiControlTable.Cell(text, sortValue, null, data.Color));
			}
			_table.Add(row);
		}
		// Keeps the column the player clicked; does nothing until they click one, so the mod's own order shows first.
		_table.Sort(switchSort: false);
		if (selectedKey != null)
		{
			for (int i = 0; i < rows.Count; i++)
			{
				if (_table.GetRow(i).UserData as string == selectedKey)
				{
					_table.SelectedRowIndex = i;
					break;
				}
			}
		}
		if (_table.ScrollBar != null)
		{
			_table.ScrollBar.Value = scroll;
		}
	}

	private static string EmptyText(View view)
	{
		switch (view)
		{
		case View.WeldTargets:
			return "Nothing to weld.";
		case View.GrindTargets:
			return "Nothing to grind.";
		case View.CollectTargets:
			return "No floating items to collect.";
		case View.MissingComponents:
			return "Nothing missing.";
		default:
			return "Nothing to show.";
		}
	}

	private List<RowData> BuildRows(BobGroup group)
	{
		switch (s_view)
		{
		case View.Status:
			return StatusRows(group);
		case View.WeldTargets:
			return TargetRows(group, group.WeldTargets(), "Welding", BarApi.CurrentWeldTarget);
		case View.GrindTargets:
			return TargetRows(group, group.GrindTargets(), "Grinding", BarApi.CurrentGrindTarget);
		case View.CollectTargets:
			return CollectRows(group);
		case View.MissingComponents:
			return MissingRows(group);
		case View.WeldPriority:
			return PriorityRows(group.FirstSystem == null ? null : BarApi.WeldPriority(group.FirstSystem));
		case View.GrindPriority:
			return PriorityRows(group.FirstSystem == null ? null : BarApi.GrindPriority(group.FirstSystem));
		default:
			return SetupRows(group);
		}
	}

	private static RowData Row(string key, Color? color, params string[] texts)
	{
		return new RowData { Key = key, Texts = texts, Color = color };
	}

	private List<RowData> StatusRows(BobGroup group)
	{
		List<IMyShipWelder> systems = group.LiveSystems.ToList();
		IMyShipWelder first = systems.FirstOrDefault();
		List<RowData> rows = new List<RowData>();
		int working = systems.Count(s => s.IsWorking && s.IsFunctional);
		rows.Add(Row("online", working > 0 ? GoodColor : WarningColor, "Online", $"{working} of {systems.Count} system(s) working"));
		rows.Add(Row("weld", null, "Welding", TargetName(systems.Select(s => BarApi.CurrentWeldTarget(s)).FirstOrDefault(t => t != null))));
		rows.Add(Row("toweld", null, "Blocks to weld", group.WeldTargets().Count.ToString()));
		rows.Add(Row("grind", null, "Grinding", TargetName(systems.Select(s => BarApi.CurrentGrindTarget(s)).FirstOrDefault(t => t != null))));
		rows.Add(Row("togrind", null, "Blocks to grind", group.GrindTargets().Count.ToString()));
		rows.Add(Row("floating", null, "Floating items", group.CollectTargets().Count.ToString()));
		int missing = group.MissingComponents().Count(m => m.Value > 0);
		rows.Add(Row("missing", missing > 0 ? WarningColor : (Color?)null, "Missing kinds", missing.ToString()));
		if (first != null)
		{
			rows.Add(Row("search", null, "Search mode", BarApi.SearchModeName(BarApi.SearchMode(first))));
			rows.Add(Row("work", null, "Work mode", BarApi.WorkModeName(BarApi.WorkMode(first))));
			rows.Add(Row("build", null, "Build projected", YesNo(BarApi.AllowBuild(first))));
			rows.Add(Row("ignore", null, "Use ignore color", YesNo(BarApi.UseIgnoreColor(first))));
			rows.Add(Row("script", null, "Script controlled", YesNo(BarApi.ScriptControlled(first))));
		}
		rows.Add(Row("queue", group.AutoQueue ? GoodColor : (Color?)null, "Auto-queue", AutoQueueText(group)));
		foreach (IMyShipWelder system in systems)
		{
			rows.Add(Row("sys" + system.EntityId, system.IsWorking ? null : WarningColor, "System", $"{system.CustomName}: {SystemState(system)}"));
		}
		for (int i = 0; i < group.Log.Count; i++)
		{
			rows.Add(Row("log" + i, MutedColor, i == 0 ? "Recent" : "", group.Log[i]));
		}
		return rows;
	}

	private static string AutoQueueText(BobGroup group)
	{
		int assemblers = group.Assemblers.Count(a => !a.Closed);
		if (assemblers == 0)
		{
			return group.IsDefault ? "No assemblers on this construct" : $"No assemblers in group {group.Name}";
		}
		int usable = group.UsableAssemblers().Count;
		string count = usable == assemblers ? $"{assemblers} assembler(s)" : $"{usable} of {assemblers} assembler(s) usable";
		return (group.AutoQueue ? "On, " : "Off, ") + count;
	}

	private static string SystemState(IMyShipWelder system)
	{
		if (!system.IsFunctional)
		{
			return "damaged";
		}
		if (!system.Enabled)
		{
			return "off";
		}
		if (!system.IsWorking)
		{
			return "no power";
		}
		IngameSlimBlock weld = BarApi.CurrentWeldTarget(system);
		if (weld != null)
		{
			return "welding " + TargetName(weld);
		}
		IngameSlimBlock grind = BarApi.CurrentGrindTarget(system);
		if (grind != null)
		{
			return "grinding " + TargetName(grind);
		}
		return "idle";
	}

	private List<RowData> TargetRows(BobGroup group, List<IngameSlimBlock> targets, string activeText, Func<IMyTerminalBlock, IngameSlimBlock> current)
	{
		HashSet<IngameSlimBlock> active = new HashSet<IngameSlimBlock>(group.LiveSystems.Select(s => current(s)).Where(t => t != null));
		Vector3D origin = group.FirstSystem?.GetPosition() ?? Vector3D.Zero;
		List<RowData> rows = new List<RowData>();
		int index = 0;
		foreach (IngameSlimBlock target in targets)
		{
			if (!(target is IMySlimBlock slim))
			{
				continue;
			}
			slim.ComputeWorldCenter(out Vector3D center);
			double distance = Vector3D.Distance(origin, center);
			bool projected = slim.CubeGrid is MyCubeGrid grid && grid.Projector != null;
			double integrity = slim.MaxIntegrity > 0f ? slim.Integrity / slim.MaxIntegrity : 0.0;
			bool isActive = active.Contains(target);
			rows.Add(new RowData
			{
				Key = "t" + (slim.FatBlock?.EntityId.ToString() ?? $"{slim.CubeGrid?.EntityId}:{slim.Position}") + ":" + index++,
				Texts = new[] { TargetName(target), slim.CubeGrid?.CustomName ?? "", projected ? "Projected" : $"{integrity:P0}", FormatDistance(distance), isActive ? activeText : "" },
				SortValues = new object[] { null, null, projected ? -1.0 : integrity, distance, isActive ? "0" : "1" },
				Color = isActive ? GoodColor : (Color?)null
			});
		}
		return rows;
	}

	private List<RowData> CollectRows(BobGroup group)
	{
		Vector3D origin = group.FirstSystem?.GetPosition() ?? Vector3D.Zero;
		List<RowData> rows = new List<RowData>();
		foreach (IngameEntity target in group.CollectTargets())
		{
			if (!(target is IMyEntity entity) || entity.Closed)
			{
				continue;
			}
			string name = entity.DisplayName;
			double amount = 0.0;
			if (entity is MyFloatingObject floating)
			{
				name = BobGroup.ComponentName(floating.Item.Content.GetId());
				amount = (double)floating.Item.Amount;
			}
			double distance = Vector3D.Distance(origin, entity.WorldMatrix.Translation);
			rows.Add(new RowData
			{
				Key = "c" + entity.EntityId,
				Texts = new[] { string.IsNullOrEmpty(name) ? "Object" : name, amount > 0 ? amount.ToString("N0") : "", FormatDistance(distance) },
				SortValues = new object[] { null, amount, distance }
			});
		}
		return rows;
	}

	private List<RowData> MissingRows(BobGroup group)
	{
		List<IMyAssembler> assemblers = group.Assemblers.Where(a => !a.Closed).ToList();
		bool canQueue = group.UsableAssemblers().Count > 0;
		double now = NeedyBobSession.Now;
		List<RowData> rows = new List<RowData>();
		foreach (KeyValuePair<MyDefinitionId, int> item in group.MissingComponents().Where(m => m.Value > 0).OrderBy(m => BobGroup.ComponentName(m.Key)))
		{
			int queued = AssemblerQueue.QueuedAmount(assemblers, item.Key);
			int built = AssemblerQueue.OutputAmount(assemblers, item.Key);
			string state;
			Color? color = null;
			if (group.Notes.TryGetValue(item.Key, out string note) && group.WaitSeconds(item.Key, now) > 0)
			{
				state = note;
				color = note.StartsWith("Queued") ? GoodColor : WarningColor;
			}
			else if (queued + built >= item.Value)
			{
				state = "Enough queued";
				color = GoodColor;
			}
			else if (!canQueue)
			{
				state = assemblers.Count == 0 ? "No assemblers in this group" : "No usable assembler (off, damaged or disassembling)";
				color = WarningColor;
			}
			else
			{
				state = group.AutoQueue ? "Will be queued" : "Auto-queue off";
			}
			rows.Add(new RowData
			{
				Key = "m" + item.Key,
				Texts = new[] { BobGroup.ComponentName(item.Key), item.Value.ToString("N0"), queued.ToString("N0"), built.ToString("N0"), state },
				SortValues = new object[] { null, (double)item.Value, (double)queued, (double)built, null },
				Color = color
			});
		}
		return rows;
	}

	private static List<RowData> PriorityRows(List<string> entries)
	{
		List<RowData> rows = new List<RowData>();
		if (entries == null)
		{
			return rows;
		}
		int order = 0;
		foreach (string entry in entries)
		{
			if (BarApi.TryParsePriority(entry, out string className, out bool enabled))
			{
				order++;
				rows.Add(Row("p" + order, enabled ? null : MutedColor, order.ToString(), className, enabled ? "Yes" : "No"));
			}
		}
		return rows;
	}

	private List<RowData> SetupRows(BobGroup group)
	{
		List<RowData> rows = new List<RowData>();
		foreach (IMyTerminalBlock block in group.ConstructBlocks.Where(b => !b.Closed))
		{
			bool isSystem = block is IMyShipWelder;
			string name = BobConfig.GetGroup(block);
			string groupText = BobConfig.IsNoGroup(name) ? "(none)" : BobConfig.IsDefaultGroup(name) ? (isSystem ? BobConfig.DefaultGroup : "Default (unassigned)") : name;
			string state = !block.IsFunctional ? "Damaged" : block is IMyFunctionalBlock functional && !functional.Enabled ? "Off" : !block.IsWorking ? "No power" : "Working";
			if (block is IMyAssembler assembler && assembler.Mode != MyAssemblerMode.Assembly)
			{
				state += ", disassembling";
			}
			string key = "b" + block.EntityId;
			_rowBlocks[key] = block;
			bool inThisGroup = isSystem ? group.Systems.Contains(block) : group.Assemblers.Contains(block);
			rows.Add(new RowData
			{
				Key = key,
				Texts = new[] { block.CustomName, isSystem ? "Build and Repair" : "Assembler", groupText, state },
				Color = inThisGroup ? GoodColor : BobConfig.IsNoGroup(name) ? MutedColor : (Color?)null
			});
		}
		return rows.OrderBy(r => r.Texts[1] == "Assembler").ThenBy(r => r.Texts[0], StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string TargetName(IngameSlimBlock target)
	{
		if (target == null)
		{
			return "-";
		}
		if (target.FatBlock is Sandbox.ModAPI.Ingame.IMyTerminalBlock terminal)
		{
			return terminal.CustomName;
		}
		if (target is IMySlimBlock slim && slim.BlockDefinition != null && !string.IsNullOrEmpty(slim.BlockDefinition.DisplayNameText))
		{
			return slim.BlockDefinition.DisplayNameText;
		}
		return target.BlockDefinition.SubtypeName;
	}

	private static string YesNo(bool? value)
	{
		return value == null ? "?" : value.Value ? "Yes" : "No";
	}

	private static string FormatDistance(double metres)
	{
		return metres >= 1000.0 ? $"{metres / 1000.0:0.0} km" : $"{metres:0} m";
	}

	private static int CompareCells(MyGuiControlTable.Cell a, MyGuiControlTable.Cell b)
	{
		if (a.UserData is double x && b.UserData is double y)
		{
			return x.CompareTo(y);
		}
		return string.Compare(a.UserData?.ToString() ?? "", b.UserData?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
	}
}
