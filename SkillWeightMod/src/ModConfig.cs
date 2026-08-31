using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Burst2Flame;

namespace SkillWeightMod
{
    /// <summary>
    /// All tuning lives in BepInEx/config/bfivealive.stolenrealm.skillweightmod.cfg,
    /// so weights can be retuned between runs without rebuilding.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<float> SynergyStrength;
        public static ConfigEntry<float> TreeAffinity;
        public static ConfigEntry<float> BroadAffinity;
        public static ConfigEntry<float> NarrowAffinity;
        public static ConfigEntry<float> DependencyBonus;
        public static ConfigEntry<float> OfferedDecay;
        public static ConfigEntry<int> MaxOfferedPenaltyStacks;
        public static ConfigEntry<float> MinWeight;
        public static ConfigEntry<float> MaxWeight;
        public static ConfigEntry<bool> LogRolls;
        public static ConfigEntry<bool> ShowWeightsInMenu;
        public static ConfigEntry<float> WeightReadoutOffsetX;
        public static ConfigEntry<float> WeightReadoutOffsetY;
        public static ConfigEntry<bool> HotReloadConfig;
        public static ConfigEntry<bool> DumpSkillData;

        public static ConfigEntry<bool> RerollEnabled;
        public static ConfigEntry<float> RerollHealthCost;
        public static ConfigEntry<float> RerollManaCost;
        public static ConfigEntry<float> RerollHealthThreshold;
        public static ConfigEntry<float> RerollManaThreshold;
        public static ConfigEntry<bool> RerollCostFromMaxPool;
        public static ConfigEntry<float> RerollButtonMarginX;
        public static ConfigEntry<float> RerollButtonMarginY;
        public static ConfigEntry<float> RerollButtonScale;
        public static ConfigEntry<int> RerollSubtextScale;
        public static ConfigEntry<float> RerollButtonOffsetX;
        public static ConfigEntry<float> RerollButtonOffsetY;

