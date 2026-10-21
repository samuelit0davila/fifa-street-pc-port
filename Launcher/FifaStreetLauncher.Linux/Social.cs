using System.Text.Json;

namespace FifaStreetLauncher.Linux;

// Social page (players, friends, chat, rooms): the same calls as GlassUi.cs, HTTP to the player's own server.
internal sealed partial class Launcher
{
    // ---------- Social (players, friends, chat): HTTP to the player's own server with their name and key ----------
    private static readonly HttpClient SocialHttp = new() { Timeout = TimeSpan.FromSeconds(6) };
    private DateTime lastBackgroundSocial = DateTime.MinValue;

    // One retry: a single dropped connection (a busy moment on the server, Wi-Fi) must not look like an outage.
    private static async Task<string> SendWithRetry(Func<Task<string>> send)
    {
        try { return await send(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Task.Delay(400);
            return await send();
        }
    }

    private static async Task<JsonDocument?> PostJson(string server, string path, object body)
    {
        string json = JsonSerializer.Serialize(body);
        string text = await SendWithRetry(async () =>
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await SocialHttp.PostAsync($"http://{server}:29812{path}", content);
            return await response.Content.ReadAsStringAsync();
        });
        return text.StartsWith("{") ? JsonDocument.Parse(text) : null;
    }

    private static async Task<JsonDocument> GetJson(string server, string path) =>
        JsonDocument.Parse(await SendWithRetry(() => SocialHttp.GetStringAsync($"http://{server}:29812{path}")));

    private int socialFailures;

    private int socialPollBusy;
    private volatile bool inRoom;

    // A background poll is skipped while the previous one still runs: on a slow link they piled up every 3 s.
    private async Task SocialAsync(string cmd, JsonElement m)
    {
        if (cmd != "social") { await SocialCoreAsync(cmd, m); return; }
        if (Interlocked.Exchange(ref socialPollBusy, 1) == 1) return;
        try { await SocialCoreAsync(cmd, m); }
        finally { Volatile.Write(ref socialPollBusy, 0); }
    }

