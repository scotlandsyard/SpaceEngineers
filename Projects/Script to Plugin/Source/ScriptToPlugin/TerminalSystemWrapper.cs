using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using IngameCubeGrid = VRage.Game.ModAPI.Ingame.IMyCubeGrid;
using ModTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using ModCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace ScriptToPlugin;

/// <summary>
/// The script's GridTerminalSystem. Like the programmable block's own wrapper it stays the same object for the
/// script's whole life while the terminal system behind it can change (grids docking and undocking). Blocks are
/// filtered with the local player as owner, the same way a programmable block filters them with its owner.
/// </summary>
internal class TerminalSystemWrapper : IMyGridTerminalSystem
{
	private readonly VirtualProgram _program;

	private IMyGridTerminalSystem _terminal;

	public TerminalSystemWrapper(VirtualProgram program)
	{
		_program = program;
	}

	internal void SetInstance(MyGridTerminalSystem terminal)
	{
		_terminal = terminal;
	}

	private IMyGridTerminalSystem Terminal => _terminal ?? throw new InvalidOperationException("GridTerminalSystem can only be used while the script runs.");

	public void GetBlocks(List<IMyTerminalBlock> blocks)
	{
		Terminal.GetBlocks(blocks);
	}

	public void GetBlockGroups(List<IMyBlockGroup> blockGroups, Func<IMyBlockGroup, bool> collect = null)
	{
		Terminal.GetBlockGroups(blockGroups, collect);
	}

	public void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null) where T : class
	{
		Terminal.GetBlocksOfType<T>(blocks, collect);
	}

	public void GetBlocksOfType<T>(List<T> blocks, Func<T, bool> collect = null) where T : class
	{
		Terminal.GetBlocksOfType(blocks, collect);
	}

	public void SearchBlocksOfName(string name, List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
	{
		Terminal.SearchBlocksOfName(name, blocks, collect);
	}

	public IMyTerminalBlock GetBlockWithName(string name)
	{
		return Terminal.GetBlockWithName(name);
	}

	public IMyBlockGroup GetBlockGroupWithName(string name)
	{
		return Terminal.GetBlockGroupWithName(name);
	}

	public IMyTerminalBlock GetBlockWithId(long id)
	{
		return Terminal.GetBlockWithId(id);
	}

	public bool CanAccess(IMyTerminalBlock block, MyTerminalAccessScope scope = MyTerminalAccessScope.Construct)
	{
		MyTerminalBlock host = _program.Host;
		if (block == null || block.Closed || host == null || !block.HasPlayerAccess(ScriptSession.LocalIdentityId))
		{
			return false;
		}
		switch (scope)
		{
		case MyTerminalAccessScope.All:
			return block is ModTerminalBlock modBlock && modBlock.IsInSameLogicalGroupAs(host);
		case MyTerminalAccessScope.Construct:
			return block.IsSameConstructAs(host);
		case MyTerminalAccessScope.Grid:
			return block.CubeGrid == host.CubeGrid;
		default:
			throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
		}
	}

	public bool CanAccess(IngameCubeGrid grid, MyTerminalAccessScope scope = MyTerminalAccessScope.Construct)
	{
		MyTerminalBlock host = _program.Host;
		if (grid == null || grid.Closed || host == null || !(grid is MyCubeGrid cubeGrid) || !cubeGrid.BigOwners.Contains(ScriptSession.LocalIdentityId))
		{
			return false;
		}
		switch (scope)
		{
		case MyTerminalAccessScope.All:
			return ((ModCubeGrid)grid).IsInSameLogicalGroupAs(host.CubeGrid);
		case MyTerminalAccessScope.Construct:
			return grid.IsSameConstructAs(host.CubeGrid);
		case MyTerminalAccessScope.Grid:
			return grid == host.CubeGrid;
		default:
			throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
		}
	}
}
