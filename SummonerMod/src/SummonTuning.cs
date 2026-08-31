using System;
using System.Collections.Generic;
using System.Globalization;

namespace SummonerMod
{
    /// <summary>
    /// Applies the configured multipliers to the game's global settings.
    ///
    /// The settings object is a ScriptableObject, which lives for the whole process, so an edit
    /// made in place would compound on every reload: "x2" would mean 2x, then 4x, then 8x. The
    /// shipped values are therefore snapshotted once and every apply starts by restoring them,
    /// exactly as StatusEffectsMod does for statuses. Nothing is written to disk - Unity only
    /// serialises ScriptableObject edits in the editor - so uninstalling restores the game.
    /// </summary>
    internal static class SummonTuning
    {
        private static bool captured;

        private static float damagePerMight;
        private static float healthPercentPerInt;
        private static float healthFlatPerInt;
        private static float dodgePerReflex;
        private static float critDamagePerDex;
        private static float critRatingPerDex;
        private static int summonLimitBase;

        private static float[] damageCurve;
        private static float[] healthCurve;
        private static float[] baseHealthByTier;

        public static bool Ready => captured;

        /// <summary>Takes the shipped values, once, the first time the settings object exists.</summary>
        public static bool Capture()
        {
            if (captured)
                return true;

            var settings = Settings();
            if (settings == null)
                return false;

            damagePerMight = settings.AbilityPwrPerMightSummon;
            healthPercentPerInt = settings.SummonLifePerInt;
            healthFlatPerInt = settings.SummonLifeFlatPerInt;
            dodgePerReflex = settings.DodgeRatingPerReflexSummon;
            critDamagePerDex = settings.CritDamagePerDex;
            critRatingPerDex = settings.CritRatingPerDex;
            summonLimitBase = settings.maxSummonLimitBase;

            damageCurve = CurveOf(settings.SummonDamageMultiplerNodes);
            healthCurve = CurveOf(settings.SummonHealthMultiplierNodes);
            baseHealthByTier = Copy(settings.SummonBaseHealthBonusPerEnemyType);

            captured = true;

            Plugin.Log.LogInfo(string.Format(
                "Captured the shipped values: damage/Might {0}, health%/Int {1}, flat health/Int {2}, "
                + "dodge/Reflex {3}, summon limit {4}, {5} damage curve point(s), {6} health curve "
                + "point(s), {7} creature tier(s).",
                Num(damagePerMight), Num(healthPercentPerInt), Num(healthFlatPerInt),
                Num(dodgePerReflex), summonLimitBase,
                damageCurve != null ? damageCurve.Length : 0,
                healthCurve != null ? healthCurve.Length : 0,
                baseHealthByTier != null ? baseHealthByTier.Length : 0));

            return true;
        }

