using System.ComponentModel;
using Exiled.API.Features;
using Exiled.API.Interfaces;

namespace AutoUpdate
{
    public class Config : IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string RepositoriesConfigPath { get; set; } = $"{Paths.Configs}/AutoUpdate/repositories.json";

        [Description("Run updater at start")] 
        public bool RunUpdaterAtStart { get; set; } = true;
    }
}