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
    }
}