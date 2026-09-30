using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// One script: what a programmable block would hold (code, Custom Data, Storage, default argument, on/off)
/// plus the block that stands in for the programmable block.
/// </summary>
public class ScriptEntry
{
	[XmlAttribute]
	public string Id;

	public string Name = "";

	/// <summary>Entity id of the host block, the block the script sees as Me. 0 = none picked yet.</summary>
	public long HostId;

	/// <summary>Host name and grid name as last seen, shown while the host isn't loaded.</summary>
	public string HostName = "";

	public string HostGridName = "";

	public bool Enabled = true;

	/// <summary>The default run argument, like the programmable block's Argument box.</summary>
	public string Argument = "";

	// Code, Custom Data and Storage can hold characters XML can't store, so they are saved as Base64.
	[XmlElement("Code")]
	public string CodeBase64 = "";

	[XmlElement("CustomData")]
	public string CustomDataBase64 = "";

	[XmlElement("Storage")]
	public string StorageBase64 = "";

	[XmlIgnore]
	public string Code = "";

	[XmlIgnore]
	public string CustomData = "";

	[XmlIgnore]
	public string Storage = "";

	internal void Encode()
	{
		CodeBase64 = ToBase64(Code);
		CustomDataBase64 = ToBase64(CustomData);
		StorageBase64 = ToBase64(Storage);
	}

	internal void Decode()
	{
		Code = FromBase64(CodeBase64);
		CustomData = FromBase64(CustomDataBase64);
		Storage = FromBase64(StorageBase64);
	}

	private static string ToBase64(string text)
	{
		return string.IsNullOrEmpty(text) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
	}

	private static string FromBase64(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		try
		{
			return Encoding.UTF8.GetString(Convert.FromBase64String(text));
		}
		catch (FormatException)
		{
			return "";
		}
	}
}

public class ScriptFile
{
	public List<ScriptEntry> Scripts = new List<ScriptEntry>();
}

/// <summary>Keeps the scripts of the current world in the plugin's local storage, one file per world.</summary>
internal static class ScriptStore
{
	private static string _fileName;

	public static List<ScriptEntry> Load()
	{
		_fileName = FileName();
		List<ScriptEntry> scripts = new List<ScriptEntry>();
		try
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(_fileName, typeof(ScriptStore)))
			{
				using TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(_fileName, typeof(ScriptStore));
				ScriptFile file = MyAPIGateway.Utilities.SerializeFromXML<ScriptFile>(reader.ReadToEnd());
				if (file?.Scripts != null)
				{
					scripts.AddRange(file.Scripts.Where(s => s != null));
				}
			}
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not read {_fileName}: {e}");
		}
		foreach (ScriptEntry entry in scripts)
		{
			entry.Decode();
			if (string.IsNullOrEmpty(entry.Id))
			{
				entry.Id = NewId(scripts);
			}
		}
		return scripts;
	}

	public static void Save(IEnumerable<ScriptEntry> scripts)
	{
		if (_fileName == null)
		{
			return;
		}
		try
		{
			ScriptFile file = new ScriptFile();
			foreach (ScriptEntry entry in scripts)
			{
				entry.Encode();
				file.Scripts.Add(entry);
			}
			string xml = MyAPIGateway.Utilities.SerializeToXML(file);
			using TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(_fileName, typeof(ScriptStore));
			writer.Write(xml);
		}
		catch (Exception e)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not save {_fileName}: {e}");
		}
	}

	public static void Unload()
	{
		_fileName = null;
	}

	/// <summary>A short id that no other script in the list uses. It names the script's toolbar actions.</summary>
	public static string NewId(IEnumerable<ScriptEntry> existing)
	{
		HashSet<string> used = new HashSet<string>(existing.Select(e => e.Id).Where(id => id != null));
		Random random = new Random();
		string id;
		do
		{
			id = random.Next(0x10000000, int.MaxValue).ToString("x8");
		}
		while (used.Contains(id));
		return id;
	}

	private static string FileName()
	{
		string world = MyAPIGateway.Session?.Name ?? "Unknown";
		string safe = new string(world.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
		return $"ScriptToPlugin_{safe}.xml";
	}
}
