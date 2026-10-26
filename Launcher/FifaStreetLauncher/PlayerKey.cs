using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FifaStreetLauncher;

// The player's personal online key (online.json "key"): the server binds the online name to the first key it sees,
// so a reinstall, which writes a new online.json, must not lose it. The launcher keeps a copy outside the game folder
// (%LOCALAPPDATA%\ReStreet\player-key.json, ~/.local/share/ReStreet on Linux) and puts it back into online.json when
// a fresh install brought another key for the same name. A recovery code (RS1-...) carries the name and the key to
// another PC. Nothing here ever drops a key: one that is replaced stays in the backup under "previous".
// Shared by the Windows and the Linux launcher.
internal static class PlayerKey
{
    private const int MinKey = 16;       // the server binds no shorter key (friends.py MIN_KEY)
    private const string CodePrefix = "RS1-";

    internal sealed record Result(bool Ok, string Note);

    // RESTREET_KEY_BACKUP points the copy elsewhere, for tests.
    internal static string BackupPath() =>
        Environment.GetEnvironmentVariable("RESTREET_KEY_BACKUP") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReStreet", "player-key.json");

    // At launcher start, with the online.json copies (the first is the one the launcher reads). Makes them and the
    // backup agree; online.json without a key gets a new one so the name can be claimed.
    internal static void Sync(IReadOnlyList<string> onlineFiles)
    {
        var (name, key) = Read(onlineFiles);
        if (name.Length == 0) return;
        var backup = LoadBackup();
        var saved = backup[Id(name)] as JsonObject;
        string savedKey = saved?["key"]?.GetValue<string>() ?? "";
        if (savedKey.Length >= MinKey)
        {
            if (savedKey == key) return;
            Remember(saved!, key);
            if (Write(onlineFiles, name, savedKey)) SaveBackup(backup);
            return;
        }
        if (key.Length < MinKey && !Write(onlineFiles, name, key = NewKey())) return;
        backup[Id(name)] = new JsonObject { ["name"] = name, ["key"] = key, ["saved"] = DateTime.UtcNow.ToString("s") };
        SaveBackup(backup);
    }

    // The code shown in Advanced, or "" when online is not set up.
    internal static string RecoveryCode(IReadOnlyList<string> onlineFiles)
    {
        var (name, key) = Read(onlineFiles);
        return name.Length == 0 || key.Length < MinKey ? "" : CodePrefix + Base64Url(Encoding.UTF8.GetBytes(name + "\n" + key));
    }

    // A code from another PC: its name and key go into online.json (and the backup) here.
    internal static Result UseCode(IReadOnlyList<string> onlineFiles, string? code)
    {
        if (!TryParse(code, out string name, out string key)) return new(false, "That is not a recovery code. Copy the whole code, starting with RS1-.");
        var (current, currentKey) = Read(onlineFiles);
        if (current.Length == 0) return new(false, "Online is not set up. Run the online patch again.");
        if (current == name && currentKey == key) return new(true, "This PC already uses this code.");
        var backup = LoadBackup();
        // The key this PC had stays in the backup, under its own name.
        if (currentKey.Length > 0)
        {
            if (backup[Id(current)] is not JsonObject old) backup[Id(current)] = new JsonObject { ["name"] = current, ["key"] = currentKey };
            else if (old["key"]?.GetValue<string>() != currentKey) Remember(old, currentKey);
        }
        var entry = backup[Id(name)] as JsonObject ?? new JsonObject();
        if (entry["key"]?.GetValue<string>() is { Length: > 0 } before && before != key) Remember(entry, before);
        entry["name"] = name;
        entry["key"] = key;
        entry["saved"] = DateTime.UtcNow.ToString("s");
        backup[Id(name)] = entry;
        if (!Write(onlineFiles, name, key)) return new(false, "Could not write online.json. Close the game and try again.");
        SaveBackup(backup);
        return new(true, "Done. This PC now plays online with the name and key from the code.");
    }

    internal static bool TryParse(string? code, out string name, out string key)
    {
        name = key = "";
        string text = new((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!text.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string payload = text[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\n');
            if (parts.Length != 2) return false;
            (name, key) = (parts[0].Trim(), parts[1].Trim());
        }
        catch (FormatException) { return false; }
        // The server's rules for a name (friends.py valid_name) and a key.
        return name.Length is >= 1 and <= 32 && !name.Any(c => c < 0x20 || c == 0x7f || "\\\"{}[]".Contains(c))
            && key.Length is >= MinKey and <= 128;
    }

    private static (string Name, string Key) Read(IReadOnlyList<string> onlineFiles)
    {
        try
        {
            if (onlineFiles.Count == 0 || !File.Exists(onlineFiles[0])) return ("", "");
            var node = JsonNode.Parse(File.ReadAllText(onlineFiles[0])) as JsonObject;
            return ((node?["name"]?.GetValue<string>() ?? "").Trim(), (node?["key"]?.GetValue<string>() ?? "").Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ("", "");
        }
    }

    // Name and key into every online.json copy that exists; the other fields (server, ...) stay as they are.
    private static bool Write(IReadOnlyList<string> onlineFiles, string name, string key)
    {
        bool wrote = false;
        foreach (string path in onlineFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
                node["name"] = name;
                node["key"] = key;
                WriteAtomic(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                wrote = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                if (path == onlineFiles[0]) return false;
            }
        }
        return wrote;
    }

    // Keeps |key| in the entry's "previous" list (when it is not the entry's key, or is about to be replaced).
    private static void Remember(JsonObject entry, string key)
    {
        if (key.Length == 0) return;
        var previous = entry["previous"] as JsonArray ?? new JsonArray();
        if (!previous.Any(p => p?.GetValue<string>() == key)) previous.Add(JsonValue.Create(key));
        while (previous.Count > 5) previous.RemoveAt(0);
        entry["previous"] = previous;
    }

    private static JsonObject LoadBackup()
    {
        try
        {
            string path = BackupPath();
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o ? o : new JsonObject();
        }
        catch (JsonException)
        {
            // A damaged copy is put aside (never deleted) before a new one is written.
            try { File.Copy(BackupPath(), BackupPath() + ".bad", true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return new JsonObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }

    private static void SaveBackup(JsonObject backup)
    {
        try
        {
            string path = BackupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the next start tries again */ }
    }

    private static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    private static string Id(string name) => name.Trim().ToLowerInvariant();

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
