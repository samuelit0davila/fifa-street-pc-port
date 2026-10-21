using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FifaStreetLauncher.Linux;

// The launcher state the page shows and changes: the same fields, toggles and commands as the Windows launcher
// (GlassUi.cs), kept as plain lists instead of WinForms controls. Settings live in linux-settings.json; the graphics
// options also go to fifastreet.toml, which the in-game menu (F5 / L3+R3) shares.
internal sealed partial class Launcher
{
    private sealed class Field
    {
        public string[] Items = Array.Empty<string>();
        public int Index;
        public bool Enabled = true;
        public string Text => Index >= 0 && Index < Items.Length ? Items[Index] : "";
    }

    private sealed class Toggle
    {
        public bool On;
        public bool Enabled = true;
    }

    // Regions the online patch offers (keep in sync with GlassUi.cs).
    private static readonly (string Name, string Address)[] Regions =
    {
        ("Europe", "57.129.180.15"), ("South America", "216.238.113.7"),
    };

    private static readonly string[] ProfileNames = { "Balanced (default)", "Quality (strong PCs)", "Performance (weak PCs)", "Custom" };
    private static readonly string[] ReadbackResolveModes = { "full", "some", "fast", "none" };
    private const int PtBrLanguageId = 1001;
    private static readonly int[] GameLanguageIds = { 1, 5, 4, 3, 6, PtBrLanguageId };

    // (label, present_effect, modo_fsr): as DeckLaunch.cs.
    private static readonly (string Label, string Effect, string Mode)[] DeckScaling =
    {
        ("Bilinear", "bilinear", ""), ("FSR 1", "fsr", ""), ("FSR 1 Quality", "fsr", "calidad"),
        ("FSR 1 Balanced", "fsr", "equilibrado"), ("FSR 1 Performance", "fsr", "rendimiento"),
        ("FSR 1 Ultra Performance", "fsr", "ultra_rendimiento"),
    };
    private static readonly (string Label, double Hz)[] DeckFpsLimits =
    {
        ("Screen refresh rate", -1), ("No limit", 0), ("30", 30), ("60", 60), ("90", 90), ("120", 120), ("144", 144),
        ("165", 165), ("240", 240),
    };
    private static readonly (string Label, int Value)[] DeckAniso =
    {
        ("Game default", -1), ("Off", 0), ("1x", 1), ("2x", 2), ("4x", 3), ("8x", 4), ("16x", 5),
    };
    private static readonly (string Label, string Value)[] DeckOcclusion = { ("Off", ""), ("SSAO", "ssao"), ("HBAO", "hbao"), ("GTAO", "gtao") };

    private readonly object sync = new();
    private readonly string root;
    private readonly string binaries;
    private readonly string gameRoot;
    private readonly bool deck;
    private readonly Dictionary<string, Field> fields = new();
    private readonly Dictionary<string, Toggle> toggles = new();
    private readonly List<string> protons;
    private string prefix;
    private bool perfShare;
    private string status = "";
    private bool statusOk = true;
    private volatile bool gameRunning;
    private Process? gameProcess;
    private string lastState = "";
    private volatile bool closing;

    public PageServer? Page { get; set; }
    public Action? CloseWindow { get; set; }

    private string SettingsPath => Path.Combine(root, "linux-settings.json");
    private string TomlPath => Path.Combine(binaries, "fifastreet.toml");

