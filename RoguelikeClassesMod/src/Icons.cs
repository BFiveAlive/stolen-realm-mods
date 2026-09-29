using System;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Draws the game's own icons in the editor.
    ///
    /// Skills and items carry a <c>Sprite</c>, and IMGUI has no way to draw one: it draws textures.
    /// A sprite is a named rectangle inside a larger texture, so drawing it means handing
    /// <c>GUI.DrawTextureWithTexCoords</c> that rectangle in normalised coordinates. Nothing here
    /// needs the texture to be readable, which is why this works on the game's packed art.
    /// </summary>
    internal static class Icons
    {
        /// <summary>Draws a sprite to fit the box, keeping its shape. Returns false if it had none.</summary>
        internal static bool Draw(Rect box, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
                return false;

            try
            {
                Rect area = Area(sprite);
                Texture texture = sprite.texture;

                var uv = new Rect(
                    area.x / texture.width,
                    area.y / texture.height,
                    area.width / texture.width,
                    area.height / texture.height);

                GUI.DrawTextureWithTexCoords(Fit(box, area.width / area.height), texture, uv, alphaBlend: true);
                return true;
            }
            catch (Exception)
            {
                // A sprite the atlas will not give up its coordinates for is not worth a log line
                // every frame; the caller falls back to text.
                return false;
            }
        }

        /// <summary>
        /// Where the sprite sits in its texture. <c>textureRect</c> is the honest answer but throws
        /// for some packed sprites, and <c>rect</c> is right whenever the sprite is not packed.
        /// </summary>
        private static Rect Area(Sprite sprite)
        {
            try { return sprite.packed ? sprite.textureRect : sprite.rect; }
            catch (Exception) { return sprite.rect; }
        }

        /// <summary>The largest box of the given shape that fits, centred.</summary>
        private static Rect Fit(Rect box, float aspect)
        {
            if (aspect <= 0f || float.IsNaN(aspect))
                return box;

            float width = box.width;
            float height = width / aspect;

            if (height > box.height)
            {
                height = box.height;
                width = height * aspect;
            }

            return new Rect(box.x + (box.width - width) * 0.5f, box.y + (box.height - height) * 0.5f, width, height);
        }
    }
}
