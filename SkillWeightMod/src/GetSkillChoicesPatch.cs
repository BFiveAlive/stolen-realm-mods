using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace SkillWeightMod
{
    /// <summary>
    /// Replaces RoguelikeManager.GetSkillChoices with a synergy-weighted version.
    ///
    /// The eligibility pool, the per-option tier roll and the per-tier maximums are all
    /// reproduced exactly as vanilla computes them. The only behavioural change is the final
    /// pick within a tier: vanilla orders the tier's candidates by Random.value and takes the
    /// first (a uniform draw), while this picks by weight from <see cref="SkillWeighting"/>.
    ///
    /// Any unexpected state makes the prefix return true, which runs the untouched original.
    /// A broken mod therefore degrades to vanilla rolls rather than to a crash.
    /// </summary>
    [HarmonyPatch(typeof(RoguelikeManager), nameof(RoguelikeManager.GetSkillChoices))]
    internal static class GetSkillChoicesPatch
    {
        /// <summary>
        /// What the most recent roll computed, for the ShowWeightsInMenu readout. Keyed on the
        /// SkillInfo asset, which is a stable singleton reference.
        /// </summary>
        internal struct RollInfo
        {
            public float Weight;
            public float Share;
            public int PoolSize;
            public string Breakdown;   // pre-rendered for the hover tooltip
        }

        internal static readonly Dictionary<SkillInfo, RollInfo> LastRoll =
            new Dictionary<SkillInfo, RollInfo>();

        [HarmonyPrefix]
        private static bool Prefix(
            RoguelikeManager __instance,
            int numOptions,
            int level,
            List<SkillInfo> alreadyObtained,
            int forcedTier,
            ref List<SkillInfo> __result)
        {
            // Only hand off to vanilla when BOTH mechanics are disabled. Repetition damping
            // is useful on its own with SynergyStrength = 0.
            if (Mathf.Approximately(ModConfig.SynergyStrength.Value, 0f) &&
                Mathf.Approximately(ModConfig.OfferedDecay.Value, 1f))
                return true;

            try
            {
                List<SkillInfo> choices = BuildWeightedChoices(__instance, numOptions, level, alreadyObtained, forcedTier);
                if (choices == null)
                    return true;

                __result = choices;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Weighted roll failed, falling back to vanilla selection: {e}");
                return true;
            }
        }

        /// <summary>Returns null to signal "give up, run vanilla instead".</summary>
        private static List<SkillInfo> BuildWeightedChoices(
            RoguelikeManager manager,
            int numOptions,
            int level,
            List<SkillInfo> alreadyObtained,
            int forcedTier)
        {
            RoguelikeSettings settings = GlobalSettingsManager.instance?.roguelikeManager;
            if (settings == null || Burst2Flame.Game.Instance == null || SteamManager.instance == null)
                return null;

            // Set by OpenSkillSelectWindow immediately before PopulateSkillChoices, so it is
            // the character this roll belongs to. Null history simply disables the damping.
            Dictionary<Guid, int> history = OfferHistory.For(manager.CurrentRoguelikeSkillSelectingCharacter);

            LastRoll.Clear();

            List<SkillInfo> pool = BuildEligiblePool(alreadyObtained);
            var chosenSkills = new List<SkillInfo>();

            for (int i = 0; i < numOptions; i++)
            {
                int tierToRoll = forcedTier == -1
                    ? RollTier(settings, level)
                    : forcedTier;

                // Vanilla walks the tier down until the per-tier maximum allows it. The extra
                // tierToRoll > 1 guard only prevents an unbounded walk; vanilla's own loop has
                // no floor.
                while (tierToRoll > 1 &&
                       !manager.TierLimitRulePassed(tierToRoll, alreadyObtained.Concat(chosenSkills).ToList()))
                {
                    tierToRoll--;
                }

                SkillInfo pick = PickFromTier(pool, chosenSkills, tierToRoll, alreadyObtained, history);

                // Vanilla calls .First() here and throws when a tier is exhausted. Stopping
                // early instead hands back the options we did manage to fill.
                if (pick == null)
                    break;

                chosenSkills.Add(pick);
            }

            // Recorded after the loop: within a single roll, duplicates are already prevented
            // by the !chosenSkills.Contains(x) filter, so the options must not damp each other.
            foreach (SkillInfo offered in chosenSkills)
                OfferHistory.Record(history, offered);

            return chosenSkills;
        }

        /// <summary>Mirrors vanilla's eligibility filter and its exclusion of owned/replaced/disabled skills.</summary>
        private static List<SkillInfo> BuildEligiblePool(List<SkillInfo> alreadyObtained)
        {
            List<SkillInfo> pool = Burst2Flame.Game.Instance.Skills.Where(x =>
                !x.Disabled &&
                x.SkillType != SkillType.Basic &&
                x.SkillType != SkillType.Innate &&
                !x.DontIncludeInTree &&
                Burst2Flame.Game.Instance.FullReleaseModeEnabled(x) &&
                SteamManager.instance.MeetsDLCRequirements(x.SkillType)).ToList();

            var excluded = new List<SkillInfo>();
            foreach (SkillInfo skill in alreadyObtained)
            {
                excluded.Add(skill);

                if (skill.DisablingSkills != null)
                    excluded.AddRange(skill.DisablingSkills);

                excluded.AddRange(pool.Where(x => x.SkillsThatReplace != null && x.SkillsThatReplace.Contains(skill)));
            }

            foreach (SkillInfo skill in excluded)
                pool.Remove(skill);

            return pool;
        }

        /// <summary>Vanilla's tier roll: one 1-100 draw tested against the level-scaled tier thresholds.</summary>
        private static int RollTier(RoguelikeSettings settings, int level)
        {
            float roll = UnityEngine.Random.Range(1f, 100f);

            int tier;
            if (roll <= settings.Tier5ChanceNodes.GetMultipler(level)) tier = 5;
            else if (roll <= settings.Tier4ChanceNodes.GetMultipler(level)) tier = 4;
            else if (roll <= settings.Tier3ChanceNodes.GetMultipler(level)) tier = 3;
            else if (roll <= settings.Tier2ChanceNodes.GetMultipler(level)) tier = 2;
            else tier = 1;

            return Mathf.Min(5, tier);
        }

        /// <summary>
        /// Weighted pick within a tier. Falls back down through lower tiers when a tier has been
        /// exhausted, which is the case vanilla would throw on.
        /// </summary>
        private static SkillInfo PickFromTier(
            List<SkillInfo> pool,
            List<SkillInfo> chosenSkills,
            int tier,
            List<SkillInfo> alreadyObtained,
            Dictionary<Guid, int> history)
        {
            for (int t = tier; t >= 1; t--)
            {
                List<SkillInfo> candidates = pool
                    .Where(x => !chosenSkills.Contains(x) && x.Tier == t)
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                List<float> weights = candidates
                    .Select(c => SkillWeighting.ComputeWeight(c, alreadyObtained, OfferHistory.TimesOffered(history, c)))
                    .ToList();

                if (ModConfig.LogRolls.Value)
                    LogRoll(t, candidates, weights, history);

                SkillInfo pick = SkillWeighting.WeightedPick(candidates, weights);

                if (pick != null && ModConfig.ShowWeightsInMenu.Value)
                {
                    float total = 0f;
                    foreach (float w in weights)
                        total += w;

                    int index = candidates.IndexOf(pick);
                    float weight = index >= 0 ? weights[index] : 0f;

                    LastRoll[pick] = new RollInfo
                    {
                        Weight = weight,
                        Share = total > 0f ? weight / total : 0f,
                        PoolSize = candidates.Count,
                        Breakdown = WeightReadoutPatch.DescribeBreakdown(pick, alreadyObtained, weight)
                    };
                }

                return pick;
            }

            return null;
        }

        private static void LogRoll(int tier, List<SkillInfo> candidates, List<float> weights, Dictionary<Guid, int> history)
        {
            float total = weights.Sum();
            var sb = new StringBuilder();
            sb.AppendLine($"Tier {tier} roll over {candidates.Count} candidates:");

            var ranked = candidates
                .Select((c, i) => new { Skill = c, Weight = weights[i] })
                .OrderByDescending(x => x.Weight)
                .Take(8);

            foreach (var entry in ranked)
            {
                float pct = total > 0f ? entry.Weight / total * 100f : 0f;
                int seen = OfferHistory.TimesOffered(history, entry.Skill);
                string seenNote = seen > 0 ? $" seen={seen}x" : string.Empty;
                sb.AppendLine($"  {entry.Skill.SkillName} [{entry.Skill.SkillType}] w={entry.Weight:F2} p={pct:F1}%{seenNote}");
            }

            Plugin.Log.LogInfo(sb.ToString().TrimEnd());
        }
    }
}
