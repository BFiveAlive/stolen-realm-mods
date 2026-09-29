using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Reads classes.json and turns it into presets.
    ///
    /// A class with an unresolvable skill or item is skipped rather than added half-built: a
    /// picker entry that produces a character missing the skill it was chosen for is worse than
    /// no entry at all. Every rejection names what could not be found, because the usual cause is
    /// a typo or a name the game spells differently.
    /// </summary>
    internal static class ClassLibrary
    {
        internal static IEnumerable<CharacterPresetFile> BuildAll()
        {
            var built = new List<CharacterPresetFile>();

            ClassLibraryFile library = Read();
            if (library?.Classes == null || library.Classes.Count == 0)
                return built;

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();

            foreach (ClassDefinition definition in library.Classes)
            {
                if (string.IsNullOrEmpty(definition?.Id))
                {
                    skipped.Add("a class with no id");
                    continue;
                }

                if (!seenIds.Add(definition.Id))
                {
                    skipped.Add($"{definition.Id} (duplicate id)");
                    continue;
                }

                if (definition.Skills == null || definition.Skills.Count == 0)
                {
                    skipped.Add($"{definition.Id} (no skills)");
                    continue;
                }

                CharacterPresetFile preset = PresetFactory.Build(definition, library.Defaults, out List<string> problems);
                if (problems.Count > 0)
                {
                    skipped.Add($"{definition.Id} ({string.Join("; ", problems.ToArray())})");
                    UnityEngine.Object.Destroy(preset);
                    continue;
                }

                built.Add(preset);
            }

            Report(built, skipped);
            return built;
        }

        /// <summary>
        /// The shipped classes with the player's changes laid over them. The same merge the editor
        /// works from, so what the game builds and what the editor shows cannot drift apart.
        /// </summary>
        private static ClassLibraryFile Read()
        {
            ClassLibraryFile defaults = ClassSources.ReadDefaults(out string defaultsProblem);
            ClassLibraryFile user = ClassSources.ReadUser(out string userProblem);

            if (defaultsProblem != null)
                Plugin.Log.LogWarning(defaultsProblem);

            if (userProblem != null)
                Plugin.Log.LogWarning(userProblem);

            if ((defaults.Classes == null || defaults.Classes.Count == 0) && defaultsProblem == null
                && !System.IO.File.Exists(ClassSources.DefaultsPath))
            {
                Plugin.Log.LogWarning("No classes.default.json next to the plugin, so no classes were added. "
                                      + "Expected it at " + ClassSources.DefaultsPath + ".");
            }

            return ClassSources.Merge(defaults, user, out _);
        }

        private static void Report(List<CharacterPresetFile> built, List<string> skipped)
        {
            foreach (CharacterPresetFile preset in built)
            {
                string unlock = preset.NeedsUnlock
                    ? $"locked ({preset.UnlockDescription}, needs {preset.UnlockConditions[0].StatName} >= {preset.UnlockConditions[0].RequiredValue})"
                    : "available from the start";

                Plugin.Log.LogInfo($"  {preset.PresetName}: {preset.StartingSkills.Length} skills, {unlock}");
            }

            foreach (string entry in skipped)
                Plugin.Log.LogWarning($"  skipped {entry}");
        }
    }
}
