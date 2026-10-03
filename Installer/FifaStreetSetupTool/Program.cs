using System;
using System.Diagnostics;
using System.IO;

namespace FifaStreetSetupTool;

internal class InstallerEngine
{
    internal static string? BundleRoot;
    internal static System.Threading.CancellationToken CancellationToken;
    internal static int Run(string[] args)
    {
        CancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine("=======================================");
        Console.WriteLine("       FIFA STREET PC SETUP TOOL");
        Console.WriteLine("=======================================");
        Console.WriteLine();

        if (args.Length < 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine();
            Console.WriteLine("FifaStreetSetupTool.exe <ISO> <InstallationFolder> [BuildWorkspace]");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine(@"FifaStreetSetupTool.exe ""D:\FIFASTREET.iso"" ""C:\Games\FIFA Street PC""");
            return 1;
        }

        string isoPath = Path.GetFullPath(args[0]);
        string installFolder = Path.GetFullPath(args[1]);
        string? extractorPath = FindExtractor();

        if (extractorPath == null)
        {
            Console.WriteLine("ERROR:");
            Console.WriteLine("extract-xiso.exe was not found.");
            Console.WriteLine();
            Console.WriteLine("Expected at:");
            Console.WriteLine(@"Installer\tools\extract-xiso.exe");
            return 2;
        }

        if (!File.Exists(isoPath))
        {
            Console.WriteLine("ERROR:");
            Console.WriteLine("The selected ISO does not exist.");
            Console.WriteLine();
            Console.WriteLine(isoPath);
            return 3;
        }

        if (!isoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("ERROR: The selected file is not an ISO.");
            return 4;
        }

        string tempFolder = Path.Combine(installFolder, ".extract-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempFolder);
            Console.WriteLine($"ISO:");
            Console.WriteLine(isoPath);
            Console.WriteLine();
            Console.WriteLine($"Destination:");
            Console.WriteLine(installFolder);
            Console.WriteLine();
            Console.WriteLine("Extracting the FIFA Street ISO...");
            Console.WriteLine();

            int extractResult = ExtractIso(extractorPath, isoPath, tempFolder);
            if (extractResult != 0)
            {
                Console.WriteLine();
                Console.WriteLine($"ERROR: extract-xiso exited with code {extractResult}.");
                return 5;
            }

            Console.WriteLine();
            Console.WriteLine("Extraction complete.");
            Console.WriteLine();
            Console.WriteLine("Locating FIFA Street files...");

            string? gameRoot = FindGameRoot(tempFolder);
            if (gameRoot == null)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: default.xex was not found in the ISO.");
                Console.WriteLine("The selected image may not be a supported version of FIFA Street.");
                return 6;
            }

            Console.WriteLine($"Files found at: {gameRoot}");
            Console.WriteLine();

            if (!ValidateGame(gameRoot)) return 7;

            string installerRoot = FindInstallerRoot()
                ?? throw new Exception("The installer package was not found.");
            if (PrecompiledPackage.Enabled)
            {
                Console.WriteLine("Checking ISO version and installation files...");
                var package = PrecompiledPackage.Validate(installerRoot, gameRoot);
                Console.WriteLine($"Supported game version: {package.Version}");
            }

            Console.WriteLine("FIFA Street files validated.");
            Console.WriteLine();

            Directory.CreateDirectory(installFolder);
            string gameDataFolder = Path.Combine(installFolder, "GameData");
            string gameFolder = Path.Combine(installFolder, "Game");

            Console.WriteLine("Installing game data...");
            Console.WriteLine();

            if (Directory.Exists(gameDataFolder))
            {
                Console.WriteLine("An existing GameData installation was found.");
                Console.WriteLine("Existing files will be updated.");
                Console.WriteLine();
            }

            CancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(gameDataFolder))
                Directory.Move(gameRoot, gameDataFolder);
            else
                CopyDirectory(gameRoot, gameDataFolder);
            Directory.CreateDirectory(gameFolder);

            string payload = Path.Combine(installerRoot, "payload");
            if (!Directory.Exists(payload))
                throw new Exception("The payload folder was not found.");
            CopyDirectory(payload, installFolder);

            Console.WriteLine("Applying the start screen and main menu credits...");
            CreditPatch.Apply(gameDataFolder);

