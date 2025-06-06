using System;

namespace AutoUpdate.Extensions;

public class Log
{
    public static void Info(string message)
    {
        LabApi.Features.Console.Logger.Raw($"[INFO] [{Plugin.Instance.Name}] {message}", ConsoleColor.DarkBlue);
    }
    public static void Warn(string message)
    {
        LabApi.Features.Console.Logger.Raw($"[Warn] [{Plugin.Instance.Name}] {message}", ConsoleColor.Yellow);
    }
    public static void Update(string message)
    {
        LabApi.Features.Console.Logger.Raw($"{message}", ConsoleColor.Cyan);
    }
    public static void Error(string message)
    {
        LabApi.Features.Console.Logger.Raw($"[Error] [{Plugin.Instance.Name}] {message}", ConsoleColor.Red);
    }
}