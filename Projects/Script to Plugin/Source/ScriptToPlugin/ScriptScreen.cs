using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Game.Screens;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using TimShared;

namespace ScriptToPlugin;

/// <summary>
/// The Script to Plugin window: the list of scripts on the left, the selected script's state and output on the
/// right, and the buttons a programmable block's terminal would have below.
/// </summary>
public class ScriptScreen : MyGuiScreenBase
{
	private const int RefreshFrames = 30;

	/// <summary>The template a new programmable block starts with.</summary>
	private const string NewScriptTemplate = "public Program()\n{\n    // Runs once when the script starts. Set Runtime.UpdateFrequency here to run on its own.\n}\n\npublic void Save()\n{\n    // Runs when the world is saved or the script is stopped. Put what to keep in Storage.\n}\n\npublic void Main(string argument, UpdateType updateSource)\n{\n    // Runs when the script is run.\n}\n";

	// Remembered between openings of the window.
	private static string s_selectedId;

	private static bool s_showHelp;

	private static string s_queuedMessage;

	private static Color? s_queuedColor;

	private MyGuiControlTable _table;

	private MyGuiControlMultilineText _output;

	private MyGuiControlLabel _outputCaption;

	private MyGuiControlLabel _status;

	private MyGuiControlTextbox _argumentBox;

	private MyGuiControlButton _onOffButton;

	private MyGuiControlButton _helpButton;

	private MyGuiControlTextbox _nameBox;

	private string _rowsSignature;

	private string _outputSignature;

	/// <summary>Which script (or the help page) the output panel shows.</summary>
	private string _outputKey;

	private int _frames;

	private string _message;

	private double _messageUntil;

	private bool _updatingArgumentBox;

	/// <summary>What to do once the host picker has closed (open the code editor or the copy question).</summary>
	private Action _afterPicker;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color WarningColor => new Color(255, 190, 90);

	private static Color GoodColor => new Color(140, 230, 140);