            if (!PrecompiledPackage.Enabled)
            {
                Console.WriteLine("Recompiling the game on your PC. This may take some time.");
                var buildInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (string argument in new[] {
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(installerRoot, "BuildFifaStreet.ps1"),
                    "-GameData", gameDataFolder, "-Output", gameFolder })
                    buildInfo.ArgumentList.Add(argument);
                if (args.Length >= 3)
                {
                    buildInfo.ArgumentList.Add("-Workspace");
                    buildInfo.ArgumentList.Add(Path.GetFullPath(args[2]));
                }

                int buildResult = RunProcess(buildInfo);
                if (buildResult != 0)
                    throw new Exception($"Recompilation failed with code {buildResult}. Check the build log for details.");
            }
            else
                Console.WriteLine("Installing precompiled game files. No compiler is required.");

            foreach (string binary in new[] {
                "fifastreet.exe",
                "fifastreet_fifadllzf_xex.dll",
                "fifastreet_FootballCompEngzf_xex.dll"
            })
                if (!File.Exists(Path.Combine(gameFolder, binary)))
                    throw new Exception($"The build did not create {binary}.");

            Console.WriteLine();
            Console.WriteLine("Game files installed successfully.");
            Console.WriteLine();
            Console.WriteLine("=======================================");
            Console.WriteLine("       INSTALLATION COMPLETE");
            Console.WriteLine("=======================================");
            Console.WriteLine();
            Console.WriteLine("Installation folder:");
            Console.WriteLine();
            Console.WriteLine(installFolder);
            Console.WriteLine();
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Installation cancelled.");
            return 1223;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("UNEXPECTED ERROR:");
            Console.WriteLine(ex.Message);
            return 100;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Console.WriteLine();
                    Console.WriteLine("Cleaning temporary files...");
                    Directory.Delete(tempFolder, true);
                }
            }
            catch
            {
                Console.WriteLine("Warning: Some temporary files could not be removed.");
            }
        }
    }

    static string? FindExtractor()
    {
        string? installerRoot = FindInstallerRoot();
        if (installerRoot != null)
        {
            string extractor = Path.Combine(installerRoot, "tools", "extract-xiso.exe");
            if (File.Exists(extractor)) return extractor;
        }

        string exeFolder = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(exeFolder, "tools", "extract-xiso.exe"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "tools", "extract-xiso.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "extract-xiso.exe"))
        };

        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    static string? FindInstallerRoot()
    {
        if (BundleRoot != null) return BundleRoot;
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (DirectoryInfo? folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "BuildFifaStreet.ps1")) ||
                    (PrecompiledPackage.Enabled && File.Exists(Path.Combine(folder.FullName, "precompiled.json"))))
                    return folder.FullName;
        return null;
    }

    static int ExtractIso(string extractor, string iso, string destination)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = extractor,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(destination);
        startInfo.ArgumentList.Add(iso);
        return RunProcess(startInfo);
    }

    internal static int RunProcess(ProcessStartInfo startInfo)
    {
        CancellationToken.ThrowIfCancellationRequested();
        using Process? process = Process.Start(startInfo);
        if (process == null) throw new Exception($"Could not start {startInfo.FileName}.");

        using var cancellation = CancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        CancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    static string? FindGameRoot(string folder)
    {
        string directXex = Path.Combine(folder, "default.xex");
        if (File.Exists(directXex)) return folder;

        foreach (string file in Directory.EnumerateFiles(folder, "default.xex", SearchOption.AllDirectories))
            return Path.GetDirectoryName(file);
        return null;
    }

    static bool ValidateGame(string folder)
    {
        string[] requiredFiles = { "default.xex", "fifadllzf.xex.dll", "data0.big", "data1.big", "data1.bh" };
        foreach (string required in requiredFiles)
        {
            string path = Path.Combine(folder, required);
            if (!File.Exists(path))
            {
                Console.WriteLine($"ERROR: Required file missing: {required}");
                return false;
            }
        }

        string worldTourModule = Path.Combine(folder, "dlc", "dlc_FootballCompEng", "dlc", "FootballCompEng", "FootballCompEngzf.xex.dll");
        if (!File.Exists(worldTourModule))
        {
            Console.WriteLine("ERROR: Required World Tour module missing: FootballCompEngzf.xex.dll");
            return false;
        }

        return true;
    }

    static void CopyDirectory(string sourceDir, string destinationDir)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            CancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }
}
