using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Wilson;

/// <summary>
/// The banter director on the plugins' personality channel (see "Banter (the director)" in Shared/README.md). The
/// plugins send Wilson their events instead of talking themselves; for each one he either asks that plugin to say
/// one of its own lines ("say"), or plays a written exchange between several characters from Wilson.txt, one line
/// every few seconds. Everything runs on the game thread: mod messages are delivered there, straight from the
/// sender's call.
/// </summary>
internal static class Director
{
	/// <summary>Private message channel for the plugins' personalities ("TIM_PERS"), the same as Shared/Personality.cs.</summary>
	private const long Channel = 0x54494D5F50455253L;

	/// <summary>After anyone speaks, nobody else does for this long (the same as Shared/Personality.cs).</summary>
	private static readonly TimeSpan Floor = TimeSpan.FromSeconds(8);

	/// <summary>A plugin isn't asked to comment on the same event again for this long (the same as Shared/Personality.cs).</summary>
	private static readonly TimeSpan EventCooldown = TimeSpan.FromMinutes(3);

	/// <summary>Ticks after the first plugin's greeting before the greeting is played, so every plugin's has arrived.</summary>
	private const int GreetingGatherTicks = 120;

	/// <summary>Ticks after the world loads before Wilson greets on his own, if no plugin has (they greet after 600).</summary>
	private const int GreetingLatestTicks = 1500;

	private const int MaxLog = 40;

	/// <summary>A character Wilson has heard from this session.</summary>
	public sealed class Character
	{
		public readonly string Name;

		/// <summary>The plugin's own Personality setting. Plugins built before the level was sent count as Normal.</summary>
		public Level Level = Level.Normal;

		/// <summary>When this character may next be asked to say one of its own lines.</summary>
		public DateTime NextAt;

		public Character(string name)
		{
			Name = name;
		}
	}

