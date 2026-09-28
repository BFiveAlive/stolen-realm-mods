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
    /// The eligibility pool, the player's blocked skill trees, the per-option tier roll, the
    /// per-tier maximums and the empty-tier fallback are all reproduced as vanilla computes them.
    /// The only behavioural change is the final pick: vanilla orders candidates by Random.value
    /// and takes the first (a uniform draw), while this picks by weight from
    /// <see cref="SkillWeighting"/>.
    ///
    /// Because the prefix returns false, NOTHING in the vanilla body runs. Anything the game adds
    /// to that method later is silently skipped until it is mirrored here - which is exactly how
    /// the skill-tree blocking added in the September 2026 update was first bypassed. The startup
    /// check in <see cref="GameVersionCheck"/> exists to make that kind of drift loud.
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
                List<SkillInfo> choices = BuildWeightedChoices(__instance, numOptions, level,
                                                               alreadyObtained, forcedTier);
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

            // The 2026-09 build passes the rolling character to GetSkillChoices, but declaring
            // that parameter on the prefix would make Harmony fail to bind on older builds and
            // take the whole plugin down. OpenSkillSelectWindow assigns this immediately before
            // calling PopulateSkillChoices, on every build, so it is the portable source.
            // Null history simply disables the damping.
            Character roller = manager.CurrentRoguelikeSkillSelectingCharacter;
            Dictionary<Guid, int> history = OfferHistory.For(roller);

            LastRoll.Clear();

            List<SkillInfo> pool = BuildEligiblePool(alreadyObtained, roller);
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

                // Matches vanilla: nothing left anywhere in the pool ends the roll early with
                // however many options were filled.
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

        /// <summary>
        /// Mirrors vanilla's eligibility filter, the player's blocked skill trees, and the
        /// exclusion of owned/replaced/disabled skills.
        /// </summary>
        private static List<SkillInfo> BuildEligiblePool(List<SkillInfo> alreadyObtained, Character character)
        {
            List<SkillInfo> pool = Burst2Flame.Game.Instance.Skills.Where(x =>
                !x.Disabled &&
                x.SkillType != SkillType.Basic &&
                x.SkillType != SkillType.Innate &&
                !x.DontIncludeInTree &&
                Burst2Flame.Game.Instance.FullReleaseModeEnabled(x) &&
                SteamManager.instance.MeetsDLCRequirements(x.SkillType)).ToList();

            // Skill trees the player has blocked for this character. Asked of the game rather
            // than reimplemented: its own check also enforces the removal-point budget and the
            // available-tree list, so the game stays the sole authority on what counts as
            // blocked. Empty on game builds that predate the feature.
            List<SkillType> blockedTrees = TreeBlocking.For(character);
            if (blockedTrees.Count > 0)
                pool.RemoveAll(x => blockedTrees.Contains(x.SkillType));

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
        /// Weighted pick within the rolled tier, falling back to the whole remaining pool when
        /// that tier is empty - the same two-step fallback vanilla uses. Blocked skill trees make
        /// an empty tier far more likely than before, which is why the game added it.
        /// </summary>
        private static SkillInfo PickFromTier(
            List<SkillInfo> pool,
            List<SkillInfo> chosenSkills,
            int tier,
            List<SkillInfo> alreadyObtained,
            Dictionary<Guid, int> history)
        {
            // Pass 0: the rolled tier. Pass 1: anything not already offered, any tier.
            for (int pass = 0; pass < 2; pass++)
            {
                List<SkillInfo> candidates = pool
                    .Where(x => !chosenSkills.Contains(x) && (pass == 1 || x.Tier == tier))
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                int t = pass == 0 ? tier : 0;   // 0 marks the any-tier fallback in the roll log

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
            sb.AppendLine(tier > 0
                ? $"Tier {tier} roll over {candidates.Count} candidates:"
                : $"Rolled tier was empty; any-tier fallback over {candidates.Count} candidates:");

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
