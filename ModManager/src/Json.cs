using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ModManager
{
    /// <summary>
    /// A small recursive-descent JSON reader, used instead of UnityEngine.JsonUtility.
    ///
    /// JsonUtility looked like the dependency-free choice, but it would not map the manifest's
    /// nested types: against valid JSON it returned an object with the int field populated and
    /// every nested class and array left null, with no exception to say so. Making the types
    /// public did not change it. The alternative on hand was the game's own Newtonsoft, which
    /// would make this assembly depend on a game-shipped DLL - and referencing no game assembly
    /// at all is the property that keeps this mod from breaking when the game updates.
    ///
    /// So the manifest is parsed here instead. The schema is small and fixed, the mapping in
    /// <see cref="Manifest.Parse"/> is explicit rather than reflective, and both can be tested
    /// outside the game.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty document.");

            int index = 0;
            object value = ParseValue(text, ref index);

            SkipWhitespace(text, ref index);
            if (index < text.Length)
                throw new FormatException("Unexpected trailing content at position " + index + ".");

            return value;
        }

        /// <summary>Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;.</summary>
        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);

            if (index >= text.Length)
                throw new FormatException("Unexpected end of document.");

            char c = text[index];

            switch (c)
            {
                case '{': return ParseObject(text, ref index);
                case '[': return ParseArray(text, ref index);
                case '"': return ParseString(text, ref index);
                case 't': Expect(text, ref index, "true"); return true;
                case 'f': Expect(text, ref index, "false"); return false;
                case 'n': Expect(text, ref index, "null"); return null;
                default: return ParseNumber(text, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);

            index++; // '{'
            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != '"')
                    throw new FormatException("Expected a key at position " + index + ".");

                string key = ParseString(text, ref index);

                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                    throw new FormatException("Expected ':' at position " + index + ".");

                index++;
                result[key] = ParseValue(text, ref index);

                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("Unterminated object.");

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return result;
                }

                throw new FormatException("Expected ',' or '}' at position " + index + ".");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var result = new List<object>();

            index++; // '['
            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(text, ref index));

                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("Unterminated array.");

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return result;
                }

                throw new FormatException("Expected ',' or ']' at position " + index + ".");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++; // opening quote

            var builder = new StringBuilder();

            while (true)
            {
                if (index >= text.Length)
                    throw new FormatException("Unterminated string.");

                char c = text[index++];

                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                    throw new FormatException("Unterminated escape sequence.");

                char escape = text[index++];

                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;

                    case 'u':
                        if (index + 4 > text.Length)
                            throw new FormatException("Truncated \\u escape.");

                        builder.Append((char)ushort.Parse(
                            text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;

                    default:
                        throw new FormatException("Unknown escape '\\" + escape + "'.");
                }
            }
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;

            while (index < text.Length && "+-0123456789.eE".IndexOf(text[index]) >= 0)
                index++;

            if (start == index)
                throw new FormatException("Expected a value at position " + start + ".");

            string raw = text.Substring(start, index - start);

            // Integers stay integers so a version or count does not arrive as 1.0.
            if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0
                && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            {
                return integer;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return number;

            throw new FormatException("Invalid number '" + raw + "'.");
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length
                || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Expected '" + literal + "' at position " + index + ".");
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }

        // --- Typed access -------------------------------------------------------------------
        // A missing or wrong-typed field yields the default rather than throwing: a manifest
        // written by a newer release should degrade to "cannot see that field" rather than
        // failing the whole update check.

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static string GetString(Dictionary<string, object> source, string key)
        {
            if (source != null && source.TryGetValue(key, out object value) && value != null)
                return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

            return string.Empty;
        }

        public static int GetInt(Dictionary<string, object> source, string key)
        {
            if (source != null && source.TryGetValue(key, out object value) && value != null)
            {
                try
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return 0;
                }
            }

            return 0;
        }

        public static bool GetBool(Dictionary<string, object> source, string key)
        {
            return source != null
                && source.TryGetValue(key, out object value)
                && value is bool flag
                && flag;
        }
    }
}
