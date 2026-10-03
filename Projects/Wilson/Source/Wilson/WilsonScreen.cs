using System;
using System.Text;
using Sandbox.Graphics.GUI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Wilson;

/// <summary>
/// Wilson's window: every character with the name it goes by, its plugin and chat setting, a box to rename the
/// one picked, what Wilson decided about the latest events, his own setting, buttons to try an exchange or the roll
/// call, and a Help page. Refreshes once a second.
/// </summary>
public class WilsonScreen : MyGuiScreenBase
{
	private const int RefreshFrames = 60;

	private const float Left = -0.42f;

	private const float Width = 0.84f;

	private const float SettingY = -0.375f;

	private const float CastTop = -0.345f;

	private const float RenameY = -0.02f;

	private const float LevelY = 0.04f;

	private const float NoteY = 0.1f;

	private const float StatusY = 0.345f;

	private const float ButtonsY = 0.41f;

	private enum Page
	{
		Crew,
		Log,
		Help
	}

	private static Page s_page = Page.Crew;

	private static Page s_pageBeforeHelp = Page.Crew;

	/// <summary>The character picked in the Crew table (its own name), kept while the game runs.</summary>
	private static string s_selected = Cast.WilsonName;

	/// <summary>Names for the palette in Personality.cs, shown in the Colour column.</summary>
	private static readonly System.Collections.Generic.Dictionary<string, string> s_colourNames = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["BaR Maid"] = "Hot pink",
		["Fat Albert"] = "Bright orange",
		["Stockpile Manager"] = "Salmon",
		["Script to Plugin"] = "Electric cyan",
		["OreScout"] = "Gold",
		["Wilson"] = "Fence-post tan",
		["Tim"] = "Teal"
	};

	private MyGuiControlTable _cast;

	private MyGuiControlTable _log;

	private MyGuiControlTextbox _nameBox;

	private MyGuiControlCombobox _levelCombo;

	private MyGuiControlLabel _levelNote;

	private MyGuiControlLabel _summary;

	private MyGuiControlLabel _status;

	private int _frames;

	private bool _recreatePending;

	private bool _suppressEvents;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color GoodColor => new Color(140, 230, 140);

	private static Color BadColor => new Color(255, 120, 110);

	public WilsonScreen()
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.95f))
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
		_suppressEvents = true;
		try
		{
			CreateControls();
		}
		finally
		{
			_suppressEvents = false;
		}
		Refresh();
	}

	private void CreateControls()
	{
		MyGuiControlLabel caption = AddCaption("Wilson - over the fence");
		PluginSwitcher.AddSwitcher(this, caption);
		// Wilson's own pace, in the same place and style as every plugin's chat setting.
		Personality.AddChatSetting(this, caption, Settings.ToChattiness(Settings.Level), chattiness =>
		{
			if (_suppressEvents)
			{
				return;
			}
			Level level = (Level)(int)chattiness;
			WilsonSession.SetLevel(level);
			SetStatus($"Wilson: {level}. {Settings.Hint(level)}", GoodColor);
			_recreatePending = true;
		}, "How much Wilson does, for the whole crew. Same as /wilson off, quiet, normal or chatty.\nOff: he doesn't direct at all and each plugin talks on its own. Quiet: a turn every 15 minutes, important moments only. Normal: every 5 minutes. Chatty: every 2 minutes.");
		_cast = null;
		_log = null;
		_summary = null;
		_nameBox = null;
		_levelCombo = null;
		_levelNote = null;
		switch (s_page)
		{
		case Page.Help:
			MyGuiControlMultilineText help = new MyGuiControlMultilineText(new Vector2(0f, SettingY), new Vector2(Width, StatusY - 0.03f - SettingY), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
			{
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
			};
			help.AppendText(HelpText);
			Controls.Add(help);
			break;
		case Page.Log:
			_summary = new MyGuiControlLabel(new Vector2(Left, SettingY), null, "", MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
			Controls.Add(_summary);
			_log = AddTable(CastTop, 15, new[] { "What Wilson decided (newest first)" }, new[] { 1f });
			break;
		default:
			CreateCrewPage();
			break;
		}

		_status = new MyGuiControlLabel(new Vector2(Left, StatusY), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, ButtonsY, "Try an exchange", () => SetStatus(Director.PlayAny(), Director.Playing ? GoodColor : BadColor))
			.SetToolTip("Plays a random exchange now, between characters that are loaded with their chat on, with made-up names and numbers. Ignores the timers. Same as /wilson try.");
		AddButton(-0.105f, ButtonsY, "Roll call", () => SetStatus(Director.RollCallNow(), Director.Playing ? GoodColor : BadColor))
			.SetToolTip("Wilson calls the roll again now, as he does when the world loads. Same as /wilson rollcall.");
		if (s_page == Page.Help)
		{
			AddButton(0.315f, ButtonsY, "Back", () => ShowPage(s_pageBeforeHelp));
		}
		else
		{
			AddButton(0.105f, ButtonsY, s_page == Page.Crew ? "Decision log" : "Crew", () => ShowPage(s_page == Page.Crew ? Page.Log : Page.Crew))
				.SetToolTip(s_page == Page.Crew ? "What Wilson decided about the latest events, and why." : "The characters, their names and chat settings.");
			AddButton(0.315f, ButtonsY, "Help", () => ShowPage(Page.Help));
		}
	}

	/// <summary>Every character with its name, colour and chat setting; the one picked can be renamed and its chat set.</summary>
	private void CreateCrewPage()
	{
		Controls.Add(new MyGuiControlLabel(new Vector2(Left, SettingY), null, "Wilson: " + Settings.Hint(Settings.Level), MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_cast = AddTable(CastTop, 7, new[] { "Character", "Shown as", "Colour", "Plugin", "Chat", "In exchanges" }, new[] { 0.15f, 0.18f, 0.15f, 0.22f, 0.12f, 0.18f });
		_cast.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) =>
		{
			if (_cast.SelectedRow?.UserData is string name && name != s_selected)
			{
				s_selected = name;
				_nameBox?.SetText(new StringBuilder(Director.DisplayFor(name)));
				ShowSelectedLevel();
			}
		};

		Controls.Add(new MyGuiControlLabel(new Vector2(Left, RenameY), null, "Shown as", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_nameBox = new MyGuiControlTextbox(new Vector2(-0.31f, RenameY), Director.DisplayFor(s_selected), Personality.MaxNameLength)
		{
			Size = new Vector2(0.3f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		_nameBox.SetToolTip("The name the character picked in the table shows under in chat. Type one and press Rename.");
		Controls.Add(_nameBox);
		AddButton(0.11f, RenameY, "Rename", () => Rename(_nameBox.Text))
			.SetToolTip("Gives the character picked in the table the name in the box. Each plugin keeps its name in its own settings.");
		AddButton(0.32f, RenameY, "Own name", () => Rename(null))
			.SetToolTip("Gives the character picked in the table its own name back.");

		Controls.Add(new MyGuiControlLabel(new Vector2(Left, LevelY), null, "Chat", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
		_levelCombo = new MyGuiControlCombobox(new Vector2(-0.31f, LevelY), new Vector2(0.3f, 0.04f), null, null, 4, null, false, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		foreach (Level level in Enum.GetValues(typeof(Level)))
		{
			_levelCombo.AddItem((long)level, level.ToString(), (int)level, null, sort: false);
		}
		_levelCombo.SetToolTip("How often the character picked in the table talks: Off, Quiet, Normal or Chatty. The plugin keeps it in its own settings, the same as its own Chat dropdown.");
		_levelCombo.ItemSelected += () =>
		{
			if (!_suppressEvents)
			{
				SetSelectedLevel((Level)_levelCombo.GetSelectedKey());
			}
		};
		Controls.Add(_levelCombo);
		_levelNote = new MyGuiControlLabel(new Vector2(0.01f, LevelY), null, "", MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_levelNote);
		ShowSelectedLevel();

		_summary = new MyGuiControlLabel(new Vector2(Left, NoteY), null, "", MutedColor.ToVector4(), 0.75f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_summary);
	}

	/// <summary>Puts the picked character's chat setting in the dropdown, or explains why it has none here.</summary>
	private void ShowSelectedLevel()
	{
		if (_levelCombo == null)
		{
			return;
		}
		Level? level = null;
		string note;
		if (Cast.IsWilson(s_selected))
		{
			level = Settings.Level;
			note = "Wilson's own pace for the whole crew (also top right).";
		}
		else if (Cast.IsTim(s_selected))
		{
			note = "Tim follows Wilson's setting.";
		}
		else if (Director.Find(s_selected) is Director.Character c)
		{
			level = c.Level;
			note = "Saved by " + (Cast.ByChatName(s_selected)?.PluginName ?? s_selected) + ".";
		}
		else
		{
			note = "Not loaded, so it can't be set from here.";
		}
		bool was = _suppressEvents;
		_suppressEvents = true;
		try
		{
			_levelCombo.Visible = level.HasValue;
			if (level.HasValue)
			{
				_levelCombo.SelectItemByKey((long)level.Value, sendEvent: false);
			}
		}
		finally
		{
			_suppressEvents = was;
		}
		if (_levelNote != null)
		{
			_levelNote.Text = note;
		}
	}

	private void SetSelectedLevel(Level level)
	{
		string result;
		if (Cast.IsWilson(s_selected))
		{
			WilsonSession.SetLevel(level);
			result = $"Wilson: {level}. {Settings.Hint(level)}";
			// The top-right dropdown and the hint show Wilson's setting too.
			_recreatePending = true;
		}
		else
		{
			result = Director.SetLevel(s_selected, level);
		}
		SetStatus(result, result.Contains("isn't loaded") || result.Contains("needs updating") ? BadColor : GoodColor);
		Refresh();
	}

	private void ShowPage(Page page)
	{
		if (page == Page.Help && s_page != Page.Help)
		{
			s_pageBeforeHelp = s_page;
		}
		s_page = page;
		_recreatePending = true;
	}

	private void Rename(string name)
	{
		string result = Director.Rename(s_selected, name);
		SetStatus(result, result.Contains("isn't loaded") ? BadColor : GoodColor);
		_nameBox?.SetText(new StringBuilder(Personality.CleanName(name) ?? s_selected));
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

	private MyGuiControlButton AddButton(float x, float y, string text, Action onClick)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) =>
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
			string home = Director.Directing ? "always" : "Wilson is Off";
			AddCastRow(Cast.WilsonName, true, "Wilson", Settings.Level.ToString(), home);
			AddCastRow(Cast.TimName, true, "Wilson (on /tim)", "as Wilson", home);
			foreach (Cast.Member member in Cast.Others)
			{
				Director.Character c = Director.Find(member.ChatName);
				string chat = c == null ? "not loaded" : c.Level.ToString();
				string inExchanges = !Director.Directing ? "Wilson is Off" : c == null ? "no (not loaded)" : c.Level >= Level.Normal ? "yes" : c.Level == Level.Off ? "no (chat off)" : "no (Quiet)";
				AddCastRow(member.ChatName, c != null && c.Level >= Level.Normal, member.PluginName, chat, inExchanges);
			}
			for (int i = 0; i < _cast.RowsCount; i++)
			{
				if (_cast.GetRow(i).UserData is string name && name == s_selected)
				{
					_cast.SelectedRowIndex = i;
					break;
				}
			}
			if (_cast.ScrollBar != null)
			{
				_cast.ScrollBar.Value = scroll;
			}
			// The level can change from the plugin's own Chat dropdown too.
			if (_levelCombo != null && !_levelCombo.IsOpen)
			{
				ShowSelectedLevel();
			}
		}
		if (_summary != null)
		{
			_summary.Text = Director.Directing ? Director.ExchangeSummary() + " A plugin from an older build shows up once it says something." : "Wilson is Off, so each plugin talks on its own.";
		}
		if (_log != null)
		{
			_log.Clear();
			foreach (string line in Director.Log)
			{
				AddRow(_log, null, null, line);
			}
			if (Director.Log.Count == 0)
			{
				AddRow(_log, null, MutedColor, "Nothing yet. The plugins' events show up here as they happen.");
			}
		}
	}

	/// <summary>A Crew row: name and colour columns in the character's colour, the rest muted unless it's active.</summary>
	private void AddCastRow(string name, bool active, string plugin, string chat, string inExchanges)
	{
		Color own = Personality.ColorFor(name);
		Color? rest = active ? (Color?)null : MutedColor;
		bool loaded = Cast.IsHome(name) || Director.Find(name) != null;
		MyGuiControlTable.Row row = new MyGuiControlTable.Row(name);
		row.AddCell(new MyGuiControlTable.Cell(name, null, null, loaded ? own : MutedColor) { IsAutoScaleEnabled = true });
		row.AddCell(new MyGuiControlTable.Cell(loaded ? Director.DisplayFor(name) : "", null, null, own) { IsAutoScaleEnabled = true });
		s_colourNames.TryGetValue(name, out string colour);
		row.AddCell(new MyGuiControlTable.Cell(colour ?? "", null, null, own) { IsAutoScaleEnabled = true });
		row.AddCell(new MyGuiControlTable.Cell(plugin, null, plugin.Length > 20 ? plugin : null, rest) { IsAutoScaleEnabled = true });
		row.AddCell(new MyGuiControlTable.Cell(chat, null, null, rest) { IsAutoScaleEnabled = true });
		row.AddCell(new MyGuiControlTable.Cell(inExchanges, null, null, rest) { IsAutoScaleEnabled = true });
		_cast.Add(row);
	}

	private static void AddRow(MyGuiControlTable table, string key, Color? color, params string[] texts)
	{
		MyGuiControlTable.Row row = new MyGuiControlTable.Row(key);
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
		"Wilson is the neighbour over the fence. His job is keeping the rest of the family in order: with any of BaR Maid, Fat Albert, Sacrificial Stockpile Manager, Script to Plugin or OreScout loaded, their chat lines go through him. When something happens, the plugin tells Wilson, and he decides who speaks: usually the plugin itself, with one of its own lines, and now and then a short exchange between several characters, one line every few seconds. Every character's name has its own colour in chat. Only you see any of it; nothing goes to the server or other players.\n\n" +
		"ONE TURN PER CYCLE\n" +
		"A turn is one character's line or one short exchange. After a turn, nobody says an ordinary line until the cycle is over: 2 minutes on Chatty, 5 on Normal, 15 on Quiet. Important moments (a starved assembler, a ship too heavy to lift, a crashed script, full containers) can break in, but never within 30 seconds of the last line. A character isn't asked about the same event again for 10 minutes, the same exchange doesn't come round again for 30, and each character also keeps to its own setting's cycle. The plugins keep the same rhythm when Wilson isn't loaded.\n\n" +
		"ROLL CALL\n" +
		"About ten seconds after the world loads, Wilson calls the roll. Each loaded character answers once, saying how to call it up, and Wilson wraps up. That replaces the old greetings: nobody greets on their own any more, with or without Wilson. The Roll call button (or /wilson rollcall) calls it again.\n\n" +
		"TIM\n" +
		"/tim reopens the plugin window you used last. Sometimes Wilson's guest Tim drops by when you use it: always the first time in a session, then now and then (not on Quiet).\n\n" +
		"WHO TAKES PART\n" +
		"Wilson respects each plugin's own chat setting. A plugin set to Off sends him nothing and is never in an exchange. A plugin on Quiet only reports its important moments, says them in its own words, and isn't pulled into exchanges, but it does answer the roll call. An exchange only plays when every character in it is loaded with chat on Normal or Chatty.\n\n" +
		"CREW PAGE: NAMES AND CHAT SETTINGS\n" +
		"The Crew page lists every character with the name it goes by, its colour in chat, its plugin, its chat setting and whether it can be in exchanges. Pick one in the table to change it. Type a name under it and press Rename; Own name puts its own back. The Chat dropdown under it sets how often that character talks, the same as the Chat dropdown at the top right of its own plugin's window. Each plugin keeps its name and chat setting in its own settings, so they stay when Wilson isn't loaded. Tim follows Wilson's setting. Lines that mention another character by name keep the original name.\n\n" +
		"WILSON'S SETTING\n" +
		"The Chat dropdown at the top right of this window sets Wilson's own pace for the whole crew (picking Wilson on the Crew page does the same).\n" +
		"Off: Wilson stays out of it completely, and each plugin talks on its own as if he weren't loaded.\n" +
		"Quiet: a turn every 15 minutes for important moments only, no exchanges, and the roll call.\n" +
		"Normal: a turn every 5 minutes, about a third of them exchanges; sometimes he breaks up an argument, and after a long quiet spell he may share a proverb.\n" +
		"Chatty: a turn every 2 minutes, about half of them exchanges, arguments broken up more often.\n\n" +
		"CHAT COMMANDS\n" +
		"/wilson opens this window. /wilson off, /wilson quiet, /wilson normal or /wilson chatty sets his setting. /wilson try plays a random exchange now, with made-up names and numbers, ignoring the timers. /wilson rollcall calls the roll again.\n\n" +
		"WINDOW\n" +
		"Crew (above) is the main page. Decision log shows what Wilson decided about the latest events: who spoke, which exchange played, and why something passed. Try an exchange and Roll call play one right away. The dropdown at the top left switches to another of the family's windows; the one at the top right is Wilson's setting.\n\n" +
		"SETTINGS FILE\n" +
		"Wilson's setting and the names for Wilson and Tim are kept in Wilson_Settings.txt in the game's local storage for plugins.";
}
