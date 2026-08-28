using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace ModUpdatePatcher
{
    /// <summary>
    /// Installs mod updates that the in-game manager downloaded, during the next launch.
    ///
    /// This exists because a running game cannot replace a loaded plugin. Mono has no way to
    /// unload an assembly from the default AppDomain, the DLL is locked by the loader, and its
    /// Harmony patches are already applied. A BepInEx *preloader patcher* runs before the
    /// chainloader touches BepInEx/plugins at all, which is the one moment when those files are
    /// unlocked and nothing has been loaded from them yet.
    ///
    /// It patches no assembly. TargetDLLs is empty and Patch is never called; all the work
    /// happens in Initialize. Both members still have to exist with these exact signatures or
    /// BepInEx will not recognise the type as a patcher.
    /// </summary>
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => Enumerable.Empty<string>();

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        private static ManualLogSource log;

        private static string StagingPath => Path.Combine(Paths.BepInExRootPath, "mod-updates", "staged");

        public static void Initialize()
        {
            log = Logger.CreateLogSource("ModUpdatePatcher");

            try
            {
                Apply();
            }
            catch (Exception e)
            {
                // A failure here must never stop the game booting: the worst acceptable outcome
                // is that the update is not applied and the old version loads as normal.
                log.LogError("Applying staged mod updates failed, continuing with what is installed: " + e);
            }
        }

        private static void Apply()
        {
            if (!Directory.Exists(StagingPath))
                return;

            var archives = Directory.GetFiles(StagingPath, "*.zip");
            if (archives.Length == 0)
                return;

            log.LogInfo("Applying " + archives.Length + " staged mod update(s).");

            foreach (var archive in archives)
            {
                try
                {
                    ApplyOne(archive);
                }
                catch (Exception e)
                {
                    log.LogError("Could not apply " + Path.GetFileName(archive) + ": " + e.Message);

                    // Left in place deliberately. A staged file that failed once may succeed after
                    // whatever locked it goes away, and deleting it would silently lose the
                    // download with no way for the player to tell that anything went wrong.
                }
            }
        }

        private static void ApplyOne(string archivePath)
        {
            string gameRoot = Paths.GameRootPath;

            using (var zip = ZipFile.OpenRead(archivePath))
            {
                // Two passes: check every entry before writing any of them, so a malformed
                // archive cannot leave a half-installed mod behind.
                var planned = new List<KeyValuePair<ZipArchiveEntry, string>>();

                foreach (var entry in zip.Entries)
                {
                    // Directory entries have an empty name and are created implicitly below.
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destination = ResolveInside(gameRoot, entry.FullName);
                    planned.Add(new KeyValuePair<ZipArchiveEntry, string>(entry, destination));
                }

                foreach (var pair in planned)
                {
                    string directory = Path.GetDirectoryName(pair.Value);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    pair.Key.ExtractToFile(pair.Value, true);
                }

                log.LogInfo("Installed " + Path.GetFileNameWithoutExtension(archivePath)
                    + " (" + planned.Count + " file(s)).");
            }

            File.Delete(archivePath);
        }

        /// <summary>
        /// Turns an entry path from the archive into an absolute path, and refuses anything that
        /// would land outside the game folder.
        ///
        /// Archive paths are attacker-controlled in the general case - the zip came off the
        /// network - and a "../../.." entry would otherwise let a download write anywhere the
        /// game process can reach.
        /// </summary>
        private static string ResolveInside(string root, string entryPath)
        {
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;

            string combined = Path.GetFullPath(Path.Combine(rootFull, entryPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archive entry escapes the game folder: " + entryPath);

            return combined;
        }
    }
}
