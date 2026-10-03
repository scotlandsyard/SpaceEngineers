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
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
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
			Personality.Level = Settings.Chattiness;
			Personality.DisplayName = Settings.DisplayName;
			Personality.Changed += Chatter.SaveDisplayName;
			Personality.Register(ChatSender);
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
				Personality.Unregister();
				Personality.Changed -= Chatter.SaveDisplayName;
				Settings.Save();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Fat Albert] Unload: {ex}");
			}
		}
		Hud.Unload();
		ShipReader.Clear();
		Chatter.Reset();
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
		if (words.Length > 1 && words[1].Equals("hud", StringComparison.OrdinalIgnoreCase))
		{
			Hud.Toggle();
			return;
		}
		if (words.Length > 1 && words[1].Equals("chat", StringComparison.OrdinalIgnoreCase))
		{
			SetChatLevel(words.Length > 2 ? words[2] : null);
			return;
		}
		if (words.Length > 1 && words[1].Equals("name", StringComparison.OrdinalIgnoreCase))
		{
			// Everything after "/fat name", spaces kept; nothing resets it to his own name.
			int start = messageText.IndexOf(words[1], StringComparison.OrdinalIgnoreCase) + words[1].Length;
			Chatter.SetDisplayName(messageText.Substring(start));
			return;
		}
		OpenMenu(null);
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			Hud.Update();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] HUD update: {ex}");
		}
	}

	/// <summary>/fat chat [off|quiet|normal|chatty]: sets how much Fat Albert talks, or says what it's set to.</summary>
	private static void SetChatLevel(string word)
	{
		if (word == null || !Enum.TryParse(word, true, out Personality.Chattiness level) || !Enum.IsDefined(typeof(Personality.Chattiness), level))
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality is {Settings.Chattiness}. Change it with /fat chat off, quiet, normal or chatty.");
			return;
		}
		Chatter.SetLevel(level);
		MyAPIGateway.Utilities.ShowMessage(ChatSender, $"Chat personality: {level}. {Chatter.LevelHint(level)}");
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
			Personality.Say("menu_opened");
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(ChatSender, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] {ex}");
		}
	}
}
