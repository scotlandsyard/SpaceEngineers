using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;

namespace Wilson;

/// <summary>
/// The banter director on the plugins' personality channel (see "Banter (the director)" in Shared/README.md). The
/// plugins send Wilson their events instead of talking themselves. He keeps one rhythm for everyone: one turn per
/// cycle, where a turn is one plugin saying one of its own lines ("say"), or a short exchange between several
/// characters from Wilson.txt, one line every few seconds. On world load he calls a roll call, and when the player
/// types /tim his guest Tim sometimes pops up. Everything runs on the game thread: mod messages are delivered
/// there, straight from the sender's call.
/// </summary>
internal static class Director
{
	/// <summary>Private message channel for the plugins' personalities ("TIM_PERS"), the same as Shared/Personality.cs.</summary>
	private const long Channel = 0x54494D5F50455253L;

	/// <summary>Ticks after the world loads before the roll call, about when the plugins used to greet.</summary>
	private const int RollCallTicks = 600;

	/// <summary>An exchange trigger doesn't come round again for this long.</summary>
	private static readonly TimeSpan TriggerCooldown = TimeSpan.FromMinutes(30);

	/// <summary>Tim doesn't pop up again for at least this long.</summary>
	private static readonly TimeSpan TimCooldown = TimeSpan.FromMinutes(10);

	/// <summary>Wilson's proverb comes after this many cycles without a word.</summary>
	private const int IdleCycles = 3;

	private const int MaxLog = 40;

	/// <summary>A plugin character Wilson has heard from this session.</summary>
	public sealed class Character
	{
		/// <summary>Its own name: how it's addressed on the channel and named in Wilson.txt.</summary>
		public readonly string Name;

		/// <summary>The plugin's own Personality setting. Plugins built before the level was sent count as Normal.</summary>
		public Level Level = Level.Normal;

		/// <summary>The player's name for it, or null for its own.</summary>
		public string DisplayName;

		/// <summary>When this character may next have an ordinary turn (its own setting's cycle after its last line).</summary>
		public DateTime NextAt;

		public Character(string name)
		{
			Name = name;
		}
	}

	/// <summary>One step of a turn: a line shown by Wilson, or a plugin asked to say one of its own lines.</summary>
	private sealed class Step
	{
		public string Speaker;

		public string Text;

		public string SayKey;

		public string[] SayValues;

		/// <summary>Only played if someone answered the steps before it (the roll call's closing line).</summary>
		public bool NeedsAnswer;
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

	/// <summary>When anyone last showed a line (important lines wait 30 s after it).</summary>
	private static DateTime s_lastLineAt;

	/// <summary>When the last turn ended; the next ordinary turn waits a cycle after it.</summary>
	private static DateTime s_turnEndAt;

	private static int s_tick;

	private static bool s_rollCallDone;

	private static bool s_timSeen;

	private static DateTime s_timNextAt;

	// The turn being played: its steps, the next one to play, and when.
	private static List<Step> s_playing;

	private static int s_stepIndex;

	private static DateTime s_nextStepAt;

	private static bool s_squabbleChecked;

	private static int s_answers;

	/// <summary>Who showed a line during the last "say" we sent (the plugin answers from inside the call).</summary>
	private static string s_lastSpeaker;

	/// <summary>Wilson is directing: the plugins send him their events.</summary>
	public static bool Directing => s_directing;

