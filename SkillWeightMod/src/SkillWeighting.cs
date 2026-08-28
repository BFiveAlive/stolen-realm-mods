using System.Collections.Generic;
using Burst2Flame;
using UnityEngine;

namespace SkillWeightMod
{
    /// <summary>
    /// Turns "what this character already knows" into a relative roll weight for a candidate
    /// skill. A weight of 1.0 is vanilla-neutral; everything is relative within a single tier.
    ///
    /// Four independent contributions, each with its own master scalar:
    ///
    ///   TreeAffinity      per owned skill in the same tree
    ///   BroadAffinity     per owned skill sharing a broad category, times that category's weight
    ///   NarrowAffinity    per owned skill sharing a narrow category, times that category's weight
    ///   DependencyBonus   flat, when the candidate's prerequisite is already owned
    ///
    /// The per-category weights let a narrow path (Poison) pull harder than a broad one
    /// (Damage) without touching the other three levers.
    /// </summary>
    internal static class SkillWeighting
    {
        public static float ComputeWeight(SkillInfo candidate, List<SkillInfo> owned, int timesOffered)
        {
            if (candidate == null)
                return 1f;

            if (owned == null || owned.Count == 0)
                return ApplyRepetitionDecay(1f, timesOffered);

            int treeMatches = 0;
            bool dependencyOwned = false;

            foreach (SkillInfo own in owned)
            {
                if (own == null)
                    continue;

                if (own.SkillType == candidate.SkillType)
                    treeMatches++;

                if (candidate.Dependency != null && own == candidate.Dependency)
                    dependencyOwned = true;
            }

            float synergy = ModConfig.TreeAffinity.Value * treeMatches;

            synergy += ModConfig.BroadAffinity.Value
                       * CategoryScore(CategoryData.BroadOf(candidate.SkillName), owned, broad: true);

            synergy += ModConfig.NarrowAffinity.Value
                       * CategoryScore(CategoryData.NarrowOf(candidate.SkillName), owned, broad: false);

            if (dependencyOwned)
                synergy += ModConfig.DependencyBonus.Value;

            float weight = 1f + ModConfig.SynergyStrength.Value * synergy;

            // Decay is applied before the clamp so MinWeight stays a genuine floor: a skill
            // offered many times gets rare, never unrollable.
            weight = ApplyRepetitionDecay(weight, timesOffered);

            return Mathf.Clamp(weight, ModConfig.MinWeight.Value, ModConfig.MaxWeight.Value);
        }

        /// <summary>
        /// For each category the candidate carries, count how many owned skills share it and
        /// scale by that category's configured weight. Mirrors how tree affinity counts owned
        /// skills in the same tree.
        /// </summary>
        private static float CategoryScore(string[] categories, List<SkillInfo> owned, bool broad)
        {
            if (categories.Length == 0)
                return 0f;

            float total = 0f;

            foreach (string category in categories)
            {
                float weight = broad
                    ? CategoryWeights.BroadWeight(category)
                    : CategoryWeights.NarrowWeight(category);

                if (Mathf.Approximately(weight, 0f))
                    continue;

                int shared = 0;
                foreach (SkillInfo own in owned)
                {
                    if (own == null)
                        continue;

                    string[] ownCategories = broad
                        ? CategoryData.BroadOf(own.SkillName)
                        : CategoryData.NarrowOf(own.SkillName);

                    foreach (string c in ownCategories)
                    {
                        if (c == category)
                        {
                            shared++;
                            break;
                        }
                    }
                }

                total += weight * shared;
            }

            return total;
        }

        /// <summary>One line of the weight breakdown: where a chunk of synergy came from.</summary>
        internal struct Contribution
        {
            public string Source;   // "Tree", "Broad", "Narrow", "Dependency"
            public string Label;    // the tree or category name
            public int Shared;      // how many owned skills matched
            public float Points;    // synergy contributed, after the affinity scalar
        }

