using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace StatusEffectsMod
{
    /// <summary>
    /// Binds one config entry per status, under [Status Overrides].
    ///
    /// This cannot happen in Awake like the rest of the config: the list of statuses lives in
    /// the game's asset bundles and is not loaded until well after plugins start, so the keys
    /// simply do not exist yet. Binding is therefore deferred until the catalog is ready, which
    /// is fine — ConfigFile.Bind works at any point in the lifecycle.
    /// </summary>
    internal static class StatusOverrides
    {
        private static readonly Dictionary<string, ConfigEntry<string>> entries =
            new Dictionary<string, ConfigEntry<string>>();

        public static bool Bound { get; private set; }

        private const string Section = "Status Overrides";

        public static void Bind(ConfigFile cfg)
        {
            if (Bound || !StatusCatalog.Loaded)
                return;

            int failed = 0;

            foreach (StatusEntry entry in StatusCatalog.Entries)
            {
                try
                {
                    // The schema travels with the entry as a config tag. A UI that recognises it
                    // can offer one control per field; anything else sees an ordinary string
                    // setting and shows a text box, which is exactly what it was before.
                    ConfigEntry<string> bound = cfg.Bind(
                        Section,
                        entry.ConfigKey,
                        string.Empty,
                        new ConfigDescription(
                            StatusCatalog.DescribeVanilla(entry),
                            null,
                            OverrideSchema.Descriptor));

                    entries[entry.ConfigKey] = bound;
                }
                catch (Exception e)
                {
                    // BepInEx throws on a key it dislikes rather than escaping it. One such
                    // status must cost only itself: letting this escape would abandon the whole
                    // catalog, and - since the caller retries until it succeeds - would do so
                    // once per frame, forever.
                    failed++;
                    Plugin.Log.LogWarning(
                        $"Could not bind a config entry for '{entry.ConfigKey}', so it cannot be " +
                        $"edited. Every other status still works. Reason: {e.Message}");
                }
            }

            Bound = true;

            if (failed > 0)
                Plugin.Log.LogWarning($"{failed} status(es) could not be bound; see the warnings above.");
        }

        public static ConfigEntry<string> Get(StatusEntry entry)
        {
            return entries.TryGetValue(entry.ConfigKey, out ConfigEntry<string> bound) ? bound : null;
        }
    }
}
