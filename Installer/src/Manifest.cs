using System.Text.Json.Serialization;

namespace Installer;

/// <summary>
/// mods.json, the same file the in-game updater reads. Keeping one manifest for both means a
/// release is published once and both the installer and the updater see it immediately.
/// </summary>
internal sealed class Manifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("bepinex")] public BepInExRelease? BepInEx { get; set; }
    [JsonPropertyName("mods")] public List<ModRelease> Mods { get; set; } = new();
}

internal sealed class BepInExRelease
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

internal sealed class ModRelease
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    /// <summary>Ticked by default on a fresh install.</summary>
    [JsonPropertyName("recommended")] public bool Recommended { get; set; }
}