	public ScriptScreen(string selectId)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.9f))
	{
		if (selectId != null)
		{
			s_selectedId = selectId;
			s_showHelp = false;
		}
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		CloseButtonEnabled = true;
		RecreateControls(constructor: true);
		if (s_queuedMessage != null)
		{
			ShowMessage(s_queuedMessage, s_queuedColor);
			s_queuedMessage = null;
		}
	}

	public override string GetFriendlyName()
	{
		return "ScriptToPluginScreen";
	}

	private static ScriptSession Session => ScriptSession.Instance;

	private VirtualProgram Selected => s_selectedId == null ? null : Session?.FindProgram(s_selectedId);

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		PluginSwitcher.AddSwitcher(this, AddCaption("Script to Plugin"));

		Controls.Add(new MyGuiControlLabel(new Vector2(-0.42f, -0.355f), null, "Scripts", null, 0.8f, "White", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		AddChatControls(new Vector2(0.05f, -0.355f));
		_table = new MyGuiControlTable
		{
			Position = new Vector2(-0.42f, -0.33f),
			Size = new Vector2(0.47f, 0.5f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
			ColumnsCount = 3,
			VisibleRowsCount = 13
		};
		_table.SetCustomColumnWidths(new[] { 0.36f, 0.3f, 0.34f });
		_table.SetColumnName(0, new StringBuilder("Script"));
		_table.SetColumnName(1, new StringBuilder("Host block"));
		_table.SetColumnName(2, new StringBuilder("State"));
		_table.ItemSelected += (table, args) => OnRowSelected();
		_table.ItemDoubleClicked += (table, args) => EditCode();
		Controls.Add(_table);

		_outputCaption = new MyGuiControlLabel(new Vector2(0.07f, -0.355f), null, "", null, 0.8f, "White", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_outputCaption);
		_output = new MyGuiControlMultilineText(new Vector2(0.07f, -0.33f), new Vector2(0.35f, 0.5f), null, "Blue", 0.7f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
		Controls.Add(_output);

		_status = new MyGuiControlLabel(new Vector2(-0.42f, 0.2f), null, "", null, 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, 0.255f, "New script", NewScript);
		AddButton(-0.105f, 0.255f, "Edit code", EditCode);
		AddButton(0.105f, 0.255f, "Custom Data", EditCustomData);
		AddButton(0.315f, 0.255f, "Set host block", PickHost);

		_onOffButton = AddButton(-0.315f, 0.315f, "On / Off", ToggleOnOff);
		AddButton(-0.105f, 0.315f, "Recompile", Recompile);
		AddButton(0.105f, 0.315f, "Rename", Rename);
		AddButton(0.315f, 0.315f, "Delete", Delete);

		Controls.Add(new MyGuiControlLabel(new Vector2(-0.42f, 0.375f), null, "Argument", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_argumentBox = new MyGuiControlTextbox(new Vector2(-0.18f, 0.375f), Selected?.Entry.Argument ?? "", 400)
		{
			Size = new Vector2(0.3f, 0.045f)
		};
		_argumentBox.TextChanged += OnArgumentChanged;
		Controls.Add(_argumentBox);
		AddButton(0.105f, 0.375f, "Run", RunSelected);
		_helpButton = AddButton(0.315f, 0.375f, "Help", ToggleHelp);

		_rowsSignature = null;
		_outputSignature = null;
		_outputKey = null;
		RefreshAll();
	}

	/// <summary>
	/// Script to Plugin's chat settings, ending at rightCenter: the name its lines show under, and how much it talks.
	/// </summary>
	private void AddChatControls(Vector2 rightCenter)
	{
		const float comboWidth = 0.11f;
		const float nameWidth = 0.13f;
		const float gap = 0.01f;
		float nameRight = rightCenter.X - comboWidth - gap;
		Controls.Add(new MyGuiControlLabel(new Vector2(nameRight - nameWidth - gap, rightCenter.Y), null, "Chat as", null, 0.7f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER));
		_nameBox = new MyGuiControlTextbox(new Vector2(nameRight - nameWidth / 2f, rightCenter.Y), Personality.DisplayName, Personality.MaxNameLength)
		{
			Size = new Vector2(nameWidth, 0.04f)
		};
		_nameBox.SetToolTip("The name Script to Plugin's chat lines show under. Press Enter to keep it; clear it to go back to Script to Plugin. Also: /stp name <name>.");
		_nameBox.EnterPressed += _ => CommitName();
		_nameBox.FocusChanged += (control, focus) =>
		{
			if (!focus)
			{
				CommitName();
			}
		};
		Controls.Add(_nameBox);

		MyGuiControlCombobox combo = new MyGuiControlCombobox(rightCenter, new Vector2(comboWidth, 0.04f), null, null, 4, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
		Personality.Chattiness[] levels = (Personality.Chattiness[])Enum.GetValues(typeof(Personality.Chattiness));
		foreach (Personality.Chattiness level in levels)
		{
			combo.AddItem((long)level, level.ToString(), null, null, sort: false);
		}
		combo.SelectItemByKey((long)ScriptSettings.Chattiness, sendEvent: false);
		combo.SetToolTip("How much Script to Plugin talks in chat. Off: never. Quiet: only compile errors and crashes. Normal: now and then. Chatty: often. Only you see it.");
		combo.ItemSelected += () =>
		{
			Personality.Chattiness level = (Personality.Chattiness)combo.GetSelectedKey();
			Session?.SetPersonality(level);
			ShowMessage($"Chat personality: {level}. {PersonalityHint(level)}", GoodColor);
		};
		Controls.Add(combo);
	}

	private void CommitName()
	{
		if (Session == null || _nameBox == null)
		{
			return;
		}
		string before = Personality.DisplayName;
		Session.SetDisplayName(_nameBox.Text);
		string after = Personality.DisplayName;
		_nameBox.Text = after;
		if (after != before)
		{
			ShowMessage($"Chat lines now show as {after}.", GoodColor);
		}
	}

	/// <summary>Shows a rename made elsewhere (Wilson's window, /stp name) unless the player is typing in the box.</summary>
	private void RefreshNameBox()
	{
		if (_nameBox != null && !_nameBox.HasFocus && _nameBox.Text != Personality.DisplayName)
		{
			_nameBox.Text = Personality.DisplayName;
		}
	}

	private static string PersonalityHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "Script to Plugin stays silent.";
		case Personality.Chattiness.Quiet:
			return "Only speaks up about compile errors and crashes.";
		case Personality.Chattiness.Chatty:
			return "Comments on everything.";
		default:
			return "Comments now and then.";
		}
	}

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ =>
		{
			try
			{
				onClick();
			}
			catch (Exception ex)
			{
				ShowMessage("Something went wrong: " + ex.Message, WarningColor);
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Button '{text}': {ex}");
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
			// Screens opened straight from the picker's click end up without input focus (the picker is still
			// closing and this window is still coming back), so they wait until this window has focus again.
			if (_afterPicker != null && State == MyGuiScreenState.OPENED && MyScreenManager.GetScreenWithFocus() == this)
			{
				Action next = _afterPicker;
				_afterPicker = null;
				next();
			}
			if (++_frames >= RefreshFrames)
			{
				_frames = 0;
				RefreshAll();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Screen update: {ex}");
		}
		return result;
	}

	private void RefreshAll()
	{
		if (Session == null)
		{
			SetStatus("Script to Plugin isn't running in this session.", WarningColor);
			return;
		}
		RefreshRows();
		RefreshOutput();
		RefreshNameBox();
		VirtualProgram selected = Selected;
		_onOffButton.Text = selected == null ? "On / Off" : selected.Entry.Enabled ? "Switch off" : "Switch on";
		_helpButton.Text = s_showHelp ? "Back" : "Help";
		if (_message != null && ScriptSession.Now < _messageUntil)
		{
			return;
		}
		_message = null;
		SetStatus(Hint(selected), null);
	}

	private string Hint(VirtualProgram selected)
	{
		if (Session.Programs.Count == 0)
		{
			return "No scripts yet. Press New script to add one.";
		}
		if (MyAPIGateway.Session?.SessionSettings != null && !MyAPIGateway.Session.SessionSettings.EnableIngameScripts)
		{
			return "Note: this world has in-game scripts switched off. Your scripts here still run in your game.";
		}
		if (selected == null)
		{
			return "Select a script. Double-click one to edit its code.";
		}
		if (selected.Entry.HostId == 0)
		{
			return "This script has no host block yet. Press Set host block.";
		}
		return "Double-click a script to edit its code. Run uses the argument box.";
	}

	private void SetStatus(string text, Color? color)
	{
		_status.Text = text;
		_status.ColorMask = (color ?? Color.White).ToVector4();
	}

	private void ShowMessage(string text, Color? color = null)
	{
		_message = text;
		_messageUntil = ScriptSession.Now + 6.0;
		SetStatus(text, color);
	}

	// The list

	private void RefreshRows()
	{
		List<VirtualProgram> programs = Session.Programs.OrderBy(p => p.Entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
		if (Selected == null)
		{
			s_selectedId = programs.FirstOrDefault()?.Entry.Id;
			UpdateArgumentBox();
		}
		StringBuilder signature = new StringBuilder();
		foreach (VirtualProgram program in programs)
		{
			signature.Append(program.Entry.Id).Append('\u0001').Append(program.Entry.Name).Append('\u0001').Append(HostText(program)).Append('\u0001').Append(program.StateText).Append('\u0002');
		}
		if (signature.ToString() == _rowsSignature)
		{
			return;
		}
		_rowsSignature = signature.ToString();

		float scroll = _table.ScrollBar?.Value ?? 0f;
		_table.Clear();
		foreach (VirtualProgram program in programs)
		{
			Color? color = !program.Entry.Enabled ? MutedColor : program.HasProblem ? WarningColor : program.IsInstantiated ? GoodColor : (Color?)null;
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(program.Entry.Id);
			row.AddCell(new MyGuiControlTable.Cell(program.Entry.Name, null, null, color));
			row.AddCell(new MyGuiControlTable.Cell(HostText(program), null, null, color));
			row.AddCell(new MyGuiControlTable.Cell(program.StateText, null, null, color));
			_table.Add(row);
		}
		for (int i = 0; i < _table.RowsCount; i++)
		{
			if (_table.GetRow(i).UserData as string == s_selectedId)
			{
				_table.SelectedRowIndex = i;
				break;
			}
		}
		if (_table.ScrollBar != null)
		{
			_table.ScrollBar.Value = scroll;
		}
	}

	private static string HostText(VirtualProgram program)
	{
		if (program.Entry.HostId == 0)
		{
			return "(none)";
		}
		string name = program.Host?.CustomName?.ToString() ?? program.Entry.HostName;
		return string.IsNullOrEmpty(name) ? "(unknown)" : name;
	}

	private void OnRowSelected()
	{
		if (_table.SelectedRow?.UserData is string id && id != s_selectedId)
		{
			s_selectedId = id;
			s_showHelp = false;
			UpdateArgumentBox();
			_outputSignature = null;
			RefreshAll();
		}
	}

	private void UpdateArgumentBox()
	{
		_updatingArgumentBox = true;
		_argumentBox.Text = Selected?.Entry.Argument ?? "";
		_updatingArgumentBox = false;
	}

	private void OnArgumentChanged(MyGuiControlTextbox box)
	{
		VirtualProgram selected = Selected;
		if (_updatingArgumentBox || selected == null || selected.Entry.Argument == box.Text)
		{
			return;
		}
		selected.Entry.Argument = box.Text ?? "";
		Session.MarkDirty();
	}

	// The output panel

	private void RefreshOutput()
	{
		VirtualProgram program = Selected;
		string text = s_showHelp ? "help" : OutputText(program);
		if (text == _outputSignature)
		{
			return;
		}
		_outputSignature = text;
		// Keep the scroll position while the same script's output refreshes; start at the top for anything else.
		string key = s_showHelp ? "help" : program?.Entry.Id;
		float scroll = key == _outputKey ? _output.ScrollbarOffsetV : 0f;
		_outputKey = key;
		_output.Clear();
		try
		{
			WriteOutput(program);
		}
		finally
		{
			_output.ScrollbarOffsetV = scroll;
		}
	}

	private void WriteOutput(VirtualProgram program)
	{
		if (s_showHelp)
		{
			_outputCaption.Text = "Help";
			ScriptHelp.Write(_output);
			return;
		}
		_outputCaption.Text = program == null ? "Output" : program.Entry.Name;
		if (program == null)
		{
			return;
		}
		Vector4 label = new Color(150, 170, 190).ToVector4();
		Vector4 value = Color.White.ToVector4();
		foreach (KeyValuePair<string, string> line in InfoLines(program))
		{
			_output.AppendText(line.Key + ": ", "Blue", 0.7f, label);
			_output.AppendText(line.Value, "White", 0.7f, value);
			_output.AppendLine();
		}
		_output.AppendLine();
		string detail = program.DetailedInfo;
		if (!string.IsNullOrEmpty(detail))
		{
			Vector4 color = program.HasProblem ? WarningColor.ToVector4() : new Color(200, 215, 230).ToVector4();
			_output.AppendText(detail.TrimEnd('\n'), "White", 0.7f, color);
		}
		else if (program.CompileMessages.Count > 0)
		{
			_output.AppendText("Compiler notes:\n" + string.Join("\n", program.CompileMessages), "White", 0.7f, MutedColor.ToVector4());
		}
	}

	private static string OutputText(VirtualProgram program)
	{
		if (program == null)
		{
			return "";
		}
		StringBuilder text = new StringBuilder();
		foreach (KeyValuePair<string, string> line in InfoLines(program))
		{
			text.Append(line.Key).Append(line.Value).Append('\n');
		}
		return text.Append(program.DetailedInfo).Append(program.HasProblem).Append(program.CompileMessages.Count).ToString();
	}

	private static IEnumerable<KeyValuePair<string, string>> InfoLines(VirtualProgram program)
	{
		yield return new KeyValuePair<string, string>("State", program.StateText);
		string host = HostText(program);
		string grid = program.Host?.CubeGrid?.DisplayName ?? program.Entry.HostGridName;
		if (!string.IsNullOrEmpty(grid) && program.Entry.HostId != 0)
		{
			host += " on " + grid;
		}
		yield return new KeyValuePair<string, string>("Host", host);
		if (program.RunCount > 0)
		{
			yield return new KeyValuePair<string, string>("Last run", $"{program.LastRunMs:0.000} ms, {program.LastInstructions:N0} instructions");
			yield return new KeyValuePair<string, string>("Cost", $"{program.AverageRunMs:0.000} ms per run, {program.LoadMsPerTick:0.000} ms per tick");
		}
	}

	// Buttons

	private VirtualProgram RequireSelected()
	{
		VirtualProgram program = Selected;
		if (program == null)
		{
			ShowMessage("Select a script first.", WarningColor);
		}
		return program;
	}

	private void NewScript()
	{
		MyGuiSandbox.AddScreen(new HostPickerScreen(ScriptSession.ControlledBlock(), block => _afterPicker = () => CreateScriptOn(block)));
	}

	private void CreateScriptOn(MyTerminalBlock host)
	{
		if (host is MyProgrammableBlock pb && !string.IsNullOrWhiteSpace(((Sandbox.ModAPI.IMyProgrammableBlock)pb).ProgramData))
		{
			MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder("Copy the programmable block's script?"), messageText: new StringBuilder($"{pb.CustomName} has a script. Copy its code and Custom Data into the new script?\n\nIf you do, switch the programmable block off afterwards so the script doesn't run twice."), callback: result =>
			{
				if (result == MyGuiScreenMessageBox.ResultEnum.YES)
				{
					VirtualProgram copy = Session.CreateScript(host, pb.CustomName.ToString(), ((Sandbox.ModAPI.IMyProgrammableBlock)pb).ProgramData);
					copy.Entry.CustomData = pb.CustomData ?? "";
					copy.Entry.Argument = pb.TerminalRunArgument ?? "";
					Select(copy);
					ShowMessage($"Copied the script from {pb.CustomName}. It starts once it has compiled.", GoodColor);
				}
				else
				{
					CreateEmpty(host);
				}
			}));
			return;
		}
		CreateEmpty(host);
	}

	private void CreateEmpty(MyTerminalBlock host)
	{
		VirtualProgram program = Session.CreateScript(host, "Script", NewScriptTemplate);
		Select(program);
		OpenEditor(program);
	}

	private void Select(VirtualProgram program)
	{
		s_selectedId = program.Entry.Id;
		s_showHelp = false;
		_rowsSignature = null;
		_outputSignature = null;
		UpdateArgumentBox();
		RefreshAll();
	}

	private void EditCode()
	{
		VirtualProgram program = RequireSelected();
		if (program != null)
		{
			OpenEditor(program);
		}
	}

	/// <summary>Closes this window and opens the code editor; the window comes back when the editor closes.</summary>
	private void OpenEditor(VirtualProgram program)
	{
		string id = program.Entry.Id;
		HandOff(() => ScriptEditors.EditCode(id));
	}

	private void EditCustomData()
	{
		VirtualProgram program = RequireSelected();
		if (program != null)
		{
			string id = program.Entry.Id;
			HandOff(() => ScriptEditors.EditCustomData(id));
		}
	}

	private void HandOff(Action open)
	{
		CloseScreen();
		ScriptEditors.AfterClosed(this, open);
	}

	/// <summary>A message to show in the status line the next time the window opens.</summary>
	internal static void QueueMessage(string text, Color? color)
	{
		s_queuedMessage = text;
		s_queuedColor = color;
	}

	private void PickHost()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		MyGuiSandbox.AddScreen(new HostPickerScreen(program.Host, block =>
		{
			if (Session?.FindProgram(program.Entry.Id) == null)
			{
				return;
			}
			program.SetHost(block);
			ShowMessage($"{program.Entry.Name} now runs on {block.CustomName}.", GoodColor);
			_rowsSignature = null;
			RefreshAll();
		}));
	}

	private void ToggleOnOff()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		program.SetEnabled(!program.Entry.Enabled);
		ShowMessage(program.Entry.Enabled ? $"{program.Entry.Name} switched on." : $"{program.Entry.Name} switched off.", program.Entry.Enabled ? GoodColor : (Color?)null);
		RefreshAll();
	}

	private void Recompile()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		program.Recompile();
		ShowMessage($"Recompiling {program.Entry.Name}.", GoodColor);
		RefreshAll();
	}

	private void Rename()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		MyGuiScreenDialogText dialog = new MyGuiScreenDialogText(program.Entry.Name, MyStringId.GetOrCompute("Script name"));
		dialog.OnConfirmed += name =>
		{
			if (string.IsNullOrWhiteSpace(name) || Session?.FindProgram(program.Entry.Id) == null)
			{
				return;
			}
			program.Rename(name);
			ShowMessage($"Renamed to {program.Entry.Name}. Toolbar slots keep working.", GoodColor);
			RefreshAll();
		};
		MyGuiSandbox.AddScreen(dialog);
	}

	private void Delete()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: new StringBuilder("Delete script"), messageText: new StringBuilder($"Delete {program.Entry.Name}? Its code, Custom Data and Storage are removed. This can't be undone."), callback: answer =>
		{
			if (answer != MyGuiScreenMessageBox.ResultEnum.YES || Session?.FindProgram(program.Entry.Id) == null)
			{
				return;
			}
			Session.DeleteScript(program);
			s_selectedId = null;
			ShowMessage($"Deleted {program.Entry.Name}.", null);
			_rowsSignature = null;
			_outputSignature = null;
			RefreshAll();
		}));
	}

	private void RunSelected()
	{
		VirtualProgram program = RequireSelected();
		if (program == null)
		{
			return;
		}
		string argument = _argumentBox.Text ?? "";
		if (program.Run(argument, UpdateType.Terminal))
		{
			ShowMessage(argument.Length == 0 ? $"Ran {program.Entry.Name}." : $"Ran {program.Entry.Name} with '{argument}'.", GoodColor);
		}
		else
		{
			ShowMessage($"{program.Entry.Name} can't run right now: {program.StateText}.", WarningColor);
		}
		_outputSignature = null;
		RefreshAll();
	}

	private void ToggleHelp()
	{
		s_showHelp = !s_showHelp;
		_outputSignature = null;
		RefreshAll();
	}
}
