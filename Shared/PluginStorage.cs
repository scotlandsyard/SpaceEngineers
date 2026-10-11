using System;
using System.IO;
using VRage.FileSystem;

namespace TimShared;

/// <summary>
/// Drop-in replacement for MyAPIGateway.Utilities' FileExists/Read/WriteFileInLocalStorage. The game names the
/// folder after the calling type's DLL, but Pulsar's PluginHub compiles each plugin under a new random assembly
/// name on every rebuild, so settings saved there would be lost after each update. This keeps the folder the game
/// would use for our own DLL name, Storage\&lt;first part of the type's namespace&gt;, which is the plugin's assembly
/// name, so files saved by a plugin installed as a DLL are found again when it's built by the hub.
/// </summary>
internal static class PluginStorage
{
	public static bool FileExists(string file, Type callingType)
	{
		string path = GetPath(file, callingType);
		return path != null && File.Exists(path);
	}

	public static TextReader Read(string file, Type callingType)
	{
		Stream stream = MyFileSystem.OpenRead(GetPath(file, callingType) ?? throw new FileNotFoundException());
		return stream != null ? new StreamReader(stream) : throw new FileNotFoundException();
	}

	public static TextWriter Write(string file, Type callingType)
	{
		Stream stream = MyFileSystem.OpenWrite(GetPath(file, callingType) ?? throw new FileNotFoundException());
		return stream != null ? new StreamWriter(stream) : throw new FileNotFoundException();
	}

	/// <summary>Same checks as the game's: no invalid file name characters, no way out of the folder.</summary>
	private static string GetPath(string file, Type callingType)
	{
		if (file.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
			return null;
		string folder = Path.Combine(MyFileSystem.UserDataPath, "Storage", callingType.Namespace.Split('.')[0]);
		string path = Path.Combine(folder, file);
		return Path.GetFullPath(path).StartsWith(folder) ? path : null;
	}
}
