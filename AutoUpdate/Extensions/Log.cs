using System;

namespace AutoUpdate.Extensions;

public class Log
{
    public static void Debug(string message)
    {
        if (Plugin.Instance.Config.Debug)
        {
            ServerConsole.AddLog($"[Debug] [{Plugin.Instance.Name}] {message}", ConsoleColor.Green);
        }
    }
    public static void Info(string message)
    {
        ServerConsole.AddLog($"[INFO] [{Plugin.Instance.Name}] {message}", ConsoleColor.DarkBlue);
    }
    public static void Warn(string message)
    {
        ServerConsole.AddLog($"[Warn] [{Plugin.Instance.Name}] {message}", ConsoleColor.Yellow);
    }
    public static void Update(string message)
    {
        ServerConsole.AddLog($"{message}", ConsoleColor.Cyan);
    }
    public static void Error(string message)
    {
        ServerConsole.AddLog($"[Error] [{Plugin.Instance.Name}] {message}", ConsoleColor.Red);
    }
}