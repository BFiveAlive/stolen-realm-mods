using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RoguelikeSlotsMod
{
    /// <summary>
    /// Picks which saved run the menu's Continue should resume.
    ///
    /// The game's own menu assumes exactly one save exists, and asks for it before you have chosen
    /// a party - so there is nowhere in its flow to say which of several runs you meant. This is
    /// that missing step, kept out of the game's UI entirely.
    ///
    /// Choosing here only changes what gets loaded. Once a run is actually underway the slot is
    /// taken from the party, so a wrong choice here can show you the wrong run but cannot write
    /// one run over another.
    /// </summary>
    public static class ModManagerTab
    {
        public static string Title => "Roguelike Saves";

        private const float RowHeight = 54f;

        private static Vector2 scroll;
        private static string status = string.Empty;
        private static List<string> slots;

        private static GUIStyle headerStyle, noteStyle, wrappedStyle;

        public static void Refresh()
        {
            slots = null;
            status = string.Empty;
        }

        public static void Draw(Rect body)
        {
            if (slots == null)
                slots = Slots.Existing();

            float y = body.y;

            GUI.Label(new Rect(body.x, y, body.width, 22f),
                "Active: " + Name(Slots.Active) + (Slots.LockedToParty ? "   (a run is in progress)" : string.Empty),
                Header());
            y += 26f;

            GUI.Label(new Rect(body.x, y, body.width, 36f),
                "Pick the run the main menu's Continue should resume. While you are playing, the "
                + "slot follows the party automatically.", Wrapped());
            y += 40f;

            if (slots.Count == 0)
            {
                GUI.Label(new Rect(body.x, y, body.width, 40f),
                    "No saved runs yet. One appears here once a roguelike game has been played.",
                    Note());
                return;
            }

            var list = new Rect(body.x, y, body.width - 20f, body.height - (y - body.y) - 40f);
            var content = new Rect(0f, 0f, list.width - 20f, slots.Count * RowHeight);

            scroll = GUI.BeginScrollView(list, scroll, content);

            for (int i = 0; i < slots.Count; i++)
            {
                string slot = slots[i];
                var row = new Rect(0f, i * RowHeight, content.width, RowHeight - 4f);

                bool active = string.Equals(slot, Slots.Active, StringComparison.Ordinal);
                if (active)
                {
                    Fill(row, new Color(0.169f, 0.196f, 0.259f));
                    Fill(new Rect(row.x, row.y, 3f, row.height), new Color(0.847f, 0.651f, 0.341f));
                }

                SlotInfo info = SlotIndex.For(slot);

                GUI.Label(new Rect(row.x + 14f, row.y + 4f, row.width - 150f, 20f),
                    info == null ? slot : info.Who);

                GUI.Label(new Rect(row.x + 14f, row.y + 24f, row.width - 150f, 18f), Detail(slot, info), Note());

                if (!active && GUI.Button(new Rect(row.xMax - 120f, row.y + 12f, 110f, 26f), "Resume this"))
                {
                    if (Slots.LockedToParty)
                    {
                        status = "A run is in progress, so the slot is following that party. "
                                 + "Leave the game to the main menu first.";
                    }
                    else
                    {
                        Slots.Activate(slot, fromParty: false);
                        status = "Continue will now resume " + Name(slot) + ".";
                    }
                }
            }

            GUI.EndScrollView();

            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(body.x, body.yMax - 34f, body.width, 30f), status, Wrapped());
        }

        private static string Name(string slot)
        {
            SlotInfo info = SlotIndex.For(slot);

            if (info != null)
                return info.Who;

            return string.IsNullOrEmpty(slot) ? "the original single save" : slot;
        }

        private static string Detail(string slot, SlotInfo info)
        {
            var parts = new List<string>();

            if (info != null && info.Difficulty >= 0)
                parts.Add("Difficulty " + (info.Difficulty + 1));

            if (info != null && info.Level > 0)
                parts.Add("level " + info.Level);

            DateTime written = Slots.WrittenAt(slot);
            if (written != DateTime.MinValue)
                parts.Add("saved " + written.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture));

            parts.Add(slot);

            return string.Join("   ·   ", parts.ToArray());
        }

        // ------------------------------------------------------------------------ styling

        private static GUIStyle Header()
        {
            EnsureStyles();
            return headerStyle;
        }

        private static GUIStyle Note()
        {
            EnsureStyles();
            return noteStyle;
        }

        private static GUIStyle Wrapped()
        {
            EnsureStyles();
            return wrappedStyle;
        }

        private static void EnsureStyles()
        {
            if (headerStyle != null)
                return;

            GUIStyle label = GUI.skin.label;
            int size = label.fontSize > 0 ? label.fontSize : 16;

            headerStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = size + 2 };
            headerStyle.normal.textColor = new Color(0.914f, 0.906f, 0.886f);

            noteStyle = new GUIStyle(label) { fontSize = Mathf.Max(11, size - 3) };
            noteStyle.normal.textColor = new Color(0.420f, 0.451f, 0.522f);

            wrappedStyle = new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            wrappedStyle.normal.textColor = new Color(0.596f, 0.627f, 0.690f);
        }

        private static Texture2D fill;

        private static void Fill(Rect rect, Color colour)
        {
            if (fill == null)
            {
                fill = new Texture2D(1, 1);
                fill.SetPixel(0, 0, Color.white);
                fill.Apply();
                fill.hideFlags = HideFlags.HideAndDontSave;
            }

            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, fill);
            GUI.color = previous;
        }
    }
}
