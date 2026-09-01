using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace SummonerMod
{
    /// <summary>
    /// Tunes how strong summons are.
    ///
    /// It patches nothing. Everything that decides a summon's damage, health, dodge and the limit
    /// on how many you may have is a value in the game's global settings object, so this mod reads
    /// those, remembers them, and multiplies them - which means there is no Harmony patch here to
    /// break when the game updates, only field names to keep up with.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.summonermod";
        public const string Name = "Summoner Mod";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        private FileSystemWatcher configWatcher;
        private bool applied;
        private bool reloadQueued;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);

            Config.SettingChanged += (sender, args) => Reapply("setting changed");

            if (ModConfig.HotReloadConfig.Value)
                StartWatchingConfig();

            Log.LogInfo(Name + " " + Version + " loaded. Waiting for the game's settings.");
        }

        private void Update()
        {
            try
            {
                // The settings object does not exist for the first few frames. Capturing is
                // retried until it does, then the shipped values are held for the session.
                if (!applied)
                {
                    if (!SummonTuning.Capture())
                        return;

                    applied = true;
                    SummonTuning.Apply("startup");
                    return;
                }

                if (reloadQueued)
                {
                    reloadQueued = false;

                    // Reload raises SettingChanged for anything that actually differs, which is
                    // what triggers the reapply; a file saved with no change does nothing.
                    Config.Reload();
                }
            }
            catch (Exception e)
            {
                Log.LogError("Summoner Mod update failed: " + e);
                applied = true; // Do not retry a failing capture every frame.
            }
        }

        private void Reapply(string reason)
        {
            if (!SummonTuning.Ready)
                return;

            try
            {
                SummonTuning.Apply(reason);
            }
            catch (Exception e)
            {
                Log.LogError("Could not apply summon settings: " + e);
            }
        }

        private void StartWatchingConfig()
        {
            try
            {
                string path = Config.ConfigFilePath;

                configWatcher = new FileSystemWatcher(Path.GetDirectoryName(path),
                    Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                // The event arrives on a worker thread, and Unity objects may only be touched on
                // the main one, so it only sets a flag for Update to notice.
                configWatcher.Changed += (s, e) => reloadQueued = true;
                configWatcher.Created += (s, e) => reloadQueued = true;
                configWatcher.Renamed += (s, e) => reloadQueued = true;

                Log.LogInfo("Watching config for changes; edits apply without restarting.");
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not watch the config file; edits will need a restart: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (configWatcher == null)
                return;

            configWatcher.EnableRaisingEvents = false;
            configWatcher.Dispose();
            configWatcher = null;
        }
    }
}
