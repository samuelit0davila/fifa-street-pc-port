using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using ReStreet.Ui;

namespace FifaStreetLauncher;

public partial class LauncherForm : Form, ISceneHost, IUiScaled
{
    // Until it has finished once: starts the stadium pictures (stadium-thumbs next to the game exe) from the player's own
    // game files, with the bundled tools\ReStreetAssetExplorer.exe, without waiting for it. Nothing from the game ships.
    // The tool writes stadium-thumbs\.complete5 last (3: + team crests in crests\; 4: crests at 112 px; 5: wide stadium previews; older sets are redone),
    // so an interrupted run is simply started again the next time the launcher opens or on PLAY.
    private static void EnsureStadiumThumbs(string exeDir, string gameRoot)
    {
        string folder = Path.Combine(exeDir, "stadium-thumbs");
        if (File.Exists(Path.Combine(folder, ".complete5"))) return;
        // A run started from a previous PLAY may still be working on the same folder.
        var running = Process.GetProcessesByName("ReStreetAssetExplorer");
        foreach (var process in running) process.Dispose();
        if (running.Length > 0) return;
        string tool = Path.Combine(AppContext.BaseDirectory, "tools", "ReStreetAssetExplorer.exe");
        if (!File.Exists(tool)) return;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = tool,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("--stadiumpics");
            info.ArgumentList.Add(gameRoot);
            info.ArgumentList.Add(folder);
            // Runs in the background so the game starts at once; the pictures show from the next session.
            _ = Process.Start(info);
        }
        catch (Exception)
        {
            // The game still starts; the stadium picker then shows names only.
        }
    }

    private readonly UiScaler scaler;
    public float UiScale => scaler.Scale;

    private readonly Color Bg = StreetTheme.Asphalt;
    private readonly Color Surface = Color.FromArgb(28, 31, 36);
    private readonly Color SurfaceAlt = StreetTheme.Field;
    private readonly Color Border = Color.FromArgb(62, 66, 72);
    private readonly Color Accent = StreetTheme.Yellow;
    private readonly Color AccentHover = Color.FromArgb(255, 212, 48);
    private readonly Color TextPrimary = StreetTheme.Ink;
    private readonly Color TextSecondary = StreetTheme.Muted;

    private readonly GlassCombo resolutionBox = new();
    private readonly GlassCombo refreshBox = new();
    private readonly GlassCombo displayModeBox = new();
    private readonly GlassCombo monitorBox = new();
    private readonly GlassCombo graphicsApiBox = new();
    private readonly GlassCombo gpuBox = new();
    private readonly List<int> gpuAdapterIndices = new();
    private readonly GlassCombo postEffectBox = new();
    private readonly GlassCombo internalResolutionBox = new();
    private readonly GlassCombo gameLanguageBox = new();
    private readonly GlassCombo readbackResolveBox = new();
    private readonly GlassCombo profileBox = new();
    private bool applyingProfile;
    private static readonly string[] ProfileNames =
    {
        "Balanced (default)",
        "Quality (strong PCs)",
        "Performance (weak PCs)",
        "Custom"
    };
    private static readonly string[] ReadbackResolveModes = { "full", "some", "fast", "none" };
    // Xbox language ids; PtBrLanguageId is ReStreet's Brazilian Portuguese: English to the game (the translation takes
    // the English slot) plus FIFASTREET_GAME_LANGUAGE=pt-BR, which loads locale_ptbr.big and shows cm/kg.
    private const int PtBrLanguageId = 1001;
    private static readonly int[] GameLanguageIds = { 1, 5, 4, 3, 6, PtBrLanguageId };

    private readonly GlassToggle vsyncBox = new();
    private readonly GlassToggle vrrBox = new();
    private readonly GlassToggle msaaBox = new();
    private readonly GlassToggle readbackMemexportBox = new();
    private readonly GlassToggle readbackMemexportFastBox = new();
    private readonly GlassToggle clearMemoryPageStateBox = new();
    private readonly GlassToggle occlusionQueryBox = new();
    private readonly GlassToggle asyncShadersBox = new();
    private readonly GlassToggle logFrameStatsBox = new();

    private readonly TextBox gamePathBox = new();
    private readonly TextBox exePathBox = new();

    private readonly ThemedLabel statusLabel = new();
    private readonly ThemedLabel hardwareLabel = new();
    private readonly Label advancedChevron = new();

    private readonly RoundedPanel advancedPanel = new();
    private readonly SlantButton advancedButton = new();
    private readonly RoundedPanel compatibilityPanel = new();
    private readonly SlantButton compatibilityButton = new();

    private bool gameRunning;
    private Process? gameProcess;  // the game this launcher started, while it runs
    private bool closeAfterGame;
    private DiscordPresence? discord;
    private System.Windows.Forms.Timer? presenceTimer;
    private string? changedDisplay;
    private DEVMODE previousDisplayMode;
    private bool advancedVisible;
    private bool compatibilityVisible;

    private string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public LauncherForm()
    {
        Text = "ReStreet Launcher";
        using (var icon = typeof(LauncherForm).Assembly.GetManifestResourceStream("fifastreet.ico")!)
            Icon = new Icon(icon);
        ClientSize = new Size(1250, 720);
        scaler = new UiScaler(this, 1250, 720);
        DoubleBuffered = true;
        ResizeRedraw = true;
        HandleCreated += (_, _) => StreetTheme.ApplyDarkTitleBar(Handle);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F);

        BuildInterface();
        WireEvents();
        applyingProfile = true;     // loading values must not flip the profile to Custom
        LoadDefaults();
        profileBox.SelectedIndex = 0;
        LoadSettings();
        // A reinstall wrote a new online.json: put this player's key back before anything signs in with it.
        PlayerKey.Sync(OnlineFiles());
        InitDeckOptions();
        applyingProfile = false;
        UpdateGraphicsApiState();
        UpdateRefreshRates();
        UpdateSummary();
        UpdateStatus(ReadyText(), true);
        scaler.Capture();
        // Stay invisible until the new page has loaded, so the classic controls underneath never flash on screen.
        if (Environment.GetEnvironmentVariable("RESTREET_CLASSIC_UI") != "1") Opacity = 0;
        // Direct play asks for updates itself, before the game starts (StartDirect).
        Shown += (_, _) => { InitGlassUi(); if (!closeAfterGame) { StartUpdateChecks(); BeginInvoke(WarnOneDriveOnce); } };
    }

    // Layout on an 8 px grid (design pixels, window 1250 x 720): left column margin 48; glass sheet
    // 620..1202 x 32..688 (margin 48 / 32) with 24 px padding, so content spans x 644..1178 as two
    // 259 px columns with a 16 px gap. Captions are 16 high, fields 36, buttons 40.
    private const float DesignW = 1250f, DesignH = 720f;
    private const int Left = 48;
    private const int SheetX = 620, SheetY = 32, SheetW = 582, SheetH = 656, Pad = 24;
    private const int ContentX = SheetX + Pad, ContentW = SheetW - 2 * Pad;
    private const int ColW = 259, Col1 = ContentX, Col2 = ContentX + ColW + 16;
    private const int PanelY = 720, PanelX = Left, PanelW = 1154;
    private Bitmap? scene;
    private Size sceneSize;

    public Bitmap SceneBitmap { get { BuildScene(); return scene!; } }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        BuildScene();
        using (var floor = new SolidBrush(StreetTheme.Asphalt)) e.Graphics.FillRectangle(floor, ClientRectangle);
        e.Graphics.DrawImageUnscaled(scene!, 0, 0);
    }

    // Court backdrop, glass sheet, wordmark and credit, painted once per window size.
    private void BuildScene()
    {
        float s = ClientSize.Width / DesignW;
        var size = new Size(ClientSize.Width, (int)Math.Ceiling(DesignH * s));
        if (scene != null && sceneSize == size) return;
        scene?.Dispose();
        sceneSize = size;
        try
        {
            BuildSceneFull(size, s);
        }
        catch (Exception ex) when (ex is NotImplementedException or ArgumentException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException or InvalidOperationException)
        {
            // Last resort (seen on Wine/Proton): a plain background, so the controls still draw on top of it.
            scene?.Dispose();
            scene = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            using var g = Graphics.FromImage(scene);
            g.Clear(StreetTheme.Asphalt);
        }
    }

    private void BuildSceneFull(Size size, float s)
    {
        using var backdrop = StreetTheme.RenderBackdrop(size, DesignW, DesignH);
        using var blurred = StreetTheme.Blur(backdrop, 10);
        scene = new Bitmap(backdrop);
        using var g = Graphics.FromImage(scene);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(s, s);

        StreetTheme.DrawGlass(g, blurred, s, new RectangleF(SheetX, SheetY, SheetW, SheetH), 14);
        StreetTheme.DrawSection(g, "GENERAL", ContentX, 56, ContentX + ContentW);
        StreetTheme.DrawSection(g, "DISPLAY", ContentX, 166, ContentX + ContentW);
        StreetTheme.DrawSection(g, "GRAPHICS", ContentX, 350, ContentX + ContentW);
        using (var line = new Pen(StreetTheme.Hairline, 1f)) g.DrawLine(line, ContentX, 607.5f, ContentX + ContentW, 607.5f);
        // Status block: yellow rule beside the hardware summary and the online status.
        using (var rule = new SolidBrush(StreetTheme.Yellow)) g.FillRectangle(rule, Left, 314, 3, 52);
        using (var line = new Pen(StreetTheme.Hairline, 1f)) g.DrawLine(line, Left, 627.5f, Left + 480, 627.5f);

        using (var small = new Font(StreetTheme.CondensedFamily, 17, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var yellow = new SolidBrush(StreetTheme.Yellow))
            StreetTheme.DrawSpaced(g, "FIFA STREET 2012 RECOMPILED", small, yellow, new PointF(Left, 56), 3.6f);
        StreetTheme.DrawWordmark(g, "ReStreet", new PointF(Left, 88), 178, StreetTheme.Ink);
        var tag = new RectangleF(Left, 240, 270, 36);
        using (var bar = new SolidBrush(StreetTheme.Yellow)) g.FillPolygon(bar, StreetTheme.Slant(tag, 12));
        using (var tagFont = new Font(StreetTheme.CondensedFamily, 18, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var dark = new SolidBrush(StreetTheme.YellowDark))
            StreetTheme.DrawSpaced(g, "V" + LauncherVersion + " · ONLINE BETA", tagFont, dark, new PointF(Left + 14, 246), 2.8f);

        using var name = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        StreetTheme.DrawLabelValue(g, "Ported by:", "SamuelitoDaVila", name, StreetTheme.Ink, StreetTheme.Ink, Left, 642);
        StreetTheme.DrawLabelValue(g, "Contributions:", "Emran_Ahm3d", name, StreetTheme.Muted, StreetTheme.Muted, Left, 665);
    }

    // online.json is written by the online patch, next to this launcher.
    internal static (string Server, string Name, string Key)? LoadOnlineConfig()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "online.json");
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
        var online = LoadOnlineConfig();
        if (online != null && !onlineModeBox.Checked) return "Ready to play offline (online is off)";
        return online == null ? "Online is not set up. Run the online patch again" : "Ready to play online as " + online.Value.Name;
    }

    private void BuildInterface()
    {
        SuspendLayout();

        // Three sections (titles painted in BuildScene at y 56 / 166 / 350). A field is a 16 px caption,
        // 6 px gap and a 36 px box; rows inside a section are 74 apart.
        void Field(string label, ComboBox box, int x, int y)
        {
            AddFieldLabel(this, label, x, y);
            ConfigureCombo(box, x, y + 22, ColW);
            Controls.Add(box);
        }
        Field("Profile", profileBox, Col1, 84);
        profileBox.AccessibleName = "Performance profile";
        profileBox.Items.AddRange(ProfileNames);
        profileBox.SelectedIndex = 0;
        profileBox.SelectedIndexChanged += (_, _) => ApplyProfile();
        postEffectBox.SelectedIndexChanged += (_, _) => MarkCustomProfile();
        internalResolutionBox.SelectedIndexChanged += (_, _) => MarkCustomProfile();
        readbackResolveBox.SelectedIndexChanged += (_, _) => MarkCustomProfile();
        msaaBox.CheckedChanged += (_, _) => MarkCustomProfile();
        occlusionQueryBox.CheckedChanged += (_, _) => MarkCustomProfile();

        Field("Game language", gameLanguageBox, Col2, 84);
        gameLanguageBox.AccessibleName = "Game language";
        Field("Resolution", resolutionBox, Col1, 194);
        Field("Refresh rate", refreshBox, Col2, 194);
        Field("Display mode", displayModeBox, Col1, 268);
        Field("Monitor", monitorBox, Col2, 268);
        Field("Graphics API", graphicsApiBox, Col1, 378);
        Field("GPU", gpuBox, Col2, 378);
        Field("Post-processing", postEffectBox, Col1, 452);
        Field("Internal resolution", internalResolutionBox, Col2, 452);

        // Switches in one row, 24 px apart.
        vsyncBox.CheckedChanged += (_, _) => UpdateGraphicsApiState();
        readbackMemexportBox.CheckedChanged += (_, _) => UpdateGraphicsApiState();
        int switchX = Col1;
        foreach (var (box, text) in new (GlassToggle, string)[] { (vsyncBox, "VSync"), (vrrBox, "VRR / Tearing (D3D12)"), (msaaBox, "Native 2x MSAA") })
        {
            ConfigureCheck(box, text, switchX, 526);
            Controls.Add(box);
            switchX += box.GetPreferredSize(Size.Empty).Width + 24;
        }

        var tip = new ThemedLabel
        {
            Text = "Weaker PC? Pick the Performance profile.",
            ForeColor = TextSecondary,
            Font = StreetTheme.Body(10F),
            Bounds = new Rectangle(ContentX, 568, ContentW, 20)
        };
        Controls.Add(tip);

        // Bottom row: five equal buttons, 100 wide with 8 px gaps (5 x 100 + 4 x 8 = 532).
        void Chip(SlantButton button, string text, int index)
        {
            button.Kind = SlantKind.Glass;
            button.Text = text;
            button.Font = StreetTheme.Body(10F);
            button.Bounds = new Rectangle(ContentX + index * 108, 624, 100, 40);
            Controls.Add(button);
        }
        Chip(advancedButton, "Advanced", 0);
        Chip(compatibilityButton, "Compatibility", 1);
        var logsButton = new SlantButton();
        logsButton.Click += (_, _) => OpenLogs();
        Chip(logsButton, "Open logs", 2);
        var folderButton = new SlantButton();
        folderButton.Click += (_, _) => OpenGameFolder();
        Chip(folderButton, "Open folder", 3);
        var shortcutButton = new SlantButton();
        shortcutButton.Click += (_, _) => CreateDesktopShortcut();
        Chip(shortcutButton, "Shortcut", 4);

        ConfigureAdvancedPanel();
        Controls.Add(advancedPanel);

        ConfigureCompatibilityPanel();
        Controls.Add(compatibilityPanel);

        hardwareLabel.Text = "Detecting hardware...";
        hardwareLabel.ForeColor = TextSecondary;
        hardwareLabel.Font = StreetTheme.Body(11F);
        hardwareLabel.AutoEllipsis = true;
        hardwareLabel.TextAlign = ContentAlignment.MiddleLeft;
        hardwareLabel.Bounds = new Rectangle(Left + 16, 314, 496, 24);
        Controls.Add(hardwareLabel);

        statusLabel.Font = StreetTheme.Body(11F, FontStyle.Bold);
        statusLabel.ForeColor = TextSecondary;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Bounds = new Rectangle(Left + 16, 342, 496, 24);
        Controls.Add(statusLabel);

        var playButton = new SlantButton
        {
            Kind = SlantKind.Primary,
            Text = "PLAY",
            Font = StreetTheme.Condensed(36, FontStyle.Bold),
            Bounds = new Rectangle(Left, 520, 480, 88)
        };
        playButton.Click += (_, _) => LaunchGame();
        Controls.Add(playButton);
        if (LoadOnlineConfig() != null)
        {
            // Online build: PLAY shares its row with the lobby and the friends list.
            playButton.Width = 240;
            var lobbyButton = new SlantButton
            {
                Kind = SlantKind.Glass,
                Text = "LOBBY",
                Font = StreetTheme.Condensed(22, FontStyle.Bold),
                Bounds = new Rectangle(Left + 240 + 8, 520, 112, 88)
            };
            lobbyButton.Click += (_, _) =>
            {
                var current = LoadOnlineConfig();
                if (current == null) return;
                using var lobby = new LobbyForm(current.Value.Server, current.Value.Name);
                lobby.ShowDialog(this);
            };
            Controls.Add(lobbyButton);
            var friendsButton = new SlantButton
            {
                Kind = SlantKind.Glass,
                Text = "FRIENDS",
                Font = StreetTheme.Condensed(22, FontStyle.Bold),
                Bounds = new Rectangle(Left + 240 + 8 + 112 + 8, 520, 112, 88)
            };
            friendsButton.Click += (_, _) =>
            {
                var current = LoadOnlineConfig();
                if (current == null) return;
                using var friends = new FriendsForm(current.Value.Server, current.Value.Name, current.Value.Key);
                friends.ShowDialog(this);
            };
            Controls.Add(friendsButton);
        }
        AcceptButton = playButton;
        ActiveControl = playButton;

        ResumeLayout(false);
        PerformLayout();
    }

    private void ConfigureAdvancedPanel()
    {
        advancedPanel.Bounds = new Rectangle(PanelX, PanelY, PanelW, 192);
        advancedPanel.FillColor = StreetTheme.PanelFlat;
        advancedPanel.BorderColor = Color.FromArgb(52, 55, 60);
        advancedPanel.Radius = 14;
        advancedPanel.Visible = false;

        const int browseW = 96, gap = 8, innerW = PanelW - 2 * Pad;
        void PathRow(string label, TextBox box, int y, Action browse)
        {
            AddFieldLabel(advancedPanel, label, Pad, y);
            advancedPanel.Controls.Add(new FieldFrame(box) { Bounds = new Rectangle(Pad, y + 24, innerW - browseW - gap, 40) });
            box.Font = StreetTheme.Body(10F);
            var button = new SlantButton { Kind = SlantKind.Glass, Text = "Browse", Font = StreetTheme.Body(10F), Bounds = new Rectangle(Pad + innerW - browseW, y + 24, browseW, 40) };
            button.Click += (_, _) => browse();
            advancedPanel.Controls.Add(button);
        }
        PathRow("Game executable", exePathBox, Pad, BrowseExe);
        PathRow("Game data folder", gamePathBox, Pad + 80, BrowseGame);
    }

    private void ConfigureCompatibilityPanel()
    {
        compatibilityPanel.Bounds = new Rectangle(PanelX, PanelY, PanelW, 248);
        compatibilityPanel.FillColor = StreetTheme.PanelFlat;
        compatibilityPanel.BorderColor = Color.FromArgb(52, 55, 60);
        compatibilityPanel.Radius = 14;
        compatibilityPanel.Visible = false;

        compatibilityPanel.Controls.Add(new ThemedLabel
        {
            Text = "COMPATIBILITY", Font = StreetTheme.Condensed(12F, FontStyle.Bold), ForeColor = StreetTheme.Yellow,
            Bounds = new Rectangle(Pad, Pad, 400, 24)
        });
        compatibilityPanel.Controls.Add(new ThemedLabel
        {
            Text = "Renderer options for textures, lighting, black screens and stutter.", Font = StreetTheme.Body(10F), ForeColor = TextSecondary,
            Bounds = new Rectangle(Pad, Pad + 28, 800, 20)
        });

        AddFieldLabel(compatibilityPanel, "Readback Resolve", Pad, 92);
        ConfigureCombo(readbackResolveBox, Pad, 116, ColW);
        readbackResolveBox.AccessibleName = "Readback Resolve";
        compatibilityPanel.Controls.Add(readbackResolveBox);

        // Switches on the same 275 px column pitch as the main sheet (x = 299, 574, 849).
        int[] cols = { Pad + 275, Pad + 550, Pad + 825 };
        ConfigureCheck(readbackMemexportBox, "Memory Export", cols[0], 120);
        ConfigureCheck(readbackMemexportFastBox, "Fast MemExport", cols[1], 120);
        ConfigureCheck(clearMemoryPageStateBox, "Memory Page State", cols[2], 120);
        ConfigureCheck(occlusionQueryBox, "Occlusion Queries", cols[0], 162);
        ConfigureCheck(asyncShadersBox, "Async Shaders", cols[1], 162);
        ConfigureCheck(logFrameStatsBox, "Frame stats log", cols[2], 162);
        foreach (var box in new[] { readbackMemexportBox, readbackMemexportFastBox, clearMemoryPageStateBox, occlusionQueryBox, asyncShadersBox, logFrameStatsBox })
            compatibilityPanel.Controls.Add(box);

        compatibilityPanel.Controls.Add(new ThemedLabel
        {
            Text = "Readback Resolve: Some is the recommended mode (more FPS with the validated fixes). Full is the most compatible; Fast gives more FPS but may cause graphical corruption.",
            Font = StreetTheme.Body(10F), ForeColor = TextSecondary,
            Bounds = new Rectangle(Pad, 204, PanelW - 2 * Pad, 20)
        });
    }

    private void WireEvents()
    {
        advancedButton.Click += (_, _) => ToggleAdvanced();
        advancedChevron.Click += (_, _) => ToggleAdvanced();
        compatibilityButton.Click += (_, _) => ToggleCompatibility();
        onlineModeBox.CheckedChanged += (_, _) =>
        {
            if (applyingProfile) return;  // loading settings.json, not a player's change
            if (!gameRunning) UpdateStatus(ReadyText(), true);
            try { SaveSettings(); }
            catch (IOException ex) { Debug.WriteLine(ex.Message); }
        };

        monitorBox.SelectedIndexChanged += (_, _) =>
        {
            DetectDisplayModes();
            UpdateSummary();
        };

        graphicsApiBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateGraphicsApiState();
            UpdateSummary();
        };

        resolutionBox.SelectedIndexChanged += (_, _) => { UpdateRefreshRates(); UpdateSummary(); };
