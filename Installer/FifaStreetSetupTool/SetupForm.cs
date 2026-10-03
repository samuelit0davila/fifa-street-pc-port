using System.Diagnostics;
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
        if (args.Length == 2 && args[0] == "--preview")
        {
            using var form = new SetupForm();
            form.Show();
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

internal sealed class SetupForm : Form
{
    readonly TextBox iso = new();
    readonly TextBox destination = new();
    readonly TextBox details = new();
    readonly Button install = new();
    readonly Button cancel = new();
    System.Threading.CancellationTokenSource? cancellation;
    readonly ProgressBar progress = new();
    readonly Label status = new();
    readonly List<Control> inputs = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    readonly System.Collections.Concurrent.ConcurrentQueue<string> messages = new();
    bool busy;
    bool completed;
    string installedPath = "";
    static readonly Color Accent = Color.FromArgb(133, 255, 72);
    internal const string Credit = "PORTED BY: SAMUELITODAVILA";

    internal SetupForm()
    {
        Text = "FIFA Street PC — Setup";
        using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("fifastreet.ico")!)
            Icon = new Icon(icon);
        ClientSize = new Size(930, 610);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(10, 12, 16);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("cover.jpg")
            ?? throw new InvalidOperationException("The installer artwork is missing.");
        using var source = Image.FromStream(resource);
        var artwork = new MessiPicture { Bounds = new Rectangle(0, 0, 310, 610), Image = new Bitmap(source), BackColor = Color.Black };
        Controls.Add(artwork);
        Controls.Add(Label("FIFA STREET", 340, 27, 550, 50, 30, Accent));
        Controls.Add(Label("PC INSTALLER", 343, 80, 540, 30, 12, Color.LightGray));
        Controls.Add(Label(Credit + "\nContributions: Emran_Ahm3d", 343, 113, 545, 40, 10, Accent));
        Controls.Add(Label(PrecompiledPackage.Enabled
            ? "New installation: select your ISO. To update, choose your existing game folder below (the folder containing Game and GameData)."
            : "Select your FIFA Street ISO and choose where to install the game.", 343, 158, 548, 48, 9));
        AddPath("FIFA Street ISO", iso, 209, () =>
        {
            using var dialog = new OpenFileDialog { Title = "Select FIFA Street ISO", Filter = "ISO image (*.iso)|*.iso", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) iso.Text = dialog.FileName;
        });
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Games", "FIFA Street PC");
        AddPath(PrecompiledPackage.Enabled ? "Installation folder / existing game folder to update" : "Installation folder", destination, 288, () =>
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
        destination.TextChanged += (_, _) => RefreshInstallMode();
        status.SetBounds(343, 373, 550, 43);
        status.Text = PrecompiledPackage.Enabled ? "Your ISO supplies the game data. Ready-to-play binaries are included." : "Game files will be created on your PC from your ISO.";
        Controls.Add(status);
        progress.SetBounds(343, 423, 548, 10);
        Controls.Add(progress);
        var note = Label(PrecompiledPackage.Enabled ? "No compiler, Visual Studio or Internet connection required." : "An Internet connection may be needed to set up Windows components.", 343, 448, 548, 35, 9, Color.Silver);
        Controls.Add(note);
        install.SetBounds(343, 500, 548, 46);
        install.Text = "INSTALL FIFA STREET";
        install.BackColor = Accent;
        install.ForeColor = Color.Black;
        install.FlatStyle = FlatStyle.Flat;
        install.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        install.Click += async (_, _) => await Install();
        AcceptButton = install;
        Controls.Add(install);
        var showDetails = new Button { Text = "Show details", Bounds = new Rectangle(343, 559, 130, 30), FlatStyle = FlatStyle.Flat, ForeColor = Color.LightGray };
        showDetails.Click += (_, _) =>
        {
            details.Visible = !details.Visible;
            showDetails.Text = details.Visible ? "Hide details" : "Show details";
            MaximumSize = Size.Empty;
            Height += details.Visible ? 180 : -180;
            MaximumSize = Size;
        };
        Controls.Add(showDetails);
        cancel.Text = "Cancel";
        cancel.SetBounds(761, 559, 130, 30);
        cancel.FlatStyle = FlatStyle.Flat;
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
        details.SetBounds(20, 620, 885, 155);
        details.Multiline = true;
        details.ReadOnly = true;
        details.ScrollBars = ScrollBars.Vertical;
        details.BackColor = Color.FromArgb(18, 22, 28);
        details.ForeColor = Color.Gainsboro;
        details.Visible = false;
        Controls.Add(details);
        timer.Tick += (_, _) => DrainMessages();
        timer.Start();
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            CancelInstallation();
        };
        FormClosed += (_, _) => { timer.Dispose(); artwork.Image?.Dispose(); };
        RefreshInstallMode();
    }

    void RefreshInstallMode()
    {
        if (busy || completed || !PrecompiledPackage.Enabled) return;
        bool updating = InstallationUpdate.IsInstallation(destination.Text);
        install.Text = updating ? "UPDATE FIFA STREET" : "INSTALL FIFA STREET";
        iso.Enabled = !updating;
        inputs[1].Enabled = !updating;
        status.Text = updating ? "Existing installation found. Click UPDATE FIFA STREET. No ISO required; saves and preferences are preserved." : "Select your ISO for a new installation, or choose your existing game folder to update.";
    }

    static Label Label(string text, int x, int y, int width, int height, float size = 10, Color? color = null) =>
        new() { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font("Segoe UI", size, size >= 20 ? FontStyle.Bold : FontStyle.Regular), ForeColor = color ?? Color.White };

    void AddPath(string label, TextBox box, int y, Action browse)
    {
        Controls.Add(Label(label, 343, y, 548, 25));
        box.SetBounds(343, y + 28, 432, 30);
        box.BackColor = Color.FromArgb(25, 29, 36);
        box.ForeColor = Color.White;
        box.AccessibleName = label;
        Controls.Add(box);
        var button = new Button { Text = "Browse…", Bounds = new Rectangle(785, y + 25, 106, 33), FlatStyle = FlatStyle.Flat };
        button.AccessibleName = "Browse " + label;
        button.Click += (_, _) => browse();
        Controls.Add(button);
        inputs.Add(box);
        inputs.Add(button);
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
            cancel.SetBounds(620, 559, 271, 30);
            cancel.Text = "Create desktop shortcut";
            cancel.Enabled = true;
            try { File.WriteAllText(Path.Combine(target, "Play FIFA Street.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"%~dp0FifaStreetLauncher.exe\"\r\n", Encoding.ASCII); }
            catch (IOException error) { MessageBox.Show(this, "The game was installed, but the launch shortcut could not be written.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (UnauthorizedAccessException error) { MessageBox.Show(this, "The game was installed, but the launch shortcut could not be written.\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        else if (result == 1223)
        {
            status.Text = updating ? "Update cancelled. Previous files restored." : "Installation cancelled. Incomplete files remain in the selected folder.";
            install.Text = "INSTALL FIFA STREET";
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
        string shortcutPath = Path.Combine(desktop, "FIFA Street PC.lnk");
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
            link.Description = "FIFA Street PC — " + Credit;
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
