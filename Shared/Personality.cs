using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TimShared;

/// <summary>
/// Gives a plugin a voice: short in-character lines in chat when something happens. The plugin calls
/// Say("event_key") at interesting moments; this class picks a line from the plugin's Personality.txt (an embedded
/// resource), decides whether to say it now, and shows it in chat under the character's name, in the character's
/// own colour. Chat lines from here are only shown on this client and never sent to the server.
///
/// Our plugins share one message channel and keep to one rhythm: after anyone speaks, nobody says an ordinary line
/// again until a cycle has passed (2 to 15 minutes depending on the setting). When the banter plugin (the
/// "director", Wilson) is loaded, plugins send it their events instead of talking themselves, and it decides who
/// speaks: it can ask a plugin to say one of its own lines, or play an exchange between several characters itself.
/// This one file is linked into every plugin's project, Wilson's included (see Shared/README.md).
/// </summary>
internal static class Personality
{
	public enum Chattiness
	{
		Off,
		/// <summary>Only lines marked important ([!key] in Personality.txt).</summary>
		Quiet,
		Normal,
		Chatty
	}

	/// <summary>Private message channel for these plugins ("TIM_PERS").</summary>
	private const long Channel = 0x54494D5F50455253L;

	private const string ResourceName = "Personality.txt";

	/// <summary>Longest display name a player can give a character.</summary>
	public const int MaxNameLength = 24;

	/// <summary>An important line still waits this long after anyone's last line.</summary>
	public static readonly TimeSpan ImportantFloor = TimeSpan.FromSeconds(30);

	/// <summary>The same event isn't commented on again for this long.</summary>
	public static readonly TimeSpan EventCooldown = TimeSpan.FromMinutes(10);

	/// <summary>Answer to Wilson's roll call for a plugin whose Personality.txt has no [rollcall] lines yet.</summary>
	private const string FallbackRollCall = "Here. {command} calls me up.";

	private sealed class Event
	{
		public readonly List<string> Lines = new List<string>();

		public bool Important;

		public int Last = -1;

		public DateTime NextAllowed;
	}

	private static readonly Dictionary<string, Event> s_events = new Dictionary<string, Event>(StringComparer.OrdinalIgnoreCase);

	private static readonly Random s_random = new Random();

	private static string s_name;

	private static string s_displayName;

	private static bool s_registered;

	private static string s_director;

	/// <summary>When anyone of ours last showed a line.</summary>
	private static DateTime s_lastLineAt;

	private static Chattiness s_level = Chattiness.Normal;

	/// <summary>How much this plugin talks. The plugin saves it in its own settings and sets it here on load.</summary>
	public static Chattiness Level
	{
		get => s_level;
		set
		{
			if (s_level == value)
			{
				return;
			}
			s_level = value;
			// The director needs to know, so it never makes a plugin that's Off speak in an exchange.
			if (s_registered && s_director != null)
			{
				Send("here");
			}
		}
	}

	/// <summary>
	/// The name this character's lines show under, chosen by the player; the character's own name (the one passed to
	/// Register) when unset. The plugin saves it in its own settings, sets it here on load, and saves it again on
	/// Changed, which also fires when the player renames the character from Wilson's window. Setting null or blank
	/// goes back to the character's own name.
	/// </summary>
	public static string DisplayName
	{
		get => s_displayName ?? s_name;
		set
		{
			string name = CleanName(value);
			if (name != null && s_name != null && name.Equals(s_name, StringComparison.Ordinal))
			{
				name = null;
			}
			if (name == s_displayName)
			{
				return;
			}
			s_displayName = name;
			if (s_registered && s_director != null)
			{
				Send("here");
			}
			try
			{
				Changed?.Invoke();
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[{s_name}] Personality name change: {ex.Message}");
			}
		}
	}

	/// <summary>Fires when DisplayName changes, from the plugin or from Wilson's window. Save the name here.</summary>
	public static event Action Changed;

