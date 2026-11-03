using System.Collections.Generic;
using System.Linq;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Figures worked out from what the game already counts.
    ///
    /// None of this needs a hook. The game records each kind of mitigation separately and never
    /// adds them up, and it records each character's damage without ever expressing it as a share
    /// of the party's - so these are arithmetic over numbers already in hand, computed when they
    /// are shown rather than stored.
    /// </summary>
    internal static class DerivedStats
    {
        /// <summary>The five ways the game reduces incoming damage, in the order it applies them.</summary>
        internal static readonly string[] BlockKinds =
        {
            "BlockedByArmor",
            "BlockedByMagicArmor",
            "BlockedByDamageReduction",
            "BlockedByResistance",
            "BlockedByShields",
        };

        internal const string TotalBlocked = "DamageBlocked";
        internal const string BlockedShare = "BlockedShareOfIncoming";
        internal const string PartyShare = "ShareOfPartyDamage";
        internal const string DealtPerTaken = "DamageDealtPerTaken";

        /// <summary>Every block type added together. Negative contributions are kept.</summary>
        internal static float Blocked(Dictionary<string, float> stats)
        {
            float total = 0f;

            foreach (string kind in BlockKinds)
            {
                if (stats.TryGetValue(kind, out float value))
                    total += value;
            }

            return total;
        }

        /// <summary>
        /// The rows to show under the raw numbers, for one character.
        ///
        /// <paramref name="partyDamage"/> is the whole party's damage, needed for the share; pass
        /// zero when it is not known and that row is left out rather than divided by nothing.
        /// </summary>
        internal static Dictionary<string, float> For(Dictionary<string, float> stats, float partyDamage)
        {
            var derived = new Dictionary<string, float>();
            if (stats == null || stats.Count == 0)
                return derived;

            float blocked = Blocked(stats);
            if (System.Math.Abs(blocked) > 0.001f)
                derived[TotalBlocked] = blocked;

            stats.TryGetValue("DamageTaken", out float taken);
            stats.TryGetValue("DamageDealt", out float dealt);

            // Against what would have landed without mitigation, which is what was taken plus what
            // was stopped - not against what was taken, which would read over 100% for a good tank.
            float incoming = taken + blocked;
            if (incoming > 0.001f && blocked > 0.001f)
                derived[BlockedShare] = blocked / incoming * 100f;

            if (partyDamage > 0.001f && dealt > 0.001f)
                derived[PartyShare] = dealt / partyDamage * 100f;

            if (taken > 0.001f && dealt > 0.001f)
                derived[DealtPerTaken] = dealt / taken;

            return derived;
        }

        /// <summary>Everyone's damage added up, for the share-of-party row.</summary>
        internal static float PartyDamage(Dictionary<string, Dictionary<string, float>> everyone)
        {
            if (everyone == null)
                return 0f;

            return everyone.Values.Sum(x => x.TryGetValue("DamageDealt", out float value) ? value : 0f);
        }

        /// <summary>Whether a value reads as a percentage rather than a count.</summary>
        internal static bool IsPercent(string key) => key == BlockedShare || key == PartyShare;

        /// <summary>Whether a value reads as a ratio rather than a count.</summary>
        internal static bool IsRatio(string key) => key == DealtPerTaken;

        /// <summary>Readable names, for the ones the automatic spacing gets wrong.</summary>
        internal static string Label(string key)
        {
            switch (key)
            {
                case TotalBlocked: return "Damage Blocked (total)";
                case BlockedShare: return "Blocked Share of Incoming";
                case PartyShare: return "Share of Party Damage";
                case DealtPerTaken: return "Damage Dealt per Taken";
                case ExtraStats.LargestHit: return "Largest Single Attack Dealt";
                case ExtraStats.LargestTaken: return "Largest Attack Received";
                case ExtraStats.LargestHeal: return "Largest Single Heal";
                case ExtraStats.LargestAction: return "Largest Single Action";
                default: return null;
            }
        }
    }
}
