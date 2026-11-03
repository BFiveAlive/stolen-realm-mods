using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Watches a roguelike game go by and writes it down.
    ///
    /// The hard part of this - knowing when a battle ended - is already solved by
    /// <see cref="StatTracker"/>, which infers it from the stats dropping rather than from any
    /// event the game raises. This hangs off that: every time a battle folds into the run total,
    /// a copy of what each character did in that battle is kept.
    ///
    /// A run opens on the first battle recorded and closes when the rewards screen appears, which
    /// is the one moment the game reliably says a run is over. A run left part-way through - alt
    /// F4, back to the menu - is still written, marked unfinished, because a half-recorded run is
    /// worth more than no record of it.
    /// </summary>
    internal static class RunRecorder
    {
        private static RunRecord current;
        private static int battleNumber;

        /// <summary>Guards against the rewards screen finalising the same run twice.</summary>
        private static bool finalised;

        internal static RunRecord Current => current;

        /// <summary>
        /// Called by the tracker when one or more characters' battles have just folded into their
        /// run totals. Everything folded in a single pass is one battle.
        /// </summary>
        internal static void BattleFinished(List<KeyValuePair<string, Dictionary<int, float>>> folded)
        {
            if (!ModConfig.ArchiveRuns.Value || folded == null || folded.Count == 0)
                return;

            try
            {
                if (current == null)
                    Begin();

                var battle = new BattleRecord
                {
                    Number = ++battleNumber,
                    EndedUtc = DateTime.UtcNow.ToString("o"),
                };

                foreach (KeyValuePair<string, Dictionary<int, float>> entry in folded)
                {
                    var named = new Dictionary<string, float>();

                    foreach (KeyValuePair<int, float> stat in entry.Value)
                    {
                        if (Math.Abs(stat.Value) < 0.001f)
                            continue;           // a stat nobody scored is noise in the file

                        named[((BattleStat)stat.Key).ToString()] = stat.Value;
                    }

                    if (named.Count > 0)
                        battle.Stats[entry.Key] = named;
                }

                if (battle.Stats.Count == 0)
                    return;                     // an empty battle is not worth a row

                // Taken rather than peeked: this battle is over, so its records are final and the
                // next battle starts from nothing.
                battle.Records = ExtraStats.TakeBattle();
                ExtraStats.FoldInto(current.Records, battle.Records);

                current.Battles.Add(battle);
                AddToTotals(battle);

                // Written as it goes rather than only at the end: a crash or an alt-F4 mid-run
                // should still leave everything up to that point on disk.
                Snapshot();
                RunArchive.Save(current);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not record that battle: " + e);
            }
        }

        /// <summary>Called when the rewards screen appears, which is the end of a run.</summary>
        internal static void RunEnded(bool won)
        {
            if (!ModConfig.ArchiveRuns.Value || current == null || finalised)
                return;

            try
            {
                finalised = true;
                current.Outcome = won ? "won" : "lost";
                current.EndedUtc = DateTime.UtcNow.ToString("o");

                Snapshot();
                RunArchive.Save(current);

                Plugin.Log.LogInfo("Archived the run: " + current.Title + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not finish the run record: " + e);
            }
        }

        /// <summary>
        /// Lets the next run start cleanly. Called once the rewards screen has gone, rather than
        /// when it appears, so anything still arriving lands on the run it belongs to.
        /// </summary>
        internal static void Reset()
        {
            current = null;
            battleNumber = 0;
            finalised = false;
        }

        private static void Begin()
        {
            current = new RunRecord
            {
                Id = DateTime.Now.ToString("yyyy-MM-dd-HHmmss"),
                StartedUtc = DateTime.UtcNow.ToString("o"),
            };

            battleNumber = 0;
            finalised = false;
        }

        private static void AddToTotals(BattleRecord battle)
        {
            foreach (KeyValuePair<string, Dictionary<string, float>> who in battle.Stats)
            {
                if (!current.Totals.TryGetValue(who.Key, out Dictionary<string, float> totals))
                    current.Totals[who.Key] = totals = new Dictionary<string, float>();

                foreach (KeyValuePair<string, float> stat in who.Value)
                {
                    totals.TryGetValue(stat.Key, out float running);
                    totals[stat.Key] = running + stat.Value;
                }
            }
        }

        /// <summary>
        /// Records the party as it stands: class, level, trees removed, attributes, skills and
        /// gear. Taken afresh each time rather than once at the start, so the stored build is the
        /// one the run finished with, which is the one worth looking back at.
        /// </summary>
        private static void Snapshot()
        {
            List<Character> party = Party();
            if (party.Count == 0)
                return;

            current.Party = party.Select(Describe).Where(x => x != null).ToList();

            try
            {
                ObservableQuestData quest = NetworkingManager.Instance?.NetworkManager?.Root?.ObservableQuestData;
                if (quest != null)
                {
                    current.Difficulty = quest.CurrentDifficultyIndex;
                    current.LevelReached = quest.CurrentRoguelikeLevelIndex;
                }
            }
            catch (Exception)
            {
                // Progress is a nicety; the party and the battles are the record.
            }
        }

        private static MemberRecord Describe(Character character)
        {
            try
            {
                var member = new MemberRecord
                {
                    Name = character.CharacterName,
                    Level = character.Level,
                    StartingClass = StartingClass(character),
                };

                member.TreesRemoved = TreesRemoved(character);
                member.Attributes = Attributes(character);

                if (character.Skills != null)
                {
                    member.Skills = character.Skills
                        .Where(x => x != null && !string.IsNullOrEmpty(x.SkillName)
                                    && x.SkillType != SkillType.Basic && x.SkillType != SkillType.Innate)
                        .Select(x => x.SkillName + " [" + x.SkillType + " T" + x.Tier + "]")
                        .ToList();
                }

                if (character.EquippedItems != null)
                {
                    member.Equipment = character.EquippedItems
                        .Where(x => x?.ItemInfo != null)
                        .Select(x => x.ItemInfo.ItemName)
                        .ToList();
                }

                return member;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not describe a party member: " + e.Message);
                return null;
            }
        }

        /// <summary>The preset the character was created from, which is their class.</summary>
        private static string StartingClass(Character character)
        {
            try
            {
                if (string.IsNullOrEmpty(character.CharacterPresetFileGuid))
                    return null;

                string wanted = character.CharacterPresetFileGuid;

                // Matched by string rather than through the guid dictionary: the character stores
                // its preset as text, and parsing it back would throw on anything the game wrote
                // in a shape Guid.Parse does not accept.
                CharacterPresetFile[] presets = Burst2Flame.Game.Instance.CharacterPresetFiles;
                if (presets == null)
                    return null;

                foreach (CharacterPresetFile preset in presets)
                {
                    if (preset != null &&
                        string.Equals(preset.Guid.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                        return preset.PresetName;
                }

                return null;
            }
            catch (Exception)
            {
                // A preset from a mod the player has since removed resolves to nothing, which is
                // not worth losing the rest of the record over.
                return null;
            }
        }

        private static List<string> TreesRemoved(Character character)
        {
            try
            {
                List<SkillType> excluded = RoguelikeSkillTreeRemoval.GetValidatedExclusions(character);
                return excluded == null
                    ? new List<string>()
                    : excluded.Select(x => x.ToString()).ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static Dictionary<string, float> Attributes(Character character)
        {
            var values = new Dictionary<string, float>();

            foreach (string name in new[] { "Might", "Dexterity", "Vitality", "Intelligence", "Reflex" })
            {
                try { values[name] = character[name]; }
                catch (Exception) { }
            }

            return values;
        }

        private static List<Character> Party()
        {
            try
            {
                return NetworkingManager.Instance?.PartyCharacters?.Where(x => x != null).ToList()
                       ?? new List<Character>();
            }
            catch (Exception)
            {
                return new List<Character>();
            }
        }
    }
}
