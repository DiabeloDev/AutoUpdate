using System;
using CommandSystem;
using Exiled.Permissions.Extensions;

namespace AutoUpdate.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    public class UpdateCheck : ICommand
    {
        public string Command => "updatecheck";
        public string[] Aliases => new[] { "au" };
        public string Description => "Checks for plugin updates based on the configuration file.";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!sender.CheckPermission("au.updatecheck"))
            {
                response = "You do not have permission to use this command (au.updatecheck).";
                return false;
            }
            Updater.CheckForUpdates().ConfigureAwait(false);
            response = "Update check has been started. Check the server console for progress and results.";
            return true;
        }
    }
}