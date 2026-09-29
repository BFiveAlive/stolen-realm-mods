using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Builds a <see cref="CharacterPresetFile"/> from a class definition.
    ///
    /// A preset is an ordinary ScriptableObject with public fields, so one created at runtime is
    /// indistinguishable from one loaded out of Resources as far as the rest of the game is
    /// concerned: <c>PresetManager</c> lists it, the tile renders it, and
    /// <c>Character.SyncCharacterToPreset</c> builds a character from it.
    ///
    /// Two details are worth knowing when authoring classes:
    ///
    /// - <c>SyncCharacterToPreset</c> calls <c>AddSkills</c> directly, with no <c>CanLevel</c>
    ///   check, so a class can grant a tier 5 skill at level 1 and can skip a skill's
    ///   prerequisite. The tree then shows a gap, which is cosmetic.
    /// - <c>SpecialPresetInfo</c> is deliberately left empty. The game sweeps every preset once at
    ///   start-up to register the skill triggers those passives declare, and a preset injected
    ///   after that sweep would have its triggers ignored. Skills and gear have no such ordering
    ///   requirement.
    /// </summary>
    internal static class PresetFactory
    {
        /// <summary>Marks a preset as one of ours, so donors and re-injection can tell them apart.</summary>
        internal const string NamePrefix = "RoguelikeClassesMod_";

        internal static CharacterPresetFile Build(ClassDefinition definition, ClassDefaults defaults, out List<string> problems)
        {
            problems = new List<string>();

            var preset = ScriptableObject.CreateInstance<CharacterPresetFile>();
            preset.name = NamePrefix + definition.Id;
            preset.hideFlags = HideFlags.HideAndDontSave;

            Populate(preset, definition, defaults, problems);
            return preset;
        }

        /// <summary>
        /// Writes a definition onto a preset that already exists.
        ///
        /// Editing in place rather than rebuilding matters: the array the game caches and the guid
        /// dictionary both hold this object, and a saved character resolves to it by guid. Swapping
        /// in a new instance would leave all three pointing at the old one.
        /// </summary>
        internal static void Populate(CharacterPresetFile preset, ClassDefinition definition, ClassDefaults defaults, List<string> problems)
        {
            preset.Guid = GuidFor(definition.Id);
            preset.PresetName = definition.Name ?? definition.Id;
            preset.PresetDescription = definition.Description ?? string.Empty;
            preset.PresetType = GameMode.Roguelike;
            preset.FullReleaseOnly = false;
            preset.RequiredDlc = DlcType.None;
            preset.Tier = TierOf(definition.Tier ?? defaults?.Tier ?? 2);

            ApplyUnlock(preset, definition);
            ApplyStats(preset, definition.Stats);
            ApplyAppearance(preset, definition, defaults);
            ApplySkills(preset, definition, problems);
            ApplyEquipment(preset, definition, problems);
            ApplyExtraItems(preset, definition, defaults, problems);

            if (!string.IsNullOrEmpty(definition.GearNote))
                Plugin.Log.LogInfo($"  {preset.PresetName} gear intent: {definition.GearNote}");
        }

        /// <summary>
        /// A Guid derived from the class id rather than a random one.
        ///
        /// Characters store <c>CharacterPresetFileGuid</c> in their save file, so the same class
        /// has to produce the same Guid on every launch and on every machine, or a saved character
        /// stops resolving to the class it was made from. Hashing the id gives that for free and
        /// spares whoever writes a class from pasting Guids around.
        /// </summary>
        internal static Guid GuidFor(string id)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("bfivealive.stolenrealm.roguelikeclass." + id));
                return new Guid(hash);
            }
        }

        private static RoguelikePresetTier TierOf(int tier)
        {
            switch (tier)
            {
                case 1: return RoguelikePresetTier.Tier1;
                case 3: return RoguelikePresetTier.Tier3;
                default: return RoguelikePresetTier.Tier2;
            }
        }

        private static void ApplyUnlock(CharacterPresetFile preset, ClassDefinition definition)
        {
            int endless = definition.Unlock?.EndlessLevel ?? 0;
            int difficulty = definition.Unlock?.Difficulty ?? 0;

            if (endless <= 0 && difficulty <= 0)
            {
                preset.NeedsUnlock = false;
                preset.UnlockConditions = new RoguelikePresetUnlockCondition[0];
                preset.UnlockDescription = string.Empty;
                return;
            }

            // Both gates are the same stat, so the endless one simply asks for a larger value.
            float required = endless > 0
                ? UnlockRules.RequiredValue(endless)
                : UnlockRules.RequiredValueForDifficulty(difficulty);

            string described = endless > 0
                ? UnlockRules.DefaultDescription(endless)
                : UnlockRules.DefaultDifficultyDescription(difficulty);

            preset.NeedsUnlock = true;
            preset.UnlockDescription = definition.Unlock?.Description ?? described;
            preset.UnlockConditions = new[]
            {
                new RoguelikePresetUnlockCondition
                {
                    StatName = UnlockRules.DifficultyStat,
                    RequiredValue = required
                }
            };
        }

        private static void ApplyStats(CharacterPresetFile preset, StatBlock stats)
        {
            stats = stats ?? new StatBlock();

            preset.Might = stats.Might;
            preset.Dexterity = stats.Dexterity;
            preset.Vitality = stats.Vitality;
            preset.Intelligence = stats.Intelligence;
            preset.Reflex = stats.Reflex;

            // The game computes this in OnValidate, which only runs in the editor.
            preset.TotalStatPoints = stats.Total;
        }

        /// <summary>
        /// Copies a shipped preset's look wholesale. The appearance fields are index-into-palette
        /// pairs whose colours are normally resolved by a private editor-only method, so taking
        /// both halves from a preset that already works avoids reimplementing that resolution and
        /// avoids any chance of an index landing outside its range.
        /// </summary>
        private static void ApplyAppearance(CharacterPresetFile preset, ClassDefinition definition, ClassDefaults defaults)
        {
            CharacterPresetFile donor = GameData.FindDonor(definition.AppearanceFrom ?? defaults?.AppearanceFrom);
            if (donor == null)
            {
                Plugin.Log.LogWarning($"{preset.PresetName}: no preset to copy an appearance from; using engine defaults.");
                return;
            }

            preset.Gender = donor.Gender;
            preset.SkinColorIndex = donor.SkinColorIndex;
            preset.SkinColor = donor.SkinColor;
            preset.HairTypeMale = donor.HairTypeMale;
            preset.HairTypeFemale = donor.HairTypeFemale;
            preset.HairColorIndex = donor.HairColorIndex;
            preset.HairColor = donor.HairColor;
            preset.HeadTypeMale = donor.HeadTypeMale;
            preset.HeadTypeFemale = donor.HeadTypeFemale;
            preset.EyebrowTypeMale = donor.EyebrowTypeMale;
            preset.EyebrowTypeFemale = donor.EyebrowTypeFemale;
            preset.EyeColorIndex = donor.EyeColorIndex;
            preset.EyeColor = donor.EyeColor;
            preset.FacialHairType = donor.FacialHairType;
            preset.BodyArtColorIndex = donor.BodyArtColorIndex;
            preset.BodyArtColor = donor.BodyArtColor;
            preset.StartingWeaponIndex = donor.StartingWeaponIndex;

            if (string.Equals(definition.Gender, "Female", StringComparison.OrdinalIgnoreCase))
                preset.Gender = Gender.Female;
            else if (string.Equals(definition.Gender, "Male", StringComparison.OrdinalIgnoreCase))
                preset.Gender = Gender.Male;

            // Applied after the gender is settled: each part index means a different thing for a
            // male and a female model, so writing them first would put them in the wrong list.
            AppearanceTables.ApplyTo(preset, definition.Appearance);
        }

        private static void ApplySkills(CharacterPresetFile preset, ClassDefinition definition, List<string> problems)
        {
            var resolved = new List<SkillInfo>();

            foreach (string name in definition.Skills ?? new List<string>())
            {
                SkillInfo skill = GameData.FindSkill(name, out string why);
                if (skill == null)
                    problems.Add(why);
                else
                    resolved.Add(skill);
            }

            preset.StartingSkills = resolved.ToArray();
            preset.SpecialPresetInfo = new SpecialPresetInfo[0];
        }

        private static void ApplyEquipment(CharacterPresetFile preset, ClassDefinition definition, List<string> problems)
        {
            EquipmentBlock gear = definition.Equipment;
            if (gear == null)
                return;

            preset.Head = Item(gear.Head, problems);
            preset.Armor = Item(gear.Armor, problems);
            preset.MainHand = Item(gear.MainHand, problems);
            preset.OffHand = Item(gear.OffHand, problems);
            preset.Ring = Item(gear.Ring, problems);
            preset.Amulet = Item(gear.Amulet, problems);
        }

        private static void ApplyExtraItems(CharacterPresetFile preset, ClassDefinition definition, ClassDefaults defaults, List<string> problems)
        {
            var stacks = new List<ItemInfoAndStacks>();

            foreach (ItemStack entry in Concat(defaults?.ExtraItems, definition.ExtraItems))
            {
                ItemInfo info = Item(entry.Item, problems);
                if (info == null)
                    continue;

                stacks.Add(new ItemInfoAndStacks { ItemInfo = info, Stacks = Math.Max(1, entry.Stacks) });
            }

            preset.UnequippedStartingItems = stacks.ToArray();
        }

        private static IEnumerable<ItemStack> Concat(List<ItemStack> first, List<ItemStack> second)
        {
            if (first != null)
                foreach (ItemStack entry in first)
                    yield return entry;

            if (second != null)
                foreach (ItemStack entry in second)
                    yield return entry;
        }

        private static ItemInfo Item(string name, List<string> problems)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            ItemInfo info = GameData.FindItem(name, out string why);
            if (info == null)
                problems.Add(why);

            return info;
        }
    }
}
