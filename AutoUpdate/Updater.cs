using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AutoUpdate.Extensions;
using Newtonsoft.Json;
using AutoUpdate.Models;
using Exiled.API.Interfaces;

namespace AutoUpdate
{
    public static class Updater
    {
        private static Log Log = new Log();
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

        private static readonly HttpClient HttpClient = new HttpClient();

        static Updater()
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "AutoUpdate-Plugin-for-EXILED");
        }

        public static async Task CheckForUpdates()
        {
            var configPath = Plugin.Instance.Config.RepositoriesConfigPath;
            if (!File.Exists(configPath))
            {
                Log.Warn($"Configuration file not found: {configPath}. Creating an example file.");
                var exampleRepos = new Dictionary<string, RepositoryConfig>
                {
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

            Log.Info($"[AutoUpdate] Starting update check for {repositories.Count} plugins...");

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
                    HttpResponseMessage response = await HttpClient.GetAsync(apiUrl);

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
                string dllFileName = $"{plugin.Name}.dll"; 
                string finalPluginPath = Path.Combine(pluginsDirectory, dllFileName);
                
                byte[] fileBytes = await HttpClient.GetByteArrayAsync(asset.DownloadUrl);
                File.WriteAllBytes(finalPluginPath, fileBytes);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        private static void LogSummary(List<UpdateResult> results)
        {
            Log.Update("");
            Log.Update("╔══════════════════════════════════════════════════════════════════════════════╗");
            string title = "AutoUpdate - Scan Summary";
            int padding = (78 - title.Length) / 2;
            Log.Update($"║{title.PadLeft(title.Length + padding).PadRight(78)}║");
            Log.Update("╠══════════════════════════════════════════════════════════════════════════════╣");

            foreach (var result in results)
            {
                string content = "";
                switch (result.Status)
                {
                    case UpdateStatus.Updated:
                        content = $" [↑] {result.PluginName}: Updated from v{result.OldVersion} to v{result.NewVersion}";
                        break;
                    case UpdateStatus.UpToDate:
                        content = $" [✓] {result.PluginName}: Is up to date (v{result.OldVersion})";
                        break;
                    case UpdateStatus.PluginNotFound:
                        content = $" [X] {result.PluginName}: Plugin not found on this server.";
                        break;
                    case UpdateStatus.ApiError:
                    case UpdateStatus.NoDllFound:
                    case UpdateStatus.WriteError:
                    case UpdateStatus.ConfigError:
                        content = $" [X] {result.PluginName}: Error - {result.ErrorMessage}";
                        break;
                }
                Log.Update($"║{content.PadRight(78)}║");
            }

            Log.Update("╠══════════════════════════════════════════════════════════════════════════════╣");
            int updatesFound = results.Count(r => r.Status == UpdateStatus.Updated);
            if (updatesFound > 0)
            {
                string updateWord = GetEnglishUpdateForm(updatesFound);
                string summaryContent = $" Found {updatesFound} {updateWord}. A FULL SERVER RESTART is required.";
                Log.Update($"║{summaryContent.PadRight(78)}║");
            }
            else
            {
                Log.Update($"║ No new updates found. Everything is up to date.                              ║");
            }
            Log.Update("╚══════════════════════════════════════════════════════════════════════════════╝");
            Log.Update("");
        }
        private static string GetEnglishUpdateForm(int count)
        {
            return count == 1 ? "update" : "updates";
        }
    }
}