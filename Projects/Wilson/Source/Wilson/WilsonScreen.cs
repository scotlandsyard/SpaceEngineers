using System;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Wilson;

/// <summary>
/// Wilson's window: who's loaded and how much each character talks, what Wilson decided about the latest events, his
/// own setting, a button to try an exchange, and a Help page. Refreshes once a second.
/// </summary>
public class WilsonScreen : MyGuiScreenBase
{
	private const int RefreshFrames = 60;

	private const float Left = -0.37f;

	private const float SettingY = -0.33f;

	private const float CastTop = -0.27f;

	private const float SummaryY = -0.02f;

	private const float LogTop = 0.02f;

	private const float StatusY = 0.33f;

	private const float ButtonsY = 0.39f;

	private const float Width = 0.74f;

	private static bool s_help;

	private MyGuiControlTable _cast;

	private MyGuiControlTable _log;

	private MyGuiControlLabel _summary;

	private MyGuiControlLabel _status;

	private int _frames;

	private bool _recreatePending;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color GoodColor => new Color(140, 230, 140);

	private static Color BadColor => new Color(255, 120, 110);

	public WilsonScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.8f, 0.9f))
	{
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		CloseButtonEnabled = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "WilsonScreen";
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		PluginSwitcher.AddSwitcher(this, AddCaption("Wilson - over the fence"));
		_cast = null;
		_log = null;
		_summary = null;
		if (s_help)
		{
			MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, SettingY - 0.02f), new Vector2(Width, StatusY - 0.03f - (SettingY - 0.02f)), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
			{
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
			};
			help.AppendText(HelpText);
			Controls.Add(help);
		}
		else
		{
			Controls.Add(new MyGuiControlLabel(new Vector2(Left, SettingY), null, Settings.Hint(Settings.Level), MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER)
			{
				IsAutoScaleEnabled = true,
				IsAutoEllipsisEnabled = true,
				Size = new Vector2(Width, 0.04f)
			});
			_cast = AddTable(CastTop, 5, new[] { "Character", "Plugin", "Chat", "In exchanges" }, new[] { 0.22f, 0.32f, 0.2f, 0.26f });
			_summary = new MyGuiControlLabel(new Vector2(Left, SummaryY), null, "", MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
			Controls.Add(_summary);
			_log = AddTable(LogTop, 7, new[] { "What Wilson decided (newest first)" }, new[] { 1f });
		}

		_status = new MyGuiControlLabel(new Vector2(Left, StatusY), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.24f, "Try an exchange", () => SetStatus(Director.PlayAny(), Director.Playing ? GoodColor : BadColor))
			.SetToolTip("Plays a random exchange now, between characters that are loaded with their chat on, with made-up names and numbers. Ignores the timers. Same as /wilson try.");
		AddButton(0f, "Wilson: " + Settings.Level, () =>
		{
			Level level = (Level)(((int)Settings.Level + 1) % Enum.GetValues(typeof(Level)).Length);
			WilsonSession.SetLevel(level);
			SetStatus($"{level}. {Settings.Hint(level)}", GoodColor);
			_recreatePending = true;
		}).SetToolTip("How much Wilson does: Off, Quiet, Normal or Chatty. Click to step through them, or /wilson quiet in chat. Off: he doesn't direct at all and each plugin talks on its own.");
		AddButton(0.24f, s_help ? "Back" : "Help", () =>
		{
			s_help = !s_help;
			_recreatePending = true;
		});
		Refresh();
	}

	private MyGuiControlTable AddTable(float top, int rows, string[] names, float[] widths)
	{
		MyGuiControlTable table = new MyGuiControlTable
		{
			Position = new Vector2(0f, top),
			Size = new Vector2(Width, 0.2f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = names.Length,
			VisibleRowsCount = rows
		};
		table.SetCustomColumnWidths(widths);
		for (int i = 0; i < names.Length; i++)
		{
			table.SetColumnName(i, new StringBuilder(names[i]));
		}
		Controls.Add(table);
		return table;
	}

	private MyGuiControlButton AddButton(float x, string text, Action onClick)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, ButtonsY), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) =>
		{
			try
			{
				onClick();
			}
			catch (Exception ex)
			{
				SetStatus("Something went wrong: " + ex.Message, BadColor);
				MyLog.Default.WriteLineAndConsole($"[Wilson] Button '{text}': {ex}");
			}
		});
		Controls.Add(button);
		return button;
	}

	private void SetStatus(string text, Color color)
	{
		if (_status != null)
		{
			_status.Text = text;
			_status.ColorMask = color.ToVector4();
		}
	}

	public override bool Update(bool hasFocus)
	{
		bool result = base.Update(hasFocus);
		try
		{
			if (_recreatePending)
			{
				// Rebuilt here rather than inside a button's event, which is still going through the controls.
				_recreatePending = false;
				RecreateControls(constructor: false);
			}
			else if (++_frames >= RefreshFrames)
			{
				Refresh();
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Screen update: {ex}");
		}
		return result;
	}

	private void Refresh()
	{
		_frames = 0;
		if (_cast != null)
		{
			float scroll = _cast.ScrollBar?.Value ?? 0f;
			_cast.Clear();
			foreach (Cast.Member member in Cast.Others)
			{
				Director.Character c = Director.Find(member.ChatName);
				string chat = c == null ? "not loaded" : c.Level.ToString();
				string inExchanges = !Director.Directing ? "Wilson is Off" : c == null ? "no (not loaded)" : c.Level >= Level.Normal ? "yes" : c.Level == Level.Off ? "no (chat off)" : "no (Quiet)";
				AddRow(_cast, c == null || c.Level < Level.Normal ? MutedColor : (Color?)null, member.ChatName, member.PluginName, chat, inExchanges);
			}
			if (_cast.ScrollBar != null)
			{
				_cast.ScrollBar.Value = scroll;
			}
		}
		if (_summary != null)
		{
			_summary.Text = Director.Directing ? Director.ExchangeSummary() + " Plugins loaded before Wilson's update show up once they say something." : "Wilson is Off, so each plugin talks on its own.";
		}
		if (_log != null)
		{
			_log.Clear();
			foreach (string line in Director.Log)
			{
				AddRow(_log, null, line);
			}
			if (Director.Log.Count == 0)
			{
				AddRow(_log, MutedColor, "Nothing yet. The plugins' events show up here as they happen.");
			}
		}
	}

	private static void AddRow(MyGuiControlTable table, Color? color, params string[] texts)
	{
		MyGuiControlTable.Row row = new MyGuiControlTable.Row();
		foreach (string text in texts)
		{
			row.AddCell(new MyGuiControlTable.Cell(text, null, text.Length > 30 ? text : null, color)
			{
				IsAutoScaleEnabled = true
			});
		}
		table.Add(row);
	}

	private const string HelpText =
		"Wilson is the neighbour over the fence. On his own he just says hello now and then; his real job is keeping the rest of the family in order. With any of BaR Maid, Fat Albert, Sacrificial Stockpile Manager, Script to Plugin or OreScout loaded, their chat lines go through him: when something happens, the plugin tells Wilson, and he decides who speaks. Usually that's the plugin itself, with one of its own lines. Every so often he turns the moment into a short exchange between several characters instead, one line every few seconds. Only you see any of it; nothing goes to the server or other players.\n\n" +
		"HOUSE RULES\n" +
		"One voice at a time: while an exchange plays, other events pass. After anyone speaks, everyone waits 8 seconds. A character isn't asked about the same event again for 3 minutes, and each one keeps its own pace (15 seconds between its lines on Chatty, 45 on Normal, 2 minutes on Quiet). The same exchange trigger doesn't come round again for 10 to 30 minutes depending on Wilson's setting, and there's a pause between any two exchanges.\n\n" +
		"WHO TAKES PART\n" +
		"Wilson respects each plugin's own chat setting. A plugin set to Off sends him nothing and is never in an exchange. A plugin on Quiet only reports its important moments, says them in its own words, and isn't pulled into exchanges. An exchange only plays when every character in it is loaded with chat on Normal or Chatty. The table on the main page shows who's here; a plugin loaded from an older build only shows up once it has said something.\n\n" +
		"WORLD LOAD\n" +
		"About ten seconds in, the plugins would each say hello. With Wilson loaded they greet together instead: one greeting exchange with whichever of them are here. If nobody else is, Wilson says hello himself.\n\n" +
		"WILSON'S SETTING\n" +
		"Off: Wilson stays out of it completely, and each plugin talks on its own as if he weren't loaded.\n" +
		"Quiet: one voice at a time and the greeting, and only the odd exchange.\n" +
		"Normal: an exchange every so often; sometimes he breaks up an argument, and rarely, after a long quiet spell, he shares a proverb.\n" +
		"Chatty: exchanges often, arguments broken up more often, and proverbs after shorter quiet spells.\n\n" +
		"CHAT COMMANDS\n" +
		"/wilson opens this window. /wilson off, /wilson quiet, /wilson normal or /wilson chatty sets his setting. /wilson try plays a random exchange right now between the characters that are loaded, with made-up names and numbers, ignoring the timers (the Try an exchange button does the same).\n\n" +
		"WINDOW\n" +
		"The table lists each character, its plugin, its chat setting, and whether it can be in exchanges. Underneath is what Wilson decided about the latest events: who spoke, which exchange played, and why something passed. The dropdown at the top left switches to another of the family's windows; /tim reopens the one used last.\n\n" +
		"SETTINGS FILE\n" +
		"Wilson's setting is kept in Wilson_Settings.txt in the game's local storage for plugins.";
}
