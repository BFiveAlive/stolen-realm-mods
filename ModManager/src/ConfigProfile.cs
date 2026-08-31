using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace ModManager
{
    /// <summary>What an import actually did, so the UI can say more than "done".</summary>
    internal sealed class ImportReport
    {
        public int Applied;
        public int Unchanged;
        public int UnknownMod;
        public int UnknownSetting;
        public int Rejected;
        public string Error;

        public bool Failed => !string.IsNullOrEmpty(Error);

        public override string ToString()
        {
            if (Failed)
                return Error;

            var parts = new List<string> { Applied + " applied" };

            if (Unchanged > 0) parts.Add(Unchanged + " already matched");
            if (UnknownSetting > 0) parts.Add(UnknownSetting + " unknown setting(s) skipped");
            if (UnknownMod > 0) parts.Add(UnknownMod + " from mods you don't have");
            if (Rejected > 0) parts.Add(Rejected + " value(s) refused");

            return string.Join("  ·  ", parts.ToArray()) + ".";
        }
    }

    /// <summary>
    /// Reads and writes shareable snapshots of every mod's settings.
    ///
    /// Values are stored in BepInEx's own serialized form - the exact text its .cfg files hold -
    /// rather than being mapped to types here. That means a profile round-trips any setting the
    /// game's mods can define, including types this assembly has never heard of, and it makes the
    /// file legible and hand-editable, which is the point of something meant to be shared.
    ///
    /// The format is deliberately close to a .cfg so it reads as familiar:
    ///
    ///     [[plugin.guid]]
    ///     [Section]
    ///     Key = value
    /// </summary>
    internal static class ConfigProfile
    {
        public const string Extension = ".srprofile";

        public static string Folder => Path.Combine(Paths.BepInExRootPath, "config-profiles");

        // --- export -------------------------------------------------------------------------

        public static string Export(string name, List<PluginSettings> plugins, bool changedOnly)
        {
            if (plugins == null || plugins.Count == 0)
                throw new InvalidOperationException("There are no settings to export.");

            string path = Path.Combine(Folder, SafeName(name) + Extension);

            var text = new StringBuilder();
            text.AppendLine("# Stolen Realm mod settings profile");
            text.AppendLine("# exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            text.AppendLine("# Values are in BepInEx's own serialized form, so this file can be hand-edited.");

            if (changedOnly)
                text.AppendLine("# Only settings that differ from their default are included.");

            int written = 0;

            foreach (var plugin in plugins)
            {
                var rows = changedOnly ? plugin.Rows.Where(IsModified).ToList() : plugin.Rows;
                if (rows.Count == 0)
                    continue;

                text.AppendLine();
                text.AppendLine("[[" + plugin.Guid + "]]");
                text.AppendLine("# " + plugin.Name + " v" + plugin.Version);

                foreach (var section in rows.GroupBy(r => r.Section))
                {
                    text.AppendLine();
                    text.AppendLine("[" + section.Key + "]");

                    foreach (var row in section)
                    {
                        string value;
                        try
                        {
                            value = row.Entry.GetSerializedValue();
                        }
                        catch
                        {
                            // A setting that cannot serialise is one BepInEx could not have
                            // written to its own .cfg either. Skipping it beats aborting.
                            continue;
                        }

                        text.AppendLine(row.Key + " = " + Escape(value));
                        written++;
                    }
                }
            }

            if (written == 0)
                throw new InvalidOperationException("Nothing to export: every setting is at its default.");

            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));

            return path;
        }

        private static bool IsModified(SettingRow row)
        {
            try
            {
                return !Equals(row.Entry.BoxedValue, row.Entry.DefaultValue);
            }
            catch
            {
                return false;
            }
        }

        // --- import -------------------------------------------------------------------------

        public static ImportReport Import(string path)
        {
            var report = new ImportReport();

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                report.Error = "Could not read the profile: " + e.Message;
                return report;
            }

            // Collected fresh rather than reusing the browser's snapshot: a mod may have bound
            // more settings since the panel was opened.
            var index = BuildIndex(ConfigDiscovery.Collect());

            Dictionary<string, SettingRow> current = null;
            bool sawUnknownMod = false;
            string section = string.Empty;

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                if (line.StartsWith("[[") && line.EndsWith("]]"))
                {
                    string guid = line.Substring(2, line.Length - 4).Trim();
                    sawUnknownMod = !index.TryGetValue(guid, out current);

                    if (sawUnknownMod)
                        current = null;

                    section = string.Empty;
                    continue;
                }

                if (line[0] == '[' && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int split = line.IndexOf('=');
                if (split <= 0)
                    continue;

                if (current == null)
                {
                    if (sawUnknownMod)
                        report.UnknownMod++;

                    continue;
                }

                string key = line.Substring(0, split).Trim();
                string value = Unescape(line.Substring(split + 1).Trim());

                if (!current.TryGetValue(section + "\u0000" + key, out SettingRow row))
                {
                    report.UnknownSetting++;
                    continue;
                }

                Apply(row, value, report);
            }

            if (report.Applied > 0)
                EntryDrawer.ClearTransientState();

            return report;
        }

        private static void Apply(SettingRow row, string value, ImportReport report)
        {
            object converted;

            // Converted here rather than through ConfigEntryBase.SetSerializedValue, which
            // catches its own parse failures, logs a warning and leaves the value alone. That
            // silence is fine for loading a .cfg but wrong for an import that has to report what
            // it did: a junk value would have been counted as applied.
            try
            {
                converted = TomlTypeConverter.ConvertToValue(value, row.Entry.SettingType);
            }
            catch
            {
                report.Rejected++;
                return;
            }

            object before;
            try
            {
                before = row.Entry.BoxedValue;
            }
            catch
            {
                report.Rejected++;
                return;
            }

            if (Equals(before, converted))
            {
                report.Unchanged++;
                return;
            }

            // Via ConfigWriter so the file is marked dirty on the usual debounce and a
            // restart-needed change is recorded. The entry clamps to its acceptable range on the
            // way in, so an out-of-range value in a shared file lands at the nearest legal one.
            ConfigWriter.Set(row, converted);
            report.Applied++;
        }

        private static Dictionary<string, Dictionary<string, SettingRow>> BuildIndex(
            List<PluginSettings> plugins)
        {
            var index = new Dictionary<string, Dictionary<string, SettingRow>>(StringComparer.OrdinalIgnoreCase);

            foreach (var plugin in plugins)
            {
                var rows = new Dictionary<string, SettingRow>(StringComparer.Ordinal);

                foreach (var row in plugin.Rows)
                    rows[row.Section + "\u0000" + row.Key] = row;

                index[plugin.Guid] = rows;
            }

            return index;
        }

        // --- files --------------------------------------------------------------------------

        public static List<string> List()
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return new List<string>();

                return Directory.GetFiles(Folder, "*" + Extension)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not list profiles: " + e.Message);
                return new List<string>();
            }
        }

        public static void Delete(string path)
        {
            // Only ever a file this folder listed, so a crafted name cannot reach outside it.
            if (File.Exists(path) && string.Equals(Path.GetExtension(path), Extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// The profile name comes from a text box, so it decides a path on disk. Anything that is
        /// not a plain name is rejected rather than quietly sanitised into something else.
        /// </summary>
        private static string SafeName(string name)
        {
            name = (name ?? string.Empty).Trim();

            if (name.Length == 0)
                throw new InvalidOperationException("Give the profile a name first.");

            if (name.Length > 64)
                throw new InvalidOperationException("That name is too long.");

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.Contains("..") || name.Contains("/") || name.Contains("\\"))
            {
                throw new InvalidOperationException("A profile name cannot contain / \\ .. or : * ? \" < > |");
            }

            return name;
        }

        // A serialized value is a single line by construction here, so an embedded newline would
        // silently truncate the entry on the way back in.
        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
                return value ?? string.Empty;

            var text = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    text.Append(value[i]);
                    continue;
                }

                char next = value[++i];
                if (next == 'n') text.Append('\n');
                else if (next == 'r') text.Append('\r');
                else if (next == '\\') text.Append('\\');
                else { text.Append('\\'); text.Append(next); }
            }

            return text.ToString();
        }
    }
}
