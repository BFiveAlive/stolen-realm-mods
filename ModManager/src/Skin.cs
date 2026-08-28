using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// IMGUI styling, built once on first use.
    ///
    /// Unity's default IMGUI skin is the grey editor look and is close to unreadable over a dark
    /// game. These styles are deliberately plain rather than an attempt to imitate Stolen Realm's
    /// art: nothing here depends on a game asset, so a game update cannot break the panel.
    /// </summary>
    internal static class Skin
    {
        public const float FieldWidth = 230f;
        public const float NumberBoxWidth = 64f;
        public const float LabelWidth = 210f;

        public static GUIStyle Window;
        public static GUIStyle Field;
        public static GUIStyle Toggle;
        public static GUIStyle Muted;
        public static GUIStyle Header;
        public static GUIStyle SectionHeader;
        public static GUIStyle Foldout;
        public static GUIStyle Tab;
        public static GUIStyle TabActive;
        public static GUIStyle Badge;
        public static GUIStyle Warning;
        public static GUIStyle SmallButton;
        public static GUIStyle Row;

        private static bool built;

        private static readonly Color Ink = new Color(0.88f, 0.87f, 0.83f);
        private static readonly Color InkMuted = new Color(0.62f, 0.61f, 0.58f);
        private static readonly Color Accent = new Color(0.85f, 0.72f, 0.42f);
        private static readonly Color Alert = new Color(0.93f, 0.62f, 0.36f);

        public static void Build()
        {
            if (built)
                return;

            built = true;

            Window = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(14, 14, 26, 12)
            };
            Window.normal.background = Fill(new Color(0.09f, 0.09f, 0.10f, 0.97f));
            Window.onNormal.background = Window.normal.background;
            Window.normal.textColor = Ink;
            Window.onNormal.textColor = Ink;
            Window.fontStyle = FontStyle.Bold;

            Field = new GUIStyle(GUI.skin.textField);
            Field.normal.textColor = Ink;
            Field.focused.textColor = Color.white;

            Toggle = new GUIStyle(GUI.skin.toggle);
            Toggle.normal.textColor = InkMuted;
            Toggle.onNormal.textColor = Ink;
            Toggle.hover.textColor = Ink;
            Toggle.onHover.textColor = Ink;

            Muted = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 11
            };
            Muted.normal.textColor = InkMuted;

            Header = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            Header.normal.textColor = Accent;

            SectionHeader = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                margin = new RectOffset(8, 0, 8, 2)
            };
            SectionHeader.normal.textColor = InkMuted;

            // A left-aligned button reads as a clickable header rather than as a control.
            Foldout = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 5, 5)
            };
            Foldout.normal.textColor = Ink;

            Tab = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(16, 16, 6, 6)
            };
            Tab.normal.textColor = InkMuted;

            TabActive = new GUIStyle(Tab)
            {
                fontStyle = FontStyle.Bold
            };
            TabActive.normal.textColor = Accent;
            TabActive.normal.background = Fill(new Color(0.20f, 0.19f, 0.16f, 1f));

            Badge = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight
            };
            Badge.normal.textColor = Accent;

            Warning = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 12
            };
            Warning.normal.textColor = Alert;

            SmallButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 3, 3)
            };

            Row = new GUIStyle
            {
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        private static Texture2D Fill(Color colour)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, colour);
            texture.Apply();

            // Unity destroys ordinary textures on scene load; this one has to outlive that or the
            // panel loses its background the first time the player enters a battle.
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
