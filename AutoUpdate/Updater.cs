using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AutoUpdate.Models;
using Exiled.API.Interfaces;
using Log = AutoUpdate.Extensions.Log;

namespace AutoUpdate
{
    /// <summary>
    /// Handles the entire process of checking for and applying plugin updates from GitHub.
    /// </summary>
    public static class Updater
    {
        #region Nested Types

        /// <summary>
        /// Represents the result of a single plugin update check.
        /// </summary>
        private class UpdateResult
        {
            public string PluginName { get; set; }
            public UpdateStatus Status { get; set; }
            public Version OldVersion { get; set; }
            public Version NewVersion { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Defines the possible outcomes of an update check.
        /// </summary>
        private enum UpdateStatus
        {
            UpToDate,
            Updated,
            PluginNotFound,
            ApiError,
            ConfigError,
            NoDllFound,
            WriteError
        }

        #endregion

        #region Fields & Properties

        private static GitHubConfig _githubConfig;
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly Dictionary<string, RepositoryConfig> DynamicallyRegisteredRepos = new Dictionary<string, RepositoryConfig>();
        
        /// <summary>
        /// Static constructor to initialize the HttpClient.
        /// </summary>
        static Updater()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "AutoUpdate-Plugin-for-EXILED");
        }
        #endregion
        
        #region Public Methods

        /// <summary>
        /// Allows other plugins to register themselves for auto-updating.
        /// This should be called during the plugin's OnEnabled lifecycle event.
        /// </summary>
        /// <param name="pluginName">The name of the plugin (must match the plugin's Name property).</param>
        /// <param name="githubUser">The GitHub username or organization.</param>
        /// <param name="githubRepo">The GitHub repository name.</param>
        /// <param name="fileName">Optional: The specific DLL file name in the release. If null, the first .dll found will be used.</param>
        public static void RegisterPluginForUpdates(string pluginName, string githubUser, string githubRepo, string fileName = null)
        {
            if (string.IsNullOrEmpty(pluginName) || string.IsNullOrEmpty(githubUser) || string.IsNullOrEmpty(githubRepo))
            {
                Log.Warn($"[Integration] A plugin tried to register for AutoUpdate but provided invalid information (pluginName, user, or repo was null/empty).");
                return;
            }

            if (DynamicallyRegisteredRepos.ContainsKey(pluginName))
            {
                Log.Debug($"[Integration] Plugin '{pluginName}' is already registered for updates. Overwriting previous registration.");
            }

            var repoConfig = new RepositoryConfig
            {
                User = githubUser,
                Repository = githubRepo,
                FileName = fileName
            };

            DynamicallyRegisteredRepos[pluginName] = repoConfig;
            Log.Debug($"[Integration] Plugin '{pluginName}' has successfully registered for automatic updates from {githubUser}/{githubRepo}.");
        }
        
        /// <summary>
        /// The main entry point for the update process. Loads configurations and checks all configured plugins for updates.
        /// </summary>
        public static async Task CheckForUpdates()
        {
            LoadGitHubConfig();

            var combinedRepositories = GetCombinedRepositories();
            if (combinedRepositories == null || !combinedRepositories.Any())
            {
                Log.Info("No plugins configured for update, either from repositories.json or dynamic registration.");
                return;
            }
            
            var fileRepositories = GetRepositoriesFromFile();

            Log.Info($"Starting update check for {combinedRepositories.Count} plugins...");
            
            var updateTasks = combinedRepositories.Select(ProcessPluginUpdate);
            var results = await Task.WhenAll(updateTasks);
            
            LogSummary(results.ToList(), fileRepositories);
        }
        
        /// <summary>
        /// Gets a combined list of repositories from both the configuration file and dynamic registrations.
        /// File configurations take precedence over dynamic ones.
        /// </summary>
        /// <returns>A dictionary of all repository configurations to be checked.</returns>
        public static Dictionary<string, RepositoryConfig> GetCombinedRepositories()
        {
            var fileRepositories = GetRepositoriesFromFile();

            // Start with dynamically registered repos, using a case-insensitive comparer
            var combinedRepositories = new Dictionary<string, RepositoryConfig>(DynamicallyRegisteredRepos, StringComparer.OrdinalIgnoreCase);

            // Overwrite with file-based configs, as they have priority
            foreach (var repo in fileRepositories)
            {
                combinedRepositories[repo.Key] = repo.Value;
            }

            return combinedRepositories;
        }
        
        /// <summary>
        /// Loads the repositories configuration from the JSON file.
        /// </summary>
        /// <returns>A dictionary of repository configurations from the file.</returns>
        public static Dictionary<string, RepositoryConfig> GetRepositoriesFromFile()
        {
            return LoadConfig(Plugin.Instance.Config.RepositoriesConfigPath, CreateDefaultRepositoriesConfig);
        }
        
