using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace StatusEffectsMod
{
    /// <summary>
    /// Config-driven edits to the game's status effect definitions.
    ///
    /// There are no Harmony patches here. Statuses are ScriptableObjects that the game reads
    /// from every time it needs them, so editing those objects in memory is enough - and it is
    /// both simpler and less brittle than intercepting the code paths that read them.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.statuseffectsmod";
        public const string Name = "Status Effects Mod";
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

        private bool applied;

        private void Awake()
        {
            Log = Logger;

            ModConfig.Bind(Config);

            if (ModConfig.HotReloadConfig.Value)
                StartWatchingConfig();

            // The status table is not loaded yet - it comes out of the asset bundles well after
            // plugins start - so the per-status config entries and the first apply both wait
            // for Update.
            Log.LogInfo($"{Name} {Version} loaded. Waiting for the game's status data.");
        }

        private void Update()
        {
            if (!applied)
                TryFirstApply();

            StatusDumper.TryDump();

            PumpConfigReload();
        }

        /// <summary>
        /// Binds the per-status config entries and applies the config, as soon as the game's
        /// status table exists. Runs once.
        /// </summary>
        private void TryFirstApply()
        {
            if (!StatusCatalog.TryLoad())
                return;

            // Set before the work, not after. Anything that escapes below would otherwise leave
            // this false and be retried on the very next frame, turning a single bad status into
            // an exception every frame for the rest of the session.
            applied = true;

            try
            {
                StatusOverrides.Bind(Config);

                // Binding writes the file out with the newly discovered keys. That write is the
                // watcher's own echo, so it is suppressed the same way a reload's is.
                ignoreEventsUntil = Time.realtimeSinceStartup + SelfWriteIgnoreSeconds;

                Log.LogInfo($"Found {StatusCatalog.Entries.Count} statuses; " +
                            $"[Status Overrides] in {Path.GetFileName(Config.ConfigFilePath)} now lists them all.");
            }
            catch (Exception e)
            {
                Log.LogError($"Could not build the [Status Overrides] section: {e}");
                return;
            }

            Apply("startup");
        }

        private void Apply(string reason)
        {
            if (!StatusCatalog.Loaded)
                return;

            try
            {
                if (!ModConfig.Enabled.Value)
                {
                    RestoreAll();
                    Log.LogInfo($"Disabled ({reason}); all statuses restored to vanilla.");
                    return;
                }

                StatusApplier.Report report = StatusApplier.ApplyAll();

                if (ModConfig.LogChanges.Value)
                {
                    foreach (string change in report.Changes)
                        Log.LogInfo($"  {change}");
                }

                foreach (string problem in report.Problems)
                    Log.LogWarning($"config: {problem}");

                Log.LogInfo($"Applied ({reason}): {report.FieldsChanged} change(s) across " +
                            $"{report.StatusesChanged} status(es)" +
                            (report.Problems.Count > 0 ? $", {report.Problems.Count} problem(s)" : "") +
                            (ModConfig.LogChanges.Value || report.FieldsChanged == 0
                                ? "."
                                : ". Set LogChanges=true to see each one."));
            }
            catch (Exception e)
            {
                // Leaving the game on vanilla statuses is a far better failure than leaving it
                // on a half-applied config.
                Log.LogError($"Apply failed, restoring vanilla statuses: {e}");
                RestoreAll();
            }
        }

        private void RestoreAll()
        {
            foreach (StatusEntry entry in StatusCatalog.Entries)
            {
                try
                {
                    entry.Original.RestoreTo(entry.Status);
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Could not restore {entry.ConfigKey}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// BepInEx reads a plugin's config once at startup and never watches the file, so
        /// without this every tweak costs a restart. The watcher is driven by OS change
        /// notifications rather than polling, so it is idle until the file is actually written.
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

                Log.LogInfo("Watching config for changes; edits apply without restarting.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not watch the config file, edits will need a restart: {e.Message}");
                configWatcher = null;
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            reloadRequested = true;
        }

        private void PumpConfigReload()
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
            }
            catch (Exception e)
            {
                // Most likely the editor still had the file open. The next save will retry.
                Log.LogWarning($"Config reload failed, retrying on next change: {e.Message}");
                ignoreEventsUntil = Time.realtimeSinceStartup + SelfWriteIgnoreSeconds;
                return;
            }

            ignoreEventsUntil = Time.realtimeSinceStartup + SelfWriteIgnoreSeconds;

            Apply("config reload");
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
