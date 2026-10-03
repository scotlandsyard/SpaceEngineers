using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Library.Compiler;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// One script running in the plugin: a programmable block without the block. It follows the programmable block's
/// own code closely: the same compiler and whitelist, the same instruction limits, the same order of setting up
/// Me/Runtime/Storage/Echo before the constructor runs, and the same rule that a crashed script stays stopped
/// until it is recompiled or switched on again. All of it runs on the game thread.
/// </summary>
internal class VirtualProgram
{
	public const int MaxInstructions = 50000;

	private const int MaxMethodCalls = 10000;

	private const int MaxEchoLength = 8000;

	/// <summary>Ticks between two looks for a host block that isn't loaded, and between access checks.</summary>
	private const int HostCheckTicks = 60;

	private readonly ScriptSession _session;

	private readonly ProgramRuntimeInfo _runtime = new ProgramRuntimeInfo();

	private readonly ProgramBlockProxy _me;

	private readonly TerminalSystemWrapper _terminal;

	private readonly StringBuilder _echo = new StringBuilder();

	private Assembly _assembly;

	/// <summary>The code _assembly was compiled from, to tell whether the script needs compiling again.</summary>
	private string _assemblyCode;

	private IMyGridProgram _instance;

	private LocalIgc _igc;

	private bool _compiling;

	private bool _compileFailed;

	/// <summary>Stopped by an exception or the instruction limit; stays stopped until restarted.</summary>
	private bool _crashed;

	private bool? _pendingEnabled;

	private bool _hasAccess;

	private int _nextHostCheck;

	private int _nextAccessCheck;

	/// <summary>Spreads the 10- and 100-tick updates of different scripts over different ticks.</summary>
	private readonly int _updateOffset;

	private double _loadWindowMs;

	public VirtualProgram(ScriptSession session, ScriptEntry entry)
	{
		_session = session;
		Entry = entry;
		_me = new ProgramBlockProxy(this);
		_terminal = new TerminalSystemWrapper(this);
		_updateOffset = (int)((uint)(entry.Id ?? "").GetHashCode() % 100u);
	}

	public ScriptEntry Entry { get; }

	/// <summary>The loaded host block, or null while it isn't loaded (or none is picked).</summary>
	public MyTerminalBlock Host { get; private set; }

	/// <summary>What the programmable block would show in its detailed info: the last run's Echo output and errors.</summary>
	public string DetailedInfo { get; private set; } = "";

	public List<string> CompileMessages { get; } = new List<string>();

	public bool IsRunning { get; private set; }

	public bool IsInstantiated => _instance != null;

	public UpdateFrequency UpdateFrequency => _instance != null ? _runtime.UpdateFrequency : UpdateFrequency.None;

	public double LastRunMs { get; private set; }

	public double AverageRunMs { get; private set; }

	/// <summary>Average time the script took per game tick over the last second, so scripts can be compared.</summary>
	public double LoadMsPerTick { get; private set; }

	public int LastInstructions { get; private set; }

	public long RunCount { get; private set; }

	public bool HasProblem => _compileFailed || _crashed || (Entry.Enabled && (Host == null || !_hasAccess));

	public string StateText
	{
		get
		{
			if (!Entry.Enabled)
			{
				return "Off";
			}
			if (_compiling)
			{
				return "Compiling";
			}
			if (_compileFailed)
			{
				return "Compile errors";
			}
			if (_crashed)
			{
				return "Stopped by error";
			}
			if (Host == null)
			{
				return Entry.HostId == 0 ? "No host block" : "Host not loaded";
			}
			if (!_hasAccess)
			{
				return "No access to host";
			}
			if (_instance == null)
			{
				return "Starting";
			}
			UpdateFrequency frequency = _runtime.UpdateFrequency;
			if ((frequency & UpdateFrequency.Update1) != 0)
			{
				return "Running (every tick)";
			}
			if ((frequency & UpdateFrequency.Update10) != 0)
			{
				return "Running (every 10 ticks)";
			}
			if ((frequency & UpdateFrequency.Update100) != 0)
			{
				return "Running (every 100 ticks)";
			}
			return "Waiting for a run";
		}
	}

