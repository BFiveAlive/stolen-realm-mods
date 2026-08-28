using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace SkillWeightMod
{
    /// <summary>
    /// One config entry per broad and narrow category, bound at startup from whatever
    /// skill-categories.json contains. Adding a category to tools/taxonomy.py and regenerating
    /// is enough to make it tunable - nothing here needs editing.
    ///
    /// Sections are named so they sort together in the .cfg file: [Broad Weights] and
    /// [Narrow Weights].
    /// </summary>
    internal static class CategoryWeights
    {
        private static readonly Dictionary<string, ConfigEntry<float>> Broad =
            new Dictionary<string, ConfigEntry<float>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ConfigEntry<float>> Narrow =
            new Dictionary<string, ConfigEntry<float>>(StringComparer.Ordinal);

        public static void Bind(ConfigFile cfg)
        {
            Broad.Clear();
            Narrow.Clear();

            foreach (string name in CategoryData.BroadCategories)
            {
                float baseline = CategoryData.BaselineWeight(name);
                Broad[name] = cfg.Bind("Broad Weights", Sanitise(name), baseline,
                    $"Relative pull toward '{name}'. Multiplied by BroadAffinity. " +
                    $"Default {baseline} is 379 / the {CategoryData.SizeOf(name)} skills that " +
                    "carry it, so every category starts with equal pull per unit of rarity. " +
                    "0 ignores this category.");
            }

            foreach (string name in CategoryData.NarrowCategories)
            {
                float baseline = CategoryData.BaselineWeight(name);
                Narrow[name] = cfg.Bind("Narrow Weights", Sanitise(name), baseline,
                    $"Relative pull toward '{name}'. Multiplied by NarrowAffinity. " +
                    $"Default {baseline} is 379 / the {CategoryData.SizeOf(name)} skills that " +
                    "carry it, so every category starts with equal pull per unit of rarity. " +
                    "0 ignores this category.");
            }
        }

        public static float BroadWeight(string name)
        {
            return Broad.TryGetValue(name, out ConfigEntry<float> e)
                ? e.Value : CategoryData.BaselineWeight(name);
        }

        public static float NarrowWeight(string name)
        {
            return Narrow.TryGetValue(name, out ConfigEntry<float> e)
                ? e.Value : CategoryData.BaselineWeight(name);
        }

        /// <summary>
        /// BepInEx keys cannot contain = \n \t or the quoting characters. Category names are
        /// plain words plus the odd '&' and '-', but this keeps a future name from corrupting
        /// the file.
        /// </summary>
        private static string Sanitise(string name)
        {
            return name.Replace('=', '-').Replace('\n', ' ').Replace('\t', ' ')
                       .Replace('[', '(').Replace(']', ')').Trim();
        }
    }
}
