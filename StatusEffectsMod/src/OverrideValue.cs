using System;
using System.Globalization;

namespace StatusEffectsMod
{
    /// <summary>
    /// How one override token relates to the value the game shipped with.
    /// </summary>
    internal enum OverrideOp
    {
        /// <summary>Replace the vanilla value outright: <c>duration=4</c>.</summary>
        Set,

        /// <summary>Scale the vanilla value: <c>duration=*2</c>.</summary>
        Multiply,

        /// <summary>Offset the vanilla value: <c>duration=+2</c> or <c>duration=-1</c>.</summary>
        Add,

        /// <summary>Substitute a raw game script expression: <c>duration=expr:Source.Level / 2</c>.</summary>
        Expression,
    }

    /// <summary>
    /// One parsed right-hand side from the config's override language.
    ///
    /// Everything is resolved against the ORIGINAL shipped value rather than the current one,
    /// so re-applying after a hot reload is idempotent: <c>*2</c> always means "twice vanilla",
    /// never "twice whatever the last edit left behind".
    /// </summary>
    internal sealed class OverrideValue
    {
        public OverrideOp Op { get; private set; }

        /// <summary>Operand for Set / Multiply / Add. Unused for Expression.</summary>
        public float Number { get; private set; }

        /// <summary>Raw text for Expression, and the source text for bool/enum reads.</summary>
        public string Text { get; private set; }

        private OverrideValue() { }

        /// <summary>
        /// Parses one right-hand side. Returns null and fills <paramref name="error"/> rather
        /// than throwing, because every one of these comes from a hand-edited text file.
        /// </summary>
        public static OverrideValue Parse(string raw, out string error)
        {
            error = null;

            if (raw == null)
            {
                error = "missing value";
                return null;
            }

            string text = raw.Trim();
            if (text.Length == 0)
            {
                error = "empty value";
                return null;
            }

            // expr: hands the rest of the token to the game's own expression evaluator
            // untouched. Deliberately case-insensitive and whitespace-tolerant.
            if (text.StartsWith("expr:", StringComparison.OrdinalIgnoreCase))
            {
                string expression = text.Substring(5).Trim();
                if (expression.Length == 0)
                {
                    error = "expr: with nothing after it";
                    return null;
                }

                return new OverrideValue { Op = OverrideOp.Expression, Text = expression };
            }

            if (text[0] == '*' || text[0] == 'x' || text[0] == 'X')
            {
                return Numeric(OverrideOp.Multiply, text.Substring(1), text, out error);
            }

            // A leading + is unambiguous. A leading - is not: "-1" could mean "subtract one"
            // or "set to minus one". Add is the useful reading for the fields this touches
            // (durations, radii, stack counts), and Set of a negative is available as "=-1".
            if (text[0] == '+' || text[0] == '-')
            {
                string digits = text[0] == '-' ? text : text.Substring(1);
                OverrideValue add = Numeric(OverrideOp.Add, digits, text, out error);
                return add;
            }

            if (text[0] == '=')
            {
                return Numeric(OverrideOp.Set, text.Substring(1), text, out error);
            }

            // Bare token: a number is a Set, anything else is kept as text for the bool and
            // enum readers to interpret.
            if (TryParseNumber(text, out float parsed))
                return new OverrideValue { Op = OverrideOp.Set, Number = parsed, Text = text };

            return new OverrideValue { Op = OverrideOp.Set, Number = 0f, Text = text };
        }

        private static OverrideValue Numeric(OverrideOp op, string digits, string original, out string error)
        {
            error = null;

            if (!TryParseNumber(digits.Trim(), out float value))
            {
                error = $"'{original}' is not a number";
                return null;
            }

            return new OverrideValue { Op = op, Number = value, Text = original };
        }

        /// <summary>
        /// Invariant-culture parse. The config file is written by BepInEx with invariant
        /// formatting, but a user on a comma-decimal locale may well type "1,5" by hand, so
        /// that is accepted too rather than silently parsing as something else.
        /// </summary>
        private static bool TryParseNumber(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            return float.TryParse(text.Replace(',', '.'), NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Resolves this override against the value the game shipped with.</summary>
        public float Apply(float original)
        {
            switch (Op)
            {
                case OverrideOp.Multiply: return original * Number;
                case OverrideOp.Add: return original + Number;
                default: return Number;
            }
        }

        /// <summary>
        /// Reads the token as a boolean. Accepts true/false, yes/no, on/off and 1/0 so the
        /// config reads naturally whichever convention the user reaches for.
        /// </summary>
        public bool AsBool(bool original, out string error)
        {
            error = null;
            string text = (Text ?? string.Empty).Trim();

            switch (text.ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    return true;

                case "false":
                case "no":
                case "off":
                case "0":
                    return false;

                case "toggle":
                    return !original;
            }

            error = $"'{text}' is not true/false";
            return original;
        }

        /// <summary>Reads the token as an enum member, by name or by underlying number.</summary>
        public T AsEnum<T>(T original, out string error) where T : struct
        {
            error = null;
            string text = (Text ?? string.Empty).Trim();

            if (Enum.TryParse(text, ignoreCase: true, result: out T parsed) && Enum.IsDefined(typeof(T), parsed))
                return parsed;

            error = $"'{text}' is not one of: {string.Join(", ", Enum.GetNames(typeof(T)))}";
            return original;
        }
    }
}
