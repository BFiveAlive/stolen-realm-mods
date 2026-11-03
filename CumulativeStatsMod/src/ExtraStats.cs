using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CumulativeStatsMod
{
    /// <summary>
    /// The stats the game does not keep: the biggest things that happened, rather than the totals.
    ///
    /// <c>Root.BattleStats</c> only ever holds running sums, so "the hardest you were ever hit" is
    /// not recoverable from it afterwards - by the time you read it, that hit is indistinguishable
    /// from every other. It has to be noticed as it happens.
    ///
    /// <c>StatManager.ModifyBattleStat</c> is called once per damage instance from inside
    /// <c>Character.ApplyAction</c>, so every call is one hit and the largest is just the largest
    /// argument ever passed. That one hook gives the single-hit records for free.
    ///
    /// <b>Host only.</b> Those calls run inside the damage calculation, which is host-authoritative
    /// - the host computes damage and syncs the resulting health. The ordinary totals reach clients
    /// because <c>Root.BattleStats</c> is a synced list, but these are worked out here rather than
    /// read from it, so on a client they stay empty. Nothing can be done about that short of
    /// syncing them ourselves, which is not worth a network message per hit.
    /// </summary>
    internal static class ExtraStats
    {
        internal const string LargestHit = "LargestSingleAttackDealt";
        internal const string LargestTaken = "LargestAttackReceived";
        internal const string LargestHeal = "LargestSingleHeal";
        internal const string LargestAction = "LargestSingleActionDamage";

        /// <summary>Per character, the records for the battle currently being fought.</summary>
        private sealed class Record
        {
            public float MaxHit;
            public float MaxTaken;
            public float MaxHeal;
            public float MaxAction;

            /// <summary>Damage so far in the burst being watched, and when it stops counting.</summary>
            public float ActionSum;
            public float ActionEndsAt;
        }

        private static readonly Dictionary<string, Record> records =
            new Dictionary<string, Record>(StringComparer.Ordinal);

        /// <summary>
        /// How long after a hit another hit still counts as the same action.
        ///
        /// A single action is not one call into the damage code. Chains and pierces arrive as
        /// separate applications, some of them deliberately delayed by the game so the hits land
        /// one after another on screen rather than all at once, so grouping by the call stack
        /// would split a chain lightning into five separate records. Damage that keeps arriving is
        /// therefore treated as the same action until it stops for this long.
        ///
        /// The trade-off is the obvious one: two genuinely separate actions less than this apart
        /// would be counted as one. In a turn-based game where actions are chosen one at a time
        /// that is rare, and a window short enough to never merge them would split every chain.
        /// </summary>
        private static float Window => Mathf.Max(0.1f, ModConfig.ActionWindowSeconds.Value);

        internal static void Clear() => records.Clear();

        /// <summary>
        /// The records for the battle just finished, as plain numbers, and resets them.
        ///
        /// Any burst still open is closed first: the last action of a battle has nothing arriving
        /// after it to close the window, so without this the killing blow would never be counted.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, float>> TakeBattle()
        {
            var result = new Dictionary<string, Dictionary<string, float>>();

            foreach (KeyValuePair<string, Record> pair in records)
            {
                CloseAction(pair.Value);

                var values = new Dictionary<string, float>();
                Put(values, LargestHit, pair.Value.MaxHit);
                Put(values, LargestTaken, pair.Value.MaxTaken);
                Put(values, LargestHeal, pair.Value.MaxHeal);
                Put(values, LargestAction, pair.Value.MaxAction);

                if (values.Count > 0)
                    result[pair.Key] = values;
            }

            records.Clear();
            return result;
        }

        /// <summary>The same records without clearing them, for showing a battle in progress.</summary>
        internal static Dictionary<string, Dictionary<string, float>> Peek()
        {
            var result = new Dictionary<string, Dictionary<string, float>>();

            foreach (KeyValuePair<string, Record> pair in records)
            {
                var values = new Dictionary<string, float>();
                Put(values, LargestHit, pair.Value.MaxHit);
                Put(values, LargestTaken, pair.Value.MaxTaken);
                Put(values, LargestHeal, pair.Value.MaxHeal);

                // The open burst counts towards the live figure, so a chain still landing is
                // visible as it grows rather than appearing only once it finishes.
                Put(values, LargestAction, Mathf.Max(pair.Value.MaxAction, pair.Value.ActionSum));

                if (values.Count > 0)
                    result[pair.Key] = values;
            }

            return result;
        }

        private static void Put(Dictionary<string, float> into, string key, float value)
        {
            if (value > 0.001f)
                into[key] = value;
        }

        /// <summary>A run total of records is the largest of each battle's, not their sum.</summary>
        internal static void FoldInto(Dictionary<string, Dictionary<string, float>> totals,
            Dictionary<string, Dictionary<string, float>> battle)
        {
            foreach (KeyValuePair<string, Dictionary<string, float>> who in battle)
            {
                if (!totals.TryGetValue(who.Key, out Dictionary<string, float> running))
                    totals[who.Key] = running = new Dictionary<string, float>();

                foreach (KeyValuePair<string, float> stat in who.Value)
                {
                    running.TryGetValue(stat.Key, out float best);
                    running[stat.Key] = Mathf.Max(best, stat.Value);
                }
            }
        }

        /// <summary>True for the keys whose run total is a maximum rather than a sum.</summary>
        internal static bool IsRecord(string key)
        {
            return key == LargestHit || key == LargestTaken || key == LargestHeal || key == LargestAction;
        }

        // ------------------------------------------------------------------- recording

        internal static void Observe(Character character, BattleStat stat, float amount)
        {
            if (character == null)
                return;

            Observe(character.CharacterName, stat, amount);
        }

        /// <summary>
        /// The same, by name. Who the damage belongs to and what happened to them are separate
        /// questions, and only the name is needed here - which also makes the records testable
        /// without conjuring a Character.
        /// </summary>
        internal static void Observe(string name, BattleStat stat, float amount)
        {
            if (!ModConfig.TrackRecords.Value || amount <= 0.001f || string.IsNullOrEmpty(name))
                return;

            if (!records.TryGetValue(name, out Record record))
                records[name] = record = new Record();

            switch (stat)
            {
                case BattleStat.DamageDealt:
                    if (amount > record.MaxHit)
                        record.MaxHit = amount;

                    AddToAction(record, amount);
                    break;

                case BattleStat.DamageTaken:
                    if (amount > record.MaxTaken)
                        record.MaxTaken = amount;
                    break;

                case BattleStat.HealingAdministered:
                    if (amount > record.MaxHeal)
                        record.MaxHeal = amount;
                    break;
            }
        }

        private static void AddToAction(Record record, float amount)
        {
            float now = Time.unscaledTime;

            if (now > record.ActionEndsAt)
                CloseAction(record);

            record.ActionSum += amount;
            record.ActionEndsAt = now + Window;
        }

        private static void CloseAction(Record record)
        {
            if (record.ActionSum > record.MaxAction)
                record.MaxAction = record.ActionSum;

            record.ActionSum = 0f;
        }
    }

    /// <summary>
    /// Watches every change the game makes to a battle stat.
    ///
    /// A postfix rather than a prefix: if the game decides not to record something - an enemy's
    /// damage, which it drops on the way in - this should not record it either.
    /// </summary>
    [HarmonyPatch(typeof(StatManager), nameof(StatManager.ModifyBattleStat))]
    internal static class ModifyBattleStatPatch
    {
        [HarmonyPostfix]
        private static void Observed(Character character, BattleStat battleStat, float amount)
        {
            try
            {
                // The same early-outs the game applies, so a summon's damage is not counted twice
                // - once for the summon and again when it is folded into its master.
                if (character == null || character.TeamIndex != 0 || character.IsAI)
                    return;

                ExtraStats.Observe(character, battleStat, amount);
            }
            catch (Exception)
            {
                // This runs inside the damage path; a bad stat is never worth a broken fight.
            }
        }
    }
}
