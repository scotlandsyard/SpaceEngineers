using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game;
using VRage.Game.Components.Interfaces;
using VRage.ObjectBuilders;
using VRageMath;
using IngameCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IngameCubeGrid = VRage.Game.ModAPI.Ingame.IMyCubeGrid;
using IngameEntity = VRage.Game.ModAPI.Ingame.IMyEntity;
using IngameInventory = VRage.Game.ModAPI.Ingame.IMyInventory;

namespace ScriptToPlugin;

/// <summary>
/// What the script sees as Me. Where it is and what it's attached to (grid, position, owner, screens) comes from
/// the host block. What a programmable block would hold itself (name, Custom Data, on/off, detailed info, run
/// argument) belongs to the script, so a script can't rename, reconfigure or switch off its host by accident.
/// </summary>
internal class ProgramBlockProxy : IMyProgrammableBlock
{
	private readonly VirtualProgram _program;

	public ProgramBlockProxy(VirtualProgram program)
	{
		_program = program;
	}

	private MyTerminalBlock Host => _program.Host ?? throw new InvalidOperationException("The host block of this script isn't loaded.");

	private IMyTerminalBlock HostTerminal => Host;

	private IngameCubeBlock HostCube => Host;

	private IngameEntity HostEntity => Host;

	// IMyProgrammableBlock

	public bool IsRunning => _program.IsRunning;

	public string TerminalRunArgument => _program.Entry.Argument ?? "";

	public bool TryRun(string argument)
	{
		return _program.TryRunFromScript(argument);
	}

	// IMyFunctionalBlock

	public bool Enabled
	{
		get => _program.Entry.Enabled;
		set => _program.RequestEnabled(value);
	}

	public void RequestEnable(bool enable)
	{
		_program.RequestEnabled(enable);
	}

	// IMyTerminalBlock

	public string CustomName
	{
		get => _program.Entry.Name;
		set => SetCustomName(value);
	}

	public string CustomNameWithFaction => CustomName;

	public string DetailedInfo => _program.DetailedInfo;

	public string CustomInfo => "";

	public string CustomData
	{
		get => _program.Entry.CustomData;
		set => _program.SetCustomData(value);
	}

	public bool ShowOnHUD
	{
		get => HostTerminal.ShowOnHUD;
		set { }
	}

	public bool ShowInTerminal
	{
		get => HostTerminal.ShowInTerminal;
		set { }
	}

	public bool ShowInToolbarConfig
	{
		get => HostTerminal.ShowInToolbarConfig;
		set { }
	}

	public bool ShowInInventory
	{
		get => HostTerminal.ShowInInventory;
		set { }
	}

	public bool HasLocalPlayerAccess()
	{
		return HostTerminal.HasLocalPlayerAccess();
	}

	public bool HasPlayerAccess(long playerId, MyRelationsBetweenPlayerAndBlock defaultNoUser = MyRelationsBetweenPlayerAndBlock.NoOwnership)
	{
		return HostTerminal.HasPlayerAccess(playerId, defaultNoUser);
	}

	public bool HasNobodyPlayerAccessToBlock()
	{
		return HostTerminal.HasNobodyPlayerAccessToBlock();
	}

	public bool HasPlayerAccessWithNobodyCheck(long playerId, bool isForPB = false)
	{
		return HostTerminal.HasPlayerAccessWithNobodyCheck(playerId, isForPB);
	}

	public void SetCustomName(string text)
	{
		_program.Rename(text);
	}

	public void SetCustomName(StringBuilder text)
	{
		_program.Rename(text?.ToString());
	}

	// Me has no terminal actions or properties of its own: handing out the host's would let a script switch the
	// host off when it meant to switch itself off.
	public void GetActions(List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null)
	{
	}

	public void SearchActionsOfName(string name, List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null)
	{
	}

	public ITerminalAction GetActionWithName(string name)
	{
		return null;
	}

	public ITerminalProperty GetProperty(string id)
	{
		return null;
	}

	public void GetProperties(List<ITerminalProperty> resultList, Func<ITerminalProperty, bool> collect = null)
	{
	}

	public bool IsSameConstructAs(IMyTerminalBlock other)
	{
		return HostTerminal.IsSameConstructAs(other);
	}

	// IMyCubeBlock

	public SerializableDefinitionId BlockDefinition => HostCube.BlockDefinition;

	public IngameCubeGrid CubeGrid => HostCube.CubeGrid;

	public string DefinitionDisplayNameText => HostCube.DefinitionDisplayNameText;

	public float DisassembleRatio => HostCube.DisassembleRatio;

	public string DisplayNameText => CustomName;

	public bool IsBeingHacked => HostCube.IsBeingHacked;

	public bool IsFunctional => HostCube.IsFunctional;

	public bool IsWorking => HostCube.IsWorking;

	public Vector3I Max => HostCube.Max;

	public float Mass => HostCube.Mass;

	public Vector3I Min => HostCube.Min;

	public int NumberInGrid => HostCube.NumberInGrid;

	public MyBlockOrientation Orientation => HostCube.Orientation;

	public long OwnerId => HostCube.OwnerId;

	public Vector3I Position => HostCube.Position;

	public string GetOwnerFactionTag()
	{
		return HostCube.GetOwnerFactionTag();
	}

	// Obsolete in the game's interface, but scripts can still call it.
#pragma warning disable CS0618
	public MyRelationsBetweenPlayerAndBlock GetPlayerRelationToOwner()
	{
		return HostCube.GetPlayerRelationToOwner();
	}
#pragma warning restore CS0618

	public MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(long playerId, MyRelationsBetweenPlayerAndBlock defaultNoUser = MyRelationsBetweenPlayerAndBlock.NoOwnership)
	{
		return HostCube.GetUserRelationToOwner(playerId, defaultNoUser);
	}

	public void UpdateIsWorking()
	{
	}

	public void UpdateVisual()
	{
	}

	// IMyEntity

	public IMyEntityComponentContainer Components => HostEntity.Components;

	public long EntityId => HostEntity.EntityId;

	public string Name => HostEntity.Name;

	public string DisplayName => CustomName;

	public bool HasInventory => HostEntity.HasInventory;

	public int InventoryCount => HostEntity.InventoryCount;

	public bool Closed => _program.Host == null || HostEntity.Closed;

	public BoundingBoxD WorldAABB => HostEntity.WorldAABB;

	public BoundingBoxD WorldAABBHr => HostEntity.WorldAABBHr;

	public MatrixD WorldMatrix => HostEntity.WorldMatrix;

	public BoundingSphereD WorldVolume => HostEntity.WorldVolume;

	public BoundingSphereD WorldVolumeHr => HostEntity.WorldVolumeHr;

	public IngameInventory GetInventory()
	{
		return HostEntity.GetInventory();
	}

	public IngameInventory GetInventory(int index)
	{
		return HostEntity.GetInventory(index);
	}

	public Vector3D GetPosition()
	{
		return HostEntity.GetPosition();
	}

	// IMyTextSurfaceProvider: the host's screens, if it has any.

	public bool UseGenericLcd => Host is IMyTextSurfaceProvider provider && provider.UseGenericLcd;

	public int SurfaceCount => Host is IMyTextSurfaceProvider provider ? provider.SurfaceCount : 0;

	public IMyTextSurface GetSurface(int index)
	{
		return Host is IMyTextSurfaceProvider provider ? provider.GetSurface(index) : null;
	}
}
