using System;
using System.Linq;
using System.Text;
using CommandSystem;
using Exiled.API.Interfaces;
using Exiled.Loader;
using Exiled.Permissions.Extensions;

namespace AutoUpdate.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    public class AutoUpdateCommand : ICommand, IUsageProvider
    {
        public string Command => "autoupdate";
        public string[] Usage { get; } = ["check/list/info"];
        public string[] Aliases => new[] { "au" };
        public string Description => "Manages the AutoUpdate plugin. Available subcommands: check, list, info.";
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response = $"Unknown subcommand. Available options: check, list, info.";
                return false;
            }

            string subCommand = arguments.At(0).ToLower();
            
            switch (subCommand)
            {
                case "check":
                    return HandleCheckCommand(sender, out response);

                case "list":
                    return HandleListCommand(sender, out response);

                case "info":
                    return HandleInfoCommand(arguments, sender, out response);

                default:
                    response = $"Unknown subcommand '{subCommand}'. Available options: check, list, info.";
                    return false;
            }
        }
        private bool HandleCheckCommand(ICommandSender sender, out string response)
        {
            if (!sender.CheckPermission("au.check"))
            {
                response = "You do not have permission to use this command (au.check).";
                return false;
            }

            Updater.CheckForUpdates().ConfigureAwait(false);
            
            response = "Update check has been started. Check the server console for progress and results.";
            return true;
        }
        private bool HandleListCommand(ICommandSender sender, out string response)
        {
            if (!sender.CheckPermission("au.list"))
            {
                response = "You do not have permission to use this command (au.list).";
                return false;
            }

            var combinedRepos = Updater.GetCombinedRepositories();
            var fileRepos = Updater.GetRepositoriesFromFile();

            if (combinedRepos == null || !combinedRepos.Any())
            {
                response = "No plugins are configured for auto-update.";
                return true;
            }
            
            var sb = new StringBuilder("The following plugins are configured for AutoUpdate:\n");
            foreach (var repoEntry in combinedRepos)
            {
                bool isFromFile = fileRepos.Keys.Any(k => k.Equals(repoEntry.Key, StringComparison.OrdinalIgnoreCase));
                string sourceType = isFromFile ? "File" : "Integration";
                
                sb.AppendLine($"- {repoEntry.Key} (Source: {repoEntry.Value.User}/{repoEntry.Value.Repository}) [Type: {sourceType}]");
            }

            response = sb.ToString();
            return true;
        }
        private bool HandleInfoCommand(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!sender.CheckPermission("au.info"))
            {
                response = "You do not have permission to use this command (au.info).";
                return false;
            }

            if (arguments.Count < 2)
            {
                response = "Usage: au info <PluginName>";
                return false;
            }

            string pluginName = arguments.At(1);
            var repositories = Updater.GetCombinedRepositories();

            if (!repositories.TryGetValue(pluginName, out var config))
            {
                response = $"Plugin '{pluginName}' is not configured for auto-update.";
                return false;
            }

            IPlugin<IConfig> installedPlugin = Loader.GetPlugin(pluginName);
            
            var sb = new StringBuilder($"AutoUpdate Info for '{pluginName}':\n");
            sb.AppendLine($" - GitHub User: {config.User}");
            sb.AppendLine($" - GitHub Repository: {config.Repository}");
            sb.AppendLine($" - Specific DLL Name: {(string.IsNullOrEmpty(config.FileName) ? "(Not set, will find the first .dll)" : config.FileName)}");
            sb.AppendLine($" - Status: {(installedPlugin == null ? "Not currently installed/loaded." : $"Installed (Version: v{installedPlugin.Version})")}");

            response = sb.ToString();
            return true;
        }
    }
}