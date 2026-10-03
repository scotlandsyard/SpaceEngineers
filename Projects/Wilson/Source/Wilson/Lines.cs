using System;
using System.Collections.Generic;
using System.IO;
using VRage.Utils;

namespace Wilson;

/// <summary>One line of an exchange: who says it, and what (placeholders not filled in yet).</summary>
internal sealed class ExchangeLine
{
	public readonly string Speaker;

	public readonly string Text;

	public ExchangeLine(string speaker, string text)
	{
		Speaker = speaker;
		Text = text;
	}
}

/// <summary>A short written scene between several characters, played when its trigger event happens.</summary>
internal sealed class Exchange
{
	/// <summary>character.event, e.g. barmaid.starved, or any.greeting.</summary>
	public readonly string Trigger;

	public readonly List<ExchangeLine> Lines = new List<ExchangeLine>();

	public Exchange(string trigger)
	{
		Trigger = trigger;
	}
}

/// <summary>
/// Reads Wilson.txt (an embedded resource). A [key] line starts Wilson's own lines for that key (rollcall_open,
/// rollcall_close, idle, squabble) or Tim's (tim), one per line. [exchange trigger=character.event] starts an exchange: one "Chat name: text" per line,
/// in the order they're said. Blank lines and lines starting with # are skipped, and a leading "- " is dropped.
/// </summary>
internal static class Lines
{
	private const string ResourceName = "Wilson.txt";

	private const string ExchangePrefix = "exchange trigger=";

	private static readonly Dictionary<string, List<string>> s_own = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, List<Exchange>> s_exchanges = new Dictionary<string, List<Exchange>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Random s_random = new Random();

	private static readonly Dictionary<string, string> s_lastOwn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public static int ExchangeCount { get; private set; }

	public static void Load()
	{
		s_own.Clear();
		s_exchanges.Clear();
		s_lastOwn.Clear();
		ExchangeCount = 0;
		using (Stream stream = typeof(Lines).Assembly.GetManifestResourceStream(ResourceName))
		{
			if (stream == null)
			{
				MyLog.Default.WriteLineAndConsole($"[Wilson] No {ResourceName} embedded");
				return;
			}
			using (StreamReader reader = new StreamReader(stream))
			{
				List<string> own = null;
				Exchange exchange = null;
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
						string key = text.Substring(1, text.Length - 2).Trim().TrimStart('!').Trim();
						own = null;
						exchange = null;
						if (key.StartsWith(ExchangePrefix, StringComparison.OrdinalIgnoreCase))
						{
							exchange = new Exchange(key.Substring(ExchangePrefix.Length).Trim());
							if (!s_exchanges.TryGetValue(exchange.Trigger, out List<Exchange> list))
							{
								list = new List<Exchange>();
								s_exchanges[exchange.Trigger] = list;
							}
							list.Add(exchange);
							ExchangeCount++;
						}
						else if (!s_own.TryGetValue(key, out own))
						{
							own = new List<string>();
							s_own[key] = own;
						}
						continue;
					}
					if (text.StartsWith("- "))
					{
						text = text.Substring(2).Trim();
					}
					if (own != null)
					{
						own.Add(text);
					}
					else if (exchange != null)
					{
						int colon = text.IndexOf(':');
						if (colon > 0 && colon < text.Length - 1)
						{
							exchange.Lines.Add(new ExchangeLine(text.Substring(0, colon).Trim(), text.Substring(colon + 1).Trim()));
						}
						else
						{
							MyLog.Default.WriteLineAndConsole($"[Wilson] {ResourceName}: no speaker in '{text}'");
						}
					}
				}
			}
		}
	}

	/// <summary>The exchanges for a trigger such as barmaid.starved; empty if there are none.</summary>
	public static List<Exchange> ExchangesFor(string trigger)
	{
		return s_exchanges.TryGetValue(trigger, out List<Exchange> list) ? list : new List<Exchange>();
	}

	public static IEnumerable<Exchange> AllExchanges()
	{
		foreach (List<Exchange> list in s_exchanges.Values)
		{
			foreach (Exchange exchange in list)
			{
				yield return exchange;
			}
		}
	}

	/// <summary>One line for one of Wilson's or Tim's own keys, never the same one twice in a row; null if there are none.</summary>
	public static string Own(string key)
	{
		if (!s_own.TryGetValue(key, out List<string> list) || list.Count == 0)
		{
			return null;
		}
		s_lastOwn.TryGetValue(key, out string last);
		string line = list[s_random.Next(list.Count)];
		if (line == last && list.Count > 1)
		{
			line = list[(list.IndexOf(line) + 1 + s_random.Next(list.Count - 1)) % list.Count];
		}
		s_lastOwn[key] = line;
		return line;
	}

	public static bool HasPlaceholder(string line)
	{
		int open = line.IndexOf('{');
		return open >= 0 && line.IndexOf('}', open) > open;
	}
}
