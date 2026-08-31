using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StatusEffectsMod
{
    /// <summary>
    /// Describes one editable key in the override language, for any UI that wants to offer a
    /// control per field instead of a single text box.
    ///
    /// The member names here are a contract read by reflection, not a compile-time reference: the
    /// mod manager duck-types this object out of the config entry's tags exactly as it already
    /// does for the restart hint. Neither assembly references the other, so this mod still works
    /// with no manager installed and the manager still works with mods that know nothing about it.
    /// Renaming a member breaks that contract silently, so don't.
    /// </summary>
    public sealed class StructuredField
    {
        /// <summary>The key as it appears in the config: <c>duration</c>, <c>stackType</c>.</summary>
        public string Key;

        /// <summary>Human-readable name for the control.</summary>
        public string Label;

        /// <summary>One of "number", "bool", "enum".</summary>
        public string Kind;

        /// <summary>Permitted values, for Kind == "enum".</summary>
        public string[] Options;

        /// <summary>Shown beside the control.</summary>
        public string Help;

        /// <summary>
        /// True when the value scales the shipped one rather than replacing it, and is therefore
        /// written as a multiplier. Only potency works this way: the amounts it scales are game
        /// script expressions, so there is no number to replace, and the applier rejects an
        /// absolute value outright. A UI must offer these as "x N", never as a plain value.
        /// </summary>
        public bool Relative;

        /// <summary>Whether to show this field before the reader asks for the rest.</summary>
        public bool Common;
    }

    /// <summary>
    /// Marks a setting whose value is a list of <c>key=value</c> pairs, and lists the keys.
    /// </summary>
    public sealed class StructuredValue
    {
        /// <summary>
        /// Identifies the encoding, so a reader can refuse anything it was not written for.
        /// "semicolon-key-value" means: pairs of <c>key=value</c> separated by <c>;</c>, where an
        /// absent key means "leave the shipped value alone".
        /// </summary>
        public string Syntax = "semicolon-key-value";

        public StructuredField[] Fields;

        /// <summary>
        /// The shipped value of each field, positionally matching <see cref="Fields"/>, so an
        /// editor can show what a setting currently is rather than an empty box. Empty where the
        /// value is unknown or meaningless. For a Relative field this is the neutral multiplier.
        /// </summary>
        public string[] Vanilla;
    }

    /// <summary>
    /// The single description of what the override language accepts, shared by the parser that
    /// applies it and the schema the UI is built from.
    /// </summary>
    internal static class OverrideSchema
    {
        // Enum members are listed rather than reflected so this file states exactly what the UI
        // will offer. A game update adding a member is then a visible edit here rather than a
        // silent change in what the menus contain.
        private static readonly string[] StackTypes =
        {
            "ReplaceSource", "ReplaceAny", "ReplaceOthers", "Add",
            "AddAndRefresh", "IgnoreAndRefresh", "Ignore"
        };

        private static readonly string[] TickTypes =
        {
            "TargetTurnStart", "TargetTurnEnd", "SourceTurnStart", "SourceTurnEnd"
        };

        private static readonly string[] TurnEvents = { "TurnStart", "TurnEnd" };

        private static StructuredField[] fields;

        /// <summary>Shared across every status: the shape is the same, only the values differ.</summary>
        private static StructuredField[] Fields
        {
            get { return fields ?? (fields = Build()); }
        }

        /// <summary>
        /// The descriptor for one status, carrying that status's own shipped values.
        /// </summary>
        public static StructuredValue For(StatusEntry entry)
        {
            return new StructuredValue
            {
                Fields = Fields,
                Vanilla = VanillaOf(entry.Original)
            };
        }

        private static string[] VanillaOf(StatusSnapshot original)
        {
            var values = new string[Fields.Length];

            for (int i = 0; i < Fields.Length; i++)
                values[i] = Vanilla(Fields[i].Key, original);

            return values;
        }

        private static string Vanilla(string key, StatusSnapshot o)
        {
            switch (key)
            {
                // The shipped duration can be an expression rather than a number; it is handed
                // over as written and the editor shows it as-is rather than pretending otherwise.
                case "duration": return o.Duration ?? string.Empty;

                // Neutral multiplier: potency scales, so "unchanged" is 1 rather than a value.
                case "potency": return "1";

                case "maxStacks": return Num(o.MaxStacks);
                case "stackBonus": return Num(o.StackBonusMultplier);
                case "stackType": return o.StackType.ToString();
                case "stackIgnoreSource": return Bool(o.StackIgnoreSource);
                case "tickType": return o.TickType.ToString();
                case "expireType": return o.ExpireType.ToString();
                case "infinite": return Bool(o.Infinite);
                case "activateImmediately": return Bool(o.ActivateImmediately);
                case "decrementOnTurnEnd": return Bool(o.DecrementOnTurnEnd);
                case "cannotBeDispelled": return Bool(o.CannotBeDispelled);
                case "endOnCrit": return Bool(o.EndOnCrit);
                case "endOnAction": return Bool(o.EndOnAction);
                case "isAura": return Bool(o.IsAura);
                case "auraRadius": return o.AuraRadius.ToString(CultureInfo.InvariantCulture);
                case "auraAllies": return Bool(o.AuraEffectsAllies);
                case "auraEnemies": return Bool(o.AuraEffectsEnemies);
                case "maintainMana": return Num(o.MaintainManaRatio);
                case "groundMovement": return o.GroundMovementMod.ToString(CultureInfo.InvariantCulture);
                case "damageMod": return Num(o.FlatDamageModifier);

                default: return string.Empty;
            }
        }

        private static string Num(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static StructuredField[] Build()
        {
            var list = new List<StructuredField>();

            Number(list, "duration", "Duration",
                "Rounds the status lasts, then scaled by the global duration multipliers.",
                common: true);

            Relative(list, "potency", "Potency",
                "Scales how much the status changes what it touches. 1 leaves it alone. "
                + "Effects that set an absolute value are skipped.", common: true);

            Number(list, "maxStacks", "Max stacks",
                "How many copies can be present at once.", common: true);

            Enum(list, "stackType", "Stack type", StackTypes,
                "What happens when it is applied to something that already has it.", common: true);

            Bool(list, "infinite", "Infinite",
                "Never expires on its own, whatever the duration says.", common: true);

            Number(list, "damageMod", "Flat damage modifier",
                "Flat change to damage. Setting this also switches on the flag that makes the "
                + "game read it.");

            Number(list, "stackBonus", "Stack bonus", "Multiplier applied per additional stack.");

            Bool(list, "stackIgnoreSource", "Stacks ignore source",
                "Treat stacks from different casters as the same stack.");

            Enum(list, "tickType", "Tick type", TickTypes, "Whose turn the status ticks on.");

            Enum(list, "expireType", "Expire type", TurnEvents,
                "Whether it wears off at the start or the end of a turn.");

            Bool(list, "activateImmediately", "Activate immediately",
                "Apply its effect on being cast rather than on the first tick.");

            Bool(list, "decrementOnTurnEnd", "Decrement on turn end",
                "Count down at the end of the turn instead of the start.");

            Bool(list, "cannotBeDispelled", "Cannot be dispelled",
                "Immune to effects that remove statuses.");

            Bool(list, "endOnCrit", "End on crit", "Removed when the bearer is critically hit.");
            Bool(list, "endOnAction", "End on action", "Removed as soon as the bearer acts.");
            Bool(list, "isAura", "Is aura", "Affects characters around the bearer.");

            Number(list, "auraRadius", "Aura radius", "Tiles the aura reaches. Whole numbers.");

            Bool(list, "auraAllies", "Aura affects allies", null);
            Bool(list, "auraEnemies", "Aura affects enemies", null);

            Number(list, "maintainMana", "Maintain mana",
                "Fraction of maximum mana reserved while the status is held.");

            Number(list, "groundMovement", "Ground movement", "Change to movement over ground.");

            return list.ToArray();
        }

        private static void Number(List<StructuredField> into, string key, string label,
            string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key, Label = label, Kind = "number", Help = help, Common = common
            });
        }

        private static void Relative(List<StructuredField> into, string key, string label,
            string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key, Label = label, Kind = "number", Help = help,
                Relative = true, Common = common
            });
        }

        private static void Bool(List<StructuredField> into, string key, string label, string help,
            bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key, Label = label, Kind = "bool", Help = help, Common = common
            });
        }

        private static void Enum(List<StructuredField> into, string key, string label,
            string[] options, string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key, Label = label, Kind = "enum", Options = options.ToArray(),
                Help = help, Common = common
            });
        }
    }
}
