using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Applies edits to config entries and decides when they reach disk.
    ///
    /// The in-memory value is set immediately, so a mod that reads <c>Entry.Value</c> at the point
    /// of use - the pattern modding-notes recommends - reacts on the very next frame with no file
    /// involved at all. The write is what gets delayed: <c>SaveOnConfigSet</c> would otherwise
    /// write the whole file on every assignment, and dragging a slider assigns every frame. That
    /// is both a disk write per frame and, for any mod running a config file watcher, a reload
    /// per frame.
    /// </summary>
    internal static class ConfigWriter
    {
        // Files with an edit not yet written, and when they became dirty.
        private static readonly Dictionary<ConfigFile, float> Pending = new Dictionary<ConfigFile, float>();

        // Files whose SaveOnConfigSet we turned off, so it can be put back exactly as found.
        private static readonly Dictionary<ConfigFile, bool> SuppressedSaveOnSet = new Dictionary<ConfigFile, bool>();

        /// <summary>Entries changed since the manager was opened, for the restart-needed banner.</summary>
        public static readonly HashSet<string> ChangedRequiringRestart = new HashSet<string>();

        public static void Set(SettingRow row, object value)
        {
            if (row == null || row.Entry == null)
                return;

            var file = row.Entry.ConfigFile;

            try
            {
                Suppress(file);
                row.Entry.BoxedValue = value;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not set " + row.Id + ": " + e.Message);
                return;
            }

            Pending[file] = Time.realtimeSinceStartup;

            if (row.RequiresRestart)
                ChangedRequiringRestart.Add(row.Id);
        }

        public static void Reset(SettingRow row)
        {
            if (row != null && row.Entry != null)
                Set(row, row.Entry.DefaultValue);
        }

        /// <summary>
        /// Writes any file whose last edit has settled. Called once a frame from the plugin.
        /// </summary>
        public static void Tick()
        {
            if (Pending.Count == 0)
                return;

            float debounce = Mathf.Max(0.05f, ModConfig.SaveDebounceSeconds.Value);
            float now = Time.realtimeSinceStartup;

            List<ConfigFile> due = null;

            foreach (var pair in Pending)
            {
                if (now - pair.Value < debounce)
                    continue;

                due ??= new List<ConfigFile>();
                due.Add(pair.Key);
            }

            if (due == null)
                return;

            foreach (var file in due)
            {
                Pending.Remove(file);

                try
                {
                    file.Save();
                }
                catch (Exception e)
                {
                    // Almost always the file being held open by an editor. The value is already
                    // live in memory, so this costs persistence rather than the edit itself.
                    Plugin.Log.LogWarning("Could not save " + file.ConfigFilePath + ": " + e.Message);
                }
                finally
                {
                    Restore(file);
                }
            }
        }

        /// <summary>
        /// Writes everything outstanding now, ignoring the debounce. Used when the window closes
        /// and on quit, so a pending edit is never lost to a fast exit.
        /// </summary>
        public static void FlushAll()
        {
            if (Pending.Count == 0)
                return;

            var files = new List<ConfigFile>(Pending.Keys);
            Pending.Clear();

            foreach (var file in files)
            {
                try
                {
                    file.Save();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not save " + file.ConfigFilePath + ": " + e.Message);
                }
                finally
                {
                    Restore(file);
                }
            }
        }

        private static void Suppress(ConfigFile file)
        {
            if (SuppressedSaveOnSet.ContainsKey(file))
                return;

            SuppressedSaveOnSet[file] = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
        }

        private static void Restore(ConfigFile file)
        {
            if (!SuppressedSaveOnSet.TryGetValue(file, out bool original))
                return;

            file.SaveOnConfigSet = original;
            SuppressedSaveOnSet.Remove(file);
        }
    }
}
