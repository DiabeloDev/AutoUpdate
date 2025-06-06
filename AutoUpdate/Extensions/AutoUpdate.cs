using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Exiled.API.Features;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace AutoUpdate.Extensions
{
    public static class UpdateChecker
    {
        private static Log Log = new Log();
        private static readonly string RepositoryUrl = "https://api.github.com/repos/DiabeloDev/AutoUpdate/releases/latest";
        private static readonly string PluginPath = Path.Combine(Paths.Plugins, "AutoUpdate.dll");
        private static readonly string CurrentVersion = Plugin.Instance.Version.ToString();
        private static readonly Lazy<HttpClient> LazyClient = new(() =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoUpdate-UpdateChecker");
            return client;
        });
        private static HttpClient Client => LazyClient.Value;
        public static async Task RunAsync()
        {
            Log.Info("Checking for updates...");
            await CheckForUpdatesAsync();
        }
        private static async Task CheckForUpdatesAsync()
        {
            try
            {
                var response = await Client.GetAsync(RepositoryUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = $"Failed to check for updates. Status: {response.StatusCode}";
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.NotFound:
                            errorMessage += " - Repository not found. Please check if the repository URL is correct.";
                            break;
                        case HttpStatusCode.RequestEntityTooLarge:
                            errorMessage += " - Request too large. Please check your connection.";
                            break;
                        case HttpStatusCode.Unauthorized:
                            errorMessage += " - Unauthorized access. Please check your credentials.";
                            break;
                        case HttpStatusCode.Forbidden:
                            errorMessage += " - Access forbidden. Please check your permissions.";
                            break;
                        case HttpStatusCode.ServiceUnavailable:
                            errorMessage += " - GitHub service is temporarily unavailable. Please try again later.";
                            break;
                        default:
                            errorMessage += " - Please check your internet connection and try again.";
                            break;
                    }
                    Log.Error(errorMessage);
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();

                var latestVersion = ExtractLatestVersion(content);
                var downloadUrl = ExtractDownloadUrl(content);

                if (latestVersion == null || downloadUrl == null)
                {
                    Log.Error("Failed to parse update information. Please check if the release format is correct.");
                    return;
                }

                if (IsNewerVersion(CurrentVersion, latestVersion))
                {
                    string[] updateLines =
                    [
                        $"New version available: {latestVersion}",
                        $"Current version: {CurrentVersion}",
                        "Starting update process..."
                    ];
                    LogInBoxWarn(updateLines);

                    await UpdatePluginAsync(downloadUrl);
                    Log.Info("Update completed successfully. Please restart the server to apply changes.");
                }
                else
                {
                    Log.Info("You are using the latest version. No update needed.");
                }
            }
            catch (HttpRequestException ex)
            {
                Log.Error($"Network error while checking for updates: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log.Error($"Inner error: {ex.InnerException.Message}");
                }
            }
            catch (TaskCanceledException)
            {
                Log.Error("Update check was cancelled due to timeout");
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected error while checking for updates: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log.Error($"Inner error: {ex.InnerException.Message}");
                }
            }
        }
        private static string ExtractLatestVersion(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                return obj["tag_name"]?.ToString();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to extract the latest version: {ex.Message}");
                return null;
            }
        }
        private static string ExtractDownloadUrl(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var assets = obj["assets"] as JArray;

                if (assets == null || assets.Count == 0)
                {
                    Log.Error("No assets found in the release");
                    return null;
                }
                
                foreach (var asset in assets)
                {
                    var name = asset["name"]?.ToString();
                    if (name != null && name.Equals("OverwatchSystem.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset["browser_download_url"]?.ToString();
                    }
                }

                Log.Error("No matching 'OverwatchSystem.dll' file found in the release assets.");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to extract download URL: {ex.Message}");
                return null;
            }
        }
        private static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            if (Version.TryParse(currentVersion, out var current) && 
                Version.TryParse(latestVersion, out var latest))
            {
                return latest > current;
            }

            Log.Error("Failed to compare versions. Using current version as the latest.");
            return false;
        }
        private static async Task UpdatePluginAsync(string downloadUrl)
        {
            try
            {
                var pluginData = await Client.GetByteArrayAsync(downloadUrl);
                File.WriteAllBytes(PluginPath, pluginData);
            }
            catch (Exception ex)
            {
                Log.Error($"Error during plugin update: {ex.Message}");
            }
        }
        private static void LogInBoxWarn(string[] lines)
        {
            int maxWidth = lines.Max(line => line.Length);
            string horizontalBorder = $"╔{new string('═', maxWidth + 2)}╗";

            Log.Warn(horizontalBorder);
            foreach (var line in lines)
            {
                Log.Warn($"║ {line.PadRight(maxWidth)} ║");
            }
            Log.Warn($"╚{new string('═', maxWidth + 2)}╝");
        }
    }
}