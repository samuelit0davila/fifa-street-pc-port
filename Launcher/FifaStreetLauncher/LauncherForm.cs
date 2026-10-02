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

namespace FifaStreetLauncher;

public class LauncherForm : Form
{
    private readonly Color Bg = Color.FromArgb(10, 12, 16);
    private readonly Color Surface = Color.FromArgb(18, 22, 28);
    private readonly Color SurfaceAlt = Color.FromArgb(24, 29, 36);
    private readonly Color Border = Color.FromArgb(48, 57, 68);
    private readonly Color Accent = Color.FromArgb(133, 255, 72);
    private readonly Color AccentHover = Color.FromArgb(111, 229, 53);
    private readonly Color TextPrimary = Color.FromArgb(245, 247, 250);
    private readonly Color TextSecondary = Color.FromArgb(151, 161, 175);

    private readonly ComboBox resolutionBox = new();
    private readonly ComboBox refreshBox = new();
    private readonly ComboBox displayModeBox = new();
    private readonly ComboBox monitorBox = new();
    private readonly ComboBox graphicsApiBox = new();
    private readonly ComboBox gpuBox = new();
    private readonly List<int> gpuAdapterIndices = new();
    private readonly ComboBox postEffectBox = new();
    private readonly ComboBox internalResolutionBox = new();

    private readonly CheckBox vsyncBox = new();
    private readonly CheckBox vrrBox = new();
    private readonly CheckBox msaaBox = new();
    private readonly CheckBox readbackMemexportBox = new();
    private readonly CheckBox readbackMemexportFastBox = new();
    private readonly CheckBox clearMemoryPageStateBox = new();
    private readonly CheckBox occlusionQueryBox = new();
    private readonly CheckBox asyncShadersBox = new();

    private readonly TextBox gamePathBox = new();
    private readonly TextBox exePathBox = new();

    private readonly Label statusLabel = new();
    private readonly Label hardwareLabel = new();
    private readonly Label advancedChevron = new();

    private readonly RoundedPanel advancedPanel = new();
    private readonly ModernButton advancedButton = new();
    private readonly RoundedPanel compatibilityPanel = new();
    private readonly ModernButton compatibilityButton = new();

    private bool advancedVisible;
    private bool compatibilityVisible;

    private string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public LauncherForm()
    {
        Text = "FIFA Street Launcher";
        using (var icon = typeof(LauncherForm).Assembly.GetManifestResourceStream("fifastreet.ico")!)
            Icon = new Icon(icon);
        ClientSize = new Size(1250, 720);
        MinimumSize = new Size(1266, 759);
        MaximumSize = new Size(1266, 960);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        WireEvents();
        LoadDefaults();
        LoadSettings();
        UpdateSummary();
        UpdateStatus("Ready to play", true);
    }