        /// <summary>
        /// Exposes the list of plugins that have registered themselves dynamically.
        /// </summary>
        /// <returns>A read-only dictionary of dynamically registered repositories.</returns>
        public static IReadOnlyDictionary<string, RepositoryConfig> GetDynamicallyRegisteredRepos()
        {
            return DynamicallyRegisteredRepos;
        }
        #endregion

        #region Core Update Logic

        /// <summary>
        /// Processes the update check for a single plugin.
        /// </summary>
        /// <param name="repoEntry">The configuration entry for the plugin to check.</param>
        /// <returns>An <see cref="UpdateResult"/> detailing the outcome.</returns>
        private static async Task<UpdateResult> ProcessPluginUpdate(KeyValuePair<string, RepositoryConfig> repoEntry)
        {
            string pluginName = repoEntry.Key;
            RepositoryConfig repoConfig = repoEntry.Value;
            var result = new UpdateResult { PluginName = pluginName };

            try
            {
                if (string.IsNullOrEmpty(repoConfig.User) || string.IsNullOrEmpty(repoConfig.Repository))
                {
                    result.Status = UpdateStatus.ConfigError;
                    result.ErrorMessage = "The 'user' or 'repository' field is missing in the configuration.";
                    return result;
                }

                IPlugin<IConfig> targetPlugin = Exiled.Loader.Loader.GetPlugin(pluginName);
                if (targetPlugin == null)
                {
                    result.Status = UpdateStatus.PluginNotFound;
                    return result;
                }
                
                result.OldVersion = targetPlugin.Version;
                
                var latestRelease = await GetLatestRelease(repoConfig.User, repoConfig.Repository);
                if (latestRelease == null)
                {
                    result.Status = UpdateStatus.ApiError;
                    result.ErrorMessage = "Could not fetch latest release data from GitHub.";
                    return result;
                }

                string versionTag = latestRelease.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(versionTag, out Version latestVersion))
                {
                    result.Status = UpdateStatus.ApiError;
                    result.ErrorMessage = $"Could not parse version from tag '{latestRelease.TagName}'.";
                    return result;
                }

                result.NewVersion = latestVersion;
                
                if (latestVersion > result.OldVersion)
                {
                    var dllAsset = FindPluginAsset(latestRelease, repoConfig.FileName);
                    if (dllAsset == null)
                    {
                        result.Status = UpdateStatus.NoDllFound;
                        result.ErrorMessage = string.IsNullOrEmpty(repoConfig.FileName)
                            ? "No .dll file found in the latest release."
                            : $"DLL with specific name '{repoConfig.FileName}' not found.";
                        return result;
                    }

                    if (await DownloadAndOverwriteFile(targetPlugin, dllAsset))
                    {
                        result.Status = UpdateStatus.Updated;
                    }
                    else
                    {
                        result.Status = UpdateStatus.WriteError;
                        result.ErrorMessage = "Failed to write the new DLL file (check file permissions or if it's locked).";
                    }
                }
                else
                {
                    result.Status = UpdateStatus.UpToDate;
                }
            }
            catch (Exception ex)
            {
                result.Status = UpdateStatus.ApiError;
                result.ErrorMessage = ex.Message;
                Log.Debug($"An exception occurred while checking {pluginName}: {ex}");
            }

