using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Browses the run archive, as a tab inside the Mod Manager.
    ///
    /// The manager finds this by convention - a public static class called ModManagerTab with a
    /// Title and a Draw(Rect) - using reflection, so neither assembly references the other.
    ///
    /// Runs down the left, newest first; the selected one on the right, under three views, because
    /// a run holds three different kinds of thing and one scrolling wall would bury all of them.
    /// </summary>
    public static class ModManagerTab
    {
        public static string Title => "Run Archive";

        private enum View { Party, Battles, Totals, Live }

        private const float ListWidth = 260f;
        private const float RowHeight = 24f;

        private static View view = View.Party;
        private static int selected;
        private static Vector2 listScroll, bodyScroll;
        private static string status = string.Empty;

        private static GUIStyle headerStyle, noteStyle, wrappedStyle;

        public static void Refresh()
        {
            RunArchive.Forget();
            status = string.Empty;
        }

        public static void Draw(Rect body)
        {
            List<RunRecord> runs = RunArchive.All();

            if (runs.Count == 0)
            {
                GUI.Label(new Rect(body.x + 24f, body.y + 20f, body.width - 48f, 80f),
                    "No runs recorded yet. Finish a roguelike game with archiving on and it will "
                    + "appear here.\n\nArchiving is a setting of this mod, under Data.", Wrapped());
                return;
            }

            selected = Mathf.Clamp(selected, 0, runs.Count - 1);

            DrawList(new Rect(body.x, body.y, ListWidth, body.height), runs);

            var right = new Rect(body.x + ListWidth + 12f, body.y, body.width - ListWidth - 12f, body.height);
            DrawDetail(right, runs[selected]);
        }

        // --------------------------------------------------------------------------- the list

        private static readonly Color RowSelected = new Color(0.169f, 0.196f, 0.259f);
        private static readonly Color AccentBar = new Color(0.847f, 0.651f, 0.341f);
        private static readonly Color Divider = new Color(1f, 1f, 1f, 0.06f);

        private static void DrawList(Rect area, List<RunRecord> runs)
        {
            var view = new Rect(area.x, area.y, area.width, area.height - 34f);
            float row = 46f;
            var content = new Rect(0f, 0f, area.width - 20f, runs.Count * row);

            listScroll = GUI.BeginScrollView(view, listScroll, content);

            for (int i = 0; i < runs.Count; i++)
            {
                var line = new Rect(0f, i * row, content.width, row);

                if (i == selected)
                {
                    Fill(line, RowSelected);
                    Fill(new Rect(line.x, line.y, 3f, line.height), AccentBar);
                }

                GUI.Label(new Rect(line.x + 14f, line.y + 3f, line.width - 22f, 20f), When(runs[i]));

                Color previous = GUI.contentColor;
                GUI.contentColor = new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(new Rect(line.x + 14f, line.y + 23f, line.width - 22f, 18f), Summary(runs[i]));
                GUI.contentColor = previous;

                Fill(new Rect(line.x, line.yMax - 1f, line.width, 1f), Divider);

                Event e = Event.current;
                if (e != null && e.type == EventType.MouseDown && e.button == 0 && line.Contains(e.mousePosition))
                {
                    selected = i;
                    bodyScroll = Vector2.zero;
                    e.Use();
                }
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(area.x + 10f, area.yMax - 28f, 110f, 24f), "Reload"))
                Refresh();

            if (GUI.Button(new Rect(area.x + 128f, area.yMax - 28f, 110f, 24f), "Open folder"))
                Reveal();
        }

        private static string When(RunRecord run)
        {
            DateTime started = run.Started;
            return started == DateTime.MinValue
                ? (run.Id ?? "a run")
                : started.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }

        private static string Summary(RunRecord run)
        {
            string who = run.Party.Count == 0 ? "no party" : run.Party[0].Name;
            if (run.Party.Count > 1)
                who += " +" + (run.Party.Count - 1);

            return who + "  ·  " + run.Outcome + "  ·  " + run.Battles.Count + " battles";
        }

        // ------------------------------------------------------------------------- the detail

        private static void DrawDetail(Rect area, RunRecord run)
        {
            float y = area.y;

            GUI.Label(new Rect(area.x, y, area.width, 24f), When(run) + "   -   " + run.Outcome);
            y += 26f;

            GUI.Label(new Rect(area.x, y, area.width, 20f),
                "Difficulty " + run.Difficulty + "   ·   reached level " + run.LevelReached
                + "   ·   " + run.Battles.Count + " battles", Note());
            y += 26f;

            float tab = 120f;
            float x = area.x;

            foreach (View value in new[] { View.Party, View.Battles, View.Totals, View.Live })
            {
                if (GUI.Toggle(new Rect(x, y, tab, 26f), view == value, value.ToString(), GUI.skin.button)
                    && view != value)
                {
                    view = value;
                    bodyScroll = Vector2.zero;
                }

                x += tab + 4f;
            }

            y += 34f;

            var panel = new Rect(area.x, y, area.width, area.height - (y - area.y) - 8f);

            switch (view)
            {
                case View.Battles: DrawBattles(panel, run); break;
                case View.Totals: DrawTotals(panel, run); break;
                case View.Live: DrawLive(panel); break;
                default: DrawParty(panel, run); break;
            }
        }

        private static void DrawParty(Rect area, RunRecord run)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            if (run.Party.Count == 0)
                GUILayout.Label("No party was recorded for this run.", Note());

            foreach (MemberRecord member in run.Party)
            {
                Header(member.Name, "level " + member.Level
                                    + (string.IsNullOrEmpty(member.StartingClass)
                                        ? string.Empty
                                        : "  ·  " + member.StartingClass));

                if (member.Attributes.Count > 0)
                {
                    GUILayout.Label("Attributes   " + string.Join("   ",
                        member.Attributes.Select(x => x.Key + " " + Num(x.Value)).ToArray()));
                }

                GUILayout.Label("Trees removed   " + (member.TreesRemoved.Count == 0
                    ? "none"
                    : string.Join(", ", member.TreesRemoved.ToArray())));

                GUILayout.Space(6f);
                GUILayout.Label("Equipment", Note());
                GUILayout.Label(member.Equipment.Count == 0
                    ? "    nothing recorded"
                    : "    " + string.Join("\n    ", member.Equipment.ToArray()), Wrapped());

                GUILayout.Space(6f);
                GUILayout.Label("Skills  (" + member.Skills.Count + ")", Note());
                GUILayout.Label(member.Skills.Count == 0
                    ? "    none recorded"
                    : "    " + string.Join("\n    ", member.Skills.ToArray()), Wrapped());

                GUILayout.Space(10f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawBattles(Rect area, RunRecord run)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            if (run.Battles.Count == 0)
                GUILayout.Label("No battles were recorded for this run.", Note());

            foreach (BattleRecord battle in run.Battles)
            {
                Header("Battle " + battle.Number, Ended(battle));

                float party = DerivedStats.PartyDamage(battle.Stats);

                foreach (KeyValuePair<string, Dictionary<string, float>> who in battle.Stats)
                {
                    GUILayout.Label("  " + who.Key);
                    DrawStatRows(who.Value);
                    DrawExtraRows(DerivedStats.For(who.Value, party), Records(battle.Records, who.Key));
                    GUILayout.Space(4f);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawTotals(Rect area, RunRecord run)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            if (run.Totals.Count == 0)
                GUILayout.Label("No totals were recorded for this run.", Note());

            float party = DerivedStats.PartyDamage(run.Totals);

            foreach (KeyValuePair<string, Dictionary<string, float>> who in run.Totals)
            {
                Header(who.Key, "whole run");
                DrawStatRows(who.Value);
                DrawExtraRows(DerivedStats.For(who.Value, party), Records(run.Records, who.Key));
                GUILayout.Space(8f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The battle being fought right now, straight out of the trackers rather than off disk.
        ///
        /// The archive is written when a battle ends, so mid-fight it has nothing to say about
        /// the fight you are in. This reads the same live data the game's own stats window reads,
        /// plus the records, so the current battle is visible while it is happening.
        /// </summary>
        private static void DrawLive(Rect area)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            Dictionary<string, Dictionary<string, float>> live = StatTracker.LiveByName();
            Dictionary<string, Dictionary<string, float>> records = ExtraStats.Peek();

            if (live.Count == 0 && records.Count == 0)
            {
                GUILayout.Label("Nothing is being fought right now. This view fills in during a battle.",
                    Note());
            }
            else
            {
                float party = DerivedStats.PartyDamage(live);

                foreach (KeyValuePair<string, Dictionary<string, float>> who in live)
                {
                    Header(who.Key, "this battle, so far");
                    DrawStatRows(who.Value);
                    DrawExtraRows(DerivedStats.For(who.Value, party), Records(records, who.Key));
                    GUILayout.Space(8f);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static Dictionary<string, float> Records(
            Dictionary<string, Dictionary<string, float>> all, string who)
        {
            if (all != null && all.TryGetValue(who, out Dictionary<string, float> found))
                return found;

            return new Dictionary<string, float>();
        }

        /// <summary>
        /// The worked-out rows and the records, under the raw numbers and set apart from them, so
        /// it stays obvious which figures the game itself keeps and which this mod works out.
        /// </summary>
        private static void DrawExtraRows(Dictionary<string, float> derived, Dictionary<string, float> records)
        {
            if (derived.Count == 0 && records.Count == 0)
                return;

            GUILayout.Space(4f);

            foreach (KeyValuePair<string, float> row in derived)
                DrawRow(row.Key, row.Value);

            foreach (KeyValuePair<string, float> row in records.OrderByDescending(x => x.Value))
                DrawRow(row.Key, row.Value);
        }

        /// <summary>Biggest first: the line that matters is usually the one with the most in it.</summary>
        private static void DrawStatRows(Dictionary<string, float> stats)
        {
            foreach (KeyValuePair<string, float> stat in stats.OrderByDescending(x => x.Value))
                DrawRow(stat.Key, stat.Value);
        }

        private static void DrawRow(string key, float value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label("      " + (DerivedStats.Label(key) ?? Pretty(key)),
                GUILayout.Width(280f), GUILayout.Height(RowHeight));
            GUILayout.Label(Format(key, value), GUILayout.Width(100f), GUILayout.Height(RowHeight));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static string Format(string key, float value)
        {
            if (DerivedStats.IsPercent(key))
                return value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

            if (DerivedStats.IsRatio(key))
                return value.ToString("0.00", CultureInfo.InvariantCulture);

            return Num(value);
        }

        private static string Ended(BattleRecord battle)
        {
            return DateTime.TryParse(battle.EndedUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out DateTime value)
                ? value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary>DamageDealt reads better as Damage Dealt.</summary>
        private static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var text = new System.Text.StringBuilder(name.Length + 6);

            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    text.Append(' ');

                text.Append(name[i]);
            }

            return text.ToString();
        }

        private static string Num(float value)
        {
            return Mathf.Abs(value - Mathf.Round(value)) < 0.01f
                ? Mathf.RoundToInt(value).ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString("N1", CultureInfo.InvariantCulture);
        }

        private static void Reveal()
        {
            try
            {
                System.IO.Directory.CreateDirectory(RunArchive.Folder);
                System.Diagnostics.Process.Start(RunArchive.Folder);
            }
            catch (Exception e)
            {
                status = "Could not open the folder: " + e.Message;
                Plugin.Log.LogWarning(status);
            }
        }

        // ------------------------------------------------------------------------- the styling

        private static void Header(string text, string note)
        {
            EnsureStyles();

            GUILayout.Space(14f);

            GUILayout.BeginHorizontal(GUILayout.Height(24f));
            GUILayout.Label(text, headerStyle, GUILayout.Height(24f));

            if (!string.IsNullOrEmpty(note))
            {
                GUILayout.Space(10f);
                GUILayout.Label(note, noteStyle, GUILayout.Height(24f));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Fill(GUILayoutUtility.GetRect(10f, 1f, GUILayout.Height(1f)), Divider);
            GUILayout.Space(8f);
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

        /// <summary>
        /// The manager hands panels a skin rather than named styles, so everything arrives at one
        /// weight and colour. These are derived from that skin's own label, so they keep its font
        /// and change only what separates a heading from a row.
        /// </summary>
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
