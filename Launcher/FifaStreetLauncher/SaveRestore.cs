using System.Diagnostics;
using System.Globalization;

namespace FifaStreetLauncher;

// World Tour saves the game deleted, put back from the launcher (Advanced > Saves).
// The game's runtime never destroys a deleted save: it moves it to <profile>\<title>\_apagados\<unix time>-<name>\
// (folder "dados" + "<name>.header") and keeps the newest 10 (content_manager.cpp). Every game save deletes the
// previous one, so a player who lost a career finds it there. Restoring: the whole title folder is copied to
// Documents\fifastreet\_launcher-backups\<time>\ first, the current save goes to _apagados the same way the game
// would move it, and the chosen save is copied (not moved) back. Nothing is done while the game runs.
public partial class LauncherForm
{
    private const string SaveTitleId = "4541097B";
    private const string SaveType = "00000001";  // saved games
    private const string TrashDir = "_apagados", CopyDir = "_copia", HeadersDir = "Headers", BackupsDir = "_launcher-backups";

    // Documents\fifastreet, the game's user data root (RESTREET_SAVES_ROOT points it elsewhere, for tests).
    internal static string SavesRoot() =>
        Environment.GetEnvironmentVariable("RESTREET_SAVES_ROOT") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fifastreet");

    // <root>\<16 hex profile>\4541097B for every profile folder that has one.
    private static IEnumerable<string> SaveTitleFolders(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string profile in Directory.GetDirectories(root))
        {
            string name = Path.GetFileName(profile);
            if (name.Length != 16 || !name.All(Uri.IsHexDigit)) continue;
            string title = Path.Combine(profile, SaveTitleId);
            if (Directory.Exists(title)) yield return title;
        }
    }

    private static long FolderSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // "Settings 20261026011248A" -> the time the game wrote it (its own local clock), or null.
    private static DateTime? SaveTimeOf(string saveName)
    {
        string digits = new(saveName.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return digits.Length == 14 && DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t) ? t : null;
    }

    internal sealed record SaveEntry(string Id, string Kind, string Name, string? Saved, string? Deleted, long Size, string Profile);

    // The current saves and the deleted ones, newest first. Id = path relative to the root (what restore receives).
    internal static List<SaveEntry> ListSaves(string root)
    {
        var list = new List<SaveEntry>();
        foreach (string title in SaveTitleFolders(root))
        {
            string profile = Path.GetFileName(Path.GetDirectoryName(title)!);
            string current = Path.Combine(title, SaveType);
            if (Directory.Exists(current))
                foreach (string dir in Directory.GetDirectories(current))
                {
                    string name = Path.GetFileName(dir);
                    list.Add(new SaveEntry(Path.GetRelativePath(root, dir), "current", name,
                        SaveTimeOf(name)?.ToString("s"), null, FolderSize(dir), profile));
                }
            string trash = Path.Combine(title, TrashDir);
            if (Directory.Exists(trash))
                foreach (string dir in Directory.GetDirectories(trash))
                {
                    string folder = Path.GetFileName(dir);
                    int dash = folder.IndexOf('-');
                    if (dash <= 0 || !long.TryParse(folder[..dash], out long unix)) continue;
                    string name = folder[(dash + 1)..];
                    string data = Path.Combine(dir, "dados");
                    if (!Directory.Exists(data) || !File.Exists(Path.Combine(dir, name + ".header"))) continue;
                    list.Add(new SaveEntry(Path.GetRelativePath(root, dir), "deleted", name, SaveTimeOf(name)?.ToString("s"),
                        DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().DateTime.ToString("s"), FolderSize(data), profile));
                }
        }
        return list.OrderBy(e => e.Kind == "current" ? 0 : 1).ThenByDescending(e => e.Saved ?? "").ToList();
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
        foreach (string dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    // Puts a deleted save back. Returns null when done, else the reason (shown to the player).
    internal static string? RestoreSave(string root, string id, DateTime now)
    {
        string entry = Path.GetFullPath(Path.Combine(root, id));
        string trash = Path.GetDirectoryName(entry)!;
        string title = Path.GetDirectoryName(trash)!;
        // Only a folder directly inside some profile's _apagados, under this root.
        if (!entry.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(trash) != TrashDir || !SaveTitleFolders(root).Any(t => Path.GetFullPath(t) == title))
            return "That save is not in the deleted saves folder";
        string folder = Path.GetFileName(entry);
        int dash = folder.IndexOf('-');
        string name = dash > 0 ? folder[(dash + 1)..] : "";
        string data = Path.Combine(entry, "dados"), header = Path.Combine(entry, name + ".header");
        if (name.Length == 0 || !Directory.Exists(data) || !File.Exists(header) || FolderSize(data) == 0)
            return "That save is incomplete and cannot be restored";

        // 1. The whole title folder as it is now, before anything changes.
        string stamp = now.ToString("yyyyMMdd-HHmmss");
        string backup = Path.Combine(root, BackupsDir, stamp, Path.GetFileName(Path.GetDirectoryName(title)!), SaveTitleId);
        if (Directory.Exists(backup)) return "A backup with this time already exists, try again in a second";
        try { CopyFolder(title, backup); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { Directory.Delete(Path.Combine(root, BackupsDir, stamp), true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }  // the error below matters
            return "Could not make the backup (" + ex.Message + "): nothing was changed";
        }

        // 2. The current saves go to _apagados the way the game moves a deleted save (their copies in _copia too).
        string current = Path.Combine(title, SaveType), headers = Path.Combine(title, HeadersDir, SaveType);
        long unix = new DateTimeOffset(now).ToUnixTimeSeconds();
        var moved = new List<(string Dir, string Header, string To, string Name)>();
        if (Directory.Exists(current))
            foreach (string dir in Directory.GetDirectories(current))
            {
                string curName = Path.GetFileName(dir);
                string to = Path.Combine(trash, unix + "-" + curName);
                if (Directory.Exists(to)) to += "-" + Guid.NewGuid().ToString("N")[..6];
                Directory.CreateDirectory(to);
                Directory.Move(dir, Path.Combine(to, "dados"));
                string curHeader = Path.Combine(headers, curName + ".header");
                if (File.Exists(curHeader)) File.Move(curHeader, Path.Combine(to, curName + ".header"));
                moved.Add((dir, curHeader, to, curName));
            }

        // 3. The chosen save, copied back (it stays in _apagados too). A failure puts the current save back.
        string target = Path.Combine(current, name), targetHeader = Path.Combine(headers, name + ".header");
        try
        {
            CopyFolder(data, target);
            Directory.CreateDirectory(headers);
            File.Copy(header, targetHeader, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (File.Exists(targetHeader)) File.Delete(targetHeader);
            foreach (var m in moved)
            {
                Directory.Move(Path.Combine(m.To, "dados"), m.Dir);
                string h = Path.Combine(m.To, m.Name + ".header");
                if (File.Exists(h)) File.Move(h, m.Header);
                Directory.Delete(m.To, true);
            }
            return "Could not restore (" + ex.Message + "): nothing was changed, a backup is in " + backup;
        }
        // The game's own copies of the saves that left (it would never use them again: their saves are gone).
        foreach (var m in moved)
        {
            string copy = Path.Combine(title, CopyDir, m.Name);
            if (Directory.Exists(copy)) Directory.Delete(copy, true);
        }
        return null;
    }

    private static bool AnyGameRunning() =>
        Process.GetProcessesByName("fifastreet").Any(p => { using (p) return !p.HasExited; });

    private void PushSaves(string? note = null, bool ok = true)
    {
        string root = SavesRoot();
        List<SaveEntry> saves;
        try { saves = ListSaves(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { saves = new(); note ??= "Could not read the saves folder"; ok = false; }
        PostToPage(new { type = "saves", root, saves, note, ok });
    }

    private void RestoreSaveFromPage(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (gameRunning || AnyGameRunning()) { PushSaves("Close the game first, then restore the save", false); return; }
        string? error;
        try { error = RestoreSave(SavesRoot(), id, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = "Could not restore: " + ex.Message; }
        PushSaves(error ?? "Save restored. Your previous save and a full backup were kept.", error == null);
    }
}
