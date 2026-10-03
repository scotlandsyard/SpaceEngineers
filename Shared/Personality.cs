using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage.Utils;

namespace TimShared;

/// <summary>
/// Gives a plugin a voice: short in-character lines in chat when something happens. The plugin calls
/// Say("event_key") at interesting moments; this class picks a line from the plugin's Personality.txt (an embedded
/// resource), decides whether to say it now, and shows it in chat under the plugin's name. Chat lines from here are
/// only shown on this client and never sent to the server.
///
/// Our plugins share one message channel, so only one of them talks at a time. When the banter plugin (the
/// "director") is loaded, plugins send it their events instead of talking themselves, and it decides who speaks:
/// it can ask a plugin to say one of its own lines, or write an exchange between several plugins itself.
/// This one file is linked into every plugin's project (see Shared/README.md).
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

	/// <summary>After anyone speaks, nobody else does for this long, so lines never pile up.</summary>
	private static readonly TimeSpan Floor = TimeSpan.FromSeconds(8);

	/// <summary>The same event isn't commented on again for this long.</summary>
	private static readonly TimeSpan EventCooldown = TimeSpan.FromMinutes(3);

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

	private static bool s_registered;

	private static string s_director;

	private static DateTime s_floorFreeAt;

	private static DateTime s_ourNextAt;

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

	/// <summary>Call once the session has started (BeforeStart). characterName is the name shown in chat.</summary>
	public static void Register(string characterName)
	{
		if (s_registered || MyAPIGateway.Utilities == null)
		{
			return;
		}
		try
		{
			s_name = characterName;
			LoadLines();
			s_director = null;
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
	/// often: cooldowns decide whether anything is actually said.
	/// </summary>
	public static void Say(string key, params string[] values)
	{
		if (!s_registered || Level == Chattiness.Off || string.IsNullOrEmpty(key))
		{
			return;
		}
		bool known = s_events.TryGetValue(key, out Event ev);
		if (s_director != null)
		{
			// On Quiet, the director only hears about the events this plugin would speak up for itself.
			if (Level != Chattiness.Quiet || (known && ev.Important))
			{
				Send("event", key, values);
			}
			return;
		}
		if (known)
		{
			TrySpeak(ev, values, force: false);
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
			if (now < ev.NextAllowed || now < s_floorFreeAt || now < s_ourNextAt)
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
		s_ourNextAt = now + Gap();
		s_floorFreeAt = now + Floor;
		MyAPIGateway.Utilities.ShowMessage(s_name, Fill(ev.Lines[index], values));
		Send("spoke");
	}

	/// <summary>The least time between two of this plugin's own lines.</summary>
	private static TimeSpan Gap()
	{
		switch (Level)
		{
		case Chattiness.Chatty:
			return TimeSpan.FromSeconds(15);
		case Chattiness.Normal:
			return TimeSpan.FromSeconds(45);
		default:
			return TimeSpan.FromMinutes(2);
		}
	}

	private static string Fill(string line, string[] values)
	{
		if (values == null)
		{
			return line;
		}
		for (int i = 0; i + 1 < values.Length; i += 2)
		{
			line = line.Replace("{" + values[i] + "}", values[i + 1] ?? "");
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

	/// <summary>
	/// Every message carries: kind, our name, the kind's own arguments (event key and values), then our Level as
	/// text ("Off", "Quiet", "Normal" or "Chatty"), which the director uses to pick who may speak.
	/// </summary>
	private static void Send(string kind, string key = null, string[] values = null)
	{
		MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { kind, s_name, key, values, s_level.ToString() });
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
		switch (kind)
		{
		case "director":
			s_director = name;
			// Tells a director that loaded after us that we're here, and how much we talk.
			Send("here");
			break;
		case "bye":
			if (name.Equals(s_director, StringComparison.OrdinalIgnoreCase))
			{
				s_director = null;
			}
			break;
		case "spoke":
			s_floorFreeAt = DateTime.UtcNow + Floor;
			break;
		case "say":
			// The director picked us to speak: say one of our own lines for that event now.
			if (Level != Chattiness.Off && parts.Length > 2 && parts[2] is string target && target.Equals(s_name, StringComparison.OrdinalIgnoreCase) && parts.Length > 3 && parts[3] is string key && s_events.TryGetValue(key, out Event ev))
			{
				TrySpeak(ev, parts.Length > 4 ? parts[4] as string[] : null, force: true);
			}
			break;
		}
	}
}
