using System;
using System.IO;
using System.Reflection;
using MelonLoader;

namespace SnapAccess;

public static class DebugLogger
{
	private static string _logPath;

	private static bool _initialized = false;

	private static readonly object _lock = new object();

	public static void Initialize()
	{
		if (_initialized)
		{
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			_logPath = Path.Combine(directoryName, "SnapAccess_debug.log");
			if (File.Exists(_logPath))
			{
				File.Delete(_logPath);
			}
			_initialized = true;
			Log(LogCategory.State, "DebugLogger initialized");
		}
		catch (Exception ex)
		{
            MelonLogger.Warning("Could not initialize log file: " + ex.Message);
		}
	}

	public static void Log(LogCategory category, string message)
	{
		string prefix = GetPrefix(category);
		string text = prefix + " " + message;
		WriteToFile(text);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text);
		}
	}

	public static void Log(LogCategory category, string source, string message)
	{
		string prefix = GetPrefix(category);
		string text = $"{prefix} [{source}] {message}";
		WriteToFile(text);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text);
		}
	}

	public static void LogScreenReader(string text)
	{
		string text2 = "[SR] " + text;
		WriteToFile(text2);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text2);
		}
	}

	public static void LogInput(string keyName, string action = null)
	{
		string text = ((action != null) ? (keyName + " -> " + action) : keyName);
		string text2 = "[INPUT] " + text;
		WriteToFile(text2);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text2);
		}
	}

	public static void LogState(string description)
	{
		string text = "[STATE] " + description;
		WriteToFile(text);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text);
		}
	}

	public static void LogGameValue(string name, object value)
	{
		string text = $"[GAME] {name} = {value}";
		WriteToFile(text);
		if (Main.DebugMode)
		{
			MelonLogger.Msg(text);
		}
	}

	public static void Warning(string message)
	{
		string line = "[WARN] " + message;
		WriteToFile(line);
		MelonLogger.Warning(message);
	}

	public static void Error(string message)
	{
		string line = "[ERROR] " + message;
		WriteToFile(line);
		MelonLogger.Error(message);
	}

	private static void WriteToFile(string line)
	{
		if (!_initialized || _logPath == null)
		{
			return;
		}
		try
		{
			lock (_lock)
			{
				using StreamWriter streamWriter = new StreamWriter(_logPath, append: true);
				streamWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
			}
		}
		catch
		{
		}
	}

	private static string GetPrefix(LogCategory category)
	{
		string result = category switch
		{
			LogCategory.ScreenReader => "[SR]", 
			LogCategory.Input => "[INPUT]", 
			LogCategory.State => "[STATE]", 
			LogCategory.Handler => "[HANDLER]", 
			LogCategory.Game => "[GAME]", 
			_ => "[DEBUG]", 
		};
		return result;
	}
}