	/// <summary>Called every tick by the session.</summary>
	public void Update(int tick)
	{
		if (tick % 60 == 0)
		{
			LoadMsPerTick = _loadWindowMs / 60.0;
			_loadWindowMs = 0.0;
		}
		if (!Entry.Enabled || _crashed || _compileFailed || _compiling)
		{
			return;
		}
		if (_assembly == null || _assemblyCode != Entry.Code)
		{
			StartCompile();
			return;
		}
		if (!RefreshHost(tick))
		{
			if (_instance != null)
			{
				// The host was unloaded or access was lost. Storage is already current (it's copied after every run),
				// so the script can start again from it when the host comes back.
				Unload();
			}
			return;
		}
		if (_instance == null)
		{
			CreateInstance();
			if (_instance == null)
			{
				return;
			}
		}

		// The same scheduling as the game's MyIngameScriptComponent.
		UpdateType updateType = UpdateType.None;
		UpdateFrequency frequency = _runtime.UpdateFrequency;
		if ((frequency & UpdateFrequency.Once) != 0)
		{
			updateType |= UpdateType.Once;
			_runtime.UpdateFrequency = frequency & ~UpdateFrequency.Once;
		}
		if ((frequency & UpdateFrequency.Update1) != 0)
		{
			updateType |= UpdateType.Update1;
		}
		if ((frequency & UpdateFrequency.Update10) != 0 && (tick + _updateOffset) % 10 == 0)
		{
			updateType |= UpdateType.Update10;
		}
		if ((frequency & UpdateFrequency.Update100) != 0 && (tick + _updateOffset) % 100 == 0)
		{
			updateType |= UpdateType.Update100;
		}
		if (updateType != UpdateType.None)
		{
			Run("", updateType);
		}
	}

	/// <summary>Finds the host block and checks access to it. False when the script can't run now.</summary>
	private bool RefreshHost(int tick)
	{
		if (Host != null && (Host.Closed || Host.MarkedForClose || Host.EntityId != Entry.HostId))
		{
			Host = null;
			_nextHostCheck = tick;
		}
		if (Host == null)
		{
			if (tick < _nextHostCheck)
			{
				return false;
			}
			_nextHostCheck = tick + HostCheckTicks;
			if (Entry.HostId == 0 || !MyEntities.TryGetEntityById(Entry.HostId, out MyTerminalBlock block) || block.Closed || block.MarkedForClose)
			{
				return false;
			}
			Host = block;
			_nextAccessCheck = tick;
		}
		if (tick >= _nextAccessCheck)
		{
			_nextAccessCheck = tick + HostCheckTicks;
			_hasAccess = ((Sandbox.ModAPI.IMyTerminalBlock)Host).HasPlayerAccess(ScriptSession.LocalIdentityId);
			string hostName = Host.CustomName?.ToString() ?? "";
			string gridName = Host.CubeGrid?.DisplayName ?? "";
			if (hostName != Entry.HostName || gridName != Entry.HostGridName)
			{
				Entry.HostName = hostName;
				Entry.HostGridName = gridName;
				_session.MarkDirty();
			}
		}
		return _hasAccess;
	}

	private void StartCompile()
	{
		_compiling = true;
		CompileMessages.Clear();
		DetailedInfo = "";
		_session.QueueCompile(this, Entry.Code ?? "");
	}

	/// <summary>Called on the game thread when a compile started by this script has finished.</summary>
	public void OnCompiled(string code, Assembly assembly, List<string> messages)
	{
		_compiling = false;
		if (code != Entry.Code)
		{
			// Edited while compiling: the next update compiles the new code.
			return;
		}
		ReplaceAssembly(assembly, code);
		CompileMessages.Clear();
		CompileMessages.AddRange(messages);
		_compileFailed = assembly == null;
		if (_compileFailed)
		{
			DetailedInfo = "Compile errors:\n" + string.Join("\n", messages);
			_session.OnScriptCompileError(this);
		}
		else
		{
			_session.OnScriptCompiled(this);
		}
	}

