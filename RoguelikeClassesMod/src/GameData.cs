using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Turns the names written in classes.json into the game's own assets.
    ///
    /// Skills and items are matched on the names a player sees, because that is what someone
    /// writing a class will type. Both tables contain genuine duplicates - Beast Master I exists
    /// in Ranger and in Nature - so a name that matches more than one asset is reported rather
    /// than silently resolved to whichever came first.
    /// </summary>
    internal static class GameData
    {
        private static Dictionary<string, List<SkillInfo>> skills;
        private static Dictionary<string, List<ItemInfo>> items;

        internal static bool Ready =>
            Burst2Flame.Game.Instance != null &&
            Burst2Flame.Game.Instance.Skills != null &&
            Burst2Flame.Game.Instance.Skills.Count > 0;

        internal static void Reset()
        {
            skills = null;
            items = null;
        }

        /// <summary>
        /// Null when the name matches nothing or is ambiguous; <paramref name="why"/> says which.
        /// A handful of skill names really do exist twice - Beast Master I is in both Ranger and
        /// Nature - so those can be written "Nature:Beast Master I" to say which is meant.
        /// </summary>
        internal static SkillInfo FindSkill(string name, out string why)
        {
            skills = skills ?? Index(Burst2Flame.Game.Instance.Skills, x => x.SkillName);

            string tree = null;
            if (!string.IsNullOrEmpty(name) && name.IndexOf(':') > 0)
            {
                int at = name.IndexOf(':');
                tree = name.Substring(0, at).Trim();
                name = name.Substring(at + 1).Trim();
            }

            if (tree == null)
                return Single(skills, name, "skill", out why);

            why = null;
            if (!skills.TryGetValue(name, out List<SkillInfo> found))
            {
                why = $"no skill called '{name}'";
                return null;
            }

            SkillInfo match = found.FirstOrDefault(
                x => string.Equals(x.SkillType.ToString(), tree, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                why = $"'{name}' exists, but not in the {tree} tree";

            return match;
        }

        internal static ItemInfo FindItem(string name, out string why)
        {
            items = items ?? Index(Burst2Flame.Game.Instance.Items, x => x.ItemName);
            return Single(items, name, "item", out why);
        }

        /// <summary>
        /// A shipped roguelike preset to copy an appearance from. Never returns one of ours: the
        /// injected presets are appended to the same array, and borrowing a look from a class that
        /// borrowed it in turn would make the donor setting meaningless.
        /// </summary>
        internal static CharacterPresetFile FindDonor(string name)
        {
            CharacterPresetFile[] all = Burst2Flame.Game.Instance.CharacterPresetFiles;
            if (all == null || all.Length == 0)
                return null;

            IEnumerable<CharacterPresetFile> shipped = all.Where(x => x != null && !PresetInjection.IsOurs(x));

            if (!string.IsNullOrEmpty(name))
            {
                CharacterPresetFile named = shipped.FirstOrDefault(
                    x => string.Equals(x.PresetName, name, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));

                if (named != null)
                    return named;

                Plugin.Log.LogWarning($"No preset called '{name}' to copy an appearance from; using the first roguelike preset instead.");
            }

            return shipped.FirstOrDefault(x => x.PresetType == GameMode.Roguelike) ?? shipped.FirstOrDefault();
        }

        private static Dictionary<string, List<T>> Index<T>(IEnumerable<T> source, Func<T, string> name)
        {
            var map = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
                return map;

            foreach (T entry in source)
            {
                if (entry == null)
                    continue;

                string key = name(entry);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!map.TryGetValue(key, out List<T> bucket))
                    map[key] = bucket = new List<T>();

                bucket.Add(entry);
            }

            return map;
        }

        private static T Single<T>(Dictionary<string, List<T>> map, string name, string kind, out string why)
            where T : class
        {
            why = null;

            if (string.IsNullOrEmpty(name))
                return null;

            if (!map.TryGetValue(name, out List<T> found) || found.Count == 0)
            {
                why = $"no {kind} called '{name}'";
                return null;
            }

            if (found.Count > 1)
            {
                why = $"'{name}' matches {found.Count} {kind}s; rename it in the game data or pick another";
                return null;
            }

            return found[0];
        }
    }
}
