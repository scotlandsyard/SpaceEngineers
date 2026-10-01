using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TimShared;

/// <summary>
/// Lets our plugins find each other: a dropdown in each plugin's window switches to another plugin's window, and
/// the /tim chat command opens the window used last. This one file is linked into every plugin's project (see
/// Shared/README.md), so each plugin compiles its own copy and works on its own; the plugins talk through the
/// game's mod message channel, so none of them needs another one to be loaded.
/// </summary>
internal static class PluginSwitcher
{
	/// <summary>Private message channel for these plugins ("TIM_SWIT").</summary>
	private const long Channel = 0x54494D5F53574954L;

	private const string ChatCommand = "/tim";

	/// <summary>Frames to wait for a window to finish closing before opening the next one anyway.</summary>
	private const int MaxWaitFrames = 120;

	// Every loaded plugin by display name, with the action that opens its window. Ours included.
	private static readonly SortedDictionary<string, Action> s_plugins = new SortedDictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

	private static string s_name;

	private static Action s_open;

	private static string s_lastUsed;

	private static bool s_registered;

	/// <summary>Call once the session has started (BeforeStart). openMenu opens this plugin's window.</summary>
	public static void Register(string displayName, Action openMenu)
	{
		if (s_registered || MyAPIGateway.Utilities == null)
		{
			return;
		}
		try
		{
			s_name = displayName;
			s_open = openMenu;
			s_plugins.Clear();
			s_plugins[displayName] = openMenu;
			s_lastUsed = null;
			MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
			MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
			s_registered = true;
			// Plugins already loaded answer with "here", so everyone ends up with the same list.
			Send("hello");
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[{displayName}] Plugin switcher: {ex.Message}");
		}
	}

	/// <summary>Call when the session unloads (UnloadData).</summary>
	public static void Unregister()
	{
		if (!s_registered)
		{
			return;
		}
		s_registered = false;
		try
		{
			Send("bye");
			MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);
			MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
		}
		catch
		{
		}
		s_plugins.Clear();
		s_lastUsed = null;
	}

	/// <summary>Call when this plugin's window opens, so /tim comes back to it.</summary>
	public static void MenuOpened()
	{
		if (!s_registered || s_lastUsed == s_name)
		{
			return;
		}
		s_lastUsed = s_name;
		Send("used");
	}

	/// <summary>
	/// Call from the window's RecreateControls with the label AddCaption returns. Marks this plugin as the one used
	/// last, and puts the plugin dropdown in the top-left corner of the window, on the caption row. The dropdown
	/// shows this plugin; picking another closes the window and opens that plugin's. Hidden when no other of our
	/// plugins is loaded.
	/// </summary>
	public static void AddSwitcher(MyGuiScreenBase screen, MyGuiControlLabel caption)
	{
		MenuOpened();
		if (!s_registered || s_plugins.Count < 2 || screen?.Size == null || caption == null)
		{
			return;
		}
		Vector2 size = screen.Size.Value;
		float left = -size.X / 2f + 0.03f;
		float captionLeft = caption.Position.X - caption.Size.X / 2f;
		float width = Math.Min(0.22f, captionLeft - 0.015f - left);
		if (width < 0.12f)
		{
			width = 0.12f;
		}
		List<string> names = s_plugins.Keys.ToList();
		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(left, caption.Position.Y), new Vector2(width, 0.04f), null, null, names.Count, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		for (int i = 0; i < names.Count; i++)
		{
			combo.AddItem(i, names[i], i, null, sort: false);
		}
		combo.SelectItemByKey(Math.Max(0, names.FindIndex(n => n.Equals(s_name, StringComparison.OrdinalIgnoreCase))), sendEvent: false);
		combo.SetToolTip("Switch to another of your plugins. /tim in chat opens the one you used last.");
		combo.ItemSelected += () =>
		{
			int index = (int)combo.GetSelectedKey();
			if (index >= 0 && index < names.Count && !names[index].Equals(s_name, StringComparison.OrdinalIgnoreCase))
			{
				SwitchTo(screen, names[index]);
			}
		};
		screen.Controls.Add(combo);
	}

	/// <summary>
	/// Closes the window, then opens the other plugin's once it has really closed: opening a window in the same
	/// click that closes another leaves the new one without proper input focus.
	/// </summary>
	private static void SwitchTo(MyGuiScreenBase screen, string name)
	{
		if (!s_plugins.TryGetValue(name, out Action open))
		{
			return;
		}
		// Closed on the next frame rather than inside the dropdown's own event.
		MyAPIGateway.Utilities.InvokeOnGameThread(() =>
		{
			screen.CloseScreen();
			WaitThenRun(() => screen.State == MyGuiScreenState.CLOSED, open, MaxWaitFrames);
		});
	}

	private static void WaitThenRun(Func<bool> ready, Action action, int framesLeft)
	{
		MyAPIGateway.Utilities.InvokeOnGameThread(() =>
		{
			if (ready() || framesLeft <= 0)
			{
				Run(action);
			}
			else
			{
				WaitThenRun(ready, action, framesLeft - 1);
			}
		});
	}

	private static void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[{s_name}] Plugin switcher: {ex}");
		}
	}

	private static void OnMessageEntered(string messageText, ref bool sendToOthers)
	{
		if (messageText == null || !messageText.Trim().Equals(ChatCommand, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		sendToOthers = false;
		// Every loaded plugin sees the command; only the first one in the shared list acts on it.
		if (!s_plugins.Keys.First().Equals(s_name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string target = s_lastUsed != null && s_plugins.ContainsKey(s_lastUsed) ? s_lastUsed : s_plugins.Keys.First();
		Run(s_plugins[target]);
	}

	/// <summary>Every message carries: kind, our name, our open action, and the plugin used last as far as we know.</summary>
	private static void Send(string kind)
	{
		MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { kind, s_name, s_open, s_lastUsed });
	}

	private static void OnMessage(object message)
	{
		if (!(message is object[] parts) || parts.Length < 2 || !(parts[0] is string kind) || !(parts[1] is string name) || name.Equals(s_name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		Action open = parts.Length > 2 ? parts[2] as Action : null;
		switch (kind)
		{
		case "hello":
			if (open != null)
			{
				s_plugins[name] = open;
			}
			Send("here");
			break;
		case "here":
			if (open != null)
			{
				s_plugins[name] = open;
			}
			if (s_lastUsed == null && parts.Length > 3 && parts[3] is string lastUsed)
			{
				s_lastUsed = lastUsed;
			}
			break;
		case "bye":
			s_plugins.Remove(name);
			if (name.Equals(s_lastUsed, StringComparison.OrdinalIgnoreCase))
			{
				s_lastUsed = null;
			}
			break;
		case "used":
			s_lastUsed = name;
			break;
		}
	}
}
