using System;
using Burst2Flame;

namespace StatusEffectsMod
{
    /// <summary>
    /// The shipped values of every field this mod is willing to write, captured before the
    /// first edit.
    ///
    /// This exists because the mod edits ScriptableObjects in place. Those objects live for the
    /// whole process, so without a snapshot a hot reload would compound onto already-modified
    /// values and "*2" would mean 2x, then 4x, then 8x across successive saves. Every reload
    /// restores from here first, then applies the config fresh.
    ///
    /// Nothing is written to disk: Unity only serialises ScriptableObject edits in the editor,
    /// so uninstalling the mod restores the game exactly.
    /// </summary>
    internal sealed class StatusSnapshot
    {
        public readonly string Duration;
        public readonly bool Infinite;
        public readonly float MaxStacks;
        public readonly float StackBonusMultplier;
        public readonly StackType StackType;
        public readonly bool StackIgnoreSource;
        public readonly TickType TickType;
        public readonly TurnEvent ExpireType;
        public readonly bool ActivateImmediately;
        public readonly bool DecrementOnTurnEnd;
        public readonly bool CannotBeDispelled;
        public readonly bool EndOnCrit;
        public readonly bool EndOnAction;
        public readonly bool IsAura;
        public readonly int AuraRadius;
        public readonly bool AuraEffectsAllies;
        public readonly bool AuraEffectsEnemies;
        public readonly float MaintainManaRatio;
        public readonly int GroundMovementMod;
        public readonly bool UseFlatDamageModifier;
        public readonly float FlatDamageModifier;

        /// <summary>
        /// Original <c>Amount</c> expression per entry of <c>AttributeEffects</c>, positionally.
        /// Null when the status has no attribute effects.
        /// </summary>
        public readonly string[] AttributeAmounts;

        public StatusSnapshot(ActionStatusInfo status)
        {
            Duration = status.Duration;
            Infinite = status.Infinite;
            MaxStacks = status.MaxStacks;
            StackBonusMultplier = status.StackBonusMultplier;
            StackType = status.StackType;
            StackIgnoreSource = status.StackIgnoreSource;
            TickType = status.TickType;
            ExpireType = status.ExpireType;
            ActivateImmediately = status.ActivateImmediately;
            DecrementOnTurnEnd = status.DecrementOnTurnEnd;
            CannotBeDispelled = status.CannotBeDispelled;
            EndOnCrit = status.EndOnCrit;
            EndOnAction = status.EndOnAction;
            IsAura = status.IsAura;
            AuraRadius = status.AuraRadius;
            AuraEffectsAllies = status.AuraEffectsAllies;
            AuraEffectsEnemies = status.AuraEffectsEnemies;
            MaintainManaRatio = status.MaintainManaRatio;
            GroundMovementMod = status.GroundMovementMod;
            UseFlatDamageModifier = status.UseFlatDamageModifier;
            FlatDamageModifier = status.FlatDamageModifier;

            CharacterEffectInfo[] effects = status.AttributeEffects;
            if (effects == null)
                return;

            AttributeAmounts = new string[effects.Length];
            for (int i = 0; i < effects.Length; i++)
                AttributeAmounts[i] = effects[i]?.Amount;
        }

        /// <summary>Puts the status back exactly as the game shipped it.</summary>
        public void RestoreTo(ActionStatusInfo status)
        {
            status.Duration = Duration;
            status.Infinite = Infinite;
            status.MaxStacks = MaxStacks;
            status.StackBonusMultplier = StackBonusMultplier;
            status.StackType = StackType;
            status.StackIgnoreSource = StackIgnoreSource;
            status.TickType = TickType;
            status.ExpireType = ExpireType;
            status.ActivateImmediately = ActivateImmediately;
            status.DecrementOnTurnEnd = DecrementOnTurnEnd;
            status.CannotBeDispelled = CannotBeDispelled;
            status.EndOnCrit = EndOnCrit;
            status.EndOnAction = EndOnAction;
            status.IsAura = IsAura;
            status.AuraRadius = AuraRadius;
            status.AuraEffectsAllies = AuraEffectsAllies;
            status.AuraEffectsEnemies = AuraEffectsEnemies;
            status.MaintainManaRatio = MaintainManaRatio;
            status.GroundMovementMod = GroundMovementMod;
            status.UseFlatDamageModifier = UseFlatDamageModifier;
            status.FlatDamageModifier = FlatDamageModifier;

            if (AttributeAmounts == null)
                return;

            CharacterEffectInfo[] effects = status.AttributeEffects;
            if (effects == null)
                return;

            // Length is compared rather than assumed: another mod could in principle have
            // replaced the array, and writing past its end would be worse than skipping.
            int count = Math.Min(effects.Length, AttributeAmounts.Length);
            for (int i = 0; i < count; i++)
            {
                if (effects[i] != null)
                    effects[i].Amount = AttributeAmounts[i];
            }
        }

        /// <summary>
        /// The shipped duration as a number, for resolving <c>*</c> and <c>+</c> overrides.
        /// Returns false when the shipped value is an expression rather than a literal, in
        /// which case relative arithmetic has no meaningful base to work from.
        /// </summary>
        public bool TryGetNumericDuration(out float value)
        {
            return float.TryParse(Duration, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
