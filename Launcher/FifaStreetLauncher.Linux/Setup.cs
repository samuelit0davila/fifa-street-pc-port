using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FifaStreetLauncher.Linux;

// First run (no ReStreet folder yet): installs ReStreet from the player's ISO without a terminal. It downloads the
// release setup (or uses one beside the launcher), runs its headless ISO install through Proton, copies this launcher
// into <ReStreet>/Linux and adds the menu entries. The setup's own window needs WebView2, which Proton lacks.
internal sealed partial class Setup : IPageHost
{
    private const string Repo = "samuelit0davila/fifa-street-pc-port";

    // Regions the online patch offers (keep in sync with GlassUi.cs).
    private static readonly (string Name, string Address)[] Regions =
    {
        ("Europe", "57.129.180.15"), ("South America", "216.238.113.7"),
    };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _-]{1,13}[A-Za-z0-9_-]\z")]
    private static partial Regex OnlineName();

    [GeneratedRegex(@"^\[(\d+)%\]\s*(.*)$")]
    private static partial Regex ProgressLine();

    private readonly object sync = new();
    private readonly List<string> protons = Proton.Installed();
    private string iso = FindIso() ?? "";
    private string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "ReStreet");
    private string name = "";
    private int region;
    private string stage = "";
    private int percent = -1;
    private string error = "";
    private bool busy, done;
    private readonly List<string> log = new();

    public bool Busy { get { lock (sync) return busy; } }
    public PageServer? Page { get; set; }
    public Func<string, string, Task<string?>>? PickFile { get; set; }    // (title, start folder) -> path
    public Func<string, string, Task<string?>>? PickFolder { get; set; }
    public Action? CloseWindow { get; set; }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // A FIFA Street ISO in the usual download folders, so most players only type a name.
    private static string? FindIso()
    {
        var folders = new[] { "Downloads", "Transferências", "Descargas", "Téléchargements", "Desktop", "Games", "" }
            .Select(f => Path.Combine(Home, f)).Where(Directory.Exists);
        foreach (string dir in folders)
        {
            try
            {
                var isos = Directory.GetFiles(dir, "*.iso").Concat(Directory.GetFiles(dir, "*.ISO")).ToList();
                string? fifa = isos.FirstOrDefault(f => Path.GetFileName(f).Contains("fifa", StringComparison.OrdinalIgnoreCase));
                if (fifa != null) return fifa;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
        }
        return null;
    }

    public void OnMessage(string json)
    {
        JsonElement m;
        try { using var doc = JsonDocument.Parse(json); m = doc.RootElement.Clone(); }
        catch (JsonException) { return; }
        string Str(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        switch (Str("cmd"))
        {
            case "pickIso": _ = Pick(true); return;
            case "pickTarget": _ = Pick(false); return;
            case "set":
                lock (sync)
                {
                    if (busy) break;
                    if (m.TryGetProperty("name", out _)) name = Str("name").Trim();
                    if (m.TryGetProperty("iso", out _)) iso = Str("iso").Trim();
                    if (m.TryGetProperty("target", out _)) target = Str("target").Trim();
                    if (m.TryGetProperty("region", out var r) && r.TryGetInt32(out int ri) && ri >= 0 && ri < Regions.Length) region = ri;
                }
                break;
            case "install": _ = Task.Run(Install); return;
            case "play": OpenLauncher(); return;
            case "close": CloseWindow?.Invoke(); return;
            case "state": break;
        }
        Push();
    }

    private async Task Pick(bool isoFile)
    {
        var picker = isoFile ? PickFile : PickFolder;
        if (picker == null) return;
        string start;
        lock (sync) start = Path.GetDirectoryName(isoFile ? iso : target) is { Length: > 0 } d && Directory.Exists(d) ? d : Home;
        string? chosen = await picker(isoFile ? "Choose your FIFA Street (Xbox 360) ISO" : "Choose where to install ReStreet", start);
        if (string.IsNullOrEmpty(chosen)) return;
        lock (sync)
        {
            if (isoFile) iso = chosen;
            // A chosen parent folder gets a ReStreet folder inside it, unless it is already an empty one.
            else target = Directory.Exists(chosen) && Directory.EnumerateFileSystemEntries(chosen).Any() ? Path.Combine(chosen, "ReStreet") : chosen;
        }
        Push();
    }

    public void Push()
    {
        string json;
        lock (sync)
        {
            json = JsonSerializer.Serialize(new
            {
                type = "setup", iso, target, name, region, regions = Regions.Select(r => r.Name).ToArray(),
                proton = Proton.Missing(protons) ?? Proton.Describe(protons),
                protonOk = Proton.Missing(protons) == null,
                stage, percent, error, busy, done, log = log.TakeLast(8).ToArray(),
                version = "V" + typeof(Setup).Assembly.GetName().Version!.ToString(3),
            });
        }
        Page?.Send(json);
    }

    private void Step(string text, int pct = -1)
    {
        lock (sync)
        {
            stage = text;
            if (pct >= 0) percent = pct;
            log.Add(text);
        }
        Push();
    }

    private string? Check()
    {
        if (Proton.Missing(protons) is { } missing) return missing;
        if (!File.Exists(iso)) return "Choose your FIFA Street (Xbox 360) ISO file.";
        if (!OnlineName().IsMatch(name)) return "Choose an online name with 3 to 15 letters, numbers, spaces, _ or -.";
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathRooted(target)) return "Choose the folder to install ReStreet to.";
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            return InstallFolder.IsInstall(target) ? "ReStreet is already installed there. Pick another folder." : "That folder is not empty. Pick a new or empty folder.";
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!);
        try
        {
            string probe = Path.GetFullPath(target);
            while (!Directory.Exists(probe)) probe = Path.GetDirectoryName(probe)!;
            if (new DriveInfo(probe).AvailableFreeSpace < 12L * 1024 * 1024 * 1024) return "Not enough free space there: about 12 GB are needed.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { Debug.WriteLine(ex.Message + drive.Name); }
        return null;
    }

    private async Task Install()
    {
        lock (sync)
        {
            if (busy || done) return;
            error = Check() ?? "";
            if (error.Length > 0) { Push(); return; }
            busy = true;
            percent = 0;
            log.Clear();
        }
        Push();
        try
        {
            string setupExe = await GetSetup();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await RunSetup(setupExe);
            Step("Copying the Linux launcher", 99);
            string linux = Path.Combine(target, "Linux");
            CopyFolder(AppContext.BaseDirectory, linux);
            SetRegion();
            DesktopEntries.Create(Path.Combine(linux, "restreet-launcher"), target);
            Step("ReStreet is installed. Press PLAY to open the launcher.", 100);
            lock (sync) done = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException
                                       or TaskCanceledException or System.ComponentModel.Win32Exception or JsonException)
        {
            lock (sync) error = ex.Message;
        }
        finally
        {
            lock (sync) busy = false;
            Push();
        }
    }

    // "ReStreet-2.1.10-Setup.exe" -> 2.1.10 (names sort wrong as text: "2.1.10" < "2.1.9"); anything else is 0.
    internal static Version SetupVersion(string file)
    {
        string name = Path.GetFileName(file);
        return name.StartsWith("ReStreet-", StringComparison.Ordinal) && name.EndsWith("-Setup.exe", StringComparison.Ordinal) &&
               Version.TryParse(name["ReStreet-".Length..^"-Setup.exe".Length], out var v) ? v : new Version(0, 0);
    }

    private static string? NewestSetup(IEnumerable<string> dirs) =>
        dirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "ReStreet-*-Setup.exe")).MaxBy(SetupVersion);

    // The setup: one beside the launcher (offline installs), or the latest release's, checked against its .sha256.
    // One the player downloaded is used when it is that release's setup, or (no network) the newest one there.
    private async Task<string> GetSetup()
    {
        string? beside = NewestSetup(new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "..") });
        if (beside != null) { Step("Using " + Path.GetFileName(beside), 1); return beside; }
        string[] downloads = { Path.Combine(Home, "Downloads"), Path.Combine(Home, "Transferências") };
        Step("Looking for the latest ReStreet release", 1);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReStreet-Linux-Launcher");
        string releaseJson;
        try { releaseJson = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"); }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && NewestSetup(downloads) is not null)
        {
            string offline = NewestSetup(downloads)!;
            Step("No connection to GitHub; using " + Path.GetFileName(offline), 1);
            return offline;
        }
        var release = JsonNode.Parse(releaseJson)!;
        var assets = release["assets"]!.AsArray();
        string Url(string suffix) => assets.Select(a => a!).FirstOrDefault(a => a["name"]!.GetValue<string>().EndsWith(suffix, StringComparison.Ordinal))
            ?["browser_download_url"]?.GetValue<string>() ?? throw new InvalidOperationException("The latest release has no " + suffix + " file.");
        string exeUrl = Url("-Setup.exe"), shaUrl = Url("-Setup.exe.sha256");
        string cache = Path.Combine(Home, ".cache", "restreet");
        Directory.CreateDirectory(cache);
        string fileName = Path.GetFileName(new Uri(exeUrl).LocalPath);
        string file = Path.Combine(cache, fileName);
        string expected = (await http.GetStringAsync(shaUrl)).Split(' ', '\t', '\n')[0].Trim().ToLowerInvariant();
        string? downloaded = downloads.Select(d => Path.Combine(d, fileName)).FirstOrDefault(f => File.Exists(f) && Sha256(f) == expected);
        if (downloaded != null) { Step("Using " + fileName + " from " + Path.GetDirectoryName(downloaded), 15); return downloaded; }
        if (!File.Exists(file) || Sha256(file) != expected)
        {
            using var response = await http.GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(file + ".part"))
            {
                var buffer = new byte[1 << 20];
                long got = 0;
                int read, lastShown = -1;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    got += read;
                    int shown = total > 0 ? (int)(got * 100 / total) : -1;
                    if (shown != lastShown && shown % 5 == 0)
                    {
                        lastShown = shown;
                        Step($"Downloading the setup: {got / (1024 * 1024)} of {total / (1024 * 1024)} MB", 1 + shown * 14 / 100);
                    }
                }
            }
            File.Move(file + ".part", file, true);
            if (Sha256(file) != expected)
            {
                File.Delete(file);
                throw new InvalidOperationException("The downloaded setup does not match its checksum. Try again.");
            }
        }
        Step("Setup downloaded and checked", 15);
        return file;
    }

    private static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // The setup's headless install (--iso-install-test ISO TARGET NAME). It writes "[NN%] step" lines to
    // <target>.iso-install-test.log and ends with OK or "ERROR: ...".
    private async Task RunSetup(string setupExe)
    {
        string logFile = Path.GetFullPath(target).TrimEnd('/') + ".iso-install-test.log";
        File.Delete(logFile);
        string prefix = Proton.DefaultPrefix(target);
        var info = Proton.StartInfo(setupExe, prefix, protons.FirstOrDefault());
        foreach (string a in new[] { "--iso-install-test", InstallFolder.WindowsPath(iso), InstallFolder.WindowsPath(target), name }) info.ArgumentList.Add(a);
        info.RedirectStandardOutput = info.RedirectStandardError = true;
        Step("Starting Proton (the first time takes a minute)", 16);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Proton could not be started.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        long position = 0;
        string last = "";
        while (true)
        {
            bool exited = process.WaitForExit(1000);
            if (File.Exists(logFile))
            {
                using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    last = line.Trim().Length > 0 ? line.Trim() : last;
                    var p = ProgressLine().Match(line.Trim());
                    // The setup's 0-100 maps to 16-98 here (download before, launcher copy after).
                    if (p.Success) Step(p.Groups[2].Value.TrimEnd('…', '.'), 16 + int.Parse(p.Groups[1].Value) * 82 / 100);
                }
                position = stream.Position;
            }
            if (exited) break;
        }
        await Task.CompletedTask;
        if (last.StartsWith("ERROR:", StringComparison.Ordinal)) throw new InvalidOperationException(last[6..].Trim());
        if (!InstallFolder.IsInstall(target))
            throw new InvalidOperationException("The setup stopped before the end" + (last.Length > 0 ? ": " + last : ". See " + logFile));
    }

    private static void CopyFolder(string from, string to)
    {
        from = Path.GetFullPath(from).TrimEnd('/');
        Directory.CreateDirectory(to);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, dest, true);
            File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
        }
    }

    // The setup always picks the first region: the chosen one goes into both online.json copies.
    private void SetRegion()
    {
        string address = Regions[region].Address;
        foreach (string path in new[] { Path.Combine(target, "online.json"), Path.Combine(target, "Binaries", "online.json") })
        {
            if (!File.Exists(path)) continue;
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            node["server"] = address;
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void OpenLauncher()
    {
        string launcher = Path.Combine(target, "Linux", "restreet-launcher");
        if (!done || !File.Exists(launcher)) return;
        Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = Path.GetDirectoryName(launcher)!, UseShellExecute = false });
        CloseWindow?.Invoke();
    }
}