	private static readonly Dictionary<string, Character> s_roster = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);

	/// <summary>"Chat name|event" to when that character may comment on that event again.</summary>
	private static readonly Dictionary<string, DateTime> s_eventNext = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Exchange trigger to when an exchange may play for it again.</summary>
	private static readonly Dictionary<string, DateTime> s_triggerNext = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Exchange trigger to the exchange played last for it, so the next one is a different one.</summary>
	private static readonly Dictionary<string, Exchange> s_lastPlayed = new Dictionary<string, Exchange>(StringComparer.OrdinalIgnoreCase);

	private static readonly List<string> s_log = new List<string>();

	private static readonly Random s_random = new Random();

	private static bool s_registered;

	private static bool s_directing;

	private static DateTime s_floorFreeAt;

	private static DateTime s_nextExchangeAt;

	private static DateTime s_nextIdleAt;

	private static int s_tick;

	private static int s_firstGreetingTick;

	private static bool s_greetingDone;

	// The exchange being played: its lines (filled in), the next one to show, and when.
	private static List<ExchangeLine> s_playing;

	private static int s_playIndex;

	private static DateTime s_nextLineAt;

	private static bool s_squabbleChecked;

	/// <summary>Wilson is directing: the plugins send him their events.</summary>
	public static bool Directing => s_directing;

	/// <summary>An exchange is playing right now.</summary>
	public static bool Playing => s_playing != null;

	/// <summary>Newest first: what Wilson decided about each event, for the window.</summary>
	public static IReadOnlyList<string> Log => s_log;

	/// <summary>Call once the session has started (BeforeStart), after Settings.Load.</summary>
	public static void Register()
	{
		if (s_registered)
		{
			return;
		}
		Lines.Load();
		s_roster.Clear();
		s_eventNext.Clear();
		s_triggerNext.Clear();
		s_lastPlayed.Clear();
		s_log.Clear();
		s_tick = 0;
		s_firstGreetingTick = 0;
		s_greetingDone = false;
		s_playing = null;
		s_floorFreeAt = DateTime.MinValue;
		s_nextExchangeAt = DateTime.MinValue;
		ScheduleIdle();
		MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
		s_registered = true;
		ApplyLevel();
	}

	/// <summary>Call when the session unloads (UnloadData).</summary>
	public static void Unregister()
	{
		if (!s_registered)
		{
			return;
		}
		if (s_directing)
		{
			// The plugins go back to talking themselves.
			Send("bye");
			s_directing = false;
		}
		s_registered = false;
		try
		{
			MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);
		}
		catch
		{
		}
		s_playing = null;
		s_roster.Clear();
	}

	/// <summary>Starts or stops directing to match Settings.Level. Call after changing it.</summary>
	public static void ApplyLevel()
	{
		if (!s_registered)
		{
			return;
		}
		bool direct = Settings.Level != Level.Off;
		if (direct == s_directing)
		{
			ScheduleIdle();
			return;
		}
		s_directing = direct;
		if (direct)
		{
			AddLog("Wilson is directing: the plugins send him their events.");
			// Loaded plugins answer with "here", so Wilson knows who's around and how much each one talks.
			Send("director");
		}
		else
		{
			s_playing = null;
			AddLog("Wilson is Off: each plugin talks on its own.");
			Send("bye");
		}
		ScheduleIdle();
	}

	/// <summary>Every character but Wilson that he has heard from this session, by chat name.</summary>
	public static Character Find(string chatName)
	{
		return chatName != null && s_roster.TryGetValue(chatName, out Character c) ? c : null;
	}

	/// <summary>Call every tick from the session component (game thread).</summary>
	public static void Update()
	{
		if (!s_registered)
		{
			return;
		}
		s_tick++;
		if (!s_directing)
		{
			// Turned on later in the session: no "world loaded" greeting then.
			s_greetingDone |= s_tick >= GreetingLatestTicks;
			return;
		}
		DateTime now = DateTime.UtcNow;
		if (s_playing != null)
		{
			if (now >= s_nextLineAt)
			{
				PlayNextLine(now);
			}
			return;
		}
		if (!s_greetingDone)
		{
			if ((s_firstGreetingTick > 0 && s_tick >= s_firstGreetingTick + GreetingGatherTicks) || s_tick >= GreetingLatestTicks)
			{
				Greet();
			}
			return;
		}
		if (now >= s_nextIdleAt && now >= s_floorFreeAt)
		{
			// Rarely: half the time the quiet spell just goes on.
			if (Settings.Level >= Level.Normal && s_random.NextDouble() < 0.5)
			{
				string line = Lines.Own("idle");
				if (line != null)
				{
					AddLog("A long quiet spell: Wilson shares a proverb.");
					ShowLine(Cast.WilsonName, line, now);
				}
			}
			ScheduleIdle();
		}
	}

	// ---- Messages ----

	private static void Send(string kind, string target = null, string key = null, string[] values = null)
	{
		MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { kind, Cast.WilsonName, target, key, values });
	}

	private static void OnMessage(object message)
	{
		// Delivered straight from the sender's call: an exception here would land in that plugin's code.
		try
		{
			Handle(message);
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Wilson] Message: {ex}");
		}
	}

	private static void Handle(object message)
	{
		if (!(message is object[] parts) || parts.Length < 2 || !(parts[0] is string kind) || !(parts[1] is string name) || Cast.IsWilson(name))
		{
			return;
		}
		switch (kind)
		{
		case "hello":
			Seen(name, parts);
			if (s_directing)
			{
				Send("director");
			}
			break;
		case "here":
			Seen(name, parts);
			break;
		case "event":
			Character c = Seen(name, parts);
			if (s_directing && parts.Length > 2 && parts[2] is string key && key.Length > 0)
			{
				OnEvent(c, key, parts.Length > 3 ? parts[3] as string[] : null);
			}
			break;
		case "spoke":
			Seen(name, parts);
			Spoke(DateTime.UtcNow);
			break;
		case "director":
			AddLog($"Another director ({name}) announced itself; Wilson carries on.");
			break;
		}
	}

	/// <summary>Adds the sender to the roster and notes its Personality setting, if the message carries it.</summary>
	private static Character Seen(string name, object[] parts)
	{
		if (!s_roster.TryGetValue(name, out Character c))
		{
			c = new Character(name);
			s_roster[name] = c;
			AddLog($"{name} is here.");
		}
		if (parts.Length > 4 && parts[4] is string text && Enum.TryParse(text, true, out Level level) && Enum.IsDefined(typeof(Level), level) && level != c.Level)
		{
			c.Level = level;
			AddLog($"{name}'s chat is {level}.");
		}
		return c;
	}

	private static void Spoke(DateTime now)
	{
		s_floorFreeAt = now + Floor;
		ScheduleIdle();
	}

	// ---- Events ----

	private static void OnEvent(Character c, string key, string[] values)
	{
		if (c.Level == Level.Off)
		{
			return;
		}
		if (key.Equals("greeting", StringComparison.OrdinalIgnoreCase))
		{
			// Gathered: one greeting for everyone plays once they've all had their say.
			if (!s_greetingDone && s_firstGreetingTick == 0)
			{
				s_firstGreetingTick = s_tick;
			}
			return;
		}
		Cast.Member member = Cast.ByChatName(c.Name);
		string trigger = member != null ? member.Id + "." + key : null;
		DateTime now = DateTime.UtcNow;
		if (s_playing != null)
		{
			AddLog($"{Label(c, key)}: an exchange is playing, so it passes.");
			return;
		}
		if (now < s_floorFreeAt)
		{
			AddLog($"{Label(c, key)}: someone just spoke, so it passes.");
			return;
		}
		string eventKey = c.Name + "|" + key;
		if (trigger != null && TryExchange(trigger, values, c, now))
		{
			s_eventNext[eventKey] = now + EventCooldown;
			return;
		}
		if (now < c.NextAt || (s_eventNext.TryGetValue(eventKey, out DateTime next) && now < next))
		{
			AddLog($"{Label(c, key)}: said too recently, so it passes.");
			return;
		}
		c.NextAt = now + Gap(c.Level);
		s_eventNext[eventKey] = now + EventCooldown;
		AddLog($"{Label(c, key)}: {c.Name} says its own line.");
		Send("say", c.Name, key, values);
	}

	private static bool TryExchange(string trigger, string[] values, Character c, DateTime now)
	{
		if (c.Level < Level.Normal)
		{
			return false;
		}
		List<Exchange> all = Lines.ExchangesFor(trigger);
		if (all.Count == 0)
		{
			return false;
		}
		if (now < s_nextExchangeAt || (s_triggerNext.TryGetValue(trigger, out DateTime next) && now < next))
		{
			return false;
		}
		List<Exchange> playable = all.Where(e => e.Lines.All(l => CanSpeak(l.Speaker) && !Lines.HasPlaceholder(Lines.Fill(l.Text, values)))).ToList();
		if (playable.Count == 0)
		{
			AddLog($"{trigger}: no exchange has all its speakers here with chat on.");
			return false;
		}
		if (s_random.NextDouble() >= ExchangeChance())
		{
			return false;
		}
		s_triggerNext[trigger] = now + TriggerCooldown();
		AddLog($"{trigger}: Wilson stages an exchange.");
		Play(Pick(trigger, playable), values, now);
		return true;
	}

	private static Exchange Pick(string trigger, List<Exchange> choices)
	{
		s_lastPlayed.TryGetValue(trigger, out Exchange last);
		List<Exchange> fresh = choices.Count > 1 ? choices.Where(e => e != last).ToList() : choices;
		Exchange picked = fresh[s_random.Next(fresh.Count)];
		s_lastPlayed[trigger] = picked;
		return picked;
	}

	/// <summary>Wilson always can; another character only when its plugin is loaded with chat on Normal or Chatty.</summary>
	private static bool CanSpeak(string chatName)
	{
		if (Cast.IsWilson(chatName))
		{
			return true;
		}
		return Cast.ByChatName(chatName) != null && s_roster.TryGetValue(chatName, out Character c) && c.Level >= Level.Normal;
	}

	// ---- Greeting ----

	/// <summary>
	/// When the world loads: with other characters here, one any.greeting exchange with only the lines of those
	/// that are loaded; alone, one of Wilson's own greetings.
	/// </summary>
	private static void Greet()
	{
		s_greetingDone = true;
		DateTime now = DateTime.UtcNow;
		List<ExchangeLine> best = null;
		int bestScore = 0;
		int ties = 0;
		foreach (Exchange exchange in Lines.ExchangesFor("any.greeting"))
		{
			// Keep the loaded speakers' lines, but never one character twice in a row (they'd be answering someone absent).
			List<ExchangeLine> kept = new List<ExchangeLine>();
			foreach (ExchangeLine line in exchange.Lines)
			{
				if (CanSpeak(line.Speaker) && (kept.Count == 0 || !kept[kept.Count - 1].Speaker.Equals(line.Speaker, StringComparison.OrdinalIgnoreCase)))
				{
					kept.Add(line);
				}
			}
			int others = kept.Count(l => !Cast.IsWilson(l.Speaker));
			if (others == 0)
			{
				continue;
			}
			// Whole exchanges first, then the ones with the most of the cast in them.
			int score = (exchange.Lines.All(l => CanSpeak(l.Speaker)) ? 100 : 0) + others;
			if (score > bestScore)
			{
				best = kept;
				bestScore = score;
				ties = 1;
			}
			else if (score == bestScore && s_random.Next(++ties) == 0)
			{
				best = kept;
			}
		}
		if (best != null)
		{
			AddLog("World loaded: Wilson stages a greeting.");
			Play(best, null, now);
			return;
		}
		string own = Lines.Own("greeting");
		if (own != null)
		{
			AddLog("World loaded: nobody else to greet with, so Wilson says hello himself.");
			ShowLine(Cast.WilsonName, own, now);
		}
	}

	// ---- Playing an exchange ----

	private static void Play(Exchange exchange, string[] values, DateTime now)
	{
		Play(exchange.Lines, values, now);
	}

	private static void Play(List<ExchangeLine> lines, string[] values, DateTime now)
	{
		s_playing = lines.Select(l => new ExchangeLine(l.Speaker, Lines.Fill(l.Text, values))).ToList();
		s_playIndex = 0;
		s_squabbleChecked = false;
		s_nextLineAt = now;
		PlayNextLine(now);
	}

	private static void PlayNextLine(DateTime now)
	{
		if (s_playIndex >= s_playing.Count && !s_squabbleChecked)
		{
			s_squabbleChecked = true;
			if (EndsInArgument(s_playing) && s_random.NextDouble() < SquabbleChance())
			{
				string line = Lines.Own("squabble");
				if (line != null)
				{
					AddLog("That ended in an argument: Wilson steps in.");
					s_playing.Add(new ExchangeLine(Cast.WilsonName, line));
				}
			}
		}
		if (s_playIndex >= s_playing.Count)
		{
			Finish(now);
			return;
		}
		ExchangeLine next = s_playing[s_playIndex++];
		ShowLine(next.Speaker, next.Text, now);
		s_nextLineAt = now + LineDelay(next.Text);
	}

	/// <summary>Someone answered back and Wilson didn't get the last word: A, B, then A again.</summary>
	private static bool EndsInArgument(List<ExchangeLine> lines)
	{
		if (lines.Count < 3)
		{
			return false;
		}
		string last = lines[lines.Count - 1].Speaker;
		return !Cast.IsWilson(last) && lines.Take(lines.Count - 1).Any(l => l.Speaker.Equals(last, StringComparison.OrdinalIgnoreCase));
	}

	private static void Finish(DateTime now)
	{
		foreach (string speaker in s_playing.Select(l => l.Speaker).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (s_roster.TryGetValue(speaker, out Character c))
			{
				c.NextAt = now + Gap(c.Level);
			}
		}
		s_playing = null;
		s_nextExchangeAt = now + ExchangeGap();
		Spoke(now);
	}

	private static void ShowLine(string speaker, string text, DateTime now)
	{
		MyAPIGateway.Utilities.ShowMessage(speaker, text);
		Spoke(now);
		Send("spoke");
	}

	/// <summary>Time to read a line before the next one: longer lines get longer.</summary>
	private static TimeSpan LineDelay(string text)
	{
		double seconds = 2.5 + 0.04 * (text?.Length ?? 0);
		return TimeSpan.FromSeconds(Math.Max(3.0, Math.Min(7.0, seconds)));
	}

	// ---- Trying it out ----

	/// <summary>Values for the placeholders when an exchange is played from the window.</summary>
	private static readonly string[] SampleValues =
	{
		"item", "Steel Plate",
		"count", "500",
		"ship", "Big Bertha",
		"mass", "1,250 t",
		"grid", "Home Base",
		"script", "Airlock Control",
		"ore", "Platinum",
		"distance", "2.4 km"
	};

	/// <summary>Plays a random exchange now whose speakers are all here, ignoring the timers. Returns what happened.</summary>
	public static string PlayAny()
	{
		if (!s_directing)
		{
			return "Wilson is Off. Pick another setting first.";
		}
		if (s_playing != null)
		{
			return "An exchange is already playing.";
		}
		List<Exchange> playable = Lines.AllExchanges().Where(e => e.Lines.All(l => CanSpeak(l.Speaker))).ToList();
		if (playable.Count == 0)
		{
			return "No exchange has all its speakers loaded with chat on. Load more of the family.";
		}
		Exchange exchange = Pick("(tried from the window)", playable);
		AddLog($"Tried from the window: an exchange for {exchange.Trigger}.");
		Play(exchange, SampleValues, DateTime.UtcNow);
		return $"Playing an exchange for {exchange.Trigger} in chat (with made-up names and numbers).";
	}

	/// <summary>How many exchanges could play with the characters loaded now, and how many there are.</summary>
	public static string ExchangeSummary()
	{
		int total = Lines.ExchangeCount;
		int playable = Lines.AllExchanges().Count(e => e.Lines.All(l => CanSpeak(l.Speaker)));
		return $"{playable} of {total} exchanges have all their speakers here.";
	}

	// ---- Timing by level ----

	/// <summary>The least time between two of a character's own lines (the same as Shared/Personality.cs).</summary>
	private static TimeSpan Gap(Level level)
	{
		switch (level)
		{
		case Level.Chatty:
			return TimeSpan.FromSeconds(15);
		case Level.Normal:
			return TimeSpan.FromSeconds(45);
		default:
			return TimeSpan.FromMinutes(2);
		}
	}

	/// <summary>Chance that an event with a playable exchange gets the exchange instead of the plugin's own line.</summary>
	private static double ExchangeChance()
	{
		switch (Settings.Level)
		{
		case Level.Chatty:
			return 0.6;
		case Level.Normal:
			return 0.35;
		default:
			return 0.15;
		}
	}

	/// <summary>The least time between the end of one exchange and the start of the next.</summary>
	private static TimeSpan ExchangeGap()
	{
		switch (Settings.Level)
		{
		case Level.Chatty:
			return TimeSpan.FromSeconds(90);
		case Level.Normal:
			return TimeSpan.FromMinutes(4);
		default:
			return TimeSpan.FromMinutes(10);
		}
	}

	/// <summary>No exchange for the same trigger again for this long.</summary>
	private static TimeSpan TriggerCooldown()
	{
		switch (Settings.Level)
		{
		case Level.Chatty:
			return TimeSpan.FromMinutes(10);
		case Level.Normal:
			return TimeSpan.FromMinutes(20);
		default:
			return TimeSpan.FromMinutes(30);
		}
	}

	private static double SquabbleChance()
	{
		switch (Settings.Level)
		{
		case Level.Chatty:
			return 0.7;
		case Level.Normal:
			return 0.4;
		default:
			return 0.15;
		}
	}

	/// <summary>Next time Wilson may share a proverb, counted from now (anyone speaking starts it over).</summary>
	private static void ScheduleIdle()
	{
		double minutes;
		switch (Settings.Level)
		{
		case Level.Chatty:
			minutes = 12 + 13 * s_random.NextDouble();
			break;
		case Level.Normal:
			minutes = 25 + 20 * s_random.NextDouble();
			break;
		default:
			// Quiet and Off: no proverbs.
			s_nextIdleAt = DateTime.MaxValue;
			return;
		}
		s_nextIdleAt = DateTime.UtcNow + TimeSpan.FromMinutes(minutes);
	}

	// ---- Log ----

	private static string Label(Character c, string key)
	{
		Cast.Member member = Cast.ByChatName(c.Name);
		return member != null ? member.Id + "." + key : c.Name + " " + key;
	}

	private static void AddLog(string text)
	{
		s_log.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text);
		if (s_log.Count > MaxLog)
		{
			s_log.RemoveAt(s_log.Count - 1);
		}
	}
}
