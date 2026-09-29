using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The class editor, shown as a tab inside the Mod Manager.
    ///
    /// The manager finds this by convention - a public static class called ModManagerTab with a
    /// Title and a Draw(Rect) - using reflection, so neither assembly references the other and the
    /// manager keeps knowing nothing about any particular mod.
    ///
    /// Layout is a narrow list of classes on the left and three sub-tabs on the right: Character
    /// for the things that describe it, Skills for what it starts knowing, and Appearance for what
    /// it looks like. Edits go to the working copy; Save writes classes.json and pushes the values
    /// onto the preset the game is already holding, so the next character made from the class has
    /// them.
    /// </summary>
    public static class ModManagerTab
    {
        public static string Title => "Roguelike Classes";

        private enum Section { Character, Skills, Appearance }

        private const float ListWidth = 210f;

        private static Section section = Section.Character;
        private static int selected;
        private static Vector2 listScroll, bodyScroll;
        private static string status = string.Empty;
        private static string skillSearch = string.Empty;
        private static string lookSearch = string.Empty;

        public static void Refresh()
        {
            ClassStore.EnsureLoaded();
            status = ClassStore.LastError ?? string.Empty;
        }

        public static void Draw(Rect body)
        {
            ClassStore.EnsureLoaded();

            var classes = ClassStore.Classes;
            if (classes.Count == 0)
            {
                GUI.Label(new Rect(body.x + 24f, body.y + 20f, body.width - 48f, 60f),
                    string.IsNullOrEmpty(status) ? "No classes are defined in classes.json." : status);
                return;
            }

            selected = Mathf.Clamp(selected, 0, classes.Count - 1);

            DrawList(new Rect(body.x, body.y, ListWidth, body.height), classes);

            var right = new Rect(body.x + ListWidth + 1f, body.y, body.width - ListWidth - 1f, body.height);
            DrawDetail(right, classes[selected]);
        }

        /// <summary>A flat 1x1 texture, so a filled rect costs nothing to draw.</summary>
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

        // Taken from the manager's own palette so the selected row reads the same as its rail.
        private static readonly Color RowSelected = new Color(0.169f, 0.196f, 0.259f);
        private static readonly Color AccentBar = new Color(0.847f, 0.651f, 0.341f);
        private static readonly Color Divider = new Color(1f, 1f, 1f, 0.06f);

        private static void DrawList(Rect area, IList<ClassDefinition> classes)
        {
            var view = new Rect(area.x, area.y, area.width, area.height - 40f);
            float rowHeight = 42f;
            var content = new Rect(0f, 0f, area.width - 20f, classes.Count * rowHeight);

            listScroll = GUI.BeginScrollView(view, listScroll, content);

            for (int i = 0; i < classes.Count; i++)
            {
                var row = new Rect(0f, i * rowHeight, content.width, rowHeight);
                bool active = i == selected;

                if (active)
                {
                    Fill(row, RowSelected);
                    Fill(new Rect(row.x, row.y, 3f, row.height), AccentBar);
                }

                string name = string.IsNullOrEmpty(classes[i].Name) ? classes[i].Id : classes[i].Name;
                GUI.Label(new Rect(row.x + 14f, row.y + 3f, row.width - 22f, 20f), name);

                Color previous = GUI.contentColor;
                GUI.contentColor = new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(new Rect(row.x + 14f, row.y + 21f, row.width - 22f, 18f), UnlockLabel(classes[i]));
                GUI.contentColor = previous;

                Fill(new Rect(row.x, row.yMax - 1f, row.width, 1f), Divider);

                var e = Event.current;
                if (e != null && e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    selected = i;
                    skillSearch = string.Empty;
                    e.Use();
                }
            }

            GUI.EndScrollView();

            // Save sits with the list rather than in a sub-tab: it commits every class, not the
            // one being looked at.
            var save = new Rect(area.x + 10f, area.yMax - 34f, area.width - 96f, 26f);
            if (GUI.Button(save, ClassStore.Dirty ? "Save changes *" : "Save changes"))
                SaveAll();

            if (GUI.Button(new Rect(area.xMax - 82f, save.y, 72f, 26f), "Revert"))
            {
                ClassStore.Reload();
                status = "Reloaded classes.json from disk.";
            }
        }

        private static string UnlockLabel(ClassDefinition definition)
        {
            int difficulty = definition.Unlock?.Difficulty ?? 0;
            int endless = definition.Unlock?.EndlessLevel ?? 0;

            if (endless > 0) return "Endless " + endless;
            if (difficulty > 0) return "Difficulty " + difficulty;
            return "Available from the start";
        }

        private static void DrawDetail(Rect area, ClassDefinition definition)
        {
            float x = area.x + 18f;
            float y = area.y + 12f;

            GUI.Label(new Rect(x, y, area.width - 36f, 24f),
                string.IsNullOrEmpty(definition.Name) ? definition.Id : definition.Name);
            y += 28f;

            float tabWidth = 130f;
            foreach (Section value in new[] { Section.Character, Section.Skills, Section.Appearance })
            {
                var rect = new Rect(x, y, tabWidth, 26f);
                if (GUI.Toggle(rect, section == value, value.ToString(), GUI.skin.button) && section != value)
                {
                    section = value;
                    bodyScroll = Vector2.zero;
                }

                x += tabWidth + 4f;
            }

            y += 34f;

            var panel = new Rect(area.x + 18f, y, area.width - 36f, area.height - (y - area.y) - 44f);

            switch (section)
            {
                case Section.Skills: DrawSkills(panel, definition); break;
                case Section.Appearance: DrawAppearance(panel, definition); break;
                default: DrawCharacter(panel, definition); break;
            }

            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(area.x + 18f, area.yMax - 32f, area.width - 36f, 24f), status);
        }

        // ------------------------------------------------------------------ Character

        private static void DrawCharacter(Rect area, ClassDefinition definition)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            definition.Name = Field("Name", definition.Name);
            definition.Description = Field("Description", definition.Description);

            definition.Unlock = definition.Unlock ?? new UnlockDefinition();
            definition.Unlock.Difficulty = IntField("Unlock: difficulty", definition.Unlock.Difficulty,
                "0 for available from the start, otherwise 1-6.");
            definition.Unlock.EndlessLevel = IntField("Unlock: endless level", definition.Unlock.EndlessLevel,
                "Use instead of difficulty for an endless gate. 0 for none.");

            definition.Tier = IntField("Tile tier", definition.Tier ?? 2, "1, 2 or 3. Cosmetic.");

            GUILayout.Space(8f);
            GUILayout.Label("Starting attributes  (every shipped class totals 50)");

            definition.Stats = definition.Stats ?? new StatBlock();
            definition.Stats.Might = IntField("Might", definition.Stats.Might, null);
            definition.Stats.Dexterity = IntField("Dexterity", definition.Stats.Dexterity, null);
            definition.Stats.Vitality = IntField("Vitality", definition.Stats.Vitality, null);
            definition.Stats.Intelligence = IntField("Intelligence", definition.Stats.Intelligence, null);
            definition.Stats.Reflex = IntField("Reflex", definition.Stats.Reflex, null);
            GUILayout.Label("    total " + definition.Stats.Total);

            GUILayout.Space(8f);
            GUILayout.Label("Starting gear  (item names; blank leaves the slot empty)");

            definition.Equipment = definition.Equipment ?? new EquipmentBlock();
            definition.Equipment.Head = ItemField("Head", definition.Equipment.Head);
            definition.Equipment.Armor = ItemField("Armor", definition.Equipment.Armor);
            definition.Equipment.MainHand = ItemField("Main hand", definition.Equipment.MainHand);
            definition.Equipment.OffHand = ItemField("Off hand", definition.Equipment.OffHand);
            definition.Equipment.Ring = ItemField("Ring", definition.Equipment.Ring);
            definition.Equipment.Amulet = ItemField("Amulet", definition.Equipment.Amulet);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // -------------------------------------------------------------------- Skills

        private static void DrawSkills(Rect area, ClassDefinition definition)
        {
            GUILayout.BeginArea(area);

            definition.Skills = definition.Skills ?? new List<string>();

            GUILayout.Label("Starting skills  (granted at level 1, whatever their tier or prerequisite)");

            for (int i = 0; i < definition.Skills.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Describe(definition.Skills[i]), GUILayout.Width(420f));

                bool remove = GUILayout.Button("Remove", GUILayout.Width(70f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (remove)
                {
                    definition.Skills.RemoveAt(i);
                    ClassStore.Touch();
                    break;
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("Add a skill");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(60f));
            skillSearch = GUILayout.TextField(skillSearch ?? string.Empty);
            GUILayout.EndHorizontal();

            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            if (!string.IsNullOrEmpty(skillSearch) && skillSearch.Length >= 2)
            {
                foreach (SkillInfo skill in Search(skillSearch))
                {
                    if (!GUILayout.Button(skill.SkillName + "   [" + skill.SkillType + " T" + skill.Tier + "]"))
                        continue;

                    definition.Skills.Add(skill.SkillName);
                    ClassStore.Touch();
                    break;
                }
            }
            else
            {
                GUILayout.Label("Type at least two characters to search the game's skill table.");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static IEnumerable<SkillInfo> Search(string needle)
        {
            if (!GameData.Ready)
                return new SkillInfo[0];

            return Burst2Flame.Game.Instance.Skills
                .Where(x => x != null && !x.Disabled && x.SkillType != SkillType.Basic && x.SkillType != SkillType.Innate
                            && !x.DontIncludeInTree && !string.IsNullOrEmpty(x.SkillName)
                            && x.SkillName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.SkillName)
                .Take(40);
        }

        private static string Describe(string skillName)
        {
            SkillInfo skill = GameData.Ready ? GameData.FindSkill(skillName, out _) : null;
            return skill == null
                ? skillName + "   [not found]"
                : skillName + "   [" + skill.SkillType + " T" + skill.Tier + "]";
        }

        // ---------------------------------------------------------------- Appearance

        private static void DrawAppearance(Rect area, ClassDefinition definition)
        {
            GUILayout.BeginArea(area);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            GUILayout.Label("Gender");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(IsMale(definition), "Male", GUI.skin.button, GUILayout.Width(90f)) && !IsMale(definition))
            {
                definition.Gender = "Male";
                ClassStore.Touch();
            }

            if (GUILayout.Toggle(!IsMale(definition), "Female", GUI.skin.button, GUILayout.Width(90f)) && IsMale(definition))
            {
                definition.Gender = "Female";
                ClassStore.Touch();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(12f);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(420f));
            DrawCurrentAppearance(definition);
            GUILayout.EndVertical();

            GUILayout.Space(20f);
            DrawClassPreview(definition);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);
            GUILayout.Label("Start from an existing look");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(50f));
            lookSearch = GUILayout.TextField(lookSearch ?? string.Empty, GUILayout.Width(260f));
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                lookSearch = string.Empty;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(300f));
            GUILayout.Label("The game's own classes");
            DrawPresetCopies(definition);
            GUILayout.EndVertical();

            GUILayout.Space(24f);

            GUILayout.BeginVertical(GUILayout.Width(300f));
            GUILayout.Label("Your characters");
            DrawCharacterCopies(definition);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The class as the game draws it, if it has ever been shown in the character creator.
        /// There is no way to produce one without that: a class is a preset, and the game's
        /// renderer photographs a live model rather than building one from data.
        /// </summary>
        private static void DrawClassPreview(ClassDefinition definition)
        {
            GUILayout.BeginVertical(GUILayout.Width(200f));

            Texture2D shot = Preview.ForClass(definition.Id);
            if (shot != null)
            {
                Rect rect = GUILayoutUtility.GetRect(180f, 180f, GUILayout.Width(180f), GUILayout.Height(180f));
                GUI.DrawTexture(rect, shot, ScaleMode.ScaleToFit);
                GUILayout.Label("As it looks in game");
            }
            else
            {
                Rect rect = GUILayoutUtility.GetRect(180f, 180f, GUILayout.Width(180f), GUILayout.Height(180f));
                Fill(rect, new Color(1f, 1f, 1f, 0.03f));
                GUILayout.Label("No picture yet - pick this class once in the character creator and it is taken and kept.");
            }

            GUILayout.EndVertical();
        }

        private static bool IsMale(ClassDefinition definition)
        {
            return !string.Equals(definition.Gender, "Female", StringComparison.OrdinalIgnoreCase);
        }

        private static void DrawPresetCopies(ClassDefinition definition)
        {
            if (!GameData.Ready || Burst2Flame.Game.Instance.CharacterPresetFiles == null)
            {
                GUILayout.Label("The game's presets are not loaded yet.");
                return;
            }

            var presets = Burst2Flame.Game.Instance.CharacterPresetFiles
                .Where(x => x != null && !PresetInjection.IsOurs(x))
                .OrderBy(x => x.PresetName)
                .ToList();

            DrawColumn(Filter(presets.Select(p => p.PresetName).Distinct().ToList()), name =>
            {
                CharacterPresetFile preset = presets.First(p => p.PresetName == name);
                definition.Appearance = AppearanceTables.FromPreset(preset);
                definition.Gender = preset.Gender == Gender.Male ? "Male" : "Female";
                definition.AppearanceFrom = preset.PresetName;
                ClassStore.Touch();
                status = "Copied " + preset.PresetName + "'s look into " + definition.Name + ".";
            });
        }

        private static void DrawCharacterCopies(ClassDefinition definition)
        {
            List<Character> characters = MyCharacters();
            if (characters.Count == 0)
            {
                GUILayout.Label("No characters of yours are loaded. Open the character select screen, " +
                                "where the game loads them.");
                return;
            }

            if (!AppearanceTables.PartsKnown)
                GUILayout.Label("Visit the character select screen once so the part lists can be read.");

            List<string> shown = Filter(characters.Select(c => c.CharacterName).Distinct().ToList());

            DrawCharacterColumn(
                characters.Where(c => shown.Contains(c.CharacterName))
                          .GroupBy(c => c.CharacterName).Select(g => g.First())
                          .OrderBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase).ToList(),
                name =>
            {
                Character character = characters.First(c => c.CharacterName == name);
                AppearanceBlock block = AppearanceTables.FromCharacter(character, out string problem);

                if (block == null)
                {
                    status = "Could not copy " + name + ": " + problem;
                    return;
                }

                definition.Appearance = block;
                definition.Gender = character.IsMale ? "Male" : "Female";
                definition.AppearanceFrom = null;
                ClassStore.Touch();
                status = problem == null
                    ? "Copied " + name + "'s look into " + definition.Name + "."
                    : "Copied " + name + "'s look, but " + problem + ".";
            });
        }

        private static List<Character> MyCharacters()
        {
            try
            {
                if (GameLogic.instance == null || GameLogic.instance.AllMyCharacters == null)
                    return new List<Character>();

                return GameLogic.instance.AllMyCharacters
                    .Where(c => c != null && !c.IsDeleted && !string.IsNullOrEmpty(c.CharacterName))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<Character>();
            }
        }

        private static List<string> Filter(List<string> names)
        {
            names.Sort(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(lookSearch))
                return names;

            return names.Where(n => n.IndexOf(lookSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// <summary>
        /// One name per row rather than a four-wide grid of buttons. The old grid put unrelated
        /// names side by side at wildly different widths, which made a list of 46 presets read as
        /// a wall rather than as something to pick from.
        /// </summary>
        private static void DrawColumn(List<string> names, Action<string> onPick)
        {
            if (names.Count == 0)
            {
                GUILayout.Label("    nothing matches that filter");
                return;
            }

            foreach (string name in names)
            {
                if (GUILayout.Button(name, GUILayout.Width(290f)))
                    onPick(name);
            }
        }

        /// <summary>
        /// The same column, with each character's own portrait beside its name. Those already
        /// exist - the game renders and caches one per character - so this costs a draw call.
        /// </summary>
        private static void DrawCharacterColumn(List<Character> characters, Action<string> onPick)
        {
            if (characters.Count == 0)
            {
                GUILayout.Label("    nothing matches that filter");
                return;
            }

            foreach (Character character in characters)
            {
                GUILayout.BeginHorizontal();

                Texture2D portrait = Preview.ForCharacter(character);
                Rect icon = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));

                if (portrait != null)
                    GUI.DrawTexture(icon, portrait, ScaleMode.ScaleToFit);
                else
                    Fill(icon, new Color(1f, 1f, 1f, 0.04f));

                if (GUILayout.Button(character.CharacterName, GUILayout.Width(250f), GUILayout.Height(34f)))
                    onPick(character.CharacterName);

                GUILayout.EndHorizontal();
            }
        }

        private static void DrawCurrentAppearance(ClassDefinition definition)
        {
            AppearanceBlock block = definition.Appearance;
            if (block == null)
            {
                GUILayout.Label("This class has no appearance of its own; it uses "
                                + (definition.AppearanceFrom ?? "the first roguelike preset") + ".");
                return;
            }

            GUILayout.Label("Current values");
            GUILayout.Label("    hair type " + Show(block.HairType) + "    head " + Show(block.HeadType)
                            + "    eyebrows " + Show(block.EyebrowType) + "    facial hair " + Show(block.FacialHair));

            Swatch("hair colour", block.HairColor, AppearanceTables.HairColors);
            Swatch("skin colour", block.SkinColor, AppearanceTables.SkinColors);
            Swatch("eye colour", block.EyeColor, AppearanceTables.EyeColors);
            Swatch("body art", block.BodyArtColor, AppearanceTables.BodyArtColors);
        }

        private static string Show(int? value) => value.HasValue ? value.Value.ToString() : "-";

        /// <summary>The colour palettes are real colours, so the value is shown rather than described.</summary>
        private static void Swatch(string label, int? index, Color[] palette)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("    " + label + "  " + Show(index), GUILayout.Width(180f));

            if (index.HasValue && index.Value >= 0 && index.Value < palette.Length)
            {
                Rect rect = GUILayoutUtility.GetRect(40f, 16f, GUILayout.Width(40f));
                Color previous = GUI.color;
                GUI.color = palette[index.Value];
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------- small widgets

        private const float RowHeight = 26f;

        private static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label(label, GUILayout.Width(150f), GUILayout.Height(RowHeight));
            string result = GUILayout.TextField(value ?? string.Empty, GUILayout.Height(RowHeight));
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            if (!string.Equals(result, value ?? string.Empty, StringComparison.Ordinal))
                ClassStore.Touch();

            return result;
        }

        private static int IntField(string label, int value, string help)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label(label, GUILayout.Width(150f), GUILayout.Height(RowHeight));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(70f), GUILayout.Height(RowHeight));

            if (!string.IsNullOrEmpty(help))
                GUILayout.Label(help, GUILayout.Height(RowHeight));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            if (!int.TryParse(text, out int parsed) || parsed == value)
                return value;

            ClassStore.Touch();
            return parsed;
        }

        /// <summary>
        /// An item slot. Typed rather than picked from a list: there are 905 items, and a name that
        /// does not resolve is worth saying so immediately rather than at save time.
        /// </summary>
        private static string ItemField(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label(label, GUILayout.Width(150f), GUILayout.Height(RowHeight));
            string result = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(260f), GUILayout.Height(RowHeight));

            if (!string.IsNullOrEmpty(result))
            {
                bool known = GameData.Ready && GameData.FindItem(result, out _) != null;
                GUILayout.Label(known ? "found" : "no item by that name", GUILayout.Height(RowHeight));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            if (!string.Equals(result, value ?? string.Empty, StringComparison.Ordinal))
                ClassStore.Touch();

            return string.IsNullOrEmpty(result) ? null : result;
        }

        // ------------------------------------------------------------------- saving

        private static void SaveAll()
        {
            if (!ClassStore.Save())
            {
                status = ClassStore.LastError;
                return;
            }

            var trouble = new List<string>();
            foreach (ClassDefinition definition in ClassStore.Classes)
            {
                List<string> problems = ClassStore.Apply(definition);
                if (problems.Count > 0)
                    trouble.Add(definition.Id + ": " + string.Join("; ", problems.ToArray()));
            }

            status = trouble.Count == 0
                ? "Saved. New characters made from these classes use the new values."
                : "Saved, with problems — " + string.Join(" | ", trouble.ToArray());
        }
    }
}
