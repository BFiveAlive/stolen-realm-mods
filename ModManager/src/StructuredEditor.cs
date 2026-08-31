using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Draws a control per field for a setting whose value is a list of <c>key=value</c> pairs.
    ///
    /// Every field starts showing the value the game actually ships with, read from the owning
    /// mod's descriptor, so the panel reads as the status itself rather than as a blank form. A
    /// field is overridden by typing a different value and returned to the shipped one with the
    /// reset beside it; the encoding is never something to get right by hand, and anything limited
    /// to a fixed set of words is a list of buttons, so a typo is not among the possible outcomes.
    ///
    /// A value the controls cannot represent - a hand-written multiplier or expression - is shown
    /// as raw text and left exactly as written, because an editor that does not understand
    /// something must not be the reason it disappears.
    /// </summary>
    internal static class StructuredEditor
    {
        private const float LabelWidth = 150f;
        private const float ResetWidth = 62f;
        private const float LineHeight = 32f;
        private const float NoteHeight = 20f;
        private const float Gap = 8f;

        private static bool showAll;

        private static readonly Dictionary<string, string> Buffers = new Dictionary<string, string>();

        public static void Reset()
        {
            Buffers.Clear();
        }

        // --- measuring ----------------------------------------------------------------------

        public static float Height(SettingRow row, StructuredSchema schema, float width)
        {
            var tokens = Tokens(row);
            float height = 26f;

            for (int i = 0; i < schema.Fields.Length; i++)
            {
                var field = schema.Fields[i];
                if (!showAll && !field.Common)
                    continue;

                height += FieldHeight(field, tokens) + Gap;
            }

            if (tokens.UnknownKeys(schema).Count > 0)
                height += 46f;

            return height + 44f;
        }

        private static float FieldHeight(StructuredField field, StructuredTokens tokens)
        {
            float height = LineHeight;

            // An enum's options sit under the label, on their own rows, so every legal value is
            // visible and clickable. A menu would have to paint over the fields below it, which
            // inside a scroll view means the same split-input handling the settings list needs -
            // a lot of machinery to hide four words.
            if (field.IsEnum)
                height += OptionRows(field) * 28f + 4f;

            if (tokens.Has(field.Key))
                height += NoteHeight;

            return height;
        }

        private static int OptionRows(StructuredField field)
        {
            int columns = OptionColumns(field);
            return (field.Options.Length + columns - 1) / columns;
        }

        private static int OptionColumns(StructuredField field)
        {
            return field.Options.Length > 4 ? 2 : 1;
        }

        // --- drawing ------------------------------------------------------------------------

        public static float Draw(Rect area, SettingRow row, StructuredSchema schema)
        {
            var tokens = Tokens(row);
            float y = area.y;

            Skin.Text(new Rect(area.x, y, area.width, 20f), "FIELDS", Skin.SmallCaps, Skin.InkDim);
            y += 26f;

            for (int i = 0; i < schema.Fields.Length; i++)
            {
                var field = schema.Fields[i];
                if (!showAll && !field.Common)
                    continue;

                float height = FieldHeight(field, tokens);
                DrawField(new Rect(area.x, y, area.width, height), row, field,
                    schema.VanillaFor(i), tokens);

                y += height + Gap;
            }

            var unknown = tokens.UnknownKeys(schema);
            if (unknown.Count > 0)
            {
                Skin.Text(new Rect(area.x, y, area.width, 40f),
                    "Also set by hand, and left untouched: " + string.Join(", ", unknown.ToArray()),
                    Skin.Muted, Skin.AccentDim);
                y += 46f;
            }

            if (GUI.Button(new Rect(area.x, y, 200f, 30f),
                    showAll ? "Show fewer fields" : "Show all fields", Skin.ButtonQuiet))
            {
                showAll = !showAll;
            }

            return y + 44f;
        }

        private static void DrawField(Rect rect, SettingRow row, StructuredField field,
            string vanilla, StructuredTokens tokens)
        {
            string raw = tokens.Get(field.Key);
            bool overridden = raw != null;

            var label = new Rect(rect.x, rect.y, LabelWidth, LineHeight);
            Skin.Text(label, field.Label, overridden ? Skin.RowNameBold : Skin.RowName,
                overridden ? Skin.Accent : Skin.Ink);

            var reset = new Rect(rect.xMax - ResetWidth, rect.y + 2f, ResetWidth, LineHeight - 4f);

            GUI.enabled = overridden;
            if (GUI.Button(reset, "reset", Skin.ButtonQuiet))
            {
                tokens.Remove(field.Key);
                Buffers.Remove(BufferKey(row, field));
                Commit(row, tokens);
            }
            GUI.enabled = true;

            float controlX = rect.x + LabelWidth + Gap;
            float controlWidth = reset.x - Gap - controlX;

            // The shipped value is what the control shows until something overrides it, so the
            // panel describes the status as it stands rather than asking for it from nothing.
            string shown = overridden ? raw : vanilla;

            if (field.IsEnum)
                DrawEnum(rect, controlX, controlWidth, row, field, tokens, shown);
            else if (field.IsBool)
                DrawBool(new Rect(controlX, rect.y + 2f, controlWidth, LineHeight - 4f),
                    row, field, tokens, vanilla, shown);
            else
                DrawNumber(new Rect(controlX, rect.y + 2f, controlWidth, LineHeight - 4f),
                    row, field, tokens, vanilla, shown, overridden);

            if (overridden)
            {
                Skin.Text(new Rect(rect.x, rect.yMax - NoteHeight, rect.width, NoteHeight),
                    "shipped value: " + (string.IsNullOrEmpty(vanilla) ? "unset" : vanilla),
                    Skin.Value, Skin.InkDim);
            }
        }

        /// <summary>
        /// Numbers are edited as numbers. The override language also accepts a multiplier or an
        /// expression, but for a single status those say nothing a plain value cannot - the
        /// shipped number is on screen - so the box stays a box. A value already written in one of
        /// those forms is detected and left as raw text rather than being flattened.
        /// </summary>
        private static void DrawNumber(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, string vanilla, string shown, bool overridden)
        {
            if (field.Relative)
            {
                // Potency only ever scales, so the box is the multiplier and 1 means unchanged.
                var times = new Rect(rect.x, rect.y, 18f, rect.height);
                Skin.Text(times, "x", Skin.RowName, Skin.InkMuted);

                DrawValueBox(new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height),
                    row, field, tokens, "1", StripMultiplier(shown), Multiplier);
                return;
            }

            if (overridden && !IsPlainNumber(shown))
            {
                // Written by hand as "*2" or "expr:...". Editable, but as the text it is.
                DrawValueBox(rect, row, field, tokens, vanilla, shown, Verbatim);
                return;
            }

            DrawValueBox(rect, row, field, tokens, vanilla, shown, Verbatim);
        }

        private delegate string Encoder(string typed);

        private static string Verbatim(string typed)
        {
            return typed;
        }

        private static string Multiplier(string typed)
        {
            return "*" + typed;
        }

        private static string StripMultiplier(string shown)
        {
            if (string.IsNullOrEmpty(shown))
                return "1";

            string text = shown.Trim();
            char first = text[0];

            return first == '*' || first == 'x' || first == 'X' ? text.Substring(1).Trim() : text;
        }

        private static void DrawValueBox(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, string vanilla, string shown, Encoder encode)
        {
            string id = BufferKey(row, field);

            if (!Buffers.TryGetValue(id, out string buffer) || buffer == null)
                buffer = shown ?? string.Empty;

            GUI.SetNextControlName(id);
            string typed = GUI.TextField(rect, buffer, Skin.Field);

            if (typed == buffer)
                return;

            Buffers[id] = typed;

            // Typing the shipped value back is the same statement as resetting, so the key is
            // dropped rather than written - a config file that only lists real changes.
            if (SameAsVanilla(typed, vanilla, encode))
                tokens.Remove(field.Key);
            else
                tokens.Set(field.Key, encode(typed.Trim()));

            Commit(row, tokens);
        }

        private static bool SameAsVanilla(string typed, string vanilla, Encoder encode)
        {
            string text = (typed ?? string.Empty).Trim();

            if (text.Length == 0)
                return true;

            // Compared numerically where both sides are numbers, so "3" and "3.0" agree.
            if (TryNumber(text, out float a) && TryNumber(vanilla, out float b))
                return Mathf.Approximately(a, b);

            return string.Equals(text, (vanilla ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        private static void DrawBool(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, string vanilla, string shown)
        {
            bool value = string.Equals(shown, "true", StringComparison.OrdinalIgnoreCase);

            int chosen = GUI.SelectionGrid(rect, value ? 0 : 1, TrueFalse, 2, Skin.ButtonQuiet);
            bool next = chosen == 0;

            if (next == value)
                return;

            string written = next ? "true" : "false";

            if (string.Equals(written, (vanilla ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                tokens.Remove(field.Key);
            else
                tokens.Set(field.Key, written);

            Commit(row, tokens);
        }

        private static readonly string[] TrueFalse = { "true", "false" };

        private static void DrawEnum(Rect rect, float controlX, float controlWidth,
            SettingRow row, StructuredField field, StructuredTokens tokens, string shown)
        {
            Skin.Text(new Rect(controlX, rect.y + 2f, controlWidth, LineHeight - 4f),
                string.IsNullOrEmpty(shown) ? "unset" : shown, Skin.Value, Skin.InkMuted);

            int columns = OptionColumns(field);
            int rows = OptionRows(field);

            var grid = new Rect(rect.x, rect.y + LineHeight + 2f, rect.width, rows * 28f);
            int index = Array.IndexOf(field.Options, shown);

            int chosen = GUI.SelectionGrid(grid, index, field.Options, columns, Skin.ButtonQuiet);

            if (chosen == index || chosen < 0 || chosen >= field.Options.Length)
                return;

            tokens.Set(field.Key, field.Options[chosen]);
            Commit(row, tokens);
        }

        // --- helpers ------------------------------------------------------------------------

        private static bool IsPlainNumber(string text)
        {
            return TryNumber(text, out _);
        }

        private static bool TryNumber(string text, out float value)
        {
            return float.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        private static string BufferKey(SettingRow row, StructuredField field)
        {
            return row.Id + "|" + field.Key;
        }

        private static StructuredTokens Tokens(SettingRow row)
        {
            try
            {
                return StructuredTokens.Parse(row.Entry.BoxedValue as string);
            }
            catch
            {
                return StructuredTokens.Parse(null);
            }
        }

        private static void Commit(SettingRow row, StructuredTokens tokens)
        {
            ConfigWriter.Set(row, tokens.ToString());
        }
    }
}
