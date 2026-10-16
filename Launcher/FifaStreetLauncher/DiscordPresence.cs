using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FifaStreetLauncher;

// Discord Rich Presence over the local Discord IPC pipe, without the Discord SDK.
// Shows "Playing ReStreet" while the game runs and clears it when disposed. Everything runs on a
// background task and failures are silent: no Discord, no presence, the game is never affected.
internal sealed class DiscordPresence : IDisposable
{
    // Application ID from https://discord.com/developers/applications. The application name is shown
    // as "Playing <name>"; its Rich Presence art asset named "restreet" is the large image.
    // Empty disables presence. RESTREET_DISCORD_APP_ID overrides it (useful for testing).
    private const string DefaultApplicationId = "";
    private const string LargeImageKey = "restreet";
    private const string ProjectUrl = "https://github.com/samuelit0davila/fifa-street-pc-port";

    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    private const int MaxFrameLength = 64 * 1024;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly string applicationId;
    private readonly string state;
    private readonly long startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly CancellationTokenSource cancel = new();
    private readonly Task worker;

    private DiscordPresence(string applicationId, string state)
    {
        this.applicationId = applicationId;
        this.state = state;
        worker = Task.Run(() => RunAsync(cancel.Token));
    }

    public static DiscordPresence? Start(string state)
    {
        string? id = Environment.GetEnvironmentVariable("RESTREET_DISCORD_APP_ID");
        if (string.IsNullOrWhiteSpace(id)) id = DefaultApplicationId;
        return string.IsNullOrWhiteSpace(id) ? null : new DiscordPresence(id.Trim(), state);
    }

    // Keeps one connection open while the game runs (Discord clears the activity when it closes) and
    // reconnects if Discord is started or restarted later.
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Discord presence: " + ex.Message);
            }
            try
            {
                await Task.Delay(RetryDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken token)
    {
        using var pipe = await ConnectAsync(token);
        if (pipe == null) return;

        await WriteFrameAsync(pipe, OpHandshake, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = applicationId }), token);
        var (op, _) = await ReadFrameAsync(pipe, token);
        if (op != OpFrame) return;  // OpClose: Discord rejected the application ID.

        await WriteFrameAsync(pipe, OpFrame, BuildActivity(), token);

        while (true)
        {
            var (next, body) = await ReadFrameAsync(pipe, token);
            if (next == OpPing) await WriteFrameAsync(pipe, OpPong, body, token);
            else if (next == OpClose || next < 0) return;
        }
    }

    private byte[] BuildActivity()
    {
        var version = typeof(DiscordPresence).Assembly.GetName().Version;
        string versionText = version != null ? $"ReStreet v{version.Major}.{version.Minor}" : "ReStreet";
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            cmd = "SET_ACTIVITY",
            nonce = Guid.NewGuid().ToString(),
            args = new
            {
                pid = Environment.ProcessId,
                activity = new
                {
                    details = "FIFA Street (2012)",
                    state,
                    timestamps = new { start = startTime },
                    assets = new
                    {
                        large_image = LargeImageKey,
                        large_text = versionText + " - FIFA Street 2012 Recompiled"
                    },
                    buttons = new[] { new { label = "Get ReStreet", url = ProjectUrl } }
                }
            }
        });
    }

    // Discord listens on the first free pipe of discord-ipc-0 .. discord-ipc-9.
    private static async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken token)
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(100, token);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }
        return null;
    }

    // Frame: little-endian int32 opcode, little-endian int32 length, UTF-8 JSON body.
    private static async Task WriteFrameAsync(Stream pipe, int op, byte[] body, CancellationToken token)
    {
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame, 8);
        await pipe.WriteAsync(frame, token);
        await pipe.FlushAsync(token);
    }

    // Returns op -1 when Discord closed the pipe.
    private static async Task<(int op, byte[] body)> ReadFrameAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[8];
        try
        {
            await pipe.ReadExactlyAsync(header, token);
        }
        catch (EndOfStreamException)
        {
            return (-1, Array.Empty<byte>());
        }
        int op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length < 0 || length > MaxFrameLength)
            throw new InvalidDataException($"Unexpected Discord IPC frame length {length}.");
        var body = new byte[length];
        await pipe.ReadExactlyAsync(body, token);
        if (op == OpClose)
            Debug.WriteLine("Discord presence closed: " + Encoding.UTF8.GetString(body));
        return (op, body);
    }

    public void Dispose()
    {
        cancel.Cancel();
        try
        {
            worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }
}
