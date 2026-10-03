using System;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game.Components;
using VRage.Utils;

namespace Wilson;

/// <summary>
/// Starts the director, adds the /wilson chat command and the plugin switcher entry. Client only: Wilson only reads
/// the other plugins' messages and shows chat lines on this screen, so it's fine on any server.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class WilsonSession : MySessionComponentBase
{
	private const string ChatCommand = "/wilson";

	private bool _started;

	public override void BeforeStart()
	{
		try
		{
			if (MyAPIGateway.Utilities.IsDedicated)
			{
				return;
			}
			Settings.Load();
			Director.Register();
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			PluginSwitcher.Register(Plugin.Name, OpenMenu);
			_started = true;
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] BeforeStart failed: {ex}");
		}
	}

	protected override void UnloadData()
	{
		if (!_started)
		{
			return;
		}
		_started = false;
		try
		{
			MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
			PluginSwitcher.Unregister();
			Director.Unregister();
			Settings.Save();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Unload: {ex}");
		}
	}

	public override void UpdateAfterSimulation()
	{
		if (!_started || MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			Director.Update();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Update: {ex}");
		}
	}

	/// <summary>/wilson opens the window; /wilson off|quiet|normal|chatty sets the level; /wilson try plays an exchange.</summary>
	private void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		string[] words = (messageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0 || !words[0].Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		sendToOthers = false;
		if (words.Length == 1)
		{
			OpenMenu();
			return;
		}
		if (words[1].Equals("try", StringComparison.OrdinalIgnoreCase))
		{
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, Director.PlayAny());
			return;
		}
		if (Enum.TryParse(words[1], true, out Level level) && Enum.IsDefined(typeof(Level), level))
		{
			SetLevel(level);
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"{level}. {Settings.Hint(level)}");
			return;
		}
		MyAPIGateway.Utilities.ShowMessage(Plugin.Name, $"Wilson is {Settings.Level}. /wilson opens his window; /wilson off, quiet, normal or chatty sets how much he does; /wilson try plays an exchange.");
	}

	internal static void SetLevel(Level level)
	{
		Settings.Level = level;
		Settings.Save();
		Director.ApplyLevel();
	}

	private static void OpenMenu()
	{
		if (MyAPIGateway.Session?.Player == null)
		{
			return;
		}
		try
		{
			MyGuiSandbox.AddScreen(new WilsonScreen());
		}
		catch (Exception ex)
		{
			MyAPIGateway.Utilities.ShowMessage(Plugin.Name, "Could not open the window: " + ex.Message);
			MyLog.Default.WriteLineAndConsole($"[Wilson] {ex}");
		}
	}
}
