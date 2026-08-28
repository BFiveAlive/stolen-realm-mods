using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CumulativeStatsMod
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.cumulativestatsmod";
        public const string Name = "Cumulative Stats Mod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// Editors frequently emit several change events for a single save (write, then
        /// truncate/flush), and the file can still be locked when the first one arrives.
        /// Waiting a moment collapses the burst into one reload of a settled file.
        /// </summary>
        private const float ReloadDebounceSeconds = 0.3f;

        /// <summary>
        /// Reloading makes BepInEx write the file back out, which the watcher then reports as a
        /// fresh change and reloads again. Ignoring events for a moment afterwards breaks that
        /// echo without being long enough to swallow a real follow-up edit.
        /// </summary>
        private const float SelfWriteIgnoreSeconds = 0.75f;

        private FileSystemWatcher configWatcher;

        // Set from a thread pool thread by the watcher; only ever read and cleared on the main
        // thread in Update, so the actual reload never happens off-thread.
        private volatile bool reloadRequested;

        private bool reloadScheduled;
        private float reloadDueAt;
        private float ignoreEventsUntil;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);

            // PatchAll throws if a target method cannot be resolved, so logging *after* it makes
            // the log line itself proof that every patch bound successfully.
            new Harmony(Guid).PatchAll(typeof(StatManagerPatches));

            if (ModConfig.HotReloadConfig.Value)
                StartWatchingConfig();

            Log.LogInfo(Name + " " + Version + " loaded.");
        }

        private void Update()
        {
            HandleConfigReload();

            try
            {
                StatTracker.Tick();

                StatManager manager = StatManager.Instance;
                if (manager != null && manager.gameObject.activeSelf)
                {
                    StatsToggle.Ensure(manager);
                    StatsToggle.UpdateState(manager);
                }
            }
            catch (Exception e)
            {
                Log.LogError("Cumulative stats update failed: " + e);
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                StatTracker.Save();
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not flush run totals on quit: " + e.Message);
            }
        }

        /// <summary>
        /// BepInEx reads a plugin's config once at startup and never watches the file, so without
        /// this every tweak costs a restart. The watcher is driven by OS change notifications
        /// rather than polling, so it is idle until the file is actually written.
        /// </summary>
        private void StartWatchingConfig()
        {
            try
            {
                string path = Config.ConfigFilePath;
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                    return;

                configWatcher = new FileSystemWatcher(directory, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };

                configWatcher.Changed += OnConfigFileChanged;
                configWatcher.Created += OnConfigFileChanged;
                configWatcher.Renamed += OnConfigFileChanged;
                configWatcher.EnableRaisingEvents = true;

                Log.LogInfo("Watching config for changes; edits apply without restarting the game.");
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not watch the config file, edits will need a restart: " + e.Message);
                configWatcher = null;
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            reloadRequested = true;
        }

        private void HandleConfigReload()
        {
            if (reloadRequested)
            {
                reloadRequested = false;

                if (Time.realtimeSinceStartup >= ignoreEventsUntil)
                {
                    reloadDueAt = Time.realtimeSinceStartup + ReloadDebounceSeconds;
                    reloadScheduled = true;
                }
            }

            if (!reloadScheduled || Time.realtimeSinceStartup < reloadDueAt)
                return;

            reloadScheduled = false;

            try
            {
                Config.Reload();
                Log.LogInfo("Config reloaded.");
            }
            catch (Exception e)
            {
                // Most likely the editor still had the file open. The next save will retry.
                Log.LogWarning("Config reload failed, retrying on next change: " + e.Message);
            }
            finally
            {
                ignoreEventsUntil = Time.realtimeSinceStartup + SelfWriteIgnoreSeconds;
            }
        }

        private void OnDestroy()
        {
            if (configWatcher == null)
                return;

            configWatcher.EnableRaisingEvents = false;
            configWatcher.Changed -= OnConfigFileChanged;
            configWatcher.Created -= OnConfigFileChanged;
            configWatcher.Renamed -= OnConfigFileChanged;
            configWatcher.Dispose();
            configWatcher = null;
        }
    }
}
