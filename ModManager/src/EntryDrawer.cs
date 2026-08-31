using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Draws the editing widget for one setting, chosen from its runtime type.
    ///
    /// Everything here draws into an explicit <see cref="Rect"/> rather than through GUILayout.
    /// The settings list is virtualised - only the rows actually on screen are drawn - and that is
    /// only possible with a constant row pitch and absolute positioning. It also sidesteps
    /// GUILayout's Layout/Repaint control pairing entirely, which is what used to make a list
    /// whose length changed mid-frame throw.
    ///
    /// The important property is that an unrecognised type still gets an editor. BepInEx can
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
            CloseMenu();
        }

        private const float ControlHeight = 28f;

        /// <summary>Draws the editor for <paramref name="row"/> centred inside <paramref name="area"/>.</summary>
        public static void Draw(Rect area, SettingRow row)
        {
            var rect = new Rect(area.x, area.y + (area.height - ControlHeight) / 2f,
                area.width, ControlHeight);

            var entry = row.Entry;
            var type = entry.SettingType;

            object current;
            try
            {
                current = entry.BoxedValue;
            }
            catch (Exception e)
            {
                Skin.Text(rect, "<unreadable: " + e.Message + ">", Skin.Value, Skin.Bad);
                return;
            }

            if (type == typeof(bool))
            {
                DrawBool(rect, row, (bool)current);
                return;
            }

            if (type == typeof(KeyboardShortcut))
            {
                DrawShortcut(rect, row, (KeyboardShortcut)current);
                return;
            }

            if (type.IsEnum)
            {
                DrawEnum(rect, row, type, current);
                return;
            }

            if (IsNumeric(type))
            {
                DrawNumeric(rect, row, type, current);
                return;
            }

            if (type == typeof(string))
            {
                DrawString(rect, row, (string)current);
                return;
            }

            DrawSerialized(rect, row);
        }

        // --- boolean dropdown ---------------------------------------------------------------
        //
        // A popup in IMGUI has an ordering problem: controls take the mouse in the order they are
        // drawn, so a menu painted last - which is what puts it on top - would lose every click to
        // the row controls underneath it. Input and painting are therefore split. The click is
        // handled before the list is drawn, against the rectangle worked out on the previous
        // frame, and the menu is only painted afterwards, with no controls in it at all.

        private static string openMenu;
        private static SettingRow menuRow;
        private static bool menuValue;

        /// <summary>Where the button was, in the scroll view's content space.</summary>
        private static Rect menuAnchorContent;

        /// <summary>The same rect in window space, and the two item rects under it.</summary>
        private static Rect menuAnchor;
        private static Rect menuOn;
        private static Rect menuOff;
        private static bool menuPlaced;

        private static void CloseMenu()
        {
            openMenu = null;
            menuRow = null;
            menuPlaced = false;
        }

        /// <summary>
        /// Two options in a dropdown rather than a checkbox, so a boolean reads as the same kind
        /// of control as every other value in the column - a box showing its current setting -
        /// instead of a tick whose meaning depends on how the setting happens to be named.
        /// </summary>
        private static void DrawBool(Rect rect, SettingRow row, bool value)
        {
            var box = new Rect(rect.x, rect.y, Mathf.Min(rect.width, 130f), rect.height);

            // While the menu is open its own handler owns the clicks, including the one that
            // closes it again, so this must not also act on them.
            if (openMenu == row.Id)
            {
                GUI.Label(box, value ? "on" : "off", Skin.DropdownOpen);
                Skin.Caret(box);

                menuAnchorContent = box;
                menuRow = row;
                menuValue = value;
                return;
            }

            if (GUI.Button(box, value ? "on" : "off", Skin.Dropdown))
            {
                openMenu = row.Id;
                menuRow = row;
                menuValue = value;
                menuAnchorContent = box;
                menuPlaced = false;
            }

            Skin.Caret(box);
        }

        /// <summary>
        /// Acts on a click in the open menu. Called before the list is drawn, so the click is
        /// consumed before any row control can take it.
        /// </summary>
        public static void HandleMenuInput()
        {
            if (openMenu == null)
                return;

            var e = Event.current;
            if (e == null)
                return;

            // Scrolling would move the anchor out from under the menu.
            if (e.type == EventType.ScrollWheel)
            {
                CloseMenu();
                return;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CloseMenu();
                e.Use();
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 0 || !menuPlaced)
                return;

            if (menuOn.Contains(e.mousePosition))
            {
                if (!menuValue)
                    ConfigWriter.Set(menuRow, true);

                CloseMenu();
                e.Use();
                return;
            }

            if (menuOff.Contains(e.mousePosition))
            {
                if (menuValue)
                    ConfigWriter.Set(menuRow, false);

                CloseMenu();
                e.Use();
                return;
            }

            bool onButton = menuAnchor.Contains(e.mousePosition);
            CloseMenu();

            // Swallowed only when it landed on the button itself, so clicking the button while the
            // menu is open closes it rather than closing and immediately reopening.
            if (onButton)
                e.Use();
        }

        /// <summary>
        /// Works out where the menu sits in window space, from the anchor recorded while the row
        /// was drawn inside the scroll view.
        /// </summary>
        public static void PlaceMenu(Vector2 scroll, Rect view)
        {
            if (openMenu == null || menuRow == null)
                return;

            var anchor = new Rect(menuAnchorContent.x + view.x - scroll.x,
                menuAnchorContent.y + view.y - scroll.y,
                menuAnchorContent.width, menuAnchorContent.height);

            // A row scrolled out of sight takes its menu with it.
            if (anchor.yMax < view.y || anchor.y > view.yMax)
            {
                CloseMenu();
                return;
            }

            float itemHeight = anchor.height;
            float below = anchor.yMax + 2f;

            // Flipped above the button when there is no room under it, which is the usual case for
            // the last rows of a long section.
            if (below + itemHeight * 2f > view.yMax)
                below = anchor.y - 2f - itemHeight * 2f;

            menuAnchor = anchor;
            menuOn = new Rect(anchor.x, below, anchor.width, itemHeight);
            menuOff = new Rect(anchor.x, below + itemHeight, anchor.width, itemHeight);
            menuPlaced = true;
        }

        /// <summary>
        /// Paints the open menu on top of everything. Deliberately draws no controls - the clicks
        /// were dealt with by <see cref="HandleMenuInput"/> before the list existed.
        /// </summary>
        public static void PaintMenu()
        {
            if (openMenu == null || !menuPlaced)
                return;

            var frame = new Rect(menuOn.x, menuOn.y, menuOn.width, menuOn.height + menuOff.height);

            Skin.Fill(frame, Skin.PanelHigh);
            Skin.Frame(frame, Skin.Accent);

            PaintItem(menuOn, "on", menuValue);
            PaintItem(menuOff, "off", !menuValue);
        }

        private static void PaintItem(Rect rect, string label, bool chosen)
        {
            bool hot = rect.Contains(Event.current.mousePosition);

            if (chosen)
                Skin.Fill(rect, Skin.Selected);
            else if (hot)
                Skin.Fill(rect, Skin.RowHover);

            Skin.Text(rect, label, chosen ? Skin.MenuItemActive : Skin.MenuItem,
                chosen ? Skin.Accent : Skin.Ink);
        }

        private static void DrawShortcut(Rect rect, SettingRow row, KeyboardShortcut value)
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

            string label = listening ? "press a key  (esc cancels)" : value.ToString();

            if (GUI.Button(rect, label, Skin.Button))
                listeningFor = listening ? null : row.Id;
        }

        private static void DrawEnum(Rect rect, SettingRow row, Type type, object current)
        {
            // [Flags] enums are a set rather than a choice, and a row of toggles for one is wider
            // than the column. The serialized form BepInEx already uses ("A, B, C") is both
            // compact and exactly what the .cfg file contains.
            if (type.IsDefined(typeof(FlagsAttribute), false))
            {
                DrawSerialized(rect, row);
                return;
            }

            var names = Enum.GetNames(type);

            // KeyCode has several hundred members; a selection grid of those is unusable, and a
            // grid of more than a handful no longer fits the control column at a legible size.
            if (names.Length > 6)
            {
                DrawSerialized(rect, row);
                return;
            }

            int index = Array.IndexOf(names, current != null ? current.ToString() : string.Empty);
            int next = GUI.SelectionGrid(rect, index, names, names.Length, Skin.Button);

            if (next != index && next >= 0 && next < names.Length)
                ConfigWriter.Set(row, Enum.Parse(type, names[next]));
        }

        private static void DrawNumeric(Rect rect, SettingRow row, Type type, object current)
        {
            var range = row.Entry.Description != null
                ? row.Entry.Description.AcceptableValues as AcceptableValueBase
                : null;

            if (TryGetRange(range, out double min, out double max))
            {
                double value = Convert.ToDouble(current, CultureInfo.InvariantCulture);

                float trackWidth = rect.width - Skin.NumberBoxWidth - 10f;
                var track = new Rect(rect.x, rect.y + (rect.height - 18f) / 2f, trackWidth, 18f);

                float slid = GUI.HorizontalSlider(track, (float)value, (float)min, (float)max,
                    Skin.Slider, Skin.SliderThumb);

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

                DrawNumberBox(new Rect(rect.xMax - Skin.NumberBoxWidth, rect.y,
                    Skin.NumberBoxWidth, rect.height), row, type);
                return;
            }

            DrawNumberBox(new Rect(rect.x, rect.y, Mathf.Min(rect.width, 150f), rect.height),
                row, type);
        }

        private static void DrawNumberBox(Rect rect, SettingRow row, Type type)
        {
            string canonical = FormatNumber(row.Entry.BoxedValue);
            string buffer = SyncBuffer(row.Id, canonical);

            // Named so focus follows the setting rather than the draw order: rows scroll in and
            // out of the virtualised list, which shifts every unnamed control's id underneath a
            // box that is being typed into.
            GUI.SetNextControlName(row.Id);
            string typed = GUI.TextField(rect, buffer, Skin.Field);

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

        private static void DrawString(Rect rect, SettingRow row, string value)
        {
            string canonical = value ?? string.Empty;
            string buffer = SyncBuffer(row.Id, canonical);

            GUI.SetNextControlName(row.Id);
            string typed = GUI.TextField(rect, buffer, Skin.Field);

            if (typed == buffer)
                return;

            Buffers[row.Id] = typed;
            BufferSource[row.Id] = typed;
            ConfigWriter.Set(row, typed);
        }

        /// <summary>
        /// The universal fallback: edit the setting in the exact TOML form the .cfg file stores.
        /// </summary>
        private static void DrawSerialized(Rect rect, SettingRow row)
        {
            string canonical;
            try
            {
                canonical = row.Entry.GetSerializedValue();
            }
            catch (Exception e)
            {
                Skin.Text(rect, "<not editable: " + e.Message + ">", Skin.Value, Skin.Bad);
                return;
            }

            string buffer = SyncBuffer(row.Id, canonical);

            GUI.SetNextControlName(row.Id);
            string typed = GUI.TextField(rect, buffer, Skin.Field);

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

        /// <summary>Human-readable current value, for the detail panel and the About list.</summary>
        public static string Format(object value)
        {
            if (value == null)
                return "(none)";

            if (value is string s)
                return s.Length == 0 ? "(empty)" : s;

            if (value is bool b)
                return b ? "on" : "off";

            return FormatNumber(value);
        }

        public static bool TryDescribeRange(SettingRow row, out string text)
        {
            text = null;

            var acceptable = row.Entry.Description != null
                ? row.Entry.Description.AcceptableValues as AcceptableValueBase
                : null;

            if (!TryGetRange(acceptable, out double min, out double max))
                return false;

            text = min.ToString("0.####", CultureInfo.InvariantCulture) + "  to  "
                 + max.ToString("0.####", CultureInfo.InvariantCulture);
            return true;
        }

        private static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt
                || key == KeyCode.LeftShift || key == KeyCode.RightShift
                || key == KeyCode.LeftCommand || key == KeyCode.RightCommand;
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
