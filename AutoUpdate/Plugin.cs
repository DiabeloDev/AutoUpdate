using System;
using Exiled.API.Features;

namespace AutoUpdate
{
    public class Plugin : Plugin<Config>
    {
        public override string Author { get; } = ".Diabelo";
        public override string Name { get; } = "AutoUpdate";
        public override Version Version => new Version(1, 1, 1);
        public override Version RequiredExiledVersion { get; } = new Version(9, 6, 1);
        public static Plugin Instance { get; private set; }
        public override void OnEnabled()
        {
            Instance = this;
            if (Config.RunUpdaterAtStart)
            {
                Updater.CheckForUpdates().ConfigureAwait(false);
            }
            base.OnEnabled();
        }
        public override void OnDisabled()
        {
            Instance = null;
            
            base.OnDisabled();
        }
    }
}