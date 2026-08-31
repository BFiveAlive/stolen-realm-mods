using System.Collections.Generic;
using UnityEngine;

namespace AutoEquipMod
{
    /// <summary>
    /// The offer itself: a small panel in the corner asking whether to wear what was just found.
    ///
    /// Drawn with IMGUI rather than cloned from the game's UI, for the same reason the mod manager
    /// is: it depends on no game asset, so it cannot be broken by a game update renaming a prefab,
    /// and it works on any screen including ones where no canvas exists yet.
    /// </summary>
    internal static class Prompt
    {
        private static readonly List<Offer> Queue = new List<Offer>();

        public static bool Any => Queue.Count > 0;

        public static void Enqueue(Offer offer)
        {
            // The same item can arrive twice - an item copy, a stack merging - and offering it
            // twice would be confusing.
            if (Queue.Exists(o => o.Item == offer.Item))
                return;

            Queue.Add(offer);

            while (Queue.Count > Mathf.Max(1, ModConfig.MaxQueued.Value))
            {
                Plugin.Log.LogInfo("Dropping the oldest pending offer: "
                    + Appraisal.Name(Queue[0].Item));
                Queue.RemoveAt(0);
            }
        }

        public static void Clear()
        {
            Queue.Clear();
        }

        /// <summary>Drops offers whose item or character has gone away since it was queued.</summary>
        public static void Prune()
        {
            Queue.RemoveAll(o => o == null || o.Item == null || o.Character == null
                || o.Item.equipped);
        }

        public static void Draw()
        {
            if (Queue.Count == 0)
                return;

            Skin.Build();

            float scale = Mathf.Clamp(ModConfig.PromptScale.Value, 0.6f, 2.5f);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;

            const float width = 430f;
            float height = Queue[0].SlotEmpty ? 190f : 226f;

            // Bottom right, out of the way of the party and the action bar.
            var rect = new Rect(screenWidth - width - 28f, screenHeight - height - 28f, width, height);

            DrawOffer(rect, Queue[0]);

            GUI.matrix = previous;
        }

        private static void DrawOffer(Rect rect, Offer offer)
        {
            Skin.Fill(rect, Skin.Panel);
            Skin.Frame(rect, Skin.Accent);

            float x = rect.x + 16f;
            float width = rect.width - 32f;
            float y = rect.y + 12f;

            string heading = offer.SlotEmpty ? "Equip this?" : "Better than what you are wearing?";
            Skin.Text(new Rect(x, y, width, 24f), heading, Skin.Heading, Skin.Accent);
            y += 26f;

            Skin.Text(new Rect(x, y, width, 22f),
                Appraisal.Name(offer.Item) + "  →  " + offer.Character.CharacterName,
                Skin.Title, Skin.Ink);
            y += 24f;

            Skin.Text(new Rect(x, y, width, 20f), Appraisal.Describe(offer.Item),
                Skin.Body, Skin.InkMuted);
            y += 24f;

            if (!offer.SlotEmpty)
            {
                Skin.Text(new Rect(x, y, width, 20f), "replacing " + Appraisal.Name(offer.Replacing),
                    Skin.Body, Skin.InkDim);
                y += 20f;

                Skin.Text(new Rect(x, y, width, 20f), Appraisal.Describe(offer.Replacing),
                    Skin.Body, Skin.InkDim);
                y += 24f;

                Skin.Text(new Rect(x, y, width, 20f),
                    offer.PercentBetter >= 0f
                        ? "scores " + offer.PercentBetter.ToString("0") + "% higher"
                        : "scores " + (-offer.PercentBetter).ToString("0") + "% lower",
                    Skin.Body, offer.PercentBetter >= 0f ? Skin.Good : Skin.Bad);
                y += 24f;
            }
            else
            {
                Skin.Text(new Rect(x, y, width, 20f), "that slot is empty", Skin.Body, Skin.InkDim);
                y += 24f;
            }

            float buttonY = rect.yMax - 44f;
            float buttonWidth = (width - 16f) / 2f;

            if (GUI.Button(new Rect(x, buttonY, buttonWidth, 32f), "Equip", Skin.Button))
            {
                Equipper.Equip(offer, "accepted");
                Queue.RemoveAt(0);
            }

            if (GUI.Button(new Rect(x + buttonWidth + 16f, buttonY, buttonWidth, 32f),
                    "Keep current", Skin.Button))
            {
                Queue.RemoveAt(0);
            }

            if (Queue.Count > 1)
            {
                Skin.Text(new Rect(x, buttonY - 20f, width, 18f),
                    (Queue.Count - 1) + " more waiting", Skin.Body, Skin.InkDim);
            }
        }
    }
}
