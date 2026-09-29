using System.Collections.Generic;
using Newtonsoft.Json;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The shape of classes.json. Deliberately close to what someone writing a class would want to
    /// type rather than to <c>CharacterPresetFile</c>'s field list: names instead of asset
    /// references, one endless number instead of a stat threshold, and no appearance block unless
    /// the class actually wants to look like something specific.
    /// </summary>
    internal sealed class ClassLibraryFile
    {
        [JsonProperty("schema")]
        public int Schema { get; set; } = 1;

        [JsonProperty("defaults")]
        public ClassDefaults Defaults { get; set; }

        [JsonProperty("classes")]
        public List<ClassDefinition> Classes { get; set; } = new List<ClassDefinition>();

        /// <summary>
        /// Ids of shipped classes to leave out. Only meaningful in the user file: a shipped class
        /// cannot be deleted from the file it lives in, because that file comes back with every
        /// update.
        /// </summary>
        [JsonProperty("hidden")]
        public List<string> Hidden { get; set; }
    }

    /// <summary>Values every class inherits unless it says otherwise.</summary>
    internal sealed class ClassDefaults
    {
        /// <summary>Name of a shipped preset whose look new classes borrow. Null picks the first one.</summary>
        [JsonProperty("appearanceFrom")]
        public string AppearanceFrom { get; set; }

        [JsonProperty("tier")]
        public int Tier { get; set; } = 2;

        /// <summary>Items every new class starts with on top of its own, by item name.</summary>
        [JsonProperty("extraItems")]
        public List<ItemStack> ExtraItems { get; set; }
    }

    internal sealed class ClassDefinition
    {
        /// <summary>
        /// Stable identifier. The preset's Guid is derived from it, and that Guid is written into
        /// every character created from the class, so changing an id orphans existing characters.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>1, 2 or 3 - the rating shown on the tile, not a mechanical effect.</summary>
        [JsonProperty("tier")]
        public int? Tier { get; set; }

        [JsonProperty("unlock")]
        public UnlockDefinition Unlock { get; set; }

        [JsonProperty("stats")]
        public StatBlock Stats { get; set; }

        /// <summary>Skill names, exactly as they read in game. Granted at level 1 regardless of tier or prerequisite.</summary>
        [JsonProperty("skills")]
        public List<string> Skills { get; set; } = new List<string>();

        [JsonProperty("equipment")]
        public EquipmentBlock Equipment { get; set; }

        [JsonProperty("extraItems")]
        public List<ItemStack> ExtraItems { get; set; }

        /// <summary>
        /// Free text describing the gear this class wants, for classes whose items are not pinned
        /// down yet. Never read by the game - it is logged so the intent stays attached to the
        /// class instead of living in a design document somewhere else.
        /// </summary>
        [JsonProperty("gearNote")]
        public string GearNote { get; set; }

        [JsonProperty("appearanceFrom")]
        public string AppearanceFrom { get; set; }

        /// <summary>"Male" or "Female"; omitted keeps whatever the donor preset uses.</summary>
        [JsonProperty("gender")]
        public string Gender { get; set; }

        /// <summary>
        /// Individual appearance values, applied on top of <see cref="AppearanceFrom"/>. Every
        /// field is optional: what is absent keeps whatever the donor had, so a class can change
        /// one thing without restating a whole look.
        /// </summary>
        [JsonProperty("appearance")]
        public AppearanceBlock Appearance { get; set; }
    }

    /// <summary>
    /// A character's look, as <c>CharacterPresetFile</c> stores it: an index into one of the
    /// game's palettes or part lists for each feature.
    ///
    /// Written by the editor rather than by hand, normally - the indices mean nothing without the
    /// tables they point into, which is why the editor offers copying from a shipped preset or
    /// from a character you already made.
    /// </summary>
    internal sealed class AppearanceBlock
    {
        [JsonProperty("skinColor")]
        public int? SkinColor { get; set; }

        [JsonProperty("hairColor")]
        public int? HairColor { get; set; }

        [JsonProperty("eyeColor")]
        public int? EyeColor { get; set; }

        [JsonProperty("bodyArtColor")]
        public int? BodyArtColor { get; set; }

        [JsonProperty("hairType")]
        public int? HairType { get; set; }

        [JsonProperty("headType")]
        public int? HeadType { get; set; }

        [JsonProperty("eyebrowType")]
        public int? EyebrowType { get; set; }

        [JsonProperty("facialHair")]
        public int? FacialHair { get; set; }
    }

    internal sealed class UnlockDefinition
    {
        /// <summary>
        /// Which authored difficulty unlocks the class, 1 to 6 - the same ladder the shipped
        /// classes use ("Complete Difficulty III"). Mutually exclusive with
        /// <see cref="EndlessLevel"/>; if both are given the endless one wins, since it is
        /// always the later requirement.
        /// </summary>
        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        /// <summary>
        /// Which endless difficulty unlocks the class. 0 (or absent) means available from the
        /// start. 6 means "reach Endless VI", which is resolved against the game's authored
        /// difficulty list at runtime - see <see cref="UnlockRules"/>.
        /// </summary>
        [JsonProperty("endlessLevel")]
        public int EndlessLevel { get; set; }

        /// <summary>Shown on the locked tile. Generated from the endless level when absent.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal sealed class StatBlock
    {
        [JsonProperty("might")]
        public int Might { get; set; } = 10;

        [JsonProperty("dexterity")]
        public int Dexterity { get; set; } = 10;

        [JsonProperty("vitality")]
        public int Vitality { get; set; } = 10;

        [JsonProperty("intelligence")]
        public int Intelligence { get; set; } = 10;

        [JsonProperty("reflex")]
        public int Reflex { get; set; } = 10;

        public int Total => Might + Dexterity + Vitality + Intelligence + Reflex;
    }

    internal sealed class EquipmentBlock
    {
        [JsonProperty("head")]
        public string Head { get; set; }

        [JsonProperty("armor")]
        public string Armor { get; set; }

        [JsonProperty("mainHand")]
        public string MainHand { get; set; }

        [JsonProperty("offHand")]
        public string OffHand { get; set; }

        [JsonProperty("ring")]
        public string Ring { get; set; }

        [JsonProperty("amulet")]
        public string Amulet { get; set; }
    }

    internal sealed class ItemStack
    {
        [JsonProperty("item")]
        public string Item { get; set; }

        [JsonProperty("stacks")]
        public int Stacks { get; set; } = 1;
    }
}
