using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Draws a control per field for a setting whose value is a list of <c>key=value</c> pairs.
    ///
    /// The point is that the encoding stops being something to get right by hand. A field is
    /// either left alone or given a value, the operator is chosen from a list rather than
    /// remembered, and anything constrained to a fixed set of words - a stack type, a tick type -
    /// is a menu, so a typo is not one of the available outcomes.
    ///
    /// Keys the schema does not describe are preserved untouched and reported, because a
    /// hand-written value must not be quietly destroyed by an editor that does not understand it.
    /// </summary>
    internal static class StructuredEditor
    {
        private const float RowHeight = 34f;
        private const float Gap = 8f;

        private static bool showAll;

        /// <summary>Key of the field whose operator menu is open, if any.</summary>
        private static string openMode;

        private static readonly Dictionary<string, string> Buffers = new Dictionary<string, string>();

        public static void Reset()
        {
            openMode = null;
            Buffers.Clear();
        }

        public static float Height(SettingRow row, StructuredSchema schema, float width)
        {
            int visible = 0;

            foreach (var field in schema.Fields)
            {
                if (showAll || field.Common)
                    visible++;
            }

            float height = 28f + visible * (RowHeight + Gap) + 44f;

            var tokens = Tokens(row);
            if (tokens.UnknownKeys(schema).Count > 0)
                height += 46f;

            return height;
        }

        public static float Draw(Rect area, SettingRow row, StructuredSchema schema)
        {
            var tokens = Tokens(row);

            float y = area.y;

            Skin.Text(new Rect(area.x, y, area.width, 20f), "FIELDS", Skin.SmallCaps, Skin.InkDim);
            y += 26f;

            foreach (var field in schema.Fields)
            {
                if (!showAll && !field.Common)
                    continue;

                DrawField(new Rect(area.x, y, area.width, RowHeight), row, field, tokens);
                y += RowHeight + Gap;
            }

            var unknown = tokens.UnknownKeys(schema);
            if (unknown.Count > 0)
            {
                Skin.Text(new Rect(area.x, y, area.width, 40f),
                    "Also set by hand, and left untouched: " + string.Join(", ", unknown.ToArray()),
                    Skin.Muted, Skin.AccentDim);
                y += 46f;
            }

            var toggle = new Rect(area.x, y, 200f, 30f);
            if (GUI.Button(toggle, showAll ? "Show fewer fields" : "Show all fields", Skin.ButtonQuiet))
            {
                showAll = !showAll;
                openMode = null;
            }

            return y + 44f;
        }

        private static void DrawField(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens)
        {
            string raw = tokens.Get(field.Key);
            ValueMode mode = ValueModes.Read(raw, out string operand);

            float labelWidth = 148f;
            Skin.Text(new Rect(rect.x, rect.y, labelWidth, rect.height), field.Label,
                Skin.RowName, mode == ValueMode.Vanilla ? Skin.InkMuted : Skin.Ink);

            float x = rect.x + labelWidth;
            float remaining = rect.xMax - x;

            // The mode selector is only worth its width where more than one mode is possible.
            float modeWidth = field.AllowOperators ? 104f : 90f;
            var modeRect = new Rect(x, rect.y + 2f, modeWidth, rect.height - 4f);

            DrawModeSelector(modeRect, row, field, tokens, mode, operand);

            x += modeWidth + 8f;
            remaining = rect.xMax - x;

            if (mode == ValueMode.Vanilla)
            {
                Skin.Text(new Rect(x, rect.y, remaining, rect.height),
                    "unchanged", Skin.Value, Skin.InkDim);
                return;
            }

            var valueRect = new Rect(x, rect.y + 2f, remaining, rect.height - 4f);

            if (field.IsEnum && mode == ValueMode.Set)
                DrawEnumValue(valueRect, row, field, tokens, operand);
            else if (field.IsBool && mode == ValueMode.Set)
                DrawBoolValue(valueRect, row, field, tokens, operand);
            else
                DrawTextValue(valueRect, row, field, tokens, mode, operand);
        }

        /// <summary>
        /// Cycles rather than opening a menu. There are at most five modes, the list is short
        /// enough to read off the button, and a popup inside the detail panel's scroll view would
        /// need the same split-input dance the settings list uses - a lot of machinery for a
        /// control whose whole job is to pick between "leave it alone" and four operators.
        /// </summary>
        private static void DrawModeSelector(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, ValueMode mode, string operand)
        {
            if (!GUI.Button(rect, ValueModes.Label(mode), Skin.ButtonQuiet))
                return;

            var order = Modes(field);
            int index = order.IndexOf(mode);
            ValueMode next = order[(index + 1) % order.Count];

            if (next == ValueMode.Vanilla)
            {
                tokens.Remove(field.Key);
            }
            else
            {
                tokens.Set(field.Key, ValueModes.Write(next, Seed(field, next, operand)));
            }

            Buffers.Remove(BufferKey(row, field));
            Commit(row, tokens);
        }

        private static List<ValueMode> Modes(StructuredField field)
        {
            var order = new List<ValueMode> { ValueMode.Vanilla, ValueMode.Set };

            if (field.AllowOperators)
            {
                order.Add(ValueMode.Multiply);
                order.Add(ValueMode.Add);
                order.Add(ValueMode.Expression);
            }

            return order;
        }

        /// <summary>A starting value that is valid for the mode being switched into.</summary>
        private static string Seed(StructuredField field, ValueMode mode, string operand)
        {
            switch (mode)
            {
                case ValueMode.Multiply:
                    return ValueModes.LooksNumeric(operand) ? operand : "1";

                case ValueMode.Add:
                    return ValueModes.LooksNumeric(operand) ? operand : "1";

                case ValueMode.Expression:
                    return string.IsNullOrEmpty(operand) ? "Source.Level" : operand;

                default:
                    if (field.IsBool)
                        return operand == "false" ? "false" : "true";

                    if (field.IsEnum)
                    {
                        return Array.IndexOf(field.Options, operand) >= 0
                            ? operand
                            : (field.Options.Length > 0 ? field.Options[0] : string.Empty);
                    }

                    return ValueModes.LooksNumeric(operand) ? operand : "1";
            }
        }

        private static void DrawEnumValue(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, string operand)
        {
            bool open = openMode == field.Key;

            if (GUI.Button(rect, operand.Length == 0 ? "choose…" : operand, Skin.Dropdown))
                openMode = open ? null : field.Key;

            Skin.Caret(rect);

            if (!open)
                return;

            // Drawn immediately below, inside the same panel. The panel scrolls, so a long option
            // list is reachable; nothing else is drawn over it because this is the last pass.
            float height = field.Options.Length * 28f;
            var list = new Rect(rect.x, rect.yMax + 2f, rect.width, height);

            Skin.Fill(list, Skin.PanelHigh);
            Skin.Frame(list, Skin.Accent);

            for (int i = 0; i < field.Options.Length; i++)
            {
                var item = new Rect(list.x, list.y + i * 28f, list.width, 28f);
                bool chosen = field.Options[i] == operand;

                if (GUI.Button(item, field.Options[i], chosen ? Skin.MenuItemActive : Skin.MenuItem))
                {
                    tokens.Set(field.Key, field.Options[i]);
                    Commit(row, tokens);
                    openMode = null;
                }
            }
        }

        private static void DrawBoolValue(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, string operand)
        {
            bool value = !string.Equals(operand, "false", StringComparison.OrdinalIgnoreCase);

            int chosen = GUI.SelectionGrid(rect, value ? 0 : 1, new[] { "true", "false" }, 2,
                Skin.ButtonQuiet);

            bool next = chosen == 0;
            if (next == value)
                return;

            tokens.Set(field.Key, next ? "true" : "false");
            Commit(row, tokens);
        }

        private static void DrawTextValue(Rect rect, SettingRow row, StructuredField field,
            StructuredTokens tokens, ValueMode mode, string operand)
        {
            string id = BufferKey(row, field);

            if (!Buffers.TryGetValue(id, out string buffer) || buffer == null)
                buffer = operand;

            GUI.SetNextControlName(id);
            string typed = GUI.TextField(rect, buffer, Skin.Field);

            if (typed == buffer)
                return;

            Buffers[id] = typed;

            // Committed on every keystroke that leaves a usable value, but a half-typed number
            // ("-", "1.") stays in the box rather than being written and bounced back.
            if (mode == ValueMode.Expression || ValueModes.LooksNumeric(typed) || typed.Length == 0)
            {
                tokens.Set(field.Key, ValueModes.Write(mode, typed));
                Commit(row, tokens);
            }
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
