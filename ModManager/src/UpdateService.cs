using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;

namespace ModManager
{
    internal enum UpdatePhase
    {
        Idle,
        Checking,
        Ready,
        Downloading,
        Failed
    }

    /// <summary>
    /// Fetches the published mod list and downloads updates.
    ///
    /// Nothing is ever installed while the game is running. Mono cannot unload an assembly from
    /// the default AppDomain, and every plugin has already had its Harmony patches applied by the
    /// time this code can run - so a downloaded update is written to a staging folder and put in
    /// place by ModUpdatePatcher during the next launch, before any plugin is loaded.
    /// </summary>
    internal static class UpdateService
    {
        public static UpdatePhase Phase { get; private set; } = UpdatePhase.Idle;
        public static string Message { get; private set; } = string.Empty;
        public static List<UpdateStatus> Statuses { get; private set; } = new List<UpdateStatus>();

        /// <summary>Folder currently downloading, for the progress line.</summary>
        public static string ActiveDownload { get; private set; }
        public static float DownloadProgress { get; private set; }

        public static bool AnyUpdatesAvailable => Statuses.Any(s => s.UpdateAvailable);
        public static bool AnyStaged => Statuses.Any(s => s.Staged);

        public static string StagingPath => Path.Combine(Paths.BepInExRootPath, "mod-updates", "staged");

        private const int TimeoutSeconds = 30;

        public static IEnumerator Check()
        {
            if (Phase == UpdatePhase.Checking || Phase == UpdatePhase.Downloading)
                yield break;

            Phase = UpdatePhase.Checking;
            Message = "Contacting " + SafeHost(ModConfig.ManifestUrl.Value) + "...";

            string url = ModConfig.ManifestUrl.Value;
            if (string.IsNullOrEmpty(url))
            {
                Fail("No manifest URL is configured.");
                yield break;
            }

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = TimeoutSeconds;

                // GitHub serves the raw file from a CDN that caches aggressively; without this a
                // check made minutes after a release can still see the previous manifest.
                request.SetRequestHeader("Cache-Control", "no-cache");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail("Could not fetch the mod list: " + request.error);
                    yield break;
                }

                string body = request.downloadHandler.text ?? string.Empty;

                Manifest manifest;
                try
                {
                    manifest = Manifest.Parse(body);
                }
                catch (Exception e)
                {
                    Fail("The mod list could not be parsed: " + e.Message);
                    yield break;
                }

                if (manifest == null || manifest.mods == null || manifest.mods.Length == 0)
                {
                    Fail("The mod list was empty or malformed.");
                    yield break;
                }

                Statuses = BuildStatuses(manifest);
                Phase = UpdatePhase.Ready;

                int available = Statuses.Count(s => s.UpdateAvailable);
                Message = available == 0
                    ? "Everything is up to date."
                    : available + (available == 1 ? " update available." : " updates available.");

