using System.Diagnostics;
using Photino.NET;

namespace FifaStreetLauncher.Linux;

// ReStreet launcher for Linux: the Windows launcher's page (ui/launcher.html) in a native WebKitGTK window, with the
// game started through Proton (umu-run). The page talks to this process over 127.0.0.1 exactly as it does with the
// Windows launcher under Wine (PageServer.cs), so the page needs no Linux-specific code.
internal static class Program
{
    private static int Main(string[] args)
    {
        string? root = null;
        bool play = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
            else if (args[i] == "--play") play = true;
        }
        root ??= InstallFolder.Find();
        if (root == null)
        {
            Console.Error.WriteLine("ReStreet folder not found. Run from the ReStreet folder or pass --root <folder>.");
            return 1;
        }

        var launcher = new Launcher(root);
        // Direct play: no window, the game starts with the saved settings and the launcher exits with it.
        if (play) return launcher.PlayAndWait();

        using var server = new PageServer(Path.Combine(AppContext.BaseDirectory, "ui"), launcher);
        launcher.Page = server;
        launcher.StartServices();
        try
        {
            PhotinoWindow? window = null;
            window = new PhotinoWindow()
                .SetLogVerbosity(0)
                .SetTitle("ReStreet Launcher")
                .SetUseOsDefaultSize(false)
                .SetSize(1250, 760)
                .SetMinSize(1000, 620)
                .Center()
                .SetIconFile(Path.Combine(AppContext.BaseDirectory, "restreet.jpg"))
                .SetContextMenuEnabled(false)
                .SetDevToolsEnabled(false)
                .Load(new Uri(server.Url));
            launcher.CloseWindow = () => window.Invoke(window.Close);
            window.WaitForClose();
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            // No WebKitGTK: the same page in the system browser, as the Windows launcher does under Wine.
            Console.Error.WriteLine("WebKitGTK is not available (" + ex.Message + "); opening the launcher in the browser.");
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { server.Url }, UseShellExecute = false });
            launcher.WaitWhileBrowserConnected(server);
        }
        launcher.Shutdown();
        return 0;
    }
}
