using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace ModManager
{
    /// <summary>One setting, plus the bits of presentation the UI needs to draw it.</summary>
    internal sealed class SettingRow
    {
        public ConfigEntryBase Entry;
        public string Section;
        public string Key;
        public string Description;

        /// <summary>First sentence of <see cref="Description"/>, for the inline hint on the row.</summary>
        public string Brief;

        public bool RequiresRestart;

        /// <summary>Stable identity for edit buffers, independent of list order.</summary>
        public string Id;

        /// <summary>The plugin this setting belongs to, for the search results' breadcrumb.</summary>
        public PluginSettings Owner;
    }

    internal sealed class PluginSettings
    {
        public string Guid;
        public string Name;
        public string Version;
        public ConfigFile File;
        public List<SettingRow> Rows = new List<SettingRow>();

        /// <summary>Section name to its rows, in the order the plugin bound them.</summary>
        public List<KeyValuePair<string, List<SettingRow>>> Sections =
            new List<KeyValuePair<string, List<SettingRow>>>();
    }

    /// <summary>
    /// Builds the settings list by asking BepInEx what is loaded, rather than by knowing anything
    /// about individual mods. Every plugin that binds settings through its inherited
    /// <c>Config</c> shows up here with no cooperation of its own.
    /// </summary>
    internal static class ConfigDiscovery
    {
        /// <summary>
        /// A mod that builds its own <see cref="ConfigFile"/> instead of using the inherited one
        /// (see modding-notes gotcha 11) is invisible to <c>PluginInfos</c>. Such a plugin can
        /// register the file here from its Awake and have it appear like any other.
        /// </summary>
        private static readonly Dictionary<string, ConfigFile> ExtraFiles =
            new Dictionary<string, ConfigFile>();

        public static void RegisterExtraConfigFile(string pluginGuid, ConfigFile file)
        {
            if (!string.IsNullOrEmpty(pluginGuid) && file != null)
                ExtraFiles[pluginGuid] = file;
        }

        public static List<PluginSettings> Collect()
        {
            var result = new List<PluginSettings>();

            foreach (var pair in Chainloader.PluginInfos)
            {
                PluginSettings settings;

                // One awkward plugin must not cost the whole list, so each is built in isolation.
                try
                {
                    settings = Build(pair.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not read settings for " + pair.Key + ": " + e.Message);
                    continue;
                }

                if (settings != null)
                    result.Add(settings);
            }

            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static PluginSettings Build(BepInEx.PluginInfo info)
        {
            // Instance is null when a plugin threw during construction. It is still listed by
            // BepInEx, but it has no config to show.
            var instance = info?.Instance;
            if (instance == null)
                return null;

            var file = instance.Config;
            if (file == null)
                ExtraFiles.TryGetValue(info.Metadata.GUID, out file);

            if (file == null || file.Count == 0)
                return null;

            var settings = new PluginSettings
            {
                Guid = info.Metadata.GUID,
                Name = info.Metadata.Name,
                Version = info.Metadata.Version != null ? info.Metadata.Version.ToString() : "?",
                File = file
            };

            // Values snapshots the entries under the file's own lock, which matters because a
            // plugin can still be binding settings while we enumerate. It is an explicit
            // interface implementation, hence the cast, and it is what the now-obsolete
            // GetConfigEntries was replaced by.
            var entries = ((IDictionary<ConfigDefinition, ConfigEntryBase>)file).Values;

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                var description = entry.Description != null ? entry.Description.Description : null;

                var row = new SettingRow
                {
                    Entry = entry,
                    Section = entry.Definition.Section,
                    Key = entry.Definition.Key,
                    Description = description ?? string.Empty,
                    Brief = FirstSentence(description),
                    RequiresRestart = DetectRequiresRestart(entry),
                    Id = settings.Guid + "|" + entry.Definition.Section + "|" + entry.Definition.Key,
                    Owner = settings
                };

                settings.Rows.Add(row);
            }

            foreach (var group in settings.Rows.GroupBy(r => r.Section))
                settings.Sections.Add(new KeyValuePair<string, List<SettingRow>>(group.Key, group.ToList()));

            if (ModConfig.VerboseLogging.Value)
            {
                Plugin.Log.LogInfo(string.Format("{0} ({1}): {2} settings in {3} section(s)",
                    settings.Name, settings.Guid, settings.Rows.Count, settings.Sections.Count));
            }

            return settings;
        }

        /// <summary>
        /// The first sentence, which for the descriptions these mods write is reliably the one
        /// that says what the setting is; the rest is qualification and detail that belongs in
        /// the panel rather than on a 44px row.
        /// </summary>
        private static string FirstSentence(string description)
        {
            if (string.IsNullOrEmpty(description))
                return string.Empty;

            int stop = description.IndexOf(". ", StringComparison.Ordinal);
            string first = stop > 0 ? description.Substring(0, stop + 1) : description;

            // Written as codes: a description may legitimately contain either, and the
            // literal escapes are easy to lose when this file is edited by tooling.
            return first.Replace((char)10, ' ').Replace((char)13, ' ').Trim();
        }

        // "restart" as a whole word, so that "needs a restart" and "restart the game" both match
        // without a stray substring elsewhere doing so.
        private static readonly Regex RestartWord =
            new Regex(@"\brestart(s|ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether changing this setting takes effect only after a relaunch.
        ///
        /// BepInEx has no field for this, so there are two sources. A plugin can attach any object
        /// as a config tag with a bool RequiresRestart or IsRestartRequired member - read by
        /// reflection, so no shared assembly is needed and the convention stays compatible with
        /// ConfigurationManager's attribute object. Failing that, the description is scanned for
        /// the word, which is what the mods in this repo already say in prose.
        /// </summary>
        private static bool DetectRequiresRestart(ConfigEntryBase entry)
        {
            var tags = entry.Description != null ? entry.Description.Tags : null;
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (tag == null)
                        continue;

                    if (TryReadBoolMember(tag, "RequiresRestart", out bool a) && a) return true;
                    if (TryReadBoolMember(tag, "IsRestartRequired", out bool b) && b) return true;
                }
            }

            var description = entry.Description != null ? entry.Description.Description : null;
            return !string.IsNullOrEmpty(description) && RestartWord.IsMatch(description);
        }

        private static bool TryReadBoolMember(object target, string name, out bool value)
        {
            value = false;

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            object raw = null;

            var property = type.GetProperty(name, flags);
            if (property != null && property.CanRead)
                raw = property.GetValue(target, null);

            if (raw == null)
            {
                var field = type.GetField(name, flags);
                if (field != null)
                    raw = field.GetValue(target);
            }

            // Nullable<bool> is what ConfigurationManagerAttributes uses; it unboxes to a plain
            // bool here whenever it has a value, and stays null otherwise.
            if (raw is bool b)
            {
                value = b;
                return true;
            }

            return false;
        }
    }
}
