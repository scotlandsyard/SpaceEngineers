using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Input;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ScriptToPlugin;

/// <summary>
/// Opens the game's code editor and Custom Data editor for a script. The menu closes first and comes back when
/// the editor has closed, so the editor sits straight on the game like when a programmable block opens it: with
/// the menu left open underneath, the editor took keyboard input but its buttons ignored the mouse.
/// Each step waits until the screen before it has really closed, checked every frame from Plugin.Update.
/// </summary>
internal static class ScriptEditors
{
	private class Pending
	{
		public Func<bool> Ready;

		public Action Action;

		public int FramesLeft;
	}

	private static readonly List<Pending> s_pending = new List<Pending>();

	private static Color WarningColor => new Color(255, 190, 90);

	private static Color GoodColor => new Color(140, 230, 140);

	/// <summary>Runs the action once ready() is true, or after maxFrames frames whatever happens.</summary>
	public static void When(Func<bool> ready, Action action, int maxFrames = 120)
	{
		s_pending.Add(new Pending { Ready = ready, Action = action, FramesLeft = maxFrames });
	}

	public static void AfterClosed(MyGuiScreenBase screen, Action action)
	{
		When(() => screen.State == MyGuiScreenState.CLOSED, action);
	}

	/// <summary>The editor on screen, watched for keys the game thinks are held down.</summary>
	private static MyGuiScreenBase s_watchedEditor;

	private static int s_heldFrames;

	/// <summary>
	/// The game's code box claims all input while any keyboard key reads as held down, which leaves the editor's
	/// buttons dead. When that lasts a second, say which key it is so it can be found.
	/// </summary>
	private static void WatchHeldKeys()
	{
		// Any code or text editor, including a real programmable block's.
		MyGuiScreenBase focused = MyScreenManager.GetScreenWithFocus();
		if (!(focused is MyGuiScreenEditor) && !(focused is MyGuiScreenTextPanel))
		{
			s_watchedEditor = null;
			return;
		}
		if (focused != s_watchedEditor)
		{
			s_watchedEditor = focused;
			s_heldFrames = 0;
		}
		if (MyInput.Static == null || !MyInput.Static.IsAnyKeyPress())
		{
			s_heldFrames = 0;
			return;
		}
		if (++s_heldFrames != 60)
		{
			return;
		}
		List<MyKeys> keys = new List<MyKeys>();
		MyInput.Static.GetPressedKeys(keys);
		string names = keys.Count == 0 ? "key code 0 or 255 (not a real key; the phantom key filter should be clearing it, see the log)" : string.Join(", ", keys.Select(k => $"{k} ({(int)k})"));
		MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Keys reported as held while the editor is open: {names}");
		MyAPIGateway.Utilities?.ShowMessage(ScriptSession.ChatSender, $"The game reports these keys as held down: {names}. While a key is held, the editor's buttons don't respond.");
	}

	/// <summary>Called every frame, on the game thread.</summary>
	public static void Pump()
	{
		WatchHeldKeys();
		if (s_pending.Count == 0)
		{
			return;
		}
		foreach (Pending pending in s_pending.ToArray())
		{
			bool ready;
			try
			{
				ready = pending.Ready() || --pending.FramesLeft <= 0;
			}
			catch
			{
				ready = true;
			}
			if (!ready)
			{
				continue;
			}
			s_pending.Remove(pending);
			try
			{
				pending.Action();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] {ex}");
			}
		}
	}

	public static void Clear()
	{
		s_pending.Clear();
	}

	private static VirtualProgram Find(string id)
	{
		return ScriptSession.Instance?.FindProgram(id);
	}

	private static void ReopenMenu(string id, string message = null, Color? color = null)
	{
		if (message != null)
		{
			ScriptScreen.QueueMessage(message, color);
		}
		ScriptSession.Instance?.OpenMenu(id, reopened: true);
	}

	/// <summary>The programmable block's code editor, with the same save rules: OK saves, closing with changes asks.</summary>
	public static void EditCode(string id)
	{
		VirtualProgram program = Find(id);
		if (program == null)
		{
			return;
		}
		string original = program.Entry.Code ?? "";
		MyGuiScreenEditor editor = null;
		editor = new MyGuiScreenEditor(original, result =>
		{
			MyGuiScreenGamePlay.ActiveGameplayScreen = MyGuiScreenGamePlay.TmpGameplayScreenHolder;
			MyGuiScreenGamePlay.TmpGameplayScreenHolder = null;
			string code = editor.Description.Text.ToString();
			bool tooLong = editor.TextTooLong();
			AfterClosed(editor, () => FinishEditing(id, original, code, tooLong, result));
		}, () => { });
		MyGuiScreenGamePlay.TmpGameplayScreenHolder = MyGuiScreenGamePlay.ActiveGameplayScreen;
		MyScreenManager.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen = editor);
	}

	private static void FinishEditing(string id, string original, string code, bool tooLong, ResultEnum result)
	{
		if (tooLong)
		{
			ReopenMenu(id, "Not saved: the code is longer than 100,000 characters.", WarningColor);
			return;
		}
		if (result == ResultEnum.OK)
		{
			SaveCode(id, code);
			return;
		}
		if (code == original)
		{
			ReopenMenu(id);
			return;
		}
		MyGuiScreenMessageBox box = MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO, messageCaption: MyTexts.Get(MySpaceTexts.ProgrammableBlock_CodeChanged), messageText: MyTexts.Get(MySpaceTexts.ProgrammableBlock_SaveChanges));
		box.ResultCallback = answer => AfterClosed(box, () =>
		{
			if (answer == MyGuiScreenMessageBox.ResultEnum.YES)
			{
				SaveCode(id, code);
			}
			else
			{
				ReopenMenu(id, "Changes not saved.", null);
			}
		});
		MyGuiSandbox.AddScreen(box);
	}

	private static void SaveCode(string id, string code)
	{
		VirtualProgram program = Find(id);
		if (program == null)
		{
			return;
		}
		program.SetCode(code);
		ReopenMenu(id, program.Entry.Enabled ? $"Saved {program.Entry.Name}. It restarts once it has compiled." : $"Saved {program.Entry.Name}. It's off: switch it on to run it.", GoodColor);
	}

	/// <summary>The terminal's Custom Data window, for the script's own Custom Data.</summary>
	public static void EditCustomData(string id)
	{
		VirtualProgram program = Find(id);
		if (program == null)
		{
			return;
		}
		MyGuiScreenTextPanel panel = null;
		panel = new MyGuiScreenTextPanel(program.Entry.Name, "", MyTexts.GetString(MySpaceTexts.Terminal_CustomData), program.Entry.CustomData ?? "", result =>
		{
			MyGuiScreenGamePlay.ActiveGameplayScreen = MyGuiScreenGamePlay.TmpGameplayScreenHolder;
			MyGuiScreenGamePlay.TmpGameplayScreenHolder = null;
			string text = panel.Description.Text.ToString();
			AfterClosed(panel, () =>
			{
				VirtualProgram current = Find(id);
				if (result == ResultEnum.OK && current != null)
				{
					current.SetCustomData(text);
					ReopenMenu(id, $"Custom Data of {current.Entry.Name} saved.", GoodColor);
				}
				else
				{
					ReopenMenu(id);
				}
			});
		}, null, null, editable: true);
		MyGuiScreenGamePlay.TmpGameplayScreenHolder = MyGuiScreenGamePlay.ActiveGameplayScreen;
		MyScreenManager.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen = panel);
	}
}
