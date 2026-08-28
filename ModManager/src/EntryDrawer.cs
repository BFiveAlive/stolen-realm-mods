using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Draws the editing widget for one setting, chosen from its runtime type.
    ///
    /// The important property here is that an unrecognised type still gets an editor. BepInEx can
    /// round-trip any setting it was able to write to disk through <c>GetSerializedValue</c> and
    /// <c>SetSerializedValue</c>, so the fallback is a text box over the same TOML representation
    /// the .cfg file holds. A future mod using a type this file has never heard of is editable,
    /// just less prettily.
    /// </summary>
    internal static class EntryDrawer
    {
        // Text being typed, keyed by setting id. Held separately from the entry so a half-finished
        // number ("-", "1.") does not have to parse on every keystroke.
        private static readonly Dictionary<string, string> Buffers = new Dictionary<string, string>();

        // The value each buffer was seeded from, so an edit made elsewhere (a hot-reloaded config
        // file, another mod writing the setting) refreshes the box instead of being overwritten.
        private static readonly Dictionary<string, string> BufferSource = new Dictionary<string, string>();

        /// <summary>Id of the setting currently waiting for a key press, if any.</summary>
        private static string listeningFor;

        public static void ClearTransientState()
        {
            Buffers.Clear();
            BufferSource.Clear();
            listeningFor = null;
        }

        public static void Draw(SettingRow row)
        {
            var entry = row.Entry;
            var type = entry.SettingType;

            object current;
            try
            {
                current = entry.BoxedValue;
            }
            catch (Exception e)
            {
                GUILayout.Label("<unreadable: " + e.Message + ">", Skin.Muted);
                return;
            }

            if (type == typeof(bool))
            {
                DrawBool(row, (bool)current);
                return;
            }

            if (type == typeof(KeyboardShortcut))
            {
                DrawShortcut(row, (KeyboardShortcut)current);
                return;
            }

            if (type.IsEnum)
            {
                DrawEnum(row, type, current);
                return;
            }

            if (IsNumeric(type))
            {
                DrawNumeric(row, type, current);
                return;
            }

            if (type == typeof(string))
            {
                DrawString(row, (string)current);
                return;
            }

            DrawSerialized(row);
        }

        private static void DrawBool(SettingRow row, bool value)
        {
            bool next = GUILayout.Toggle(value, value ? " on" : " off", Skin.Toggle, GUILayout.Width(Skin.FieldWidth));
            if (next != value)
                ConfigWriter.Set(row, next);
        }

        private static void DrawShortcut(SettingRow row, KeyboardShortcut value)
        {
            bool listening = listeningFor == row.Id;

            // Read the key before drawing the button: the same event would otherwise be consumed
            // by the button itself, and a bound key could never be changed to another one.
            if (listening && Event.current != null && Event.current.type == EventType.KeyDown)
            {
                var pressed = Event.current.keyCode;

                if (pressed == KeyCode.Escape)
                {
                    listeningFor = null;
                }
                else if (pressed != KeyCode.None && !IsModifier(pressed))
                {
                    var modifiers = new List<KeyCode>();
                    if (Event.current.control) modifiers.Add(KeyCode.LeftControl);
                    if (Event.current.alt) modifiers.Add(KeyCode.LeftAlt);
                    if (Event.current.shift) modifiers.Add(KeyCode.LeftShift);

                    ConfigWriter.Set(row, new KeyboardShortcut(pressed, modifiers.ToArray()));
                    listeningFor = null;
                }

                Event.current.Use();
                GUI.changed = true;
            }

            string label = listening ? "press a key (esc cancels)" : value.ToString();

            if (GUILayout.Button(label, Skin.Field, GUILayout.Width(Skin.FieldWidth)))
                listeningFor = listening ? null : row.Id;
        }

        private static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt
                || key == KeyCode.LeftShift || key == KeyCode.RightShift
                || key == KeyCode.LeftCommand || key == KeyCode.RightCommand;
        }

        private static void DrawEnum(SettingRow row, Type type, object current)
        {
            // [Flags] enums are a set rather than a choice, and a row of toggles for one is wider
            // than the panel. The serialized form BepInEx already uses ("A, B, C") is both
            // compact and exactly what the .cfg file contains.
            if (type.IsDefined(typeof(FlagsAttribute), false))
            {
                DrawSerialized(row);
                return;
            }

            var values = Enum.GetValues(type);

            // KeyCode has several hundred members; a selection grid of those is unusable.
            if (values.Length > 12)
            {
                DrawSerialized(row);
                return;
            }

            var names = Enum.GetNames(type);
            int index = Array.IndexOf(names, current != null ? current.ToString() : string.Empty);

            int columns = names.Length <= 4 ? names.Length : 3;
            int next = GUILayout.SelectionGrid(index, names, columns, Skin.Field, GUILayout.Width(Skin.FieldWidth));

            if (next != index && next >= 0 && next < names.Length)
                ConfigWriter.Set(row, Enum.Parse(type, names[next]));
        }

        private static void DrawNumeric(SettingRow row, Type type, object current)
        {
            var range = row.Entry.Description != null
                ? row.Entry.Description.AcceptableValues as AcceptableValueBase
                : null;

            if (TryGetRange(range, out double min, out double max))
            {
                double value = Convert.ToDouble(current, CultureInfo.InvariantCulture);

                GUILayout.BeginHorizontal(GUILayout.Width(Skin.FieldWidth));

                float slid = GUILayout.HorizontalSlider((float)value, (float)min, (float)max,
                    GUILayout.Width(Skin.FieldWidth - Skin.NumberBoxWidth - 8f));

                // An integer slider must land on integers, and float comparison needs a tolerance
                // wider than the slider's own pixel quantisation to avoid writing every frame.
                double candidate = IsIntegral(type) ? Math.Round(slid) : slid;
                double epsilon = (max - min) * 1e-5;

                if (Math.Abs(candidate - value) > epsilon)
                {
                    ConfigWriter.Set(row, ConvertTo(type, candidate));
                    // The slider is now authoritative, so drop any stale text being typed.
                    Buffers.Remove(row.Id);
                    BufferSource.Remove(row.Id);
                }

                DrawNumberBox(row, type, Skin.NumberBoxWidth);

                GUILayout.EndHorizontal();
                return;
            }

            DrawNumberBox(row, type, Skin.FieldWidth);
        }

        private static void DrawNumberBox(SettingRow row, Type type, float width)
        {
            string canonical = FormatNumber(row.Entry.BoxedValue);
            string buffer = SyncBuffer(row.Id, canonical);

            string typed = GUILayout.TextField(buffer, Skin.Field, GUILayout.Width(width));

            if (typed == buffer)
                return;

            Buffers[row.Id] = typed;

            // Only commit what parses. Anything else stays in the box as typed, so a partially
            // entered number is not stomped back to its old value mid-keystroke.
            if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                object clamped = Clamp(row, ConvertTo(type, parsed));
                ConfigWriter.Set(row, clamped);
                BufferSource[row.Id] = FormatNumber(clamped);
            }
        }

        private static void DrawString(SettingRow row, string value)
        {
            string canonical = value ?? string.Empty;
            string buffer = SyncBuffer(row.Id, canonical);

            string typed = GUILayout.TextField(buffer, Skin.Field, GUILayout.Width(Skin.FieldWidth));

            if (typed == buffer)
                return;

            Buffers[row.Id] = typed;
            BufferSource[row.Id] = typed;
            ConfigWriter.Set(row, typed);
        }

        /// <summary>
        /// The universal fallback: edit the setting in the exact TOML form the .cfg file stores.
        /// </summary>
        private static void DrawSerialized(SettingRow row)
        {
            string canonical;
            try
            {
                canonical = row.Entry.GetSerializedValue();
            }
            catch (Exception e)
            {
                GUILayout.Label("<not editable: " + e.Message + ">", Skin.Muted);
                return;
            }

            string buffer = SyncBuffer(row.Id, canonical);

            string typed = GUILayout.TextField(buffer, Skin.Field, GUILayout.Width(Skin.FieldWidth));

            if (typed == buffer)
                return;

            Buffers[row.Id] = typed;

            try
            {
                row.Entry.SetSerializedValue(typed);
                BufferSource[row.Id] = row.Entry.GetSerializedValue();

                // SetSerializedValue writes straight through the entry rather than going via
                // ConfigWriter, so the file still has to be marked dirty by hand.
                ConfigWriter.Set(row, row.Entry.BoxedValue);
            }
            catch
            {
                // Half-typed input is expected here; the box keeps what was typed and the value
                // is left alone until it parses.
            }
        }

        /// <summary>
        /// Returns the text to display, re-seeding it whenever the underlying value changed
        /// somewhere other than this box.
        /// </summary>
        private static string SyncBuffer(string id, string canonical)
        {
            if (BufferSource.TryGetValue(id, out string source) && source == canonical
                && Buffers.TryGetValue(id, out string buffer))
            {
                return buffer;
            }

            Buffers[id] = canonical;
            BufferSource[id] = canonical;
            return canonical;
        }

        private static bool TryGetRange(AcceptableValueBase acceptable, out double min, out double max)
        {
            min = 0;
            max = 0;

            if (acceptable == null)
                return false;

            var type = acceptable.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
                return false;

            try
            {
                object low = type.GetProperty("MinValue").GetValue(acceptable, null);
                object high = type.GetProperty("MaxValue").GetValue(acceptable, null);

                min = Convert.ToDouble(low, CultureInfo.InvariantCulture);
                max = Convert.ToDouble(high, CultureInfo.InvariantCulture);
                return max > min;
            }
            catch
            {
                // A range over something that is IComparable but not convertible to a double
                // (a DateTime, say) falls through to the text editor.
                return false;
            }
        }

        private static object Clamp(SettingRow row, object value)
        {
            var acceptable = row.Entry.Description != null ? row.Entry.Description.AcceptableValues : null;
            if (acceptable == null)
                return value;

            try
            {
                return acceptable.Clamp(value);
            }
            catch
            {
                return value;
            }
        }

        private static bool IsNumeric(Type type)
        {
            return type == typeof(int) || type == typeof(float) || type == typeof(double)
                || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(sbyte) || type == typeof(uint) || type == typeof(ulong)
                || type == typeof(ushort) || type == typeof(decimal);
        }

        private static bool IsIntegral(Type type)
        {
            return type != typeof(float) && type != typeof(double) && type != typeof(decimal);
        }

        private static object ConvertTo(Type type, double value)
        {
            // Convert.ChangeType throws on overflow rather than saturating, and a slider dragged
            // to the end of a byte range would otherwise take the game down.
            try
            {
                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return value > 0
                    ? Convert.ChangeType(type.GetField("MaxValue").GetValue(null), type, CultureInfo.InvariantCulture)
                    : Convert.ChangeType(type.GetField("MinValue").GetValue(null), type, CultureInfo.InvariantCulture);
            }
        }

        private static string FormatNumber(object value)
        {
            if (value == null)
                return string.Empty;

            // "R" round-trips a float exactly but produces things like 0.30000001192092896.
            // Trimming to a readable precision and dropping trailing zeros keeps the box legible;
            // the stored value is untouched until the box is actually edited.
            if (value is float f)
                return f.ToString("0.#####", CultureInfo.InvariantCulture);

            if (value is double d)
                return d.ToString("0.##########", CultureInfo.InvariantCulture);

            if (value is decimal m)
                return m.ToString(CultureInfo.InvariantCulture);

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
