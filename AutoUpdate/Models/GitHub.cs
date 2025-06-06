using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoUpdate.Models
{
    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }
        [JsonProperty("assets")]
        public List<GitHubAsset> Assets { get; set; }
    }
    public class GitHubAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("browser_download_url")]
        public string DownloadUrl { get; set; }
    }
}