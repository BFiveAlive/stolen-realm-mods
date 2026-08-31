using System;
using System.Collections.Generic;
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

        /// <summary>Shown under or beside the control.</summary>
        public string Help;

        /// <summary>
        /// Whether the value may be relative (<c>*2</c>, <c>+1</c>) or an expression rather than
        /// an outright replacement. False for anything that is not a number.
        /// </summary>
        public bool AllowOperators;

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
    }

    /// <summary>
    /// The single description of what the override language accepts, shared by the parser that
    /// applies it and the schema the UI is built from.
    /// </summary>
    internal static class OverrideSchema
    {
        // Enum members are listed rather than reflected so this file states exactly what the UI
        // will offer. A game update adding a member is then a visible edit here rather than a
        // silent change in what the dropdowns contain.
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

        private static StructuredValue cached;

        public static StructuredValue Descriptor
        {
            get { return cached ?? (cached = Build()); }
        }

        private static StructuredValue Build()
        {
            var fields = new List<StructuredField>();

            Number(fields, "duration", "Duration",
                "Rounds the status lasts. Scaled afterwards by the global duration multipliers.",
                common: true);

            Number(fields, "potency", "Potency",
                "Scales how much the status changes the attributes it touches. Effects that set "
                + "an absolute value are skipped.", common: true);

            Number(fields, "maxStacks", "Max stacks",
                "How many copies can be present at once.", common: true);

            Enum(fields, "stackType", "Stack type", StackTypes,
                "What happens when the status is applied to something that already has it.",
                common: true);

            Bool(fields, "infinite", "Infinite",
                "Never expires on its own, whatever the duration says.", common: true);

            Number(fields, "damageMod", "Flat damage modifier",
                "Flat change to damage. Setting this also switches on the flag that makes the "
                + "game read it.");

            Number(fields, "stackBonus", "Stack bonus",
                "Multiplier applied per additional stack.");

            Bool(fields, "stackIgnoreSource", "Stacks ignore source",
                "Treat stacks from different casters as the same stack.");

            Enum(fields, "tickType", "Tick type", TickTypes,
                "Whose turn the status ticks on.");

            Enum(fields, "expireType", "Expire type", TurnEvents,
                "Whether it wears off at the start or the end of a turn.");

            Bool(fields, "activateImmediately", "Activate immediately",
                "Apply its effect on being cast rather than on the first tick.");

            Bool(fields, "decrementOnTurnEnd", "Decrement on turn end",
                "Count down at the end of the turn instead of the start.");

            Bool(fields, "cannotBeDispelled", "Cannot be dispelled",
                "Immune to effects that remove statuses.");

            Bool(fields, "endOnCrit", "End on crit",
                "Removed when the bearer is critically hit.");

            Bool(fields, "endOnAction", "End on action",
                "Removed as soon as the bearer acts.");

            Bool(fields, "isAura", "Is aura", "Affects characters around the bearer.");

            Number(fields, "auraRadius", "Aura radius", "Tiles the aura reaches. Whole numbers.");

            Bool(fields, "auraAllies", "Aura affects allies", null);
            Bool(fields, "auraEnemies", "Aura affects enemies", null);

            Number(fields, "maintainMana", "Maintain mana",
                "Fraction of maximum mana reserved while the status is held.");

            Number(fields, "groundMovement", "Ground movement", "Change to movement over ground.");

            return new StructuredValue { Fields = fields.ToArray() };
        }

        private static void Number(List<StructuredField> into, string key, string label,
            string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key,
                Label = label,
                Kind = "number",
                Help = help,
                AllowOperators = true,
                Common = common
            });
        }

        private static void Bool(List<StructuredField> into, string key, string label,
            string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key,
                Label = label,
                Kind = "bool",
                Help = help,
                AllowOperators = false,
                Common = common
            });
        }

        private static void Enum(List<StructuredField> into, string key, string label,
            string[] options, string help, bool common = false)
        {
            into.Add(new StructuredField
            {
                Key = key,
                Label = label,
                Kind = "enum",
                Options = options.ToArray(),
                Help = help,
                AllowOperators = false,
                Common = common
            });
        }
    }
}
