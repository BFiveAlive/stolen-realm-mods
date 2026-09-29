using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The editor's working copy of classes.json: what the panel reads and writes, what gets saved
    /// back to disk, and what gets pushed onto the live presets.
    ///
    /// Edits apply without a restart. A preset is a ScriptableObject the game re-reads every time
    /// it builds a character, so writing new values onto the object that is already in the game's
    /// list is enough - the next character created from that class picks them up. It does not
    /// rewrite characters already made, which keeps an edit from silently changing someone's
    /// existing roster.
    /// </summary>
    internal static class ClassStore
    {
        private const string FileName = "classes.json";

        private static ClassLibraryFile library;
        private static string lastError;

        internal static string LastError => lastError;
        internal static bool Dirty { get; private set; }

        internal static IList<ClassDefinition> Classes =>
            (IList<ClassDefinition>)(library?.Classes) ?? new List<ClassDefinition>();

        internal static ClassDefaults Defaults => library?.Defaults;

        internal static string Path =>
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(ClassStore).Assembly.Location) ?? ".", FileName);

        internal static void EnsureLoaded()
        {
            if (library != null)
                return;

            Reload();
        }

        internal static void Reload()
        {
            lastError = null;
            Dirty = false;

            try
            {
                if (!File.Exists(Path))
                {
                    library = new ClassLibraryFile();
                    lastError = "No classes.json beside the plugin.";
                    return;
                }

                library = JsonConvert.DeserializeObject<ClassLibraryFile>(File.ReadAllText(Path, Encoding.UTF8))
                          ?? new ClassLibraryFile();
            }
            catch (Exception e)
            {
                library = new ClassLibraryFile();
                lastError = "Could not read classes.json: " + e.Message;
            }
        }

        internal static void Touch() => Dirty = true;

        /// <summary>Writes the working copy back to disk, formatted the way it ships.</summary>
        internal static bool Save()
        {
            lastError = null;

            try
            {
                string json = JsonConvert.SerializeObject(library, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                // No BOM. The same byte cost the in-game updater a working manifest for three
                // releases, and nothing that reads this file wants one either.
                File.WriteAllText(Path, json + Environment.NewLine, new UTF8Encoding(false));
                Dirty = false;
                return true;
            }
            catch (Exception e)
            {
                lastError = "Could not write classes.json: " + e.Message;
                Plugin.Log.LogError(lastError);
                return false;
            }
        }

        /// <summary>
        /// Pushes one class's current values onto the preset the game is already holding.
        /// Returns what could not be resolved, so the panel can say so rather than failing quietly.
        /// </summary>
        internal static List<string> Apply(ClassDefinition definition)
        {
            var problems = new List<string>();

            CharacterPresetFile preset = PresetInjection.Find(definition.Id);
            if (preset == null)
            {
                problems.Add("this class is not in the game's list yet; it is added the next time the character picker opens");
                return problems;
            }

            try
            {
                PresetFactory.Populate(preset, definition, Defaults, problems);
            }
            catch (Exception e)
            {
                problems.Add(e.Message);
                Plugin.Log.LogError("Applying " + definition.Id + " failed: " + e);
            }

            return problems;
        }
    }
}
