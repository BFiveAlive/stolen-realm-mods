using BepInEx.Configuration;

namespace SummonerMod
{
    /// <summary>
    /// Everything that decides how strong a summon is, as far as the game exposes it in data.
    ///
    /// Values are multipliers on what the game ships rather than absolute replacements, because
    /// the shipped numbers are the result of tuning against every level curve in the game and a
    /// flat replacement throws that away. 1 leaves a thing exactly as it was.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> HotReloadConfig;
        public static ConfigEntry<bool> LogChanges;

        public static ConfigEntry<float> DamagePerMight;
        public static ConfigEntry<float> HealthPercentPerIntelligence;
        public static ConfigEntry<float> HealthFlatPerIntelligence;
        public static ConfigEntry<float> DodgePerReflex;

        public static ConfigEntry<float> DamageByLevel;
        public static ConfigEntry<float> HealthByLevel;
        public static ConfigEntry<float> BaseHealthByTier;

        public static ConfigEntry<int> ExtraSummonLimit;

        public static ConfigEntry<float> CritDamagePerDexterity;
        public static ConfigEntry<float> CritRatingPerDexterity;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", false,
                "Master switch. When false every value is restored to what the game shipped and " +
                "the rest of this file is ignored. This mod ships OFF: two of its settings scale " +
                "crit for your whole party rather than for summons alone, so it is not a change " +
                "that should arrive unannounced. Turn it on to use it.");

            HotReloadConfig = cfg.Bind("General", "HotReloadConfig", true,
                "Watch this file and apply edits immediately, without restarting the game. " +
                "Toggling this setting itself needs a restart, since the watcher is created at " +
                "startup.");

            LogChanges = cfg.Bind("General", "LogChanges", false,
                "List every value this mod changes in the BepInEx log, with its before and after.");

            // --- scaling with the summoner's own attributes ---------------------------------

            DamagePerMight = cfg.Bind("Scaling With Summoner", "DamagePerMight", 1.0f,
                new ConfigDescription(
                    "Scales how much summon damage each point of the summoner's Might is worth. " +
                    "This is the main lever on a Might-based summoner.",
                    new AcceptableValueRange<float>(0f, 10f)));

            HealthPercentPerIntelligence = cfg.Bind("Scaling With Summoner",
                "HealthPercentPerIntelligence", 1.0f,
                new ConfigDescription(
                    "Scales the percentage of summon health each point of the summoner's " +
                    "Intelligence grants.",
                    new AcceptableValueRange<float>(0f, 10f)));

            HealthFlatPerIntelligence = cfg.Bind("Scaling With Summoner",
                "HealthFlatPerIntelligence", 1.0f,
                new ConfigDescription(
                    "Scales the flat summon health each point of the summoner's Intelligence " +
                    "grants. Composes with HealthPercentPerIntelligence rather than replacing it.",
                    new AcceptableValueRange<float>(0f, 10f)));

            DodgePerReflex = cfg.Bind("Scaling With Summoner", "DodgePerReflex", 1.0f,
                new ConfigDescription(
                    "Scales the dodge rating a summon gets from each point of the summoner's " +
                    "Reflex.",
                    new AcceptableValueRange<float>(0f, 10f)));

            // --- the summon's own level curves -----------------------------------------------

            DamageByLevel = cfg.Bind("Level Curves", "DamageByLevel", 1.0f,
                new ConfigDescription(
                    "Scales the whole summon damage-by-level curve. Every point on the curve is " +
                    "multiplied, so the shape the game tuned is kept and only its height changes.",
                    new AcceptableValueRange<float>(0f, 10f)));

            HealthByLevel = cfg.Bind("Level Curves", "HealthByLevel", 1.0f,
                new ConfigDescription(
                    "Scales the whole summon health-by-level curve, the same way.",
                    new AcceptableValueRange<float>(0f, 10f)));

            BaseHealthByTier = cfg.Bind("Level Curves", "BaseHealthByTier", 1.0f,
                new ConfigDescription(
                    "Scales the per-level base health bonus a summon gets for its creature tier " +
                    "(Fodder, Soldier, Elite, Champion, Boss). Raising this favours the bigger " +
                    "summons, since they sit in the higher tiers.",
                    new AcceptableValueRange<float>(0f, 10f)));

            // --- how many ---------------------------------------------------------------------

            ExtraSummonLimit = cfg.Bind("Limits", "ExtraSummonLimit", 0,
                new ConfigDescription(
                    "Added to the base number of summons a character may have at once. Skills " +
                    "and gear that raise the limit still apply on top. Negative lowers it.",
                    new AcceptableValueRange<int>(-3, 20)));

            // --- shared with the rest of the party -------------------------------------------

            CritDamagePerDexterity = cfg.Bind("Shared With The Whole Party",
                "CritDamagePerDexterity", 1.0f,
                new ConfigDescription(
                    "Scales critical damage per point of Dexterity. THIS IS NOT SUMMON-ONLY: the " +
                    "game reads the same number for summons and for every character, so raising " +
                    "it makes your whole party crit harder. It is here because it is genuinely " +
                    "part of a summoner's damage, not because it is safe to treat as a summon " +
                    "setting.",
                    new AcceptableValueRange<float>(0f, 10f)));

            CritRatingPerDexterity = cfg.Bind("Shared With The Whole Party",
                "CritRatingPerDexterity", 1.0f,
                new ConfigDescription(
                    "Scales critical rating per point of Dexterity. Shared with the whole party " +
                    "in exactly the same way as CritDamagePerDexterity.",
                    new AcceptableValueRange<float>(0f, 10f)));
        }
    }
}
