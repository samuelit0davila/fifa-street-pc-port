using System.Diagnostics;
using ReStreet.Ui;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace FifaStreetSetupTool;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--update-test")
        {
            using var log = new StreamWriter(args[1] + ".update-test.log") { AutoFlush = true };
            Console.SetOut(log);
            try { SetupForm.PrepareBundle(); InstallationUpdate.Apply(InstallerEngine.BundleRoot!, Path.GetFullPath(args[1])); return 0; }
            catch (Exception error) { Console.WriteLine(error); return 100; }
        }
        if (args.Length == 2 && args[0] == "--cancel-self-test")
        {
            using var cancellation = new System.Threading.CancellationTokenSource();
            InstallerEngine.CancellationToken = cancellation.Token;
            cancellation.CancelAfter(750);
            var clock = Stopwatch.StartNew();
            var child = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            child.ArgumentList.Add("/c");
            child.ArgumentList.Add("ping -n 30 127.0.0.1 >nul");
            try { InstallerEngine.RunProcess(child); return 1; }
            catch (OperationCanceledException)
            {
                File.WriteAllText(args[1], $"Cancellation passed in {clock.Elapsed.TotalSeconds:F2} seconds.");
                return clock.Elapsed < TimeSpan.FromSeconds(10) ? 0 : 2;
            }
        }
        if (args.Length == 3 && args[0] == "--credit-test")
        {
            using var log = new StreamWriter(args[2]);
            Console.SetOut(log);
            try { CreditPatch.Apply(args[1]); return 0; }
            catch (Exception error) { Console.WriteLine(error); return 100; }
        }
        if (args.Length >= 2 && args[0] == "--preview")
        {
            using var form = new SetupForm();
            form.Show();
            if (args.Length > 2) form.PreviewState(args[2]);
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]);
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--install-test")
        {
            using var log = new StreamWriter(args[2] + ".setup.log", false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(TextWriter.Synchronized(log));
            try
            {
                SetupForm.PrepareBundle();
                return InstallerEngine.Run(args.Skip(1).ToArray());
            }
            catch (Exception error) { Console.WriteLine(error); return 100; }
        }
        Application.Run(new SetupForm());
        return 0;
    }
}

internal sealed class SetupForm : Form, ISceneHost, IUiScaled
{
    readonly UiScaler scaler;
    public float UiScale => scaler.Scale;

    readonly TextBox iso = new();
    readonly TextBox destination = new();
    readonly TextBox details = new();
    readonly SlantButton install = new();
    readonly SlantButton cancel = new();
    System.Threading.CancellationTokenSource? cancellation;
    readonly SlantProgress progress = new();
    readonly ThemedLabel status = new();
    readonly List<Control> inputs = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    readonly System.Collections.Concurrent.ConcurrentQueue<string> messages = new();
    bool busy;
    bool completed;
    string installedPath = "";
    static readonly Color Accent = Color.FromArgb(133, 255, 72);
    internal const string Credit = "PORTED BY: SAMUELITODAVILA";

    const float DesignHeight = 610f;
    Bitmap? scene;
    Size sceneSize;