	/// <summary>Call once the session has started (BeforeStart). characterName is the character's own name.</summary>
	public static void Register(string characterName)
	{
		if (s_registered || MyAPIGateway.Utilities == null)
		{
			return;
		}
		try
		{
			s_name = characterName;
			if (s_displayName != null && s_displayName.Equals(s_name, StringComparison.Ordinal))
			{
				s_displayName = null;
			}
			LoadLines();
			s_director = null;
			s_lastLineAt = DateTime.MinValue;
			MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
			s_registered = true;
			// A loaded director answers with "director".
			Send("hello");
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[{characterName}] Personality: {ex.Message}");
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
			MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);
		}
		catch
		{
		}
		s_events.Clear();
		s_director = null;
	}

	/// <summary>
	/// Call on the game thread when something worth a comment happens. values are name/value pairs that fill
	/// placeholders in the line: Say("starved", "item", "Steel Plate") turns {item} into Steel Plate. Cheap to call
	/// often: the cycle and cooldowns decide whether anything is actually said.
	/// </summary>
	public static void Say(string key, params string[] values)
	{
		// Greetings are retired: with Wilson the crew answers his roll call, and without him nobody greets.
		if (!s_registered || Level == Chattiness.Off || string.IsNullOrEmpty(key) || key.Equals("greeting", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		bool known = s_events.TryGetValue(key, out Event ev);
		if (s_director != null)
		{
			// On Quiet, the director only hears about the events this plugin would speak up for itself.
			if (Level != Chattiness.Quiet || (known && ev.Important))
			{
				Send("event", key, values, known && ev.Important);
			}
			return;
		}
		if (known)
		{
			TrySpeak(ev, values, force: false);
		}
	}

	/// <summary>
	/// After anyone speaks, an ordinary line waits this long: one turn per cycle, so the family never floods the
	/// chat. Wilson uses the same table for his own setting.
	/// </summary>
	public static TimeSpan Cycle(Chattiness level)
	{
		switch (level)
		{
		case Chattiness.Chatty:
			return TimeSpan.FromMinutes(2);
		case Chattiness.Normal:
			return TimeSpan.FromMinutes(5);
		default:
			return TimeSpan.FromMinutes(15);
		}
	}

	private static void TrySpeak(Event ev, string[] values, bool force)
	{
		if (ev.Lines.Count == 0)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		if (!force)
		{
			if (Level == Chattiness.Quiet && !ev.Important)
			{
				return;
			}
			if (now < ev.NextAllowed || now < s_lastLineAt + (ev.Important ? ImportantFloor : Cycle(Level)))
			{
				return;
			}
		}
		// Never the same line twice in a row.
		int index = s_random.Next(ev.Lines.Count);
		if (index == ev.Last && ev.Lines.Count > 1)
		{
			index = (index + 1 + s_random.Next(ev.Lines.Count - 1)) % ev.Lines.Count;
		}
		ev.Last = index;
		ev.NextAllowed = now + EventCooldown;
		Speak(Fill(ev.Lines[index], values));
	}

	private static void Speak(string text)
	{
		s_lastLineAt = DateTime.UtcNow;
		Show(s_name, DisplayName, text);
		Send("spoke");
	}

	// ---- Names and colours ----

	/// <summary>Each character's name colour in chat, by its own name. None of them is a colour the game uses for players, factions or admins.</summary>
	private static readonly Dictionary<string, Color> s_colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
	{
		["BaR Maid"] = new Color(255, 105, 180),
		["Fat Albert"] = new Color(255, 140, 0),
		["Stockpile Manager"] = new Color(250, 128, 114),
		["Script to Plugin"] = new Color(0, 229, 255),
		["OreScout"] = new Color(255, 215, 0),
		["Wilson"] = new Color(222, 184, 135),
		["Tim"] = new Color(0, 206, 180)
	};

	/// <summary>For a character not in the palette.</summary>
	private static readonly Color s_otherColor = new Color(255, 180, 110);

	/// <summary>A character's name colour in chat, by its own name (not the display name).</summary>
	public static Color ColorFor(string characterName)
	{
		return characterName != null && s_colors.TryGetValue(characterName, out Color color) ? color : s_otherColor;
	}

	/// <summary>
	/// Shows a line in chat on this screen only, under displayName in the character's colour. The HUD chat call adds
	/// the line to the local chat list and sends nothing to the server.
	/// </summary>
	public static void Show(string characterName, string displayName, string text)
	{
		try
		{
			MyHud.Chat.ShowMessage(displayName, text, ColorFor(characterName), Color.White);
		}
		catch
		{
			MyAPIGateway.Utilities.ShowMessage(displayName, text);
		}
	}

	/// <summary>A name a player typed, cleaned up: trimmed, one line, at most MaxNameLength; null when empty.</summary>
	public static string CleanName(string name)
	{
		if (name == null)
		{
			return null;
		}
		name = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (name.Length > MaxNameLength)
		{
			name = name.Substring(0, MaxNameLength).Trim();
		}
		return name.Length == 0 ? null : name;
	}

	public static string Fill(string line, string[] values)
	{
		if (values == null)
		{
			return line;
		}
		for (int i = 0; i + 1 < values.Length; i += 2)
		{
			if (values[i] != null)
			{
				line = line.Replace("{" + values[i] + "}", values[i + 1] ?? "");
			}
		}
		return line;
	}

	/// <summary>
	/// Personality.txt: a [key] line starts an event ([!key] marks it important, said even on Quiet), and each
	/// line after it is one thing the character may say. Blank lines and lines starting with # are skipped, and a
	/// leading "- " is dropped so pasted bullet lists work.
	/// </summary>
	private static void LoadLines()
	{
		s_events.Clear();
		using (Stream stream = typeof(Personality).Assembly.GetManifestResourceStream(ResourceName))
		{
			if (stream == null)
			{
				MyLog.Default.WriteLineAndConsole($"[{s_name}] Personality: no {ResourceName} embedded");
				return;
			}
			using (StreamReader reader = new StreamReader(stream))
			{
				Event current = null;
				string text;
				while ((text = reader.ReadLine()) != null)
				{
					text = text.Trim();
					if (text.Length == 0 || text.StartsWith("#"))
					{
						continue;
					}
					if (text.StartsWith("[") && text.EndsWith("]"))
					{
						string key = text.Substring(1, text.Length - 2).Trim();
						bool important = key.StartsWith("!");
						key = key.TrimStart('!').Trim();
						if (!s_events.TryGetValue(key, out current))
						{
							current = new Event();
							s_events[key] = current;
						}
						current.Important |= important;
						continue;
					}
					if (text.StartsWith("- "))
					{
						text = text.Substring(2).Trim();
					}
					current?.Lines.Add(text);
				}
			}
		}
	}

	// ---- Messages ----

	/// <summary>
	/// Every message carries: kind, our own name, the kind's own arguments (event key and values), our Level as text
	/// ("Off", "Quiet", "Normal" or "Chatty"), our display name, and whether the event is important ("1" or "0").
	/// </summary>
	private static void Send(string kind, string key = null, string[] values = null, bool important = false)
	{
		MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { kind, s_name, key, values, s_level.ToString(), DisplayName, important ? "1" : "0" });
	}

	private static void OnMessage(object message)
	{
		// Messages are delivered straight from the sender's call, so an exception here would land in its code.
		try
		{
			Handle(message);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[{s_name}] Personality message: {ex.Message}");
		}
	}

	private static void Handle(object message)
	{
		if (!(message is object[] parts) || parts.Length < 2 || !(parts[0] is string kind) || !(parts[1] is string name) || name.Equals(s_name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		bool forUs = parts.Length > 2 && parts[2] is string target && target.Equals(s_name, StringComparison.OrdinalIgnoreCase);
		switch (kind)
		{
		case "director":
			s_director = name;
			// Tells a director that loaded after us that we're here, how much we talk, and what we're called.
			Send("here");
			break;
		case "bye":
			if (name.Equals(s_director, StringComparison.OrdinalIgnoreCase))
			{
				s_director = null;
			}
			break;
		case "spoke":
			s_lastLineAt = DateTime.UtcNow;
			break;
		case "say":
			// The director picked us to speak: say one of our own lines for that event now.
			if (forUs && Level != Chattiness.Off && parts.Length > 3 && parts[3] is string key)
			{
				string[] values = parts.Length > 4 ? parts[4] as string[] : null;
				if (s_events.TryGetValue(key, out Event ev) && ev.Lines.Count > 0)
				{
					TrySpeak(ev, values, force: true);
				}
				else if (key.Equals("rollcall", StringComparison.OrdinalIgnoreCase))
				{
					Speak(Fill(FallbackRollCall, values));
				}
			}
			break;
		case "rename":
			// The player renamed us in Wilson's window. The plugin saves it from Changed; "here" confirms it.
			if (forUs && parts.Length > 3)
			{
				DisplayName = parts[3] as string;
			}
			break;
		}
	}
}