        public static void Bind(ConfigFile cfg)
        {
            SynergyStrength = cfg.Bind("General", "SynergyStrength", 1.0f,
                "Master scalar on every affinity below. 0 = vanilla uniform rolls. " +
                "Above 1 specialises harder. NEGATIVE values invert the whole system, " +
                "pushing rolls toward skills unlike the ones you already have.");

            HotReloadConfig = cfg.Bind("General", "HotReloadConfig", true,
                "Watch this file and apply edits immediately, without restarting the game. " +
                "Every value is read fresh at the point of use, so a saved change takes effect " +
                "on the next roll or the next frame. Toggling this setting itself needs a " +
                "restart, since the watcher is created at startup.");

            DumpSkillData = cfg.Bind("General", "DumpSkillData", false,
                "Write the game's entire skill table to skill-dump.json next to this plugin, " +
                "once, as soon as the data is loaded. Skill definitions live in the game's asset " +
                "bundles rather than in code, so this is the only way to inspect them. Useful " +
                "for checking how common a tag or status really is before weighting on it.");

            ShowWeightsInMenu = cfg.Bind("General", "ShowWeightsInMenu", true,
                "Testing aid. Prints each offered skill's computed weight under its name in the " +
                "level-up window: the raw weight, its share of that tier's total, and how many " +
                "candidates it beat. Costs nothing when off.");

            WeightReadoutOffsetX = cfg.Bind("General", "WeightReadoutOffsetX", 0f,
                "Horizontal nudge for the weight readout, which sits under the skill icon. " +
                "Only used when ShowWeightsInMenu is on.");

            WeightReadoutOffsetY = cfg.Bind("General", "WeightReadoutOffsetY", -4f,
                "Vertical nudge for the weight readout, measured from the BOTTOM EDGE of the " +
                "option, measured from its bottom edge. Negative moves it further down. The " +
                "readout is a sibling of the skill option rather than a child, so that hovering " +
                "the numbers does not also trigger the option's own skill tooltip.");

            LogRolls = cfg.Bind("General", "LogRolls", false,
                "Log every skill roll and its computed weights to the BepInEx console. Debug aid.");

            TreeAffinity = cfg.Bind("Affinity", "TreeAffinity", 0.35f,
                "Added per already-owned skill from the same tree (Fire, Warrior, Shadow, ...). " +
                "This is the main specialisation lever.");

            BroadAffinity = cfg.Bind("Affinity", "BroadAffinity", 0.05f,
                "Master scalar on the broad categories (Damage, Defense, Melee, Passive, ...). " +
                "Each shared category also has its own multiplier under [Broad Weights]. " +
                "Broad categories are large, so a little goes a long way.");

            NarrowAffinity = cfg.Bind("Affinity", "NarrowAffinity", 0.2f,
                "Master scalar on the narrow categories (Poison, Crit, Teleport, ...). " +
                "Each shared category also has its own multiplier under [Narrow Weights]. " +
                "Raise this relative to BroadAffinity to chase specific mechanics rather than " +
                "general roles.");

            DependencyBonus = cfg.Bind("Affinity", "DependencyBonus", 1.0f,
                "Flat bonus when you already own the candidate's prerequisite skill. " +
                "Encourages the game to finish chains it has started.");

            OfferedDecay = cfg.Bind("Repetition", "OfferedDecay", 0.5f,
                "Multiplies a skill's weight by this for each time it has already been OFFERED to " +
                "this character during the run, whether or not it was taken. 0.5 halves it per " +
                "offer, 0.25 punishes repeats harder, 1.0 disables the whole mechanic. " +
                "Declining a skill does not remove it from the pool, so without this a heavily " +
                "weighted skill keeps reappearing at full strength.");

            MaxOfferedPenaltyStacks = cfg.Bind("Repetition", "MaxOfferedPenaltyStacks", 5,
                "Cap on how many times the OfferedDecay penalty compounds, so a skill offered " +
                "very often does not become effectively impossible. At the default 0.5 decay, " +
                "5 stacks bottoms out at 1/32 weight.");

            RerollEnabled = cfg.Bind("Reroll", "RerollEnabled", true,
                "Adds a Reroll button to the skill selection window that redraws the offered " +
                "skills for a health and mana cost. Set false to remove the button entirely.");

            RerollHealthCost = cfg.Bind("Reroll", "RerollHealthCost", 0.49f,
                "Fraction of health spent per reroll. 0.5 = half.");

            RerollManaCost = cfg.Bind("Reroll", "RerollManaCost", 0.49f,
                "Fraction of mana spent per reroll. Characters with no mana pool skip this.");

            RerollCostFromMaxPool = cfg.Bind("Reroll", "RerollCostFromMaxPool", true,
                "true  = cost is a fraction of MAXIMUM health/mana - a flat price, and the " +
                "reason the thresholds below keep you from hitting zero. " +
                "false = cost is a fraction of CURRENT health/mana, which halves what you have " +
                "left and can never quite reach zero.");

            RerollHealthThreshold = cfg.Bind("Reroll", "RerollHealthThreshold", 0.49f,
                "The button is greyed out at or below this fraction of maximum health.");

            RerollManaThreshold = cfg.Bind("Reroll", "RerollManaThreshold", 0.49f,
                "The button is greyed out at or below this fraction of maximum mana. " +
                "Ignored for characters with no mana pool.");

            RerollButtonMarginX = cfg.Bind("Reroll", "RerollButtonMarginX", 32f,
                "Distance in UI units from the right edge of the skill window.");

            RerollButtonMarginY = cfg.Bind("Reroll", "RerollButtonMarginY", 32f,
                "Distance in UI units from the top edge of the skill window.");

            RerollButtonScale = cfg.Bind("Reroll", "RerollButtonScale", 0.75f,
                "Size of the Reroll button relative to the Accept button it is cloned from. " +
                "Scales the whole button including its text.");

            RerollSubtextScale = cfg.Bind("Reroll", "RerollSubtextScale", 55,
                "Size of the cost line under the word Reroll, as a percentage of the main " +
                "label's font size.");

            RerollButtonOffsetX = cfg.Bind("Reroll", "RerollButtonOffsetX", 0f,
                "Extra manual nudge on top of the margins above. Positive moves right.");

            RerollButtonOffsetY = cfg.Bind("Reroll", "RerollButtonOffsetY", 0f,
                "Extra manual nudge on the vertical axis. Positive moves up.");

            MinWeight = cfg.Bind("Clamp", "MinWeight", 0.05f,
                "Floor on a skill's final weight. Keep above 0 so no skill becomes truly unrollable.");

            MaxWeight = cfg.Bind("Clamp", "MaxWeight", 20.0f,
                "Ceiling on a skill's final weight, so a deep specialisation cannot fully crowd out everything else.");

        }

    }
}
