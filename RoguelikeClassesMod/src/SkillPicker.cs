using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Picks a class's starting skills the way the game's own skill screen presents them: one tree
    /// at a time, in rows by tier, as icons.
    ///
    /// The old version was a search box over 422 skill names, which works only if you already know
    /// what a skill is called. A tree laid out by tier is browsable instead - it answers "what can
    /// a Cold character start with" without knowing a single name - and it is the shape both the
    /// game and the planner page use, so it needs no learning.
    ///
    /// Clicking a skill both selects it, so its description shows, and adds or removes it. One
    /// click rather than two, because picking skills is the thing this editor is mostly for.
    /// </summary>
    internal static class SkillPicker
    {
        /// <summary>The trees, in the order the game numbers them, minus the two that are not trees.</summary>
        private static readonly SkillType[] Trees =
        {
            SkillType.Fire, SkillType.Lightning, SkillType.Cold, SkillType.Warrior,
            SkillType.Light, SkillType.Ranger, SkillType.Shadow, SkillType.Thief,
            SkillType.Monk, SkillType.Nature, SkillType.Chaos, SkillType.Bard,
        };

        private const float Tile = 40f;
        private const float TileGap = 4f;
        private const int MaxTier = 5;

        private static SkillType tree = SkillType.Fire;
        private static SkillInfo selected;
        private static Vector2 treeScroll;

        private static Dictionary<SkillType, List<SkillInfo>> byTree;

        internal static SkillInfo Selected => selected;

        internal static void Forget()
        {
            selected = null;
        }

        // ------------------------------------------------------------------ the tree

        internal static void DrawTree(ClassDefinition definition, Action touched)
        {
            if (!GameData.Ready)
            {
                GUILayout.Label("The game's skill table is not loaded yet.");
                return;
            }

            DrawTreeButtons();

            GUILayout.Space(10f);

            treeScroll = GUILayout.BeginScrollView(treeScroll);

            List<SkillInfo> all = InTree(tree);

            for (int tier = 1; tier <= MaxTier; tier++)
            {
                List<SkillInfo> row = all.Where(x => x.Tier == tier).ToList();
                if (row.Count == 0)
                    continue;

                DrawTierRow(tier, row, definition, touched);
            }

            GUILayout.EndScrollView();
        }

        private static void DrawTreeButtons()
        {
            // Two rows of six: twelve across would leave each button too narrow to read at the
            // width this panel has once the description column is taken out of it.
            for (int start = 0; start < Trees.Length; start += 6)
            {
                GUILayout.BeginHorizontal();

                for (int i = start; i < Math.Min(start + 6, Trees.Length); i++)
                {
                    bool active = tree == Trees[i];
                    if (GUILayout.Toggle(active, Trees[i].ToString(), GUI.skin.button, GUILayout.Width(84f)) && !active)
                    {
                        tree = Trees[i];
                        treeScroll = Vector2.zero;
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawTierRow(int tier, List<SkillInfo> skills, ClassDefinition definition, Action touched)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tier " + tier, GUILayout.Width(52f), GUILayout.Height(Tile));

            GUILayout.BeginVertical();
            DrawTiles(skills, definition, touched);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        /// <summary>
        /// Lays tiles out by hand rather than with nested horizontal groups: the number that fits
        /// depends on the width this panel happens to have, which GUILayout will not tell you until
        /// after the row is closed.
        /// </summary>
        private static void DrawTiles(List<SkillInfo> skills, ClassDefinition definition, Action touched)
        {
            float width = Mathf.Max(Tile, ModManagerTab.SkillRowWidth);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (Tile + TileGap)));
            int rows = Mathf.CeilToInt(skills.Count / (float)perRow);

            Rect area = GUILayoutUtility.GetRect(width, rows * (Tile + TileGap));

            for (int i = 0; i < skills.Count; i++)
            {
                var box = new Rect(
                    area.x + i % perRow * (Tile + TileGap),
                    area.y + i / perRow * (Tile + TileGap),
                    Tile, Tile);

                DrawTile(box, skills[i], definition, touched);
            }
        }

        private static void DrawTile(Rect box, SkillInfo skill, ClassDefinition definition, Action touched)
        {
            bool taken = Has(definition, skill);
            bool current = ReferenceEquals(selected, skill);

            ModManagerTab.Fill(box, taken
                ? new Color(0.847f, 0.651f, 0.341f, 0.22f)
                : new Color(1f, 1f, 1f, 0.04f));

            if (!Icons.Draw(new Rect(box.x + 3f, box.y + 3f, box.width - 6f, box.height - 6f), skill.Icon))
                GUI.Label(box, Initials(skill.SkillName));

            if (taken)
                ModManagerTab.Frame(box, new Color(0.847f, 0.651f, 0.341f), 2f);
            else if (current)
                ModManagerTab.Frame(box, new Color(1f, 1f, 1f, 0.45f), 1f);

            Event e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 0 || !box.Contains(e.mousePosition))
                return;

            selected = skill;

            // Deferred: the chosen list above this grid grows or shrinks by a row, and changing
            // that while the panel is mid-draw is what crashes IMGUI.
            ModManagerTab.Defer(() => Toggle(definition, skill, touched));
            e.Use();
        }

        /// <summary>A stand-in for a skill with no icon, so the tile is still identifiable.</summary>
        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "?";

            string[] words = name.Split(' ');
            return words.Length == 1
                ? name.Substring(0, Math.Min(2, name.Length))
                : (words[0].Substring(0, 1) + words[1].Substring(0, 1));
        }

        // ------------------------------------------------------------ the description

        internal static void DrawDetail(Rect area, ClassDefinition definition, Action touched)
        {
            if (selected == null)
            {
                GUI.Label(new Rect(area.x, area.y, area.width, 60f),
                    "Pick a skill to see what it does. Clicking one adds it to the class, or takes it back off.",
                    ModManagerTab.WrappedStyle());
                return;
            }

            float y = area.y;

            var icon = new Rect(area.x, y, 48f, 48f);
            ModManagerTab.Fill(icon, new Color(1f, 1f, 1f, 0.04f));
            Icons.Draw(new Rect(icon.x + 4f, icon.y + 4f, 40f, 40f), selected.Icon);

            GUI.Label(new Rect(area.x + 58f, y + 2f, area.width - 58f, 22f), selected.SkillName);
            GUI.Label(new Rect(area.x + 58f, y + 24f, area.width - 58f, 20f),
                selected.SkillType + "  ·  tier " + selected.Tier + (selected.IsPassive ? "  ·  passive" : string.Empty),
                ModManagerTab.NoteStyle());

            y += 58f;

            if (selected.Dependency != null)
            {
                GUI.Label(new Rect(area.x, y, area.width, 20f), "needs " + selected.Dependency.SkillName,
                    ModManagerTab.NoteStyle());
                y += 22f;
            }

            bool taken = Has(definition, selected);
            if (GUI.Button(new Rect(area.x, y, 150f, 26f), taken ? "Remove from class" : "Add to class"))
            {
                SkillInfo skill = selected;
                ModManagerTab.Defer(() => Toggle(definition, skill, touched));
            }

            y += 34f;

            GUIStyle wrapped = ModManagerTab.WrappedStyle();
            string text = Clean(selected.Description);
            float height = wrapped.CalcHeight(new GUIContent(text), area.width);

            GUI.Label(new Rect(area.x, y, area.width, Mathf.Min(height, area.yMax - y)), text, wrapped);
        }

        /// <summary>
        /// The description as plain text.
        ///
        /// The stored string is markup the game fills in against a real character: <c>*0</c> stands
        /// for a damage expression and <c>{STA=Bleeding}</c> for a status link. There is no
        /// character here to work them out against, so the markers come out rather than showing
        /// numbers that would be wrong - the point of this pane is what a skill does, not what it
        /// would hit for.
        /// </summary>
        internal static string Clean(string description)
        {
            if (string.IsNullOrEmpty(description))
                return "(no description)";

            string text = Regex.Replace(description, @"\{[A-Za-z]+=([^}]*)\}", "$1");   // {STA=Bleeding} -> Bleeding
            text = Regex.Replace(text, @"\*\d+\s*", string.Empty);                       // damage expression markers
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);                        // rich text tags
            text = Regex.Replace(text, @"[ \t]{2,}", " ");

            return text.Trim();
        }

        // ------------------------------------------------------------------- the list

        /// <summary>The skills a class starts with, drawn as removable rows.</summary>
        internal static void DrawChosen(ClassDefinition definition, Action touched)
        {
            definition.Skills = definition.Skills ?? new List<string>();

            if (definition.Skills.Count == 0)
            {
                GUILayout.Label("None yet. A class with no skills is left out of the game's list.",
                    ModManagerTab.NoteStyle());
                return;
            }

            for (int i = 0; i < definition.Skills.Count; i++)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28f));

                SkillInfo skill = GameData.Ready ? GameData.FindSkill(definition.Skills[i], out _) : null;

                var icon = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
                if (skill != null)
                    Icons.Draw(icon, skill.Icon);
                else
                    ModManagerTab.Fill(icon, new Color(1f, 0.4f, 0.4f, 0.15f));

                string label = skill == null
                    ? definition.Skills[i] + "   [not found]"
                    : definition.Skills[i] + "   [" + skill.SkillType + " T" + skill.Tier + "]";

                if (GUILayout.Button(label, GUILayout.Width(300f), GUILayout.Height(26f)) && skill != null)
                {
                    selected = skill;
                    tree = skill.SkillType;
                }

                bool remove = GUILayout.Button("Remove", GUILayout.Width(74f), GUILayout.Height(26f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (remove)
                {
                    int at = i;
                    ModManagerTab.Defer(() =>
                    {
                        if (at < definition.Skills.Count)
                        {
                            definition.Skills.RemoveAt(at);
                            touched();
                        }
                    });

                    break;
                }
            }
        }

        // ------------------------------------------------------------------- plumbing

        private static List<SkillInfo> InTree(SkillType which)
        {
            if (byTree == null)
            {
                byTree = Burst2Flame.Game.Instance.Skills
                    .Where(x => x != null && !x.Disabled && !x.DontIncludeInTree
                                && x.SkillType != SkillType.Basic && x.SkillType != SkillType.Innate
                                && !string.IsNullOrEmpty(x.SkillName))
                    .GroupBy(x => x.SkillType)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Tier).ThenBy(x => x.SkillName).ToList());
            }

            return byTree.TryGetValue(which, out List<SkillInfo> found) ? found : new List<SkillInfo>();
        }

        internal static void Reset() => byTree = null;

        /// <summary>
        /// Written with the tree in front when the name alone is ambiguous - a handful really do
        /// exist twice, Beast Master I being in both Ranger and Nature - so the class still points
        /// at the one that was clicked.
        /// </summary>
        private static string NameFor(SkillInfo skill)
        {
            GameData.FindSkill(skill.SkillName, out string why);
            return why != null && why.Contains("matches")
                ? skill.SkillType + ":" + skill.SkillName
                : skill.SkillName;
        }

        private static bool Has(ClassDefinition definition, SkillInfo skill)
        {
            if (definition.Skills == null)
                return false;

            foreach (string entry in definition.Skills)
            {
                SkillInfo resolved = GameData.Ready ? GameData.FindSkill(entry, out _) : null;
                if (ReferenceEquals(resolved, skill))
                    return true;
            }

            return false;
        }

        private static void Toggle(ClassDefinition definition, SkillInfo skill, Action touched)
        {
            definition.Skills = definition.Skills ?? new List<string>();

            for (int i = 0; i < definition.Skills.Count; i++)
            {
                SkillInfo resolved = GameData.Ready ? GameData.FindSkill(definition.Skills[i], out _) : null;
                if (!ReferenceEquals(resolved, skill))
                    continue;

                definition.Skills.RemoveAt(i);
                touched();
                return;
            }

            definition.Skills.Add(NameFor(skill));
            touched();
        }
    }
}
