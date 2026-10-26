using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ReStreet.Ui;

namespace FifaStreetLauncher;

// Liquid glass interface (ui/launcher.html in a WebView2) on top of the classic launcher form. The classic controls stay
// alive underneath and keep doing all the work (settings, PLAY, display restore, online, Discord); the page only reads
// them (state messages) and changes them (set / toggle / command messages). Without the WebView2 runtime the classic
// interface simply stays visible.
public partial class LauncherForm
{
    // Regions the online patch offers (keep in sync with build_online_patch.py --region).
    private static readonly (string Name, string Address)[] Regions =
    {
        ("Europe", "57.129.180.15"), ("South America", "216.238.113.7"),
    };

    private WebView2? glass;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private bool glassFrame;

    protected override void WndProc(ref Message m)
    {
        // WM_NCCALCSIZE: the client area also takes the caption; the side and bottom sizing borders stay (they are
        // invisible on Windows 10/11). Maximized, the frame hangs off the screen, so the top keeps one frame width.
        if (glassFrame && m.Msg == 0x83 && m.WParam != IntPtr.Zero)
        {
            int top = Marshal.ReadInt32(m.LParam, 4);
            base.WndProc(ref m);
            uint dpi = (uint)DeviceDpi;
            int frame = WindowState == FormWindowState.Maximized
                ? GetSystemMetricsForDpi(33 /* SM_CYSIZEFRAME */, dpi) + GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, dpi) : 0;
            Marshal.WriteInt32(m.LParam, 4, top + frame);
            return;
        }
        base.WndProc(ref m);
    }
    private readonly System.Windows.Forms.Timer glassTimer = new() { Interval = 1000 };
    private readonly Dictionary<string, (int Ping, int Online)> serverInfo = new();
    private string lastState = "";

    private Dictionary<string, ComboBox> GlassFields() => new()
    {
        ["resolution"] = resolutionBox, ["refresh"] = refreshBox, ["displayMode"] = displayModeBox, ["monitor"] = monitorBox,
        ["language"] = gameLanguageBox, ["profile"] = profileBox, ["api"] = graphicsApiBox, ["gpu"] = gpuBox,
        ["post"] = postEffectBox, ["internal"] = internalResolutionBox, ["readback"] = readbackResolveBox,
        ["scaling"] = scalingBox, ["aspect"] = aspectBox, ["fpsLimit"] = fpsLimitBox, ["aniso"] = anisoBox, ["occlusion"] = occlusionBox,
    };

    private Dictionary<string, CheckBox> GlassToggles() => new()
    {
        ["vsync"] = vsyncBox, ["vrr"] = vrrBox, ["msaa"] = msaaBox, ["memexport"] = readbackMemexportBox,
        ["memexportFast"] = readbackMemexportFastBox, ["pageState"] = clearMemoryPageStateBox, ["occlusion"] = occlusionQueryBox,
        ["asyncShaders"] = asyncShadersBox, ["frameStats"] = logFrameStatsBox, ["onlineMode"] = onlineModeBox,
    };

    // Online on/off (page toggle, saved in settings.json): off starts the game without the online patch, so it plays
    // offline, and the launcher stops its social, presence and room calls.
    private readonly CheckBox onlineModeBox = new() { Checked = true };

    // Shows the window: when the new page has loaded, or when the classic interface is the one that stays.
    private void RevealWindow()
    {
        if (!browserActive && !viewerDownloading) Opacity = 1;
    }

    private async void InitGlassUi()
    {
        // Never leave the window invisible: whatever happens below, show it after a few seconds.
        var safety = new System.Windows.Forms.Timer { Interval = 6000 };
        safety.Tick += (_, _) => { safety.Stop(); RevealWindow(); };
        safety.Start();
        if (Environment.GetEnvironmentVariable("RESTREET_CLASSIC_UI") == "1") return;
        // Wine/Proton: WebView2 is not reliable there, so the same page opens in the system browser.
        if (RunningOnWine() || Environment.GetEnvironmentVariable("RESTREET_BROWSER_UI") == "1")
        {
            if (RunningOnWine() && !RunningOnMac()) await EnsureViewerAsync();  // first run on Proton: fetch the viewer once
            StartBrowserUi();
            if (browserActive) StartPageServices();
            RevealWindow();
            return;
        }
        try
        {
            string ui = Path.Combine(AppContext.BaseDirectory, "ui");
            if (!File.Exists(Path.Combine(ui, "launcher.html"))) { RevealWindow(); return; }
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReStreet", "WebView2");
            // A minimized launcher must keep polling at full rate: Chromium otherwise slows the page's timers to once a
            // minute, and a host waiting in a room started the game a minute late (the guest gave up meanwhile).
            var options = new CoreWebView2EnvironmentOptions(
                "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
            var env = await CoreWebView2Environment.CreateAsync(null, data, options);
            var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 12, 18) };
            Controls.Add(view);
            view.BringToFront();
            await view.EnsureCoreWebView2Async(env);
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("restreet.ui", ui, CoreWebView2HostResourceAccessKind.DenyCors);
            // Stadium pictures and team crests for the pickers: made from the player's own game files when the launcher opens
            // (and again on PLAY if that run did not finish), so a fresh install or update shows them within seconds.
            try
            {
                string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePathBox.Text.Trim()))!;
                string thumbs = Path.Combine(exeDir, "stadium-thumbs");
                Directory.CreateDirectory(thumbs);
                if (Directory.Exists(gamePathBox.Text.Trim())) EnsureStadiumThumbs(exeDir, gamePathBox.Text.Trim());
                core.SetVirtualHostNameToFolderMapping("restreet.thumbs", thumbs, CoreWebView2HostResourceAccessKind.Allow);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Debug.WriteLine(ex.Message);  // no pictures: the picker shows names only
            }
            // The page may only talk to the launcher; it never navigates anywhere else.
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://restreet.ui/", StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += (_, e) => OnGlassMessage(e.WebMessageAsJson);
            core.NavigationCompleted += (_, _) => RevealWindow();
            core.Navigate("https://restreet.ui/launcher.html");
            glass = view;
            // The page draws its own title bar: a sizable frame without the Windows caption (WM_NCCALCSIZE in WndProc),
            // so the window resizes from its sides and bottom, snaps and maximizes, with rounded corners and the shadow.
            // The scaler already gave the window its first size; from now on the user sizes it.
            scaler.Enabled = false;
            MinimumSize = MaximumSize = Size.Empty;
            glassFrame = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(Math.Min(Width, LogicalToDeviceUnits(1000)), Math.Min(Height, LogicalToDeviceUnits(620)));
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27 /* NOSIZE|NOMOVE|NOZORDER|FRAMECHANGED */);
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
            StartPageServices();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or IOException or UnauthorizedAccessException)
        {
            // No WebView2 runtime (or no writable profile folder): keep the classic interface.
            if (glass != null) { Controls.Remove(glass); glass.Dispose(); glass = null; }
            StartBrowserUi();
            if (browserActive) StartPageServices();
            RevealWindow();
        }
    }

    private bool pageServicesStarted;
    private void StartPageServices()
    {
        if (pageServicesStarted) return;
        pageServicesStarted = true;
        glassTimer.Tick += (_, _) => PushGlassState(false);
        glassTimer.Start();
        _ = Task.Run(PollServers);
    }

    private void OnGlassMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        string cmd = m.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        string id = m.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
        switch (cmd)
        {
            case "set":
                if (GlassFields().TryGetValue(id, out var box) && box.Enabled && m.TryGetProperty("index", out var ix))
                {
                    int index = ix.GetInt32();
                    if (index >= 0 && index < box.Items.Count) box.SelectedIndex = index;
                }
                break;
            case "toggle":
                if (GlassToggles().TryGetValue(id, out var t) && t.Enabled && m.TryGetProperty("value", out var v))
                    t.Checked = v.GetBoolean();
                break;
            case "play": if (!gameRunning) LaunchGame(); break;
            case "drag" when browserActive: return;
            case "minimize" when browserActive: return;
            case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero); return;
            case "minimize": WindowState = FormWindowState.Minimized; return;
            case "maximize" when browserActive: return;
            case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return;
            case "close": Close(); return;
            case "browseExe": BrowseExe(); break;
            case "browseData": BrowseGame(); break;
            case "logs": OpenLogs(); break;
            case "folder": OpenGameFolder(); break;
            // The page cannot navigate away; the Donate button opens the fixed Ko-fi page in the default browser.
            case "donate": Process.Start(new ProcessStartInfo("https://ko-fi.com/samuelitodavila") { UseShellExecute = true })?.Dispose(); return;
            case "shortcut": CreateDesktopShortcut(); break;
            case "state": if (pendingUpdate is { } pu) PostToPage(new { type = "update", version = pu.Version, notes = pu.Notes }); PushPerfHistory(); break;
            case "perfHistory": PushPerfHistory(); return;
            case "saves": PushSaves(); return;
            case "savesRestore": RestoreSaveFromPage(m.TryGetProperty("id", out var sid) ? sid.GetString() : null); return;
            case "updateGo": _ = InstallUpdateAsync(); return;
            case "perfShare": if (m.TryGetProperty("value", out var ps)) SetPerfSharing(ps.GetBoolean()); return;
            case "controls": if (m.TryGetProperty("value", out var cv) && cv.ValueKind == JsonValueKind.Object) SetControls(cv); break;
            case "lobby":
            {
                var online = LoadOnlineConfig();
                if (online != null) { using var lobby = new LobbyForm(online.Value.Server, online.Value.Name); lobby.ShowDialog(this); }
                break;
            }
            case "friends":
            {
                var online = LoadOnlineConfig();
                if (online != null) { using var friends = new FriendsForm(online.Value.Server, online.Value.Name, online.Value.Key); friends.ShowDialog(this); }
                break;
            }
            case "server":
                if (m.TryGetProperty("address", out var a)) SwitchServer(a.GetString() ?? "");
                break;
            // Advanced > Online key (PlayerKey.cs): the recovery code, and a code from another PC.
            case "keyCode": PostToPage(new { type = "key", code = PlayerKey.RecoveryCode(OnlineFiles()) }); return;
            case "keyUse":
            {
                var result = gameRunning
                    ? new PlayerKey.Result(false, "Close the game first.")
                    : PlayerKey.UseCode(OnlineFiles(), m.TryGetProperty("code", out var kc) ? kc.GetString() : null);
                PostToPage(new { type = "key", code = PlayerKey.RecoveryCode(OnlineFiles()), ok = result.Ok, note = result.Note });
                if (result.Ok) UpdateStatus(ReadyText(), true);
                break;
            }
            case "social":
            case "chatSend":
            case "chatAction":
            case "friend":
            case "room":
                _ = SocialAsync(cmd, m.Clone());
                return;
            case "comp":
                _ = CompAsync(m.Clone());
                return;
        }
        if (cmd is "set" or "toggle") SaveDeckToml();
        PushGlassState(true);
    }

    // online.json beside this launcher (the one it reads) and the copy beside the game executable (Binaries\, or
    // Game\ in installs before 1.5.5).
    internal string[] OnlineFiles()
    {
        string exeFolder;
        try { exeFolder = Path.GetDirectoryName(Path.GetFullPath(exePathBox.Text.Trim())) ?? AppContext.BaseDirectory; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { exeFolder = AppContext.BaseDirectory; }
        return new[] { Path.Combine(AppContext.BaseDirectory, "online.json"), Path.Combine(exeFolder, "online.json") };
    }

    // Moves this install to another region: the same name and key, another server (online.json in both places).
    private void SwitchServer(string address)
    {
        if (gameRunning || !Regions.Any(r => r.Address == address)) return;
        foreach (string path in OnlineFiles())
        {
            try
            {
                if (!File.Exists(path)) continue;
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                node["server"] = address;
                File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                UpdateStatus("Could not change the server: " + ex.Message, false);
            }
        }
    }

    private async Task PollServers()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        while (!IsDisposed)
        {
            foreach (var (_, address) in Regions)
            {
                int ping = -1, online = -1;
                try
                {
                    var watch = Stopwatch.StartNew();
                    using (var tcp = new TcpClient()) { await tcp.ConnectAsync(address, 29812).WaitAsync(TimeSpan.FromSeconds(3)); }
                    ping = (int)watch.ElapsedMilliseconds;
                    using var doc = JsonDocument.Parse(await http.GetStringAsync($"http://{address}:29812/v1/online"));
                    online = doc.RootElement.GetProperty("online").GetInt32();
                }
                catch (Exception) { /* offline or unreachable: shown as offline */ }
                lock (serverInfo) serverInfo[address] = (ping, online);
            }
            await Task.Delay(15000);
        }
    }

    private void PushGlassState(bool force)
    {
        if (!PageConnected) return;
        var fields = GlassFields().ToDictionary(f => f.Key, f => (object)new
        {
            items = f.Value.Items.Cast<object>().Select(x => x?.ToString() ?? "").ToArray(),
            index = f.Value.SelectedIndex,
            enabled = f.Value.Enabled,
        });
        var toggles = GlassToggles().ToDictionary(t => t.Key, t => (object)new { on = t.Value.Checked, enabled = t.Value.Enabled });
        toggles["perfShare"] = new { on = perfShareCached, enabled = true };
        var online = LoadOnlineConfig();
        object? onlineState = null;
        if (online != null)
        {
            Dictionary<string, (int Ping, int Online)> info;
            lock (serverInfo) info = new(serverInfo);
            onlineState = new
            {
                name = online.Value.Name,
                server = online.Value.Server,
                status = gameRunning ? "Playing" : onlineModeBox.Checked ? "Ready to play online" : "Online is off",
                servers = Regions.Select(r => new
                {
                    name = r.Name, address = r.Address,
                    ping = info.TryGetValue(r.Address, out var s) ? s.Ping : -1,
                    online = info.TryGetValue(r.Address, out var s2) ? s2.Online : -1,
                }).ToArray(),
            };
        }
        string json = JsonSerializer.Serialize(new
        {
            fields, toggles, online = onlineState, controls = ControlsState(),
            exe = exePathBox.Text, data = gamePathBox.Text,
            summary = hardwareLabel.Text, status = statusLabel.Text, statusOk = statusLabel.ForeColor != Color.FromArgb(255, 120, 120),
            running = gameRunning, deck = deckBase,
            api = graphicsApiBox.SelectedItem?.ToString(), profile = profileBox.SelectedItem?.ToString(),
            version = "V" + LauncherVersion + " · ONLINE BETA",
        });
        if (!force && json == lastState) return;
        lastState = json;
        SendToPage(json);
    }

    // ---------- Social (players, friends, chat): HTTP to the player's own server with their name and key ----------
    // The User-Agent tells the server which launcher version a player uses (its personal-key log).
    private static readonly HttpClient SocialHttp = new() { Timeout = TimeSpan.FromSeconds(6), DefaultRequestHeaders = { { "User-Agent", "ReStreetLauncher/" + LauncherVersion } } };
    private DateTime lastBackgroundSocial = DateTime.MinValue;

    // One retry: a single dropped connection (a busy moment on the server, Wi-Fi) must not look like an outage.
    private static async Task<string> SendWithRetry(Func<Task<string>> send)
    {
        try { return await send(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Task.Delay(400);
            return await send();
        }
    }

    private static async Task<JsonDocument?> PostJson(string server, string path, object body)
    {
        string json = JsonSerializer.Serialize(body);
        string text = await SendWithRetry(async () =>
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await SocialHttp.PostAsync($"http://{server}:29812{path}", content);
            return await response.Content.ReadAsStringAsync();
        });
        return text.StartsWith("{") ? JsonDocument.Parse(text) : null;
    }

    private static async Task<JsonDocument> GetJson(string server, string path) =>
        JsonDocument.Parse(await SendWithRetry(() => SocialHttp.GetStringAsync($"http://{server}:29812{path}")));

    private int socialFailures;

    private int socialPollBusy;
    private volatile bool inRoom;

    // A background poll is skipped while the previous one still runs: on a slow link they piled up every 3 s.
    private async Task SocialAsync(string cmd, JsonElement m)
    {
        if (cmd != "social") { await SocialCoreAsync(cmd, m); return; }
        if (Interlocked.Exchange(ref socialPollBusy, 1) == 1) return;
        try { await SocialCoreAsync(cmd, m); }
        finally { Volatile.Write(ref socialPollBusy, 0); }
    }

    private async Task SocialCoreAsync(string cmd, JsonElement m)
    {
        var online = LoadOnlineConfig();
        if (online == null || !onlineModeBox.Checked || !PageConnected) return;
        var (server, name, key) = online.Value;
        string Str(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string channel = Str("channel");
        string? error = null;
        long passOk = 0;  // the room whose password the server accepted (action 'check')
        try
        {
            if (cmd == "social" && channel == "")
            {
                // Social page closed: refresh the badge only every 15 s (every poll while in a room, to start the game on time).
                if (!inRoom && DateTime.UtcNow - lastBackgroundSocial < TimeSpan.FromSeconds(15)) return;
                lastBackgroundSocial = DateTime.UtcNow;
            }
            if (cmd == "chatSend")
            {
                using var r = await PostJson(server, "/v1/textchat", new { me = name, key, action = "send", channel, text = Str("text") });
                if (r != null && r.RootElement.TryGetProperty("error", out var e)) error = ChatError(e.GetString());
            }
            else if (cmd == "chatAction")
            {
                long id = m.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.Number ? idv.GetInt64() : 0;
                using var r = await PostJson(server, "/v1/textchat", new { me = name, key, action = Str("action"), other = Str("other"), id });
                if (Str("action") == "report") error = "Thanks, the message was reported";
            }
            else if (cmd == "friend")
            {
                using var r = await PostJson(server, "/v1/friends", new { me = name, key, action = Str("action"), other = Str("other") });
                if (r != null && r.RootElement.TryGetProperty("result", out var fr) && fr.ValueKind == JsonValueKind.String)
                    error = fr.GetString() switch { "unknown" => $"No player called {Str("other")}", "self" => "You can't add yourself", _ => null };
            }
            else if (cmd == "room")
            {
                // Game rooms (rooms.py): when a room is full the game takes both players to the match set-up by itself.
                string action = Str("action");
                long roomId = m.TryGetProperty("room", out var rv) && rv.ValueKind == JsonValueKind.Number ? rv.GetInt64() : 0;
                int stadium = m.TryGetProperty("stadium", out var sv2) && sv2.ValueKind == JsonValueKind.Number ? sv2.GetInt32() : 1047;
                // The team picked for the match (game team id, 0 = own World Tour team); the server checks it.
                int team = m.TryGetProperty("team", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetInt32() : 0;
                // Private rooms: the host sets a password, a joiner types it (an invited friend needs none).
                string password = Str("password").Trim();
                using var r = action switch
                {
                    "create" => await PostJson(server, "/v1/rooms", new { me = name, key, action, name = Str("title").Trim().Length > 0 ? Str("title").Trim() : name + "'s game", mode = Str("mode"), stadium, team, password }),
                    "join" => await PostJson(server, "/v1/rooms", new { me = name, key, action, room = roomId, team, password }),
                    // The password is checked before the team picker; a server without 'check' answers with the list, so it passes.
                    "check" => await PostJson(server, "/v1/rooms", new { me = name, key, action, room = roomId, password }),
                    // A friend invited to this player's room joins it without the password.
                    "invite" => await PostJson(server, "/v1/rooms", new { me = name, key, action, target = Str("other") }),
                    "decline" => await PostJson(server, "/v1/rooms", new { me = name, key, action, room = roomId }),
                    _ => await PostJson(server, "/v1/rooms", new { me = name, key, action = "leave" }),
                };
                if (r != null && r.RootElement.TryGetProperty("error", out var re)) error = RoomError(re.GetString());
                else if (action == "check") { if (r != null) passOk = roomId; else error = "Server not reachable"; }
                else if (action == "create" && password.Length == 0 && r != null && r.RootElement.TryGetProperty("room", out var made))
                {
                    // Tell the server channel, so anyone can join from the Open games list.
                    string text = $"🎮 I opened a {RoomModeName(Str("mode"))} game at {made.GetProperty("stadium_name").GetString()}. Join it from the Matches tab!";
                    using var _ = await PostJson(server, "/v1/textchat", new { me = name, key, action = "send", channel = "general", text });
                }
            }

            // Fresh snapshot for the page. All requests at once: one after the other they took seconds on a far server.
            var friendsTask = PostJson(server, "/v1/friends", new { me = name, key, action = "list" });
            var playersTask = GetJson(server, "/v1/online");
            var roomsTask = PostJson(server, "/v1/rooms", new { me = name, key, action = "list" });
            var myRoomTask = PostJson(server, "/v1/rooms", new { me = name, key, action = "current" });
            var invitesTask = PostJson(server, "/v1/rooms", new { me = name, key, action = "invites" });
            var convosTask = PostJson(server, "/v1/textchat", new { me = name, key, action = "conversations" });
            var msgsTask = channel == "" ? Task.FromResult<JsonDocument?>(null)
                : PostJson(server, "/v1/textchat", new { me = name, key, action = "read", channel, since = m.TryGetProperty("since", out var s2) && s2.ValueKind == JsonValueKind.Number ? s2.GetInt64() : 0 });
            JsonDocument? convos = null, msgs = null;
            bool chatOff = false;
            try
            {
                convos = await convosTask;
                msgs = await msgsTask;
                chatOff = convos == null && msgs == null;  // either answer means the chat is there
            }
            catch (HttpRequestException) { chatOff = true; }
            using var friends = await friendsTask;
            using var players = await playersTask;
            using var rooms = await roomsTask;
            using var myRoom = await myRoomTask;
            using var invites = await invitesTask;
            var waited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (players.RootElement.TryGetProperty("searching_list", out var sl) && sl.ValueKind == JsonValueKind.Array)
                foreach (var it in sl.EnumerateArray())
                    if (it.TryGetProperty("name", out var wn) && it.TryGetProperty("waited_s", out var ws) && ws.ValueKind == JsonValueKind.Number)
                        waited[wn.GetString() ?? ""] = ws.GetInt32();
            string regionName = Regions.FirstOrDefault(r => r.Address == server).Name ?? server;
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "social",
                ["me"] = name,
                ["server"] = regionName,
                ["channel"] = channel,
                ["error"] = error,
                ["passOk"] = passOk,
                ["chatOff"] = chatOff,
                ["players"] = players.RootElement.TryGetProperty("players", out var pl)
                    ? pl.EnumerateArray().Select(p => new
                    {
                        name = p.GetProperty("name").GetString(),
                        state = p.GetProperty("state").GetString(),
                        note = p.GetProperty("state").GetString() switch
                        {
                            "in_game" => "Playing " + (p.TryGetProperty("mode", out var md) ? md.GetString() : ""),
                            "searching" => "Searching · " + (p.TryGetProperty("mode", out var md2) ? md2.GetString() : "") +
                                (waited.TryGetValue(p.GetProperty("name").GetString() ?? "", out int w) ? $" · {w / 60}:{w % 60:00}" : ""),
                            _ => "In the menus",
                        },
                    }).ToArray()
                    : Array.Empty<object>(),
            };
            if (players.RootElement.TryGetProperty("searching_modes", out var smodes))
                payload["searchingModes"] = JsonSerializer.Deserialize<object>(smodes.GetRawText());
            if (rooms != null && rooms.RootElement.TryGetProperty("rooms", out var rl))
                payload["rooms"] = JsonSerializer.Deserialize<object>(rl.GetRawText());
            if (myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr))
                payload["myRoom"] = JsonSerializer.Deserialize<object>(mr.GetRawText());
            if (invites != null && invites.RootElement.TryGetProperty("invites", out var il))
                payload["invites"] = JsonSerializer.Deserialize<object>(il.GetRawText());
            inRoom = myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr3) && mr3.ValueKind == JsonValueKind.Object;
            OnRoomState(myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr2) ? mr2.Clone() : null);
            if (friends != null)
            {
                var f = friends.RootElement;
                payload["friends"] = f.TryGetProperty("friends", out var fl) ? JsonSerializer.Deserialize<object>(fl.GetRawText()) : null;
                payload["requests"] = f.TryGetProperty("requests", out var rq) ? JsonSerializer.Deserialize<object>(rq.GetRawText()) : null;
                payload["pending"] = f.TryGetProperty("pending", out var pd) ? JsonSerializer.Deserialize<object>(pd.GetRawText()) : null;
            }
            if (convos != null && convos.RootElement.TryGetProperty("conversations", out var cv))
                payload["convos"] = JsonSerializer.Deserialize<object>(cv.GetRawText());
            if (msgs != null && msgs.RootElement.TryGetProperty("messages", out var ms))
                payload["messages"] = JsonSerializer.Deserialize<object>(ms.GetRawText());
            convos?.Dispose(); msgs?.Dispose();
            socialFailures = 0;
            string json = JsonSerializer.Serialize(payload);
            SendToPage(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Background refreshes keep the last data; only say so after three failures in a row (or when the
            // player's own action failed).
            if (cmd == "social" && ++socialFailures < 3) return;
            string json = JsonSerializer.Serialize(new { type = "social", error = "Server not reachable" });
            SendToPage(json);
        }
    }

    // Competitive page (server competitive.py): the page's request goes to /v1/competitive as it is, signed with this
    // player's name and key; the answer comes back as {type: 'comp', action, data, error}.
    private async Task CompAsync(JsonElement m)
    {
        var online = LoadOnlineConfig();
        if (online == null || !onlineModeBox.Checked || !PageConnected) return;
        var (server, name, key) = online.Value;
        string action = m.TryGetProperty("action", out var av) && av.ValueKind == JsonValueKind.String ? av.GetString() ?? "me" : "me";
        var body = new Dictionary<string, object?> { ["me"] = name, ["key"] = key, ["action"] = action };
        foreach (var field in new[] { "mode", "team", "id" })
            if (m.TryGetProperty(field, out var v)) body[field] = JsonSerializer.Deserialize<object>(v.GetRawText());
        object? data = null;
        string? error = null;
        try
        {
            using var r = await PostJson(server, "/v1/competitive", body);
            if (r == null) error = "Server not reachable";
            else if (r.RootElement.TryGetProperty("error", out var e)) error = CompError(e.GetString());
            else data = JsonSerializer.Deserialize<object>(r.RootElement.GetRawText());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Background polls stay quiet: a player's own action says the server is not reachable.
            if (action is "me" or "table" or "tournaments") return;
            error = "Server not reachable";
        }
        SendToPage(JsonSerializer.Serialize(new { type = "comp", action, data, error }));
    }

    private static string CompError(string? code) => code switch
    {
        "unknown_mode" => "Pick a mode first",
        "unknown_team" => "The server does not know that team yet",
        "no_such_tournament" => "That tournament is over",
        "tournament_started" => "The tournament has already started",
        "tournament_full" => "The tournament is full",
        "not_signed_up" => "Sign up first",
        "checkin_not_open" => "Check-in opens 10 minutes before the start",
        "real_team_required" => "Tournaments are played with real teams: pick a club or a national team",
        "already_in_match" => "You already have a match waiting: play it first",
        "unknown_action" or "route_not_found" => "This server does not have competitive play yet",
        _ => "Could not do that: " + code,
    };

    private static string RoomModeName(string mode) => mode switch
    {
        "fives" => "5-a-side", "sixes" => "6-a-side", "futsal" => "Futsal", _ => mode,
    };

    private static string RoomError(string? code) => code switch
    {
        "full" => "That game is already full",
        "already_in_room" => "Leave your current game first",
        "too_fast" => "Wait a few seconds before opening another game",
        "no_such_room" or "unknown_room" => "That game is closed",
        "unknown_team" => "The server does not know that team yet",
        "wrong_password" => "Wrong password for that game",
        "not_friends" => "You can only invite your friends",
        "not_in_room" => "Post a match first, then invite",
        _ => "Could not do that: " + code,
    };

    private static string ChatError(string? code) => code switch
    {
        "too_fast" => "Slow down a little",
        "no_links" => "Links are not allowed in chat",
        "too_long" => "Message too long (300 characters max)",
        "muted" => "You are muted for a while",
        "banned" => "You can't use the chat",
        "not_friends" => "You can only message friends",
        _ => "Message not sent",
    };
}