    private async Task SocialCoreAsync(string cmd, JsonElement m)
    {
        var online = LoadOnlineConfig();
        if (online == null || !toggles["onlineMode"].On || Page == null) return;
        var (server, name, key) = online.Value;
        string Str(string p) => m.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string channel = Str("channel");
        string? error = null;
        try
        {
            if (cmd == "social" && channel == "")
            {
                // Social page closed: refresh the badge only every 15 s (every poll while in a room, to start the game on time).
                if (!inRoom && DateTime.UtcNow - lastBackgroundSocial < TimeSpan.FromSeconds(15)) return;
                lastBackgroundSocial = DateTime.UtcNow;
            }
            if (cmd == "chatSend")
            {
                using var r = await PostJson(server, "/v1/textchat", new { me = name, key, action = "send", channel, text = Str("text") });
                if (r != null && r.RootElement.TryGetProperty("error", out var e)) error = ChatError(e.GetString());
            }
            else if (cmd == "chatAction")
            {
                long id = m.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.Number ? idv.GetInt64() : 0;
                using var r = await PostJson(server, "/v1/textchat", new { me = name, key, action = Str("action"), other = Str("other"), id });
                if (Str("action") == "report") error = "Thanks, the message was reported";
            }
            else if (cmd == "friend")
            {
                using var r = await PostJson(server, "/v1/friends", new { me = name, key, action = Str("action"), other = Str("other") });
                if (r != null && r.RootElement.TryGetProperty("result", out var fr) && fr.ValueKind == JsonValueKind.String)
                    error = fr.GetString() switch { "unknown" => $"No player called {Str("other")}", "self" => "You can't add yourself", _ => null };
            }
            else if (cmd == "room")
            {
                // Game rooms (rooms.py): when a room is full the game takes both players to the match set-up by itself.
                string action = Str("action");
                long roomId = m.TryGetProperty("room", out var rv) && rv.ValueKind == JsonValueKind.Number ? rv.GetInt64() : 0;
                int stadium = m.TryGetProperty("stadium", out var sv2) && sv2.ValueKind == JsonValueKind.Number ? sv2.GetInt32() : 1047;
                using var r = action switch
                {
                    "create" => await PostJson(server, "/v1/rooms", new { me = name, key, action, name = Str("title").Trim().Length > 0 ? Str("title").Trim() : name + "'s game", mode = Str("mode"), stadium }),
                    "join" => await PostJson(server, "/v1/rooms", new { me = name, key, action, room = roomId }),
                    _ => await PostJson(server, "/v1/rooms", new { me = name, key, action = "leave" }),
                };
                if (r != null && r.RootElement.TryGetProperty("error", out var re)) error = RoomError(re.GetString());
                else if (action == "create" && r != null && r.RootElement.TryGetProperty("room", out var made))
                {
                    // Tell the server channel, so anyone can join from the Open games list.
                    string text = $"🎮 I opened a {RoomModeName(Str("mode"))} game at {made.GetProperty("stadium_name").GetString()}. Join it from the Matches tab!";
                    using var _ = await PostJson(server, "/v1/textchat", new { me = name, key, action = "send", channel = "general", text });
                }
            }

            // Fresh snapshot for the page. All requests at once: one after the other they took seconds on a far server.
            var friendsTask = PostJson(server, "/v1/friends", new { me = name, key, action = "list" });
            var playersTask = GetJson(server, "/v1/online");
            var roomsTask = PostJson(server, "/v1/rooms", new { me = name, key, action = "list" });
            var myRoomTask = PostJson(server, "/v1/rooms", new { me = name, key, action = "current" });
            var convosTask = PostJson(server, "/v1/textchat", new { me = name, key, action = "conversations" });
            var msgsTask = channel == "" ? Task.FromResult<JsonDocument?>(null)
                : PostJson(server, "/v1/textchat", new { me = name, key, action = "read", channel, since = m.TryGetProperty("since", out var s2) && s2.ValueKind == JsonValueKind.Number ? s2.GetInt64() : 0 });
            JsonDocument? convos = null, msgs = null;
            bool chatOff = false;
            try
            {
                convos = await convosTask;
                msgs = await msgsTask;
                chatOff = convos == null && msgs == null;  // either answer means the chat is there
            }
            catch (HttpRequestException) { chatOff = true; }
            using var friends = await friendsTask;
            using var players = await playersTask;
            using var rooms = await roomsTask;
            using var myRoom = await myRoomTask;
            var waited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (players.RootElement.TryGetProperty("searching_list", out var sl) && sl.ValueKind == JsonValueKind.Array)
                foreach (var it in sl.EnumerateArray())
                    if (it.TryGetProperty("name", out var wn) && it.TryGetProperty("waited_s", out var ws) && ws.ValueKind == JsonValueKind.Number)
                        waited[wn.GetString() ?? ""] = ws.GetInt32();
            string regionName = Regions.FirstOrDefault(r => r.Address == server).Name ?? server;
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "social",
                ["me"] = name,
                ["server"] = regionName,
                ["channel"] = channel,
                ["error"] = error,
                ["chatOff"] = chatOff,
                ["players"] = players.RootElement.TryGetProperty("players", out var pl)
                    ? pl.EnumerateArray().Select(p => new
                    {
                        name = p.GetProperty("name").GetString(),
                        state = p.GetProperty("state").GetString(),
                        note = p.GetProperty("state").GetString() switch
                        {
                            "in_game" => "Playing " + (p.TryGetProperty("mode", out var md) ? md.GetString() : ""),
                            "searching" => "Searching · " + (p.TryGetProperty("mode", out var md2) ? md2.GetString() : "") +
                                (waited.TryGetValue(p.GetProperty("name").GetString() ?? "", out int w) ? $" · {w / 60}:{w % 60:00}" : ""),
                            _ => "In the menus",
                        },
                    }).ToArray()
                    : Array.Empty<object>(),
            };
            if (players.RootElement.TryGetProperty("searching_modes", out var smodes))
                payload["searchingModes"] = JsonSerializer.Deserialize<object>(smodes.GetRawText());
            if (rooms != null && rooms.RootElement.TryGetProperty("rooms", out var rl))
                payload["rooms"] = JsonSerializer.Deserialize<object>(rl.GetRawText());
            if (myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr))
                payload["myRoom"] = JsonSerializer.Deserialize<object>(mr.GetRawText());
            inRoom = myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr3) && mr3.ValueKind == JsonValueKind.Object;
            OnRoomState(myRoom != null && myRoom.RootElement.TryGetProperty("room", out var mr2) ? mr2.Clone() : null);
            if (friends != null)
            {
                var f = friends.RootElement;
                payload["friends"] = f.TryGetProperty("friends", out var fl) ? JsonSerializer.Deserialize<object>(fl.GetRawText()) : null;
                payload["requests"] = f.TryGetProperty("requests", out var rq) ? JsonSerializer.Deserialize<object>(rq.GetRawText()) : null;
                payload["pending"] = f.TryGetProperty("pending", out var pd) ? JsonSerializer.Deserialize<object>(pd.GetRawText()) : null;
            }
            if (convos != null && convos.RootElement.TryGetProperty("conversations", out var cv))
                payload["convos"] = JsonSerializer.Deserialize<object>(cv.GetRawText());
            if (msgs != null && msgs.RootElement.TryGetProperty("messages", out var ms))
                payload["messages"] = JsonSerializer.Deserialize<object>(ms.GetRawText());
            convos?.Dispose(); msgs?.Dispose();
            socialFailures = 0;
            string json = JsonSerializer.Serialize(payload);
            Page?.Send(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Background refreshes keep the last data; only say so after three failures in a row (or when the
            // player's own action failed).
            if (cmd == "social" && ++socialFailures < 3) return;
            string json = JsonSerializer.Serialize(new { type = "social", error = "Server not reachable" });
            Page?.Send(json);
        }
    }

    private static string RoomModeName(string mode) => mode switch
    {
        "fives" => "5-a-side", "sixes" => "6-a-side", "futsal" => "Futsal", _ => mode,
    };

    private static string RoomError(string? code) => code switch
    {
        "full" => "That game is already full",
        "already_in_room" => "Leave your current game first",
        "too_fast" => "Wait a few seconds before opening another game",
        "no_such_room" or "unknown_room" => "That game is closed",
        _ => "Could not do that: " + code,
    };

    private static string ChatError(string? code) => code switch
    {
        "too_fast" => "Slow down a little",
        "no_links" => "Links are not allowed in chat",
        "too_long" => "Message too long (300 characters max)",
        "muted" => "You are muted for a while",
        "banned" => "You can't use the chat",
        "not_friends" => "You can only message friends",
        _ => "Message not sent",
    };
}