                Plugin.Log.LogInfo("Update check: " + Message);
            }
        }

        private static List<UpdateStatus> BuildStatuses(Manifest manifest)
        {
            var staged = StagedFolders();
            var result = new List<UpdateStatus>();

            foreach (var release in manifest.mods)
            {
                if (release == null || string.IsNullOrEmpty(release.guid))
                    continue;

                string installed = InstalledVersion(release.guid);

                result.Add(new UpdateStatus
                {
                    Release = release,
                    InstalledVersion = installed,
                    Installed = installed != null,
                    UpdateAvailable = installed != null && UpdateStatus.IsNewer(release.version, installed),
                    Staged = staged.Contains(release.folder)
                });
            }

            // Installed mods needing attention first, then the rest of what is on offer.
            return result
                .OrderByDescending(s => s.UpdateAvailable)
                .ThenByDescending(s => s.Installed)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The version BepInEx recorded from the plugin's own [BepInPlugin] attribute, which is
        /// authoritative for what is actually loaded - no manifest file on disk to drift from it.
        /// </summary>
        private static string InstalledVersion(string guid)
        {
            return Chainloader.PluginInfos.TryGetValue(guid, out var info) && info.Metadata != null
                ? info.Metadata.Version.ToString()
                : null;
        }

        private static HashSet<string> StagedFolders()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(StagingPath))
                {
                    foreach (var file in Directory.GetFiles(StagingPath, "*.zip"))
                        set.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the staging folder: " + e.Message);
            }

            return set;
        }

        public static IEnumerator Download(UpdateStatus status)
        {
            if (Phase == UpdatePhase.Downloading || status?.Release == null)
                yield break;

            var release = status.Release;

            if (string.IsNullOrEmpty(release.url))
            {
                Fail(release.name + " has no download URL in the mod list.");
                yield break;
            }

            Phase = UpdatePhase.Downloading;
            ActiveDownload = status.DisplayName;
            DownloadProgress = 0f;
            Message = "Downloading " + status.DisplayName + " " + release.version + "...";

            byte[] payload = null;

            using (var request = UnityWebRequest.Get(release.url))
            {
                request.timeout = TimeoutSeconds * 4;
                yield return SendTracked(request);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail("Download failed: " + request.error);
                    yield break;
                }

                payload = request.downloadHandler.data;
            }

            if (payload == null || payload.Length == 0)
            {
                Fail("Download produced an empty file.");
                yield break;
            }

            if (!string.IsNullOrEmpty(release.sha256))
            {
                string actual = Sha256(payload);
                if (!string.Equals(actual, release.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Fail("Checksum mismatch for " + status.DisplayName + "; the file was not saved.");
                    yield break;
                }
            }

            // Written only after the bytes are in hand and verified, so an interrupted download
            // can never leave a half-file for the patcher to unpack next launch.
            try
            {
                Directory.CreateDirectory(StagingPath);
                File.WriteAllBytes(Path.Combine(StagingPath, SafeFileName(release.folder) + ".zip"), payload);
            }
            catch (Exception e)
            {
                Fail("Could not write to the staging folder: " + e.Message);
                yield break;
            }

            status.Staged = true;
            Phase = UpdatePhase.Ready;
            ActiveDownload = null;
            Message = status.DisplayName + " " + release.version + " is staged. Restart the game to finish installing.";

            Plugin.Log.LogInfo(Message);
        }

        private static IEnumerator SendTracked(UnityWebRequest request)
        {
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                DownloadProgress = request.downloadProgress;
                yield return null;
            }

            DownloadProgress = 1f;
        }

        /// <summary>Removes a staged zip, so a download can be undone before the restart.</summary>
        public static void Unstage(UpdateStatus status)
        {
            if (status?.Release == null)
                return;

            try
            {
                string path = Path.Combine(StagingPath, SafeFileName(status.Release.folder) + ".zip");
                if (File.Exists(path))
                    File.Delete(path);

                status.Staged = false;
                Message = "Cancelled the staged update for " + status.DisplayName + ".";
            }
            catch (Exception e)
            {
                Message = "Could not cancel: " + e.Message;
            }
        }

        private static void Fail(string reason)
        {
            Phase = UpdatePhase.Failed;
            ActiveDownload = null;
            Message = reason;
            Plugin.Log.LogWarning(reason);
        }

        private static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        /// <summary>
        /// The folder name comes from a downloaded file, so it decides a path on disk. Anything
        /// that is not a plain name is rejected rather than sanitised.
        /// </summary>
        private static string SafeFileName(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                throw new InvalidOperationException("The mod list entry has no folder name.");

            if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || folder.Contains("..")
                || folder.Contains("/")
                || folder.Contains("\\"))
            {
                throw new InvalidOperationException("Refusing to use unsafe folder name: " + folder);
            }

            return folder;
        }

        private static string SafeHost(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "the update server";
            }
        }
    }
}
