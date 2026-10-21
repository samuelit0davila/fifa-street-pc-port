using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace FifaStreetLauncher.Linux;

// PLAY: the same files and REX_* / FIFASTREET_* variables as LauncherForm.LaunchGame, with the game started through
// Proton. Paths the game reads are given as Wine paths (Z:\...).
internal sealed partial class Launcher
{
    // The deck build is x86-64-v3 (AVX2/BMI2/FMA); older CPUs get Binaries/NoAVX2 (see DeckLaunch.cs).
    private static readonly bool CpuHasX86_64V3 =
        System.Runtime.Intrinsics.X86.Avx2.IsSupported && System.Runtime.Intrinsics.X86.Bmi2.IsSupported &&
        System.Runtime.Intrinsics.X86.Fma.IsSupported && System.Runtime.Intrinsics.X86.Lzcnt.IsSupported;

    public int PlayAndWait()
    {
        LaunchGame().Wait();
        return statusOk ? 0 : 1;
    }

    private async Task LaunchGame()
    {
        if (gameRunning) return;
        gameRunning = true;
        try
        {
            ProcessStartInfo startInfo;
            lock (sync) startInfo = BuildStartInfo();
            SetStatus("Starting ReStreet...", true);
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                SetStatus("Could not start the game", false);
                return;
            }
            gameProcess = process;
            SetStatus("Running through Proton", true);
            await process.WaitForExitAsync();
            SetStatus(ReadyText(), true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Launch failed: " + ex.Message, false);
        }
        finally
        {
            gameProcess = null;
            gameRunning = false;
            PushState(true);
        }
    }

