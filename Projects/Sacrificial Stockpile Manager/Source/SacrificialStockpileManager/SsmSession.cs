using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IngameSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace SacrificialStockpileManager;

/// <summary>
/// Finds the player's ships and stations, keeps their snapshots fresh while they're loaded, and runs the stock
/// engine and autocraft on them. Runs on the client only; every change goes through the same server requests
/// the terminal uses.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class SsmSession : MySessionComponentBase
{
	public const string ChatSender = "SSM";

	private const string ChatCommand = "/ssm";

	private const string MenuActionId = "SacrificialStockpileManager_OpenMenu";

	/// <summary>Ticks between the starts of two passes looking for the player's grids.</summary>
	private const int DiscoveryIntervalTicks = 300;

	/// <summary>Grids checked per tick during a pass, so big worlds don't stall a frame.</summary>
	private const int GridsPerTick = 25;

	/// <summary>Ticks between steps; each step handles at most one construct.</summary>
	private const int StepTicks = 10;

	private const double RefreshSeconds = 3.0;

	/// <summary>The construct open in the menu refreshes faster so the tables feel live.</summary>
	private const double ViewedRefreshSeconds = 1.0;

	private const double EngineSeconds = 3.0;

	private const double CraftSeconds = 5.0;

	private const int DisplayTicks = 30;

	private const int SaveTicks = 60 * 120;

	internal static SsmSession Instance { get; private set; }

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	/// <summary>Key of the grid shown in the menu, if it's open.</summary>
	internal long ViewedKey;

	/// <summary>Goes up when the set of loaded constructs changes.</summary>
	internal int ConstructsVersion { get; private set; }

	private readonly Dictionary<IMyGridTerminalSystem, Construct> _constructs = new Dictionary<IMyGridTerminalSystem, Construct>();

	private readonly List<Construct> _list = new List<Construct>();

	private readonly List<IMyCubeGrid> _pendingGrids = new List<IMyCubeGrid>();

	private readonly Dictionary<IMyGridTerminalSystem, List<IMyCubeGrid>> _found = new Dictionary<IMyGridTerminalSystem, List<IMyCubeGrid>>();

	private int _pendingIndex = -1;

	private int _tick;

	private int _nextDiscoveryTick;

	private int _stepCursor;

	private IMyTerminalAction _menuAction;

	private bool _started;

	internal IReadOnlyList<Construct> Constructs => _list;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Instance = this;
			Store.Load();
			_menuAction = new MyTerminalAction<MyTerminalBlock>(MenuActionId, new StringBuilder("Stockpile Manager"), block => OpenMenu(block), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			};
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		if (_started)
		{
			try
			{
				MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
				MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
				Store.Unload();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[SSM] Unload: {ex}");
			}
		}
		_started = false;
		Instance = null;
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		_tick++;
		try
		{
			StepDiscovery();
			if (_tick % StepTicks == 0)
			{
				StepConstruct();
			}
			if (_tick % DisplayTicks == 5)
			{
				Displays.Update(this);
			}
			if (_tick % SaveTicks == 0)
			{
				Store.SaveGridsIfDirty();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] Update: {ex}");
		}
	}

	// ---- Menu, toolbar action and chat command ----

	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if ((block.HasInventory || (block is IngameSurfaceProvider provider && provider.SurfaceCount > 0)) && block.HasLocalPlayerAccess() && !actions.Contains(_menuAction))
			{
				actions.Add(_menuAction);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[SSM] CustomActionGetter: {ex}");
		}
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		if (messageText != null && messageText.Trim().Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			sendToOthers = false;
			OpenMenu(null);
		}
	}

	internal void OpenMenu(IMyTerminalBlock fromBlock)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			RefreshNow();
			long gridKey = PickGrid(fromBlock);
			MyGuiSandbox.AddScreen(new SsmScreen(gridKey, fromBlock));
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the menu: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[SSM] {ex}");
		}
	}

	/// <summary>The grid to show first: the block's, else the one the player controls, else the nearest loaded one.</summary>
	private long PickGrid(IMyTerminalBlock fromBlock)
	{
		Construct construct = fromBlock != null ? ConstructOf(fromBlock) : null;
		if (construct == null && MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity is IMyCubeBlock controlled)
		{
			construct = ConstructOf(controlled);
		}
		if (construct == null)
		{
			Vector3D position = MyAPIGateway.Session.Player.GetPosition();
			construct = _list.Where(c => c.Snapshot != null).OrderBy(c => Vector3D.DistanceSquared(position, c.Snapshot.Position)).FirstOrDefault();
		}
		return construct?.Key ?? 0;
	}

	// ---- Lookups ----

	internal Construct ConstructOf(IMyCubeBlock block)
	{
		if (block?.CubeGrid == null)
		{
			return null;
		}
		IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(block.CubeGrid);
		return terminal != null && _constructs.TryGetValue(terminal, out Construct construct) ? construct : null;
	}

	internal Construct FindConstruct(long key)
	{
		return key == 0 ? null : _list.FirstOrDefault(c => c.Key == key);
	}

	// ---- Discovery ----

	/// <summary>Finds grids and refreshes every construct at once (used when the menu opens or on Rescan).</summary>
	internal void RefreshNow()
	{
		StartPass();
		foreach (IMyCubeGrid grid in _pendingGrids)
		{
			ScanGrid(grid);
		}
		FinishPass();
		foreach (Construct construct in _list)
		{
			RefreshConstruct(construct, Now);
		}
	}

	private void StepDiscovery()
	{
		if (_pendingIndex < 0)
		{
			if (_tick < _nextDiscoveryTick)
			{
				return;
			}
			StartPass();
		}
		int end = Math.Min(_pendingIndex + GridsPerTick, _pendingGrids.Count);
		for (int i = _pendingIndex; i < end; i++)
		{
			ScanGrid(_pendingGrids[i]);
		}
		_pendingIndex = end;
		if (_pendingIndex >= _pendingGrids.Count)
		{
			FinishPass();
		}
	}

	private void StartPass()
	{
		_pendingGrids.Clear();
		_found.Clear();
		MyAPIGateway.Entities.GetEntities(null, delegate (IMyEntity e)
		{
			if (e is IMyCubeGrid grid)
			{
				_pendingGrids.Add(grid);
			}
			return false;
		});
		_pendingIndex = 0;
	}

	private void ScanGrid(IMyCubeGrid grid)
	{
		// Skip closed grids, projections and build previews, and anything the player owns no part of.
		if (grid.Closed || !(grid is MyCubeGrid cubeGrid) || cubeGrid.Projector != null || cubeGrid.IsPreview)
		{
			return;
		}
		long identity = MyAPIGateway.Session.Player.IdentityId;
		if (!grid.SmallOwners.Contains(identity))
		{
			return;
		}
		IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
		if (terminal == null)
		{
			return;
		}
		if (!_found.TryGetValue(terminal, out List<IMyCubeGrid> grids))
		{
			grids = new List<IMyCubeGrid>();
			_found[terminal] = grids;
		}
		grids.Add(grid);
	}

	private void FinishPass()
	{
		_pendingIndex = -1;
		_nextDiscoveryTick = _tick + DiscoveryIntervalTicks;
		bool changed = false;
		foreach (KeyValuePair<IMyGridTerminalSystem, List<IMyCubeGrid>> entry in _found)
		{
			List<IMyCubeGrid> grids = entry.Value.Where(g => !g.Closed).ToList();
			if (grids.Count == 0)
			{
				continue;
			}
			// Named after and keyed by the biggest grid, so the key stays the same from pass to pass.
			IMyCubeGrid main = grids.OrderByDescending(g => ((MyCubeGrid)g).BlocksCount).ThenBy(g => g.EntityId).First();
			if (!_constructs.TryGetValue(entry.Key, out Construct construct))
			{
				construct = new Construct { Terminal = entry.Key };
				_constructs[entry.Key] = construct;
				changed = true;
			}
			if (construct.Key != main.EntityId)
			{
				changed = true;
			}
			construct.Key = main.EntityId;
			construct.MainGrid = main;
			construct.Grids = grids;
		}
		foreach (IMyGridTerminalSystem gone in _constructs.Keys.Where(t => !_found.ContainsKey(t)).ToList())
		{
			_constructs.Remove(gone);
			changed = true;
		}
		if (changed)
		{
			_list.Clear();
			_list.AddRange(_constructs.Values.OrderBy(c => c.MainGrid.CustomName, StringComparer.OrdinalIgnoreCase));
			ConstructsVersion++;
		}
	}

	// ---- Work ----

	/// <summary>Handles the next construct that is due: refresh its snapshot, then run the engine and autocraft when their turn comes.</summary>
	private void StepConstruct()
	{
		if (_list.Count == 0)
		{
			return;
		}
		double now = Now;
		for (int i = 0; i < _list.Count; i++)
		{
			Construct construct = _list[(_stepCursor + i) % _list.Count];
			if (now < construct.NextRefresh)
			{
				continue;
			}
			_stepCursor = (_stepCursor + i + 1) % _list.Count;
			RefreshConstruct(construct, now);
			GridRules rules = Store.GridRules(construct.Snapshot);
			if (now >= construct.NextEngine)
			{
				construct.NextEngine = now + EngineSeconds;
				StockEngine engine = StockEngine.Run(construct, rules, now);
				construct.Warnings = engine.Warnings;
			}
			if (now >= construct.NextCraft)
			{
				construct.NextCraft = now + CraftSeconds;
				AutoCraft.Run(construct, rules, now);
			}
			return;
		}
	}

	private void RefreshConstruct(Construct construct, double now)
	{
		if (construct.MainGrid == null || construct.MainGrid.Closed)
		{
			return;
		}
		construct.Refresh();
		construct.NextRefresh = now + (construct.Key == ViewedKey ? ViewedRefreshSeconds : RefreshSeconds);
	}

	/// <summary>Runs the engine on a construct straight away (after a settings change in the menu).</summary>
	internal void RunNow(Construct construct)
	{
		if (construct == null)
		{
			return;
		}
		double now = Now;
		RefreshConstruct(construct, now);
		GridRules rules = Store.GridRules(construct.Snapshot);
		construct.NextEngine = now + EngineSeconds;
		construct.Warnings = StockEngine.Run(construct, rules, now).Warnings;
		construct.NextCraft = now + CraftSeconds;
		AutoCraft.Run(construct, rules, now);
	}
}
