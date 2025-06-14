using System;

namespace AutoUpdate.Extensions;

/// <summary>
/// A helper class for logging messages to the server console with various levels and colors.
/// </summary>
public class Log
{
    /// <summary>
    /// Logs a debug message to the server console if debugging is enabled in the plugin's configuration.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Debug(string message)
    {
        if (Plugin.Instance.Config.Debug)
        {
            ServerConsole.AddLog($"[Debug] [{Plugin.Instance.Name}] {message}", ConsoleColor.Green);
        }
    }
    /// <summary>
    /// Logs an informational message to the server console.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Info(string message)
    {
        ServerConsole.AddLog($"[INFO] [{Plugin.Instance.Name}] {message}", ConsoleColor.DarkBlue);
    }
    /// <summary>
    /// Logs a warning message to the server console.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Warn(string message)
    {
        ServerConsole.AddLog($"[Warn] [{Plugin.Instance.Name}] {message}", ConsoleColor.Yellow);
    }
    /// <summary>
    /// Logs a special message related to the update process to the server console.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Update(string message)
    {
        ServerConsole.AddLog($"{message}", ConsoleColor.Cyan);
    }
    /// <summary>
    /// Logs an error message to the server console.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Error(string message)
    {
        ServerConsole.AddLog($"[Error] [{Plugin.Instance.Name}] {message}", ConsoleColor.Red);
    }
}