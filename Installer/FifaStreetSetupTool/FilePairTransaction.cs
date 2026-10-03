namespace FifaStreetSetupTool;

// A recoverable transaction for the BIG archive and its companion index.
// Recovery copies backups (rather than moving them), so recovery itself can
// be interrupted and repeated without losing either original.
internal static class FilePairTransaction
{
    static void WriteState(string marker, string state)
    {
        string temporary = marker + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(state);
            file.Write(bytes);
            file.Flush(true);
        }
        File.Move(temporary, marker, true);
    }

    internal static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException error) { Console.WriteLine("Cleanup pending: " + error.Message); return false; }
        catch (UnauthorizedAccessException error) { Console.WriteLine("Cleanup pending: " + error.Message); return false; }
    }

    static void Cleanup(string first, string second, string marker)
    {
        bool cleaned = TryDelete(first + ".credit.rollback");
        cleaned &= TryDelete(second + ".credit.rollback");
        cleaned &= TryDelete(first + ".credit.tmp");
        cleaned &= TryDelete(second + ".credit.tmp");
        cleaned &= TryDelete(marker + ".tmp");
        // Retain the state until cleanup is finished. A cleanup failure must
        // never turn a successfully committed new pair back into an old pair.
        if (cleaned) TryDelete(marker);
    }

    internal static void Recover(string first, string second)
    {
        string marker = first + ".credit.transaction";
        if (!File.Exists(marker)) return;
        string state = File.ReadAllText(marker);
        if (state == "prepared")
        {
            foreach (string target in new[] { first, second })
            {
                string backup = target + ".credit.rollback";
                if (File.Exists(backup)) File.Copy(backup, target, true);
            }
            // After both originals are restored, a crash during cleanup is
            // safe: the following startup only needs to finish cleanup.
            WriteState(marker, "recovered");
        }
        else if (state != "committed" && state != "recovered")
            throw new InvalidDataException("Unknown menu patch recovery state.");
        Cleanup(first, second, marker);
    }

    internal static void Commit(string first, string stagedFirst, string second,
                                string stagedSecond, Action<string>? checkpoint = null)
    {
        Recover(first, second);
        string marker = first + ".credit.transaction";
        if (File.Exists(marker) || File.Exists(first + ".credit.rollback") ||
            File.Exists(second + ".credit.rollback"))
            throw new IOException("Previous menu patch recovery must finish before retrying.");
        WriteState(marker, "prepared");
        try
        {
            File.Replace(stagedFirst, first, first + ".credit.rollback");
            checkpoint?.Invoke("after-first");
            File.Replace(stagedSecond, second, second + ".credit.rollback");
            checkpoint?.Invoke("after-second");
            WriteState(marker, "committed");
        }
        catch
        {
            Recover(first, second);
            throw;
        }
        Cleanup(first, second, marker);
    }
}