            return result;
        }
        /// <summary>
        /// Downloads a plugin DLL from a GitHub asset and overwrites the existing file.
        /// </summary>
        /// <param name="plugin">The plugin to be updated.</param>
        /// <param name="asset">The GitHub asset containing the new DLL.</param>
        /// <returns>True if the download and write were successful, otherwise false.</returns>
        private static async Task<bool> DownloadAndOverwriteFile(IPlugin<IConfig> plugin, GitHubAsset asset)
        {
            try
            {
                string pluginsDirectory = Exiled.API.Features.Paths.Plugins;
                string dllFileName = $"{plugin.Assembly.GetName().Name}.dll"; 
                string finalPluginPath = Path.Combine(pluginsDirectory, dllFileName);
                byte[] fileBytes;
                using (var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl))
                {
                    if (_githubConfig is { Enabled: true } && !string.IsNullOrEmpty(_githubConfig.Token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("token", _githubConfig.Token);
                    }
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                    var response = await HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    fileBytes = await response.Content.ReadAsByteArrayAsync();
                }
        
                File.WriteAllBytes(finalPluginPath, fileBytes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to download or write plugin file for {plugin.Name}: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region GitHub API & Helpers

        /// <summary>
        /// Fetches the latest release information for a given repository.
        /// </summary>
        /// <param name="user">The GitHub username or organization.</param>
        /// <param name="repo">The repository name.</param>
        /// <returns>A <see cref="GitHubRelease"/> object or null if an error occurs.</returns>
        private static async Task<GitHubRelease> GetLatestRelease(string user, string repo)
        {
            string apiUrl = $"https://api.github.com/repos/{user}/{repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            if (_githubConfig is { Enabled: true } && !string.IsNullOrEmpty(_githubConfig.Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("token", _githubConfig.Token);
            }

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"GitHub API Error for {user}/{repo}: {response.StatusCode}");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GitHubRelease>(json);
        }
        
        /// <summary>
        /// Finds the correct plugin asset from a release's assets list.
        /// </summary>
        /// <param name="release">The GitHub release object.</param>
        /// <param name="specificFileName">An optional, specific file name to look for.</param>
        /// <returns>The found <see cref="GitHubAsset"/> or null.</returns>
        private static GitHubAsset FindPluginAsset(GitHubRelease release, string specificFileName)
        {
            if (!string.IsNullOrEmpty(specificFileName))
            {
                return release.Assets.FirstOrDefault(a => a.Name.Equals(specificFileName, StringComparison.OrdinalIgnoreCase));
            }

            return release.Assets.FirstOrDefault(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }

        #endregion
        
        #region Configuration Loading
        
        /// <summary>
        /// Creates the default repository configuration when the config file is not found.
        /// </summary>
        /// <returns>A dictionary with example repository configurations.</returns>
        private static Dictionary<string, RepositoryConfig> CreateDefaultRepositoriesConfig()
        {
            Log.Warn("Repositories config not found. Creating an example 'repositories.json'. Please configure it and restart.");
            return new Dictionary<string, RepositoryConfig>
            {
                { "AutoUpdate", new RepositoryConfig { User = "DiabeloDev", Repository = "AutoUpdate", FileName = "AutoUpdate.dll"} },
                { "SCPStats", new RepositoryConfig { User = "PintTheDragon", Repository = "SCPStats" } },
                { "ExamplePluginWithSpecificFile", new RepositoryConfig { User = "YourUser", Repository = "YourRepo", FileName = "ExamplePlugin-Exiled.dll" } }
            };
        }
        /// <summary>
        /// Loads the GitHub personal access token configuration.
        /// </summary>
        private static void LoadGitHubConfig()
        {
            _githubConfig = LoadConfig(Plugin.Instance.Config.GitHubConfigPath, () =>
            {
                Log.Info("GitHub config not found. Creating a default 'github.json'. Please configure it with a PAT to increase the API rate limit.");
                return new GitHubConfig
                {
                    Enabled = false,
                    Token = "Your-GitHub-PAT-Here"
                };
            });

            if (_githubConfig?.Enabled == true && !string.IsNullOrEmpty(_githubConfig.Token))
                Log.Debug("GitHub PAT token loaded and enabled.");
            else
                Log.Debug("GitHub PAT token is disabled or not provided. Using unauthenticated requests.");
        }
        /// <summary>
        /// A generic helper to load a JSON configuration file.
        /// </summary>
        /// <typeparam name="T">The type of the configuration object.</typeparam>
        /// <param name="path">The full path to the configuration file.</param>
        /// <param name="getDefault">A function that returns a default configuration object if the file doesn't exist.</param>
        /// <returns>The loaded or default configuration object.</returns>
        private static T LoadConfig<T>(string path, Func<T> getDefault) where T : class
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                T defaultConfig = getDefault();
                File.WriteAllText(path, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
                return defaultConfig;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load or parse {Path.GetFileName(path)}: {ex.Message}. Using default values.");
                return getDefault();
            }
        }
        #endregion

        #region Logging & Summary

        /// <summary>
        /// Logs a formatted summary of all update check results to the console.
        /// </summary>
        private static void LogSummary(List<UpdateResult> results, IReadOnlyDictionary<string, RepositoryConfig> fileRepositories)
        {
            const int innerWidth = 80;

            string topBorder = "╔" + new string('═', innerWidth) + "╗";
            string middleSeparator = "╠" + new string('═', innerWidth) + "╣";
            string bottomBorder = "╚" + new string('═', innerWidth) + "╝";

            Log.Update(string.Empty);
            Log.Update(topBorder);

            string title = "AutoUpdate - Scan Summary";
            int padding = (innerWidth - title.Length) / 2;
            Log.Update($"║{title.PadLeft(title.Length + padding).PadRight(innerWidth)}║");

            Log.Update(middleSeparator);

            if (results.Any())
            {
                foreach (var result in results)
                {
                    string sourceType = fileRepositories.ContainsKey(result.PluginName) ? "[File]" : "[Integration]";
                    
                    string content = result.Status switch
                    {
                        UpdateStatus.Updated => $"[↑] {result.PluginName} {sourceType}: Updated from v{result.OldVersion} to v{result.NewVersion}",
                        UpdateStatus.UpToDate => $"[✓] {result.PluginName} {sourceType}: Is up to date (v{result.OldVersion})",
                        UpdateStatus.PluginNotFound => $"[X] {result.PluginName} {sourceType}: Plugin not found on this server.",
                        UpdateStatus.ApiError or UpdateStatus.NoDllFound or UpdateStatus.WriteError or UpdateStatus.ConfigError 
                            => $"[X] {result.PluginName} {sourceType}: Error - {result.ErrorMessage}",
                        _ => $"[?] {result.PluginName} {sourceType}: Unknown status."
                    };
                    LogWrappedLine(content, innerWidth);
                }
            }
            else
            {
                LogWrappedLine("No plugins were configured for auto-update.", innerWidth);
            }
            
            Log.Update(middleSeparator);

            int updatesFound = results.Count(r => r.Status == UpdateStatus.Updated);
            string summaryContent = updatesFound > 0
                ? $"Found {updatesFound} {GetEnglishUpdateForm(updatesFound)}. A FULL SERVER RESTART is required to apply changes."
                : "No new updates found. Everything is up to date.";
            
            LogWrappedLine(summaryContent, innerWidth);
            
            Log.Update(bottomBorder);
            Log.Update(string.Empty);
            _ = SendDiscordWebhookAsync(results, fileRepositories);
        }
        
        /// <summary>
        /// Asynchronously sends the update summary to a Discord webhook if configured.
        /// </summary>
        private static async Task SendDiscordWebhookAsync(List<UpdateResult> results, IReadOnlyDictionary<string, RepositoryConfig> fileRepositories)
        {
            var config = Plugin.Instance.Config;
            if (!config.DiscordWebhookEnabled || string.IsNullOrEmpty(config.DiscordWebhookUrl))
                return;

            try
            {
                int updatesFound = results.Count(r => r.Status == UpdateStatus.Updated);
                var embed = new Embed
                {
                    Title = "AutoUpdate - Scan Summary",
                    Color = updatesFound > 0 ? 16705372 : 5763719,
                    Description = updatesFound > 0
                        ? $"**Found {updatesFound} {GetEnglishUpdateForm(updatesFound)}.** A server restart is required to apply."
                        : "**All plugins are up to date!**",
                    Footer = new Footer { Text = $"Check completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" }
                };

                foreach (var result in results)
                {
                    string sourceType = fileRepositories.ContainsKey(result.PluginName) ? "File" : "Integration";
                    var field = new Field { Inline = false };

                    switch (result.Status)
                    {
                        case UpdateStatus.Updated:
                            field.Name = $"⬆️ {result.PluginName} `[{sourceType}]`";
                            field.Value = $"Updated from `v{result.OldVersion}` to `v{result.NewVersion}`";
                            break;
                        case UpdateStatus.UpToDate:
                            field.Name = $"✅ {result.PluginName} `[{sourceType}]`";
                            field.Value = $"Is up to date (`v{result.OldVersion}`)";
                            break;
                        default:
                            field.Name = $"❌ {result.PluginName} `[{sourceType}]`";
                            field.Value = $"**Error:** {result.ErrorMessage ?? "Plugin not found on this server."}";
                            break;
                    }
                    embed.Fields.Add(field);
                }
                
                var payload = new DiscordWebhookPayload
                {
                    Username = config.WebhookUsername,
                    Embeds = new List<Embed> { embed }
                };

                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await HttpClient.PostAsync(config.DiscordWebhookUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn($"Failed to send Discord webhook notification. Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"An error occurred while sending the Discord webhook: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper to log a line of text, wrapped within the summary box's width.
        /// </summary>
        private static void LogWrappedLine(string text, int maxWidth)
        {
            const int leftPadding = 1; 
            string paddingString = new string(' ', leftPadding);
            int textMaxWidth = maxWidth - leftPadding - 1;
            var words = text.Split(' ');
            var currentLine = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length > 0 && currentLine.Length + word.Length + 1 > textMaxWidth)
                {
                    Log.Update($"║{paddingString}{currentLine.ToString().PadRight(textMaxWidth)} ║");
                    currentLine.Clear();
                }
                if (currentLine.Length > 0)
                    currentLine.Append(" ");
                currentLine.Append(word);
            }
            
            if (currentLine.Length > 0)
                Log.Update($"║{paddingString}{currentLine.ToString().PadRight(textMaxWidth)} ║");
        }
        
        /// <summary>
        /// Returns the correct plural form of "update".
        /// </summary>
        private static string GetEnglishUpdateForm(int count) => count == 1 ? "update" : "updates";
        #endregion
    }
}