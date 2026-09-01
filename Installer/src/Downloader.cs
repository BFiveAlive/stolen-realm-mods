using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Installer;

/// <summary>
/// Fetching and unpacking. Every mod zip and the BepInEx zip are laid out relative to the game
/// folder, so installing anything is the same operation: extract over the game root.
/// </summary>
internal sealed class Downloader : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public Downloader()
    {
        // GitHub rejects requests with no user agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StolenRealmModInstaller/1.0");
    }

    public async Task<Manifest> FetchManifestAsync(string url)
    {
        string json = await http.GetStringAsync(url);

        var manifest = JsonSerializer.Deserialize<Manifest>(json)
            ?? throw new InvalidDataException("The mod list was empty.");

        if (manifest.Mods.Count == 0)
            throw new InvalidDataException("The mod list contains no mods.");

        return manifest;
    }

    public async Task<byte[]> DownloadAsync(string url, string label)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        long read = 0;
        int count;

        while ((count = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, count);
            read += count;
            Ui.Progress(label, read, total);
        }

        Ui.ProgressDone();
        return buffer.ToArray();
    }

    /// <summary>
    /// Refuses anything whose contents do not match the manifest.
    ///
    /// A missing hash is refused rather than skipped. Returning quietly when the manifest says
    /// nothing turns a corrupt, truncated or substituted download into a silent install, and the
    /// case where that matters most - a manifest that lost its hashes, however it lost them - is
    /// exactly the case where skipping looks like success.
    /// </summary>
    public static void VerifyChecksum(byte[] data, string? expected, string label)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            throw new InvalidDataException(
                $"The mod list gives no checksum for {label}, so it cannot be verified. " +
                "Nothing was installed.");
        }

        string actual = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for {label}: expected {expected.Trim()}, got {actual}. " +
                "Nothing was installed.");
        }
    }

    /// <summary>
    /// Extracts an archive over the game folder, refusing any entry that would write outside it.
    /// </summary>
    public static int ExtractOverGame(byte[] zipBytes, string gameDir)
    {
        string root = Path.GetFullPath(gameDir);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        using var stream = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // Validate the whole archive before writing anything, so a bad entry cannot leave a
        // half-extracted install behind.
        var planned = new List<(ZipArchiveEntry Entry, string Destination)>();

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string destination = Path.GetFullPath(
                Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes the game folder: {entry.FullName}");

            planned.Add((entry, destination));
        }

        foreach (var (entry, destination) in planned)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return planned.Count;
    }

    public void Dispose() => http.Dispose();
}
