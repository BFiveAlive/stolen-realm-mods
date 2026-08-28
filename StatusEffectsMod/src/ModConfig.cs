using BepInEx.Configuration;

namespace StatusEffectsMod
{
    /// <summary>
    /// Everything except the per-status lines, which are bound later by
    /// <see cref="StatusOverrides"/> once the game's status table has loaded.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> HotReloadConfig;
        public static ConfigEntry<bool> DumpStatusData;
        public static ConfigEntry<bool> LogChanges;
        public static ConfigEntry<bool> IncludeFortunesAndQuestStatuses;

        public static ConfigEntry<float> AllDurationMultiplier;
        public static ConfigEntry<float> BeneficialDurationMultiplier;
        public static ConfigEntry<float> HarmfulDurationMultiplier;
        public static ConfigEntry<float> BeneficialPotencyMultiplier;
        public static ConfigEntry<float> HarmfulPotencyMultiplier;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Master switch. When false every status is restored to its shipped values and " +
                "the rest of this file is ignored. Useful for checking whether the mod is " +
                "responsible for something without uninstalling it.");

            HotReloadConfig = cfg.Bind("General", "HotReloadConfig", true,
                "Watch this file and apply edits immediately, without restarting the game. " +
                "Statuses already on a character keep the duration they were applied with, " +
                "since that is snapshotted at cast time; everything else takes effect at once. " +
                "Toggling this setting itself needs a restart, since the watcher is created at " +
                "startup.");

            LogChanges = cfg.Bind("General", "LogChanges", false,
                "List every field this mod changes in the BepInEx log, one line per field. The " +
                "quickest way to confirm an edit was understood, since a typo in the override " +
                "language is otherwise silent unless you go looking for the warning.");

            DumpStatusData = cfg.Bind("General", "DumpStatusData", false,
                "Write every status the game has, with all of its adjustable fields, to " +
                "status-dump.json next to this plugin. Status definitions live in the game's " +
                "asset bundles rather than in code, so this is the only way to see the real " +
                "values. Written once per launch, and it reflects VANILLA values, not your edits.");

            IncludeFortunesAndQuestStatuses = cfg.Bind("General", "IncludeFortunesAndQuestStatuses", false,
                "Also bind fortunes and quest statuses under [Status Overrides]. They use the " +
                "same underlying type as combat statuses but there are a great many of them, so " +
                "they are left out by default to keep this file navigable. Changing this needs a " +
                "restart, and adds entries rather than removing any.");

            AllDurationMultiplier = cfg.Bind("Global Multipliers", "AllDurationMultiplier", 1.0f,
                "Scales the duration of EVERY status, before the beneficial and harmful " +
                "multipliers below. 1 = no change. Durations are in rounds and are rounded to a " +
                "whole number after scaling.");

            BeneficialDurationMultiplier = cfg.Bind("Global Multipliers", "BeneficialDurationMultiplier", 1.0f,
                "Extra duration scaling for statuses tagged Beneficial - buffs, regeneration, " +
                "Inner Warmth and the like. Composes with AllDurationMultiplier and with any " +
                "per-status duration override, so all three multiply together.");

            HarmfulDurationMultiplier = cfg.Bind("Global Multipliers", "HarmfulDurationMultiplier", 1.0f,
                "Extra duration scaling for statuses tagged Harmful - poison, bleed, stuns, " +
                "chills. Below 1 shortens every debuff in the game, which is a blunt but " +
                "effective difficulty lever in both directions, since enemies use these too.");

            BeneficialPotencyMultiplier = cfg.Bind("Global Multipliers", "BeneficialPotencyMultiplier", 1.0f,
                "Scales how much every Beneficial status changes the attributes it touches - " +
                "the size of the armour bonus, not how long it lasts. Attribute amounts are " +
                "expressions, so this wraps them rather than computing them. Effects that SET " +
                "an absolute value are skipped, because most of those are on/off immunity flags " +
                "that scaling would corrupt.");

            HarmfulPotencyMultiplier = cfg.Bind("Global Multipliers", "HarmfulPotencyMultiplier", 1.0f,
                "The same, for statuses tagged Harmful. Note that enemies apply these to you " +
                "and you apply them to enemies, so this cuts both ways rather than being a " +
                "straight difficulty slider.");
        }
    }
}
