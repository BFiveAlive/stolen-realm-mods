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
                float value = DefaultFor(name);
                Broad[name] = cfg.Bind("Broad Weights", Sanitise(name), value,
                    Describe(name, value, "BroadAffinity"));
            }

            foreach (string name in CategoryData.NarrowCategories)
            {
                float value = DefaultFor(name);
                Narrow[name] = cfg.Bind("Narrow Weights", Sanitise(name), value,
                    Describe(name, value, "NarrowAffinity"));
            }
        }

        /// <summary>
        /// Hand-tuned starting weights, replacing the rarity-derived baseline as the default.
        ///
        /// The baseline (379 / the number of skills carrying a category) gives every category
        /// equal pull per unit of rarity. That is the right neutral starting point but not a
        /// playable one: it makes a category with a handful of skills in it enormously
        /// attractive. These are the values arrived at by playing with them.
        ///
        /// A category missing from this table still falls back to its baseline, so adding one to
        /// tools/taxonomy.py and regenerating remains enough to make it tunable.
        /// </summary>
        private static readonly Dictionary<string, float> TunedDefaults =
            new Dictionary<string, float>(StringComparer.Ordinal)
        {
            // Broad
            { "Damage", 0f },
            { "Healing", 5f },
            { "Defense", 1f },
            { "Control", 1f },
            { "Passive", 3f },
            { "Ranged", 3f },
            { "Melee", 3f },
            { "Costs Action Point", 1f },
            { "Free Action", 2f },
            { "Physical", 6f },
            { "Elemental", 9f },
            { "Buff", 4f },
            { "Ally Target", 3f },
            { "Debuff", 3f },
            { "Triggered", 1f },

            // Narrow
            { "Weapon Attack", 1f },
            { "Self-Harm", 6f },
            { "Stacking Self Buff", 3f },
            { "Poison", 6f },
            { "Bleed", 6f },
            { "Heat", 6f },
            { "Cold", 6f },
            { "Lightning", 6f },
            { "Shadow", 6f },
            { "Holy", 6f },
            { "Stealth", 6f },
            { "Crit", 6f },
            { "Dodge", 6f },
            { "Armor", 2f },
            { "Resistance", 2f },
            { "Immunity", 2f },
            { "Damage Reduction", 2f },
            { "Lifesteal & Regen", 6f },
            { "Direct Heal", 3f },
            { "Character Stats", 4f },
            { "Movement Speed", 2f },
            { "Teleport", 4f },
            { "Knockback", 1f },
            { "Elemental AOE", 6f },
            { "Physical AOE", 6f },
            { "Modifies Weapon", 6f },
            { "Auras", 6f },
            { "Summon", 9f },
            { "Shapeshift", 9f },
            { "Stun", 3f },
            { "Immobilize", 3f },
            { "Blind & Silence", 3f },
            { "Retaliation", 6f },
            { "Enrage", 3f },
            { "Marks", 3f },
            { "Damage Amp", 5f },
            { "Cooldown", 3f },
            { "Mana Economy", 4f },
            { "Action Economy", 4f },
        };

        private static float DefaultFor(string name)
        {
            return TunedDefaults.TryGetValue(name, out float tuned)
                ? tuned : CategoryData.BaselineWeight(name);
        }

        /// <summary>
        /// The baseline is worth stating even where it is not the default: it is the number that
        /// says how rare a category is, which is what a weight has to be judged against.
        /// </summary>
        private static string Describe(string name, float chosen, string affinity)
        {
            float baseline = CategoryData.BaselineWeight(name);
            int size = CategoryData.SizeOf(name);

            string origin = TunedDefaults.ContainsKey(name)
                ? $"Default {chosen} is hand-tuned; the rarity-derived baseline would be "
                  + $"{baseline} (379 / the {size} skills that carry it)."
                : $"Default {baseline} is 379 / the {size} skills that carry it, so every "
                  + "category starts with equal pull per unit of rarity.";

            return $"Relative pull toward '{name}'. Multiplied by {affinity}. " + origin
                 + " 0 ignores this category.";
        }


        public static float BroadWeight(string name)
        {
            return Broad.TryGetValue(name, out ConfigEntry<float> e)
                ? e.Value : DefaultFor(name);
        }

        public static float NarrowWeight(string name)
        {
            return Narrow.TryGetValue(name, out ConfigEntry<float> e)
                ? e.Value : DefaultFor(name);
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