    private ProcessStartInfo BuildStartInfo()
    {
        string exe = Path.Combine(binaries, "fifastreet.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException("fifastreet.exe was not found.");
        if (!File.Exists(Path.Combine(gameRoot, "default.xex"))) throw new InvalidOperationException("The game folder does not contain default.xex.");

        string graphicsApi = fields["api"].Index == 1 ? "Vulkan" : "D3D12";
        string backend = Path.Combine(binaries, "Backends", graphicsApi);
        foreach (string dll in new[] { "rexruntime.dll", "rexgpu-xenos.dll" })
        {
            if (!File.Exists(Path.Combine(backend, dll))) throw new InvalidOperationException($"The {graphicsApi} graphics backend is missing ({backend}).");
            File.Copy(Path.Combine(backend, dll), Path.Combine(binaries, dll), true);
        }

        var size = fields["resolution"].Text.Split('x');
        if (size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height))
            throw new InvalidOperationException("Select a valid resolution.");
        string refresh = fields["refresh"].Text;
        bool fullscreen = fields["displayMode"].Index == 0;
        int monitor = Math.Max(0, fields["monitor"].Index);
        bool vsync = toggles["vsync"].On;
        string postEffect = fields["post"].Index switch { 1 => "fxaa", 2 => "fxaa_extreme", _ => "none" };
        int internalScale = Math.Max(1, fields["internal"].Index + 1);
        string readbackResolve = ReadbackResolveModes[Math.Clamp(fields["readback"].Index, 0, ReadbackResolveModes.Length - 1)];

        string logsDir = Path.Combine(binaries, "Logs");
        Directory.CreateDirectory(logsDir);
        string logFile = Path.Combine(logsDir, "fifastreet.log");

        var startInfo = Proton.StartInfo(exe, prefix, protons.FirstOrDefault());
        var env = startInfo.Environment;
        string wineGameRoot = InstallFolder.WindowsPath(gameRoot);
        // ReXGlue resolves each setting in this order, lowest priority first:
        // built-in default -> fifastreet.toml -> REX_* environment -> command line.
        env["REX_GAME_DATA_ROOT"] = wineGameRoot;
        if (deck)
        {
            UseCpuBuild(graphicsApi);
            // The deck port's own start-up switches (native renderer, its shader folders...), as its launcher sets them.
            foreach (string line in File.ReadAllLines(Path.Combine(binaries, "ajustes-del-port.txt")))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith('?') || line.StartsWith('#')) continue;
                env[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            env["REX_GPU_BACKEND"] = graphicsApi == "Vulkan" ? "vulkan" : "d3d12";
            if (graphicsApi == "Vulkan") env["REX_VULKAN_DEVICE"] = "-1";
            SaveDeckToml();
            ApplyGameLanguage(env);
            env["REX_MONITOR"] = (monitor + 1).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            env["REX_VIDEO_MODE_WIDTH"] = width.ToString(CultureInfo.InvariantCulture);
            env["REX_VIDEO_MODE_HEIGHT"] = height.ToString(CultureInfo.InvariantCulture);
            env["REX_DRAW_RESOLUTION_SCALE_X"] = internalScale.ToString(CultureInfo.InvariantCulture);
            env["REX_DRAW_RESOLUTION_SCALE_Y"] = internalScale.ToString(CultureInfo.InvariantCulture);
            env["REX_WINDOW_WIDTH"] = width.ToString(CultureInfo.InvariantCulture);
            env["REX_WINDOW_HEIGHT"] = height.ToString(CultureInfo.InvariantCulture);
            env["REX_VIDEO_MODE_REFRESH_RATE"] = refresh;
            env["REX_FRAME_LIMITER_MIN_HZ"] = vsync ? "auto" : "1000";
            env.Remove("REX_READBACK_SOME_SYNC_ADDRS");
            env["REX_READBACK_SOME_SYNC_BELOW"] = "0";
            ApplyGameLanguage(env);
            env["REX_GUEST_VBLANK_UNLOCKED"] = "true";
            env["REX_FULLSCREEN"] = fullscreen ? "true" : "false";
            env["REX_MONITOR"] = (monitor + 1).ToString(CultureInfo.InvariantCulture);
            env["REX_VSYNC"] = "false";
            env["REX_TRUE_VSYNC"] = vsync ? "1" : "0";
            env["REX_NATIVE_2X_MSAA"] = toggles["msaa"].On ? "true" : "false";
            env["REX_SWAP_POST_EFFECT"] = postEffect;
            if (graphicsApi == "D3D12")
            {
                env["REX_D3D12_ALLOW_VARIABLE_REFRESH_RATE_AND_TEARING"] = !vsync && toggles["vrr"].On ? "true" : "false";
                env["REX_RENDER_TARGET_PATH_D3D12"] = "";
            }
            else
            {
                env["REX_VULKAN_DEVICE"] = "-1";
                env["REX_RENDER_TARGET_PATH_VULKAN"] = "fbo";
                env["REX_DRAW_RESOLUTION_SCALED_TEXTURE_OFFSETS"] = "false";
                env["REX_DEPTH_FLOAT24_CONVERT_IN_PIXEL_SHADER"] = "true";
                env["REX_GAMMA_RENDER_TARGET_AS_UNORM16"] = "false";
                env["REX_SNORM16_RENDER_TARGET_FULL_RANGE"] = "false";
                env["REX_VULKAN_DYNAMIC_RENDERING"] = "false";
                env["REX_HALF_PIXEL_OFFSET"] = "false";
                env["REX_ANISOTROPIC_OVERRIDE"] = "-1";
                env["REX_DIRECT_HOST_RESOLVE"] = "false";
                env["REX_VULKAN_ALLOW_PRESENT_MODE_IMMEDIATE"] = vsync ? "false" : "true";
                env["REX_VULKAN_ALLOW_PRESENT_MODE_MAILBOX"] = vsync ? "true" : "false";
                env["REX_VULKAN_ALLOW_PRESENT_MODE_FIFO_RELAXED"] = vsync ? "false" : "true";
            }
            env["REX_READBACK_RESOLVE"] = readbackResolve;
            env["REX_D3D12_READBACK_SHORT_WAIT"] = "true";
            env["REX_D3D12_READBACK_SPIN_US"] = "500";
            env["REX_VULKAN_READBACK_SHORT_WAIT"] = "true";
            env["REX_VULKAN_READBACK_SPIN_US"] = "500";
            bool memexport = toggles["memexport"].On;
            env["REX_READBACK_MEMEXPORT"] = memexport ? "true" : "false";
            env[graphicsApi == "Vulkan" ? "REX_VULKAN_READBACK_MEMEXPORT" : "REX_D3D12_READBACK_MEMEXPORT"] = memexport ? "true" : "false";
            env["REX_READBACK_MEMEXPORT_FAST"] = memexport && toggles["memexportFast"].On ? "true" : "false";
            env["REX_CLEAR_MEMORY_PAGE_STATE"] = toggles["pageState"].On ? "true" : "false";
            env["REX_OCCLUSION_QUERY_ENABLE"] = toggles["occlusion"].On ? "true" : "false";
            env["REX_ASYNC_SHADER_COMPILATION"] = toggles["asyncShaders"].On ? "true" : "false";
        }
        ApplyControls(env);

        env["REX_LOG_VERBOSE"] = "false";
        env["REX_LOG_LEVEL"] = "info";
        env["REX_LOG_FRAME_STATS"] = toggles["frameStats"].On ? "true" : "false";
        env["REX_BIND_DEBUG_OVERLAY"] = "Home";
        env["REX_LOG_FILE"] = InstallFolder.WindowsPath(logFile);

        // Online play: the server and the player name come from the online patch.
        var online = LoadOnlineConfig();
        if (online != null && toggles["onlineMode"].On)
        {
            env["FIFASTREET_SERVER"] = online.Value.Server;
            env["FIFASTREET_PLAYER_NAME"] = online.Value.Name;
            env["FIFASTREET_PLAYER_KEY"] = online.Value.Key;
            env["FIFASTREET_ONLINE_EXPERIMENT"] = "1";
            env["FIFASTREET_ONLINE_ENDPOINT_PROBE"] = "1";
            env["FIFASTREET_NET_DIAG"] = "";
            if (!toggles["frameStats"].On) env["REX_LOG_LEVEL"] = "warn";
        }
        startInfo.ArgumentList.Add($"--game_data_root={wineGameRoot}");

        SaveSettings();
        try
        {
            File.WriteAllText(Path.Combine(logsDir, "launcher_settings.txt"),
                $"Launcher=Linux{Environment.NewLine}Proton={protons.FirstOrDefault() ?? "UMU-Proton (default)"}{Environment.NewLine}" +
                $"Prefix={prefix}{Environment.NewLine}Resolution={width}x{height}{Environment.NewLine}" +
                $"InternalResolution={1280 * internalScale}x{720 * internalScale} ({internalScale}x){Environment.NewLine}" +
                $"MonitorRefreshRate={refresh}{Environment.NewLine}GraphicsApi={graphicsApi}{Environment.NewLine}" +
                $"Fullscreen={fullscreen}{Environment.NewLine}Monitor={monitor}{Environment.NewLine}VSync={vsync}{Environment.NewLine}" +
                $"MSAA={toggles["msaa"].On}{Environment.NewLine}PostEffect={postEffect}{Environment.NewLine}Deck={deck}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
        return startInfo;
    }

    private void ApplyGameLanguage(IDictionary<string, string?> env)
    {
        int id = GameLanguageIds[Math.Max(0, fields["language"].Index)];
        env["REX_USER_LANGUAGE"] = (id == PtBrLanguageId ? 1 : id).ToString(CultureInfo.InvariantCulture);
        if (id == PtBrLanguageId) env["FIFASTREET_GAME_LANGUAGE"] = "pt-BR";
        else env.Remove("FIFASTREET_GAME_LANGUAGE");
    }

    private void UseCpuBuild(string graphicsApi)
    {
        string set = Path.Combine(binaries, CpuHasX86_64V3 ? "AVX2" : "NoAVX2");
        if (!Directory.Exists(set)) return;
        foreach (string name in new[] { "fifastreet.exe", "fifastreet_fifadllzf_xex.dll", "fifastreet_footballcompengzf_xex.dll" })
            CopyIfDifferent(Path.Combine(set, name), Path.Combine(binaries, name));
        string backend = Path.Combine(set, "Backends", graphicsApi);
        if (!CpuHasX86_64V3 && Directory.Exists(backend))
            foreach (string dll in Directory.GetFiles(backend, "*.dll"))
                CopyIfDifferent(dll, Path.Combine(binaries, Path.GetFileName(dll)));
    }

    private static void CopyIfDifferent(string source, string target)
    {
        if (!File.Exists(source)) return;
        var s = new FileInfo(source);
        var t = new FileInfo(target);
        if (t.Exists && t.Length == s.Length && t.LastWriteTimeUtc == s.LastWriteTimeUtc) return;
        File.Copy(source, target, true);
        File.SetLastWriteTimeUtc(target, s.LastWriteTimeUtc);  // on Linux File.Copy does not keep it
    }

    // ---------- Launcher rooms (RoomLaunch.cs): a full room starts the game by itself, once per room ----------

    private long roomLaunchedFor;

    private void OnRoomState(JsonElement? room)
    {
        if (room is not { ValueKind: JsonValueKind.Object } r) return;
        long id = r.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.Number ? idv.GetInt64() : 0;
        int players = r.TryGetProperty("players", out var pv) && pv.ValueKind == JsonValueKind.Array ? pv.GetArrayLength() : 0;
        int seats = r.TryGetProperty("seats", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetInt32() : 2;
        if (players < seats || Interlocked.Exchange(ref roomLaunchedFor, id) == id || !toggles["onlineMode"].On) return;
        _ = Task.Run(async () =>
        {
            // A game already open sits on a menu the room driver cannot walk from: close it and start it again.
            if (gameRunning && gameProcess is { } running)
            {
                try { running.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex.Message); }
                for (int i = 0; i < 100 && gameRunning; ++i) await Task.Delay(100);
            }
            if (!gameRunning) await LaunchGame();
        });
    }
}
