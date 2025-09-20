using System;

namespace AutoUpdate.Extensions
{
    /// <summary>
    /// A helper class for logging messages to the server console with various levels and colors.
    /// It is designed to be safe even if called before the main plugin instance is initialized.
    /// </summary>
    public static class Log
    {
        // Safely gets the plugin name. Returns "AutoUpdate" if the instance is not yet available.
        private static string PluginName => Plugin.Instance?.Name ?? "AutoUpdate";

        // Safely checks if debug mode is enabled. Returns false if the config is not yet available.
        private static bool IsDebugEnabled => Plugin.Instance?.Config?.Debug ?? false;

        /// <summary>
        /// Logs a debug message to the server console if debugging is enabled in the plugin's configuration.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Debug(string message)
        {
            if (IsDebugEnabled)
            {
                ServerConsole.AddLog($"[Debug] [{PluginName}] {message}", ConsoleColor.Green);
            }
        }

        /// <summary>
        /// Logs an informational message to the server console.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Info(string message)
        {
            ServerConsole.AddLog($"[INFO] [{PluginName}] {message}", ConsoleColor.DarkBlue);
        }

        /// <summary>
        /// Logs a warning message to the server console.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Warn(string message)
        {
            ServerConsole.AddLog($"[Warn] [{PluginName}] {message}", ConsoleColor.Yellow);
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
            ServerConsole.AddLog($"[Error] [{PluginName}] {message}", ConsoleColor.Red);
        }
    }
}