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

        private static void DrawList(Rect area, IList<ClassDefinition> classes)
        {
            GUI.Box(area, GUIContent.none);

            var view = new Rect(area.x, area.y, area.width, area.height - 40f);
            float rowHeight = 42f;
            var content = new Rect(0f, 0f, area.width - 20f, classes.Count * rowHeight);

            listScroll = GUI.BeginScrollView(view, listScroll, content);

            for (int i = 0; i < classes.Count; i++)
            {
                var row = new Rect(0f, i * rowHeight, content.width, rowHeight);
                bool active = i == selected;

                if (active)
                    GUI.Box(row, GUIContent.none);

                string name = string.IsNullOrEmpty(classes[i].Name) ? classes[i].Id : classes[i].Name;
                GUI.Label(new Rect(row.x + 12f, row.y + 4f, row.width - 20f, 20f), name);
                GUI.Label(new Rect(row.x + 12f, row.y + 21f, row.width - 20f, 18f),
                    UnlockLabel(classes[i]));

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

            GUILayout.Space(10f);
            GUILayout.Label("Start from an existing look");
            GUILayout.Label("Copies that look's values in. Edit the numbers afterwards in classes.json, " +
                            "or come back and copy a different one.");

            GUILayout.Space(4f);
            GUILayout.Label("— one of the game's own classes —");
            DrawPresetCopies(definition);

            GUILayout.Space(8f);
            GUILayout.Label("— one of your characters —");
            DrawCharacterCopies(definition);

            GUILayout.Space(10f);
            DrawCurrentAppearance(definition);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
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

            DrawGrid(presets.Select(p => p.PresetName).ToList(), name =>
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

            DrawGrid(characters.Select(c => c.CharacterName).Distinct().ToList(), name =>
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

        /// <summary>Buttons wrapped across the panel, so a long list does not become a long column.</summary>
        private static void DrawGrid(List<string> names, Action<string> onPick)
        {
            const int columns = 4;

            for (int i = 0; i < names.Count; i += columns)
            {
                GUILayout.BeginHorizontal();

                for (int c = 0; c < columns; c++)
                {
                    int index = i + c;
                    if (index >= names.Count)
                    {
                        GUILayout.Label(string.Empty);
                        continue;
                    }

                    if (GUILayout.Button(names[index]))
                        onPick(names[index]);
                }

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

        private static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            string result = GUILayout.TextField(value ?? string.Empty);
            GUILayout.EndHorizontal();

            if (!string.Equals(result, value ?? string.Empty, StringComparison.Ordinal))
                ClassStore.Touch();

            return result;
        }

        private static int IntField(string label, int value, string help)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(70f));

            if (!string.IsNullOrEmpty(help))
                GUILayout.Label(help);

            GUILayout.EndHorizontal();

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
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            string result = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(260f));

            if (!string.IsNullOrEmpty(result))
            {
                bool known = GameData.Ready && GameData.FindItem(result, out _) != null;
                GUILayout.Label(known ? "found" : "no item by that name");
            }

            GUILayout.EndHorizontal();

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
