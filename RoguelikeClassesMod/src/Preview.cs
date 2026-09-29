using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>A full-body picture and a face close-up, either of which may be missing.</summary>
    internal sealed class Shot
    {
        internal Texture2D Body;
        internal Texture2D Face;

        internal bool Any => Body != null || Face != null;
    }

    /// <summary>
    /// Pictures of characters, for the editor.
    ///
    /// Two sources, because the game gives them at very different prices:
    ///
    /// - **A character you have made** already has both. <c>CharacterIconMaker</c> renders every
    ///   character to <c>HeadshotIcon</c> and <c>FullBodyIcon</c> and caches them as PNGs, so
    ///   showing one costs a <c>GUI.DrawTexture</c>.
    /// - **A preset** - one of our classes, or one of the game's - has none, because the renderer
    ///   works by copying one live model onto another rather than building one from data. The one
    ///   moment a preset *does* have a model is while the game's character creator is showing it,
    ///   so that is when this takes the photograph - see <see cref="PresetShownPatch"/>.
    ///
    /// Captured images are cached on disk beside the save data, so a preset only has to be looked
    /// at once, ever, rather than once per session.
    /// </summary>
    internal static class Preview
    {
        private const int Size = 512;

        /// <summary>Distinguishes the game's presets from ours, which are keyed by class id.</summary>
        internal const string PresetKeyPrefix = "preset.";

        private static readonly Dictionary<string, Shot> Cache = new Dictionary<string, Shot>(StringComparer.Ordinal);
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>What is waiting to be photographed, and how many frames it has waited.</summary>
        private static string pendingKey;
        private static int pendingFrames;

        /// <summary>Where captures taken on this machine are kept.</summary>
        private static string Folder =>
            Path.Combine(Application.persistentDataPath, "RoguelikeClassesMod", "previews");

        /// <summary>
        /// Pictures that ship with the mod, so a new install shows them without anyone having to
        /// walk the character creator first. A capture of your own wins over one of these: if you
        /// change a class's look, the picture taken next time you view it is the accurate one.
        /// </summary>
        private static string ShippedFolder =>
            Path.Combine(Path.GetDirectoryName(typeof(Preview).Assembly.Location) ?? ".", "previews");

        // ------------------------------------------------------------------ reading

        /// <summary>A character's own renders, which the game has already made and cached.</summary>
        internal static Shot ForCharacter(Character character)
        {
            if (character == null)
                return null;

            var shot = new Shot { Body = character.FullBodyIcon, Face = character.HeadshotIcon };
            return shot.Any ? shot : null;
        }

        private static readonly Dictionary<int, Shot> TrimmedCharacters = new Dictionary<int, Shot>();

        /// <summary>
        /// The same renders, trimmed, for showing a character large.
        ///
        /// Separate from <see cref="ForCharacter"/> because trimming reads back every pixel of two
        /// 512-square textures, which is cheap once for the character just picked and wasteful for
        /// every row of a list of thirty.
        /// </summary>
        internal static Shot ForCharacterLarge(Character character)
        {
            if (character == null)
                return null;

            if (TrimmedCharacters.TryGetValue(character.SaveIndex, out Shot cached))
                return cached;

            Shot raw = ForCharacter(character);
            if (raw == null)
                return null;

            // Copied first: these are the game's own textures, and the character screens draw them
            // too, so they must not be cropped or destroyed underneath them.
            var shot = new Shot { Body = Trim(Copy(raw.Body)), Face = Trim(Copy(raw.Face)) };
            TrimmedCharacters[character.SaveIndex] = shot;
            return shot;
        }

        private static Texture2D Copy(Texture2D source)
        {
            if (source == null)
                return null;

            try
            {
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false);
                copy.SetPixels32(source.GetPixels32());
                copy.Apply();
                copy.hideFlags = HideFlags.HideAndDontSave;
                return copy;
            }
            catch (Exception)
            {
                return null;                         // unreadable; the row icon still works
            }
        }

        /// <summary>The key under which one of the game's own presets is filed.</summary>
        internal static string KeyForPreset(string presetName) => PresetKeyPrefix + presetName;

        /// <summary>A preset's pictures: cached in memory, else loaded from disk, else null.</summary>
        internal static Shot ForKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (Cache.TryGetValue(key, out Shot cached))
                return cached;

            if (Missing.Contains(key))
                return null;

            var shot = new Shot
            {
                Body = Load(PathFor(Folder, key, face: false)) ?? Load(PathFor(ShippedFolder, key, face: false)),
                Face = Load(PathFor(Folder, key, face: true)) ?? Load(PathFor(ShippedFolder, key, face: true)),
            };

            if (!shot.Any)
            {
                Missing.Add(key);
                return null;
            }

            Cache[key] = shot;
            return shot;
        }

        /// <summary>One of our classes, keyed by its id.</summary>
        internal static Shot ForClass(string id) => ForKey(id);

        private static Texture2D Load(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                texture.LoadImage(File.ReadAllBytes(path));
                texture.hideFlags = HideFlags.HideAndDontSave;
                return texture;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read " + path + ": " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Called when the game's creator starts showing a preset.</summary>
        internal static void Requested(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            // Already have both halves, so there is nothing to take. A half-filled entry is worth
            // completing, since caches written before this held a body and no face.
            Shot existing = ForKey(key);
            if (existing != null && existing.Body != null && existing.Face != null)
                return;

            pendingKey = key;

            // The model is told what to look like across a couple of frames - the preset change is
            // a coroutine with an effect in the middle - so photographing it immediately catches
            // the previous character.
            pendingFrames = 0;
        }

        internal static void Tick()
        {
            if (pendingKey == null)
                return;

            if (++pendingFrames < 8)
                return;

            string key = pendingKey;
            pendingKey = null;

            try
            {
                Shot shot = Capture();
                if (shot == null || !shot.Any)
                    return;

                Cache[key] = shot;
                Missing.Remove(key);
                Save(key, shot);
                Plugin.Log.LogInfo("Captured a preview for " + key + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not capture a preview for " + key + ": " + e.Message);
            }
        }

        /// <summary>
        /// Photographs whatever the creator's preview character currently looks like, the same way
        /// the game photographs a character for its portrait: put the look on the icon model, point
        /// a camera at it, and read the pixels back. Both cameras, because the editor shows the
        /// full body and the face - they are two fixed rigs, not one camera moved twice.
        /// </summary>
        private static Shot Capture()
        {
            CharacterIconMaker maker = CharacterIconMaker.instance;
            PresetManager presets = PresetManager.Instance;

            if (maker == null || presets == null)
                return null;

            Character subject = presets.PresetCharacter;
            if (subject == null)
                return null;

            maker.SetIconCameraSetupEnabled(true);

            try
            {
                maker.SyncCharacter(subject, ModelType.Icons);

                return new Shot
                {
                    Body = Photograph(maker.fullBodyCamera),
                    Face = Photograph(maker.headshotCamera),
                };
            }
            finally
            {
                // Left on, the icon rig's lighting and cameras keep rendering over the menu.
                maker.SetIconCameraSetupEnabled(false);
            }
        }

        private static Texture2D Photograph(Camera camera)
        {
            if (camera == null)
                return null;

            var target = new RenderTexture(Size, Size, 32);
            RenderTexture previous = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;

                var shot = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false);
                shot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                shot.Apply();
                shot.hideFlags = HideFlags.HideAndDontSave;
                return Trim(shot);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(target);
            }
        }

        /// <summary>
        /// Crops the empty border off a render.
        ///
        /// Both cameras frame a fixed 512-pixel square and the figure fills about a quarter of it,
        /// so the rest is transparent. Trimming does two things at once: the cached file gets a
        /// good deal smaller, and the editor can draw the figure at the size of its box instead of
        /// fitting a mostly-empty square into it.
        /// </summary>
        internal static Texture2D Trim(Texture2D source)
        {
            if (source == null)
                return null;

            Color32[] pixels;

            try { pixels = source.GetPixels32(); }
            catch (Exception) { return source; }     // not readable; keep what we have

            int width = source.width, height = source.height;
            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // A low threshold rather than zero: the edges of the model fade out over a few
                    // almost-invisible pixels, and keeping those would defeat the trim.
                    if (pixels[y * width + x].a <= 8)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
                return source;                       // nothing was rendered

            const int margin = 6;
            minX = Mathf.Max(0, minX - margin);
            minY = Mathf.Max(0, minY - margin);
            maxX = Mathf.Min(width - 1, maxX + margin);
            maxY = Mathf.Min(height - 1, maxY + margin);

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;

            var trimmed = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, mipChain: false);
            trimmed.SetPixels(source.GetPixels(minX, minY, cropWidth, cropHeight));
            trimmed.Apply();
            trimmed.hideFlags = HideFlags.HideAndDontSave;

            UnityEngine.Object.Destroy(source);
            return trimmed;
        }

        private static void Save(string key, Shot shot)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                if (shot.Body != null)
                    File.WriteAllBytes(PathFor(Folder, key, face: false), shot.Body.EncodeToPNG());

                if (shot.Face != null)
                    File.WriteAllBytes(PathFor(Folder, key, face: true), shot.Face.EncodeToPNG());
            }
            catch (Exception e)
            {
                // A preview that cannot be cached is still usable this session.
                Plugin.Log.LogWarning("Could not cache the preview for " + key + ": " + e.Message);
            }
        }

        private static string PathFor(string folder, string key, bool face) =>
            Path.Combine(folder, FileName(key) + (face ? ".face.png" : ".png"));

        /// <summary>
        /// Preset names are display text - spaces, apostrophes, the occasional colon - so they
        /// cannot be used as filenames unaltered.
        /// </summary>
        private static string FileName(string key)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder(key.Length);

            foreach (char c in key)
                builder.Append(invalid.Contains(c) || c == ' ' ? '_' : c);

            return builder.ToString();
        }
    }

    /// <summary>
    /// Notices when the game's character creator starts showing a preset, which is the only moment
    /// a preset has a model that can be photographed. Every preset, not only ours: the editor lets
    /// a class copy its look from one of the game's classes, and that copy is worth showing too.
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
            if (characterPresetFile == null)
                return;

            if (PresetInjection.IsOurs(characterPresetFile))
            {
                // The asset name is the class id with the mod's prefix in front of it.
                Preview.Requested(characterPresetFile.name.Substring(PresetFactory.NamePrefix.Length));
                return;
            }

            if (!string.IsNullOrEmpty(characterPresetFile.PresetName))
                Preview.Requested(Preview.KeyForPreset(characterPresetFile.PresetName));
        }
    }
}
