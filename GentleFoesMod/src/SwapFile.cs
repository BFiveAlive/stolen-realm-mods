using System.Collections.Generic;
using Newtonsoft.Json;

namespace GentleFoesMod
{
    /// <summary>
    /// The shape of swaps.json: which enemies should look like something else, and what to call
    /// them when they do.
    ///
    /// Written in the names the game uses rather than in resource paths. A model is identified by
    /// the enemy that already wears it, so "make bears look like goblins" is one line and nobody
    /// has to know that the goblin's model lives at some path inside Resources.
    /// </summary>
    internal sealed class SwapLibraryFile
    {
        [JsonProperty("schema")]
        public int Schema { get; set; } = 1;

        /// <summary>One enemy, named exactly as the dump lists it.</summary>
        [JsonProperty("swaps")]
        public List<SwapDefinition> Swaps { get; set; } = new List<SwapDefinition>();

        /// <summary>
        /// A whole faction at once - ForestCreatures, DesertCreatures and so on. Donors are dealt
        /// round-robin from the donor group so a forest full of bears does not become a forest
        /// full of one identical goblin.
        /// </summary>
        [JsonProperty("groupSwaps")]
        public List<GroupSwapDefinition> GroupSwaps { get; set; } = new List<GroupSwapDefinition>();
    }

    internal sealed class SwapDefinition
    {
        /// <summary>The enemy to change, by the name the game knows it by.</summary>
        [JsonProperty("enemy")]
        public string Enemy { get; set; }

        /// <summary>Another enemy whose model this one should borrow. Null leaves the model alone.</summary>
        [JsonProperty("lookLike")]
        public string LookLike { get; set; }

        /// <summary>What to call it on screen. Null leaves the name alone.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    internal sealed class GroupSwapDefinition
    {
        /// <summary>An EnemyGroup name, such as ForestCreatures.</summary>
        [JsonProperty("group")]
        public string Group { get; set; }

        /// <summary>The EnemyGroup to borrow models from.</summary>
        [JsonProperty("lookLikeGroup")]
        public string LookLikeGroup { get; set; }

        /// <summary>
        /// Optional: what to call everything in the group. Left null, each enemy keeps its own
        /// name, which is usually wrong once it no longer looks like a bear - so a per-enemy entry
        /// in <see cref="SwapLibraryFile.Swaps"/> is the better way to rename.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