    // Layout on an 8 px grid (design pixels): left column margin 48, glass sheet 480..902 x 32..578
    // with 24 px padding, so content spans x 504..878.
    const int Left = 48, SheetX = 480, SheetY = 32, SheetW = 422, SheetH = 546, Pad = 24;
    const int ContentX = SheetX + Pad, ContentW = SheetW - 2 * Pad;

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr handle, string appName, string? idList);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    // Keeps the end of a long path visible (the folder name is the useful part).
    static void ShowEnd(TextBox box)
    {
        if (!box.IsHandleCreated) return;
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        SendMessage(box.Handle, 0x00B7 /* EM_SCROLLCARET */, IntPtr.Zero, IntPtr.Zero);
    }

    internal SetupForm()
    {
        Text = "ReStreet - FIFA Street 2012 Recompiled — Setup";
        using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("fifastreet.ico")!)
            Icon = new Icon(icon);
        ClientSize = new Size(930, 610);
        scaler = new UiScaler(this, 930, 610);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = StreetTheme.Asphalt;
        ForeColor = StreetTheme.Ink;
        Font = StreetTheme.Body(10);
        HandleCreated += (_, _) => StreetTheme.ApplyDarkTitleBar(Handle);

        Controls.Add(Text_(PrecompiledPackage.Enabled
            ? "New installation: select your ISO. To update, choose your existing game folder (the folder containing Game and GameData)."
            : "Select your FIFA Street ISO and choose where to install the game.", Left, 264, 384, 72, 10f, StreetTheme.Muted, false));

        Controls.Add(Text_("Setup", ContentX, SheetY + Pad, ContentW, 32, 20f, StreetTheme.Ink, true));
        iso.PlaceholderText = "Choose your FIFA Street ISO";
        AddPath("FIFA Street ISO", iso, 104, () =>
        {
            using var dialog = new OpenFileDialog { Title = "Select FIFA Street ISO", Filter = "ISO image (*.iso)|*.iso", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) iso.Text = dialog.FileName;
        });
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Games", "FIFA Street PC");
        AddPath(PrecompiledPackage.Enabled ? "Install folder, or game folder to update" : "Installation folder", destination, 184, () =>
        {
            string initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            try
            {
                var folder = new DirectoryInfo(Path.GetFullPath(destination.Text));
                while (folder != null && !folder.Exists) folder = folder.Parent;
                if (folder != null) initialFolder = folder.FullName;
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                // A manually entered invalid path must not prevent browsing.
            }
            using var dialog = new FolderBrowserDialog { Description = PrecompiledPackage.Enabled ? "Choose a new installation folder, or your existing game folder to update (containing Game and GameData)." : "Choose installation folder", UseDescriptionForTitle = true, InitialDirectory = initialFolder };
            if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
        });
        destination.TextChanged += (_, _) => { RefreshInstallMode(); ShowEnd(destination); };
        destination.HandleCreated += (_, _) => ShowEnd(destination);
        destination.Leave += (_, _) => ShowEnd(destination);

        status.SetBounds(ContentX, 272, ContentW, 40);
        status.Font = StreetTheme.Body(10);
        status.ForeColor = StreetTheme.Ink;
        status.Text = PrecompiledPackage.Enabled ? "Your ISO supplies the game data. Ready-to-play binaries are included." : "Game files will be created on your PC from your ISO.";
        Controls.Add(status);
        progress.SetBounds(ContentX, 328, ContentW, 12);
        Controls.Add(progress);
        Controls.Add(Text_(PrecompiledPackage.Enabled ? "No compiler, Visual Studio or Internet connection required." : "An Internet connection may be needed to set up Windows components.", ContentX, 352, ContentW, 40, 10f, StreetTheme.Muted, false));

        install.SetBounds(ContentX, 420, ContentW, 80);
        install.Kind = SlantKind.Primary;
        install.Text = "INSTALL ReStreet";
        install.Font = StreetTheme.Condensed(22, FontStyle.Bold);
        install.Click += async (_, _) => await Install();
        AcceptButton = install;
        Controls.Add(install);

        var showDetails = new SlantButton { Kind = SlantKind.Text, Align = StringAlignment.Near, Text = "Show details", Bounds = new Rectangle(ContentX, 522, 150, 32), Font = StreetTheme.Body(10) };
        showDetails.Click += (_, _) =>
        {
            details.Visible = !details.Visible;
            showDetails.Text = details.Visible ? "Hide details" : "Show details";
            scaler.DesignHeight = details.Visible ? 790 : 610;
        };
        Controls.Add(showDetails);
        cancel.Kind = SlantKind.Text;
        cancel.Align = StringAlignment.Far;
        cancel.Text = "Cancel";
        cancel.Font = StreetTheme.Body(10);
        cancel.SetBounds(ContentX + ContentW - 150, 522, 150, 32);
        cancel.Enabled = false;
        cancel.Click += (_, _) =>
        {
            if (!completed) { CancelInstallation(); return; }
            try
            {
                CreateDesktopShortcut(installedPath);
                cancel.Text = "Desktop shortcut created";
                cancel.Enabled = false;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "The desktop shortcut could not be created.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        Controls.Add(cancel);
        details.SetBounds(Left, 626, SheetX + SheetW - Left, 132);
        details.Multiline = true;
        details.ReadOnly = true;
        details.ScrollBars = ScrollBars.Vertical;
        details.BorderStyle = BorderStyle.None;
        details.BackColor = Color.FromArgb(24, 27, 31);
        details.ForeColor = Color.Gainsboro;
        details.Font = StreetTheme.Body(9);
        details.Visible = false;
        details.HandleCreated += (_, _) => SetWindowTheme(details.Handle, "DarkMode_Explorer", null);
        Controls.Add(details);
        timer.Tick += (_, _) => DrainMessages();
        timer.Start();
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            CancelInstallation();
        };
        FormClosed += (_, _) => { timer.Dispose(); scene?.Dispose(); };
        RefreshInstallMode();
        scaler.Capture();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        BuildScene();
        using (var floor = new SolidBrush(StreetTheme.Asphalt)) e.Graphics.FillRectangle(floor, ClientRectangle);
        e.Graphics.DrawImageUnscaled(scene!, 0, 0);
    }

    // Backdrop, glass sheet, wordmark and credit are painted once per size.
    public Bitmap SceneBitmap { get { BuildScene(); return scene!; } }

    void BuildScene()
    {
        float s = ClientSize.Width / (float)StreetTheme.DesignWidth;
        var size = new Size(ClientSize.Width, (int)Math.Ceiling(DesignHeight * s));
        if (scene != null && sceneSize == size) return;
        scene?.Dispose();
        sceneSize = size;
        using var backdrop = StreetTheme.RenderBackdrop(size, StreetTheme.DesignWidth, DesignHeight);
        using var blurred = StreetTheme.Blur(backdrop, 10);
        scene = new Bitmap(backdrop);
        using var g = Graphics.FromImage(scene);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(s, s);

        StreetTheme.DrawGlass(g, blurred, s, new RectangleF(SheetX, SheetY, SheetW, SheetH), 16);

        using (var small = new Font(StreetTheme.CondensedFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var yellow = new SolidBrush(StreetTheme.Yellow))
            StreetTheme.DrawSpaced(g, "FIFA STREET 2012 RECOMPILED", small, yellow, new PointF(Left, 56), 3.2f);
        StreetTheme.DrawWordmark(g, "ReStreet", new PointF(Left, 88), 118, StreetTheme.Ink);
        var tag = new RectangleF(Left, 192, 160, 32);
        using (var bar = new SolidBrush(StreetTheme.Yellow)) g.FillPolygon(bar, StreetTheme.Slant(tag, 10));
        using (var tagFont = new Font(StreetTheme.CondensedFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var dark = new SolidBrush(StreetTheme.YellowDark))
            StreetTheme.DrawSpaced(g, "PC INSTALLER", tagFont, dark, new PointF(Left + 12, 198), 2.6f);

        using var name = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        StreetTheme.DrawLabelValue(g, "Ported by:", "SamuelitoDaVila", name, StreetTheme.Ink, StreetTheme.Ink, Left, 534);
        StreetTheme.DrawLabelValue(g, "Contributions:", "Emran_Ahm3d", name, StreetTheme.Muted, StreetTheme.Muted, Left, 557);
    }

    static ThemedLabel Text_(string text, int x, int y, int width, int height, float size, Color color, bool condensed) =>
        new()
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            Font = condensed ? StreetTheme.Condensed(size, FontStyle.Bold) : StreetTheme.Body(size),
            ForeColor = color
        };

    // Caption (16 high) + field (40 high) + Browse (96 wide) on one row; `y` is the caption's top.
    void AddPath(string label, TextBox box, int y, Action browse)
    {
        Controls.Add(Text_(label.ToUpperInvariant(), ContentX, y, ContentW, 16, 9f, StreetTheme.Muted, true));
        int fieldY = y + 24;
        const int browseW = 96, gap = 8;
        box.Font = StreetTheme.Body(10);
        box.AccessibleName = label;
        var frame = new FieldFrame(box) { Bounds = new Rectangle(ContentX, fieldY, ContentW - browseW - gap, 40) };
        Controls.Add(frame);
        var button = new SlantButton { Kind = SlantKind.Glass, Text = "Browse", Bounds = new Rectangle(ContentX + ContentW - browseW, fieldY, browseW, 40), Font = StreetTheme.Body(10) };
        button.AccessibleName = "Browse " + label;
        button.Click += (_, _) => browse();
        Controls.Add(button);
        inputs.Add(box);
        inputs.Add(button);
    }

    // Screenshots of the states that need a real installation to reach (setup --preview <png> <state>).
    internal void PreviewState(string state)
    {
        switch (state)
        {
            case "installing":
                iso.Text = @"D:\Games\FIFA Street\FIFASTREET.iso";
                destination.Text = @"D:\Games\ReStreet";
                foreach (var input in inputs) input.Enabled = false;
                install.Enabled = false;
                install.Text = "INSTALLING…";
                progress.Value = 64;
                cancel.Enabled = true;
                status.Text = "Extracting the FIFA Street ISO… (3/6)";
                break;
            case "done":
                completed = true;
                installedPath = @"D:\Games\ReStreet";
                progress.Value = 100;
                status.Text = "Installation complete. You can now launch the game.";
                install.Text = "OPEN LAUNCHER";
                cancel.SetBounds(ContentX + ContentW - 224, 522, 224, 32);
                cancel.Text = "Create desktop shortcut";
                cancel.Enabled = true;
                break;
            case "error":
                status.Text = "Installation failed. Check the details and installation.log.";
                install.Text = "TRY AGAIN";
                break;
            case "details":
                details.Text = "Preparing installation tools…\r\nChecking the ISO…\r\nExtracting the FIFA Street ISO…";
                details.Visible = true;
                scaler.DesignHeight = 790;
                break;
        }
        Application.DoEvents();
    }

    void RefreshInstallMode()
    {
        if (busy || completed || !PrecompiledPackage.Enabled) return;
        bool updating = InstallationUpdate.IsInstallation(destination.Text);
        install.Text = updating ? "UPDATE ReStreet" : "INSTALL ReStreet";
        iso.Enabled = !updating;
        inputs[1].Enabled = !updating;
        status.Text = updating ? "Existing installation found. Click UPDATE ReStreet. No ISO required; saves and preferences are preserved." : "Select your ISO for a new installation, or choose your existing game folder to update.";
    }

    async Task Install()
    {
        if (completed)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(installedPath, "FifaStreetLauncher.exe")) { UseShellExecute = true, WorkingDirectory = installedPath });
                Close();
            }
            catch (Exception error) { MessageBox.Show(this, "The launcher could not be opened.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        bool updating = PrecompiledPackage.Enabled && InstallationUpdate.IsInstallation(destination.Text);
        if (!updating && (!File.Exists(iso.Text) || !iso.Text.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Select a valid FIFA Street ISO image.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string target;
        try
        {
            target = Path.GetFullPath(destination.Text);
            if (target == Path.GetPathRoot(target)) throw new IOException("Choose a game folder instead of the root of a drive.");
            if (!updating && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException("The selected folder contains files. Choose a new or empty folder to install the game.");
            Directory.CreateDirectory(target);
            long requiredSpace = (updating ? 2L : 12L) * 1024 * 1024 * 1024;
            if (new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace < requiredSpace)
                throw new IOException(updating ? "At least 2 GB of free space is required for the update and backup." : "At least 12 GB of free space is required on the destination drive.");
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        busy = true;
        cancellation = new System.Threading.CancellationTokenSource();
        InstallerEngine.CancellationToken = cancellation.Token;
        cancel.Enabled = true;
        foreach (var input in inputs) input.Enabled = false;
        install.Enabled = false;
        install.Text = "INSTALLING…";
        progress.Style = ProgressBarStyle.Marquee;
        status.Text = "Preparing installation…";
        string selectedIso = iso.Text;
        string logPath = Path.Combine(target, updating ? "update.log" : "installation.log");
        StreamWriter? log = null;
        var previousWriter = Console.Out;
        int result;
        try
        {
            log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new ProgressWriter(log, messages));
            result = await Task.Run(async () =>
            {
                Console.WriteLine("Preparing installation tools…");
                PrepareBundle();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!PrecompiledPackage.Enabled) await EnsureWindowsTools(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (updating)
                {
                    InstallationUpdate.Apply(InstallerEngine.BundleRoot!, target);
                    return 0;
                }
                return InstallerEngine.Run(new[] { selectedIso, target });
            });
        }
        catch (OperationCanceledException) { messages.Enqueue("Installation cancelled."); result = 1223; }
        catch (Exception error)
        {
            messages.Enqueue(error.Message);
            try { log?.WriteLine(error); }
            catch (IOException) { /* The original error remains visible in the form. */ }
            result = 100;
        }
        finally
        {
            Console.SetOut(previousWriter);
            busy = false;
            cancel.Enabled = false;
            cancellation.Dispose();
            cancellation = null;
            InstallerEngine.CancellationToken = default;
            progress.Style = ProgressBarStyle.Continuous;
            install.Enabled = true;
            try { log?.Dispose(); }
            catch (IOException error) { messages.Enqueue("Could not finish writing the installation log: " + error.Message); }
        }
        DrainMessages();
        if (result == 0)
        {
            completed = true;
            installedPath = target;
            progress.Value = 100;
            status.Text = updating ? "Update complete. Saves and preferences preserved." : "Installation complete. You can now launch the game.";
            install.Text = "OPEN LAUNCHER";
            cancel.SetBounds(ContentX + ContentW - 224, 522, 224, 32);
            cancel.Text = "Create desktop shortcut";
            cancel.Enabled = true;
            try { File.WriteAllText(Path.Combine(target, "Play ReStreet.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"%~dp0FifaStreetLauncher.exe\"\r\n", Encoding.ASCII); }
            catch (IOException error) { MessageBox.Show(this, "The game was installed, but the launch shortcut could not be written.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (UnauthorizedAccessException error) { MessageBox.Show(this, "The game was installed, but the launch shortcut could not be written.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        else if (result == 1223)
        {
            status.Text = updating ? "Update cancelled. Previous files restored." : "Installation cancelled. Incomplete files remain in the selected folder.";
            install.Text = "INSTALL ReStreet";
            foreach (var input in inputs) input.Enabled = true;
            RefreshInstallMode();
        }
        else
        {
            status.Text = updating ? "Update failed. Check the details and update.log." : "Installation failed. Check the details and installation.log.";
            install.Text = "TRY AGAIN";
            foreach (var input in inputs) input.Enabled = true;
            RefreshInstallMode();
            MessageBox.Show(this, $"Installation did not finish (code {result}).\n\nLog: {logPath}\n" + (updating ? "Keep your installation and check the log before trying again." : "Choose an empty folder before trying again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void CancelInstallation()
    {
        if (!busy || cancellation == null || cancellation.IsCancellationRequested) return;
        bool updating = PrecompiledPackage.Enabled && InstallationUpdate.IsInstallation(destination.Text);
        string question = updating ? "Cancel update? The installer will restore the previous files. Keep the installer open until restoration finishes." : "Cancel installation? The game will not be ready to play. Incomplete files will remain in the selected folder.";
        if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        status.Text = "Cancelling installation…";
        cancel.Enabled = false;
        cancellation.Cancel();
    }

    internal static void CreateDesktopShortcut(string installFolder)
    {
        string launcher = Path.Combine(installFolder, "FifaStreetLauncher.exe");
        if (!File.Exists(launcher)) throw new FileNotFoundException("The installed launcher was not found.", launcher);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string shortcutPath = Path.Combine(desktop, "ReStreet.lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic automation = shell!;
            shortcut = automation.CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = launcher;
            link.WorkingDirectory = installFolder;
            link.IconLocation = launcher + ",0";
            link.Description = "ReStreet - FIFA Street 2012 Recompiled — Ported by: SamuelitoDaVila";
            link.Save();
        }
        finally
        {
            if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    void DrainMessages()
    {
        var text = new StringBuilder();
        while (messages.TryDequeue(out var line))
        {
            text.AppendLine(line);
            if (line.StartsWith("Preparing ") || line.StartsWith("Checking ") || line.StartsWith("Extracting ") || line.StartsWith("Installing ") || line.StartsWith("Recompiling ") || line.Contains("/6]")) status.Text = line;
        }
        if (text.Length == 0) return;
        if (details.TextLength > 50000) details.Clear();
        details.AppendText(text.ToString());
    }

    internal static void PrepareBundle()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FifaStreetSetup", Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToString("N"));
        string ready = Path.Combine(root, ".ready");
        if (!File.Exists(ready))
        {
            Directory.CreateDirectory(root);
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("bundle.zip")
                ?? throw new IOException("The installer does not contain the required tools.");
            using var zip = new ZipArchive(resource);
            zip.ExtractToDirectory(root, true);
            if (!PrecompiledPackage.Enabled)
                File.Copy(Path.Combine(root, "compiler", "bin", "clang.exe"), Path.Combine(root, "compiler", "bin", "clang++.exe"), true);
            File.WriteAllText(ready, "ready");
        }
        InstallerEngine.BundleRoot = root;
        Environment.SetEnvironmentVariable("FIFA_BUILD_ROOT", Path.Combine(Path.GetTempPath(), "FSB"));
    }

    static async Task EnsureWindowsTools(System.Threading.CancellationToken token)
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            var info = new ProcessStartInfo(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath" }) info.ArgumentList.Add(arg);
            using var check = Process.Start(info)!;
            string location = await check.StandardOutput.ReadToEndAsync(token);
            await check.WaitForExitAsync(token);
            if (!string.IsNullOrWhiteSpace(location)) return;
        }
        Console.WriteLine("Preparing Windows components. Accept the Windows prompt to continue.");
        string bootstrapper = Path.Combine(Path.GetTempPath(), "FifaStreet-vs-buildtools.exe");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using (var stream = await http.GetStreamAsync("https://aka.ms/vs/17/release/vs_buildtools.exe", token))
        using (var file = File.Create(bootstrapper)) await stream.CopyToAsync(file, token);
        token.ThrowIfCancellationRequested();
        // The elevated Microsoft installer manages shared system components.
        // Keep this stage explicit instead of pretending the local Cancel
        // button can safely terminate it midway through a system installation.
        Console.WriteLine("Installing Windows components. This stage must finish before cancellation can complete.");
        using var setup = Process.Start(new ProcessStartInfo(bootstrapper, "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended") { UseShellExecute = true, Verb = "runas" })!;
        await setup.WaitForExitAsync();
        token.ThrowIfCancellationRequested();
        if (setup.ExitCode != 0 && setup.ExitCode != 3010) throw new IOException($"Windows components could not be installed (code {setup.ExitCode}).");
        if (setup.ExitCode == 3010) throw new IOException("Restart Windows and run the installer again to continue.");
    }

    sealed class ProgressWriter(StreamWriter log, System.Collections.Concurrent.ConcurrentQueue<string> queue) : TextWriter
    {
        readonly object gate = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            lock (gate) { log.WriteLine(value); queue.Enqueue(value ?? ""); }
        }
        public override void WriteLine() => WriteLine("");
    }

    sealed class MessiPicture : PictureBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Image == null) return;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            // Show Messi at full panel height, excluding the cover header and footer.
            var crop = new RectangleF(Image.Width * 100f / 1280f, Image.Height * 225f / 1819f,
                Image.Width * 640f / 1280f, Image.Height * 1260f / 1819f);
            e.Graphics.DrawImage(Image, ClientRectangle, crop, GraphicsUnit.Pixel);
        }
    }
}