	private void ReplaceAssembly(Assembly assembly, string code)
	{
		if (_assembly != null && _assembly != assembly)
		{
			try
			{
				MyVRage.Platform.Scripting.ClearCachedTypesForAssembly(_assembly);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] ClearCachedTypesForAssembly: {e.Message}");
			}
		}
		_assembly = assembly;
		_assemblyCode = assembly != null ? code : null;
	}

	private void CreateInstance()
	{
		_echo.Clear();
		DetailedInfo = "";
		Type type = _assembly.GetType("Program");
		if (type == null)
		{
			Terminate("The script has no Program class.");
			return;
		}
		ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (constructor == null)
		{
			Terminate("The script has no constructor without parameters.");
			return;
		}

		// The object is made without running its constructor, so Me, Runtime and the rest are already set when the
		// script's constructor runs. Static initialisers run here, so this is inside the instruction limits too.
		IMyGridProgram instance = null;
		if (!RunCore(() => instance = FormatterServices.GetUninitializedObject(type) as IMyGridProgram) || instance == null)
		{
			if (!_crashed)
			{
				Terminate("The script could not be created.");
			}
			return;
		}
		_runtime.Reset();
		instance.Runtime = _runtime;
		instance.World = ProgramWorldInfo.Default;
		instance.Storage = Entry.Storage ?? "";
		instance.Me = _me;
		instance.Echo = EchoText;
		_igc?.Dispose();
		LocalIgc igc = new LocalIgc(_session.Igc, this);
		_igc = igc;
		instance.IGC_ContextGetter = () => igc;
		_instance = instance;
		bool started = Execute(program =>
		{
			constructor.Invoke(program, null);
			if (!program.HasMainMethod)
			{
				throw new MissingMethodException("The script has no Main method.");
			}
		}, "Constructor");
		if (started && _instance != null)
		{
			_session.OnScriptStarted(this);
		}
	}

	/// <summary>Runs Main, like pressing Run on the programmable block. False when the script isn't running.</summary>
	public bool Run(string argument, UpdateType updateType)
	{
		if (_instance == null || IsRunning)
		{
			return false;
		}
		return Execute(program =>
		{
			_runtime.BeginMainOperation();
			program.Main(argument ?? "", updateType);
			_runtime.EndMainOperation();
		}, null);
	}

	/// <summary>Me.TryRun. A script is always running when it calls this on itself, so like the game it returns false then.</summary>
	public bool TryRunFromScript(string argument)
	{
		return !IsRunning && Run(argument, UpdateType.Script);
	}

	/// <summary>Calls the script's Save method so its Storage is current. Skipped while the host isn't loaded.</summary>
	public void CallSave()
	{
		if (_instance == null || Host == null)
		{
			return;
		}
		if (_instance.HasSaveMethod)
		{
			Execute(program =>
			{
				_runtime.BeginSaveOperation();
				program.Save();
			}, "Save");
		}
		if (_instance != null)
		{
			Entry.Storage = _instance.Storage;
		}
	}

	/// <summary>What the programmable block does around every call into the script.</summary>
	private bool Execute(Action<IMyGridProgram> action, string what)
	{
		if (_instance == null || Host == null)
		{
			return false;
		}
		MyCubeGrid grid = Host.CubeGrid;
		MyGridTerminalSystem terminal = grid == null ? null : MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid) as MyGridTerminalSystem;
		if (terminal == null)
		{
			DetailedInfo = "The host block's grid has no terminal system.";
			return false;
		}
		_session.PrepareTerminal(terminal);
		_terminal.SetInstance(terminal);
		_echo.Clear();
		IMyGridProgram instance = _instance;
		instance.GridTerminalSystem = _terminal;
		bool ok;
		try
		{
			ok = RunCore(() => action(instance));
		}
		finally
		{
			instance.GridTerminalSystem = null;
		}
		if (ok)
		{
			DetailedInfo = _echo.ToString();
		}
		else if (what != null && !_crashed)
		{
			DetailedInfo = _echo + what + " failed.";
		}
		if (_instance != null)
		{
			Entry.Storage = _instance.Storage;
		}
		if (_pendingEnabled.HasValue && !IsRunning)
		{
			bool enable = _pendingEnabled.Value;
			_pendingEnabled = null;
			SetEnabled(enable);
		}
		return ok;
	}

	/// <summary>Runs script code inside the game's instruction counter and turns script errors into a stopped script.</summary>
	private bool RunCore(Action action)
	{
		IsRunning = true;
		long start = Stopwatch.GetTimestamp();
		try
		{
			IlInjector.ICounterHandle handle = IlInjector.BeginRunBlock(MaxInstructions, MaxMethodCalls, MyFakes.ENABLE_PROGRAMMABLE_BLOCK_TIME_LIMIT);
			try
			{
				_runtime.InjectorHandle = handle;
				action();
			}
			finally
			{
				LastInstructions = handle.InstructionCount;
				handle.Dispose();
			}
			return true;
		}
		catch (Exception exception)
		{
			Exception error = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
			if (error is ScriptOutOfRangeException)
			{
				if (IlInjector.IsWithinRunBlock())
				{
					// Called from inside another script's run: that script's limit was hit, not ours.
					_echo.Append("Nested call was too complex.\n");
					return false;
				}
				Terminate(_echo + LimitText(error));
			}
			else
			{
				Terminate(_echo + "Exception: " + error.Message + "\n" + TrimStackTrace(error.StackTrace));
			}
			return false;
		}
		finally
		{
			_runtime.InjectorHandle = null;
			IsRunning = false;
			double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
			LastRunMs = ms;
			AverageRunMs = RunCount == 0 ? ms : AverageRunMs * 0.95 + ms * 0.05;
			RunCount++;
			_loadWindowMs += ms;
		}
	}

	private static string LimitText(Exception error)
	{
		switch (error)
		{
		case ScriptOutOfTimeException _:
			return $"Script stopped: it ran longer than {IlInjector.MAX_RUNTIME_SECONDS} seconds.";
		case ScriptOutOfInstructionsException _:
			return $"Script stopped: too complex (more than {MaxInstructions:N0} instructions in one run).";
		case ScriptOutOfMethodCallsException _:
			return $"Script stopped: too complex (more than {MaxMethodCalls:N0} method calls in one run).";
		case ScriptOutOfCallChainDepthException _:
			return $"Script stopped: calls nested more than {IlInjector.MAX_CALL_CHAIN_DEPTH} deep.";
		case ScriptOutOfMemoryException _:
			return "Script stopped: it used too much memory.";
		default:
			return "Script stopped: " + error.Message;
		}
	}

	/// <summary>Cuts the stack trace where the plugin's own code starts, like the game cuts it at MyGridProgram.</summary>
	private static string TrimStackTrace(string trace)
	{
		if (string.IsNullOrEmpty(trace))
		{
			return "";
		}
		foreach (string marker in new[] { typeof(MyGridProgram).FullName, typeof(VirtualProgram).FullName })
		{
			int index = trace.IndexOf(marker, StringComparison.Ordinal);
			if (index > 0)
			{
				int lineStart = trace.LastIndexOf('\n', index);
				trace = lineStart > 0 ? trace.Substring(0, lineStart) : "";
			}
		}
		return trace;
	}

	private void EchoText(string line)
	{
		line = line ?? "";
		int length = line.Length + 1;
		if (length > MaxEchoLength)
		{
			_echo.Clear();
			line = line.Substring(0, MaxEchoLength);
			length = MaxEchoLength;
		}
		int total = _echo.Length + length;
		if (total > MaxEchoLength)
		{
			_echo.Remove(0, total - MaxEchoLength);
		}
		_echo.Append(line).Append('\n');
	}

	/// <summary>Stops the script after an error. Its Storage is kept.</summary>
	private void Terminate(string message)
	{
		if (_instance != null)
		{
			Entry.Storage = _instance.Storage;
		}
		_crashed = true;
		Unload();
		DetailedInfo = message;
		MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] {Entry.Name}: {message}");
		_session.OnScriptCrashed(this);
	}

	private void Unload()
	{
		_igc?.Dispose();
		_igc = null;
		_instance = null;
		_runtime.Reset();
	}

	/// <summary>Switches the script on or off. On starts it fresh (compiling first if needed); off saves it and stops it.</summary>
	public void SetEnabled(bool enabled)
	{
		if (IsRunning)
		{
			_pendingEnabled = enabled;
			return;
		}
		if (enabled)
		{
			Entry.Enabled = true;
			if (_crashed || _compileFailed)
			{
				Restart();
			}
		}
		else if (Entry.Enabled)
		{
			CallSave();
			Unload();
			Entry.Enabled = false;
			DetailedInfo = "";
			_session.OnScriptStopped(this);
		}
		_session.MarkDirty();
	}

	/// <summary>Me.Enabled from inside the script: takes effect when the current run ends.</summary>
	public void RequestEnabled(bool enabled)
	{
		if (IsRunning)
		{
			_pendingEnabled = enabled;
		}
		else
		{
			SetEnabled(enabled);
		}
	}

	/// <summary>Saves the script, then compiles and starts it again, like the programmable block's Recompile button.</summary>
	public void Recompile()
	{
		Restart();
		ReplaceAssembly(null, null);
		Entry.Enabled = true;
		_session.MarkDirty();
	}

	private void Restart()
	{
		CallSave();
		Unload();
		_crashed = false;
		_compileFailed = false;
		_nextHostCheck = 0;
		DetailedInfo = "";
	}

	public void SetCode(string code)
	{
		Entry.Code = code ?? "";
		Restart();
		_session.MarkDirty();
	}

	public void SetHost(MyTerminalBlock block)
	{
		Restart();
		Host = null;
		Entry.HostId = block?.EntityId ?? 0;
		Entry.HostName = block?.CustomName?.ToString() ?? "";
		Entry.HostGridName = block?.CubeGrid?.DisplayName ?? "";
		_session.MarkDirty();
	}

	public void SetCustomData(string value)
	{
		value = value ?? "";
		if (Entry.CustomData != value)
		{
			Entry.CustomData = value;
			_session.MarkDirty();
		}
	}

	public void Rename(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		name = name.Trim();
		if (name != Entry.Name)
		{
			Entry.Name = name;
			_session.OnRenamed(this);
		}
	}

	/// <summary>
	/// Stops the script for good (deleted, or the world is unloading). callSave is false when the world is unloading:
	/// Save() already ran while the grids still existed, and by now they are being torn down.
	/// </summary>
	public void Shutdown(bool callSave = true)
	{
		if (callSave)
		{
			try
			{
				CallSave();
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Saving {Entry.Name}: {e.Message}");
			}
		}
		Unload();
		ReplaceAssembly(null, null);
	}
}
