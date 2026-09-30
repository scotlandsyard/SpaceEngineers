using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Input;
using VRage.Input.Keyboard;
using VRage.Plugins;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// Some keyboards and keyboard/mouse software make Windows report key code 0 (or 255) as held down all the time.
/// Neither is a real key, but the game counts it as "a key is held", and the code editor's text box then claims
/// every click, so the editor's buttons (OK, Check Code, Browse Scripts, X) never respond. That happens in a
/// programmable block's own editor too. This clears those two bits in the raw keyboard buffer each frame, after
/// the game has read the keyboard and before any screen handles input. The game's own SetKey can't do it: it
/// ignores key 0.
/// </summary>
internal static class PhantomKeys
{
	private static FieldInfo s_localizedStateField;

	private static FieldInfo s_actualStateField;

	private static FieldInfo s_bufferField;

	private static bool s_failed;

	private static bool s_reportedRunning;

	private static bool s_reportedCleared;

	/// <summary>Adds the plugin to the game's list of plugins that get HandleInput, which Pulsar doesn't do itself.</summary>
	public static void Register(IHandleInputPlugin plugin)
	{
		try
		{
			if (HandleInputPlugins() is List<IHandleInputPlugin> list)
			{
				if (!list.Contains(plugin))
				{
					list.Add(plugin);
				}
				MyLog.Default.WriteLineAndConsole("[ScriptToPlugin] Registered for input handling.");
			}
			else
			{
				MyLog.Default.WriteLineAndConsole("[ScriptToPlugin] Could not register for input handling: the game's plugin list wasn't found.");
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not register for input handling: {ex}");
		}
	}

	public static void Unregister(IHandleInputPlugin plugin)
	{
		try
		{
			(HandleInputPlugins() as List<IHandleInputPlugin>)?.Remove(plugin);
		}
		catch
		{
		}
	}

	private static object HandleInputPlugins()
	{
		return typeof(MyPlugins).GetField("m_handleInputPlugins", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
	}

	/// <summary>Called every frame between the keyboard being read and the screens handling input.</summary>
	public static unsafe void Clear()
	{
		if (s_failed || MyInput.Static == null)
		{
			return;
		}
		try
		{
			if (s_bufferField == null && !FindFields())
			{
				return;
			}
			if (!s_reportedRunning)
			{
				s_reportedRunning = true;
				MyLog.Default.WriteLineAndConsole("[ScriptToPlugin] Phantom key filter is running.");
			}
			object localized = s_localizedStateField.GetValue(MyInput.Static);
			if (localized == null)
			{
				return;
			}
			MyKeyboardState state = (MyKeyboardState)s_actualStateField.GetValue(localized);
			MyKeyboardBuffer buffer = (MyKeyboardBuffer)s_bufferField.GetValue(state);
			bool key0 = (buffer.Data[0] & 0x01) != 0;
			bool key255 = (buffer.Data[31] & 0x80) != 0;
			if (!key0 && !key255)
			{
				return;
			}
			buffer.Data[0] &= 0xFE;
			buffer.Data[31] &= 0x7F;
			s_actualStateField.SetValue(localized, MyKeyboardState.FromBuffer(buffer));
			if (!s_reportedCleared)
			{
				s_reportedCleared = true;
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Cleared phantom key(s):{(key0 ? " 0" : "")}{(key255 ? " 255" : "")}. This is logged once.");
			}
		}
		catch (Exception ex)
		{
			s_failed = true;
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Clearing phantom keys failed, stopped: {ex}");
		}
	}

	private static bool FindFields()
	{
		s_localizedStateField = FindField(MyInput.Static.GetType(), "m_keyboardState");
		s_actualStateField = s_localizedStateField == null ? null : FindField(s_localizedStateField.FieldType, "m_actualKeyboardState");
		s_bufferField = FindField(typeof(MyKeyboardState), "m_buffer");
		if (s_actualStateField == null || s_actualStateField.FieldType != typeof(MyKeyboardState) || s_bufferField == null)
		{
			s_failed = true;
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not find the keyboard state (input type {MyInput.Static.GetType().FullName}); phantom keys are left alone.");
			return false;
		}
		return true;
	}

	private static FieldInfo FindField(Type type, string name)
	{
		for (; type != null; type = type.BaseType)
		{
			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (field != null)
			{
				return field;
			}
		}
		return null;
	}
}