internalResolutionBox.SelectedIndexChanged += (_, _) =>
{
    UpdateSummary();
    if (!gameRunning)
        UpdateStatus(internalResolutionBox.SelectedIndex >= 2
            ? "High internal resolution may reduce FPS"
            : ReadyText(), true);
};
refreshBox.SelectedIndexChanged += (_, _) => UpdateSummary();
gpuBox.SelectedIndexChanged += (_, _) => UpdateSummary();

gpuBox.DropDown += (_, _) =>
{
    int width = gpuBox.Width;

    using Graphics graphics = gpuBox.CreateGraphics();

    foreach (var item in gpuBox.Items)
    {
        int itemWidth = (int)Math.Ceiling(
            graphics.MeasureString(
                item?.ToString() ?? string.Empty,
                gpuBox.Font).Width);

        width = Math.Max(width, itemWidth + 40);
    }

    gpuBox.DropDownWidth = Math.Min(width, 500);
};
displayModeBox.SelectedIndexChanged += (_, _) => { UpdateRefreshRates(); UpdateSummary(); };
    }

    private void ToggleAdvanced()
    {
        if (compatibilityVisible)
        {
            compatibilityVisible = false;
            compatibilityPanel.Visible = false;
        }

        advancedVisible = !advancedVisible;
        advancedPanel.Visible = advancedVisible;
        advancedChevron.Text = advancedVisible ? "⌃" : "⌄";
        advancedButton.Active = advancedVisible;
        compatibilityButton.Active = compatibilityVisible;

        if (advancedVisible)
        {
            scaler.DesignHeight = 944;
        }
        else
        {
            scaler.DesignHeight = 720;
        }
    }

    private void ToggleCompatibility()
    {
        if (advancedVisible)
        {
            advancedVisible = false;
            advancedPanel.Visible = false;
            advancedChevron.Text = "⌄";
        }

        compatibilityVisible = !compatibilityVisible;
        compatibilityPanel.Visible = compatibilityVisible;
        compatibilityButton.Active = compatibilityVisible;
        advancedButton.Active = advancedVisible;

        if (compatibilityVisible)
        {
            scaler.DesignHeight = 1000;
        }
        else
        {
            scaler.DesignHeight = 720;
        }
    }

    private void MoveBottomControls(int secondaryY, int playY, int footerY)
    {
        foreach (Control control in Controls)
        {
            if (control is ModernButton button)
            {
                if (button.Text == "OPEN LOGS" || button.Text == "OPEN FOLDER")
                    button.Top = secondaryY;
                else if (button.Text == "PLAY")
                    button.Top = playY;
            }
            else if (control is Label label &&
                     (label.Text.StartsWith("ReStreet •", StringComparison.Ordinal) || label.Tag as string == "credit"))
            {
                label.Top = footerY;
            }
        }
    }

    private RoundedPanel CreateCard(Point location, Size size)
    {
        return new RoundedPanel
        {
            Location = location,
            Size = size,
            FillColor = Surface,
            BorderColor = Border,
            Radius = 16
        };
    }

    private void AddSectionHeader(Control parent, string title, string subtitle, int x, int y)
    {
        var titleLabel = new Label
        {
            Text = title,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(x, y)
        };
        parent.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = subtitle,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.8F),
            AutoSize = true,
            Location = new Point(x, y + 26)
        };
        parent.Controls.Add(subtitleLabel);
    }

    private void AddFieldLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new ThemedLabel
        {
            Text = text.ToUpperInvariant(),
            ForeColor = TextSecondary,
            Font = StreetTheme.Condensed(8.5F, FontStyle.Bold),
            Bounds = new Rectangle(x, y, ColW, 16)
        });
    }

    private void ConfigureCombo(ComboBox box, int x, int y, int width)
    {
        box.Bounds = new Rectangle(x, y, width, 36);
        box.DropDownHeight = 240;
    }

    private void ConfigureCheck(CheckBox box, string text, int x, int y)
    {
        box.Text = text;
        box.Location = new Point(x, y);
        box.AutoSize = true;
        box.ForeColor = TextPrimary;
        box.BackColor = Color.Transparent;
        box.Font = StreetTheme.Body(10F);
        box.Cursor = Cursors.Hand;
    }

    private void StyleTextBox(TextBox box)
    {
        box.BackColor = SurfaceAlt;
        box.ForeColor = TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 9F);
    }

    private void LoadDefaults()
    {
        // 1.5.5 layout: Binaries\ (exe, DLLs, Logs) and Game\ (the game's files); older installs had Game\ + GameData\.
        bool oldLayout = !Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Binaries")) &&
            Directory.Exists(Path.Combine(AppContext.BaseDirectory, "GameData"));
        exePathBox.Text =
            Path.Combine(AppContext.BaseDirectory, oldLayout ? "Game" : "Binaries", "fifastreet.exe");

        gamePathBox.Text =
            Path.Combine(AppContext.BaseDirectory, oldLayout ? "GameData" : "Game");

        displayModeBox.Items.AddRange(new object[]
        {
            "Fullscreen",
            "Windowed"
        });
        displayModeBox.SelectedIndex = 0;

        graphicsApiBox.Items.AddRange(new object[]
        {
            "Direct3D 12",
            "Vulkan"
        });
        graphicsApiBox.SelectedIndex = 0;
        gameLanguageBox.Items.AddRange(new object[] { "English", "Español", "Français", "Deutsch", "Italiano", "Português (Brasil)" });
        gameLanguageBox.SelectedIndex = 0;

        readbackResolveBox.Items.AddRange(new object[]
        {
            "Full (compatible)",
            "Some (recommended)",
            "Fast (more FPS, may glitch)",
            "None (experimental)"
        });
        readbackResolveBox.SelectedIndex = 1;

        monitorBox.Items.Clear();
        foreach (var screen in Screen.AllScreens)
        {
            string primary = screen.Primary ? " • Primary" : "";
            monitorBox.Items.Add(
                $"{monitorBox.Items.Count}: {screen.DeviceName}{primary}"
            );
        }

        if (monitorBox.Items.Count > 0)
            monitorBox.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, screen => screen.Primary));

        DetectDisplayModes();
        DetectGpus();

        postEffectBox.Items.AddRange(new object[]
{
    "None",
    "FXAA",
    "FXAA Extreme"
});
       postEffectBox.SelectedIndex = 1;

       internalResolutionBox.Items.AddRange(new object[]
{
       "1x - Native (1280 x 720)",
       "2x - 2560 x 1440",
       "3x - 3840 x 2160",
       "4x - 5120 x 2880"
});
       internalResolutionBox.SelectedIndex = 0;


        readbackMemexportBox.Checked = true;
        readbackMemexportFastBox.Checked = true;
        clearMemoryPageStateBox.Checked = false;
        logFrameStatsBox.Checked = false;
        occlusionQueryBox.Checked = false;
        asyncShadersBox.Checked = true;

        vsyncBox.Checked = false;
        vrrBox.Checked = true;
        msaaBox.Checked = false;
    }

    private void DetectGpus()
    {
        gpuBox.Items.Clear();
        gpuAdapterIndices.Clear();
        gpuBox.Items.Add("Automatic");
        gpuAdapterIndices.Add(-1);

        try
        {
            foreach (var gpu in DxgiAdapters.Enumerate())
            {
                gpuBox.Items.Add(
                    $"Adapter {gpu.Index}: {gpu.Name}"
                );
                gpuAdapterIndices.Add(checked((int)gpu.Index));
            }
        }
        catch
        {
            // Automatic selection remains available when DXGI enumeration fails.
        }

        gpuBox.Items.Add("WARP (Software)");
        gpuAdapterIndices.Add(-2);
        gpuBox.SelectedIndex = 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;

        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(
        string deviceName,
        int modeNum,
        ref DEVMODE devMode
    );

    private const int ENUM_CURRENT_SETTINGS = -1;

    private void DetectDisplayModes()
    {
        string? oldResolution = resolutionBox.SelectedItem?.ToString();
        string? oldRefresh = refreshBox.SelectedItem?.ToString();

        resolutionBox.Items.Clear();
        refreshBox.Items.Clear();

        var resolutions = new HashSet<string>();
        var refreshRates = new SortedSet<int>();

        Screen screen =
            monitorBox.SelectedIndex >= 0 &&
            monitorBox.SelectedIndex < Screen.AllScreens.Length
                ? Screen.AllScreens[monitorBox.SelectedIndex]
                : Screen.PrimaryScreen!;

        int modeNum = 0;

        while (true)
        {
            DEVMODE mode = CreateDevMode();

            if (!EnumDisplaySettings(
                    screen.DeviceName,
                    modeNum,
                    ref mode))
            {
                break;
            }

            if (mode.dmPelsWidth >= 1280 &&
                mode.dmPelsHeight >= 720 &&
                mode.dmBitsPerPel >= 32)
            {
                resolutions.Add(
                    $"{mode.dmPelsWidth}x{mode.dmPelsHeight}"
                );

                if (mode.dmDisplayFrequency > 20 &&
                    mode.dmDisplayFrequency < 1000)
                {
                    refreshRates.Add(mode.dmDisplayFrequency);
                }
            }

            modeNum++;
        }

        // Always expose common output resolutions up to 4K,
        // even when the current monitor does not advertise them.
        resolutions.Add("1280x720");
        resolutions.Add("1920x1080");
        resolutions.Add("2560x1440");
        resolutions.Add("3200x1800");
        resolutions.Add("3840x2160");

        foreach (string resolution in resolutions
                     .OrderBy(x =>
                     {
                         var p = x.Split('x');
                         return int.Parse(p[0]) * int.Parse(p[1]);
                     }))
        {
            resolutionBox.Items.Add(resolution);
        }

        foreach (int hz in refreshRates)
        {
            refreshBox.Items.Add(hz.ToString());
        }

        if (resolutionBox.Items.Count == 0)
        {
            resolutionBox.Items.AddRange(new object[]
            {
                "1280x720",
                "1600x900",
                "1920x1080",
                "2560x1440",
                "3200x1800",
                "3840x2160"
            });
        }

        if (refreshBox.Items.Count == 0)
        {
            refreshBox.Items.AddRange(new object[]
            {
                "30", "50", "60", "75", "100",
                "120", "144", "165", "180", "200", "240"
            });
        }

        if (!string.IsNullOrWhiteSpace(oldResolution) &&
            resolutionBox.Items.Contains(oldResolution))
        {
            resolutionBox.SelectedItem = oldResolution;
        }
        else
        {
            string currentResolution =
                $"{screen.Bounds.Width}x{screen.Bounds.Height}";

            if (resolutionBox.Items.Contains(currentResolution))
                resolutionBox.SelectedItem = currentResolution;
            else if (resolutionBox.Items.Count > 0)
                resolutionBox.SelectedIndex = resolutionBox.Items.Count - 1;
        }

        if (!string.IsNullOrWhiteSpace(oldRefresh) &&
            refreshBox.Items.Contains(oldRefresh))
        {
            refreshBox.SelectedItem = oldRefresh;
        }
        else
        {
            DEVMODE currentMode = CreateDevMode();

            if (EnumDisplaySettings(
                    screen.DeviceName,
                    ENUM_CURRENT_SETTINGS,
                    ref currentMode))
            {
                string currentHz =
                    currentMode.dmDisplayFrequency.ToString();

                if (refreshBox.Items.Contains(currentHz))
                    refreshBox.SelectedItem = currentHz;
            }

            if (refreshBox.SelectedIndex < 0 &&
                refreshBox.Items.Count > 0)
            {
                refreshBox.SelectedIndex = 0;
            }
        }
    }

    private void UpdateRefreshRates()
    {
        if (monitorBox.SelectedIndex < 0 || resolutionBox.SelectedIndex < 0) return;
        string previous = refreshBox.Text;
        Screen screen = Screen.AllScreens[monitorBox.SelectedIndex];
        DEVMODE desktop = CreateDevMode();
        if (!EnumDisplaySettings(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref desktop)) return;
        string[] size = resolutionBox.Text.Split('x');
        if (size.Length != 2 || !int.TryParse(size[0], out int width) ||
            !int.TryParse(size[1], out int height)) return;
        if (displayModeBox.SelectedIndex != 0) {
            width = desktop.dmPelsWidth;
            height = desktop.dmPelsHeight;
        }
        var rates = new SortedSet<int>();
        for (int i = 0; ; i++) {
            DEVMODE mode = CreateDevMode();
            if (!EnumDisplaySettings(screen.DeviceName, i, ref mode)) break;
            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height &&
                mode.dmBitsPerPel >= 32 && mode.dmDisplayFrequency > 20)
                rates.Add(mode.dmDisplayFrequency);
        }
        refreshBox.Items.Clear();
        foreach (int rate in rates) refreshBox.Items.Add(rate.ToString());
        if (refreshBox.Items.Contains(previous)) refreshBox.SelectedItem = previous;
        else if (refreshBox.Items.Contains(desktop.dmDisplayFrequency.ToString()))
            refreshBox.SelectedItem = desktop.dmDisplayFrequency.ToString();
        else if (refreshBox.Items.Count > 0) refreshBox.SelectedIndex = 0;
    }

    private static DEVMODE CreateDevMode()
    {
        return new DEVMODE
        {
            dmDeviceName = new string('\0', 32),
            dmFormName = new string('\0', 32),
            dmSize = (short)Marshal.SizeOf<DEVMODE>()
        };
    }

    private void UpdateGraphicsApiState()
    {
        bool isD3D12 = graphicsApiBox.SelectedIndex != 1;

        // GPU adapter selection is currently specific to the D3D12 backend.
        // Vulkan uses automatic device selection (REX_VULKAN_DEVICE=-1).
        gpuBox.Enabled = isD3D12;
        vrrBox.Enabled = isD3D12 && !vsyncBox.Checked;
        vrrBox.Text = "VRR / Tearing (D3D12)";
        readbackMemexportFastBox.Enabled = readbackMemexportBox.Checked;

        if (!isD3D12)
        {
            gpuBox.BackColor = Color.FromArgb(34, 38, 43);
        }
        else
        {
            gpuBox.BackColor = Color.FromArgb(24, 28, 32);
        }
    }
    private void UpdateSummary()
    {
        string gpu;

        if (graphicsApiBox.SelectedIndex == 1)
        {
            gpu = "Automatic GPU (Vulkan)";
        }
        else
        {
            gpu = gpuBox.SelectedItem?.ToString() ?? "Automatic GPU";
            if (gpu == "Automatic") gpu = "Automatic GPU";

            if (gpu.StartsWith("Adapter ", StringComparison.Ordinal))
            {
                int colon = gpu.IndexOf(':');
                if (colon >= 0 && colon + 1 < gpu.Length)
                    gpu = gpu[(colon + 1)..].Trim();
            }
        }

        string resolution = resolutionBox.SelectedItem?.ToString() ?? "Resolution";
        string hz = refreshBox.SelectedItem?.ToString() ?? "--";

        hardwareLabel.Text =
            $"{gpu}  ·  {resolution}  ·  {hz} Hz";
    }

    private void UpdateStatus(string text, bool ok)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = ok ? TextSecondary : Color.FromArgb(255, 120, 120);
    }

    // Direct play (--play): the window stays invisible, the game starts with the saved settings and the
    // launcher closes by itself after the game exits (it still restores the display mode first).
    public void StartDirect()
    {
        closeAfterGame = true;
        ShowInTaskbar = false;
        Opacity = 0;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Shown += async (_, _) =>
        {
            // The direct-play shortcut never showed an update (hidden window, the game already running): look first,
            // briefly, and when there is one open the launcher with the offer instead of the game.
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (await FindUpdateAsync(wait.Token))
            {
                closeAfterGame = false;
                ShowInTaskbar = true;
                Opacity = 1;
                CenterToScreen();
                Activate();
                await Task.Delay(1500);  // the page is loading in the window that just appeared
                await OfferUpdateAsync();
                return;
            }
            LaunchGame();
        };
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            SaveSettings();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string target = Environment.ProcessPath ?? Application.ExecutablePath;
            string path = Path.Combine(desktop, "ReStreet (direct play).lnk");
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows Script Host is not available.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.Arguments = "--play";
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.IconLocation = target + ",0";
            link.Description = "Start ReStreet directly with the settings saved in the launcher";
            link.Save();
            MessageBox.Show(
                "A shortcut was created on your desktop: ReStreet (direct play)." + Environment.NewLine + Environment.NewLine +
                "It starts the game straight away with the settings you have now. " +
                "To change settings later, open the launcher normally and click Shortcut again to refresh it.",
                "ReStreet Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not create the shortcut", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // A game from this install that the launcher did not start (opened from the exe): it holds rexruntime.dll, so
    // starting another one fails with "file in use".
    private Process? OtherGameFromHere()
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(exePathBox.Text.Trim())) ?? "";
        foreach (var p in Process.GetProcessesByName("fifastreet"))
        {
            try { if (p.Id != gameProcess?.Id && !p.HasExited && string.Equals(Path.GetDirectoryName(p.MainModule?.FileName), dir, StringComparison.OrdinalIgnoreCase)) return p; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            p.Dispose();
        }
        return null;
    }

    private async void LaunchGame()
    {
        if (gameRunning || OtherGameFromHere() is not null) {
            ShowError("ReStreet is already running.");
            return;
        }
        try
        {
            string exe = exePathBox.Text.Trim();
            string gameRoot = gamePathBox.Text.Trim();

            if (!File.Exists(exe))
            {
                ShowError("fifastreet.exe was not found.");
                return;
            }

            if (!Directory.Exists(gameRoot))
            {
                ShowError("The game data folder does not exist.");
                return;
            }

            if (!File.Exists(Path.Combine(gameRoot, "default.xex")))
            {
                MessageBox.Show(
                    "The selected folder does not contain default.xex.",
                    "Invalid folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            // Select the validated ReXGlue graphics backend.
            string graphicsApi = graphicsApiBox.SelectedIndex == 1
                ? "Vulkan"
                : "D3D12";

            string backendDirectory = Path.Combine(
                Path.GetDirectoryName(exe)!,
                "Backends",
                graphicsApi);

            string backendRuntime = Path.Combine(
                backendDirectory,
                "rexruntime.dll");

            string backendGpu = Path.Combine(
                backendDirectory,
                "rexgpu-xenos.dll");

            if (!File.Exists(backendRuntime) || !File.Exists(backendGpu))
            {
                MessageBox.Show(
                    $"The {graphicsApi} graphics backend is missing.\n\n" +
                    $"Expected files in:\n{backendDirectory}",
                    "ReStreet Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string executableDirectory = Path.GetDirectoryName(exe)!;

            // A game that has just been closed (room start) keeps the dlls locked for a moment: retry for up to 5 s.
            for (int attempt = 0; ; ++attempt)
            {
                try
                {
                    File.Copy(backendRuntime, Path.Combine(executableDirectory, "rexruntime.dll"), true);
                    File.Copy(backendGpu, Path.Combine(executableDirectory, "rexgpu-xenos.dll"), true);
                    break;
                }
                catch (IOException) when (attempt < 20) { await Task.Delay(250); }
            }

            var resolution = resolutionBox.Text.Split('x');
            if (resolution.Length != 2 ||
                !int.TryParse(resolution[0], out int width) ||
                !int.TryParse(resolution[1], out int height) ||
                width <= 0 || height <= 0)
            {
                ShowError("Select a valid resolution.");
                return;
            }
            string refresh = refreshBox.Text;

            bool fullscreen =
                displayModeBox.SelectedItem?.ToString() == "Fullscreen";

            int monitor = Math.Clamp(monitorBox.SelectedIndex, 0, Screen.AllScreens.Length - 1);

            int adapter = gpuBox.SelectedIndex >= 0
                ? gpuAdapterIndices[gpuBox.SelectedIndex] : -1;

            string postEffect = postEffectBox.SelectedIndex switch
            {
                1 => "fxaa",
                2 => "fxaa_extreme",
                _ => "none"
            };


            string logsDir = Path.Combine(
                Path.GetDirectoryName(exe)!,
                "Logs"
            );

            Directory.CreateDirectory(logsDir);

            string logFile =
                Path.Combine(logsDir, "fifastreet.log");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false
            };

            // ReXGlue resolves each setting in this order, lowest priority first:
            // built-in default -> fifastreet.toml -> REX_* environment -> command line.
            // The REX_* variables set here therefore win over the TOML file.

            startInfo.Environment["REX_GAME_DATA_ROOT"] = gameRoot;
            int internalResolutionScale = Math.Max(1, internalResolutionBox.SelectedIndex + 1);
            string readbackResolve = ReadbackResolveModes[Math.Clamp(readbackResolveBox.SelectedIndex, 0, ReadbackResolveModes.Length - 1)];
            bool deck = IsDeckBase(executableDirectory);
            if (deck)
            {
                ApplyDeckLaunch(startInfo, executableDirectory, graphicsApi, adapter);
                // Not in fifastreet.toml: the game language and the monitor go as variables, like on 1.5.
                ApplyGameLanguage(startInfo);
                startInfo.Environment["REX_MONITOR"] = (monitor + 1).ToString();
            }
            else
            {
            startInfo.Environment["REX_VIDEO_MODE_WIDTH"] = width.ToString();
            startInfo.Environment["REX_VIDEO_MODE_HEIGHT"] = height.ToString();
            // The display size does not change the Xbox 360 render targets.

startInfo.Environment["REX_DRAW_RESOLUTION_SCALE_X"] =
    internalResolutionScale.ToString();

startInfo.Environment["REX_DRAW_RESOLUTION_SCALE_Y"] =
    internalResolutionScale.ToString();
            startInfo.Environment["REX_WINDOW_WIDTH"] = width.ToString();
            startInfo.Environment["REX_WINDOW_HEIGHT"] = height.ToString();
            // Apply the selected rate to both the monitor and Xbox video timing.
            startInfo.Environment["REX_VIDEO_MODE_REFRESH_RATE"] = refresh;
            // VSync on: lift the game's own 40 FPS replay/cutscene cap up to the chosen rate.
            // VSync off: no frame limit at all (the game's own limiter is raised out of the way).
            startInfo.Environment["REX_FRAME_LIMITER_MIN_HZ"] = vsyncBox.Checked ? "auto" : "1000";
            // Keep the one synchronous readback that keeps stadium floors clean (default 1282B000), drop the
            // other small-range ones that held goals, replays and match setup below the chosen rate.
            startInfo.Environment.Remove("REX_READBACK_SOME_SYNC_ADDRS");
            startInfo.Environment["REX_READBACK_SOME_SYNC_BELOW"] = "0";
            ApplyGameLanguage(startInfo);
            // The game keeps its own clock in both modes (True VSync shows its newest frame at each refresh).
            startInfo.Environment["REX_GUEST_VBLANK_UNLOCKED"] = "true";
            startInfo.Environment["REX_FULLSCREEN"] =
                fullscreen ? "true" : "false";
            startInfo.Environment["REX_MONITOR"] = (monitor + 1).ToString();
            startInfo.Environment["REX_LAUNCHER_MONITOR_DEVICE"] = Screen.AllScreens[monitor].DeviceName;
            // "VSync" = True VSync: no tearing, the display shows the game's newest frame at every refresh of the
            // chosen rate (any rate), and the FPS counter shows displayed frames like GPU overlays do. The old
            // blocking host VSync (REX_VSYNC) stays off: it made every GPU wait cost two refreshes.
            startInfo.Environment["REX_VSYNC"] = "false";
            startInfo.Environment["REX_TRUE_VSYNC"] = vsyncBox.Checked ? "1" : "0";
            startInfo.Environment["REX_NATIVE_2X_MSAA"] =
                msaaBox.Checked ? "true" : "false";
            startInfo.Environment["REX_SWAP_POST_EFFECT"] = postEffect;

            if (graphicsApi == "D3D12")
            {
                // Validated D3D12 configuration.
                startInfo.Environment["REX_D3D12_ALLOW_VARIABLE_REFRESH_RATE_AND_TEARING"] =
                    !vsyncBox.Checked && vrrBox.Checked ? "true" : "false";
                startInfo.Environment["REX_D3D12_ADAPTER"] = adapter.ToString();

                // Do not force ROV/RTV. The validated D3D12 build uses
                // ReXGlue's automatic render target path selection.
                startInfo.Environment["REX_RENDER_TARGET_PATH_D3D12"] = "";
            }
            else
            {
                // Validated Vulkan configuration.
                // Vulkan device stays automatic; DXGI adapter indices are
                // not interchangeable with Vulkan device indices.
                startInfo.Environment["REX_VULKAN_DEVICE"] = "-1";
                startInfo.Environment["REX_RENDER_TARGET_PATH_VULKAN"] = "fbo";
                // Vulkan-only image settings that remove the goal-replay blur
                // (user-tested together on AMD; not bisected to a single one).
                startInfo.Environment["REX_DRAW_RESOLUTION_SCALED_TEXTURE_OFFSETS"] = "false";
                startInfo.Environment["REX_DEPTH_FLOAT24_CONVERT_IN_PIXEL_SHADER"] = "true"; // needed: false (or float24 round) blurs goal replays
                startInfo.Environment["REX_GAMMA_RENDER_TARGET_AS_UNORM16"] = "false";
                startInfo.Environment["REX_SNORM16_RENDER_TARGET_FULL_RANGE"] = "false";
                startInfo.Environment["REX_VULKAN_DYNAMIC_RENDERING"] = "false";
                startInfo.Environment["REX_HALF_PIXEL_OFFSET"] = "false";
                startInfo.Environment["REX_ANISOTROPIC_OVERRIDE"] = "-1";
                startInfo.Environment["REX_DIRECT_HOST_RESOLVE"] = "false"; // true leaves some court floors black
                // Override legacy TOML values: checked means FIFO, unchecked
                // requests immediate presentation without display synchronization.
                // With VSync on, prefer MAILBOX (latest frame wins, no tearing, never blocks the queue).
                startInfo.Environment["REX_VULKAN_ALLOW_PRESENT_MODE_IMMEDIATE"] =
                    vsyncBox.Checked ? "false" : "true";
                startInfo.Environment["REX_VULKAN_ALLOW_PRESENT_MODE_MAILBOX"] =
                    vsyncBox.Checked ? "true" : "false";
                startInfo.Environment["REX_VULKAN_ALLOW_PRESENT_MODE_FIFO_RELAXED"] =
                    vsyncBox.Checked ? "false" : "true";
            }

            // Compatibilidade / coerência GPU.
            startInfo.Environment["REX_READBACK_RESOLVE"] = readbackResolve;
            // Keep synchronous Full Readback; reduce scheduler round trips for
            // short GPU work without reading before its fence is signalled.
            startInfo.Environment["REX_D3D12_READBACK_SHORT_WAIT"] = "true";
            startInfo.Environment["REX_D3D12_READBACK_SPIN_US"] = "500";
            startInfo.Environment["REX_VULKAN_READBACK_SHORT_WAIT"] = "true";
            startInfo.Environment["REX_VULKAN_READBACK_SPIN_US"] = "500";
            startInfo.Environment["REX_READBACK_MEMEXPORT"] =
                readbackMemexportBox.Checked ? "true" : "false";
            startInfo.Environment[graphicsApi == "Vulkan"
                ? "REX_VULKAN_READBACK_MEMEXPORT" : "REX_D3D12_READBACK_MEMEXPORT"] =
                readbackMemexportBox.Checked ? "true" : "false";
            startInfo.Environment["REX_READBACK_MEMEXPORT_FAST"] =
                readbackMemexportBox.Checked && readbackMemexportFastBox.Checked ? "true" : "false";
            startInfo.Environment["REX_CLEAR_MEMORY_PAGE_STATE"] =
                clearMemoryPageStateBox.Checked ? "true" : "false";
            startInfo.Environment["REX_OCCLUSION_QUERY_ENABLE"] =
                occlusionQueryBox.Checked ? "true" : "false";
            startInfo.Environment["REX_ASYNC_SHADER_COMPILATION"] =
                asyncShadersBox.Checked ? "true" : "false";
            }
            ApplyControls(startInfo);

            startInfo.Environment["REX_LOG_VERBOSE"] = "false";
            startInfo.Environment["REX_LOG_LEVEL"] = "info";
            startInfo.Environment["REX_LOG_FRAME_STATS"] = logFrameStatsBox.Checked ? "true" : "false";
            startInfo.Environment["REX_BIND_DEBUG_OVERLAY"] = "Home";
            startInfo.Environment["REX_LOG_FILE"] = logFile;
            // Performance report (FPS per scene), read back after the session: see PerfReport.cs.
            string perfReport = Path.Combine(logsDir, "perf-report.json");
            try { File.Delete(perfReport); } catch (IOException) { }
            startInfo.Environment["REX_PERF_REPORT"] = perfReport;
            var perfSettings = new Dictionary<string, object?>
            {
                ["api"] = graphicsApi, ["profile"] = profileBox.Text, ["resolution"] = $"{width}x{height}",
                ["refresh"] = refresh, ["fullscreen"] = fullscreen, ["internal_scale"] = internalResolutionScale,
                ["vsync"] = vsyncBox.Checked, ["post"] = postEffect, ["msaa"] = msaaBox.Checked,
                ["readback"] = deck ? "deck" : readbackResolve,
            };

            // Online play: the server and the player name come from the online patch.
            var online = LoadOnlineConfig();
            if (online != null && onlineModeBox.Checked)
            {
                startInfo.Environment["FIFASTREET_SERVER"] = online.Value.Server;
                startInfo.Environment["FIFASTREET_PLAYER_NAME"] = online.Value.Name;
                startInfo.Environment["FIFASTREET_PLAYER_KEY"] = online.Value.Key;
                startInfo.Environment["FIFASTREET_ONLINE_EXPERIMENT"] = "1";
                startInfo.Environment["FIFASTREET_ONLINE_ENDPOINT_PROBE"] = "1";
                startInfo.Environment["FIFASTREET_NET_DIAG"] = "";
                if (!logFrameStatsBox.Checked) startInfo.Environment["REX_LOG_LEVEL"] = "warn";
            }

            // Mantemos game_data_root também como argumento porque esta
            // aplicação já confirmou que o aceita diretamente.
            startInfo.ArgumentList.Add($"--game_data_root={gameRoot}");

            // Settings and the diagnostic summary are conveniences: a read-only install folder must not
            // prevent the game from starting.
            try { SaveSettings(); }
            catch (IOException ex) { Debug.WriteLine(ex.Message); }

            string launcherDebug = Path.Combine(
                logsDir,
                "launcher_settings.txt"
            );

            try {
            File.WriteAllText(
                launcherDebug,
                $"Resolution={width}x{height}{Environment.NewLine}" +
                $"InternalResolution={1280 * internalResolutionScale}x{720 * internalResolutionScale} ({internalResolutionScale}x){Environment.NewLine}" +
                $"MonitorRefreshRate={refresh}{Environment.NewLine}" +
                $"GuestRefreshRate={refresh}{Environment.NewLine}" +
                $"GuestVblankUnlocked={!vsyncBox.Checked}{Environment.NewLine}" +
                $"GraphicsApi={graphicsApi}{Environment.NewLine}" +
                $"Fullscreen={fullscreen}{Environment.NewLine}" +
                $"Monitor={monitor}{Environment.NewLine}" +
                $"VSync={vsyncBox.Checked}{Environment.NewLine}" +
                $"VRRRequested={vrrBox.Checked}; VRREffective={graphicsApi == "D3D12" && !vsyncBox.Checked && vrrBox.Checked}{Environment.NewLine}" +
                $"MSAA={msaaBox.Checked}{Environment.NewLine}" +
                $"PostEffect={postEffect}{Environment.NewLine}" +
                $"Adapter={adapter}{Environment.NewLine}" +
                $"ReadbackResolve={readbackResolve}{Environment.NewLine}" +
                $"ReadbackMemexport={readbackMemexportBox.Checked}{Environment.NewLine}" +
                $"ReadbackMemexportFast={readbackMemexportBox.Checked && readbackMemexportFastBox.Checked}{Environment.NewLine}" +
                $"ClearMemoryPageState={clearMemoryPageStateBox.Checked}{Environment.NewLine}" +
                $"OcclusionQueries={occlusionQueryBox.Checked}{Environment.NewLine}" +
                $"AsyncShaders={asyncShadersBox.Checked}{Environment.NewLine}"
            );
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }

            // Discord live state: the game DLL writes "mode=<key>" to this file (presence_state.cpp).
            // A file left over from the last session would show a stale mode, so start from none.
            var presenceConfig = PresenceConfig.Load(AppContext.BaseDirectory);
            if (presenceConfig.Usable && ResolveStateFile(presenceConfig.StateFile) is { } presenceFile)
            {
                try { File.Delete(presenceFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex.Message); }
                startInfo.Environment["FIFASTREET_PRESENCE_FILE"] = presenceFile;
            }

            UpdateStatus("Starting ReStreet...", true);

            if (!deck) ApplyMonitorRefresh(monitor, refresh, fullscreen ? width : 0, fullscreen ? height : 0);
            EnsureStadiumThumbs(Path.GetDirectoryName(exe)!, gameRoot);
            var started = DateTime.UtcNow;
            using var process = Process.Start(startInfo);
            gameRunning = process != null;
            gameProcess = process;

            UpdateStatus(
                process != null
                    ? $"Running • PID {process.Id}"
                    : "Could not start the game",
                process != null
            );
            if (process != null) {
                StartPresence(process.Id, graphicsApi, width, height, refresh,
                              internalResolutionScale, gpuBox.Text,
                              GameLanguageIds[Math.Max(0, gameLanguageBox.SelectedIndex)]);
                await process.WaitForExitAsync();
                OnSessionEnded(perfReport, logFile, perfSettings, process.ExitCode);
                UpdateStatus(ReadyText(), true);
                SuggestD3D12AfterVulkanCrash(graphicsApi, process.ExitCode, DateTime.UtcNow - started);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Launch failed", false);

            MessageBox.Show(
                ex.Message,
                "Error starting ReStreet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally {
            StopPresence();
            RestoreMonitorRefresh();
            gameRunning = false;
            gameProcess = null;
            if (closeAfterGame) Close();
        }
    }

    // ---- Discord Rich Presence (ReStreet presence v2) --------------------------
    // Off unless presence.json exists next to the launcher and names a Discord
    // application id. Everything is wrapped: a missing Discord, a broken config or
    // a closed pipe can never stop the game from starting or keep the launcher open.

    private void StartPresence(int pid, string graphicsApi, int width, int height,
                               string refresh, int internalScale, string gpuName,
                               int gameLanguageId)
    {
        try
        {
            var config = PresenceConfig.Load(AppContext.BaseDirectory);
            if (!config.Usable) return;

            // The card follows the game language chosen in the launcher.
            var text = config.Resolve(PresenceConfig.LanguageCode(gameLanguageId));
            string apiName = graphicsApi == "D3D12" ? "Direct3D 12" : "Vulkan";

            string Fill(string template) => template
                .Replace("{api}", apiName)
                .Replace("{width}", width.ToString())
                .Replace("{height}", height.ToString())
                .Replace("{refresh}", refresh)
                .Replace("{internal}", internalScale + "x")
                .Replace("{profile}", profileBox.Text)
                .Replace("{gpu}", gpuName);

            var started = DateTimeOffset.UtcNow;
            string? stateFilePath = ResolveStateFile(config.StateFile);
            var stateFile = new PresenceStateFile(stateFilePath);

            DiscordActivity Compose()
            {
                stateFile.Poll();

                // The game side writes a canonical mode name (world_tour), never a
                // translated one, so it never has to know the player's language.
                string? live = stateFile.State;
                if (live is null && stateFile.Mode is { Length: > 0 } mode)
                {
                    live = text.Modes is not null && text.Modes.TryGetValue(mode, out var label)
                        ? label
                        : mode.Replace('_', ' ');
                }

                return new DiscordActivity
                {
                    Details = stateFile.Details ?? Fill(text.Details ?? ""),
                    State = live ?? Fill(text.State ?? ""),
                    Start = started,
                    LargeImage = config.LargeImage,
                    LargeText = Fill(text.LargeText ?? ""),
                    SmallImage = graphicsApi == "D3D12"
                        ? config.SmallImageD3D12 : config.SmallImageVulkan,
                    SmallText = apiName,
                    Buttons = config.Buttons.ConvertAll(
                        b => new DiscordButton { Label = b.Label, Url = b.Url })
                };
            }

            discord = new DiscordPresence(config.ApplicationId!.Trim(), pid);
            discord.Set(Compose());

            if (stateFilePath is not null)
            {
                presenceTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                presenceTimer.Tick += (_, _) =>
                {
                    try { discord?.Set(Compose()); } catch { }
                };
                presenceTimer.Start();
            }
        }
        catch
        {
            discord = null;
        }
    }

    private void StopPresence()
    {
        try { presenceTimer?.Stop(); presenceTimer?.Dispose(); } catch { }
        presenceTimer = null;
        try { discord?.Dispose(); } catch { }
        discord = null;
    }

    private string? ResolveStateFile(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (Path.IsPathRooted(configured)) return configured;
        var exeDir = Path.GetDirectoryName(exePathBox.Text.Trim());
        return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, configured);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (gameRunning && e.CloseReason == CloseReason.UserClosing) {
            // Keep the launcher alive to restore the display after the game exits.
            closeAfterGame = true;
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName, ref DEVMODE mode, IntPtr window, uint flags, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "ChangeDisplaySettingsEx")]
    private static extern int ChangeDisplaySettingsExToRegistry(
        string deviceName, IntPtr mode, IntPtr window, uint flags, IntPtr parameter);

    private void ApplyMonitorRefresh(int monitor, string refresh, int width, int height)
    {
        const uint CDS_TEST = 2;
        const int DM_DISPLAYFREQUENCY = 0x00400000;
        if (monitor < 0 || monitor >= Screen.AllScreens.Length ||
            !int.TryParse(refresh, out int hz) || hz <= 1) {
            throw new InvalidOperationException("Select a valid monitor and refresh rate.");
        }
        string device = Screen.AllScreens[monitor].DeviceName;
        DEVMODE original = CreateDevMode();
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref original)) {
            throw new InvalidOperationException("Could not read the monitor's current display mode.");
        }
        bool changeResolution = width > 0 && height > 0;
        if (original.dmDisplayFrequency == hz &&
            (!changeResolution || (original.dmPelsWidth == width && original.dmPelsHeight == height))) return;
        DEVMODE requested = original;
        requested.dmDisplayFrequency = hz;
        requested.dmFields = DM_DISPLAYFREQUENCY;
        if (changeResolution) {
            requested.dmPelsWidth = width;
            requested.dmPelsHeight = height;
            requested.dmFields |= 0x00080000 | 0x00100000; // DM_PELSWIDTH | DM_PELSHEIGHT
        }
        int result = ChangeDisplaySettingsEx(device, ref requested, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (result != 0) {
            throw new InvalidOperationException(
                $"The selected resolution and {hz} Hz are not available on this monitor (Windows code {result}).");
        }
        // Dynamic change (not CDS_FULLSCREEN): a CDS_FULLSCREEN mode is tied to the launcher, and Windows dropped
        // it back to the desktop rate when the game window took focus (60 Hz chosen, 165 Hz measured in game).
        // It is not saved to the registry; RestoreMonitorRefresh puts the previous mode back after the game exits.
        result = ChangeDisplaySettingsEx(device, ref requested, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != 0) {
            throw new InvalidOperationException($"Windows could not apply {hz} Hz (code {result}).");
        }
        original.dmFields = requested.dmFields;
        previousDisplayMode = original;
        changedDisplay = device;
        DEVMODE actual = CreateDevMode();
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref actual) ||
            Math.Abs(actual.dmDisplayFrequency - hz) > 1 ||
            (changeResolution && (actual.dmPelsWidth != width || actual.dmPelsHeight != height))) {
            throw new InvalidOperationException("Windows did not apply the selected monitor refresh rate.");
        }
    }

    private void RestoreMonitorRefresh()
    {
        if (changedDisplay == null) return;
        string device = changedDisplay;
        changedDisplay = null;
        // Back to the mode saved in Windows display settings (the change above is dynamic and never saved);
        // fall back to the mode read before the change.
        int result = ChangeDisplaySettingsExToRegistry(device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != 0) {
            result = ChangeDisplaySettingsEx(device, ref previousDisplayMode, IntPtr.Zero, 0, IntPtr.Zero);
        }
        if (result != 0) {
            ShowError($"Could not restore the monitor refresh rate (Windows code {result}). Check Windows display settings.");
        }
    }

    private void ShowError(string message)
    {
        UpdateStatus("Check your settings", false);

        MessageBox.Show(
            message,
            "ReStreet Launcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
    }

    private void BrowseExe()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "FIFA Street executable|fifastreet.exe|Executables|*.exe"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            exePathBox.Text = dialog.FileName;
    }

    private void BrowseGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing default.xex"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            gamePathBox.Text = dialog.SelectedPath;
    }

    private void OpenLogs()
    {
        string exe = exePathBox.Text.Trim();

        if (!File.Exists(exe)) {
            ShowError("Select an existing game executable first.");
            return;
        }

        string logs = Path.Combine(
            Path.GetDirectoryName(exe)!,
            "Logs"
        );

        Directory.CreateDirectory(logs);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{logs}\"",
            UseShellExecute = true
        });
    }

    private void OpenGameFolder()
    {
        string exe = exePathBox.Text.Trim();

        if (!File.Exists(exe)) {
            ShowError("Select an existing game executable first.");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{Path.GetDirectoryName(exe)}\"",
            UseShellExecute = true
        });
    }

    // post effect: 0 none, 1 FXAA, 2 FXAA extreme; internal resolution index = scale - 1;
    // readback: 0 full, 1 some (default since v1.1), 2 fast
    private void ApplyProfile()
    {
        if (applyingProfile)
            return;
        (int post, int scale, bool msaa, int readback, bool occlusion)? values = profileBox.SelectedIndex switch
        {
            0 => (1, 0, false, 1, false),   // Balanced: the shipped defaults (Some readback)
            1 => (2, 1, true, 1, false),    // Quality: 2x internal resolution, FXAA extreme, MSAA (Some readback)
            2 => (0, 0, false, 2, false),   // Performance: native resolution, no post, faster readback
            _ => null
        };
        if (values == null)
            return;
        applyingProfile = true;
        try
        {
            postEffectBox.SelectedIndex = values.Value.post;
            internalResolutionBox.SelectedIndex = values.Value.scale;
            msaaBox.Checked = values.Value.msaa;
            readbackResolveBox.SelectedIndex = values.Value.readback;
            occlusionQueryBox.Checked = values.Value.occlusion;
        }
        finally
        {
            applyingProfile = false;
        }
    }

    private void MarkCustomProfile()
    {
        if (!applyingProfile && profileBox.SelectedIndex != ProfileNames.Length - 1)
        {
            applyingProfile = true;
            profileBox.SelectedIndex = ProfileNames.Length - 1;
            applyingProfile = false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new LauncherSettings
            {
                ExePath = exePathBox.Text,
                GamePath = gamePathBox.Text,
                Resolution = resolutionBox.Text,
                RefreshRate = refreshBox.Text,
                Fullscreen = displayModeBox.SelectedIndex == 0,
                Monitor = monitorBox.SelectedIndex,
                GraphicsApi = graphicsApiBox.SelectedIndex,
                GameLanguage = GameLanguageIds[Math.Max(0, gameLanguageBox.SelectedIndex)],
                Gpu = gpuBox.SelectedIndex,
                GpuUsesDxgi = true,
                PostEffect = postEffectBox.SelectedIndex,
                InternalResolutionScale = internalResolutionBox.SelectedIndex,
                ReadbackResolve = readbackResolveBox.SelectedIndex,
                LogFrameStats = logFrameStatsBox.Checked,
                ReadbackMemexport = readbackMemexportBox.Checked,
                ReadbackMemexportFast = readbackMemexportFastBox.Checked,
                ClearMemoryPageState = clearMemoryPageStateBox.Checked,
                OcclusionQueries = occlusionQueryBox.Checked,
                AsyncShaders = asyncShadersBox.Checked,
                VSync = vsyncBox.Checked,
                VRR = vrrBox.Checked,
                MSAA = msaaBox.Checked,
                Profile = profileBox.SelectedIndex,
                OnlineMode = onlineModeBox.Checked
            };

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            throw new IOException("Could not save launcher settings: " + ex.Message, ex);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;

            var settings =
                JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(SettingsPath)
                );

            if (settings == null)
                return;
            onlineModeBox.Checked = settings.OnlineMode;
            int languageIndex = Array.IndexOf(GameLanguageIds, settings.GameLanguage);
            gameLanguageBox.SelectedIndex = Math.Max(0, languageIndex);

            // A saved path that no longer exists (the folders moved to the 1.5.5 layout) gives way to the default.
            if (settings.ExePath != null && File.Exists(settings.ExePath)) exePathBox.Text = settings.ExePath;
            if (settings.GamePath != null && Directory.Exists(settings.GamePath)) gamePathBox.Text = settings.GamePath;

            displayModeBox.SelectedIndex =
                settings.Fullscreen ? 0 : 1;

            if (settings.Monitor >= 0 &&
                settings.Monitor < monitorBox.Items.Count)
            {
                monitorBox.SelectedIndex = settings.Monitor;
            }

            DetectDisplayModes();

            SelectItem(resolutionBox, settings.Resolution);
            SelectItem(refreshBox, settings.RefreshRate);

            if (settings.GraphicsApi >= 0 &&
                settings.GraphicsApi < graphicsApiBox.Items.Count)
            {
                graphicsApiBox.SelectedIndex = settings.GraphicsApi;
            }

            if (settings.GpuUsesDxgi && settings.Gpu >= 0 &&
                settings.Gpu < gpuBox.Items.Count)
            {
                gpuBox.SelectedIndex = settings.Gpu;
            }

            if (settings.PostEffect >= 0 &&
                settings.PostEffect < postEffectBox.Items.Count)
            {
                postEffectBox.SelectedIndex = settings.PostEffect;
            }
if (settings.InternalResolutionScale >= 0 &&
    settings.InternalResolutionScale < internalResolutionBox.Items.Count)
{
    internalResolutionBox.SelectedIndex = settings.InternalResolutionScale;
}

            if (settings.ReadbackResolve >= 0 &&
                settings.ReadbackResolve < readbackResolveBox.Items.Count)
                readbackResolveBox.SelectedIndex = settings.ReadbackResolve;
            logFrameStatsBox.Checked = settings.LogFrameStats;
            readbackMemexportBox.Checked = settings.ReadbackMemexport;
            readbackMemexportFastBox.Checked = settings.ReadbackMemexportFast;
            clearMemoryPageStateBox.Checked = settings.ClearMemoryPageState;
            occlusionQueryBox.Checked = settings.OcclusionQueries;
            asyncShadersBox.Checked = settings.AsyncShaders;

            vsyncBox.Checked = settings.VSync;
            vrrBox.Checked = settings.VRR;
            msaaBox.Checked = settings.MSAA;
            if (settings.Profile >= 0 && settings.Profile < profileBox.Items.Count)
                profileBox.SelectedIndex = settings.Profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupt settings fall back to the defaults.
            Debug.WriteLine("Could not load launcher settings: " + ex.Message);
        }
    }

    private static void SelectItem(ComboBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        int index = box.Items.IndexOf(value);

        if (index >= 0)
            box.SelectedIndex = index;
    }

    private void ApplyGameLanguage(ProcessStartInfo startInfo)
    {
        int id = GameLanguageIds[Math.Max(0, gameLanguageBox.SelectedIndex)];
        startInfo.Environment["REX_USER_LANGUAGE"] = (id == PtBrLanguageId ? 1 : id).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (id == PtBrLanguageId) startInfo.Environment["FIFASTREET_GAME_LANGUAGE"] = "pt-BR";
        else startInfo.Environment.Remove("FIFASTREET_GAME_LANGUAGE");
    }

    private sealed class MessiPicture : PictureBox
    {
        private const float CropX = 100f / 1280f;
        private const float CropY = 225f / 1819f;
        private const float CropWidth = 640f / 1280f;

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            e.Graphics.Clear(Color.FromArgb(10, 12, 16));
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float sourceX = Image.Width * CropX;
            float sourceY = Image.Height * CropY;
            float sourceWidth = Image.Width * CropWidth;

            // Preserve the same horizontal framing. Expanding the launcher
            // reveals more of the source image vertically without stretching.
            float scale = ClientSize.Width / sourceWidth;
            float sourceHeight = ClientSize.Height / scale;
            float availableHeight = Image.Height - sourceY;
            sourceHeight = Math.Min(sourceHeight, availableHeight);

            var sourceRect = new RectangleF(sourceX, sourceY, sourceWidth, sourceHeight);
            float destinationHeight = sourceHeight * scale;
            var destinationRect = new RectangleF(0, 0, ClientSize.Width, destinationHeight);

            e.Graphics.DrawImage(Image, destinationRect, sourceRect, GraphicsUnit.Pixel);
        }
    }
}

