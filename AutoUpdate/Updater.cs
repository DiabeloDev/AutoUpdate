using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AutoUpdate.Extensions;
using Newtonsoft.Json;
using AutoUpdate.Models;
using Exiled.API.Interfaces;

namespace AutoUpdate
{
    public static class Updater
    {
        private class UpdateResult
        {
            public string PluginName { get; set; }
            public UpdateStatus Status { get; set; }
            public Version OldVersion { get; set; }
            public Version NewVersion { get; set; }
            public string ErrorMessage { get; set; }
        }
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
        private static GitHubConfig _githubConfig;
        private static readonly HttpClient HttpClient = new HttpClient();
        static Updater()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "AutoUpdate-Plugin-for-EXILED");
        }
        private static void LoadGitHubConfig()
        {
            string configDir = Path.GetDirectoryName(Plugin.Instance.Config.GitHubConfigPath);
            string githubConfigPath = Path.Combine(configDir, "github.json");
            string dir = Path.GetDirectoryName(githubConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            if (!File.Exists(githubConfigPath))
            {
                var defaultConfig = new GitHubConfig
                {
                    Enabled = false,
                    Token = "Your-GitHub-PAT-Here"
                };
                
                File.WriteAllText(githubConfigPath, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
                Log.Info("To increase the API request limit, enable and configure your token in github.json.");
                _githubConfig = defaultConfig;
                return;
            }

            try
            {
                _githubConfig = JsonConvert.DeserializeObject<GitHubConfig>(File.ReadAllText(githubConfigPath));
                if (_githubConfig != null && _githubConfig.Enabled && !string.IsNullOrEmpty(_githubConfig.Token))
                {
                    Log.Debug("GitHub PAT token loaded and enabled.");
                }
                else
                {
                    Log.Debug("GitHub PAT token is disabled or not provided. Using unauthenticated requests.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load or parse github.json: {ex.Message}. Using unauthenticated requests.");
                _githubConfig = new GitHubConfig { Enabled = false };
            }
        }

        public static async Task CheckForUpdates()
        {
            LoadGitHubConfig();
            var configPath = Plugin.Instance.Config.RepositoriesConfigPath;
            if (!File.Exists(configPath))
            {
                Log.Warn($"Configuration file not found: {configPath}. Creating an example file.");
                var exampleRepos = new Dictionary<string, RepositoryConfig>
                {
                    {
                        "AutoUpdate", new RepositoryConfig
                        {
                            User = "DiabeloDev",
                            Repository = "AutoUpdate"
                        }
                    },
                    {
                        "SCPStats", new RepositoryConfig 
                        { 
                            User = "PintTheDragon", 
                            Repository = "SCPStats" 
                        } 
                    },
                    { 
                        "ExamplePluginWithSpecificFile", new RepositoryConfig 
                        { 
                            User = "YourUser", 
                            Repository = "YourRepo", 
                            FileName = "ExamplePlugin-Exiled.dll"
                        } 
                    }
                };
                
                string dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(configPath, JsonConvert.SerializeObject(exampleRepos, Formatting.Indented));
                Log.Info("Please configure the repositories.json file and run the command again.");
                return;
            }
            
            var repositories = JsonConvert.DeserializeObject<Dictionary<string, RepositoryConfig>>(File.ReadAllText(configPath));
            if (repositories == null || repositories.Count == 0)
            {
                Log.Info("The repositories configuration file is empty. Nothing to check.");
                return;
            }

            Log.Info($"Starting update check for {repositories.Count} plugins...");

            var results = new List<UpdateResult>();

            foreach (var repoEntry in repositories)
            {
                string pluginName = repoEntry.Key;
                RepositoryConfig repoConfig = repoEntry.Value;
                UpdateResult result = new UpdateResult { PluginName = pluginName };
                try
                {
                    if (string.IsNullOrEmpty(repoConfig.User) || string.IsNullOrEmpty(repoConfig.Repository))
                    {
                        result.Status = UpdateStatus.ConfigError;
                        result.ErrorMessage = "The 'user' or 'repository' field is missing in the configuration.";
                        results.Add(result);
                        continue;
                    }

                    IPlugin<IConfig> targetPlugin = Exiled.Loader.Loader.GetPlugin(pluginName);
                    if (targetPlugin == null)
                    {
                        result.Status = UpdateStatus.PluginNotFound;
                        results.Add(result);
                        continue;
                    }

                    result.OldVersion = targetPlugin.Version;
                    string repoSlug = $"{repoConfig.User}/{repoConfig.Repository}";
                    string apiUrl = $"https://api.github.com/repos/{repoSlug}/releases/latest";
                    HttpResponseMessage response;
                    using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                    {
                        if (_githubConfig is { Enabled: true } && !string.IsNullOrEmpty(_githubConfig.Token))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("token", _githubConfig.Token);
                        }
                        response = await HttpClient.SendAsync(request);
                    }
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Status = UpdateStatus.ApiError;
                        result.ErrorMessage = $"GitHub API Error (Status: {response.StatusCode})";
                        results.Add(result);
                        continue;
                    }
                    
                    string json = await response.Content.ReadAsStringAsync();
                    var latestRelease = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    
                    string versionTag = latestRelease.TagName.TrimStart('v', 'V');
                    if (!Version.TryParse(versionTag, out Version latestVersion))
                    {
                        result.Status = UpdateStatus.ApiError;
                        result.ErrorMessage = $"Could not parse version tag '{latestRelease.TagName}'";
                        results.Add(result);
                        continue;
                    }
                    
                    result.NewVersion = latestVersion;

                    if (latestVersion > result.OldVersion)
                    {
                        GitHubAsset dllAsset = null;
                        if (!string.IsNullOrEmpty(repoConfig.FileName))
                        {
                            dllAsset = latestRelease.Assets.FirstOrDefault(a => a.Name.Equals(repoConfig.FileName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            dllAsset = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                        }
                        
                        if (dllAsset == null)
                        {
                            result.Status = UpdateStatus.NoDllFound;
                            if (!string.IsNullOrEmpty(repoConfig.FileName))
                                result.ErrorMessage = $"DLL with specific name '{repoConfig.FileName}' not found in the latest release.";
                            else
                                result.ErrorMessage = "No .dll file found in the latest release.";
                            
                            results.Add(result);
                            continue;
                        }

                        if (await DownloadAndOverwriteFile(targetPlugin, dllAsset))
                        {
                            result.Status = UpdateStatus.Updated;
                        }
                        else
                        {
                            result.Status = UpdateStatus.WriteError;
                            result.ErrorMessage = "Failed to write the new DLL file (is it locked by the server?).";
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
                }
                results.Add(result);
            }
            
            LogSummary(results);
        }
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
        private static void LogSummary(List<UpdateResult> results)
        {
            const int innerWidth = 80;

            string topBorder = "╔" + new string('═', innerWidth) + "╗";
            string middleSeparator = "╠" + new string('═', innerWidth) + "╣";
            string bottomBorder = "╚" + new string('═', innerWidth) + "╝";

            Log.Update("");
            Log.Update(topBorder);

            string title = "AutoUpdate - Scan Summary";
            int padding = (innerWidth - title.Length) / 2;
            Log.Update($"║{title.PadLeft(title.Length + padding).PadRight(innerWidth)}║");

            Log.Update(middleSeparator);

            if (results.Any())
            {
                foreach (var result in results)
                {
                    string content = "";
                    switch (result.Status)
                    {
                        case UpdateStatus.Updated:
                            content = $"[↑] {result.PluginName}: Updated from v{result.OldVersion} to v{result.NewVersion}";
                            break;
                        case UpdateStatus.UpToDate:
                            content = $"[✓] {result.PluginName}: Is up to date (v{result.OldVersion})";
                            break;
                        case UpdateStatus.PluginNotFound:
                            content = $"[X] {result.PluginName}: Plugin not found on this server.";
                            break;
                        case UpdateStatus.ApiError:
                        case UpdateStatus.NoDllFound:
                        case UpdateStatus.WriteError:
                        case UpdateStatus.ConfigError:
                            content = $"[X] {result.PluginName}: Error - {result.ErrorMessage}";
                            break;
                    }
                    LogWrappedLine(content, innerWidth);
                }
            }
            else
            {
                LogWrappedLine("No plugins configured for auto-update.", innerWidth);
            }
            
            Log.Update(middleSeparator);

            int updatesFound = results.Count(r => r.Status == UpdateStatus.Updated);
            string summaryContent;
            if (updatesFound > 0)
            {
                string updateWord = GetEnglishUpdateForm(updatesFound);
                summaryContent = $"Found {updatesFound} {updateWord}. A FULL SERVER RESTART is required.";
            }
            else
            {
                summaryContent = "No new updates found. Everything is up to date.";
            }
            
            LogWrappedLine(summaryContent, innerWidth);
            
            Log.Update(bottomBorder);
            Log.Update("");
        }
        private static void LogWrappedLine(string text, int maxWidth)
        {
            const int leftPadding = 1; 
            string paddingString = new string(' ', leftPadding);
            int textMaxWidth = maxWidth - leftPadding;
            var words = text.Split(' ');
            var currentLine = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length > 0 && currentLine.Length + word.Length + 1 > textMaxWidth)
                {
                    Log.Update($"║{paddingString}{currentLine.ToString().PadRight(textMaxWidth)}║");
                    currentLine.Clear();
                }
                if (currentLine.Length > 0)
                {
                    currentLine.Append(" ");
                }
                currentLine.Append(word);
            }
            
            if (currentLine.Length > 0)
            {
                Log.Update($"║{paddingString}{currentLine.ToString().PadRight(textMaxWidth)}║");
            }
        }
        private static string GetEnglishUpdateForm(int count)
        {
            return count == 1 ? "update" : "updates";
        }
    }
}