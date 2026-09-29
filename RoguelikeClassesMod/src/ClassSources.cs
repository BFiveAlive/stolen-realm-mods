using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Where classes come from, and where your own edits go.
    ///
    /// These were one file, and that was a mistake: <c>classes.json</c> was both the mod's shipped
    /// content and the only place your edits lived, so every update overwrote them silently. The
    /// two want opposite handling, so they are now two files:
    ///
    /// - <b>classes.default.json</b>, beside the plugin. The twenty shipped classes. Replaced by
    ///   every update, never written to.
    /// - <b>classes.user.json</b>, under BepInEx/config. Your edits and your own classes. Nothing
    ///   in a release names this path, so an update cannot touch it.
    ///
    /// They are merged at load: a user entry replaces the shipped one with the same id, new ids are
    /// added, and ids listed under <c>hidden</c> are dropped. Nothing is copied into the user file
    /// until you change it, so a class you never touched keeps following the shipped version and
    /// picks up any later balance change rather than freezing at whatever it was the day you
    /// installed it.
    /// </summary>
    internal static class ClassSources
    {
        internal static string PluginFolder =>
            Path.GetDirectoryName(typeof(ClassSources).Assembly.Location) ?? ".";

        /// <summary>The shipped classes. Overwritten by updates.</summary>
        internal static string DefaultsPath => Path.Combine(PluginFolder, "classes.default.json");

        /// <summary>
        /// What the single-file version wrote. Read once, to rescue edits made before the split,
        /// then set aside - see <see cref="Migrate"/>.
        /// </summary>
        internal static string LegacyPath => Path.Combine(PluginFolder, "classes.json");

        internal static string UserFolder => Path.Combine(Paths.ConfigPath, "RoguelikeClassesMod");

        internal static string UserPath => Path.Combine(UserFolder, "classes.user.json");

        /// <summary>Where exports land and imports are looked for.</summary>
        internal static string SharedFolder => Path.Combine(UserFolder, "shared");

        // ------------------------------------------------------------------ reading

        internal static ClassLibraryFile ReadDefaults(out string problem)
        {
            return Read(DefaultsPath, out problem) ?? new ClassLibraryFile();
        }

        internal static ClassLibraryFile ReadUser(out string problem)
        {
            Migrate();
            return Read(UserPath, out problem) ?? new ClassLibraryFile();
        }

        internal static ClassLibraryFile Read(string path, out string problem)
        {
            problem = null;

            if (!File.Exists(path))
                return null;

            try
            {
                var library = JsonConvert.DeserializeObject<ClassLibraryFile>(
                    File.ReadAllText(path, Encoding.UTF8));

                if (library != null && library.Schema > 1)
                    problem = Path.GetFileName(path) + " declares schema " + library.Schema +
                              "; this build understands 1. Reading it anyway.";

                return library;
            }
            catch (Exception e)
            {
                problem = "Could not read " + Path.GetFileName(path) + ": " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// Rescues edits made while classes.json was the only file.
        ///
        /// Only what actually differs from the shipped classes is carried across. Copying the file
        /// wholesale would be simpler, but it would mark all twenty as yours and freeze them at
        /// whatever this version shipped - so someone who had never edited anything would quietly
        /// stop receiving changes to classes they never touched.
        ///
        /// Done once. The old file is renamed rather than deleted, both so this does not run again
        /// and so nothing is destroyed if the comparison was wrong.
        /// </summary>
        private static void Migrate()
        {
            if (File.Exists(UserPath) || !File.Exists(LegacyPath))
                return;

            try
            {
                ClassLibraryFile legacy = Read(LegacyPath, out _);
                ClassLibraryFile defaults = Read(DefaultsPath, out _) ?? new ClassLibraryFile();

                List<ClassDefinition> edited = (legacy?.Classes ?? new List<ClassDefinition>())
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Id))
                    .Where(x => Differs(x, defaults.Classes?.FirstOrDefault(
                        d => d != null && string.Equals(d.Id, x.Id, StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                if (edited.Count > 0)
                {
                    var mine = new ClassLibraryFile { Schema = 1, Defaults = legacy.Defaults, Classes = edited };

                    if (!Write(UserPath, mine, out string problem))
                    {
                        Plugin.Log.LogError(problem + " Your classes.json was left where it is.");
                        return;
                    }

                    Plugin.Log.LogInfo("Moved " + edited.Count + " edited class(es) out of classes.json into "
                                       + UserPath + ", where updates cannot overwrite them.");
                }
                else
                {
                    Plugin.Log.LogInfo("classes.json matched the shipped classes, so there was nothing to carry over.");
                }

                File.Move(LegacyPath, LegacyPath + ".migrated");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not migrate classes.json: " + e.Message);
            }
        }

        // ------------------------------------------------------------------- merging

        /// <summary>
        /// The list the game is given: the shipped classes with your changes laid over them.
        /// <paramref name="userOwned"/> comes back holding the ids your file supplied, which is what
        /// lets the editor offer to put one back the way it shipped.
        /// </summary>
        internal static ClassLibraryFile Merge(ClassLibraryFile defaults, ClassLibraryFile user,
            out HashSet<string> userOwned)
        {
            userOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var merged = new ClassLibraryFile
            {
                Schema = 1,
                Defaults = user?.Defaults ?? defaults?.Defaults,
                Classes = new List<ClassDefinition>(),
            };

            if (defaults?.Classes != null)
                merged.Classes.AddRange(defaults.Classes);

            if (user?.Classes != null)
            {
                foreach (ClassDefinition entry in user.Classes)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id))
                        continue;

                    userOwned.Add(entry.Id);

                    int at = merged.Classes.FindIndex(
                        x => x != null && string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

                    if (at >= 0)
                        merged.Classes[at] = entry;
                    else
                        merged.Classes.Add(entry);
                }
            }

            if (user?.Hidden != null && user.Hidden.Count > 0)
            {
                var hidden = new HashSet<string>(user.Hidden, StringComparer.OrdinalIgnoreCase);
                merged.Classes.RemoveAll(x => x == null || hidden.Contains(x.Id));
                merged.Hidden = new List<string>(user.Hidden);
            }

            return merged;
        }

        // ------------------------------------------------------------------ writing

        internal static bool Write(string path, ClassLibraryFile library, out string problem)
        {
            problem = null;

            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                string json = JsonConvert.SerializeObject(library, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                // No BOM. The same byte cost the in-game updater a working manifest for three
                // releases, and nothing that reads these files wants one either.
                File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
                return true;
            }
            catch (Exception e)
            {
                problem = "Could not write " + Path.GetFileName(path) + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// Whether a class differs from the way it shipped, which is what decides if it belongs in
        /// the user file. Compared as serialised JSON rather than field by field: the comparison
        /// then cannot fall behind the model when a field is added to it.
        /// </summary>
        internal static bool Differs(ClassDefinition mine, ClassDefinition shipped)
        {
            if (shipped == null)
                return true;

            return !string.Equals(Canonical(mine), Canonical(shipped), StringComparison.Ordinal);
        }

        private static string Canonical(ClassDefinition definition)
        {
            return JsonConvert.SerializeObject(definition, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        internal static ClassDefinition Clone(ClassDefinition definition)
        {
            return JsonConvert.DeserializeObject<ClassDefinition>(Canonical(definition));
        }
    }
}