public class LauncherSettings
{
    public string? ExePath { get; set; }
    public string? GamePath { get; set; }
    public string? Resolution { get; set; }
    public string? RefreshRate { get; set; }
    public bool Fullscreen { get; set; } = true;
    public int Monitor { get; set; }
    public int GraphicsApi { get; set; } = 0;
    public int GameLanguage { get; set; } = 1;
    public int Gpu { get; set; }
    public bool GpuUsesDxgi { get; set; }
    public int PostEffect { get; set; } = 1;
    public int ReadbackResolve { get; set; } = 0;
    public bool LogFrameStats { get; set; } = false;
    public int InternalResolutionScale { get; set; } = 0;
    public bool ReadbackMemexport { get; set; } = true;
    public bool ReadbackMemexportFast { get; set; } = true;
    public bool ClearMemoryPageState { get; set; } = false;
    public bool OcclusionQueries { get; set; } = true;
    public bool AsyncShaders { get; set; } = true;
    public bool VSync { get; set; } = false;
    public bool VRR { get; set; } = true;
    public bool MSAA { get; set; } = false;
    public int Profile { get; set; } = 0;
    public bool OnlineMode { get; set; } = true;
}

public class RoundedPanel : Panel, IFlatPanel
{
    public Color FillColor { get; set; } = Color.FromArgb(18, 22, 28);
    public Color BorderColor { get; set; } = Color.FromArgb(48, 57, 68);
    public int Radius { get; set; } = 16;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var path = CreateRoundedRectangle(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(BorderColor, 1F);

        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        int diameter = radius * 2;

        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);

        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();

        return path;
    }
}

public class ModernButton : Button
{
    public Color FillColor { get; set; } = Color.FromArgb(24, 29, 36);
    public Color HoverColor { get; set; } = Color.FromArgb(34, 41, 50);
    public Color BorderColor { get; set; } = Color.FromArgb(48, 57, 68);
    public int Radius { get; set; } = 10;

    private bool hovered;

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        DoubleBuffered = true;
        BackColor = Color.Transparent;

        MouseEnter += (_, _) =>
        {
            hovered = true;
            Invalidate();
        };

        MouseLeave += (_, _) =>
        {
            hovered = false;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var path = CreateRoundedRectangle(rect, Radius);
        using var fill = new SolidBrush(hovered ? HoverColor : FillColor);
        using var border = new Pen(BorderColor, 1F);

        pevent.Graphics.FillPath(fill, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis
        );
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        int diameter = radius * 2;

        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);

        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();

        return path;
    }
}