    public Launcher(string root)
    {
        this.root = root;
        binaries = Path.Combine(root, "Binaries");
        // 1.5.5 layout: Binaries/ and Game/; older installs had Game/ + GameData/.
        gameRoot = Path.Combine(root, Directory.Exists(Path.Combine(root, "GameData")) ? "GameData" : "Game");
        deck = File.Exists(Path.Combine(binaries, "ajustes-del-port.txt"));
        protons = Proton.Installed();
        prefix = Proton.DefaultPrefix(root);

        var (sizes, rates, monitors) = Displays.Detect();
        Add("profile", ProfileNames, 0);
        Add("language", new[] { "English", "Español", "Français", "Deutsch", "Italiano", "Português (Brasil)" }, 0);
        Add("resolution", sizes, 0);
        Add("refresh", rates, 0);
        Add("displayMode", new[] { "Fullscreen", "Windowed" }, 0);
        Add("monitor", monitors, 0);
        Add("api", new[] { "Direct3D 12", "Vulkan" }, 0);
        // Proton picks the GPU (DXVK_FILTER_DEVICE_NAME / MESA_VK_DEVICE_SELECT can steer it); the list shows that.
        Add("gpu", new[] { "Automatic" }, 0).Enabled = false;
        Add("post", new[] { "None", "FXAA", "FXAA Extreme" }, 1);
        Add("internal", new[] { "1x - Native (1280 x 720)", "2x - 2560 x 1440", "3x - 3840 x 2160", "4x - 5120 x 2880" }, 0);
        Add("readback", new[] { "Full (compatible)", "Some (recommended)", "Fast (more FPS, may glitch)", "None (experimental)" }, 1);
        Add("scaling", deck ? DeckScaling.Select(s => s.Label).ToArray() : Array.Empty<string>(), 1);
        Add("aspect", deck ? new[] { "Match screen", "16:9 (console)" } : Array.Empty<string>(), 0);
        Add("fpsLimit", deck ? DeckFpsLimits.Select(s => s.Label).ToArray() : Array.Empty<string>(), 0);
        Add("aniso", deck ? DeckAniso.Select(s => s.Label).ToArray() : Array.Empty<string>(), 0);
        Add("occlusion", deck ? DeckOcclusion.Select(s => s.Label).ToArray() : Array.Empty<string>(), 0);
        foreach (var (id, on) in new[] { ("vsync", true), ("vrr", false), ("msaa", false), ("memexport", true), ("memexportFast", true),
                     ("pageState", false), ("occlusion", false), ("asyncShaders", false), ("frameStats", false), ("onlineMode", true) })
            toggles[id] = new Toggle { On = on };

        LoadSettings();
        if (deck) LoadDeckToml();
        UpdateEnabled();
        status = ReadyText();
    }

    private Field Add(string id, string[] items, int index)
    {
        var f = new Field { Items = items, Index = items.Length == 0 ? -1 : Math.Min(index, items.Length - 1), Enabled = items.Length > 0 };
        fields[id] = f;
        return f;
    }

