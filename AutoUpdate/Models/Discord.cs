using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoUpdate.Models
{
    /// <summary>
    /// Represents the top-level payload sent to a Discord webhook.
    /// </summary>
    public class DiscordWebhookPayload
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("embeds")]
        public List<Embed> Embeds { get; set; } = new List<Embed>();
    }

    /// <summary>
    /// Represents a Discord embed object.
    /// </summary>
    public class Embed
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("color")]
        public int Color { get; set; }

        [JsonProperty("fields")]
        public List<Field> Fields { get; set; } = new List<Field>();
        
        [JsonProperty("footer")]
        public Footer Footer { get; set; }
    }

    /// <summary>
    /// Represents a field within a Discord embed.
    /// </summary>
    public class Field
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("inline")]
        public bool Inline { get; set; }
    }
    
    /// <summary>
    /// Represents the footer of a Discord embed.
    /// </summary>
    public class Footer
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("icon_url")]
        public string IconUrl { get; set; }
    }
}