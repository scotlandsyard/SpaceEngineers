using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Game.Screens;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Collections;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Components;
using VRage.Scripting;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// Runs the scripts of the current world on this client: compiles them in the background, runs them every tick
/// on the game thread, saves them with the world's plugin storage, and adds the chat command and toolbar actions.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class ScriptSession : MySessionComponentBase
{
	public const string ChatSender = "Script to Plugin";

	private const string ChatCommand = "/stp";

	private const string ActionPrefix = "ScriptToPlugin_";

	private const string MenuActionId = ActionPrefix + "Menu";

	/// <summary>Ticks between saves of the script file while something changed (Storage changes all the time).</summary>
	private const int SaveIntervalTicks = 60 * 60;

	internal static ScriptSession Instance { get; private set; }

	internal static long LocalIdentityId => MyAPIGateway.Session?.Player?.IdentityId ?? 0L;

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	internal IgcHub Igc { get; } = new IgcHub();

	internal IReadOnlyList<VirtualProgram> Programs => _programs;

	/// <summary>Goes up whenever scripts are added, removed or renamed, so the menu knows to rebuild its list.</summary>
	internal int ProgramsVersion { get; private set; }

	internal int Tick { get; private set; }

	private readonly List<VirtualProgram> _programs = new List<VirtualProgram>();

	private readonly Queue<KeyValuePair<VirtualProgram, string>> _compileQueue = new Queue<KeyValuePair<VirtualProgram, string>>();

	private readonly Dictionary<MyGridTerminalSystem, int> _preparedTerminals = new Dictionary<MyGridTerminalSystem, int>();

	private readonly Dictionary<string, IMyTerminalAction> _actions = new Dictionary<string, IMyTerminalAction>();

	private IMyTerminalAction _menuAction;

	private bool _compileBusy;

	private bool _dirty;

	private int _nextSaveTick;

	private bool _started;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Instance = this;
			foreach (ScriptEntry entry in ScriptStore.Load())
			{
				_programs.Add(new VirtualProgram(this, entry));
			}
			ProgramsVersion++;
			_menuAction = CreateMenuAction();
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] BeforeStart failed: {ex}");
		}
	}

	public override void SaveData()
	{
		// Called when this game saves the world (single player or hosting): like the programmable block, ask every
		// script to save its state.
		if (!_started)
		{
			return;
		}
		foreach (VirtualProgram program in _programs)
		{
			try
			{
				program.CallSave();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Save of {program.Entry.Name}: {ex}");
			}
		}
		SaveNow();
	}

	protected override void UnloadData()
	{
		if (_started)
		{
			foreach (VirtualProgram program in _programs)
			{
				program.Shutdown();
			}
			SaveNow();
			try
			{
				MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
				MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
			}
			catch
			{
			}
		}
		_programs.Clear();
		_compileQueue.Clear();
		ScriptStore.Unload();
		_started = false;
		Instance = null;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		Tick++;
		try
		{
			if (Tick % 600 == 0)
			{
				_preparedTerminals.Clear();
			}
			StartNextCompile();
			Igc.Update();
			for (int i = 0; i < _programs.Count; i++)
			{
				_programs[i].Update(Tick);
			}
			if (Tick >= _nextSaveTick)
			{
				_nextSaveTick = Tick + SaveIntervalTicks;
				if (_dirty || _programs.Any(p => p.IsInstantiated))
				{
					SaveNow();
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Update: {ex}");
		}
	}

	internal void MarkDirty()
	{
		_dirty = true;
		// Soon, but not on every keystroke's worth of changes.
		_nextSaveTick = Math.Min(_nextSaveTick, Tick + 60);
	}

	internal void SaveNow()
	{
		_dirty = false;
		ScriptStore.Save(_programs.Select(p => p.Entry));
	}

	internal void OnRenamed(VirtualProgram program)
	{
		ProgramsVersion++;
		MarkDirty();
	}

	internal VirtualProgram FindProgram(string id)
	{
		return _programs.FirstOrDefault(p => p.Entry.Id == id);
	}

	internal VirtualProgram CreateScript(MyTerminalBlock host, string name, string code)
	{
		ScriptEntry entry = new ScriptEntry
		{
			Id = ScriptStore.NewId(_programs.Select(p => p.Entry)),
			Name = UniqueName(name),
			Code = code ?? "",
			Enabled = true
		};
		VirtualProgram program = new VirtualProgram(this, entry);
		program.SetHost(host);
		_programs.Add(program);
		ProgramsVersion++;
		MarkDirty();
		return program;
	}

	internal void DeleteScript(VirtualProgram program)
	{
		program.Shutdown();
		_programs.Remove(program);
		ProgramsVersion++;
		MarkDirty();
	}

	internal string UniqueName(string name)
	{
		string baseName = string.IsNullOrWhiteSpace(name) ? "Script" : name.Trim();
		string candidate = baseName;
		for (int i = 2; _programs.Any(p => p.Entry.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); i++)
		{
			candidate = $"{baseName} {i}";
		}
		return candidate;
	}

	/// <summary>
	/// Marks which blocks the local player may use, the way a programmable block marks them for its owner before
	/// every run. Once per tick is enough in multiplayer; in single player real programmable blocks mark them for
	/// their own owners in between, so it is done before every run there.
	/// </summary>
	internal void PrepareTerminal(MyGridTerminalSystem terminal)
	{
		if (!MyAPIGateway.Multiplayer.IsServer && _preparedTerminals.TryGetValue(terminal, out int tick) && tick == Tick)
		{
			return;
		}
		terminal.UpdateGridBlocksOwnership(LocalIdentityId);
		_preparedTerminals[terminal] = Tick;
	}

	// Compiling

	internal void QueueCompile(VirtualProgram program, string code)
	{
		_compileQueue.Enqueue(new KeyValuePair<VirtualProgram, string>(program, code));
	}

	/// <summary>Compiles one script at a time on a background thread; the result is handed back on the game thread.</summary>
	private void StartNextCompile()
	{
		if (_compileBusy || _compileQueue.Count == 0)
		{
			return;
		}
		KeyValuePair<VirtualProgram, string> job = _compileQueue.Dequeue();
		VirtualProgram program = job.Key;
		string code = job.Value;
		if (!MyVRage.Platform.Scripting.IsRuntimeCompilationSupported)
		{
			program.OnCompiled(code, null, new List<string> { "This game can't compile scripts (runtime compilation isn't supported)." });
			return;
		}
		_compileBusy = true;
		string assemblyPath = Path.Combine(MyFileSystem.UserDataPath, $"ScriptToPlugin-{program.Entry.Id}.dll");
		string friendlyName = $"Script to Plugin: {program.Entry.Name} ({program.Entry.Id})";
		Assembly assembly = null;
		List<string> messages = new List<string>();
		MyAPIGateway.Parallel.StartBackground(() =>
		{
			try
			{
				assembly = MyVRage.Platform.Scripting.CompileIngameScriptAsync(assemblyPath, code, out List<Message> diagnostics, friendlyName, "Program", "MyGridProgram", MyFakes.ENABLE_PROGRAMMABLE_BLOCK_MEMORY_LIMIT).Result;
				messages.AddRange(diagnostics.Select(m => m.Text));
			}
			catch (Exception ex)
			{
				assembly = null;
				messages.Add("Compiling failed: " + (ex.InnerException ?? ex).Message);
			}
		}, () =>
		{
			_compileBusy = false;
			if (_started && _programs.Contains(program))
			{
				program.OnCompiled(code, assembly, messages);
			}
		});
	}

	// Chat command and menu

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		if (messageText != null && messageText.Trim().Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			sendToOthers = false;
			OpenMenu(null);
		}
	}

	internal void OpenMenu(string selectId)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new ScriptScreen(selectId));
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the menu: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] {ex}");
		}
	}

	/// <summary>The block the player is controlling (cockpit, remote control...), a natural default host.</summary>
	internal static MyTerminalBlock ControlledBlock()
	{
		return MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity as MyTerminalBlock;
	}

	// Toolbar actions. They only appear on blocks that host a script, one set per script.

	private void CustomActionGetter(Sandbox.ModAPI.IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			bool any = false;
			foreach (VirtualProgram program in _programs)
			{
				if (program.Entry.HostId != block.EntityId)
				{
					continue;
				}
				any = true;
				actions.Add(GetAction(program, "Run", "Run"));
				actions.Add(GetAction(program, "RunDefault", "Run (default argument)"));
				actions.Add(GetAction(program, "Toggle", "On/Off"));
			}
			if (any)
			{
				actions.Add(_menuAction);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] CustomActionGetter: {ex}");
		}
	}

	private IMyTerminalAction CreateMenuAction()
	{
		return new MyTerminalAction<MyTerminalBlock>(MenuActionId, new StringBuilder("Script to Plugin menu"), block =>
		{
			OpenMenu(_programs.FirstOrDefault(p => p.Entry.HostId == block.EntityId)?.Entry.Id);
		}, "Textures\\GUI\\Icons\\Actions\\Start.dds")
		{
			ValidForGroups = false
		};
	}

	/// <summary>
	/// A script's action. The id holds the script's id so toolbar slots find it again after a restart; the name
	/// holds the script's name, so a renamed script gets a new action object with the same id.
	/// </summary>
	private IMyTerminalAction GetAction(VirtualProgram program, string kind, string label)
	{
		string scriptId = program.Entry.Id;
		string id = $"{ActionPrefix}{kind}_{scriptId}";
		string name = $"{program.Entry.Name}: {label}";
		string key = id + "\n" + name;
		if (_actions.TryGetValue(key, out IMyTerminalAction cached))
		{
			return cached;
		}
		MyTerminalAction<MyTerminalBlock> action;
		switch (kind)
		{
		case "Run":
			action = new MyTerminalAction<MyTerminalBlock>(id, new StringBuilder(name), (block, parameters) =>
			{
				string argument = null;
				TerminalActionParameter parameter = parameters.FirstOrDefault();
				if (!parameter.IsEmpty && parameter.TypeCode == TypeCode.String)
				{
					argument = parameter.Value as string;
				}
				RunFromToolbar(scriptId, argument);
			}, MyTerminalActionIcons.START)
			{
				DoUserParameterRequestWithItem = RequestRunArgument
			};
			action.ParameterDefinitions.Add(TerminalActionParameter.Get(string.Empty));
			break;
		case "RunDefault":
			action = new MyTerminalAction<MyTerminalBlock>(id, new StringBuilder(name), block => RunFromToolbar(scriptId, null), MyTerminalActionIcons.START);
			break;
		default:
			action = new MyTerminalAction<MyTerminalBlock>(id, new StringBuilder(name), block => ToggleFromToolbar(scriptId), (block, text) =>
			{
				VirtualProgram current = FindProgram(scriptId);
				text.Append(current == null ? "-" : current.Entry.Enabled ? "On" : "Off");
			}, MyTerminalActionIcons.TOGGLE);
			break;
		}
		action.ValidForGroups = false;
		action.Enabled = block => FindProgram(scriptId)?.Entry.HostId == block.EntityId;
		_actions[key] = action;
		return action;
	}

	private void RunFromToolbar(string scriptId, string argument)
	{
		VirtualProgram program = FindProgram(scriptId);
		if (program == null)
		{
			return;
		}
		if (!program.Run(argument ?? program.Entry.Argument, Sandbox.ModAPI.Ingame.UpdateType.Trigger))
		{
			MyAPIGateway.Utilities.ShowNotification($"{program.Entry.Name}: can't run ({program.StateText}).", 3000, MyFontEnum.Red);
		}
	}

	private void ToggleFromToolbar(string scriptId)
	{
		VirtualProgram program = FindProgram(scriptId);
		if (program == null)
		{
			return;
		}
		program.SetEnabled(!program.Entry.Enabled);
		MyAPIGateway.Utilities.ShowNotification($"{program.Entry.Name} switched {(program.Entry.Enabled ? "on" : "off")}.", 2000);
	}

	/// <summary>The programmable block's own dialogs for a Run action: first the argument, then the slot's label.</summary>
	private static void RequestRunArgument(Sandbox.ModAPI.Ingame.IUserCustomizableTerminalAction item, Action<bool> callback)
	{
		MyGuiScreenDialogText argumentDialog = new MyGuiScreenDialogText(string.Empty, MySpaceTexts.DialogText_RunArgument);
		argumentDialog.OnConfirmed += argument =>
		{
			TerminalActionParameter value = TerminalActionParameter.Get(argument);
			string label = argument.Substring(0, Math.Min(argument.Length, MyToolbarItemActions.TOOLBAR_LABEL_MAX_CHARS));
			MyGuiScreenDialogText labelDialog = new MyGuiScreenDialogText(label, MySpaceTexts.SetValue_Action_SetLabel, isTopMostScreen: false, MyToolbarItemActions.TOOLBAR_LABEL_MAX_CHARS);
			labelDialog.OnCancelled += _ => callback(false);
			labelDialog.OnConfirmed += title =>
			{
				item.Parameters[0] = value;
				item.SetCustomIconTitle(title);
				callback(true);
			};
			MyGuiSandbox.AddScreen(labelDialog);
		};
		argumentDialog.OnCancelled += _ => callback(false);
		MyGuiSandbox.AddScreen(argumentDialog);
	}
}