    // ---------- Settings ----------

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var node = JsonNode.Parse(File.ReadAllText(SettingsPath));
            if (node?["fields"] is JsonObject fo)
                foreach (var (id, value) in fo)
                    if (fields.TryGetValue(id, out var f) && Array.IndexOf(f.Items, value?.GetValue<string>()) is int i and >= 0) f.Index = i;
            if (node?["toggles"] is JsonObject to)
                foreach (var (id, value) in to)
                    if (toggles.TryGetValue(id, out var t) && value != null) t.On = value.GetValue<bool>();
            if (node?["prefix"]?.GetValue<string>() is { Length: > 0 } p) prefix = p;
            if (node?["proton"]?.GetValue<string>() is { Length: > 0 } pr && Directory.Exists(pr))
            {
                protons.Remove(pr);
                protons.Insert(0, pr);
            }
            perfShare = node?["perfShare"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void SaveSettings()
    {
        var node = new JsonObject
        {
            ["fields"] = new JsonObject(fields.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value.Text))),
            ["toggles"] = new JsonObject(toggles.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)t.Value.On))),
            ["prefix"] = prefix,
            ["proton"] = protons.FirstOrDefault() ?? "",
            ["perfShare"] = perfShare,
        };
        try { File.WriteAllText(SettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
    }

    // Same rules as the Windows launcher: VRR only with D3D12 and VSync off; the fast memexport needs memexport.
    private void UpdateEnabled()
    {
        bool d3d12 = fields["api"].Index == 0;
        toggles["vrr"].Enabled = d3d12 && !toggles["vsync"].On && !deck;
        toggles["memexportFast"].Enabled = toggles["memexport"].On;
        foreach (string id in new[] { "readback" }) fields[id].Enabled = !deck;
        foreach (string id in new[] { "memexport", "memexportFast", "pageState", "occlusion", "asyncShaders" }) if (deck) toggles[id].Enabled = false;
    }

    private void ApplyProfile()
    {
        (int post, int scale, bool msaa, int readback, bool occlusion)? values = fields["profile"].Index switch
        {
            0 => (1, 0, false, 1, false),   // Balanced: the shipped defaults (Some readback)
            1 => (2, 1, true, 1, false),    // Quality: 2x internal resolution, FXAA extreme, MSAA (Some readback)
            2 => (0, 0, false, 2, false),   // Performance: native resolution, no post, faster readback
            _ => null
        };
        if (values == null) return;
        fields["post"].Index = values.Value.post;
        fields["internal"].Index = values.Value.scale;
        toggles["msaa"].On = values.Value.msaa;
        fields["readback"].Index = values.Value.readback;
        toggles["occlusion"].On = values.Value.occlusion;
    }

    // ---------- fifastreet.toml (the 2.0 "deck" base), as DeckLaunch.cs ----------

    private void LoadDeckToml()
    {
        var toml = ReadToml(TomlPath);
        string Str(string key) => toml.TryGetValue(key, out var v) ? v.Trim('"') : "";
        bool? Bool(string key) => toml.TryGetValue(key, out var v) ? v == "true" : null;
        double? Num(string key) => toml.TryGetValue(key, out var v) && double.TryParse(v.Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

        string effect = Str("present_effect"), mode = Str("modo_fsr");
        int scale = Array.FindIndex(DeckScaling, s => s.Effect == effect && (effect != "fsr" || s.Mode == mode));
        if (scale >= 0) fields["scaling"].Index = scale;
        if (Bool("proporcion_de_la_pantalla") is { } aspect) fields["aspect"].Index = aspect ? 0 : 1;
        if (Num("ritmo_contra_pantalla_hz") is { } hz) { int i = Array.FindIndex(DeckFpsLimits, f => Math.Abs(f.Hz - hz) < 0.5); if (i >= 0) fields["fpsLimit"].Index = i; }
        if (Num("anisotropic_override") is { } an) { int i = Array.FindIndex(DeckAniso, a => a.Value == (int)an); if (i >= 0) fields["aniso"].Index = i; }
        string ao = Str("modo_oclusion"); { int i = Array.FindIndex(DeckOcclusion, o => o.Value == ao); if (i >= 0) fields["occlusion"].Index = i; }
        if (Bool("sincronia_vertical") is { } vs) toggles["vsync"].On = vs;
        if (Bool("multimuestreo_del_juego") is { } ms) toggles["msaa"].On = ms;
        if (Bool("fullscreen") is { } fs) fields["displayMode"].Index = fs ? 0 : 1;
        int post = Str("swap_post_effect") switch { "none" => 0, "fxaa" => 1, "fxaa_extreme" => 2, _ => -1 };
        if (post >= 0) fields["post"].Index = post;
        if (int.TryParse(Str("resolucion_interna"), out int ri) && ri >= 1 && ri <= fields["internal"].Items.Length) fields["internal"].Index = ri - 1;
        if (Num("window_width") is { } ww && Num("window_height") is { } wh)
        {
            int i = Array.IndexOf(fields["resolution"].Items, $"{(int)ww}x{(int)wh}");
            if (i >= 0) fields["resolution"].Index = i;
        }
    }

    private void SaveDeckToml()
    {
        if (!deck) return;
        var scaling = DeckScaling[Math.Max(0, fields["scaling"].Index)];
        var values = new Dictionary<string, string>
        {
            ["fullscreen"] = fields["displayMode"].Index == 0 ? "true" : "false",
            ["gpu_backend"] = fields["api"].Index == 1 ? "\"vulkan\"" : "\"d3d12\"",
            ["resolucion_interna"] = "\"" + Math.Max(1, fields["internal"].Index + 1).ToString(CultureInfo.InvariantCulture) + "\"",
            ["present_effect"] = "\"" + scaling.Effect + "\"",
            ["modo_fsr"] = "\"" + scaling.Mode + "\"",
            ["proporcion_de_la_pantalla"] = fields["aspect"].Index == 1 ? "false" : "true",
            ["ritmo_contra_pantalla_hz"] = DeckFpsLimits[Math.Max(0, fields["fpsLimit"].Index)].Hz.ToString("0.000000", CultureInfo.InvariantCulture),
            ["sincronia_vertical"] = toggles["vsync"].On ? "true" : "false",
            ["anisotropic_override"] = DeckAniso[Math.Max(0, fields["aniso"].Index)].Value.ToString(CultureInfo.InvariantCulture),
            ["swap_post_effect"] = "\"" + (fields["post"].Index switch { 1 => "fxaa", 2 => "fxaa_extreme", _ => "none" }) + "\"",
            ["multimuestreo_del_juego"] = toggles["msaa"].On ? "true" : "false",
            ["modo_oclusion"] = "\"" + DeckOcclusion[Math.Max(0, fields["occlusion"].Index)].Value + "\"",
        };
        var size = fields["resolution"].Text.Split('x');
        if (size.Length == 2 && int.TryParse(size[0], out int w) && int.TryParse(size[1], out int h) && w > 0 && h > 0)
        {
            values["window_width"] = w.ToString(CultureInfo.InvariantCulture);
            values["window_height"] = h.ToString(CultureInfo.InvariantCulture);
        }
        try { WriteToml(TomlPath, values); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
    }

    private static Dictionary<string, string> ReadToml(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return map;
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith('[')) continue;
                map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
        return map;
    }

    private static void WriteToml(string path, IReadOnlyDictionary<string, string> values)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string> { "# Auto-generated cvar configuration" };
        var left = new HashSet<string>(values.Keys);
        for (int i = 0; i < lines.Count; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq <= 0 || lines[i].TrimStart().StartsWith('#')) continue;
            string key = lines[i][..eq].Trim();
            if (!values.TryGetValue(key, out var v)) continue;
            lines[i] = key + " = " + v;
            left.Remove(key);
        }
        // New keys go before the first [section], where the runtime keeps the plain ones.
        int at = lines.FindIndex(l => l.TrimStart().StartsWith('['));
        if (at < 0) at = lines.Count;
        lines.InsertRange(at, left.Select(k => k + " = " + values[k]));
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    // ---------- Online ----------

    internal (string Server, string Name, string Key)? LoadOnlineConfig()
    {
        try
        {
            string path = Path.Combine(root, "online.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? server = doc.RootElement.TryGetProperty("server", out var s) ? s.GetString() : null;
            string? name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? key = doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;
            return string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(name) ? null : (server.Trim(), name.Trim(), (key ?? "").Trim());
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string ReadyText()
    {
        if (Proton.UmuRun() == null) return "umu-run is missing: install umu-launcher to play";
        var online = LoadOnlineConfig();
        if (online != null && !toggles["onlineMode"].On) return "Ready to play offline (online is off)";
        return online == null ? "Online is not set up. Run the online patch again" : "Ready to play online as " + online.Value.Name;
    }

    // Moves this install to another region: the same name and key, another server (online.json in both places).
    private void SwitchServer(string address)
    {
        if (gameRunning || !Regions.Any(r => r.Address == address)) return;
        foreach (string path in new[] { Path.Combine(root, "online.json"), Path.Combine(binaries, "online.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var node = JsonNode.Parse(File.ReadAllText(path))!;
                node["server"] = address;
                File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SetStatus("Could not change the server: " + ex.Message, false);
            }
        }
    }

    private readonly Dictionary<string, (int Ping, int Online)> serverInfo = new();

    private async Task PollServers()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        while (!closing)
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

    // ---------- Page ----------

    public void StartServices()
    {
        _ = Task.Run(PollServers);
        _ = Task.Run(async () =>
        {
            while (!closing)
            {
                PushState(false);
                await Task.Delay(1000);
            }
        });
    }

    private void SetStatus(string text, bool ok)
    {
        lock (sync) { status = text; statusOk = ok; }
        PushState(false);
    }

    private void Post(object message) => Page?.Send(JsonSerializer.Serialize(message));

    public void OnMessage(string json)
    {
        JsonElement m;
        try { using var doc = JsonDocument.Parse(json); m = doc.RootElement.Clone(); }
        catch (JsonException) { return; }
        string cmd = m.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        string id = m.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
        switch (cmd)
        {
            case "set":
                lock (sync)
                {
                    if (fields.TryGetValue(id, out var f) && f.Enabled && m.TryGetProperty("index", out var ix) && ix.TryGetInt32(out int index)
                        && index >= 0 && index < f.Items.Length)
                    {
                        f.Index = index;
                        if (id == "profile") ApplyProfile();
                        else if (id is "post" or "internal" or "readback") fields["profile"].Index = ProfileNames.Length - 1;
                    }
                }
                break;
            case "toggle":
                lock (sync)
                {
                    if (toggles.TryGetValue(id, out var t) && t.Enabled && m.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        t.On = v.GetBoolean();
                        if (id is "msaa" or "occlusion") fields["profile"].Index = ProfileNames.Length - 1;
                    }
                }
                break;
            case "play": if (!gameRunning) _ = LaunchGame(); break;
            case "close": closing = true; CloseWindow?.Invoke(); return;
            case "drag" or "minimize" or "maximize": return;  // the native window has its own title bar
            case "logs": Open(Path.Combine(binaries, "Logs")); break;
            case "folder": Open(root); break;
            case "shortcut": CreateDesktopEntry(); break;
            case "perfShare": if (m.TryGetProperty("value", out var ps) && ps.ValueKind is JsonValueKind.True or JsonValueKind.False) { perfShare = ps.GetBoolean(); SaveSettings(); } return;
            case "controls": if (m.TryGetProperty("value", out var cv) && cv.ValueKind == JsonValueKind.Object) SetControls(cv); break;
            case "server": if (m.TryGetProperty("address", out var a)) SwitchServer(a.GetString() ?? ""); break;
            case "social" or "chatSend" or "chatAction" or "friend" or "room":
                _ = SocialAsync(cmd, m);
                return;
            case "updateGo":
                Post(new { type = "updateError", message = "On Linux, update with the new ReStreet setup (see the README)." });
                return;
        }
        if (cmd is "set" or "toggle")
        {
            lock (sync)
            {
                UpdateEnabled();
                status = ReadyText();
                SaveSettings();
                SaveDeckToml();
            }
        }
        PushState(true);
    }

    private void PushState(bool force)
    {
        if (Page == null) return;
        string json;
        lock (sync)
        {
            var fieldState = fields.ToDictionary(f => f.Key, f => (object)new { items = f.Value.Items, index = f.Value.Index, enabled = f.Value.Enabled });
            var toggleState = toggles.ToDictionary(t => t.Key, t => (object)new { on = t.Value.On, enabled = t.Value.Enabled });
            toggleState["perfShare"] = new { on = perfShare, enabled = true };
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
                    status = gameRunning ? "Playing" : toggles["onlineMode"].On ? "Ready to play online" : "Online is off",
                    servers = Regions.Select(r => new
                    {
                        name = r.Name, address = r.Address,
                        ping = info.TryGetValue(r.Address, out var s) ? s.Ping : -1,
                        online = info.TryGetValue(r.Address, out var s2) ? s2.Online : -1,
                    }).ToArray(),
                };
            }
            string proton = protons.Count > 0 ? Path.GetFileName(protons[0]) : "UMU-Proton";
            json = JsonSerializer.Serialize(new
            {
                fields = fieldState, toggles = toggleState, online = onlineState, controls = ControlsState(),
                exe = Path.Combine(binaries, "fifastreet.exe"), data = gameRoot,
                summary = $"{proton} · {fields["resolution"].Text} · {fields["refresh"].Text} Hz",
                status, statusOk, running = gameRunning, deck,
                api = fields["api"].Text, profile = fields["profile"].Text,
                version = "V" + typeof(Launcher).Assembly.GetName().Version!.ToString(3) + " · ONLINE BETA",
            });
            if (!force && json == lastState) return;
            lastState = json;
        }
        Page.Send(json);
    }

    private static void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex.Message); }
    }

    // The desktop menu entry, as the Windows launcher's "direct play" shortcut: starts the game with the saved settings.
    private void CreateDesktopEntry()
    {
        try
        {
            string self = Environment.ProcessPath!;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications");
            Directory.CreateDirectory(dir);
            string icon = Path.Combine(AppContext.BaseDirectory, "restreet.jpg");
            string Entry(string name, string args) =>
                $"[Desktop Entry]\nType=Application\nName={name}\nExec=\"{self}\" --root \"{root}\"{args}\nPath={AppContext.BaseDirectory}\n" +
                $"Icon={icon}\nCategories=Game;\nComment=FIFA Street (2012) recompiled\n";
            File.WriteAllText(Path.Combine(dir, "restreet-launcher.desktop"), Entry("ReStreet Launcher", ""));
            File.WriteAllText(Path.Combine(dir, "restreet-play.desktop"), Entry("ReStreet (direct play)", " --play"));
            SetStatus("Menu entries created: ReStreet Launcher and ReStreet (direct play)", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not create the menu entries: " + ex.Message, false);
        }
    }

    public void WaitWhileBrowserConnected(PageServer server)
    {
        while (!closing && (gameRunning || DateTime.UtcNow - server.LastSeen < TimeSpan.FromSeconds(90))) Thread.Sleep(1000);
    }

    public void Shutdown()
    {
        closing = true;
    }
}