    private void BuildInterface()
    {
        SuspendLayout();

        var brand = new Label
        {
            Text = "FIFA STREET",
            Font = new Font("Segoe UI", 30F, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(40, 30)
        };
        Controls.Add(brand);

        var subtitle = new Label
        {
            Text = "NATIVE PC RECOMP",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(44, 84)
        };
        Controls.Add(subtitle);

        var topLine = new Panel
        {
            BackColor = Accent,
            Location = new Point(40, 112),
            Size = new Size(860, 2)
        };
        Controls.Add(topLine);

        var readyDot = new Label
        {
            Text = "●",
            ForeColor = Accent,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(726, 45)
        };
        Controls.Add(readyDot);

        statusLabel.AutoSize = true;
        statusLabel.Font = new Font("Segoe UI Semibold", 9.5F);
        statusLabel.ForeColor = TextSecondary;
        statusLabel.Location = new Point(746, 46);
        Controls.Add(statusLabel);

        hardwareLabel.Text = "Detecting hardware...";
        hardwareLabel.ForeColor = TextSecondary;
        hardwareLabel.Font = new Font("Segoe UI", 9.5F);
        hardwareLabel.AutoEllipsis = true;
        hardwareLabel.TextAlign = ContentAlignment.MiddleRight;
        hardwareLabel.Location = new Point(455, 72);
        hardwareLabel.Size = new Size(445, 24);
        Controls.Add(hardwareLabel);

        var displayCard = CreateCard(new Point(40, 145), new Size(420, 360));
        Controls.Add(displayCard);

        AddSectionHeader(displayCard, "DISPLAY", "Display settings", 24, 20);

        AddFieldLabel(displayCard, "Resolution", 24, 82);
        ConfigureCombo(resolutionBox, 24, 106, 372);
        displayCard.Controls.Add(resolutionBox);

        AddFieldLabel(displayCard, "Refresh rate", 24, 150);
        ConfigureCombo(refreshBox, 24, 174, 180);
        displayCard.Controls.Add(refreshBox);

        AddFieldLabel(displayCard, "Display mode", 216, 150);
        ConfigureCombo(displayModeBox, 216, 174, 180);
        displayCard.Controls.Add(displayModeBox);

        AddFieldLabel(displayCard, "Monitor", 24, 218);
        ConfigureCombo(monitorBox, 24, 242, 372);
        displayCard.Controls.Add(monitorBox);

        ConfigureCheck(vsyncBox, "VSync", 24, 300);
        ConfigureCheck(vrrBox, "VRR / Tearing", 216, 300);
        displayCard.Controls.Add(vsyncBox);
        displayCard.Controls.Add(vrrBox);

        var graphicsCard = CreateCard(new Point(480, 145), new Size(420, 360));
        Controls.Add(graphicsCard);

        AddSectionHeader(graphicsCard, "GRAPHICS", "Quality and presentation", 24, 20);

        AddFieldLabel(graphicsCard, "Graphics API", 24, 82);
        ConfigureCombo(graphicsApiBox, 24, 106, 120);
        graphicsCard.Controls.Add(graphicsApiBox);

        AddFieldLabel(graphicsCard, "GPU", 156, 82);
        ConfigureCombo(gpuBox, 156, 106, 240);
        graphicsCard.Controls.Add(gpuBox);

AddFieldLabel(graphicsCard, "Post-processing", 24, 150);
ConfigureCombo(postEffectBox, 24, 174, 372);
graphicsCard.Controls.Add(postEffectBox);

AddFieldLabel(graphicsCard, "Internal Resolution", 24, 218);
ConfigureCombo(internalResolutionBox, 24, 242, 372);
graphicsCard.Controls.Add(internalResolutionBox);

ConfigureCheck(msaaBox, "Native 2x MSAA", 24, 300);
graphicsCard.Controls.Add(msaaBox);

var fpsTitle = new Label
{
    Text = "FPS",
    ForeColor = TextPrimary,
    Font = new Font("Segoe UI Semibold", 9F),
    AutoSize = true,
    Location = new Point(24, 330)
};
graphicsCard.Controls.Add(fpsTitle);

var fpsInfo = new Label
{
    Text = "Controlled by the game, VSync and refresh rate",
    ForeColor = TextSecondary,
    Font = new Font("Segoe UI", 8.7F),
    AutoSize = true,
    Location = new Point(67, 331)
        };
        graphicsCard.Controls.Add(fpsInfo);

        advancedButton.Text = "ADVANCED SETTINGS";
        advancedButton.Location = new Point(40, 525);
        advancedButton.Size = new Size(205, 40);
        advancedButton.FillColor = SurfaceAlt;
        advancedButton.HoverColor = Color.FromArgb(34, 41, 50);
        advancedButton.BorderColor = Border;
        advancedButton.ForeColor = TextPrimary;
        advancedButton.Font = new Font("Segoe UI Semibold", 9F);
        advancedButton.Radius = 10;
        Controls.Add(advancedButton);

        advancedChevron.Text = "⌄";
        advancedChevron.ForeColor = TextSecondary;
        advancedChevron.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        advancedChevron.AutoSize = true;
        advancedChevron.Location = new Point(219, 533);
        advancedChevron.BackColor = Color.Transparent;
        advancedChevron.Cursor = Cursors.Hand;
        Controls.Add(advancedChevron);

        compatibilityButton.Text = "COMPATIBILITY";
        compatibilityButton.Location = new Point(255, 525);
        compatibilityButton.Size = new Size(205, 40);
        compatibilityButton.FillColor = SurfaceAlt;
        compatibilityButton.HoverColor = Color.FromArgb(34, 41, 50);
        compatibilityButton.BorderColor = Border;
        compatibilityButton.ForeColor = TextPrimary;
        compatibilityButton.Font = new Font("Segoe UI Semibold", 9F);
        compatibilityButton.Radius = 10;
        Controls.Add(compatibilityButton);

        ConfigureAdvancedPanel();
        Controls.Add(advancedPanel);

        ConfigureCompatibilityPanel();
        Controls.Add(compatibilityPanel);

        var logsButton = new ModernButton
        {
            Text = "OPEN LOGS",
            Location = new Point(40, 590),
            Size = new Size(150, 42),
            FillColor = SurfaceAlt,
            HoverColor = Color.FromArgb(34, 41, 50),
            BorderColor = Border,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 9F),
            Radius = 10
        };
        logsButton.Click += (_, _) => OpenLogs();
        Controls.Add(logsButton);

        var folderButton = new ModernButton
        {
            Text = "OPEN FOLDER",
            Location = new Point(750, 590),
            Size = new Size(150, 42),
            FillColor = SurfaceAlt,
            HoverColor = Color.FromArgb(34, 41, 50),
            BorderColor = Border,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 9F),
            Radius = 10
        };
        folderButton.Click += (_, _) => OpenGameFolder();
        Controls.Add(folderButton);

