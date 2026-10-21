using System.Diagnostics;

namespace FifaStreetLauncher.Linux;

internal static class InstallFolder
{
    // The ReStreet folder holds Binaries/fifastreet.exe and Game/default.xex. The Linux launcher is published to
    // <ReStreet>/Linux, so look beside it, then one level up, then in the current folder.
    public static string? Find()
    {
        foreach (string dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, ".."), Environment.CurrentDirectory })
        {
            string full = Path.GetFullPath(dir);
            if (File.Exists(Path.Combine(full, "Binaries", "fifastreet.exe"))) return full;
        }
        return null;
    }

    // Wine maps the Linux root to Z:.
    public static string WindowsPath(string linuxPath) => "Z:" + Path.GetFullPath(linuxPath).Replace('/', '\\');
}

// Proton through umu-run (umu-launcher): it runs any Proton build outside Steam, with the Steam Linux Runtime.
internal static class Proton
{
    public static string? UmuRun() =>
        new[] { "/usr/bin/umu-run", "/usr/local/bin/umu-run", Path.Combine(Home, ".local/bin/umu-run") }.FirstOrDefault(File.Exists);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Proton builds the player has installed, best first: CachyOS and GE builds, then Valve's own. An empty list
    // leaves PROTONPATH unset and umu-run fetches its default (UMU-Proton).
    public static List<string> Installed()
    {
        var found = new List<string>();
        foreach (string dir in new[]
        {
            Path.Combine(Home, ".local/share/Steam/compatibilitytools.d"), Path.Combine(Home, ".steam/root/compatibilitytools.d"),
            "/usr/share/steam/compatibilitytools.d", Path.Combine(Home, ".local/share/Steam/steamapps/common"),
        })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string tool in Directory.GetDirectories(dir))
                if (File.Exists(Path.Combine(tool, "proton")) && !found.Any(f => Path.GetFileName(f) == Path.GetFileName(tool)))
                    found.Add(tool);
        }
        static int Rank(string p)
        {
            string n = Path.GetFileName(p).ToLowerInvariant();
            return n.Contains("cachyos") ? 0 : n.Contains("ge-proton") || n.Contains("proton-ge") ? 1 : n.Contains("experimental") ? 2 : 3;
        }
        return found.OrderBy(Rank).ThenByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string DefaultPrefix(string root)
    {
        // A prefix beside the install (how the first CachyOS installs were made) is kept; otherwise one of our own.
        string beside = Path.GetFullPath(Path.Combine(root, "..", "prefix-restreet"));
        if (Directory.Exists(beside)) return beside;
        return Path.Combine(Home, ".local/share/restreet/prefix");
    }

    // The game started through umu-run: umu-run waits for the game ("waitforexitandrun"), so the process ends with it.
    public static ProcessStartInfo StartInfo(string exe, string prefix, string? protonPath)
    {
        string umu = UmuRun() ?? throw new InvalidOperationException("umu-run was not found. Install umu-launcher (pacman -S umu-launcher).");
        Directory.CreateDirectory(prefix);
        var info = new ProcessStartInfo(umu) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        info.ArgumentList.Add(exe);
        info.Environment["WINEPREFIX"] = prefix;
        info.Environment["GAMEID"] = "umu-restreet";
        info.Environment["STORE"] = "none";
        if (!string.IsNullOrEmpty(protonPath)) info.Environment["PROTONPATH"] = protonPath;
        return info;
    }
}
