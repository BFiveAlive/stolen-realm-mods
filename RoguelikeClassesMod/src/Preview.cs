using System;
using System.Collections.Generic;
using System.IO;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Pictures of characters, for the editor.
    ///
    /// Two sources, because the game gives them at very different prices:
    ///
    /// - **A character you have made** already has one. <c>CharacterIconMaker</c> renders every
    ///   character to <c>HeadshotIcon</c> and <c>FullBodyIcon</c> and caches them as PNGs, so
    ///   showing one costs a <c>GUI.DrawTexture</c>.
    /// - **A class** has none, because a class is a preset rather than a character, and the
    ///   renderer works by copying one live model onto another rather than from data. The one
    ///   moment a preset *does* have a model is while the game's own character creator is showing
    ///   it, so that is when this takes the photograph - see <see cref="PresetShownPatch"/>.
    ///
    /// Captured images are cached on disk beside the save data, so a class only has to be looked
    /// at once, ever, rather than once per session.
    /// </summary>
    internal static class Preview
    {
        private const int Size = 512;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The class waiting to be photographed, and how many frames it has waited.</summary>
        private static string pendingId;
        private static int pendingFrames;

        private static string Folder =>
            Path.Combine(Application.persistentDataPath, "RoguelikeClassesMod", "previews");

        // ------------------------------------------------------------------ reading

        /// <summary>A character's own full-body render, or null if the game has not made one.</summary>
        internal static Texture2D ForCharacter(Character character)
        {
            if (character == null)
                return null;

            return character.FullBodyIcon != null ? character.FullBodyIcon : character.HeadshotIcon;
        }

        /// <summary>A class's render: cached in memory, else loaded from disk, else null.</summary>
        internal static Texture2D ForClass(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            if (Cache.TryGetValue(id, out Texture2D cached))
                return cached;

            if (Missing.Contains(id))
                return null;

            string path = Path.Combine(Folder, id + ".png");
            if (!File.Exists(path))
            {
                Missing.Add(id);
                return null;
            }

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                texture.LoadImage(File.ReadAllBytes(path));
                texture.hideFlags = HideFlags.HideAndDontSave;
                Cache[id] = texture;
                return texture;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the preview for " + id + ": " + e.Message);
                Missing.Add(id);
                return null;
            }
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Called when the game's creator starts showing one of our classes.</summary>
        internal static void Requested(string id)
        {
            if (string.IsNullOrEmpty(id) || Cache.ContainsKey(id))
                return;

            pendingId = id;

            // The model is told what to look like across a couple of frames - the preset change is
            // a coroutine with an effect in the middle - so photographing it immediately catches
            // the previous character.
            pendingFrames = 0;
        }

        internal static void Tick()
        {
            if (pendingId == null)
                return;

            if (++pendingFrames < 8)
                return;

            string id = pendingId;
            pendingId = null;

            try
            {
                Texture2D shot = Capture();
                if (shot == null)
                    return;

                Cache[id] = shot;
                Missing.Remove(id);
                Save(id, shot);
                Plugin.Log.LogInfo("Captured a preview for " + id + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not capture a preview for " + id + ": " + e.Message);
            }
        }

        /// <summary>
        /// Photographs whatever the creator's preview character currently looks like, the same way
        /// the game photographs a character for its portrait: put the look on the icon model, point
        /// the full-body camera at it, and read the pixels back.
        /// </summary>
        private static Texture2D Capture()
        {
            CharacterIconMaker maker = CharacterIconMaker.instance;
            PresetManager presets = PresetManager.Instance;

            if (maker == null || presets == null || maker.fullBodyCamera == null)
                return null;

            Character subject = presets.PresetCharacter;
            if (subject == null)
                return null;

            maker.SetIconCameraSetupEnabled(true);

            try
            {
                maker.SyncCharacter(subject, ModelType.Icons);

                var target = new RenderTexture(Size, Size, 32);
                RenderTexture previous = RenderTexture.active;

                try
                {
                    maker.fullBodyCamera.targetTexture = target;
                    maker.fullBodyCamera.Render();
                    RenderTexture.active = target;

                    var shot = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false);
                    shot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                    shot.Apply();
                    shot.hideFlags = HideFlags.HideAndDontSave;
                    return shot;
                }
                finally
                {
                    RenderTexture.active = previous;
                    maker.fullBodyCamera.targetTexture = null;
                    UnityEngine.Object.Destroy(target);
                }
            }
            finally
            {
                // Left on, the icon rig's lighting and cameras keep rendering over the menu.
                maker.SetIconCameraSetupEnabled(false);
            }
        }

        private static void Save(string id, Texture2D shot)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, id + ".png"), shot.EncodeToPNG());
            }
            catch (Exception e)
            {
                // A preview that cannot be cached is still usable this session.
                Plugin.Log.LogWarning("Could not cache the preview for " + id + ": " + e.Message);
            }
        }
    }

    /// <summary>
    /// Notices when the game's character creator starts showing one of our classes, which is the
    /// only moment a class has a model that can be photographed.
    ///
    /// Postfixed rather than driven by us: asking the creator to show a preset ourselves would
    /// change what the player is looking at, whereas this only takes a picture of something they
    /// chose to look at anyway.
    /// </summary>
    [HarmonyPatch(typeof(PresetManager), "SyncModelToPreset")]
    internal static class PresetShownPatch
    {
        [HarmonyPostfix]
        private static void Shown(CharacterPresetFile characterPresetFile)
        {
            if (characterPresetFile == null || !PresetInjection.IsOurs(characterPresetFile))
                return;

            // The asset name is the class id with the mod's prefix in front of it.
            string id = characterPresetFile.name.Substring(PresetFactory.NamePrefix.Length);
            Preview.Requested(id);
        }
    }
}
