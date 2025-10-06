using System;
using System.Collections.Generic;
using Exiled.API.Features;
using MEC;

namespace AutoUpdate
{
    public class Plugin : Plugin<Config>
    {
        public override string Author { get; } = ".Diabelo";
        public override string Name { get; } = "AutoUpdate";
        public override Version Version => new Version(1, 3, 3, 0);
        public override Version RequiredExiledVersion { get; } = new Version(9, 9, 1);
        public static Plugin Instance { get; private set; }
        private CoroutineHandle _updateCoroutine;
        public override void OnEnabled()
        {
            Instance = this;
            _ = AnalyticsHandler.HandleAnalytics();
            if (Config.RunUpdaterAtStart)
            {
                _ = Updater.CheckForUpdates();
            }

            RegisterCoroutine();
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            Timing.KillCoroutines(_updateCoroutine);
            Instance = null;
            base.OnDisabled();
        }

        private void RegisterCoroutine()
        {
            if (!Config.ScheduleEnabled)
            {
                Extensions.Log.Debug("Scheduled update checks are disabled.");
                return;
            }

            if (Config.CheckIntervalHours < 1)
            {
                Extensions.Log.Warn("CheckIntervalHours is set to less than 1 hour. Disabling schedule to prevent API spam.");
                return;
            }
            
            Extensions.Log.Info($"Update checks are scheduled to run every {Config.CheckIntervalHours} hours.");
            _updateCoroutine = Timing.RunCoroutine(UpdateCoroutine());
        }

        private IEnumerator<float> UpdateCoroutine()
        {
            yield return Timing.WaitForSeconds(10f);

            while (true)
            {
                yield return Timing.WaitForSeconds(Config.CheckIntervalHours * 3600f);

                Extensions.Log.Info("Starting scheduled update check...");
                _ = Updater.CheckForUpdates();
            }
        }
    }
}