using System;
using System.Collections.Generic;
using System.Text;

namespace RunTagsMod
{
    /// <summary>
    /// Builds the line that replaces "Roguelike" on a character card, inside a fixed width.
    ///
    /// The card has room for roughly twenty-odd characters before the text starts crowding the
    /// name above it, and the thing that varies is how many people played. So the names are given
    /// whatever room is left after the difficulty, and trimmed to fit.
    ///
    /// Not trimmed equally, though. An equal share wastes room whenever one name is short: three
    /// names in fifteen characters would give "Al", "Bo" and "Chr" five each, when "Al" and "Bo"
    /// only need two and four of those fifteen could have gone to the third name. So the shortest
    /// name is served first and anything it does not need is passed on, which is why
    /// <c>Ann, Bartholomew</c> comes out as <c>Ann|Bartholom</c> rather than <c>Ann|Barth</c>.
    /// </summary>
    internal static class LabelFitter
    {
        private const char Separator = '|';

        /// <summary>
        /// Below this a name is unreadable, so the party is summarised as a count instead of being
        /// shaved down to initials.
        /// </summary>
        private const int MinimumNameLength = 3;

        /// <summary>
        /// The finished label, or null when there is nothing worth saying.
        ///
        /// <paramref name="italicHost"/> wraps the host in rich-text italics. The tags are not
        /// counted against the budget because they do not render.
        /// </summary>
        internal static string Build(int difficultyIndex, string host, IList<string> others,
            int budget, bool italicHost)
        {
            var names = new List<string>();

            if (!string.IsNullOrEmpty(host))
                names.Add(host.Trim());

            if (others != null)
            {
                foreach (string other in others)
                {
                    if (!string.IsNullOrEmpty(other) && !Same(other, host))
                        names.Add(other.Trim());
                }
            }

            string prefix = difficultyIndex >= 0 ? Roman(difficultyIndex + 1) + ": " : string.Empty;

            if (names.Count == 0)
                return prefix.Length == 0 ? null : prefix.TrimEnd(' ', ':');

            int available = budget - prefix.Length;

            // Not even one readable name fits, so the difficulty alone is the honest answer.
            if (available < MinimumNameLength)
                return prefix.TrimEnd(' ', ':');

            List<int> widths = Allocate(names, available);

            if (widths == null)
            {
                // Too many people for anyone to be legible: name the host and count the rest.
                string tail = names.Count > 1 ? " +" + (names.Count - 1) : string.Empty;
                int room = Math.Max(MinimumNameLength, available - tail.Length);

                return prefix + Decorate(Clip(names[0], room), italicHost) + tail;
            }

            var text = new StringBuilder(budget + 8);
            text.Append(prefix);

            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                    text.Append(Separator);

                string clipped = Clip(names[i], widths[i]);
                text.Append(i == 0 ? Decorate(clipped, italicHost) : clipped);
            }

            return text.ToString();
        }

        /// <summary>
        /// How many characters each name gets, or null if they cannot all be shown legibly.
        ///
        /// Shortest first: each name takes an equal share of what is left, or its whole length if
        /// that is less, and whatever it does not take is still there for the names after it.
        /// </summary>
        private static List<int> Allocate(List<string> names, int available)
        {
            int forNames = available - (names.Count - 1);      // the separators are not free
            if (forNames < names.Count * MinimumNameLength)
                return null;

            var order = new List<int>();
            for (int i = 0; i < names.Count; i++)
                order.Add(i);

            order.Sort((a, b) => names[a].Length.CompareTo(names[b].Length));

            var widths = new int[names.Count];
            int remaining = forNames;
            int left = names.Count;

            foreach (int index in order)
            {
                int share = remaining / left;
                int take = Math.Min(names[index].Length, share);

                widths[index] = take;
                remaining -= take;
                left--;
            }

            return new List<int>(widths);
        }

        private static string Clip(string name, int width)
        {
            return name.Length <= width ? name : name.Substring(0, Math.Max(1, width));
        }

        private static string Decorate(string text, bool italic)
        {
            return italic ? "<i>" + text + "</i>" : text;
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(b) && string.Equals(a?.Trim(), b.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] Numerals =
        {
            "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
            "XI", "XII", "XIII", "XIV", "XV",
        };

        /// <summary>Roman for the difficulty, falling back to the plain number past the table.</summary>
        internal static string Roman(int value)
        {
            if (value < 0)
                return string.Empty;

            return value < Numerals.Length ? Numerals[value] : value.ToString();
        }
    }
}