        public static void Apply(string reason)
        {
            var settings = Settings();
            if (settings == null || !captured)
                return;

            var changes = new List<string>();

            // Always restored first, so every apply is computed from the shipped values rather
            // than from whatever the last one left behind.
            settings.AbilityPwrPerMightSummon = Scaled(damagePerMight,
                ModConfig.DamagePerMight, "damage per Might", changes);

            settings.SummonLifePerInt = Scaled(healthPercentPerInt,
                ModConfig.HealthPercentPerIntelligence, "health % per Intelligence", changes);

            settings.SummonLifeFlatPerInt = Scaled(healthFlatPerInt,
                ModConfig.HealthFlatPerIntelligence, "flat health per Intelligence", changes);

            settings.DodgeRatingPerReflexSummon = Scaled(dodgePerReflex,
                ModConfig.DodgePerReflex, "dodge per Reflex", changes);

            settings.CritDamagePerDex = Scaled(critDamagePerDex,
                ModConfig.CritDamagePerDexterity, "crit damage per Dexterity (whole party)", changes);

            settings.CritRatingPerDex = Scaled(critRatingPerDex,
                ModConfig.CritRatingPerDexterity, "crit rating per Dexterity (whole party)", changes);

            int limit = summonLimitBase + (Active ? ModConfig.ExtraSummonLimit.Value : 0);
            if (limit < 0)
                limit = 0;

            // Compared against the shipped value, not the live one, so a reapply reports the
            // same thing the first apply did rather than falling silent once it has taken effect.
            if (limit != summonLimitBase)
                changes.Add("summon limit " + summonLimitBase + " -> " + limit);

            settings.maxSummonLimitBase = limit;

            ScaleCurve(settings.SummonDamageMultiplerNodes, damageCurve,
                ModConfig.DamageByLevel, "damage-by-level curve", changes);

            ScaleCurve(settings.SummonHealthMultiplierNodes, healthCurve,
                ModConfig.HealthByLevel, "health-by-level curve", changes);

            ScaleArray(settings.SummonBaseHealthBonusPerEnemyType, baseHealthByTier,
                ModConfig.BaseHealthByTier, "base health per creature tier", changes);

            if (changes.Count == 0)
            {
                Plugin.Log.LogInfo("Applied (" + reason + "): everything is at its shipped value.");
                return;
            }

            Plugin.Log.LogInfo("Applied (" + reason + "): " + changes.Count + " value(s) changed."
                + (ModConfig.LogChanges.Value ? string.Empty : " Set LogChanges=true to see each one."));

            if (ModConfig.LogChanges.Value)
            {
                foreach (string change in changes)
                    Plugin.Log.LogInfo("  " + change);
            }
        }

        private static bool Active => ModConfig.Enabled.Value;

        private static float Scaled(float shipped, BepInEx.Configuration.ConfigEntry<float> entry,
            string what, List<string> changes)
        {
            float factor = Active ? entry.Value : 1f;
            float value = shipped * factor;

            if (Math.Abs(value - shipped) > 0.0001f)
                changes.Add(what + " " + Num(shipped) + " -> " + Num(value) + " (x" + Num(factor) + ")");

            return value;
        }

        private static void ScaleCurve(LevelMultiplerNode[] nodes, float[] shipped,
            BepInEx.Configuration.ConfigEntry<float> entry, string what, List<string> changes)
        {
            if (nodes == null || shipped == null || nodes.Length != shipped.Length)
                return;

            float factor = Active ? entry.Value : 1f;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null)
                    continue;

                // The field really is spelled "mutlipler" in the game.
                nodes[i].mutlipler = shipped[i] * factor;
            }

            if (Math.Abs(factor - 1f) > 0.0001f)
                changes.Add(what + " scaled by " + Num(factor) + " across " + nodes.Length + " point(s)");
        }

        private static void ScaleArray(float[] live, float[] shipped,
            BepInEx.Configuration.ConfigEntry<float> entry, string what, List<string> changes)
        {
            if (live == null || shipped == null || live.Length != shipped.Length)
                return;

            float factor = Active ? entry.Value : 1f;

            for (int i = 0; i < live.Length; i++)
                live[i] = shipped[i] * factor;

            if (Math.Abs(factor - 1f) > 0.0001f)
                changes.Add(what + " scaled by " + Num(factor) + " across " + live.Length + " tier(s)");
        }

        private static float[] CurveOf(LevelMultiplerNode[] nodes)
        {
            if (nodes == null)
                return null;

            var values = new float[nodes.Length];

            for (int i = 0; i < nodes.Length; i++)
                values[i] = nodes[i] != null ? nodes[i].mutlipler : 0f;

            return values;
        }

        private static float[] Copy(float[] source)
        {
            if (source == null)
                return null;

            var copy = new float[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static GlobalSettings Settings()
        {
            try
            {
                var manager = GlobalSettingsManager.instance;
                return manager != null ? manager.globalSettings : null;
            }
            catch
            {
                // Asked before the manager exists, which is normal for the first few frames.
                return null;
            }
        }

        private static string Num(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
