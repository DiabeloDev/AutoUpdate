using Newtonsoft.Json;

namespace AutoUpdate.Models
{
    public class RepositoryConfig
    {
        [JsonProperty("user")]
        public string User { get; set; }
        [JsonProperty("repository")]
        public string Repository { get; set; }
        [JsonProperty("fileName", NullValueHandling = NullValueHandling.Ignore)]
        public string FileName { get; set; }
    }
}