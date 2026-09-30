using System;
using System.Diagnostics;
using Sandbox.Game.World;
using Sandbox.ModAPI.Ingame;
using VRage.Library.Compiler;

namespace ScriptToPlugin;

/// <summary>The script's Runtime property. Works like the programmable block's own runtime info.</summary>
internal class ProgramRuntimeInfo : IMyGridProgramRuntimeInfo
{
	private const long TimeSpanTicksPerFrame = 166666L;

	private static readonly double StopwatchMsFrequency = 1000.0 / Stopwatch.Frequency;

	private long _startTicks;

	private int _lastRunFrame;

	private int _firstRunFrame;

	private UpdateFrequency _updateFrequency;

	public IlInjector.ICounterHandle InjectorHandle { get; set; }

	public TimeSpan TimeSinceLastRun => new TimeSpan((Frame - _lastRunFrame) * TimeSpanTicksPerFrame);

	public double LastRunTimeMs { get; private set; }

	public int MaxInstructionCount => InjectorHandle?.MaxInstructionCount ?? VirtualProgram.MaxInstructions;

	public int CurrentInstructionCount => InjectorHandle?.InstructionCount ?? 0;

	public int MaxCallChainDepth => InjectorHandle?.MaxMethodCallCount ?? IlInjector.MAX_CALL_CHAIN_DEPTH;

	public int CurrentCallChainDepth => InjectorHandle?.MethodCallCount ?? 0;

	public long LifetimeTicks => Frame - _firstRunFrame;

	/// <summary>How often the script wants to run. The plugin clears Once after running it, like the game does.</summary>
	public UpdateFrequency UpdateFrequency
	{
		get => _updateFrequency;
		set
		{
			if ((value & ~(UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100 | UpdateFrequency.Once)) != 0)
			{
				throw new ArgumentException("Unsupported flags in UpdateFrequency");
			}
			_updateFrequency = value;
		}
	}

	private static int Frame => MySession.Static?.GameplayFrameCounter ?? 0;

	public void Reset()
	{
		_startTicks = 0L;
		LastRunTimeMs = 0.0;
		_lastRunFrame = Frame;
		_firstRunFrame = _lastRunFrame;
		_updateFrequency = UpdateFrequency.None;
	}

	public void BeginMainOperation()
	{
		_startTicks = Stopwatch.GetTimestamp();
	}

	public void EndMainOperation()
	{
		long now = Stopwatch.GetTimestamp();
		_lastRunFrame = Frame;
		LastRunTimeMs = (now - _startTicks) * StopwatchMsFrequency;
	}

	public void BeginSaveOperation()
	{
		LastRunTimeMs = 0.0;
	}
}

/// <summary>The script's World property.</summary>
internal class ProgramWorldInfo : IMyGridProgramWorldInfo
{
	public static readonly ProgramWorldInfo Default = new ProgramWorldInfo();

	public bool PressurizationEnabled => MySession.Static.Settings.EnableOxygen && MySession.Static.Settings.EnableOxygenPressurization;

	public float InventoryMultiplier => MySession.Static.Settings.BlocksInventorySizeMultiplier;

	public float LargeShipMaxSpeed => Sandbox.Game.World.MySector.EnvironmentDefinition.LargeShipMaxSpeed;

	public float LargeShipMaxAngularSpeed => Sandbox.Game.World.MySector.EnvironmentDefinition.LargeShipMaxAngularSpeedInRadians;

	public float SmallShipMaxSpeed => Sandbox.Game.World.MySector.EnvironmentDefinition.SmallShipMaxSpeed;

	public float SmallShipMaxAngularSpeed => Sandbox.Game.World.MySector.EnvironmentDefinition.SmallShipMaxAngularSpeedInRadians;
}