        /// <summary>
        /// The same arithmetic as ComputeWeight, itemised. Used by the in-game readout so the
        /// tooltip reports exactly what the roll did rather than a separate re-derivation.
        /// </summary>
        public static List<Contribution> Explain(SkillInfo candidate, List<SkillInfo> owned)
        {
            var parts = new List<Contribution>();
            if (candidate == null || owned == null || owned.Count == 0)
                return parts;

            int treeMatches = 0;
            bool dependencyOwned = false;
            foreach (SkillInfo own in owned)
            {
                if (own == null)
                    continue;
                if (own.SkillType == candidate.SkillType)
                    treeMatches++;
                if (candidate.Dependency != null && own == candidate.Dependency)
                    dependencyOwned = true;
            }

            if (treeMatches > 0)
            {
                parts.Add(new Contribution
                {
                    Source = "Tree",
                    Label = candidate.SkillType.ToString(),
                    Shared = treeMatches,
                    Points = ModConfig.TreeAffinity.Value * treeMatches
                });
            }

            AddCategoryParts(parts, "Broad", CategoryData.BroadOf(candidate.SkillName), owned,
                             ModConfig.BroadAffinity.Value, broad: true);
            AddCategoryParts(parts, "Narrow", CategoryData.NarrowOf(candidate.SkillName), owned,
                             ModConfig.NarrowAffinity.Value, broad: false);

            if (dependencyOwned)
            {
                parts.Add(new Contribution
                {
                    Source = "Dependency",
                    Label = candidate.Dependency.SkillName,
                    Shared = 1,
                    Points = ModConfig.DependencyBonus.Value
                });
            }

            parts.Sort((a, b) => b.Points.CompareTo(a.Points));
            return parts;
        }

        private static void AddCategoryParts(List<Contribution> parts, string source, string[] categories,
                                             List<SkillInfo> owned, float affinity, bool broad)
        {
            foreach (string category in categories)
            {
                float weight = broad
                    ? CategoryWeights.BroadWeight(category)
                    : CategoryWeights.NarrowWeight(category);

                if (Mathf.Approximately(weight, 0f))
                    continue;

                int shared = 0;
                foreach (SkillInfo own in owned)
                {
                    if (own == null)
                        continue;

                    string[] ownCategories = broad
                        ? CategoryData.BroadOf(own.SkillName)
                        : CategoryData.NarrowOf(own.SkillName);

                    foreach (string c in ownCategories)
                    {
                        if (c == category)
                        {
                            shared++;
                            break;
                        }
                    }
                }

                if (shared == 0)
                    continue;

                parts.Add(new Contribution
                {
                    Source = source,
                    Label = category,
                    Shared = shared,
                    Points = affinity * weight * shared
                });
            }
        }

        /// <summary>Damps a skill the character has already been shown this run.</summary>
        private static float ApplyRepetitionDecay(float weight, int timesOffered)
        {
            float decay = ModConfig.OfferedDecay.Value;
            if (timesOffered <= 0 || Mathf.Approximately(decay, 1f))
                return weight;

            int stacks = Mathf.Min(timesOffered, Mathf.Max(0, ModConfig.MaxOfferedPenaltyStacks.Value));
            return weight * Mathf.Pow(decay, stacks);
        }

        /// <summary>
        /// Roulette-wheel pick over <paramref name="candidates"/> using <paramref name="weights"/>.
        /// Returns null only when the candidate list is empty.
        /// </summary>
        public static SkillInfo WeightedPick(List<SkillInfo> candidates, List<float> weights)
        {
            if (candidates.Count == 0)
                return null;

            float total = 0f;
            foreach (float w in weights)
                total += w;

            // All weights clamped to zero (possible with an aggressive MinWeight of 0)
            // degrades to a uniform pick rather than biasing toward index 0.
            if (total <= 0f)
                return candidates[Random.Range(0, candidates.Count)];

            float roll = Random.value * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                    return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }
    }
}
