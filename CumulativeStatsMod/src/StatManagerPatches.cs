using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Hooks into the battle stats window. There are only two touch points: swapping the numbers
    /// out when the run-total view is selected, and noticing which characters the window was
    /// drawn for so a toggle can redraw the same rows.
    /// </summary>
    [HarmonyPatch(typeof(StatManager))]
    internal static class StatManagerPatches
    {
        /// <summary>
        /// Replaces the per-character value column with run totals. This reproduces vanilla's
        /// formatting — the sizes, the colours, the section subtotal, the per-row Ceil — and
        /// changes only where each number is read from.
        ///
        /// It returns true (run the original) on anything it does not fully understand, so a
        /// mistake here costs the vanilla view rather than a broken window.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("GetStatDisplay")]
        private static bool GetStatDisplayPrefix(StatManager __instance, Character character, ref List<string> __result)
        {
            try
            {
                if (!StatsToggle.ShowCumulative || !__instance.UseSections || !Application.isPlaying)
                    return true;

                if (__instance.StatSections == null)
                    return true;

                string subColour = ColorUtility.ToHtmlStringRGB(__instance.SubSectionColor);
                string titleColour = ColorUtility.ToHtmlStringRGB(__instance.SectionTitleColor);

                var lines = new List<string>();
                foreach (StatSection section in __instance.StatSections)
                {
                    if (section == null || section.BattleStats == null)
                        return true;

                    float sectionTotal = 0f;
                    var sectionLines = new List<string>();

                    foreach (BattleStat stat in section.BattleStats)
                    {
                        float value = Mathf.Ceil(StatTracker.Cumulative(character, stat));
                        sectionTotal += value;

                        if (!section.HideChildren)
                        {
                            sectionLines.Add("<size=" + __instance.SubSectionSize + "><color=#" + subColour + ">" +
                                             Format(value) + "</color></size>");
                        }
                    }

                    sectionLines.Insert(0, "<size=" + __instance.SectionTitleSize + "><color=#" + titleColour + ">" +
                                           Format(sectionTotal) + "</color></size>");
                    lines.AddRange(sectionLines);
                }

                __result = lines;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Falling back to the vanilla stat display: " + e);
                return true;
            }
        }

        /// <summary>
        /// The window renders whatever the tracker knows at this instant, so sample first rather
        /// than showing a total that is up to half a second stale.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("PopulateStats")]
        private static void PopulateStatsPrefix()
        {
            try
            {
                if (ModConfig.Enabled.Value)
                    StatTracker.Poll();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Pre-display stat sample failed: " + e.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("PopulateStats")]
        private static void PopulateStatsPostfix(List<Character> characters)
        {
            StatsToggle.CaptureCharacters(characters);
        }

        /// <summary>
        /// Vanilla prints the raw ceiling of each value, which is fine for one battle. A whole
        /// run's damage total is several orders of magnitude larger and overflows a column sized
        /// for four or five digits, so past a configurable threshold it gets abbreviated.
        /// Below that threshold the formatting is byte-for-byte vanilla.
        /// </summary>
        private static string Format(float value)
        {
            float threshold = ModConfig.CompactNumberThreshold.Value;
            float magnitude = Mathf.Abs(value);

            if (threshold <= 0f || magnitude < threshold)
                return value.ToString();

            if (magnitude >= 1000000000f)
                return (value / 1000000000f).ToString("0.##") + "B";

            if (magnitude >= 1000000f)
                return (value / 1000000f).ToString("0.##") + "M";

            if (magnitude >= 1000f)
                return (value / 1000f).ToString("0.#") + "K";

            return value.ToString();
        }
    }
}