// The applications menu entries (and a desktop icon): the launcher, and "direct play" with the saved settings.
internal static class DesktopEntries
{
    public static void Create(string launcher, string root)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string apps = Path.Combine(home, ".local/share/applications");
        Directory.CreateDirectory(apps);
        string dir = Path.GetDirectoryName(launcher)!;
        string icon = Path.Combine(dir, "restreet.jpg");
        string Entry(string name, string args) =>
            $"[Desktop Entry]\nType=Application\nName={name}\nExec=\"{launcher}\" --root \"{root}\"{args}\nPath={dir}\n" +
            $"Icon={icon}\nTerminal=false\nCategories=Game;\nComment=FIFA Street (2012) recompiled\n";
        File.WriteAllText(Path.Combine(apps, "restreet-launcher.desktop"), Entry("ReStreet", ""));
        File.WriteAllText(Path.Combine(apps, "restreet-play.desktop"), Entry("ReStreet (direct play)", " --play"));
        // The desktop icon, where the desktop shows the Desktop folder (KDE on the Deck's desktop mode, COSMIC...).
        try
        {
            string desktop = Path.Combine(home, "Desktop");
            using (var xdg = Process.Start(new ProcessStartInfo("xdg-user-dir", "DESKTOP") { RedirectStandardOutput = true, UseShellExecute = false }))
                if (xdg != null) { string d = xdg.StandardOutput.ReadToEnd().Trim(); xdg.WaitForExit(2000); if (d.Length > 0) desktop = d; }
            if (Directory.Exists(desktop))
            {
                string path = Path.Combine(desktop, "ReStreet.desktop");
                File.WriteAllText(path, Entry("ReStreet", ""));
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex.Message); }
    }
}
