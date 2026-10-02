using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TimShared;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace FatAlbert;

/// <summary>
/// Wires up the window: a toolbar action on cockpits and remote controls, the /fat chat command, and the plugin
/// switcher. Client only, and it never changes anything on the ship, so it's fine on any server.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class FatAlbertSession : MySessionComponentBase
{
	public const string ChatSender = "Fat Albert";

	private static readonly string[] ChatCommands = { "/fat", "/fatalbert" };

	private const string MenuActionId = "FatAlbert_OpenMenu";

	internal static FatAlbertSession Instance { get; private set; }

	internal static double Now => MyAPIGateway.Session?.ElapsedPlayTime.TotalSeconds ?? 0.0;

	private IMyTerminalAction _menuAction;

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
			Settings.Load();
			PlanetNames.Load();
			_menuAction = new MyTerminalAction<MyTerminalBlock>(MenuActionId, new StringBuilder("Fat Albert: lift-off check"), block => OpenMenu(block), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false
			};
			MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register(Plugin.Name, () => OpenMenu(null));
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] BeforeStart failed: {ex}");
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
				Settings.Save();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Fat Albert] Unload: {ex}");
			}
		}
		ShipReader.Clear();
		PlanetNames.Unload();
		_started = false;
		Instance = null;
	}

	private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
	{
		try
		{
			if (block is IMyShipController && block.HasLocalPlayerAccess() && !actions.Contains(_menuAction))
			{
				actions.Add(_menuAction);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] CustomActionGetter: {ex}");
		}
	}

	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		string[] words = (messageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0 || Array.FindIndex(ChatCommands, c => c.Equals(words[0], StringComparison.OrdinalIgnoreCase)) < 0)
		{
			return;
		}
		sendToOthers = false;
		OpenMenu(null);
	}

	internal void OpenMenu(IMyTerminalBlock fromBlock)
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			long key = fromBlock?.CubeGrid != null ? ShipReader.MainGrid(fromBlock.CubeGrid).EntityId : 0;
			MyGuiSandbox.AddScreen(new FatAlbertScreen(key));
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] {ex}");
		}
	}
}
