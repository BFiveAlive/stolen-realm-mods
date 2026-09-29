using System;
using System.Collections.Generic;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The lookup tables a character's appearance is expressed in, and the conversion between the
    /// two ways the game stores one.
    ///
    /// A <c>CharacterPresetFile</c> stores a look as indices - hair type 12, skin colour 3. A
    /// <c>Character</c> stores the same look as <c>ChosenColors</c> (hex strings) and
    /// <c>ActiveVisuals</c> (the names of the part GameObjects that are switched on). The game
    /// converts one way in <c>PresetManager.SyncModelToPreset</c>; importing a character you made
    /// with the game's own creator means inverting that, which is what this class is for.
    ///
    /// Colours come from <c>GlobalSettings</c> and are always available. The part lists live on
    /// <c>PresetManager</c>, which only exists on the menu screens, so their names are snapshotted
    /// the first time it is seen and kept - the editor is usable afterwards from anywhere.
    /// </summary>
    internal static class AppearanceTables
    {
        private static string[] hairMale, hairFemale, headsMale, headsFemale;
        private static string[] eyebrowsMale, eyebrowsFemale, facialHairMale;

        internal static bool PartsKnown => hairMale != null;

        internal static Color[] SkinColors => Palette(gs => gs.skinColorOptions);
        internal static Color[] HairColors => Palette(gs => gs.hairColorOptions);
        internal static Color[] EyeColors => Palette(gs => gs.eyeColorOptions);
        internal static Color[] BodyArtColors => Palette(gs => gs.bodyArtColorOptions);

        private static Color[] Palette(Func<GlobalSettings, Color[]> pick)
        {
            try
            {
                GlobalSettings settings = GlobalSettingsManager.instance?.globalSettings;
                return settings == null ? new Color[0] : (pick(settings) ?? new Color[0]);
            }
            catch (Exception)
            {
                return new Color[0];
            }
        }

        /// <summary>Called every frame; snapshots the part names once PresetManager exists.</summary>
        internal static void TryCapture()
        {
            if (PartsKnown)
                return;

            PresetManager manager = PresetManager.Instance;
            if (manager == null)
                return;

            try
            {
                hairMale = Names(manager.HairMale);
                hairFemale = Names(manager.HairFemale);
                headsMale = Names(manager.HeadsMale);
                headsFemale = Names(manager.HeadsFemale);
                eyebrowsMale = Names(manager.EyebrowsMale);
                eyebrowsFemale = Names(manager.EyebrowsFemale);
                facialHairMale = Names(manager.FacialHairMale);

                Plugin.Log.LogInfo($"Appearance tables read: {hairMale.Length}/{hairFemale.Length} hair, " +
                                   $"{headsMale.Length}/{headsFemale.Length} heads, " +
                                   $"{eyebrowsMale.Length}/{eyebrowsFemale.Length} eyebrows, " +
                                   $"{facialHairMale.Length} facial hair.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the appearance tables: " + e.Message);
                hairMale = null;
            }
        }

        private static string[] Names(GameObject[] parts)
        {
            if (parts == null)
                return new string[0];

            var names = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                names[i] = parts[i] != null ? parts[i].name : string.Empty;

            return names;
        }

        internal static string[] Hair(Gender gender) => gender == Gender.Male ? hairMale : hairFemale;
        internal static string[] Heads(Gender gender) => gender == Gender.Male ? headsMale : headsFemale;
        internal static string[] Eyebrows(Gender gender) => gender == Gender.Male ? eyebrowsMale : eyebrowsFemale;
        internal static string[] FacialHair(Gender gender) => gender == Gender.Male ? facialHairMale : new string[0];

        /// <summary>Reads a preset's look into the editable block.</summary>
        internal static AppearanceBlock FromPreset(CharacterPresetFile preset)
        {
            return new AppearanceBlock
            {
                SkinColor = preset.SkinColorIndex,
                HairColor = preset.HairColorIndex,
                EyeColor = preset.EyeColorIndex,
                BodyArtColor = preset.BodyArtColorIndex,
                HairType = preset.HairTypeIndex,
                HeadType = preset.HeadTypeIndex,
                EyebrowType = preset.EyebrowTypeIndex,
                FacialHair = preset.FacialHairTypeIndex
            };
        }

        /// <summary>
        /// Reads a character's look into the editable block, inverting what the game does when it
        /// pushes a preset onto the preview model.
        ///
        /// Names are matched against every part list rather than read positionally: a saved
        /// character's ActiveVisuals also carries parts that are not customisable, and the order is
        /// not something to depend on.
        /// </summary>
        internal static AppearanceBlock FromCharacter(Character character, out string problem)
        {
            problem = null;

            if (!PartsKnown)
            {
                problem = "the appearance tables have not been read yet - open the character select screen once";
                return null;
            }

            Gender gender = character.IsMale ? Gender.Male : Gender.Female;
            var block = new AppearanceBlock();

            var colors = character.ChosenColors;
            if (colors != null)
            {
                // SyncModelToPreset writes them in this order: hair, skin, body art, eyes.
                block.HairColor = ColorIndex(colors, 0, HairColors);
                block.SkinColor = ColorIndex(colors, 1, SkinColors);
                block.BodyArtColor = ColorIndex(colors, 2, BodyArtColors);
                block.EyeColor = ColorIndex(colors, 3, EyeColors);
            }

            var visuals = character.ActiveVisuals;
            if (visuals != null)
            {
                foreach (string visual in visuals)
                {
                    if (string.IsNullOrEmpty(visual))
                        continue;

                    int index = IndexOf(Hair(gender), visual);
                    if (index >= 0) { block.HairType = index; continue; }

                    index = IndexOf(Heads(gender), visual);
                    if (index >= 0) { block.HeadType = index; continue; }

                    index = IndexOf(Eyebrows(gender), visual);
                    if (index >= 0) { block.EyebrowType = index; continue; }

                    index = IndexOf(FacialHair(gender), visual);
                    if (index >= 0) block.FacialHair = index;
                }
            }

            if (block.HeadType == null && block.HairType == null)
                problem = "none of " + character.CharacterName + "'s visuals matched the part lists";

            return block;
        }

        private static int? ColorIndex(IList<string> colors, int slot, Color[] palette)
        {
            if (colors == null || slot >= colors.Count || palette.Length == 0)
                return null;

            string wanted = colors[slot];
            if (string.IsNullOrEmpty(wanted))
                return null;

            for (int i = 0; i < palette.Length; i++)
            {
                if (string.Equals(ColorUtility.ToHtmlStringRGB(palette[i]), wanted, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return null;
        }

        private static int IndexOf(string[] names, string value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>Applies the block's set fields to a preset, clamped to the tables they index into.</summary>
        internal static void ApplyTo(CharacterPresetFile preset, AppearanceBlock block)
        {
            if (block == null)
                return;

            Gender gender = preset.Gender;

            if (block.SkinColor.HasValue) Set(SkinColors.Length, block.SkinColor.Value, i => { preset.SkinColorIndex = i; preset.SkinColor = SkinColors[i]; });
            if (block.HairColor.HasValue) Set(HairColors.Length, block.HairColor.Value, i => { preset.HairColorIndex = i; preset.HairColor = HairColors[i]; });
            if (block.EyeColor.HasValue) Set(EyeColors.Length, block.EyeColor.Value, i => { preset.EyeColorIndex = i; preset.EyeColor = EyeColors[i]; });
            if (block.BodyArtColor.HasValue) Set(BodyArtColors.Length, block.BodyArtColor.Value, i => { preset.BodyArtColorIndex = i; preset.BodyArtColor = BodyArtColors[i]; });

            // The preset keeps a male and a female value for each part; only the one matching the
            // preset's gender is what the game reads, so only that one is written.
            if (block.HairType.HasValue) SetPart(Hair(gender), block.HairType.Value, gender, (m, i) => { if (m) preset.HairTypeMale = i; else preset.HairTypeFemale = i; });
            if (block.HeadType.HasValue) SetPart(Heads(gender), block.HeadType.Value, gender, (m, i) => { if (m) preset.HeadTypeMale = i; else preset.HeadTypeFemale = i; });
            if (block.EyebrowType.HasValue) SetPart(Eyebrows(gender), block.EyebrowType.Value, gender, (m, i) => { if (m) preset.EyebrowTypeMale = i; else preset.EyebrowTypeFemale = i; });
            if (block.FacialHair.HasValue && gender == Gender.Male) SetPart(FacialHair(gender), block.FacialHair.Value, gender, (m, i) => preset.FacialHairType = i);
        }

        private static void Set(int count, int index, Action<int> apply)
        {
            if (count > 0)
                apply(Mathf.Clamp(index, 0, count - 1));
        }

        private static void SetPart(string[] names, int index, Gender gender, Action<bool, int> apply)
        {
            // With no table to clamp against, the value is taken as written: the editor is not the
            // only way to set these, and a hand-written classes.json should not be silently zeroed
            // just because the menus have not been visited yet this session.
            int value = names != null && names.Length > 0 ? Mathf.Clamp(index, 0, names.Length - 1) : Mathf.Max(0, index);
            apply(gender == Gender.Male, value);
        }
    }
}
