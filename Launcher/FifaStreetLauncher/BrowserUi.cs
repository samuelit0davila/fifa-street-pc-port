using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FifaStreetLauncher;

// The new launcher page without WebView2 (Wine/Proton, or a Windows without the runtime): the same ui/launcher.html is
// served on 127.0.0.1 only, under a random secret path, and opened in the system browser. The page talks to the
// launcher with POST <secret>/msg and long-polls GET <secret>/events; everything else stays as with WebView2. The
// classic window is hidden, and the launcher exits when the page has been closed for a while and no game is running.
public partial class LauncherForm
{
    private TcpListener? browserListener;
    private string browserSecret = "";
    private bool browserActive;
    private DateTime browserLastSeen = DateTime.UtcNow;
    private readonly ConcurrentQueue<string> browserOutbox = new();
    private readonly SemaphoreSlim browserSignal = new(0);
    private DateTime viewerStarted;
    private bool browserPageServed;
    private bool keepHidden;

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandleA(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

    // Wine (and Proton) export wine_get_version from ntdll; WebView2 is not reliable there.
    internal static bool RunningOnWine()
    {
        IntPtr ntdll = GetModuleHandleA("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WineHostVersion(out IntPtr sysname, out IntPtr release);

    // Wine on macOS (CrossOver): the host is "Darwin". The Electron viewer there showed only a white window (2.1), while
    // the Mac's own browser shows the launcher page fine, so on a Mac the page opens in the default browser.
    internal static bool RunningOnMac()
    {
        IntPtr ntdll = GetModuleHandleA("ntdll.dll");
        IntPtr fn = ntdll == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(ntdll, "wine_get_host_version");
        if (fn == IntPtr.Zero) return false;
        try
        {
            Marshal.GetDelegateForFunctionPointer<WineHostVersion>(fn)(out IntPtr sysname, out _);
            return Marshal.PtrToStringAnsi(sysname) == "Darwin";
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or AccessViolationException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool PageConnected => glass?.CoreWebView2 != null || browserActive;

    // Every message for the page goes through here, whichever way the page is shown.
    private void SendToPage(string json)
    {
        if (browserActive)
        {
            browserOutbox.Enqueue(json);
            browserSignal.Release();
            return;
        }
        if (InvokeRequired) BeginInvoke(() => glass?.CoreWebView2?.PostWebMessageAsJson(json));
        else glass?.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void StartBrowserUi()
    {
        if (browserActive) return;
        string ui = Path.Combine(AppContext.BaseDirectory, "ui");
        if (!File.Exists(Path.Combine(ui, "launcher.html"))) return;  // no page: the classic window stays
        browserSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        browserListener = new TcpListener(IPAddress.Loopback, 0);
        browserListener.Start();
        browserActive = true;
        browserLastSeen = DateTime.UtcNow;
        int port = ((IPEndPoint)browserListener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptBrowserClients(ui));
        string url = $"http://127.0.0.1:{port}/{browserSecret}/launcher.html";
        BrowserLog($"Launcher {LauncherVersion} ({Environment.ProcessPath}); wine={RunningOnWine()}; mac={RunningOnMac()}; " +
                   $"viewer={(File.Exists(Path.Combine(AppContext.BaseDirectory, "Viewer", "ReStreetViewer.exe")) ? "found" : "missing")}");
        bool appWindow = false;
        try
        {
            // A window of its own, the launcher's size, when a Chromium browser is there; otherwise the default browser.
            appWindow = OpenAppWindow(url);
            if (!appWindow) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            browserActive = false;  // no browser to open: keep the classic window
            browserListener.Stop();
            return;
        }
        // On Wine/Proton without a Chromium app window the default browser may not open at all (inside Steam's
        // Proton container there is no host browser to reach), and a hidden classic window would leave nothing on
        // screen: keep the classic window visible then. The page still works if a browser does show it.
        if (!appWindow && RunningOnWine())
        {
            BrowserLog("No Chromium app window was opened; the classic window stays visible. " +
                       "Set RESTREET_BROWSER to a Chromium-family browser path, or RESTREET_CLASSIC_UI=1 to always use the classic window.");
            Opacity = 1;
            return;
        }
        // Only the new interface is shown: hide the classic window (it keeps launching the game and restoring the display).
        HideClassicWindow();
        var watchdog = new System.Windows.Forms.Timer { Interval = 15000 };
        watchdog.Tick += (_, _) =>
        {
            if (!gameRunning && !updating && DateTime.UtcNow - browserLastSeen > TimeSpan.FromSeconds(90)) Close();
        };
        watchdog.Start();
    }

    // Viewer\ReStreetViewer.exe: a Windows Electron build placed next to the launcher (see Viewer\README.md). It runs in
    // the same Wine prefix as the launcher, so it needs nothing from the Linux side (no host browser, no Flatpak, nothing
    // Steam's Proton container hides) and its window belongs to the same session as the launcher and the game.
    // RESTREET_VIEWER_ARGS: extra command-line switches for the viewer (for example --in-process-gpu), for trying things.
    private bool OpenViewer(string url)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Viewer");
        string exe = Path.Combine(dir, "ReStreetViewer.exe");
        if (!File.Exists(exe))
        {
            BrowserLog("No viewer at " + exe);
            return false;
        }
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
            info.ArgumentList.Add("--no-sandbox");
            info.ArgumentList.Add("--restreet-url=" + url);
            info.ArgumentList.Add($"--restreet-size={Width},{Height}");
            foreach (string extra in (Environment.GetEnvironmentVariable("RESTREET_VIEWER_ARGS") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                info.ArgumentList.Add(extra);
            var process = Process.Start(info);
            if (process == null) return false;
            viewerStarted = DateTime.UtcNow;
            process.EnableRaisingEvents = true;
            FormClosed += (_, _) => KillViewer(process);  // an update or exit must not leave the old window open
            process.Exited += (_, _) =>
            {
                try { BeginInvoke(() => OnViewerExited()); }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { Debug.WriteLine(ex.Message); }
            };
            BrowserLog("Opening the launcher page in the viewer " + exe);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            BrowserLog("The viewer could not start: " + ex.Message);
            return false;
        }
    }

    // The viewer window was closed: leave with it, unless a game or an update is running. If it died before it ever
    // loaded the page (a crash under Wine, say), bring the classic window back instead of leaving nothing on screen.
    private void OnViewerExited()
    {
        if (!browserPageServed && DateTime.UtcNow - viewerStarted < TimeSpan.FromSeconds(30))
        {
            BrowserLog("The viewer closed before the page loaded; the classic window is shown instead.");
            ShowClassicWindow();
            return;
        }
        if (!gameRunning && !updating) Close();
    }

    private static void KillViewer(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex.Message); }
    }

    // The classic window is not used while the page has a window of its own. Opacity 0 alone left a transparent window
    // that Wine still gave clicks to (a menu on the desktop, nothing in the taskbar) and that gamescope could pick as the
    // game's window. On Wine the window is really hidden; elsewhere it stays as before. SetVisibleCore keeps it hidden
    // when something calls Show() later.
    private void HideClassicWindow()
    {
        Opacity = 0;
        ShowInTaskbar = false;
        if (!RunningOnWine()) return;
        keepHidden = true;
        Hide();
    }

    private void ShowClassicWindow()
    {
        keepHidden = false;
        ShowInTaskbar = true;
        Opacity = 1;
        Show();
        Activate();
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(keepHidden ? false : value);

    // Chromium-family browsers open a URL as an app window (no tabs, no address bar) with --app; a profile of its own
    // makes it a new window of the requested size even when the browser is already open.
    private bool OpenAppWindow(string url)
    {
        // The viewer is for Wine/Proton only. On Windows nothing below changes (RESTREET_VIEWER=1 forces it, for testing).
        if ((RunningOnWine() || Environment.GetEnvironmentVariable("RESTREET_VIEWER") == "1") && !RunningOnMac() && OpenViewer(url)) return true;
        string size = $"--window-size={Width},{Height}";
        if (RunningOnMac()) return false;  // no Linux browsers to look for: the Mac's default browser opens the page
        if (RunningOnWine())
        {
            var unix = new List<string>();
            // RESTREET_BROWSER: a Linux path to any Chromium-family browser (or a script that starts one with the
            // arguments it is given), tried first.
            string custom = (Environment.GetEnvironmentVariable("RESTREET_BROWSER") ?? "").Trim();
            if (custom.StartsWith('/')) unix.Add(custom);
            unix.AddRange(new[]
            {
                "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome-stable", "/usr/bin/google-chrome",
                "/usr/bin/brave", "/usr/bin/brave-browser", "/usr/bin/microsoft-edge-stable", "/usr/bin/vivaldi-stable",
            });
            // Flatpak browsers: system-wide installs, and the user's own (~/.local/share/flatpak), which Discover on
            // SteamOS and "flatpak install --user" use.
            string[] flatpakIds = { "org.chromium.Chromium", "com.google.Chrome", "com.brave.Browser", "com.microsoft.Edge" };
            string home = (Environment.GetEnvironmentVariable("HOME") ?? "").TrimEnd('/');
            foreach (string id in flatpakIds) unix.Add("/var/lib/flatpak/exports/bin/" + id);
            if (home.StartsWith('/'))
                foreach (string id in flatpakIds) unix.Add(home + "/.local/share/flatpak/exports/bin/" + id);
            // Wine maps the Linux root to Z:, and start.exe /unix runs a Linux program.
            string? found = unix.FirstOrDefault(p => File.Exists("Z:" + p.Replace('/', '\\')));
            if (found == null)
            {
                BrowserLog("No Chromium-family browser found. Tried: " + string.Join(", ", unix));
                return false;
            }
            BrowserLog("Opening the launcher page with " + found);
            Process.Start(new ProcessStartInfo("start.exe")
            {
                ArgumentList = { "/unix", found, "--app=" + url, size, "--user-data-dir=/tmp/restreet-launcher-ui" },
                UseShellExecute = false, CreateNoWindow = true,
            });
            return true;
        }
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? exe = new[]
        {
            Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf, @"BraveSoftware\Brave-Browser\Applicationrave.exe"),
        }.FirstOrDefault(File.Exists);
        if (exe == null) return false;
        Process.Start(new ProcessStartInfo(exe)
        {
            ArgumentList = { "--app=" + url, size, "--user-data-dir=" + Path.Combine(local, "ReStreet", "LauncherWindow") },
            UseShellExecute = false,
        });
        return true;
    }

    // A short diagnostic file next to the launcher (browser-ui.log), so a failed window can be understood.
    private static void BrowserLog(string line)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "browser-ui.log"), DateTime.Now.ToString("s") + "  " + line + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
    }

    private async Task AcceptBrowserClients(string ui)
    {
        while (browserActive && browserListener != null)
        {
            TcpClient client;
            try { client = await browserListener.AcceptTcpClientAsync(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => ServeBrowserClient(client, ui));
        }
    }

    private async Task ServeBrowserClient(TcpClient client, string ui)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var (method, path, body) = await ReadRequest(stream);
            string prefix = "/" + browserSecret + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) { await Respond(stream, 404, "text/plain", Array.Empty<byte>()); return; }
            string rest = path[prefix.Length..].Split('?')[0];
            browserLastSeen = DateTime.UtcNow;
            if (rest == "launcher.html") browserPageServed = true;
            if (method == "POST" && rest == "msg")
            {
                string json = Encoding.UTF8.GetString(body);
                BeginInvoke(() => OnGlassMessage(json));
                await Respond(stream, 204, "text/plain", Array.Empty<byte>());
            }
            else if (method == "GET" && rest == "events")
            {
                // Long poll: answer at once when something is queued, otherwise after 20 s with an empty list.
                await browserSignal.WaitAsync(TimeSpan.FromSeconds(20));
                var items = new List<string>();
                while (browserOutbox.TryDequeue(out var item)) items.Add(item);
                await Respond(stream, 200, "application/json", Encoding.UTF8.GetBytes("[" + string.Join(",", items) + "]"));
            }
            else if (method == "GET" && rest.Length > 0 && rest.IndexOfAny(new[] { '/', '\\' }) < 0 && !rest.Contains(".."))
            {
                string file = Path.Combine(ui, rest);
                string type = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8", ".jpg" => "image/jpeg", ".png" => "image/png",
                    ".css" => "text/css", ".js" => "text/javascript", ".woff2" => "font/woff2", _ => "application/octet-stream",
                };
                if (File.Exists(file)) await Respond(stream, 200, type, await File.ReadAllBytesAsync(file));
                else await Respond(stream, 404, "text/plain", Array.Empty<byte>());
            }
            else await Respond(stream, 404, "text/plain", Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static async Task<(string Method, string Path, byte[] Body)> ReadRequest(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 16384 && await stream.ReadAsync(one) == 1)
        {
            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
        }
        string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
        string[] first = lines[0].Split(' ');
        if (first.Length < 2) throw new IOException("bad request");
        int length = 0;
        foreach (string line in lines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line[15..].Trim(), out length);
        length = Math.Clamp(length, 0, 65536);
        var body = new byte[length];
        for (int read = 0; read < length;)
        {
            int got = await stream.ReadAsync(body.AsMemory(read));
            if (got == 0) break;
            read += got;
        }
        return (first[0], first[1], body);
    }

    private static async Task Respond(NetworkStream stream, int status, string type, byte[] body)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", _ => "Not Found" };
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                        "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        if (body.Length > 0) await stream.WriteAsync(body);
    }
}
