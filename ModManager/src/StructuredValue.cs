using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ModManager
{
    /// <summary>One editable key, read from a mod's own descriptor.</summary>
    internal sealed class StructuredField
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public string Kind = "text";
        public string[] Options = new string[0];
        public string Help = string.Empty;

        /// <summary>Value scales the shipped one rather than replacing it: written as *N.</summary>
        public bool Relative;

        public bool Common;

        public bool IsNumber => Kind == "number";
        public bool IsBool => Kind == "bool";
        public bool IsEnum => Kind == "enum";
    }

    /// <summary>
    /// A setting whose value is a list of <c>key=value</c> pairs, described by the mod that owns
    /// it rather than known here.
    ///
    /// The descriptor is duck-typed out of the config entry's tags: a mod attaches any object with
    /// the right member names and gets a field-by-field editor, without either assembly
    /// referencing the other. That is the same trick the restart hint already uses, and it is what
    /// lets this manager offer a structured editor without knowing anything about specific mods -
    /// the property that makes it survive a game update or a mod it has never seen.
    /// </summary>
    internal sealed class StructuredSchema
    {
        public const string SupportedSyntax = "semicolon-key-value";

        public StructuredField[] Fields = new StructuredField[0];

        /// <summary>Shipped value per field, positionally. Empty where unknown.</summary>
        public string[] Vanilla = new string[0];

        /// <summary>The shipped value for one field, or empty if the mod did not supply one.</summary>
        public string VanillaFor(int index)
        {
            return index >= 0 && index < Vanilla.Length ? Vanilla[index] ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Reads a schema off a config entry's tags, or returns null if there isn't one. Any
        /// malformed descriptor is treated as absent: the setting then draws as a plain text box,
        /// which is always still correct.
        /// </summary>
        public static StructuredSchema From(SettingRow row)
        {
            var tags = row?.Entry?.Description?.Tags;
            if (tags == null)
                return null;

            foreach (object tag in tags)
            {
                if (tag == null)
                    continue;

                try
                {
                    var schema = Read(tag);
                    if (schema != null && schema.Fields.Length > 0)
                        return schema;
                }
                catch
                {
                    // A tag that looks like a descriptor but isn't must not break the panel.
                }
            }

            return null;
        }

        private static StructuredSchema Read(object tag)
        {
            string syntax = Member(tag, "Syntax") as string;
            if (!string.Equals(syntax, SupportedSyntax, StringComparison.Ordinal))
                return null;

            if (!(Member(tag, "Fields") is IEnumerable list))
                return null;

            var fields = new List<StructuredField>();

            foreach (object item in list)
            {
                if (item == null)
                    continue;

                string key = Member(item, "Key") as string;
                if (string.IsNullOrEmpty(key))
                    continue;

                fields.Add(new StructuredField
                {
                    Key = key,
                    Label = Member(item, "Label") as string ?? key,
                    Kind = Member(item, "Kind") as string ?? "text",
                    Options = Member(item, "Options") as string[] ?? new string[0],
                    Help = Member(item, "Help") as string ?? string.Empty,
                    Relative = Member(item, "Relative") as bool? ?? false,
                    Common = Member(item, "Common") as bool? ?? false
                });
            }

            return new StructuredSchema
            {
                Fields = fields.ToArray(),
                Vanilla = Member(tag, "Vanilla") as string[] ?? new string[0]
            };
        }

        private static object Member(object target, string name)
        {
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var property = type.GetProperty(name, flags);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);

            var field = type.GetField(name, flags);
            return field?.GetValue(target);
        }
    }

    /// <summary>
    /// The parsed contents of one structured value: an ordered list of key/value tokens.
    ///
    /// Order and unrecognised keys are both preserved. Someone may have hand-written a key this
    /// build has never heard of, or an expression the editor cannot represent, and rewriting the
    /// setting through this class must not throw their work away.
    /// </summary>
    internal sealed class StructuredTokens
    {
        private readonly List<KeyValuePair<string, string>> tokens =
            new List<KeyValuePair<string, string>>();

        public static StructuredTokens Parse(string text)
        {
            var result = new StructuredTokens();

            if (string.IsNullOrEmpty(text))
                return result;

            foreach (string part in text.Split(';'))
            {
                string token = part.Trim();
                if (token.Length == 0)
                    continue;

                int split = token.IndexOf('=');

                if (split <= 0)
                {
                    // Keeps malformed input visible rather than silently dropping it.
                    result.tokens.Add(new KeyValuePair<string, string>(token, null));
                    continue;
                }

                result.tokens.Add(new KeyValuePair<string, string>(
                    token.Substring(0, split).Trim(), token.Substring(split + 1).Trim()));
            }

            return result;
        }

        public bool Has(string key)
        {
            return IndexOf(key) >= 0;
        }

        public string Get(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : tokens[i].Value;
        }

        public void Set(string key, string value)
        {
            int i = IndexOf(key);

            if (i < 0)
                tokens.Add(new KeyValuePair<string, string>(key, value));
            else
                tokens[i] = new KeyValuePair<string, string>(key, value);
        }

        public void Remove(string key)
        {
            int i = IndexOf(key);
            if (i >= 0)
                tokens.RemoveAt(i);
        }

        /// <summary>Keys present that the schema does not describe, so the UI can say so.</summary>
        public List<string> UnknownKeys(StructuredSchema schema)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in schema.Fields)
                known.Add(field.Key);

            var unknown = new List<string>();

            foreach (var token in tokens)
            {
                if (!known.Contains(token.Key))
                    unknown.Add(token.Key);
            }

            return unknown;
        }

        private int IndexOf(string key)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public override string ToString()
        {
            var text = new StringBuilder();

            foreach (var token in tokens)
            {
                if (text.Length > 0)
                    text.Append("; ");

                text.Append(token.Key);

                if (token.Value != null)
                    text.Append('=').Append(token.Value);
            }

            return text.ToString();
        }
    }

    /// <summary>How one field's value relates to the value the game shipped with.</summary>
    internal enum ValueMode
    {
        /// <summary>Absent from the setting: the shipped value is left alone.</summary>
        Vanilla,
        Set,
        Multiply,
        Add,
        Expression
    }

    internal static class ValueModes
    {
        /// <summary>Splits a raw right-hand side into its operator and its operand.</summary>
        public static ValueMode Read(string raw, out string operand)
        {
            operand = string.Empty;

            if (raw == null)
                return ValueMode.Vanilla;

            string text = raw.Trim();
            if (text.Length == 0)
                return ValueMode.Vanilla;

            if (text.StartsWith("expr:", StringComparison.OrdinalIgnoreCase))
            {
                operand = text.Substring(5).Trim();
                return ValueMode.Expression;
            }

            char first = text[0];

            if (first == '*' || first == 'x' || first == 'X')
            {
                operand = text.Substring(1).Trim();
                return ValueMode.Multiply;
            }

            // The sign is part of the operand for Add, so "+2" and "-1" both round-trip.
            if (first == '+' || first == '-')
            {
                operand = text;
                return ValueMode.Add;
            }

            operand = text;
            return ValueMode.Set;
        }

        public static string Write(ValueMode mode, string operand)
        {
            operand = (operand ?? string.Empty).Trim();

            switch (mode)
            {
                case ValueMode.Multiply:
                    return "*" + operand;

                case ValueMode.Add:
                    // Written with an explicit sign, so a bare "2" typed into an Add box means +2
                    // rather than silently becoming a replacement.
                    if (operand.Length == 0)
                        return "+0";

                    return operand[0] == '+' || operand[0] == '-' ? operand : "+" + operand;

                case ValueMode.Expression:
                    return "expr:" + operand;

                default:
                    return operand;
            }
        }

        public static string Label(ValueMode mode)
        {
            switch (mode)
            {
                case ValueMode.Vanilla: return "vanilla";
                case ValueMode.Set: return "set to";
                case ValueMode.Multiply: return "multiply";
                case ValueMode.Add: return "add";
                default: return "expression";
            }
        }

        public static bool LooksNumeric(string operand)
        {
            return float.TryParse((operand ?? string.Empty).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
    }
}
