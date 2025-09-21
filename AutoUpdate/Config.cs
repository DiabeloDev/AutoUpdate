using System.ComponentModel;
using Exiled.API.Features;
using Exiled.API.Interfaces;

namespace AutoUpdate
{
    public class Config : IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
        [Description("Path to repositories config file")]
        public string RepositoriesConfigPath { get; set; } = $"{Paths.Configs}/AutoUpdate/repositories.json";
        [Description("Path to GitHub config file")]
        public string GitHubConfigPath { get; set; } = $"{Paths.Configs}/AutoUpdate/github.json";
        [Description("Run updater at start")] 
        public bool RunUpdaterAtStart { get; set; } = true;
        [Description("--- Schedule Settings ---\nEnable periodic update checks.")]
        public bool ScheduleEnabled { get; set; } = false;
        [Description("How often (in hours) should the updater check for new plugin versions? Minimum: 1")]
        public float CheckIntervalHours { get; set; } = 12.0f;
        [Description("--- Discord Webhook Settings ---\nEnable sending update summaries to a Discord webhook.")]
        public bool DiscordWebhookEnabled { get; set; } = false;
        [Description("The URL of the Discord webhook to send notifications to.")]
        public string DiscordWebhookUrl { get; set; } = "";
        [Description("The username for the webhook bot.")]
        public string WebhookUsername { get; set; } = "AutoUpdate Bot";
    }
}