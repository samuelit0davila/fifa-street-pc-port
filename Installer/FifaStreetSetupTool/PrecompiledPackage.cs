using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;

namespace FifaStreetSetupTool;

internal sealed class PrecompiledPackage
{
    public int Format { get; set; }
    public string Version { get; set; } = "";
    public Dictionary<string, string> Inputs { get; set; } = new();
    public Dictionary<string, string> Payload { get; set; } = new();

    internal static bool Enabled => Assembly.GetExecutingAssembly().GetManifestResourceInfo("precompiled-mode") != null;

    internal static PrecompiledPackage Validate(string bundleRoot, string gameRoot)
    {
        var package = JsonSerializer.Deserialize<PrecompiledPackage>(File.ReadAllText(Path.Combine(bundleRoot, "precompiled.json")))
            ?? throw new IOException("The precompiled package manifest is missing.");
        string[] inputs = { "default.xex", "fifadllzf.xex.dll", "dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll" };
        string[] required = { "FifaStreetLauncher.exe", "Game/fifastreet.exe", "Game/fifastreet_fifadllzf_xex.dll", "Game/fifastreet_FootballCompEngzf_xex.dll", "Game/fifastreet.toml", "Game/rexruntime.dll", "Game/rexgpu-xenos.dll", "Game/TracyClient.dll", "Game/msvcp140.dll", "Game/msvcp140_atomic_wait.dll", "Game/vcruntime140.dll", "Game/vcruntime140_1.dll" };
        if (package.Format != 1 || package.Inputs.Count != inputs.Length || inputs.Any(x => !package.Inputs.ContainsKey(x)) || required.Any(x => !package.Payload.ContainsKey(x)))
            throw new IOException("The precompiled package manifest is incomplete.");
        foreach (string backend in new[] { "D3D12", "Vulkan" })
            foreach (string name in new[] { "rexruntime.dll", "rexgpu-xenos.dll", "TracyClient.dll" })
                if (!package.Payload.ContainsKey($"Game/Backends/{backend}/{name}"))
                    throw new IOException($"The {backend} graphics backend is incomplete.");
        foreach (var input in package.Inputs)
            if (!Matches(gameRoot, input.Key, input.Value))
                throw new IOException($"This ISO version is not supported by this installer ({input.Key} differs). No compilation tools will be installed. Use the source installer for other versions.");
        string payload = Path.Combine(bundleRoot, "payload");
        foreach (var file in package.Payload)
            if (!Matches(payload, file.Key, file.Value))
                throw new IOException($"The installer package is damaged: {file.Key}. Download a fresh copy.");
        foreach (string file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
            if (!package.Payload.ContainsKey(Path.GetRelativePath(payload, file).Replace('\\', '/')))
                throw new IOException("The installer contains an unexpected payload file.");
        return package;
    }

    static bool Matches(string root, string relative, string expected)
    {
        InstallerEngine.CancellationToken.ThrowIfCancellationRequested();
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new IOException("Invalid path or checksum in the installer manifest.");
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        InstallerEngine.CancellationToken.ThrowIfCancellationRequested();
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