	/// <summary>A turn (exchange, roll call) is playing right now.</summary>
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
		s_rollCallDone = false;
		s_timSeen = false;
		s_timNextAt = DateTime.MinValue;
		s_playing = null;
		s_lastLineAt = DateTime.MinValue;
		// The first turn after the roll call still waits a full cycle.
		s_turnEndAt = DateTime.UtcNow;
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
			return;
		}
		s_directing = direct;
		if (direct)
		{
			AddLog("Wilson is directing: the plugins send him their events.");
			// Loaded plugins answer with "here", so Wilson knows who's around, how much each talks and its name.
			Send("director");
		}
		else
		{
			s_playing = null;
			AddLog("Wilson is Off: each plugin talks on its own.");
			Send("bye");
		}
	}

	/// <summary>A plugin character Wilson has heard from this session, by its own name.</summary>
	public static Character Find(string chatName)
	{
		return chatName != null && s_roster.TryGetValue(chatName, out Character c) ? c : null;
	}

	/// <summary>The name a character's lines show under: the player's name for it, or its own.</summary>
	public static string DisplayFor(string name)
	{
		if (Cast.IsWilson(name))
		{
			return Settings.WilsonName ?? Cast.WilsonName;
		}
		if (Cast.IsTim(name))
		{
			return Settings.TimName ?? Cast.TimName;
		}
		return Find(name)?.DisplayName ?? name;
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
			// Turned on later in the session: no roll call then.
			s_rollCallDone |= s_tick >= RollCallTicks;
			return;
		}
		DateTime now = DateTime.UtcNow;
		if (s_playing != null)
		{
			if (now >= s_nextStepAt)
			{
				PlayNextStep(now);
			}
			return;
		}
		if (!s_rollCallDone)
		{
			if (s_tick >= RollCallTicks)
			{
				RollCall(now);
			}
			return;
		}
		if (Settings.Level >= Level.Normal && now >= s_turnEndAt + TimeSpan.FromTicks(Settings.Cycle.Ticks * IdleCycles))
		{
			// Rarely: half the time the quiet spell just goes on for another cycle.
			if (s_random.NextDouble() < 0.5 && Lines.Own("idle") is string line)
			{
				AddLog("A long quiet spell: Wilson shares a proverb.");
				Play(new List<Step> { new Step { Speaker = Cast.WilsonName, Text = line } }, now);
			}
			else
			{
				s_turnEndAt = now - TimeSpan.FromTicks(Settings.Cycle.Ticks * (IdleCycles - 1));
			}
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
		if (!(message is object[] parts) || parts.Length < 2 || !(parts[0] is string kind) || !(parts[1] is string name) || Cast.IsHome(name))
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
				OnEvent(c, key, parts.Length > 3 ? parts[3] as string[] : null, parts.Length > 6 && parts[6] is string important && important == "1");
			}
			break;
		case "spoke":
			Character speaker = Seen(name, parts);
			DateTime now = DateTime.UtcNow;
			s_lastLineAt = now;
			speaker.NextAt = now + Personality.Cycle(Settings.ToChattiness(speaker.Level));
			s_lastSpeaker = name;
			break;
		case "director":
			AddLog($"Another director ({name}) announced itself; Wilson carries on.");
			break;
		}
	}

	/// <summary>Adds the sender to the roster and notes its setting and name, if the message carries them.</summary>
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
		if (parts.Length > 5 && parts[5] is string shown)
		{
			string display = Personality.CleanName(shown);
			display = display != null && display.Equals(name, StringComparison.Ordinal) ? null : display;
			if (display != c.DisplayName)
			{
				c.DisplayName = display;
				AddLog(display == null ? $"{name} goes by its own name again." : $"{name} now goes by {display}.");
			}
		}
		return c;
	}

	// ---- Events ----

	private static void OnEvent(Character c, string key, string[] values, bool important)
	{
		// Greetings are retired (the roll call replaces them); older plugin builds may still send one.
		if (c.Level == Level.Off || key.Equals("greeting", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		Cast.Member member = Cast.ByChatName(c.Name);
		string trigger = member != null ? member.Id + "." + key : c.Name + " " + key;
		DateTime now = DateTime.UtcNow;
		if (s_playing != null)
		{
			AddLog($"{trigger}: a turn is playing, so it passes.");
			return;
		}
		if (important)
		{
			if (now < s_lastLineAt + Personality.ImportantFloor)
			{
				AddLog($"{trigger} (important): someone spoke under 30 s ago, so it passes.");
				return;
			}
		}
		else if (now < s_turnEndAt + Settings.Cycle)
		{
			AddLog($"{trigger}: waiting for the cycle, so it passes.");
			return;
		}
		else if (now < c.NextAt)
		{
			AddLog($"{trigger}: {c.Name} spoke within its own cycle, so it passes.");
			return;
		}
		string eventKey = c.Name + "|" + key;
		if (s_eventNext.TryGetValue(eventKey, out DateTime next) && now < next)
		{
			AddLog($"{trigger}: said in the last 10 minutes, so it passes.");
			return;
		}
		s_eventNext[eventKey] = now + Personality.EventCooldown;
		if (member != null && TryExchange(trigger, values, c, now))
		{
			return;
		}
		// The plugin answers from inside this call, so we know straight away whether it had a line.
		s_lastSpeaker = null;
		Send("say", c.Name, key, values);
		if (s_lastSpeaker != null)
		{
			AddLog($"{trigger}: {c.Name} says its own line.");
			s_turnEndAt = now;
		}
		else
		{
			AddLog($"{trigger}: {c.Name} has no line for it.");
		}
	}

	private static bool TryExchange(string trigger, string[] values, Character c, DateTime now)
	{
		if (c.Level < Level.Normal || ExchangeChance() <= 0.0)
		{
			return false;
		}
		List<Exchange> all = Lines.ExchangesFor(trigger);
		if (all.Count == 0 || (s_triggerNext.TryGetValue(trigger, out DateTime next) && now < next))
		{
			return false;
		}
		List<Exchange> playable = all.Where(e => e.Lines.All(l => CanSpeak(l.Speaker) && !Lines.HasPlaceholder(Personality.Fill(l.Text, values)))).ToList();
		if (playable.Count == 0)
		{
			AddLog($"{trigger}: no exchange has all its speakers here with chat on.");
			return false;
		}
		if (s_random.NextDouble() >= ExchangeChance())
		{
			return false;
		}
		s_triggerNext[trigger] = now + TriggerCooldown;
		AddLog($"{trigger}: Wilson stages an exchange.");
		PlayExchange(Pick(trigger, playable), values, now);
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

	/// <summary>Wilson and Tim always can; a plugin character only when it's loaded with chat on Normal or Chatty.</summary>
	private static bool CanSpeak(string chatName)
	{
		if (Cast.IsHome(chatName))
		{
			return true;
		}
		return Cast.ByChatName(chatName) != null && s_roster.TryGetValue(chatName, out Character c) && c.Level >= Level.Normal;
	}

	// ---- Roll call ----

	/// <summary>
	/// About ten seconds after the world loads: Wilson calls the roll, each loaded character answers once with how
	/// to call it up, and Wilson wraps up. Nothing at all when no other character is loaded.
	/// </summary>
	private static void RollCall(DateTime now)
	{
		s_rollCallDone = true;
		List<Step> steps = new List<Step>();
		foreach (Cast.Member member in Cast.Others)
		{
			if (s_roster.TryGetValue(member.ChatName, out Character c) && c.Level != Level.Off)
			{
				steps.Add(new Step { Speaker = member.ChatName, SayKey = "rollcall", SayValues = new[] { "command", member.Command } });
			}
		}
		if (steps.Count == 0)
		{
			AddLog("World loaded: nobody else is here, so no roll call.");
			return;
		}
		if (Lines.Own("rollcall_open") is string open)
		{
			steps.Insert(0, new Step { Speaker = Cast.WilsonName, Text = open });
		}
		if (Lines.Own("rollcall_close") is string close)
		{
			steps.Add(new Step { Speaker = Cast.WilsonName, Text = Personality.Fill(close, new[] { "command", Cast.WilsonCommand }), NeedsAnswer = true });
		}
		AddLog($"World loaded: Wilson calls the roll ({steps.Count(s => s.SayKey != null)} to answer).");
		Play(steps, now);
	}

	// ---- Tim ----

	/// <summary>The player typed /tim: sometimes Wilson's guest Tim pops up. Rare: the first time, then now and then.</summary>
	public static void OnTimCommand()
	{
		if (!s_directing || s_playing != null || !s_rollCallDone)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		double chance = !s_timSeen ? 1.0 : Settings.Level == Level.Chatty ? 0.35 : Settings.Level == Level.Normal ? 0.2 : 0.0;
		if (now < s_timNextAt || now < s_lastLineAt + Personality.ImportantFloor || s_random.NextDouble() >= chance)
		{
			return;
		}
		List<Exchange> exchanges = Lines.ExchangesFor("any.tim");
		if (exchanges.Count > 0 && s_random.NextDouble() < 0.5)
		{
			AddLog("/tim: Tim drops by, and Wilson has a word with him.");
			PlayExchange(Pick("any.tim", exchanges), null, now);
		}
		else if (Lines.Own("tim") is string line)
		{
			AddLog("/tim: Tim drops by.");
			Play(new List<Step> { new Step { Speaker = Cast.TimName, Text = line } }, now);
		}
		else
		{
			return;
		}
		s_timSeen = true;
		s_timNextAt = now + TimCooldown;
	}

	// ---- Playing a turn ----

	private static void PlayExchange(Exchange exchange, string[] values, DateTime now)
	{
		Play(exchange.Lines.Select(l => new Step { Speaker = l.Speaker, Text = Personality.Fill(l.Text, values) }).ToList(), now);
	}

	private static void Play(List<Step> steps, DateTime now)
	{
		s_playing = steps;
		s_stepIndex = 0;
		s_answers = 0;
		s_squabbleChecked = false;
		s_nextStepAt = now;
		PlayNextStep(now);
	}

	private static void PlayNextStep(DateTime now)
	{
		while (true)
		{
			if (s_stepIndex >= s_playing.Count && !s_squabbleChecked)
			{
				s_squabbleChecked = true;
				if (EndsInArgument() && s_random.NextDouble() < SquabbleChance() && Lines.Own("squabble") is string line)
				{
					AddLog("That ended in an argument: Wilson steps in.");
					s_playing.Add(new Step { Speaker = Cast.WilsonName, Text = line });
				}
			}
			if (s_stepIndex >= s_playing.Count)
			{
				Finish(now);
				return;
			}
			Step step = s_playing[s_stepIndex++];
			if (step.NeedsAnswer && s_answers == 0)
			{
				continue;
			}
			if (step.SayKey == null)
			{
				ShowLine(step.Speaker, step.Text, now);
				s_nextStepAt = now + LineDelay(step.Text);
				return;
			}
			// A plugin's own line. It answers from inside the call; one that doesn't is skipped without a pause.
			s_lastSpeaker = null;
			Send("say", step.Speaker, step.SayKey, step.SayValues);
			if (s_lastSpeaker != null)
			{
				s_answers++;
				s_nextStepAt = now + TimeSpan.FromSeconds(5);
				return;
			}
			AddLog($"{step.Speaker} didn't answer.");
		}
	}

	/// <summary>Someone answered back and Wilson didn't get the last word: A, B, then A again.</summary>
	private static bool EndsInArgument()
	{
		List<Step> lines = s_playing.Where(s => s.SayKey == null).ToList();
		if (lines.Count < 3 || lines.Count != s_playing.Count)
		{
			return false;
		}
		string last = lines[lines.Count - 1].Speaker;
		return !Cast.IsHome(last) && lines.Take(lines.Count - 1).Any(l => l.Speaker.Equals(last, StringComparison.OrdinalIgnoreCase));
	}

	private static void Finish(DateTime now)
	{
		s_playing = null;
		s_turnEndAt = now;
	}

	private static void ShowLine(string speaker, string text, DateTime now)
	{
		Personality.Show(speaker, DisplayFor(speaker), text);
		s_lastLineAt = now;
		if (s_roster.TryGetValue(speaker, out Character c))
		{
			c.NextAt = now + Personality.Cycle(Settings.ToChattiness(c.Level));
		}
		Send("spoke");
	}

	/// <summary>Time to read a line before the next one: longer lines get longer.</summary>
	private static TimeSpan LineDelay(string text)
	{
		double seconds = 3.0 + 0.04 * (text?.Length ?? 0);
		return TimeSpan.FromSeconds(Math.Max(4.0, Math.Min(7.0, seconds)));
	}

	// ---- Renaming ----

	/// <summary>
	/// Gives a character a new name (null or blank for its own). Wilson and Tim keep theirs in Wilson's settings; a
	/// plugin character is asked to rename itself, saves it in its own settings, and confirms with "here".
	/// </summary>
	public static string Rename(string name, string newName)
	{
		newName = Personality.CleanName(newName);
		string shown = newName ?? name;
		if (Cast.IsWilson(name) || Cast.IsTim(name))
		{
			newName = newName != null && newName.Equals(name, StringComparison.Ordinal) ? null : newName;
			if (Cast.IsWilson(name))
			{
				Settings.WilsonName = newName;
			}
			else
			{
				Settings.TimName = newName;
			}
			Settings.Save();
			AddLog($"{name} now goes by {shown}.");
			return $"{name} now goes by {shown}.";
		}
		Character c = Find(name);
		if (c == null)
		{
			return $"{name} isn't loaded, so it can't be renamed from here.";
		}
		SendRename(c, newName ?? "", null);
		return (c.DisplayName ?? c.Name) == shown ? $"{name} now goes by {shown}." : $"Asked {name} to go by {shown}. If the name doesn't change, that plugin needs updating.";
	}

	/// <summary>
	/// Sets a plugin character's own chat level from the Crew page. The plugin applies it through Personality.Level,
	/// saves it in its own settings, and confirms with "here". Wilson's own setting goes through WilsonSession.SetLevel.
	/// </summary>
	public static string SetLevel(string name, Level level)
	{
		Character c = Find(name);
		if (c == null)
		{
			return $"{name} isn't loaded, so its chat can't be set from here.";
		}
		// The rename message carries the level too; the current name goes along unchanged.
		SendRename(c, c.DisplayName ?? "", level.ToString());
		return c.Level == level ? $"{name}'s chat is {level}." : $"Asked {name} to set its chat to {level}. If it doesn't change, that plugin needs updating.";
	}

	/// <summary>{"rename", "Wilson", target, newName, level}: level is null to leave it alone. Older plugins only read the name.</summary>
	private static void SendRename(Character c, string newName, string level)
	{
		MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { "rename", Cast.WilsonName, c.Name, newName, level });
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
			return "A turn is already playing.";
		}
		List<Exchange> playable = Lines.AllExchanges().Where(e => e.Lines.All(l => CanSpeak(l.Speaker))).ToList();
		if (playable.Count == 0)
		{
			return "No exchange has all its speakers loaded with chat on. Load more of the family.";
		}
		Exchange exchange = Pick("(tried from the window)", playable);
		AddLog($"Tried from the window: an exchange for {exchange.Trigger}.");
		PlayExchange(exchange, SampleValues, DateTime.UtcNow);
		return $"Playing an exchange for {exchange.Trigger} in chat (with made-up names and numbers).";
	}

	/// <summary>Plays the roll call again now, ignoring the timers.</summary>
	public static string RollCallNow()
	{
		if (!s_directing)
		{
			return "Wilson is Off. Pick another setting first.";
		}
		if (s_playing != null)
		{
			return "A turn is already playing.";
		}
		RollCall(DateTime.UtcNow);
		return s_playing != null ? "Calling the roll in chat." : "Nobody else is loaded with chat on, so there's no roll to call.";
	}

	/// <summary>How many exchanges could play with the characters loaded now, and how many there are.</summary>
	public static string ExchangeSummary()
	{
		int total = Lines.ExchangeCount;
		int playable = Lines.AllExchanges().Count(e => e.Lines.All(l => CanSpeak(l.Speaker)));
		return $"{playable} of {total} exchanges have all their speakers here.";
	}

	// ---- Chances by level ----

	/// <summary>Chance that a turn with a playable exchange gets the exchange instead of the plugin's own line.</summary>
	private static double ExchangeChance()
	{
		switch (Settings.Level)
		{
		case Level.Chatty:
			return 0.5;
		case Level.Normal:
			return 0.3;
		default:
			return 0.0;
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
			return 0.0;
		}
	}

	// ---- Log ----

	private static void AddLog(string text)
	{
		s_log.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text);
		if (s_log.Count > MaxLog)
		{
			s_log.RemoveAt(s_log.Count - 1);
		}
	}
}
