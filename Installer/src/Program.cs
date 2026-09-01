using System.Text.Json;

namespace Installer;

/// <summary>
/// Installs BepInEx and the Stolen Realm mods into the game folder.
///
/// Nothing in the game's own files is modified. BepInEx works by a proxy DLL sitting next to the
/// executable, so uninstalling is deleting the files this put there - which is what --uninstall
/// does, and why it can promise to leave a vanilla install behind.
/// </summary>
internal static class Program
{
    private const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/BFiveAlive/stolen-realm-mods/main/mods.json";

    /// <summary>Everything BepInEx puts in the game folder, for uninstall.</summary>
    private static readonly string[] BepInExFiles =
    {
        "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt",
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var options = Options.Parse(args);

        if (options.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        Ui.Title("Stolen Realm mod installer");

        try
        {
            return await RunAsync(options);
        }
        catch (Exception e)
        {
            Ui.Error("Failed: " + e.Message);
            Ui.PauseIfInteractive();
            return 1;
        }
    }

    private static async Task<int> RunAsync(Options options)
    {
        string? gameDir = GameLocator.Locate(options.GameDir);

        if (gameDir is null)
        {
            gameDir = AskForGameFolder(options);
            if (gameDir is null)
                return 1;
        }

        Ui.Info("Game folder: " + gameDir);

        if (options.Uninstall)
            return Uninstall(gameDir, options);

        using var downloader = new Downloader();

        Ui.Muted("Fetching the mod list...");
        Manifest manifest = await downloader.FetchManifestAsync(options.ManifestUrl);

        var installed = InstalledState.Load(gameDir);
        bool bepinexPresent = IsBepInExInstalled(gameDir);

        Ui.Info(bepinexPresent
            ? "BepInEx is already installed."
            : "BepInEx is not installed yet and will be set up first.");

        List<ModRelease> chosen = ChooseMods(manifest, installed, options);

        if (chosen.Count == 0 && bepinexPresent)
        {
            Ui.Warn("Nothing selected, so nothing to do.");
            Ui.PauseIfInteractive();
            return 0;
        }

        if (!options.AssumeYes)
        {
            Console.WriteLine();
            Ui.Info($"About to install {chosen.Count} mod(s) into:");
            Ui.Info("  " + gameDir);
            Console.WriteLine();

            // Listed rather than counted: without the picker this is the only place the user sees
            // what they are agreeing to.
            foreach (var mod in chosen)
                Ui.Muted($"  - {mod.Name}  v{mod.Version}");

            Console.WriteLine();
            Ui.Muted("  Re-run with --choose to install only some of them.");
            Console.WriteLine();

            if (!Ui.Confirm("Continue?"))
            {
                Ui.Warn("Cancelled.");
                return 0;
            }
        }

        if (!bepinexPresent)
        {
            if (manifest.BepInEx is null || string.IsNullOrEmpty(manifest.BepInEx.Url))
                throw new InvalidDataException("The mod list does not say where to get BepInEx.");

            Ui.Title("Installing BepInEx " + manifest.BepInEx.Version);

            byte[] zip = await downloader.DownloadAsync(manifest.BepInEx.Url, "BepInEx");
            Downloader.VerifyChecksum(zip, manifest.BepInEx.Sha256, "BepInEx");

            int files = Downloader.ExtractOverGame(zip, gameDir);
            Ui.Success($"  BepInEx installed ({files} files).");
        }

        int failures = 0;

        foreach (var mod in chosen)
        {
            Ui.Title($"Installing {mod.Name} {mod.Version}");

            try
            {
                byte[] zip = await downloader.DownloadAsync(mod.Url, mod.Name);
                Downloader.VerifyChecksum(zip, mod.Sha256, mod.Name);

                int files = Downloader.ExtractOverGame(zip, gameDir);
                installed.Record(mod.Guid, mod.Version);

                Ui.Success($"  Installed ({files} files).");
            }
            catch (Exception e)
            {
                // One bad mod should not abandon the others - each is an independent set of files.
                failures++;
                Ui.Error($"  {mod.Name} failed: {e.Message}");
            }
        }

        installed.Save(gameDir);

        Console.WriteLine();

        if (failures == 0)
        {
            Ui.Success("Done. Launch Stolen Realm from Steam as usual.");
            Ui.Muted("Press F1 in game to open the mod manager.");
        }
        else
        {
            Ui.Warn($"Finished with {failures} failure(s). Everything else was installed.");
        }

        Ui.PauseIfInteractive();
        return failures == 0 ? 0 : 1;
    }

    private static string? AskForGameFolder(Options options)
    {
        Ui.Warn("Could not find Stolen Realm automatically.");

        if (options.AssumeYes)
        {
            Ui.Error("Re-run with --game \"<path to Stolen Realm>\".");
            return null;
        }

        Ui.Muted(@"It is usually somewhere like C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            string? typed = Ui.Prompt("Path to the Stolen Realm folder (blank to give up):");

            if (string.IsNullOrWhiteSpace(typed))
                break;

            string candidate = typed.Trim().Trim('"');

            if (GameLocator.IsGameFolder(candidate))
                return candidate;

            Ui.Error("That folder does not contain \"Stolen Realm.exe\".");
        }

        Ui.PauseIfInteractive();
        return null;
    }

    /// <summary>
    /// Everything in the mod list, unless the user asked to pick.
    ///
    /// The mods are independent and each one does nothing until its settings are changed, so the
    /// useful default is to have them all available and decide in game rather than to decide here,
    /// before having seen any of them. Picking is still there behind --choose for anyone who wants
    /// a smaller install.
    /// </summary>
    private static List<ModRelease> ChooseMods(Manifest manifest, InstalledState installed, Options options)
    {
        if (!options.Choose)
            return manifest.Mods;

        return Ui.MultiSelect(
            "Choose what to install",
            manifest.Mods,
            m => $"{m.Name}  v{m.Version}",
            m =>
            {
                string? have = installed.VersionOf(m.Guid);

                string status = have is null
                    ? m.Description
                    : have == m.Version
                        ? $"{m.Description}  (already installed)"
                        : $"{m.Description}  (installed: v{have})";

                return status;
            },
            // All ticked to start with, matching what installing without --choose would do; the
            // point of this screen is to take things off the list.
            m => true);
    }

    private static bool IsBepInExInstalled(string gameDir)
    {
        return File.Exists(Path.Combine(gameDir, "winhttp.dll"))
            && File.Exists(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll"));
    }

    private static int Uninstall(string gameDir, Options options)
    {
        Ui.Title("Uninstall");

        if (!IsBepInExInstalled(gameDir))
        {
            Ui.Info("BepInEx is not installed here; nothing to remove.");
            Ui.PauseIfInteractive();
            return 0;
        }

        bool keepConfigs = options.AssumeYes
            || Ui.Confirm("Keep your mod settings (BepInEx/config)?");

        if (!options.AssumeYes && !Ui.Confirm("Remove BepInEx and all mods from this install?", defaultYes: false))
        {
            Ui.Warn("Cancelled.");
            return 0;
        }

        string bepinex = Path.Combine(gameDir, "BepInEx");
        string? savedConfigs = null;

        if (keepConfigs && Directory.Exists(Path.Combine(bepinex, "config")))
        {
            // Moved aside rather than copied, then moved back: the config folder can be large and
            // this way there is never a moment where the only copy is one we are deleting.
            savedConfigs = Path.Combine(gameDir, "BepInEx-config-backup");
            if (Directory.Exists(savedConfigs))
                Directory.Delete(savedConfigs, true);

            Directory.Move(Path.Combine(bepinex, "config"), savedConfigs);
        }

        if (Directory.Exists(bepinex))
            Directory.Delete(bepinex, true);

        foreach (string file in BepInExFiles)
        {
            string path = Path.Combine(gameDir, file);
            if (File.Exists(path))
                File.Delete(path);
        }

        if (savedConfigs is not null)
        {
            Directory.CreateDirectory(bepinex);
            Directory.Move(savedConfigs, Path.Combine(bepinex, "config"));
            Ui.Muted("Kept your settings in BepInEx/config.");
        }

        Ui.Success("Removed. The game is back to vanilla.");
        Ui.PauseIfInteractive();
        return 0;
    }
}

/// <summary>What the installer last put in place, so a re-run can show upgrades.</summary>
internal sealed class InstalledState
{
    private Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);

    private static string PathFor(string gameDir) =>
        Path.Combine(gameDir, "BepInEx", "mod-updates", "installed.json");

    public static InstalledState Load(string gameDir)
    {
        var state = new InstalledState();

        try
        {
            string path = PathFor(gameDir);
            if (File.Exists(path))
            {
                state.versions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Only used to pre-tick checkboxes and show "already installed", so a corrupt file
            // costs a nicety rather than the install.
        }

        return state;
    }

    public string? VersionOf(string guid) => versions.TryGetValue(guid, out string? v) ? v : null;

    public void Record(string guid, string version) => versions[guid] = version;

    public void Save(string gameDir)
    {
        try
        {
            string path = PathFor(gameDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path,
                JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Ui.Muted("Could not record what was installed: " + e.Message);
        }
    }
}

internal sealed class Options
{
    public string? GameDir { get; private set; }
    public string ManifestUrl { get; private set; } = "";
    public bool Choose { get; private set; }
    public bool AssumeYes { get; private set; }
    public bool Uninstall { get; private set; }
    public bool ShowHelp { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options { ManifestUrl = DefaultUrl };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--game" when i + 1 < args.Length:
                    options.GameDir = args[++i];
                    break;
                case "--manifest" when i + 1 < args.Length:
                    options.ManifestUrl = args[++i];
                    break;
                case "--choose":
                case "--pick":
                    options.Choose = true;
                    break;
                case "--all":
                    // Kept so older instructions still work; installing everything is now what
                    // happens anyway.
                    options.Choose = false;
                    break;
                case "-y":
                case "--yes":
                    options.AssumeYes = true;
                    break;
                case "--uninstall":
                    options.Uninstall = true;
                    break;
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }

    private const string DefaultUrl =
        "https://raw.githubusercontent.com/BFiveAlive/stolen-realm-mods/main/mods.json";

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Stolen Realm mod installer

              (no arguments)      find the game and install every mod, after confirming
              --game <path>       use this Stolen Realm folder instead of searching
              --manifest <url>    read the mod list from somewhere else
              --choose            pick which mods to install instead of installing all
              -y, --yes           no prompts; installs everything
              --uninstall         remove BepInEx and all mods, leaving the game vanilla
              -h, --help          this text
            """);
    }
}