        var playButton = new ModernButton
        {
            Text = "PLAY",
            Location = new Point(280, 575),
            Size = new Size(380, 72),
            FillColor = Accent,
            HoverColor = AccentHover,
            BorderColor = Accent,
            ForeColor = Color.FromArgb(9, 14, 8),
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Radius = 14
        };
        playButton.Click += (_, _) => LaunchGame();
        Controls.Add(playButton);

        var footer = new Label
        {
            Text = "FIFA Street • Native PC Recomp Launcher",
            ForeColor = Color.FromArgb(92, 102, 116),
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = true,
            Location = new Point(40, 674)
        };
        Controls.Add(footer);

        foreach (Control control in Controls) control.Left += 310;
        using var resource = typeof(LauncherForm).Assembly.GetManifestResourceStream("cover.jpg")!;
        using var cover = Image.FromStream(resource);
        var artwork = new MessiPicture
        {
            Image = new Bitmap(cover), Bounds = new Rectangle(0, 0, 310, ClientSize.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        Controls.Add(artwork);
        FormClosed += (_, _) => artwork.Image?.Dispose();
        var credit = new Label
        {
            Text = "PORTED BY: SAMUELITODAVILA", AutoSize = true,
            ForeColor = Accent, Font = new Font("Segoe UI", 8.5F),
            Location = new Point(730, 674), Tag = "credit"
        };
        Controls.Add(credit);

        ResumeLayout(false);
        PerformLayout();
    }

    private void ConfigureAdvancedPanel()
    {
        advancedPanel.Location = new Point(40, 575);
        advancedPanel.Size = new Size(860, 126);
        advancedPanel.FillColor = Surface;
        advancedPanel.BorderColor = Border;
        advancedPanel.Radius = 14;
        advancedPanel.Visible = false;

        AddFieldLabel(advancedPanel, "Game executable", 22, 18);
        exePathBox.Location = new Point(22, 42);
        exePathBox.Size = new Size(688, 27);
        StyleTextBox(exePathBox);
        advancedPanel.Controls.Add(exePathBox);

        var browseExe = new ModernButton
        {
            Text = "BROWSE",
            Location = new Point(724, 39),
            Size = new Size(112, 34),
            FillColor = SurfaceAlt,
            HoverColor = Color.FromArgb(34, 41, 50),
            BorderColor = Border,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Radius = 8
        };
        browseExe.Click += (_, _) => BrowseExe();
        advancedPanel.Controls.Add(browseExe);

        AddFieldLabel(advancedPanel, "Game data folder", 22, 76);
        gamePathBox.Location = new Point(22, 98);
        gamePathBox.Size = new Size(688, 27);
        StyleTextBox(gamePathBox);
        advancedPanel.Controls.Add(gamePathBox);

        var browseGame = new ModernButton
        {
            Text = "BROWSE",
            Location = new Point(724, 95),
            Size = new Size(112, 34),
            FillColor = SurfaceAlt,
            HoverColor = Color.FromArgb(34, 41, 50),
            BorderColor = Border,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Radius = 8
        };
        browseGame.Click += (_, _) => BrowseGame();
        advancedPanel.Controls.Add(browseGame);
    }

    private void ConfigureCompatibilityPanel()
    {
        compatibilityPanel.Location = new Point(40, 575);
        compatibilityPanel.Size = new Size(860, 182);
        compatibilityPanel.FillColor = Surface;
        compatibilityPanel.BorderColor = Border;
        compatibilityPanel.Radius = 14;
        compatibilityPanel.Visible = false;

        AddSectionHeader(
            compatibilityPanel,
            "COMPATIBILITY",
            "Renderer options for textures, lighting, black screens and stutter",
            22,
            15
        );

        ConfigureCheck(readbackMemexportBox, "Memory Export", 260, 72);
        ConfigureCheck(readbackMemexportFastBox, "Fast MemExport", 430, 72);
        ConfigureCheck(clearMemoryPageStateBox, "Memory Page State", 615, 72);

        compatibilityPanel.Controls.Add(readbackMemexportBox);
        compatibilityPanel.Controls.Add(readbackMemexportFastBox);
        compatibilityPanel.Controls.Add(clearMemoryPageStateBox);

        ConfigureCheck(occlusionQueryBox, "Occlusion Queries", 260, 112);
        ConfigureCheck(asyncShadersBox, "Async Shaders", 430, 112);

        compatibilityPanel.Controls.Add(occlusionQueryBox);
        compatibilityPanel.Controls.Add(asyncShadersBox);

        var hint = new Label
        {
            Text = "Readback Resolve is fixed to Full for FIFA Street compatibility.",
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = true,
            Location = new Point(22, 151)
        };
        compatibilityPanel.Controls.Add(hint);
    }

    private void WireEvents()
    {
        advancedButton.Click += (_, _) => ToggleAdvanced();
        advancedChevron.Click += (_, _) => ToggleAdvanced();
        compatibilityButton.Click += (_, _) => ToggleCompatibility();

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

        resolutionBox.SelectedIndexChanged += (_, _) => UpdateSummary();
internalResolutionBox.SelectedIndexChanged += (_, _) => UpdateSummary();
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
displayModeBox.SelectedIndexChanged += (_, _) => UpdateSummary();
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

        if (advancedVisible)
        {
            ClientSize = new Size(1250, 846);
            advancedPanel.Location = new Point(350, 575);
            MoveBottomControls(721, 703, 808);
        }
        else
        {
            ClientSize = new Size(1250, 720);
            MoveBottomControls(590, 575, 674);
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

        if (compatibilityVisible)
        {
            ClientSize = new Size(1250, 902);
            compatibilityPanel.Location = new Point(350, 575);
            MoveBottomControls(777, 759, 864);
        }
        else
        {
            ClientSize = new Size(1250, 720);
            MoveBottomControls(590, 575, 674);
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
                     (label.Text.StartsWith("FIFA Street •", StringComparison.Ordinal) || label.Tag as string == "credit"))
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
        var label = new Label
        {
            Text = text,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI Semibold", 8.7F),
            AutoSize = true,
            Location = new Point(x, y)
        };
        parent.Controls.Add(label);
    }

    private void ConfigureCombo(ComboBox box, int x, int y, int width)
    {
        box.Location = new Point(x, y);
        box.Size = new Size(width, 30);
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = SurfaceAlt;
        box.ForeColor = TextPrimary;
        box.Font = new Font("Segoe UI", 9.7F);
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.ItemHeight = 22;
        box.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush(SurfaceAlt);
            e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 ? box.Items[e.Index]?.ToString() ?? "" : box.Text;
            TextRenderer.DrawText(e.Graphics, text, box.Font, e.Bounds, TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        box.IntegralHeight = false;
        box.DropDownHeight = 240;
    }

    private void ConfigureCheck(CheckBox box, string text, int x, int y)
    {
        box.Text = text;
        box.Location = new Point(x, y);
        box.AutoSize = true;
        box.ForeColor = TextPrimary;
        box.BackColor = Color.Transparent;
        box.Font = new Font("Segoe UI Semibold", 9.2F);
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
        exePathBox.Text =
            Path.Combine(AppContext.BaseDirectory, "Game", "fifastreet.exe");

        gamePathBox.Text =
            Path.Combine(AppContext.BaseDirectory, "GameData");

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

        monitorBox.Items.Clear();
        foreach (var screen in Screen.AllScreens)
        {
            string primary = screen.Primary ? " • Primary" : "";
            monitorBox.Items.Add(
                $"{monitorBox.Items.Count}: {screen.DeviceName}{primary}"
            );
        }

        if (monitorBox.Items.Count > 0)
            monitorBox.SelectedIndex = 0;

        DetectDisplayModes();
        DetectGpus();

        postEffectBox.Items.AddRange(new object[]
{
    "None",
    "FXAA",
    "FXAA Extreme"
});
       postEffectBox.SelectedIndex = 0;

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
        occlusionQueryBox.Checked = true;
        asyncShadersBox.Checked = false;

        vsyncBox.Checked = false;
        vrrBox.Checked = false;
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
            $"{gpu}   •   {resolution}   •   {hz} Hz";
    }

    private void UpdateStatus(string text, bool ok)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = ok ? TextSecondary : Color.FromArgb(255, 120, 120);
    }

    private void LaunchGame()
    {
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
                    "FIFA Street Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string executableDirectory = Path.GetDirectoryName(exe)!;

            File.Copy(
                backendRuntime,
                Path.Combine(executableDirectory, "rexruntime.dll"),
                true);

            File.Copy(
                backendGpu,
                Path.Combine(executableDirectory, "rexgpu-xenos.dll"),
                true);

            if (string.IsNullOrWhiteSpace(resolutionBox.Text) ||
                !resolutionBox.Text.Contains('x'))
            {
                ShowError("Select a valid resolution.");
                return;
            }

            var resolution = resolutionBox.Text.Split('x');
            int width = int.Parse(resolution[0]);
            int height = int.Parse(resolution[1]);
            string refresh = refreshBox.Text;

            bool fullscreen =
                displayModeBox.SelectedItem?.ToString() == "Fullscreen";

            int monitor = Math.Max(0, monitorBox.SelectedIndex);

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

            // ReXGlue aplica a configuração por esta ordem:
            // argumentos CLI -> ficheiro TOML -> variáveis de ambiente.
            // Portanto, usar REX_* aqui garante que as escolhas do launcher
            // têm prioridade sobre qualquer configuração guardada no jogo.

            startInfo.Environment["REX_GAME_DATA_ROOT"] = gameRoot;
            startInfo.Environment["REX_VIDEO_MODE_WIDTH"] = width.ToString();
            startInfo.Environment["REX_VIDEO_MODE_HEIGHT"] = height.ToString();
            // The display size does not change the Xbox 360 render targets.
         int internalResolutionScale = Math.Max(1, internalResolutionBox.SelectedIndex + 1);

startInfo.Environment["REX_DRAW_RESOLUTION_SCALE_X"] =
    internalResolutionScale.ToString();

startInfo.Environment["REX_DRAW_RESOLUTION_SCALE_Y"] =
    internalResolutionScale.ToString();
            startInfo.Environment["REX_WINDOW_WIDTH"] = width.ToString();
            startInfo.Environment["REX_WINDOW_HEIGHT"] = height.ToString();
            startInfo.Environment["REX_VIDEO_MODE_REFRESH_RATE"] = refresh;
            startInfo.Environment["REX_FULLSCREEN"] =
                fullscreen ? "true" : "false";
            startInfo.Environment["REX_MONITOR"] = monitor.ToString();
            startInfo.Environment["REX_VSYNC"] =
                vsyncBox.Checked ? "true" : "false";
            startInfo.Environment["REX_NATIVE_2X_MSAA"] =
                msaaBox.Checked ? "true" : "false";
            startInfo.Environment["REX_SWAP_POST_EFFECT"] = postEffect;

            if (graphicsApi == "D3D12")
            {
                // Validated D3D12 configuration.
                startInfo.Environment["REX_D3D12_ALLOW_VARIABLE_REFRESH_RATE_AND_TEARING"] =
                    vrrBox.Checked ? "true" : "false";
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
            }

            // Compatibilidade / coerência GPU.
            startInfo.Environment["REX_READBACK_RESOLVE"] = "full";
            startInfo.Environment["REX_READBACK_MEMEXPORT"] =
                readbackMemexportBox.Checked ? "true" : "false";
            startInfo.Environment["REX_READBACK_MEMEXPORT_FAST"] =
                readbackMemexportFastBox.Checked ? "true" : "false";
            startInfo.Environment["REX_CLEAR_MEMORY_PAGE_STATE"] =
                clearMemoryPageStateBox.Checked ? "true" : "false";
            startInfo.Environment["REX_OCCLUSION_QUERY_ENABLE"] =
                occlusionQueryBox.Checked ? "true" : "false";
            startInfo.Environment["REX_ASYNC_SHADER_COMPILATION"] =
                asyncShadersBox.Checked ? "true" : "false";

            startInfo.Environment["REX_LOG_VERBOSE"] = "false";
            startInfo.Environment["REX_LOG_LEVEL"] = "info";
            startInfo.Environment["REX_LOG_FRAME_STATS"] = "true";
            startInfo.Environment["REX_BIND_DEBUG_OVERLAY"] = "Home";
            startInfo.Environment["REX_LOG_FILE"] = logFile;

            // Mantemos game_data_root também como argumento porque esta
            // aplicação já confirmou que o aceita diretamente.
            startInfo.ArgumentList.Add($"--game_data_root={gameRoot}");

            SaveSettings();

            string launcherDebug = Path.Combine(
                logsDir,
                "launcher_settings.txt"
            );

            File.WriteAllText(
                launcherDebug,
                $"Resolution={width}x{height}{Environment.NewLine}" +
                $"InternalResolution={1280 * internalResolutionScale}x{720 * internalResolutionScale} ({internalResolutionScale}x){Environment.NewLine}" +
                $"RefreshRate={refresh}{Environment.NewLine}" +
                $"Fullscreen={fullscreen}{Environment.NewLine}" +
                $"Monitor={monitor}{Environment.NewLine}" +
                $"VSync={vsyncBox.Checked}{Environment.NewLine}" +
                $"VRR={vrrBox.Checked}{Environment.NewLine}" +
                $"MSAA={msaaBox.Checked}{Environment.NewLine}" +
                $"PostEffect={postEffect}{Environment.NewLine}" +
                $"Adapter={adapter}{Environment.NewLine}" +
                $"ReadbackResolve=full (fixed){Environment.NewLine}" +
                $"ReadbackMemexport={readbackMemexportBox.Checked}{Environment.NewLine}" +
                $"ReadbackMemexportFast={readbackMemexportFastBox.Checked}{Environment.NewLine}" +
                $"ClearMemoryPageState={clearMemoryPageStateBox.Checked}{Environment.NewLine}" +
                $"OcclusionQueries={occlusionQueryBox.Checked}{Environment.NewLine}" +
                $"AsyncShaders={asyncShadersBox.Checked}{Environment.NewLine}"
            );

            UpdateStatus("Starting FIFA Street...", true);

            var process = Process.Start(startInfo);

            UpdateStatus(
                process != null
                    ? $"Running • PID {process.Id}"
                    : "Could not start the game",
                process != null
            );
        }
        catch (Exception ex)
        {
            UpdateStatus("Launch failed", false);

            MessageBox.Show(
                ex.Message,
                "Error starting FIFA Street",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void ShowError(string message)
    {
        UpdateStatus("Check your settings", false);

        MessageBox.Show(
            message,
            "FIFA Street Launcher",
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

        if (!File.Exists(exe))
            return;

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

        if (!File.Exists(exe))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{Path.GetDirectoryName(exe)}\"",
            UseShellExecute = true
        });
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
                Gpu = gpuBox.SelectedIndex,
                GpuUsesDxgi = true,
                PostEffect = postEffectBox.SelectedIndex,
                InternalResolutionScale = internalResolutionBox.SelectedIndex,
                ReadbackMemexport = readbackMemexportBox.Checked,
                ReadbackMemexportFast = readbackMemexportFastBox.Checked,
                ClearMemoryPageState = clearMemoryPageStateBox.Checked,
                OcclusionQueries = occlusionQueryBox.Checked,
                AsyncShaders = asyncShadersBox.Checked,
                VSync = vsyncBox.Checked,
                VRR = vrrBox.Checked,
                MSAA = msaaBox.Checked
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
        catch
        {
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

            exePathBox.Text = settings.ExePath ?? exePathBox.Text;
            const string oldDefaultExe = @"C:\Users\Samuel M\Desktop\FIFASTREET - 2012\Jogo\FifaStreetRex\out\build\win-amd64-debug\fifastreet.exe";
            string releaseExe = oldDefaultExe.Replace("win-amd64-debug", "win-amd64-release");
            if (string.Equals(exePathBox.Text, oldDefaultExe, StringComparison.OrdinalIgnoreCase)
                && File.Exists(releaseExe))
                exePathBox.Text = releaseExe;
            gamePathBox.Text = settings.GamePath ?? gamePathBox.Text;

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

            readbackMemexportBox.Checked = settings.ReadbackMemexport;
            readbackMemexportFastBox.Checked = settings.ReadbackMemexportFast;
            clearMemoryPageStateBox.Checked = settings.ClearMemoryPageState;
            occlusionQueryBox.Checked = settings.OcclusionQueries;
            asyncShadersBox.Checked = settings.AsyncShaders;

            vsyncBox.Checked = settings.VSync;
            vrrBox.Checked = settings.VRR;
            msaaBox.Checked = settings.MSAA;
        }
        catch
        {
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
    public int Gpu { get; set; }
    public bool GpuUsesDxgi { get; set; }
    public int PostEffect { get; set; }
    public int InternalResolutionScale { get; set; } = 0;
    public bool ReadbackMemexport { get; set; } = true;
    public bool ReadbackMemexportFast { get; set; } = true;
    public bool ClearMemoryPageState { get; set; } = false;
    public bool OcclusionQueries { get; set; } = true;
    public bool AsyncShaders { get; set; } = true;
    public bool VSync { get; set; } = true;
    public bool VRR { get; set; } = false;
    public bool MSAA { get; set; } = false;
}

public class RoundedPanel : Panel
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
