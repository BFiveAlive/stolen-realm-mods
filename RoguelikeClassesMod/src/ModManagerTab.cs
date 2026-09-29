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
        private static string lookSearch = string.Empty;

        /// <summary>
        /// The look most recently copied in, so the preview can show what was taken rather than
        /// the class's own older picture - which, after a copy, is no longer what it looks like.
        /// Held per class id, so switching classes falls back to that class's own picture.
        /// </summary>
        private static string copiedForClass;
        private static string copiedLabel;
        private static Shot copiedShot;

        /// <summary>Width of each "start from an existing look" column, set from the panel width.</summary>
        private static float copyColumnWidth = 280f;

        public static void Refresh()
        {
            ClassStore.EnsureLoaded();

            // Both pickers index the game's tables once and keep the result. Dropping those here
            // means reopening the manager picks up a table that was not loaded the first time.
            SkillPicker.Reset();
            ItemPicker.Reset();

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

        internal static void Fill(Rect rect, Color colour)
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

        /// <summary>An outline, for marking a tile as chosen without hiding the icon under it.</summary>
        internal static void Frame(Rect rect, Color colour, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
        }

        // Taken from the manager's own palette so the selected row reads the same as its rail.
        private static readonly Color RowSelected = new Color(0.169f, 0.196f, 0.259f);
        private static readonly Color AccentBar = new Color(0.847f, 0.651f, 0.341f);
        private static readonly Color Divider = new Color(1f, 1f, 1f, 0.06f);

        private static void DrawList(Rect area, IList<ClassDefinition> classes)
        {
            var view = new Rect(area.x, area.y, area.width, area.height - 74f);
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
                    SkillPicker.Forget();
                    e.Use();
                }
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(area.x + 10f, area.yMax - 66f, area.width - 20f, 26f), "New class"))
                NewClass();

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

        /// <summary>
        /// Starts a class off with the shape every shipped one has: 50 attribute points spread
        /// evenly, available from the start, no gear and no skills. It is not playable until it
        /// has at least one skill - a preset with none is refused rather than added half-built -
        /// and it reaches the game on Save, like every other edit.
        /// </summary>
        private static void NewClass()
        {
            var definition = new ClassDefinition
            {
                Id = FreshId(),
                Name = "New class",
                Description = string.Empty,
                Tier = 2,
                Unlock = new UnlockDefinition(),
                Stats = new StatBlock { Might = 10, Dexterity = 10, Vitality = 10, Intelligence = 10, Reflex = 10 },
                Skills = new List<string>(),
                Equipment = new EquipmentBlock(),
            };

            ClassStore.Add(definition);

            selected = ClassStore.Classes.Count - 1;
            section = Section.Character;
            SkillPicker.Forget();
            bodyScroll = Vector2.zero;
            listScroll.y = float.MaxValue;      // clamped by the scroll view; puts the new row in sight

            status = "Added a class. Give it a name and at least one skill, then Save changes.";
        }

        /// <summary>
        /// An id no existing class uses. It is not cosmetic: the preset's Guid is derived from it
        /// and written into every character made from the class, so it has to be settled before
        /// anyone plays the class and left alone afterwards.
        /// </summary>
        private static string FreshId()
        {
            var taken = new HashSet<string>(
                ClassStore.Classes.Where(c => c != null && c.Id != null).Select(c => c.Id),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 1; ; i++)
            {
                string candidate = "custom-" + i;
                if (!taken.Contains(candidate))
                    return candidate;
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
            // Same shape as the Appearance tab: the editable fields scroll on the left, and a fixed
            // column on the right shows what the gear slot being edited can hold and what it does.
            float panelWidth = Mathf.Min(340f, area.width * 0.36f);
            var content = new Rect(area.x, area.y, area.width - panelWidth - 20f, area.height);

            ItemPicker.DrawPanel(new Rect(content.xMax + 20f, area.y, panelWidth, area.height),
                definition, ClassStore.Touch);

            GUILayout.BeginArea(content);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            Header("Identity");

            // Shown, not editable: characters already made resolve back to their class by a Guid
            // derived from this, so changing it would orphan them.
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label("Id", GUILayout.Width(150f), GUILayout.Height(RowHeight));
            GUILayout.Label(definition.Id ?? "-", GUILayout.Width(200f), GUILayout.Height(RowHeight));
            GUILayout.Label("fixed; characters made from this class are tied to it", noteStyle ?? GUI.skin.label,
                GUILayout.Height(RowHeight));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            definition.Name = Field("Name", definition.Name);
            definition.Description = Field("Description", definition.Description);

            definition.Unlock = definition.Unlock ?? new UnlockDefinition();
            definition.Unlock.Difficulty = IntField("Unlock: difficulty", definition.Unlock.Difficulty,
                "0 for available from the start, otherwise 1-6.");
            definition.Unlock.EndlessLevel = IntField("Unlock: endless level", definition.Unlock.EndlessLevel,
                "Use instead of difficulty for an endless gate. 0 for none.");

            definition.Tier = IntField("Tile tier", definition.Tier ?? 2, "1, 2 or 3. Cosmetic.");

            Header("Starting attributes", "every shipped class totals 50");

            definition.Stats = definition.Stats ?? new StatBlock();
            definition.Stats.Might = IntField("Might", definition.Stats.Might, null);
            definition.Stats.Dexterity = IntField("Dexterity", definition.Stats.Dexterity, null);
            definition.Stats.Vitality = IntField("Vitality", definition.Stats.Vitality, null);
            definition.Stats.Intelligence = IntField("Intelligence", definition.Stats.Intelligence, null);
            definition.Stats.Reflex = IntField("Reflex", definition.Stats.Reflex, null);
            GUILayout.Label("    total " + definition.Stats.Total);

            Header("Starting gear", "click a slot to choose from what the game has");

            definition.Equipment = definition.Equipment ?? new EquipmentBlock();
            EquipmentBlock gear = definition.Equipment;

            gear.Head = ItemPicker.DrawSlot(GearSlot.Head, "Head", gear.Head, ClassStore.Touch);
            gear.Armor = ItemPicker.DrawSlot(GearSlot.Armor, "Armor", gear.Armor, ClassStore.Touch);
            gear.MainHand = ItemPicker.DrawSlot(GearSlot.MainHand, "Main hand", gear.MainHand, ClassStore.Touch);
            gear.OffHand = ItemPicker.DrawSlot(GearSlot.OffHand, "Off hand", gear.OffHand, ClassStore.Touch);
            gear.Ring = ItemPicker.DrawSlot(GearSlot.Ring, "Ring", gear.Ring, ClassStore.Touch);
            gear.Amulet = ItemPicker.DrawSlot(GearSlot.Amulet, "Amulet", gear.Amulet, ClassStore.Touch);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // -------------------------------------------------------------------- Skills

        /// <summary>Width the skill grid has to lay tiles out in; set as the tab is drawn.</summary>
        internal static float SkillRowWidth = 400f;

        private static void DrawSkills(Rect area, ClassDefinition definition)
        {
            float panelWidth = Mathf.Min(340f, area.width * 0.36f);
            var content = new Rect(area.x, area.y, area.width - panelWidth - 20f, area.height);

            SkillPicker.DrawDetail(new Rect(content.xMax + 20f, area.y, panelWidth, area.height),
                definition, ClassStore.Touch);

            // The tile grid needs a width before GUILayout would report one, and the tier rows sit
            // behind a label, so it is worked out here rather than measured.
            SkillRowWidth = content.width - 90f;

            GUILayout.BeginArea(content);

            Header("Starting skills", "granted at level 1, whatever their tier or prerequisite");
            SkillPicker.DrawChosen(definition, ClassStore.Touch);

            Header("Add from a tree", "click to add or remove");
            SkillPicker.DrawTree(definition, ClassStore.Touch);

            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- Appearance

        private static void DrawAppearance(Rect area, ClassDefinition definition)
        {
            // The preview is a column of its own rather than a row inside the scrolling content:
            // it is what the copy lists below are for, so it should stay in sight while they are
            // scrolled, and a fixed column is the only place tall enough to show it at a useful size.
            float previewWidth = Mathf.Min(300f, area.width * 0.32f);
            var content = new Rect(area.x, area.y, area.width - previewWidth - 20f, area.height);
            DrawClassPreview(new Rect(content.xMax + 20f, area.y, previewWidth, area.height), definition);

            // The two copy lists share whatever the preview column left, rather than each taking a
            // fixed 300, which no longer fits beside it.
            copyColumnWidth = Mathf.Max(170f, (content.width - 60f) * 0.5f);

            GUILayout.BeginArea(content);
            bodyScroll = GUILayout.BeginScrollView(bodyScroll);

            Header("Gender");
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
            DrawCurrentAppearance(definition);

            Header("Start from an existing look", "the look is copied in; the class keeps it from then on");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(50f));
            lookSearch = GUILayout.TextField(lookSearch ?? string.Empty, GUILayout.Width(260f));
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                lookSearch = string.Empty;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(copyColumnWidth));
            GUILayout.Label("The game's own classes");
            DrawPresetCopies(definition);
            GUILayout.EndVertical();

            GUILayout.Space(24f);

            GUILayout.BeginVertical(GUILayout.Width(copyColumnWidth));
            GUILayout.Label("Your characters");
            DrawCharacterCopies(definition);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// What the class looks like: its own picture normally, or the look just copied in from
        /// the list below, since after a copy the class's own older picture is no longer accurate.
        ///
        /// The face sits above the body, and the body gets whatever height is left, because the
        /// body is the one worth looking at closely and a face reads fine small.
        ///
        /// A picture only exists for something the character creator has shown, here or in normal
        /// play. There is no way to produce one otherwise: a class is a preset, and the game's
        /// renderer photographs a live model rather than building one from data. Characters of
        /// yours are the exception - the game renders and caches both views of every one of those.
        /// </summary>
        private static void DrawClassPreview(Rect area, ClassDefinition definition)
        {
            bool copied = string.Equals(copiedForClass, definition.Id, StringComparison.Ordinal);
            Shot shot = copied ? copiedShot : Preview.ForClass(definition.Id);

            const float gap = 10f;
            const float captionHeight = 56f;

            float available = Mathf.Max(200f, area.height - captionHeight - gap * 2f);
            float faceHeight = Mathf.Min(area.width, available * 0.36f);
            float bodyHeight = available - faceHeight - gap;

            DrawShot(new Rect(area.x, area.y, area.width, faceHeight), shot == null ? null : shot.Face);
            DrawShot(new Rect(area.x, area.y + faceHeight + gap, area.width, bodyHeight),
                shot == null ? null : shot.Body);

            GUI.Label(new Rect(area.x, area.y + available + gap * 2f, area.width, captionHeight),
                Caption(copied, shot != null), CaptionStyle());
        }

        private static string Caption(bool copied, bool havePicture)
        {
            if (havePicture)
                return copied ? "Copied from " + copiedLabel : "As it looks in game";

            return copied
                ? "Copied from " + copiedLabel + " - no picture of that one yet. Open it once in the "
                  + "character creator and one is taken and kept."
                : "No picture yet - pick this class once in the character creator and it is taken and kept.";
        }

        private static GUIStyle wrappedStyle, listItemStyle;

        /// <summary>Body text that wraps - the skin's own label does not.</summary>
        internal static GUIStyle WrappedStyle()
        {
            if (wrappedStyle == null)
            {
                wrappedStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
                wrappedStyle.normal.textColor = new Color(0.596f, 0.627f, 0.690f);
            }

            return wrappedStyle;
        }

        /// <summary>The dim small text used for hints beside a heading or a field.</summary>
        internal static GUIStyle NoteStyle()
        {
            EnsureHeadingStyles();
            return noteStyle;
        }

        /// <summary>A row in a long list: left aligned, and quiet until it is under the pointer.</summary>
        internal static GUIStyle ListItemStyle()
        {
            if (listItemStyle == null)
            {
                listItemStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 0, 0),
                };
                listItemStyle.hover.textColor = new Color(0.914f, 0.906f, 0.886f);
                listItemStyle.hover.background = listItemStyle.normal.background;
            }

            return listItemStyle;
        }

        private static GUIStyle captionStyle;

        private static GUIStyle CaptionStyle()
        {
            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    alignment = TextAnchor.UpperCenter,
                    fontSize = Mathf.Max(11, (GUI.skin.label.fontSize > 0 ? GUI.skin.label.fontSize : 16) - 2),
                };
                captionStyle.normal.textColor = new Color(0.596f, 0.627f, 0.690f);
            }

            return captionStyle;
        }

        /// <summary>
        /// Draws a render as large as its box allows, keeping its shape.
        ///
        /// Fitting rather than cropping, because the renders arrive trimmed to the figure - see
        /// <see cref="Preview.Trim"/>. Untrimmed, they are square with the figure filling about a
        /// quarter of the middle, and fitting one of those into a tall box wastes most of it.
        /// </summary>
        private static void DrawShot(Rect box, Texture2D texture)
        {
            if (texture == null)
            {
                Fill(box, new Color(1f, 1f, 1f, 0.03f));
                return;
            }

            GUI.DrawTexture(box, texture, ScaleMode.ScaleToFit);
        }

        /// <summary>Remembers what a class's look was just taken from, for the preview above.</summary>
        private static void RememberCopy(string classId, string label, Shot shot)
        {
            copiedForClass = classId;
            copiedLabel = label;
            copiedShot = shot;
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
                RememberCopy(definition.Id, preset.PresetName,
                    Preview.ForKey(Preview.KeyForPreset(preset.PresetName)));
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
                RememberCopy(definition.Id, name, Preview.ForCharacterLarge(character));
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
                if (GUILayout.Button(name, GUILayout.Width(copyColumnWidth)))
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

                // The face, not the body: at 34 pixels a full body is a smudge.
                Shot shot = Preview.ForCharacter(character);
                Texture2D portrait = shot == null ? null : (shot.Face ?? shot.Body);
                Rect icon = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));

                if (portrait != null)
                    GUI.DrawTexture(icon, portrait, ScaleMode.ScaleToFit);
                else
                    Fill(icon, new Color(1f, 1f, 1f, 0.04f));

                if (GUILayout.Button(character.CharacterName, GUILayout.Width(copyColumnWidth - 40f), GUILayout.Height(34f)))
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

        private static GUIStyle headerStyle, noteStyle;

        /// <summary>
        /// A section heading, with room above it and a hairline under it.
        ///
        /// The manager hands panels a skin rather than named styles, so every label in here came
        /// out at one weight and one colour - which is what made "Starting attributes" read as
        /// just another field label and let the sections run together. These derive from the
        /// skin's own label, so they keep its font and only change what separates a heading from
        /// a row.
        /// </summary>
        private static void EnsureHeadingStyles()
        {
            if (headerStyle != null)
                return;

            GUIStyle label = GUI.skin.label;
            int size = label.fontSize > 0 ? label.fontSize : 16;

            headerStyle = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = size + 2 };
            headerStyle.normal.textColor = new Color(0.914f, 0.906f, 0.886f);

            noteStyle = new GUIStyle(label) { fontSize = Mathf.Max(11, size - 3) };
            noteStyle.normal.textColor = new Color(0.420f, 0.451f, 0.522f);
        }

        private static void Header(string text, string note = null)
        {
            EnsureHeadingStyles();

            GUILayout.Space(20f);

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
            GUILayout.Space(10f);
        }

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
