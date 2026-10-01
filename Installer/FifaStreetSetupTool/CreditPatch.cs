using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework.Content;

namespace FifaStreetSetupTool;

internal static class CreditPatch
{
    sealed record Edit(string Name, string SourceHash, string DecodedHash, string OutputHash, int Length, byte[] Xor);
    sealed record Recipe(string Name, string SourceHash, string OutputHash, Edit[] Edits);
    sealed record Entry(string Name, int Offset, int Length, int TablePosition);
    static int U(byte[] b, int p) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4)));
    static void Put(byte[] b, int p, int n) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p, 4), checked((uint)n));
    static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

    static List<Entry> Index(byte[] b, long fileLength)
    {
        Require(b.Length >= 16 && (Encoding.ASCII.GetString(b, 0, 4) is "BIG4" or "BIGF"), "Unsupported BIG archive.");
        int count = U(b, 8), end = U(b, 12), p = 16;
        Require(count <= 100000 && end <= b.Length, "Invalid BIG directory.");
        var entries = new List<Entry>();
        for (int i = 0; i < count; i++)
        {
            Require(p + 8 < end, "Truncated BIG directory.");
            int offset = U(b, p), length = U(b, p + 4), zero = Array.IndexOf(b, (byte)0, p + 8, end - p - 8);
            Require(zero >= 0 && offset >= end && (long)offset + length <= fileLength, "Invalid BIG entry.");
            entries.Add(new(Encoding.UTF8.GetString(b, p + 8, zero - p - 8), offset, length, p)); p = zero + 1;
        }
        return entries;
    }

    internal static byte[] DecodeChunk(byte[] b)
    {
        Require(b.Length > 48 && Encoding.ASCII.GetString(b, 0, 8) == "chunklzx" && U(b, 8) == 2, "Unsupported menu compression.");
        int full = U(b, 12), chunk = U(b, 16), p = 40;
        Require(full <= 16 * 1024 * 1024 && chunk is > 0 and <= 1024 * 1024, "Invalid menu size.");
        using var output = new MemoryStream();
        while (output.Length < full)
        {
            InstallerEngine.CancellationToken.ThrowIfCancellationRequested();
            p = (p + 7) / 16 * 16 + 8;
            int n = U(b, p), kind = U(b, p + 4); p += 8;
            Require(n > 0 && p + (long)n <= b.Length, "Truncated menu chunk.");
            int target = Math.Min(chunk, full - (int)output.Length), end = p + n;
            if (kind == 4) { Require(n == target, "Invalid raw chunk."); output.Write(b, p, n); p = end; continue; }
            Require(kind == 3, "Unknown menu chunk type.");
            var decoder = new LzxDecoder(17); int produced = 0;
            while (p < end && produced < target)
            {
                int outSize = 32768, inSize;
                if (b[p] == 255) { Require(p + 5 <= end, "Truncated LZX frame."); outSize = (b[p + 1] << 8) | b[p + 2]; inSize = (b[p + 3] << 8) | b[p + 4]; p += 5; }
                else { Require(p + 2 <= end, "Truncated LZX frame."); inSize = (b[p] << 8) | b[p + 1]; p += 2; }
                Require(inSize > 0 && p + inSize <= end && outSize > 0 && produced + outSize <= target, "Invalid LZX frame.");
                using var input = new MemoryStream(b, p, inSize, false);
                Require(decoder.Decompress(input, inSize, output, outSize) == 0, "Menu decompression failed.");
                p += inSize; produced += outSize;
            }
            Require(produced == target, "Incomplete menu chunk."); p = end;
        }
        Require(output.Length == full, "Incomplete menu archive."); return output.ToArray();
    }

    static byte[] DecodeRefPack(byte[] b)
    {
        if (b.Length < 5 || b[0] != 0x10 || b[1] != 0xfb) return b;
        int length = (b[2] << 16) | (b[3] << 8) | b[4], p = 5, at = 0;
        Require(length <= 16 * 1024 * 1024, "Invalid texture size."); var result = new byte[length];
        while (at < length)
        {
            int c = b[p++], literals, copies = 0, distance = 0;
            if (c < 0x80) { int d = b[p++]; literals = c & 3; copies = ((c & 0x1c) >> 2) + 3; distance = ((c & 0x60) << 3) + d + 1; }
            else if (c < 0xc0) { int d = b[p++], e = b[p++]; literals = d >> 6; copies = (c & 0x3f) + 4; distance = ((d & 0x3f) << 8) + e + 1; }
            else if (c < 0xe0) { int d = b[p++], e = b[p++], f = b[p++]; literals = c & 3; copies = ((c & 0x0c) << 6) + f + 5; distance = ((c & 0x10) << 12) + (d << 8) + e + 1; }
            else if (c < 0xfc) literals = ((c & 0x1f) << 2) + 4;
            else literals = c & 3;
            Require(p + literals <= b.Length && at + literals + copies <= length, "Invalid texture data.");
            Buffer.BlockCopy(b, p, result, at, literals); p += literals; at += literals;
            Require(copies == 0 || distance <= at, "Invalid texture reference.");
            for (int i = 0; i < copies; i++) { result[at] = result[at - distance]; at++; }
            if (c >= 0xfc) break;
        }
        Require(at == length, "Incomplete texture data."); return result;
    }

    static byte[] Modify(byte[] compressed, Recipe recipe)
    {
        if (Hash(compressed) == recipe.OutputHash) return compressed;
        Require(Hash(compressed) == recipe.SourceHash, "This FIFA Street ISO version is not supported by the menu credit patch.");
        byte[] original = DecodeChunk(compressed); var entries = Index(original, original.Length);
        var data = entries.ToDictionary(e => e.Name, e => original.AsSpan(e.Offset, e.Length).ToArray());
        foreach (var edit in recipe.Edits)
        {
            byte[] source = data[edit.Name]; Require(Hash(source) == edit.SourceHash, "Unexpected menu asset.");
            source = DecodeRefPack(source); Require(Hash(source) == edit.DecodedHash, "Unexpected decoded menu asset.");
            Require(edit.Length >= source.Length && edit.Xor.Length == edit.Length, "Invalid credit recipe.");
            var changed = new byte[edit.Length]; source.CopyTo(changed, 0);
            for (int i = 0; i < changed.Length; i++) changed[i] ^= edit.Xor[i];
            Require(Hash(changed) == edit.OutputHash, "Credit asset verification failed."); data[edit.Name] = changed;
        }
        int headerLength = 16 + entries.Sum(e => 9 + Encoding.UTF8.GetByteCount(e.Name));
        using var output = new MemoryStream(); output.SetLength((headerLength + 63) & ~63); output.Position = output.Length;
        var header = new byte[headerLength]; Encoding.ASCII.GetBytes("BIGF").CopyTo(header, 0); Put(header, 8, entries.Count); Put(header, 12, headerLength);
        int p = 16;
        foreach (var e in entries)
        {
            byte[] asset = data[e.Name], name = Encoding.UTF8.GetBytes(e.Name);
            Put(header, p, (int)output.Position); Put(header, p + 4, asset.Length); name.CopyTo(header, p + 8); p += 9 + name.Length;
            output.Write(asset); int padding = (int)((-output.Position) & 63); output.Write(new byte[padding]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)output.Length);
        output.Position = 0; output.Write(header); byte[] result = output.ToArray();
        Require(Hash(result) == recipe.OutputHash, "Menu archive verification failed."); return result;
    }

    internal static void Apply(string gameData)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("credit-patch.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        Recipe[] recipes = JsonSerializer.Deserialize<Recipe[]>(gzip)!;
        string big = Path.Combine(gameData, "data1.big"), bh = Path.Combine(gameData, "data1.bh");
        string staged = big + ".credit.tmp", stagedBh = bh + ".credit.tmp", backup = big + ".credit.rollback";
        Require(!File.Exists(staged) && !File.Exists(stagedBh) && !File.Exists(backup), "An interrupted menu patch needs recovery before retrying.");
        byte[] index = File.ReadAllBytes(bh), header;
        var replacements = new List<(Entry Entry, byte[] Data, int BhPosition)>();
        using (var input = new FileStream(big, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            header = new byte[16]; input.ReadExactly(header); int tableEnd = U(header, 12);
            Require(tableEnd <= 8 * 1024 * 1024, "Invalid archive directory size."); Array.Resize(ref header, tableEnd); input.ReadExactly(header.AsSpan(16));
            var entries = Index(header, input.Length);
            foreach (var recipe in recipes)
            {
                Entry e = entries.Single(x => x.Name == recipe.Name); byte[] data = new byte[e.Length]; input.Position = e.Offset; input.ReadExactly(data);
                if (Hash(data) == recipe.OutputHash) continue;
                byte[] modified = Modify(data, recipe); var matches = new List<int>();
                for (int i = 16; i + 8 <= index.Length; i += 20) if (U(index, i) == e.Offset && U(index, i + 4) == e.Length) matches.Add(i);
                Require(matches.Count == 1, "The companion archive index does not match this ISO."); replacements.Add((e, modified, matches[0]));
            }
        }
        if (replacements.Count == 0) { Console.WriteLine("Menu credits already applied."); return; }
        bool committed = false;
        try
        {
            using (var input = File.OpenRead(big))
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                byte[] buffer = new byte[1024 * 1024]; int n;
                while ((n = input.Read(buffer)) > 0) { InstallerEngine.CancellationToken.ThrowIfCancellationRequested(); output.Write(buffer, 0, n); }
                foreach (var item in replacements)
                {
                    output.Write(new byte[(int)((-output.Position) & 63)]); int offset = checked((int)output.Position); output.Write(item.Data);
                    Put(header, item.Entry.TablePosition, offset); Put(header, item.Entry.TablePosition + 4, item.Data.Length);
                    Put(index, item.BhPosition, offset); Put(index, item.BhPosition + 4, item.Data.Length);
                }
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), checked((uint)output.Length)); output.Position = 0; output.Write(header); output.Flush(true);
            }
            File.WriteAllBytes(stagedBh, index); InstallerEngine.CancellationToken.ThrowIfCancellationRequested();
            File.Replace(staged, big, backup); committed = true;
            File.Move(stagedBh, bh, true); File.Delete(backup);
            Console.WriteLine("PORTED BY: SAMUELITODAVILA applied to the start screen and main menu.");
        }
        catch { if (committed && File.Exists(backup)) File.Move(backup, big, true); throw; }
        finally { if (File.Exists(staged)) File.Delete(staged); if (File.Exists(stagedBh)) File.Delete(stagedBh); }
    }
}
