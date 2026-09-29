using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The editor's working copy: what the panel reads and writes, what gets saved back to disk,
    /// and what gets pushed onto the live presets.
    ///
    /// Edits apply without a restart. A preset is a ScriptableObject the game re-reads every time
    /// it builds a character, so writing new values onto the object that is already in the game's
    /// list is enough - the next character created from that class picks them up. It does not
    /// rewrite characters already made, which keeps an edit from silently changing someone's
    /// existing roster.
    ///
    /// Saving writes only your side of it. See <see cref="ClassSources"/> for why the shipped
    /// classes and your changes are two files.
    /// </summary>
    internal static class ClassStore
    {
        private static ClassLibraryFile library;
        private static ClassLibraryFile shipped;
        private static HashSet<string> userOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string lastError;

        internal static string LastError => lastError;
        internal static bool Dirty { get; private set; }

        internal static IList<ClassDefinition> Classes =>
            (IList<ClassDefinition>)(library?.Classes) ?? new List<ClassDefinition>();

        internal static ClassDefaults Defaults => library?.Defaults;

        /// <summary>Where your edits are kept, for showing in the editor.</summary>
        internal static string Path => ClassSources.UserPath;

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

            shipped = ClassSources.ReadDefaults(out string defaultsProblem);
            ClassLibraryFile user = ClassSources.ReadUser(out string userProblem);

            library = ClassSources.Merge(shipped, user, out userOwned);

            if (library.Classes.Count == 0 && defaultsProblem == null)
                defaultsProblem = "No classes.default.json beside the plugin.";

            lastError = defaultsProblem ?? userProblem;
        }

        internal static void Touch() => Dirty = true;

        /// <summary>Appends a class to the working copy. It reaches the game on the next Save.</summary>
        internal static void Add(ClassDefinition definition)
        {
            EnsureLoaded();

            if (library.Classes == null)
                library.Classes = new List<ClassDefinition>();

            library.Classes.Add(definition);
            Dirty = true;
        }

        /// <summary>The class as it ships, or null for one that does not ship at all.</summary>
        internal static ClassDefinition Shipped(string id)
        {
            if (shipped?.Classes == null || string.IsNullOrEmpty(id))
                return null;

            return shipped.Classes.FirstOrDefault(
                x => x != null && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsShipped(string id) => Shipped(id) != null;

        /// <summary>Whether a class currently differs from the way it shipped.</summary>
        internal static bool IsEdited(ClassDefinition definition)
        {
            if (definition == null)
                return false;

            ClassDefinition original = Shipped(definition.Id);
            return original != null && ClassSources.Differs(definition, original);
        }

        /// <summary>
        /// Puts a shipped class back the way it came. The values are copied onto the object the
        /// editor is already holding rather than swapped for a new one, so nothing else that has a
        /// reference to it - the panel's selection, most obviously - ends up pointing at the old
        /// copy. It then matches the shipped version, so the next save leaves it out of your file.
        /// </summary>
        internal static bool ResetToShipped(ClassDefinition definition)
        {
            ClassDefinition original = Shipped(definition?.Id);
            if (original == null)
                return false;

            ClassDefinition copy = ClassSources.Clone(original);

            definition.Name = copy.Name;
            definition.Description = copy.Description;
            definition.Tier = copy.Tier;
            definition.Unlock = copy.Unlock;
            definition.Stats = copy.Stats;
            definition.Skills = copy.Skills;
            definition.Equipment = copy.Equipment;
            definition.ExtraItems = copy.ExtraItems;
            definition.GearNote = copy.GearNote;
            definition.AppearanceFrom = copy.AppearanceFrom;
            definition.Gender = copy.Gender;
            definition.Appearance = copy.Appearance;

            Dirty = true;
            return true;
        }

        /// <summary>
        /// Removes a class. One of your own is simply dropped; a shipped one is listed under
        /// <c>hidden</c> instead, because the file it lives in belongs to the mod and comes back
        /// with every update.
        /// </summary>
        internal static void Remove(ClassDefinition definition)
        {
            if (definition == null)
                return;

            library.Classes.Remove(definition);

            if (IsShipped(definition.Id))
            {
                library.Hidden = library.Hidden ?? new List<string>();
                if (!library.Hidden.Contains(definition.Id))
                    library.Hidden.Add(definition.Id);
            }

            Dirty = true;
        }

        // -------------------------------------------------------------------- saving

        /// <summary>Writes your side of the library - edited classes, your own, and what you hid.</summary>
        internal static bool Save()
        {
            lastError = null;

            var mine = new ClassLibraryFile
            {
                Schema = 1,
                Defaults = library.Defaults,
                Hidden = library.Hidden,
                Classes = library.Classes.Where(Mine).ToList(),
            };

            if (!ClassSources.Write(ClassSources.UserPath, mine, out string problem))
            {
                lastError = problem;
                Plugin.Log.LogError(lastError);
                return false;
            }

            // Recomputed rather than left alone: a class reset to shipped stops being yours, and a
            // class edited for the first time starts being yours.
            userOwned = new HashSet<string>(mine.Classes.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

            Dirty = false;
            return true;
        }

        /// <summary>
        /// Whether a class belongs in your file: one you made, or a shipped one you have changed.
        /// A shipped class you never touched is left out, so it keeps following the shipped version
        /// instead of freezing at whatever it was the day you installed the mod.
        /// </summary>
        private static bool Mine(ClassDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
                return false;

            ClassDefinition original = Shipped(definition.Id);
            return original == null || ClassSources.Differs(definition, original);
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
