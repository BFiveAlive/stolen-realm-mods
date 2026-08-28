using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Installer;

/// <summary>
/// Finds the Stolen Realm install without asking the player where it is.
///
/// Steam spreads games across "library folders" that can be on any drive, and the list of them
/// lives in libraryfolders.vdf next to the main install. Reading that is far more reliable than
/// guessing at drive letters, so the drive scan below is only a fallback for an unusual setup.
/// </summary>
internal static class GameLocator
{
    private const string GameFolderName = "Stolen Realm";
    private const string GameExeName = "Stolen Realm.exe";

    public static string? Locate(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string trimmed = explicitPath.Trim().Trim('"');
            return IsGameFolder(trimmed) ? trimmed : null;
        }

        foreach (string root in SteamRoots())
        {
            foreach (string library in LibraryFolders(root))
            {
                string candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
                if (IsGameFolder(candidate))
                    return candidate;
            }
        }

        return ScanDrives();
    }

    public static bool IsGameFolder(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(Path.Combine(path, GameExeName));
    }

    /// <summary>Places a Steam install itself is likely to be.</summary>
    private static IEnumerable<string> SteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in new[]
                 {
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                     @"C:\Steam",
                 })
        {
            if (Directory.Exists(candidate) && seen.Add(candidate))
                yield return candidate;
        }
    }

    // Matches the "path" values in libraryfolders.vdf, which is Valve's own key-value text
    // format rather than anything standard. Only the paths are needed, so a targeted regex is
    // simpler and less brittle here than a full VDF parser.
    private static readonly Regex LibraryPath =
        new("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<string> LibraryFolders(string steamRoot)
    {
        // The Steam install is itself a library, and is not always listed in the file.
        yield return steamRoot;

        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in LibraryPath.Matches(text))
        {
            // Paths in the VDF are escaped C-style, so backslashes arrive doubled.
            string path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private static string? ScanDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            foreach (string relative in new[]
                     {
                         @"SteamLibrary\steamapps\common",
                         @"Steam\steamapps\common",
                         @"Games\steamapps\common",
                         @"steamapps\common",
                     })
            {
                string candidate = Path.Combine(drive.RootDirectory.FullName, relative, GameFolderName);

                // Each candidate is a single existence check on a known path rather than a
                // recursive walk, so this stays fast even with several large drives attached.
                if (IsGameFolder(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
