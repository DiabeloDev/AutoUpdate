using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Exiled.API.Features;
using Newtonsoft.Json;

namespace AutoUpdate
{
    /// <summary>
    /// Handles the collection and submission of anonymous analytics data.
    /// </summary>
    public static class AnalyticsHandler
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const string ApiBaseUrl = "https://autoupdate.diabelo.xyz/api";
        private static string TokenPath => Path.Combine(Paths.Configs, "AutoUpdate", "token_server.txt");
        private static string _cachedToken;

        /// <summary>
        /// Main entry point for the analytics process. It orchestrates the consent check, server registration, data preparation, and submission.
        /// </summary>
        public static async Task HandleAnalytics()
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            var consentLevel = Plugin.Instance.Config.AnalyticsConsentLevel;

            if (consentLevel == AnalyticsConsent.None)
            {
                Extensions.Log.Error("Analytics are disabled. To support the developer, please consider enabling them in the AutoUpdate config (level 1, 2, or 3). This helps in understanding usage and improving the plugin.");
                return;
            }

            string token = await GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Extensions.Log.Warn("Could not register this server with the analytics service. Data will not be sent.");
                return;
            }

            object payload = PreparePayload(consentLevel, token);
            if (payload != null)
            {
                await SendData(payload);
            }
        }

        /// <summary>
        /// Retrieves the server's unique token. It checks a local cache and file storage first.
        /// If no token is found, it registers the server with the API to obtain a new one.
        /// </summary>
        /// <returns>A unique server token as a string, or null if registration fails.</returns>
        private static async Task<string> GetToken()
        {
            if (!string.IsNullOrEmpty(_cachedToken)) return _cachedToken;
            try
            {
                if (File.Exists(TokenPath))
                {
                    _cachedToken = File.ReadAllText(TokenPath).Trim();
                    if (!string.IsNullOrEmpty(_cachedToken)) return _cachedToken;
                }

                var response = await HttpClient.PostAsync($"{ApiBaseUrl}/register", null);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonResponse);
                    if (responseData != null && responseData.TryGetValue("token", out string newToken))
                    {
                        string dirPath = Path.GetDirectoryName(TokenPath);
                        if (dirPath != null && !Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                        File.WriteAllText(TokenPath, newToken);
                        _cachedToken = newToken;
                        Extensions.Log.Debug("Successfully registered with the analytics service.");
                        return newToken;
                    }
                }
                Extensions.Log.Warn($"Failed to register with analytics service. Status: {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                Extensions.Log.Error($"An error occurred while registering for analytics: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Prepares the data payload for submission based on the user's consent level.
        /// </summary>
        /// <param name="level">The configured <see cref="AnalyticsConsent"/> level, determining what data to include.</param>
        /// <param name="token">The unique server token to include in the payload.</param>
        /// <returns>An object containing the data to be serialized into JSON.</returns>
        private static object PreparePayload(AnalyticsConsent level, string token)
        {
            var payload = new Dictionary<string, object>
            {
                { "token", token },
                { "pluginVersion", Plugin.Instance.Version.ToString() }
            };

            switch (level)
            {
                case AnalyticsConsent.Anonymous:
                    break;
                case AnalyticsConsent.PluginsOnly:
                case AnalyticsConsent.Full:
                    var pluginsData = new Dictionary<string, string>();
                    try
                    {
                        foreach (var plugin in Exiled.Loader.Loader.Plugins)
                        {
                            if (plugin?.Name != null && plugin.Version != null && !pluginsData.ContainsKey(plugin.Name))
                            {
                                pluginsData.Add(plugin.Name, plugin.Version.ToString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Extensions.Log.Error($"An error occurred while collecting plugin list for analytics: {ex.Message}");
                    }
                    payload["plugins"] = pluginsData;
                    
                    if (level == AnalyticsConsent.Full)
                    {
                        payload["serverIp"] = Server.IpAddress;
                        payload["serverPort"] = Server.Port;
                    }
                    break;
                default:
                    return null;
            }
            return payload;
        }

        /// <summary>
        /// Serializes the provided payload and sends it to the analytics API endpoint.
        /// </summary>
        /// <param name="payload">The data payload to be sent.</param>
        private static async Task SendData(object payload)
        {
            try
            {
                var settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
                string jsonPayload = JsonConvert.SerializeObject(payload, settings);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await HttpClient.PostAsync($"{ApiBaseUrl}/submit_data", content);

                if (response.IsSuccessStatusCode)
                    Extensions.Log.Debug("Analytics data sent successfully.");
                else
                    Extensions.Log.Warn($"Failed to send analytics data. Status: {response.StatusCode}. Response: {await response.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex)
            {
                Extensions.Log.Error($"An error occurred while sending analytics: {ex.Message}");
            }
        }
    }
}