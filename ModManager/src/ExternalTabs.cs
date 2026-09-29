using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace ModManager
{
    /// <summary>One tab contributed by another mod.</summary>
    internal sealed class ExternalTab
    {
        public string Title;
        public string Owner;
        public Action<Rect> Draw;
        public Action Refresh;

        /// <summary>Set after the panel throws, so one broken mod cannot break the manager.</summary>
        public bool Broken;
    }

    /// <summary>
    /// Lets a mod contribute a whole tab to this window.
    ///
    /// Settings already arrive here without any mod knowing this manager exists, and the
    /// structured-setting descriptor is read by reflection so that neither assembly references the
    /// other. A tab works the same way and for the same reason: some mods need an editor rather
    /// than a list of key-value pairs, and hard-coding those into the manager would make it the one
    /// place that has to know about every mod in the repo.
    ///
    /// The contract is a convention, not an interface, so there is still nothing shared to
    /// reference. A plugin assembly declares:
    ///
    /// <code>
    /// public static class ModManagerTab
    /// {
    ///     public static string Title =&gt; "Roguelike Classes";
    ///     public static void Draw(Rect body) { ... }
    ///     public static void Refresh() { }          // optional
    /// }
    /// </code>
    ///
    /// Draw is handed a window-relative rect and may use either GUI or GUILayout inside it. A
    /// panel that throws is disabled for the rest of the session and reported once - the manager
    /// has to stay usable for every other mod, and an exception every frame inside OnGUI is
    /// otherwise unreadable.
    /// </summary>
    internal static class ExternalTabs
    {
        private const string TypeName = "ModManagerTab";

        private static readonly List<ExternalTab> Tabs = new List<ExternalTab>();
        private static bool discovered;

        public static IList<ExternalTab> All => Tabs;

        public static void Discover()
        {
            if (discovered)
                return;

            discovered = true;

            foreach (var pair in Chainloader.PluginInfos)
            {
                var info = pair.Value;
                var instance = info?.Instance;

                // ReferenceEquals, not ==: this game destroys the object plugins live on, and a
                // destroyed component reads as null under Unity's operator while its assembly is
                // perfectly reachable.
                if (ReferenceEquals(instance, null))
                    continue;

                try
                {
                    ExternalTab tab = FromAssembly(instance.GetType().Assembly, info.Metadata.Name);
                    if (tab != null)
                    {
                        Tabs.Add(tab);
                        Plugin.Log.LogInfo("Added the '" + tab.Title + "' tab, contributed by " + tab.Owner + ".");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not read a tab from " + info.Metadata.Name + ": " + e.Message);
                }
            }
        }

        private static ExternalTab FromAssembly(Assembly assembly, string owner)
        {
            Type type = null;

            // A plugin that fails to load one of its own types must not stop the scan; the types
            // that did load are still worth looking at.
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }

            foreach (Type candidate in types)
            {
                if (candidate != null && candidate.Name == TypeName)
                {
                    type = candidate;
                    break;
                }
            }

            if (type == null)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            var draw = type.GetMethod("Draw", flags, null, new[] { typeof(Rect) }, null);
            if (draw == null)
            {
                Plugin.Log.LogWarning(owner + " declares " + TypeName + " but no static Draw(Rect); the tab was skipped.");
                return null;
            }

            string title = ReadString(type, "Title", flags) ?? owner;
            var refresh = type.GetMethod("Refresh", flags, null, Type.EmptyTypes, null);

            var tab = new ExternalTab { Title = title, Owner = owner };
            tab.Draw = body => draw.Invoke(null, new object[] { body });
            tab.Refresh = refresh == null ? (Action)null : () => refresh.Invoke(null, null);
            return tab;
        }

        private static string ReadString(Type type, string name, BindingFlags flags)
        {
            var property = type.GetProperty(name, flags);
            if (property != null && property.PropertyType == typeof(string))
                return property.GetValue(null, null) as string;

            var field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(null) as string;

            return null;
        }

        public static void RefreshAll()
        {
            foreach (ExternalTab tab in Tabs)
            {
                if (tab.Broken || tab.Refresh == null)
                    continue;

                try { tab.Refresh(); }
                catch (Exception e) { Disable(tab, "refresh", e); }
            }
        }

        public static void Draw(ExternalTab tab, Rect body)
        {
            if (tab.Broken)
            {
                Skin.Text(new Rect(body.x + 24f, body.y + 18f, body.width - 48f, 40f),
                    tab.Title + " stopped responding and was disabled. See the BepInEx log.",
                    Skin.Body, Skin.InkDim);
                return;
            }

            try { tab.Draw(body); }
            catch (Exception e) { Disable(tab, "draw", e); }
        }

        private static void Disable(ExternalTab tab, string what, Exception e)
        {
            tab.Broken = true;

            // Reflection wraps whatever the panel threw; the inner one is the useful half.
            Exception real = (e as TargetInvocationException)?.InnerException ?? e;
            Plugin.Log.LogError("The '" + tab.Title + "' tab from " + tab.Owner +
                                " threw during " + what + " and was disabled: " + real);
        }
    }
}
