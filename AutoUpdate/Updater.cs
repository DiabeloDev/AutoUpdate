using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
        /// The main entry point for the update process. Loads configurations and checks all configured plugins for updates.
        /// </summary>
        public static async Task CheckForUpdates()
        {
            LoadGitHubConfig();

            var repositories = GetConfiguredRepositories();
            if (repositories == null || !repositories.Any())
            {
                Log.Info("The repositories configuration file is empty or missing. Nothing to check.");
                return;
            }

            Log.Info($"Starting update check for {repositories.Count} plugins...");
            
            var updateTasks = repositories.Select(ProcessPluginUpdate);
            var results = await Task.WhenAll(updateTasks);

            LogSummary(results.ToList());
        }
        /// <summary>
        /// Loads the repositories configuration
        /// </summary>
        /// <returns>A dictionary of repository configurations.</returns>
        public static Dictionary<string, RepositoryConfig> GetConfiguredRepositories()
        {
            return LoadConfig(Plugin.Instance.Config.RepositoriesConfigPath, CreateDefaultRepositoriesConfig);
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
        private static void LogSummary(List<UpdateResult> results)
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
                    string content = result.Status switch
                    {
                        UpdateStatus.Updated => $"[↑] {result.PluginName}: Updated from v{result.OldVersion} to v{result.NewVersion}",
                        UpdateStatus.UpToDate => $"[✓] {result.PluginName}: Is up to date (v{result.OldVersion})",
                        UpdateStatus.PluginNotFound => $"[X] {result.PluginName}: Plugin not found on this server.",
                        UpdateStatus.ApiError or UpdateStatus.NoDllFound or UpdateStatus.WriteError or UpdateStatus.ConfigError 
                            => $"[X] {result.PluginName}: Error - {result.ErrorMessage}",
                        _ => $"[?] {result.PluginName}: Unknown status."
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