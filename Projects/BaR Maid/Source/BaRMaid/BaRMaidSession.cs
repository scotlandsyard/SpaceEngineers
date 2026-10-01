using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using TimShared;
using VRageMath;

namespace BaRMaid;

/// <summary>
/// Finds the Build and Repair systems this player can access, sorts them into groups, and keeps each group's
/// assemblers stocked with what the systems are missing. Runs on the client only; every change it makes goes
/// through the same server requests the terminal uses.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class BaRMaidSession : MySessionComponentBase
{
	public const string ChatSender = "BaR Maid";

	private const string ChatCommand = "/barmaid";

	private const string MenuActionId = "BaRMaid_OpenMenu";

	private const string AutoQueueToggleId = "BaRMaid_AutoQueueToggle";

	private const string AutoQueueOnId = "BaRMaid_AutoQueueOn";

	private const string AutoQueueOffId = "BaRMaid_AutoQueueOff";

	/// <summary>Ticks between the starts of two passes looking for Build and Repair systems.</summary>
	private const int DiscoveryIntervalTicks = 300;

	/// <summary>Grids checked per tick during a pass, so big worlds don't stall a frame.</summary>
	private const int GridsPerTick = 25;

	/// <summary>Ticks between auto-queue steps; each step handles one group.</summary>
	private const int QueueStepTicks = 20;

	/// <summary>Seconds between two auto-queue checks of the same group.</summary>
	private const double QueueIntervalSeconds = 3.0;

	internal static BaRMaidSession Instance { get; private set; }

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	/// <summary>Groups, sorted by label.</summary>
	internal IReadOnlyList<MaidGroup> Groups => _sortedGroups;

	/// <summary>Goes up whenever the set of groups or their members change.</summary>
	internal int GroupsVersion { get; private set; }

	private readonly Dictionary<string, MaidGroup> _groups = new Dictionary<string, MaidGroup>(StringComparer.Ordinal);

	private readonly List<MaidGroup> _sortedGroups = new List<MaidGroup>();

	private readonly HashSet<long> _knownSystems = new HashSet<long>();

	private readonly List<IMyCubeGrid> _pendingGrids = new List<IMyCubeGrid>();

	private readonly List<IMyShipWelder> _foundSystems = new List<IMyShipWelder>();

	private string _groupsSignature = "";

	private int _pendingIndex = -1;

	private int _tick;

	private int _nextDiscoveryTick;

	private int _queueCursor;

	private readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

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
			CreateActions();
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register("BaR Maid", () => OpenMenu(null));
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] BeforeStart failed: {ex}");
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
				PluginSwitcher.Unregister();
			}
			catch
			{
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
			if (_tick % QueueStepTicks == 0)
			{
				StepAutoQueue();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] Update: {ex}");
		}
	}

	private void CreateActions()
	{
		AddAction(MenuActionId, "BaR Maid", "Start", b => OpenMenu(b), null);
		AddAction(AutoQueueToggleId, "BaR Maid Auto-queue On/Off", "Toggle", b => SetAutoQueueFromToolbar(b, null), AutoQueueWriter);
		AddAction(AutoQueueOnId, "BaR Maid Auto-queue On", "SwitchOn", b => SetAutoQueueFromToolbar(b, true), AutoQueueWriter);
		AddAction(AutoQueueOffId, "BaR Maid Auto-queue Off", "SwitchOff", b => SetAutoQueueFromToolbar(b, false), AutoQueueWriter);
	}

	private void AddAction(string id, string name, string icon, Action<IMyShipWelder> run, Action<IMyTerminalBlock, StringBuilder> writer)
	{
		IMyTerminalAction action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(id);
		action.Name = new StringBuilder(name);
		action.Icon = $"Textures\\GUI\\Icons\\Actions\\{icon}.dds";
		action.ValidForGroups = false;
		action.Enabled = (IMyTerminalBlock b) => b is IMyShipWelder welder && IsKnownSystem(welder);
		action.Action = (IMyTerminalBlock b) =>
		{
			if (b is IMyShipWelder welder)
			{
				run(welder);
			}
		};
		if (writer != null)
		{
			action.Writer = writer;
		}
		_actions.Add(action);
	}

	/// <summary>Shows On/Off under the toolbar slot.</summary>
	private void AutoQueueWriter(IMyTerminalBlock block, StringBuilder text)
	{
		MaidGroup group = GroupOf(block);
		text.Append(group == null ? "-" : group.AutoQueue ? "On" : "Off");
	}

	/// <summary>Sets auto-queue for the block's group; null flips it.</summary>
	private void SetAutoQueueFromToolbar(IMyShipWelder block, bool? enabled)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		MaidGroup group = GroupOf(block);
		if (group == null)
		{
			RefreshNow();
			group = GroupOf(block);
		}
		if (group == null)
		{
			MyAPIGateway.Utilities.ShowNotification($"BaR Maid: {block.CustomName} isn't in a group.", 3000, MyFontEnum.Red);
			return;
		}
		SetAutoQueue(group, enabled ?? !group.AutoQueue);
		MyAPIGateway.Utilities.ShowNotification($"BaR Maid: auto-queue {(group.AutoQueue ? "on" : "off")} for {group.Label}", 3000);
	}

	private MaidGroup GroupOf(IMyTerminalBlock block)
	{
		return block is IMyShipWelder welder ? _sortedGroups.FirstOrDefault(g => g.Systems.Contains(welder)) : null;
	}

	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if (block is IMyShipWelder welder && IsKnownSystem(welder))
			{
				foreach (IMyTerminalAction action in _actions)
				{
					if (!actions.Contains(action))
					{
						actions.Add(action);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] CustomActionGetter: {ex}");
		}
	}

	private bool IsKnownSystem(IMyShipWelder welder)
	{
		return _knownSystems.Contains(welder.EntityId) || BarApi.IsBar(welder);
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		if (messageText != null && messageText.Trim().Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			sendToOthers = false;
			OpenMenu(null);
		}
	}

	internal void OpenMenu(IMyShipWelder fromBlock)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			RefreshNow();
			MyGuiSandbox.AddScreen(new MaidScreen(PickGroupKey(fromBlock)));
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the menu: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[BaRMaid] {ex}");
		}
	}

	/// <summary>
	/// The group to show first: the one holding the block the menu was opened from, else one on the grid the
	/// player is controlling, else the nearest one.
	/// </summary>
	private string PickGroupKey(IMyShipWelder fromBlock)
	{
		if (fromBlock != null)
		{
			MaidGroup owner = _sortedGroups.FirstOrDefault(g => g.Systems.Contains(fromBlock));
			if (owner != null)
			{
				return owner.Key;
			}
		}
		if (MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity is IMyCubeBlock controlled)
		{
			IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(controlled.CubeGrid);
			MaidGroup onGrid = _sortedGroups.FirstOrDefault(g => g.TerminalSystem == terminal);
			if (onGrid != null)
			{
				return onGrid.Key;
			}
		}
		Vector3D position = MyAPIGateway.Session.Player.GetPosition();
		return _sortedGroups.Where(g => g.FirstSystem != null).OrderBy(g => Vector3D.DistanceSquared(position, g.FirstSystem.GetPosition())).FirstOrDefault()?.Key;
	}

	/// <summary>Runs a whole discovery pass at once (used when the menu opens or after a setup change).</summary>
	internal void RefreshNow()
	{
		StartPass();
		foreach (IMyCubeGrid grid in _pendingGrids)
		{
			ScanGrid(grid);
		}
		FinishPass();
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
		_foundSystems.Clear();
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
		// Skip closed grids, projections and build previews.
		if (grid.Closed || !(grid is MyCubeGrid cubeGrid) || cubeGrid.Projector != null || cubeGrid.IsPreview)
		{
			return;
		}
		foreach (MyCubeBlock block in cubeGrid.GetFatBlocks())
		{
			if (block is IMyShipWelder welder && BarApi.IsBar(welder))
			{
				_foundSystems.Add(welder);
			}
		}
	}

	private void FinishPass()
	{
		_pendingIndex = -1;
		_nextDiscoveryTick = _tick + DiscoveryIntervalTicks;
		_knownSystems.Clear();
		foreach (IMyShipWelder welder in _foundSystems)
		{
			_knownSystems.Add(welder.EntityId);
		}
		RebuildGroups();
	}

	private void RebuildGroups()
	{
		// A construct is the grids joined by rotors, pistons and hinges. Ships docked by connector share the terminal
		// system but are constructs of their own, so their assemblers are never used for this one's systems.
		Dictionary<long, Construct> constructs = new Dictionary<long, Construct>();
		List<IMyCubeGrid> linked = new List<IMyCubeGrid>();
		foreach (IMyShipWelder system in _foundSystems)
		{
			if (system.Closed || !system.HasLocalPlayerAccess())
			{
				continue;
			}
			linked.Clear();
			MyAPIGateway.GridGroups.GetGroup(system.CubeGrid, GridLinkTypeEnum.Mechanical, linked);
			if (linked.Count == 0)
			{
				linked.Add(system.CubeGrid);
			}
			long id = linked.Min(g => g.EntityId);
			if (!constructs.TryGetValue(id, out Construct construct))
			{
				IMyGridTerminalSystem terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(system.CubeGrid);
				if (terminal == null)
				{
					continue;
				}
				construct = new Construct { Terminal = terminal, Grids = new HashSet<IMyCubeGrid>(linked) };
				constructs[id] = construct;
			}
			construct.Systems.Add(system);
		}

		Dictionary<string, MaidGroup> rebuilt = new Dictionary<string, MaidGroup>(StringComparer.Ordinal);
		foreach (Construct construct in constructs.Values)
		{
			BuildConstructGroups(construct, rebuilt);
		}

		_groups.Clear();
		foreach (KeyValuePair<string, MaidGroup> entry in rebuilt)
		{
			_groups[entry.Key] = entry.Value;
		}
		_sortedGroups.Clear();
		_sortedGroups.AddRange(_groups.Values.OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase));

		StringBuilder signature = new StringBuilder();
		foreach (MaidGroup group in _sortedGroups)
		{
			signature.Append(group.Key).Append(group.AutoQueue).Append(group.GridName);
			foreach (IMyShipWelder system in group.Systems)
			{
				signature.Append(',').Append(system.EntityId);
			}
			foreach (IMyAssembler assembler in group.Assemblers)
			{
				signature.Append(';').Append(assembler.EntityId);
			}
			signature.Append('|');
		}
		if (signature.ToString() != _groupsSignature)
		{
			_groupsSignature = signature.ToString();
			GroupsVersion++;
		}
	}

	/// <summary>Sorts one construct's systems and assemblers into groups by their Custom Data.</summary>
	private class Construct
	{
		public IMyGridTerminalSystem Terminal;

		public HashSet<IMyCubeGrid> Grids;

		public readonly List<IMyShipWelder> Systems = new List<IMyShipWelder>();
	}

	private void BuildConstructGroups(Construct construct, Dictionary<string, MaidGroup> rebuilt)
	{
		IMyGridTerminalSystem terminal = construct.Terminal;
		List<IMyShipWelder> systems = construct.Systems;
		// Name groups after the biggest grid of the construct and key them by its entity id, so the key survives
		// renaming and stays the same from pass to pass.
		IMyCubeGrid mainGrid = systems.Select(s => s.CubeGrid).OrderByDescending(g => ((MyCubeGrid)g).BlocksCount).ThenBy(g => g.EntityId).First();
		List<IMyAssembler> assemblers = new List<IMyAssembler>();
		terminal.GetBlocksOfType(assemblers, a => !a.Closed && construct.Grids.Contains(a.CubeGrid) && a.HasLocalPlayerAccess() && !a.BlockDefinition.TypeIdString.Contains("SurvivalKit"));
		List<IMyTerminalBlock> constructBlocks = new List<IMyTerminalBlock>();
		constructBlocks.AddRange(systems);
		constructBlocks.AddRange(assemblers);

		Dictionary<string, MaidGroup> local = new Dictionary<string, MaidGroup>(StringComparer.OrdinalIgnoreCase);
		foreach (IMyShipWelder system in systems)
		{
			string name = MaidConfig.GetGroup(system);
			if (MaidConfig.IsNoGroup(name))
			{
				continue;
			}
			bool isDefault = MaidConfig.IsDefaultGroup(name);
			if (isDefault)
			{
				name = MaidConfig.DefaultGroup;
			}
			if (!local.TryGetValue(name, out MaidGroup group))
			{
				string key = mainGrid.EntityId + "|" + name.ToUpperInvariant();
				if (!_groups.TryGetValue(key, out group))
				{
					group = new MaidGroup { Key = key };
				}
				group.Name = name;
				group.IsDefault = isDefault;
				group.GridName = mainGrid.CustomName;
				group.TerminalSystem = terminal;
				group.ConstructBlocks = constructBlocks;
				group.Systems.Clear();
				group.Assemblers.Clear();
				group.AutoQueue = false;
				local[name] = group;
				rebuilt[key] = group;
			}
			group.Systems.Add(system);
			group.AutoQueue |= MaidConfig.GetAutoQueue(system);
		}

		foreach (IMyAssembler assembler in assemblers)
		{
			// Assemblers are opt-in: only ones assigned to a group (Default included) ever get work.
			string name = MaidConfig.GetGroup(assembler);
			if (name == null || MaidConfig.IsNoGroup(name))
			{
				continue;
			}
			if (local.TryGetValue(MaidConfig.IsDefaultGroup(name) ? MaidConfig.DefaultGroup : name, out MaidGroup group))
			{
				group.Assemblers.Add(assembler);
			}
		}
	}

	internal MaidGroup FindGroup(string key)
	{
		return key != null && _groups.TryGetValue(key, out MaidGroup group) ? group : null;
	}

	/// <summary>Switches auto-queuing for a group. The setting lives in the systems' Custom Data, so it syncs to the server and other players.</summary>
	internal void SetAutoQueue(MaidGroup group, bool enabled)
	{
		foreach (IMyShipWelder system in group.LiveSystems)
		{
			MaidConfig.Set(system, MaidConfig.AutoQueueKey, enabled ? "true" : null);
		}
		group.AutoQueue = enabled;
		group.NextQueueCheck = Now;
		group.AddLog(enabled ? "Auto-queue switched on" : "Auto-queue switched off");
	}

	/// <summary>Moves a block into a group (null or Default = the Default group, None = no group).</summary>
	internal void AssignBlock(IMyTerminalBlock block, string groupName)
	{
		string name = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
		// A system with no Group line is in Default and needs Group=None to leave; an assembler is the other way
		// round, unused until it has a Group line, so Default is written out for it.
		bool isSystem = block is IMyShipWelder;
		string value = MaidConfig.IsNoGroup(name) ? (isSystem ? MaidConfig.NoGroup : null)
			: MaidConfig.IsDefaultGroup(name) ? (isSystem ? null : MaidConfig.DefaultGroup)
			: name;
		MaidConfig.Set(block, MaidConfig.GroupKey, value);
		if (block is IMyShipWelder system)
		{
			// A moved system takes on its new group's auto-queue setting instead of switching the group's.
			MaidGroup target = _sortedGroups.FirstOrDefault(g => g.TerminalSystem != null && g.ConstructBlocks.Contains(block) && g.Name.Equals(MaidConfig.IsDefaultGroup(name) ? MaidConfig.DefaultGroup : name, StringComparison.OrdinalIgnoreCase));
			MaidConfig.Set(system, MaidConfig.AutoQueueKey, target != null && target.AutoQueue ? "true" : null);
		}
		RefreshNow();
	}

	private void StepAutoQueue()
	{
		if (_sortedGroups.Count == 0)
		{
			return;
		}
		double now = Now;
		for (int i = 0; i < _sortedGroups.Count; i++)
		{
			MaidGroup group = _sortedGroups[(_queueCursor + i) % _sortedGroups.Count];
			if (group.AutoQueue && now >= group.NextQueueCheck)
			{
				_queueCursor = (_queueCursor + i + 1) % _sortedGroups.Count;
				group.NextQueueCheck = now + QueueIntervalSeconds;
				group.QueueMissing(now);
				return;
			}
		}
	}
}
