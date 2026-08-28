namespace Installer;

/// <summary>Console presentation. Kept in one place so the flow in Program reads as a story.</summary>
internal static class Ui
{
    private static bool progressActive;

    public static void Title(string text)
    {
        Console.WriteLine();
        WriteColoured(text, ConsoleColor.White);
        Console.WriteLine(new string('-', text.Length));
    }

    public static void Info(string text) => Console.WriteLine(text);

    public static void Muted(string text) => WriteColoured(text, ConsoleColor.DarkGray);

    public static void Success(string text) => WriteColoured(text, ConsoleColor.Green);

    public static void Warn(string text) => WriteColoured(text, ConsoleColor.Yellow);

    public static void Error(string text) => WriteColoured(text, ConsoleColor.Red);

    private static void WriteColoured(string text, ConsoleColor colour)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public static bool Confirm(string question, bool defaultYes = true)
    {
        Console.Write($"{question} {(defaultYes ? "[Y/n]" : "[y/N]")} ");

        string? answer = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(answer))
            return defaultYes;

        return answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Prompt(string question)
    {
        Console.Write($"{question} ");
        return Console.ReadLine()?.Trim();
    }

    /// <summary>
    /// A checkbox list driven by typed numbers. Deliberately not a cursor-key menu: this has to
    /// behave identically in a double-clicked console window, Windows Terminal and a piped shell.
    /// </summary>
    public static List<T> MultiSelect<T>(
        string heading,
        IReadOnlyList<T> items,
        Func<T, string> label,
        Func<T, string> detail,
        Func<T, bool> initiallySelected)
    {
        var selected = items.Select(initiallySelected).ToArray();

        while (true)
        {
            Title(heading);

            for (int i = 0; i < items.Count; i++)
            {
                Console.Write($"  {i + 1,2}. [{(selected[i] ? "x" : " ")}] ");
                Console.WriteLine(label(items[i]));

                string extra = detail(items[i]);
                if (!string.IsNullOrEmpty(extra))
                    Muted($"          {extra}");
            }

            Console.WriteLine();
            Muted("  Type numbers to toggle (e.g. \"1 3\"), \"a\" for all, \"n\" for none.");
            Console.Write("  Enter to continue: ");

            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                return items.Where((_, i) => selected[i]).ToList();

            if (input.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                Array.Fill(selected, true);
                continue;
            }

            if (input.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                Array.Fill(selected, false);
                continue;
            }

            foreach (string token in input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out int index) && index >= 1 && index <= items.Count)
                    selected[index - 1] = !selected[index - 1];
            }
        }
    }

    public static void Progress(string label, long read, long? total)
    {
        // Redrawing on every chunk flickers and, when output is redirected, fills the log with
        // thousands of lines. Neither is worth more than a percentage that moves.
        if (!Console.IsOutputRedirected)
        {
            string text = total is > 0
                ? $"  {label}  {read * 100 / total.Value,3}%"
                : $"  {label}  {read / 1024:N0} KB";

            Console.Write($"\r{text.PadRight(60)}");
            progressActive = true;
        }
    }

    public static void ProgressDone()
    {
        if (progressActive)
        {
            Console.Write("\r".PadRight(62));
            Console.Write("\r");
            progressActive = false;
        }
    }

    public static void PauseIfInteractive()
    {
        // A double-clicked exe closes its window the instant Main returns, taking the result with
        // it. Only worth pausing when there is a human there to press a key.
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return;

        Console.WriteLine();
        Console.Write("Press any key to close...");
        Console.ReadKey(true);
        Console.WriteLine();
    }
}
