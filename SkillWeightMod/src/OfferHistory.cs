using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Burst2Flame;

namespace SkillWeightMod
{
    /// <summary>
    /// Per-character record of how many times each skill has been *offered* during this run,
    /// used to damp down skills the player keeps declining.
    ///
    /// Declining a skill does not remove it from the eligible pool, so without this a
    /// heavily-weighted skill is re-offered at full weight every single level.
    ///
    /// Keyed on the Character instance via a ConditionalWeakTable: entries disappear when the
    /// character is collected, so a new run (which builds new Character objects from presets)
    /// starts with empty history and nothing leaks. Character is a plain Observable object,
    /// not a MonoBehaviour, so instance identity is stable for the life of a run.
    /// </summary>
    internal static class OfferHistory
    {
        private static readonly ConditionalWeakTable<Character, Dictionary<Guid, int>> Table =
            new ConditionalWeakTable<Character, Dictionary<Guid, int>>();

        /// <summary>Returns null for a null character; callers treat that as "no history".</summary>
        public static Dictionary<Guid, int> For(Character character)
        {
            return character == null ? null : Table.GetOrCreateValue(character);
        }

        public static int TimesOffered(Dictionary<Guid, int> history, SkillInfo skill)
        {
            if (history == null || skill == null)
                return 0;

            return history.TryGetValue(skill.Guid, out int count) ? count : 0;
        }

        public static void Record(Dictionary<Guid, int> history, SkillInfo skill)
        {
            if (history == null || skill == null)
                return;

            history.TryGetValue(skill.Guid, out int count);
            history[skill.Guid] = count + 1;
        }
    }
}
