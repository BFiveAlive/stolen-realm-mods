using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Writes the reference data that authoring a class needs and that no decompile can provide:
    /// the shipped presets (the yardstick for how strong a starting class is meant to be), the
    /// item names equipment can be written against, and the difficulty list that endless unlocks
    /// are numbered from.
    ///
    /// All three live in the game's asset bundles rather than in Assembly-CSharp, so reading them
    /// from a running game is the only way to see them.
    ///
    /// The dump is written under the user's own save folder rather than beside the plugin: the
    /// game is often installed under Program Files, where a non-elevated process may not be able
    /// to write.
    /// </summary>
    internal static class GameDataDumper
    {
        private static bool done;

        internal static void TryDump()
        {
            if (done || !Plugin.DumpGameData.Value || !GameData.Ready)
                return;

            done = true;

            try
            {
                string directory = Path.Combine(UnityEngine.Application.persistentDataPath, "RoguelikeClassesMod");
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, "game-data.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(Collect(), Formatting.Indented), Encoding.UTF8);

                Plugin.Log.LogInfo($"Wrote reference data to {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Reference dump failed: {e}");
            }
        }

        private static object Collect()
        {
            return new
            {
                presets = Presets(),
                injected = Injected(),
                difficulties = Difficulties(),
                roguelikeSettings = RoguelikeSettings(),
                items = Items()
            };
        }

        private static object[] Presets()
        {
            CharacterPresetFile[] all = Burst2Flame.Game.Instance.CharacterPresetFiles ?? new CharacterPresetFile[0];

            return all.Where(x => x != null && !PresetInjection.IsOurs(x))
                      .Select(x => (object)new
                      {
                          assetName = x.name,
                          name = x.PresetName,
                          description = x.PresetDescription,
                          mode = x.PresetType.ToString(),
                          tier = x.Tier.ToString(),
                          needsUnlock = x.NeedsUnlock,
                          unlockDescription = x.UnlockDescription,
                          unlockConditions = (x.UnlockConditions ?? new RoguelikePresetUnlockCondition[0])
                              .Select(c => new { c.StatName, c.RequiredValue }).ToArray(),
                          stats = new { x.Might, x.Dexterity, x.Vitality, x.Intelligence, x.Reflex, total = x.TotalStatPoints },
                          startingWeaponIndex = x.StartingWeaponIndex,
                          skills = (x.StartingSkills ?? new SkillInfo[0])
                              .Where(s => s != null)
                              .Select(s => new { name = s.SkillName, tree = s.SkillType.ToString(), s.Tier })
                              .ToArray(),
                          equipment = new
                          {
                              head = Name(x.Head),
                              armor = Name(x.Armor),
                              mainHand = Name(x.MainHand),
                              offHand = Name(x.OffHand),
                              ring = Name(x.Ring),
                              amulet = Name(x.Amulet)
                          },
                          extraItems = (x.UnequippedStartingItems ?? new ItemInfoAndStacks[0])
                              .Where(i => i?.ItemInfo != null)
                              .Select(i => new { item = i.ItemInfo.ItemName, i.Stacks })
                              .ToArray(),
                          specialPassives = (x.SpecialPresetInfo ?? new SpecialPresetInfo[0])
                              .Select(p => new { p.Title, p.Description })
                              .ToArray()
                      })
                      .ToArray();
        }

        /// <summary>
        /// What this mod's own classes resolved to. Written next to the shipped presets so a class
        /// can be checked against them field by field - particularly the equipment, which is
        /// written by name in classes.json and is silently absent if a name is wrong.
        /// </summary>
        private static object[] Injected()
        {
            CharacterPresetFile[] all = Burst2Flame.Game.Instance.CharacterPresetFiles ?? new CharacterPresetFile[0];

            return all.Where(PresetInjection.IsOurs)
                      .Select(x => (object)new
                      {
                          name = x.PresetName,
                          guid = x.Guid.ToString(),
                          tier = x.Tier.ToString(),
                          needsUnlock = x.NeedsUnlock,
                          unlockDescription = x.UnlockDescription,
                          unlockConditions = (x.UnlockConditions ?? new RoguelikePresetUnlockCondition[0])
                              .Select(c => new { c.StatName, c.RequiredValue }).ToArray(),
                          stats = new { x.Might, x.Dexterity, x.Vitality, x.Intelligence, x.Reflex, total = x.TotalStatPoints },
                          skills = (x.StartingSkills ?? new SkillInfo[0])
                              .Where(s => s != null)
                              .Select(s => new { name = s.SkillName, tree = s.SkillType.ToString(), s.Tier })
                              .ToArray(),
                          equipment = new
                          {
                              head = Name(x.Head),
                              armor = Name(x.Armor),
                              mainHand = Name(x.MainHand),
                              offHand = Name(x.OffHand),
                              ring = Name(x.Ring),
                              amulet = Name(x.Amulet)
                          }
                      })
                      .ToArray();
        }

        private static object Difficulties()
        {
            DifficultySettings settings = GlobalSettingsManager.instance?.difficultySettings;
            if (settings?.DifficultiesRoguelike == null)
                return null;

            return new
            {
                authoredRoguelikeCount = settings.DifficultiesRoguelike.Count,
                // The index HighestRoguelikeDifficultyUnlocked has to reach for each endless step.
                endlessThresholds = Enumerable.Range(1, 12)
                    .Select(n => new { endless = n, requiredValue = UnlockRules.RequiredValue(n) })
                    .ToArray(),
                roguelike = settings.DifficultiesRoguelike
                    .Select(d => new { d.DifficultyName, d.HealthModifierPerc, d.DamageModifierPerc, d.AdditionalEnemyModsPerc })
                    .ToArray()
            };
        }

        /// <summary>
        /// The roguelike roll tuning. Not needed to build a class, but it lives in the same asset
        /// and nothing else in this repo can currently read it.
        /// </summary>
        private static object RoguelikeSettings()
        {
            global::RoguelikeSettings settings = GlobalSettingsManager.instance?.roguelikeManager;
            if (settings == null)
                return null;

            return new
            {
                settings.NumSkillOptions,
                settings.RoguelikeMaxLevel,
                settings.NumBattlesToBoss,
                maximumSkillsPerTier = (settings.MaximumSkillsPerTierSettings ?? new MaximumSkillsPerTier[0])
                    .Select(m => new { m.Tier, m.Max }).ToArray(),
                tier2ChanceNodes = Nodes(settings.Tier2ChanceNodes),
                tier3ChanceNodes = Nodes(settings.Tier3ChanceNodes),
                tier4ChanceNodes = Nodes(settings.Tier4ChanceNodes),
                tier5ChanceNodes = Nodes(settings.Tier5ChanceNodes),
                availableSkillTrees = RoguelikeSkillTreeRemoval.AvailableSkillTrees.Select(t => t.ToString()).ToArray(),
                RoguelikeSkillTreeRemoval.MaxRemovalsPossible
            };
        }

        private static object[] Nodes(LevelMultiplerNode[] nodes)
        {
            return (nodes ?? new LevelMultiplerNode[0])
                .Select(n => (object)new { level = n.level, value = n.mutlipler })
                .ToArray();
        }

        /// <summary>
        /// Equipment in classes.json is written by name, so the list of names that exist is the
        /// difference between guessing and knowing.
        /// </summary>
        private static object[] Items()
        {
            List<ItemInfo> items = Burst2Flame.Game.Instance.Items;
            if (items == null)
                return new object[0];

            return items.Where(x => x != null)
                        .Select(x => (object)new { name = x.ItemName, type = x.ItemType.ToString(), rarity = x.Rarity.ToString(), x.MinLevel })
                        .ToArray();
        }

        private static string Name(ItemInfo item) => item != null ? item.ItemName : null;
    }
}
