using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace FifaStreetLauncher.Linux;

// The page's link to the launcher (same protocol as BrowserUi.cs in the Windows launcher): files of ui/, POST msg and
// a long-polled GET events, served on 127.0.0.1 only, under a random secret path.
internal sealed class PageServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly string ui;
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private readonly Launcher launcher;
    private readonly ConcurrentQueue<string> outbox = new();
    private readonly SemaphoreSlim signal = new(0);
    private volatile bool running = true;

    public DateTime LastSeen { get; private set; } = DateTime.UtcNow;
    public string Url { get; }

    public PageServer(string ui, Launcher launcher)
    {
        this.ui = ui;
        this.launcher = launcher;
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{secret}/launcher.html";
        _ = Task.Run(AcceptClients);
    }

    public void Send(string json)
    {
        outbox.Enqueue(json);
        signal.Release();
    }

    private async Task AcceptClients()
    {
        while (running)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var (method, path, body) = await ReadRequest(stream);
            string prefix = "/" + secret + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) { await Respond(stream, 404, "text/plain", Array.Empty<byte>()); return; }
            string rest = path[prefix.Length..].Split('?')[0];
            LastSeen = DateTime.UtcNow;
            if (method == "POST" && rest == "msg")
            {
                launcher.OnMessage(Encoding.UTF8.GetString(body));
                await Respond(stream, 204, "text/plain", Array.Empty<byte>());
            }
            else if (method == "GET" && rest == "events")
            {
                // Long poll: answer at once when something is queued, otherwise after 20 s with an empty list.
                await signal.WaitAsync(TimeSpan.FromSeconds(20));
                var items = new List<string>();
                while (outbox.TryDequeue(out var item)) items.Add(item);
                await Respond(stream, 200, "application/json", Encoding.UTF8.GetBytes("[" + string.Join(",", items) + "]"));
            }
            else if (method == "GET" && rest.Length > 0 && rest.IndexOfAny(new[] { '/', '\\' }) < 0 && !rest.Contains(".."))
            {
                string file = Path.Combine(ui, rest);
                string type = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8", ".jpg" => "image/jpeg", ".png" => "image/png",
                    ".css" => "text/css", ".js" => "text/javascript", _ => "application/octet-stream",
                };
                if (File.Exists(file)) await Respond(stream, 200, type, await File.ReadAllBytesAsync(file));
                else await Respond(stream, 404, "text/plain", Array.Empty<byte>());
            }
            else await Respond(stream, 404, "text/plain", Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static async Task<(string Method, string Path, byte[] Body)> ReadRequest(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 16384 && await stream.ReadAsync(one) == 1)
        {
            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
        }
        string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
        string[] first = lines[0].Split(' ');
        if (first.Length < 2) throw new IOException("bad request");
        int length = 0;
        foreach (string line in lines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line[15..].Trim(), out length);
        length = Math.Clamp(length, 0, 65536);
        var body = new byte[length];
        for (int read = 0; read < length;)
        {
            int got = await stream.ReadAsync(body.AsMemory(read));
            if (got == 0) break;
            read += got;
        }
        return (first[0], first[1], body);
    }

    private static async Task Respond(NetworkStream stream, int status, string type, byte[] body)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", _ => "Not Found" };
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                        "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        if (body.Length > 0) await stream.WriteAsync(body);
    }

    public void Dispose()
    {
        running = false;
        listener.Stop();
    }
}
