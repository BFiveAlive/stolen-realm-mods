using BepInEx.Configuration;
using UnityEngine;

namespace AutoEquipMod
{
    /// <summary>
    /// All tuning lives in BepInEx/config/bfivealive.stolenrealm.autoequipmod.cfg, and therefore
    /// also in the mod manager, which reads its list straight from BepInEx.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> OnlyUpgrades;
        public static ConfigEntry<bool> FillEmptySlotsSilently;
        public static ConfigEntry<bool> AskBeforeReplacing;
        public static ConfigEntry<float> MinimumImprovement;
        public static ConfigEntry<float> SettleSeconds;
        public static ConfigEntry<int> MaxQueued;
        public static ConfigEntry<float> PromptScale;
        public static ConfigEntry<bool> LogDecisions;

        public static ConfigEntry<bool> ConsiderWeapons;
        public static ConfigEntry<bool> ConsiderShields;
        public static ConfigEntry<bool> ConsiderArmour;
        public static ConfigEntry<bool> ConsiderJewellery;

        public static ConfigEntry<float> WeightItemLevel;
        public static ConfigEntry<float> WeightRarity;
        public static ConfigEntry<float> WeightArmour;
        public static ConfigEntry<float> WeightMagicArmour;
        public static ConfigEntry<float> WeightMods;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", false,
                "Master switch. Off means the game behaves exactly as it does without this mod. " +
                "This mod ships OFF, unlike the others here: it is the one that acts on its own " +
                "rather than waiting to be configured, and equipping a character's gear for them " +
                "uninvited is not a thing to do before being asked. Turn it on to use it.");

            AskBeforeReplacing = cfg.Bind("General", "AskBeforeReplacing", true,
                "Ask before swapping something you are already wearing. Turn this off to equip " +
                "silently, which is quicker but will replace an item you deliberately chose.");

            FillEmptySlotsSilently = cfg.Bind("General", "FillEmptySlotsSilently", true,
                "Equip straight away when the slot is empty, without asking. There is nothing to " +
                "lose in that case, so a prompt is only in the way.");

            OnlyUpgrades = cfg.Bind("General", "OnlyUpgrades", true,
                "Only speak up when the new item scores higher than what is worn. Off asks about " +
                "every equippable item you pick up, which is a great many prompts.");

            MinimumImprovement = cfg.Bind("General", "MinimumImprovement", 5f,
                new ConfigDescription(
                    "How much better the new item has to score, as a percentage, before it is " +
                    "worth interrupting you. 0 asks about any improvement at all.",
                    new AcceptableValueRange<float>(0f, 100f)));

            LogDecisions = cfg.Bind("General", "LogDecisions", false,
                "Write every item considered and the score it was given to the BepInEx log. The " +
                "quickest way to find out why something was or was not offered.");

            ConsiderWeapons = cfg.Bind("Item Types", "ConsiderWeapons", true, "Include weapons.");
            ConsiderShields = cfg.Bind("Item Types", "ConsiderShields", true, "Include shields.");
            ConsiderArmour = cfg.Bind("Item Types", "ConsiderArmour", true,
                "Include head and body armour.");
            ConsiderJewellery = cfg.Bind("Item Types", "ConsiderJewellery", true,
                "Include rings and amulets. These have two slots, so the comparison is against " +
                "whichever of the two scores lower.");

            // The score is deliberately simple and every part of it is exposed, because no
            // weighting can be right for every build. It decides whether to ask, never what to
            // wear - the prompt shows the numbers and the choice stays with the reader.
            WeightItemLevel = cfg.Bind("Scoring", "WeightItemLevel", 1f,
                "How much an item's level counts toward its score.");

            WeightRarity = cfg.Bind("Scoring", "WeightRarity", 8f,
                "How much rarity counts. Multiplied by the game's own stat bonus for that " +
                "rarity, so the ranking matches what the game thinks rarity is worth.");

            WeightArmour = cfg.Bind("Scoring", "WeightArmour", 0.5f, "How much armour counts.");

            WeightMagicArmour = cfg.Bind("Scoring", "WeightMagicArmour", 0.5f,
                "How much magic armour counts.");

            WeightMods = cfg.Bind("Scoring", "WeightMods", 6f,
                "How much each prefix, suffix or endgame modifier counts.");

            SettleSeconds = cfg.Bind("Advanced", "SettleSeconds", 6f,
                new ConfigDescription(
                    "Items gained within this many seconds of a scene loading are ignored. " +
                    "Loading a save hands every item you already own to the character one at a " +
                    "time, which is indistinguishable from picking them up.",
                    new AcceptableValueRange<float>(0f, 60f)));

            MaxQueued = cfg.Bind("Advanced", "MaxQueued", 6,
                new ConfigDescription(
                    "How many pending offers to hold at once. Opening a large chest can produce " +
                    "a run of them, and a queue that grows without limit is worse than one that " +
                    "drops the overflow.",
                    new AcceptableValueRange<int>(1, 50)));

            PromptScale = cfg.Bind("Advanced", "PromptScale", 1f,
                new ConfigDescription("Size of the prompt and its text.",
                    new AcceptableValueRange<float>(0.6f, 2.5f)));
        }
    }
}
