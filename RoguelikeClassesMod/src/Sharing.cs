using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Sending classes to other people and taking theirs.
    ///
    /// A class is plain JSON, so sharing one is sharing a file. What that needs from the editor is
    /// somewhere agreed to put it - there is no file dialog in IMGUI, and asking someone to type a
    /// path into a text field is the kind of thing this editor exists to stop. So there is one
    /// folder: exports land in it, and anything dropped into it can be imported.
    ///
    /// Importing never overwrites. A class whose id is already taken comes in under a new one, and
    /// the panel says which - two people who both kept the default id for their first class would
    /// otherwise silently clobber each other's work, and an id is not cosmetic: characters are tied
    /// to their class by a Guid derived from it.
    /// </summary>
    internal static class Sharing
    {
        internal static string Folder => ClassSources.SharedFolder;

        /// <summary>The files sitting in the shared folder, newest first.</summary>
        internal static List<string> Available()
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return new List<string>();

                return Directory.GetFiles(Folder, "*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        internal static void Reveal()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                System.Diagnostics.Process.Start(Folder);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not open the shared folder: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ exporting

        /// <summary>Writes one class to its own file. Returns what to tell the player.</summary>
        internal static string Export(ClassDefinition definition)
        {
            if (definition == null)
                return "Nothing to export.";

            var library = new ClassLibraryFile
            {
                Schema = 1,
                Defaults = ClassStore.Defaults,
                Classes = new List<ClassDefinition> { definition },
            };

            string path = Path.Combine(Folder, Safe(definition.Id) + ".json");

            return ClassSources.Write(path, library, out string problem)
                ? "Exported " + Name(definition) + " to " + path
                : problem;
        }

        /// <summary>
        /// Writes everything that is yours - your own classes and the shipped ones you have changed.
        /// The ones you never touched are left out: they are the same twenty the other person
        /// already has, and sending them back would only overwrite their copies with yours.
        /// </summary>
        internal static string ExportAll()
        {
            List<ClassDefinition> mine = ClassStore.Classes
                .Where(x => x != null && (!ClassStore.IsShipped(x.Id) || ClassStore.IsEdited(x)))
                .ToList();

            if (mine.Count == 0)
                return "You have not made or changed any classes yet, so there is nothing to send.";

            var library = new ClassLibraryFile
            {
                Schema = 1,
                Defaults = ClassStore.Defaults,
                Classes = mine,
            };

            string path = Path.Combine(Folder,
                "classes-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".json");

            return ClassSources.Write(path, library, out string problem)
                ? "Exported " + mine.Count + " class(es) to " + path
                : problem;
        }

        // ------------------------------------------------------------------ importing

        internal static string Import(string path)
        {
            ClassLibraryFile incoming = ClassSources.Read(path, out string problem);

            if (incoming == null)
                return problem ?? "There is nothing in " + Path.GetFileName(path) + ".";

            if (incoming.Classes == null || incoming.Classes.Count == 0)
                return Path.GetFileName(path) + " has no classes in it.";

            var added = new List<string>();
            var renamed = new List<string>();

            foreach (ClassDefinition entry in incoming.Classes)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id))
                    continue;

                string original = entry.Id;
                entry.Id = FreeId(entry.Id);

                if (!string.Equals(entry.Id, original, StringComparison.Ordinal))
                    renamed.Add(original + " as " + entry.Id);
                else
                    added.Add(entry.Id);

                ClassStore.Add(entry);
            }

            if (added.Count == 0 && renamed.Count == 0)
                return "Nothing in " + Path.GetFileName(path) + " could be read as a class.";

            string message = "Imported " + (added.Count + renamed.Count) + " class(es) from "
                             + Path.GetFileName(path) + ".";

            if (renamed.Count > 0)
                message += " Already had those ids, so brought in " + string.Join(", ", renamed.ToArray()) + ".";

            return message + " Save changes to keep them.";
        }

        /// <summary>An id nothing is using, so an import can never overwrite what is already here.</summary>
        private static string FreeId(string wanted)
        {
            var taken = new HashSet<string>(
                ClassStore.Classes.Where(x => x?.Id != null).Select(x => x.Id),
                StringComparer.OrdinalIgnoreCase);

            if (!taken.Contains(wanted))
                return wanted;

            for (int i = 2; ; i++)
            {
                string candidate = wanted + "-" + i;
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private static string Name(ClassDefinition definition)
        {
            return string.IsNullOrEmpty(definition.Name) ? definition.Id : definition.Name;
        }

        private static string Safe(string id)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var text = new System.Text.StringBuilder(id.Length);

            foreach (char c in id)
                text.Append(invalid.Contains(c) ? '_' : c);

            return text.ToString();
        }
    }
}
