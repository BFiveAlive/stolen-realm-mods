using UnityEngine;

namespace AutoEquipMod
{
    /// <summary>
    /// Styling for the prompt, built once on first use. Nothing here touches a game asset, so a
    /// game update cannot take the prompt's appearance with it.
    /// </summary>
    internal static class Skin
    {
        public static readonly Color Panel    = new Color(0.098f, 0.110f, 0.141f, 0.97f);
        public static readonly Color Ink      = new Color(0.914f, 0.906f, 0.886f);
        public static readonly Color InkMuted = new Color(0.596f, 0.627f, 0.690f);
        public static readonly Color InkDim   = new Color(0.420f, 0.451f, 0.522f);
        public static readonly Color Accent   = new Color(0.851f, 0.643f, 0.255f);
        public static readonly Color Good     = new Color(0.435f, 0.749f, 0.451f);
        public static readonly Color Bad      = new Color(0.878f, 0.400f, 0.400f);

        public static GUIStyle Heading;
        public static GUIStyle Title;
        public static GUIStyle Body;
        public static GUIStyle Button;

        private static bool built;
        private static Texture2D white;

        public static void Build()
        {
            if (built)
                return;

            built = true;
            white = Solid(Color.white);

            Heading = Label(14, FontStyle.Bold, Accent);
            Title = Label(18, FontStyle.Bold, Ink);
            Body = Label(15, FontStyle.Normal, InkMuted);

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                padding = new RectOffset(10, 10, 6, 6)
            };
            Button.normal.background = Solid(new Color(0.125f, 0.141f, 0.180f));
            Button.hover.background = Solid(new Color(0.153f, 0.173f, 0.220f));
            Button.active.background = Solid(new Color(0.200f, 0.251f, 0.353f));
            Button.normal.textColor = InkMuted;
            Button.hover.textColor = Ink;
            Button.active.textColor = Ink;
        }

        private static GUIStyle Label(int size, FontStyle weight, Color colour)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            style.normal.textColor = colour;
            style.hover.textColor = colour;
            return style;
        }

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

        /// <summary>
        /// A label in a colour other than its style's, without mutating the shared style - these
        /// are singletons, so assigning to one leaks everywhere.
        /// </summary>
        public static void Text(Rect rect, string content, GUIStyle style, Color colour)
        {
            var previous = style.normal.textColor;
            style.normal.textColor = colour;
            GUI.Label(rect, content, style);
            style.normal.textColor = previous;
        }

        private static Texture2D Solid(Color colour)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, colour);
            texture.Apply();

            // Unity destroys ordinary textures on scene load; this one has to outlive that or the
            // prompt loses its background the first time the player changes area.
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
