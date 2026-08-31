using System.Collections.Generic;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// IMGUI styling and the primitive rect painters the panel is drawn from.
    ///
    /// Unity's default IMGUI skin is the grey editor look and is close to unreadable over a dark
    /// game. These styles are deliberately plain rather than an attempt to imitate Stolen Realm's
    /// art: nothing here depends on a game asset, so a game update cannot break the panel.
    ///
    /// Everything is square-edged on purpose. Rounded corners in IMGUI mean a nine-slice texture
    /// per corner radius, which is a pile of generated art to maintain for a visual detail; a 1px
    /// border drawn as four thin rects reads nearly as well and costs nothing.
    /// </summary>
    internal static class Skin
    {
        // --- palette ------------------------------------------------------------------------

        public static readonly Color Backdrop   = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color Panel      = new Color(0.098f, 0.110f, 0.141f, 0.98f);
        public static readonly Color PanelHigh  = new Color(0.125f, 0.141f, 0.180f, 1f);
        public static readonly Color Rail       = new Color(0.078f, 0.090f, 0.125f, 1f);
        public static readonly Color RowAlt     = new Color(0.114f, 0.129f, 0.169f, 1f);
        public static readonly Color RowHover   = new Color(0.153f, 0.173f, 0.220f, 1f);
        public static readonly Color Selected   = new Color(0.200f, 0.251f, 0.353f, 1f);
        public static readonly Color Line       = new Color(0.200f, 0.227f, 0.282f, 1f);
        public static readonly Color Sunken     = new Color(0.055f, 0.063f, 0.086f, 1f);

        public static readonly Color Ink        = new Color(0.914f, 0.906f, 0.886f);
        public static readonly Color InkMuted   = new Color(0.596f, 0.627f, 0.690f);
        public static readonly Color InkDim     = new Color(0.420f, 0.451f, 0.522f);
        public static readonly Color Accent     = new Color(0.851f, 0.643f, 0.255f);
        public static readonly Color Good       = new Color(0.435f, 0.749f, 0.451f);
        public static readonly Color Bad        = new Color(0.878f, 0.400f, 0.400f);

        // --- metrics (unscaled; GUI.matrix applies UiScale on top) ---------------------------

        public const float TitleBarHeight  = 54f;
        public const float SearchStrip     = 62f;
        public const float FooterHeight    = 44f;
        public const float RailWidth       = 306f;
        public const float DetailWidth     = 404f;

        public const float PluginRowHeight  = 52f;
        public const float SectionRowHeight = 34f;

        /// <summary>Uniform, because virtualising the list depends on a constant row pitch.</summary>
        public const float ListRowHeight   = 44f;
        public const float ResultRowHeight = 76f;

        public const float ControlWidth    = 300f;
        public const float NumberBoxWidth  = 74f;

        // --- styles -------------------------------------------------------------------------

        public static GUIStyle Window;
        public static GUIStyle Title;
        public static GUIStyle Subtitle;
        public static GUIStyle TabLabel;
        public static GUIStyle Header;
        public static GUIStyle SmallCaps;
        public static GUIStyle RowName;
        public static GUIStyle RowNameBold;
        public static GUIStyle Value;
        public static GUIStyle ValueRight;
        public static GUIStyle Body;
        public static GUIStyle Muted;
        public static GUIStyle MutedRight;
        public static GUIStyle Warning;
        public static GUIStyle Field;
        public static GUIStyle FieldPlaceholder;
        public static GUIStyle Button;
        public static GUIStyle ButtonQuiet;
        public static GUIStyle Toggle;
        public static GUIStyle Slider;
        public static GUIStyle SliderThumb;
        public static GUIStyle Badge;
        public static GUIStyle Row;

        private static bool built;
        private static Texture2D white;

        public static void Build()
        {
            if (built)
                return;

            built = true;
            white = Solid(Color.white);

            Window = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };
            Window.normal.background = Solid(Panel);
            Window.onNormal.background = Window.normal.background;

            Title = Label(24, FontStyle.Bold, Ink);
            Subtitle = Label(15, FontStyle.Normal, InkDim);
            TabLabel = Label(17, FontStyle.Normal, InkMuted, TextAnchor.MiddleCenter);
            Header = Label(21, FontStyle.Bold, Ink);
            SmallCaps = Label(13, FontStyle.Bold, InkDim);

            RowName = Label(17, FontStyle.Normal, Ink);
            RowNameBold = Label(17, FontStyle.Bold, Ink);

            Value = Label(15, FontStyle.Normal, InkMuted);
            ValueRight = Label(15, FontStyle.Normal, InkMuted, TextAnchor.MiddleRight);

            Body = Label(16, FontStyle.Normal, InkMuted);
            Body.wordWrap = true;

            Muted = Label(15, FontStyle.Normal, InkMuted);
            Muted.wordWrap = true;

            MutedRight = Label(15, FontStyle.Normal, InkDim, TextAnchor.MiddleRight);

            Warning = Label(16, FontStyle.Normal, Accent);
            Badge = Label(14, FontStyle.Normal, InkDim, TextAnchor.MiddleRight);

            Field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 16,
                padding = new RectOffset(9, 9, 5, 5),
                alignment = TextAnchor.MiddleLeft
            };
            Field.normal.background = Solid(Sunken);
            Field.focused.background = Field.normal.background;
            Field.hover.background = Field.normal.background;
            Field.normal.textColor = Ink;
            Field.focused.textColor = Color.white;
            Field.hover.textColor = Ink;

            FieldPlaceholder = new GUIStyle(Field);
            FieldPlaceholder.normal.textColor = InkDim;

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                padding = new RectOffset(10, 10, 5, 5)
            };
            Button.normal.background = Solid(PanelHigh);
            Button.hover.background = Solid(RowHover);
            Button.active.background = Solid(Selected);
            Button.normal.textColor = InkMuted;
            Button.hover.textColor = Ink;
            Button.active.textColor = Ink;

            ButtonQuiet = new GUIStyle(Button) { fontSize = 14 };

            Toggle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 16
            };
            Toggle.normal.textColor = InkMuted;
            Toggle.onNormal.textColor = Ink;
            Toggle.hover.textColor = Ink;
            Toggle.onHover.textColor = Ink;

            Slider = new GUIStyle(GUI.skin.horizontalSlider);
            Slider.normal.background = Solid(new Color(0.208f, 0.235f, 0.298f));
            Slider.fixedHeight = 8f;

            SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb);
            SliderThumb.normal.background = Solid(Accent);
            SliderThumb.active.background = Solid(Color.white);
            SliderThumb.hover.background = Solid(Color.Lerp(Accent, Color.white, 0.3f));
            SliderThumb.fixedWidth = 14f;
            SliderThumb.fixedHeight = 18f;
            SliderThumb.border = new RectOffset(0, 0, 0, 0);
            SliderThumb.overflow = new RectOffset(0, 0, 0, 0);

            // Used by the Updates tab, which is still laid out with GUILayout: a card background
            // it can wrap a whole entry in.
            Row = new GUIStyle
            {
                padding = new RectOffset(14, 14, 10, 10),
                margin = new RectOffset(0, 0, 0, 6)
            };
            Row.normal.background = Solid(RowAlt);
        }

        private static GUIStyle Label(int size, FontStyle weight, Color colour,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = anchor,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            style.normal.textColor = colour;
            style.hover.textColor = colour;
            return style;
        }

        // --- painters -----------------------------------------------------------------------

        /// <summary>Flat colour fill. Tinting one white texture avoids a texture per colour.</summary>
        public static void Fill(Rect rect, Color colour)
        {
            var previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        public static void Frame(Rect rect, Color colour, float thickness = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
        }

        public static void HLine(float x, float y, float width, Color colour)
        {
            Fill(new Rect(x, y, width, 1f), colour);
        }

        public static void VLine(float x, float y, float height, Color colour)
        {
            Fill(new Rect(x, y, 1f, height), colour);
        }

        /// <summary>The accent stripe that marks a selected rail entry.</summary>
        public static void SelectionMarker(Rect rect)
        {
            Fill(rect, Selected);
            Fill(new Rect(rect.x, rect.y, 4f, rect.height), Accent);
        }

        /// <summary>
        /// A label drawn in a colour other than its style's, without permanently mutating the
        /// shared style - the styles here are singletons, so assigning to one leaks everywhere.
        /// </summary>
        public static void Text(Rect rect, string content, GUIStyle style, Color colour)
        {
            var previous = style.normal.textColor;
            style.normal.textColor = colour;
            GUI.Label(rect, content, style);
            style.normal.textColor = previous;
        }

        // Truncation is measured rather than estimated from a character count, because the font
        // is proportional and a row of "Illlll" and a row of "WWWWWW" are nowhere near the same
        // width. Cached because the same handful of rows are re-measured on every event.
        private static readonly Dictionary<string, string> Elided = new Dictionary<string, string>();

        public static void ClearTextCache()
        {
            Elided.Clear();
        }

        public static string Ellipsize(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || width < 24f)
                return string.Empty;

            string key = ((int)width) + "\u0000" + text;
            if (Elided.TryGetValue(key, out string cached))
                return cached;

            var content = new GUIContent(text);
            string result = text;

            if (style.CalcSize(content).x > width)
            {
                int low = 0;
                int high = text.Length;

                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    content.text = text.Substring(0, mid) + "\u2026";

                    if (style.CalcSize(content).x <= width)
                        low = mid;
                    else
                        high = mid - 1;
                }

                result = low <= 0 ? string.Empty : text.Substring(0, low).TrimEnd() + "\u2026";
            }

            // The panel can be resized by UiScale, so the key space is not fixed; a bound keeps
            // this from growing without limit over a long session.
            if (Elided.Count > 4000)
                Elided.Clear();

            Elided[key] = result;
            return result;
        }

        private static Texture2D Solid(Color colour)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, colour);
            texture.Apply();

            // Unity destroys ordinary textures on scene load; these have to outlive that or the
            // panel loses its background the first time the player enters a battle.
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
