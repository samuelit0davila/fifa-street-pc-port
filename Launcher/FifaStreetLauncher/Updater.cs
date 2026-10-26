using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace FifaStreetLauncher;

// Automatic updates: at start the launcher asks GitHub for the latest ReStreet release. When it is newer than this
// launcher and has a patch (.exe plus .exe.sha256), the page offers it. "Update" downloads both files from GitHub only,
// checks the SHA-256, starts the patch with --auto-update (it waits for the launcher to close, installs with the saved
// name, key and server, and reopens the launcher) and closes the launcher. Only installs that have the online patch.
public partial class LauncherForm
{
    internal const string LauncherVersion = "2.1.3";
    private const string ReleasesApi = "https://api.github.com/repos/samuelit0davila/fifa-street-pc-port/releases/latest";
    private static readonly HttpClient UpdateHttp = CreateUpdateHttp();
    private (string Version, string ExeUrl, string ShaUrl, string Notes)? pendingUpdate;
    private bool updating;

    private static HttpClient CreateUpdateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReStreetLauncher/" + LauncherVersion);
        return http;
    }

    // "v1.5.1-online-beta" -> 1.5.1; anything unparsable compares as 0.
    internal static Version ParseVersion(string tag)
    {
        var digits = new string(tag.TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        return Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v) ? v : new Version(0, 0);
    }

    private static bool FromGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
        (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));

    private System.Windows.Forms.Timer? updateTimer;

    // A launcher left open for days still hears about a new version: ask GitHub again every 2 hours (its API allows
    // 60 requests an hour per address) until one is found.
    private void StartUpdateChecks()
    {
        _ = CheckForUpdateAsync();
        updateTimer = new System.Windows.Forms.Timer { Interval = 2 * 60 * 60 * 1000 };
        updateTimer.Tick += (_, _) => { if (pendingUpdate == null) _ = CheckForUpdateAsync(); };
        updateTimer.Start();
    }

    private async Task CheckForUpdateAsync()
    {
        if (!await FindUpdateAsync()) return;
        // The page may still be loading (it asks again with "state"); give it a moment to say whether it is up.
        await Task.Delay(4000);
        await OfferUpdateAsync();
    }

    // Sets pendingUpdate when GitHub has a newer release with a patch; true when it did.
    private async Task<bool> FindUpdateAsync(CancellationToken cancel = default)
    {
        if (LoadOnlineConfig() == null) return false;  // offline installs update with the full installer
        // RESTREET_NO_UPDATE=1: never offer an update (a self-built launcher would be replaced by the official one).
        if (Environment.GetEnvironmentVariable("RESTREET_NO_UPDATE") == "1") return false;
        try
        {
            using var doc = JsonDocument.Parse(await UpdateHttp.GetStringAsync(ReleasesApi, cancel));
            var r = doc.RootElement;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (ParseVersion(tag) <= ParseVersion(LauncherVersion)) return false;
            string? exe = null, sha = null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (!FromGitHub(url)) continue;
                if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = url;
            }
            if (exe == null || sha == null) return false;
            string notes = r.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            pendingUpdate = (ParseVersion(tag).ToString(), exe, sha, notes);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No network or GitHub unavailable: try again later.
            return false;
        }
    }

    private async Task OfferUpdateAsync()
    {
        if (pendingUpdate is not { } u) return;
        if (PageConnected)
        {
            PostToPage(new { type = "update", version = u.Version, notes = u.Notes });
        }
        else
        {
            // Classic interface (no WebView2, for example on Wine/Proton): ask with a plain dialog.
            var answer = MessageBox.Show(this, "ReStreet " + u.Version + " is available.\n\nThe launcher downloads it from GitHub, " +
                "checks it and installs it for you. Your name, server and saves are kept.\n\nUpdate now?", "ReStreet update",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                UpdateStatus("Downloading the update...", true);
                await InstallUpdateAsync();
            }
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (pendingUpdate is not { } u || updating || gameRunning) return;
        updating = true;
        string folder = Path.Combine(Path.GetTempPath(), "ReStreet-update");
        string exePath = Path.Combine(folder, "ReStreet-Update-" + u.Version + ".exe");
        try
        {
            Directory.CreateDirectory(folder);
            string expected = (await UpdateHttp.GetStringAsync(u.ShaUrl)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            if (expected.Length != 64) throw new InvalidDataException("bad checksum file");
            using (var response = await UpdateHttp.GetAsync(u.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0, done = 0;
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(exePath);
                var buffer = new byte[1 << 20];
                int read, lastPercent = -1;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != lastPercent) { lastPercent = percent; PostToPage(new { type = "updateProgress", percent }); }
                }
            }
            string actual;
            await using (var check = File.OpenRead(exePath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant();
            if (actual != expected) throw new InvalidDataException("the download is damaged (checksum mismatch)");

            string root = AppContext.BaseDirectory.TrimEnd('\\');
            Process.Start(new ProcessStartInfo(exePath)
            {
                ArgumentList = { "--auto-update", root, Environment.ProcessId.ToString() },
                UseShellExecute = false,
            });
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            updating = false;
            try { File.Delete(exePath); } catch (IOException) { }
            PostToPage(new { type = "updateError", message = "Update failed: " + ex.Message });
            if (!PageConnected) UpdateStatus("Update failed: " + ex.Message, false);
        }
    }

    private void PostToPage(object message)
    {
        SendToPage(JsonSerializer.Serialize(message));
    }
}
