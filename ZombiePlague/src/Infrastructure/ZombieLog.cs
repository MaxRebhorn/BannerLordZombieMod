using System;
using System.IO;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// Diagnostics sink. Primary target is the engine log
/// (C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt) via
/// Debug.Print - that path is proven to work from inside the game process and
/// survives hard native crashes, because the engine flushes it continuously.
///
/// A plain file in the module folder is written as a convenience copy. It is
/// strictly best effort: writing into the user's Documents folder silently
/// failed (Windows folder protection), which is exactly why the engine log is
/// the primary target now.
/// </summary>
internal static class ZombieLog
{
	private const string Prefix = "[ZombiePlague] ";

	private static readonly object Gate = new();
	private static string _logFilePath;
	private static bool _filePathResolved;

	public static void Info(string message)
	{
		Write("INFO ", message, notifyChat: false);
	}

	public static void Warn(string message)
	{
		Write("WARN ", message, notifyChat: true);
	}

	public static void Error(string message, Exception exception = null)
	{
		Write("ERROR", exception == null ? message : message + " :: " + exception, notifyChat: true);
	}

	private static void Write(string level, string message, bool notifyChat)
	{
		string line = Prefix + level + " " + message;

		try
		{
			Debug.Print(line);
		}
		catch
		{
			// Never let diagnostics take the game down.
		}

		WriteToFile(line);

		if (notifyChat)
		{
			NotifyChat(level, message);
		}
	}

	private static void NotifyChat(string level, string message)
	{
		if (!ZombieBehaviorConfig.DebugChatMessagesEnabled)
		{
			return;
		}

		try
		{
			if (Game.Current == null)
			{
				return;
			}

			Color color = level == "ERROR" ? Colors.Red : Colors.Yellow;
			InformationManager.DisplayMessage(new InformationMessage(Prefix + level + ": " + message, color));
		}
		catch
		{
			// A broken notification must never take the game down either.
		}
	}

	private static void WriteToFile(string line)
	{
		try
		{
			string path = ResolveFilePath();
			if (path == null)
			{
				return;
			}

			lock (Gate)
			{
				File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
			}
		}
		catch
		{
			// Best effort only - the engine log above is the source of truth.
		}
	}

	private static string ResolveFilePath()
	{
		if (_filePathResolved)
		{
			return _logFilePath;
		}

		_filePathResolved = true;
		try
		{
			// <module>/bin/Win64_Shipping_Client/ZombiePlague.dll -> <module>/
			string assemblyPath = Assembly.GetExecutingAssembly().Location;
			string moduleRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(assemblyPath)));
			if (moduleRoot != null)
			{
				_logFilePath = Path.Combine(moduleRoot, "zombieplague.log");
			}
		}
		catch
		{
			_logFilePath = null;
		}

		return _logFilePath;
	}
}
