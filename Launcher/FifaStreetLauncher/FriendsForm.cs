using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ReStreet.Ui;

namespace FifaStreetLauncher;

// One row of the people list.
internal enum PersonKind { Header, Request, Friend, Online, Pending }

internal sealed record PersonRow(PersonKind Kind, string Name, bool IsOnline = false);

// The people list is drawn by hand so it matches the rest of the launcher.
internal sealed class PeopleList : Control
{
    const int RowH = 44, HeaderH = 32;
    readonly List<PersonRow> rows = new();
    int scroll, hover = -1;

    public event Action<string, string>? ActionClicked;   // action, name
    public string EmptyText { get; set; } = "";

    public PeopleList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = StreetTheme.Field;
    }

    public void SetRows(IEnumerable<PersonRow> items)
    {
        rows.Clear();
        rows.AddRange(items);
        ClampScroll();
        Invalidate();
    }

    float K => StreetTheme.K(this);
    int RowHeight(PersonRow row) => (int)((row.Kind == PersonKind.Header ? HeaderH : RowH) * K);
    int TotalHeight() { int sum = 0; foreach (var row in rows) sum += RowHeight(row); return sum; }

    void ClampScroll() => scroll = Math.Max(0, Math.Min(scroll, Math.Max(0, TotalHeight() - Height)));

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scroll -= Math.Sign(e.Delta) * (int)(RowH * K);
        ClampScroll();
        Invalidate();
    }

    // Rectangles of the action words on a row, right to left.
    IEnumerable<(string Action, string Text, Rectangle Rect, bool Primary)> Actions(PersonRow row, Rectangle bounds)
    {
        using var font = new Font("Segoe UI", 12.67f * K, GraphicsUnit.Pixel);
        int right = bounds.Right - (int)(14 * K);
        (string, string, bool)[] items = row.Kind switch
        {
            PersonKind.Request => new[] { ("decline", "Decline", false), ("accept", "Accept", true) },
            PersonKind.Friend => new[] { ("remove", "Remove", false) },
            PersonKind.Online => new[] { ("add", "Add friend", true) },
            PersonKind.Pending => new[] { ("remove", "Cancel", false) },
            _ => Array.Empty<(string, string, bool)>()
        };
        foreach (var (action, text, primary) in items)
        {
            int width = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(16 * K);
            var rect = new Rectangle(right - width, bounds.Y + (int)(8 * K), width, bounds.Height - (int)(16 * K));
            yield return (action, text, rect, primary);
            right = rect.X - (int)(6 * K);
        }
    }

    (int Index, Rectangle Bounds) RowAt(Point point)
    {
        int y = -scroll;
        for (int i = 0; i < rows.Count; i++)
        {
            int h = RowHeight(rows[i]);
            if (point.Y >= y && point.Y < y + h) return (i, new Rectangle(0, y, Width, h));
            y += h;
        }
        return (-1, Rectangle.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var (index, bounds) = RowAt(e.Location);
        bool overAction = false;
        if (index >= 0)
            foreach (var action in Actions(rows[index], bounds))
                if (action.Rect.Contains(e.Location)) overAction = true;
        Cursor = overAction ? Cursors.Hand : Cursors.Default;
        if (index != hover) { hover = index; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var (index, bounds) = RowAt(e.Location);
        if (index < 0) return;
        foreach (var action in Actions(rows[index], bounds))
            if (action.Rect.Contains(e.Location)) { ActionClicked?.Invoke(action.Action, rows[index].Name); return; }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        StreetTheme.PaintBehind(this, g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = K;
        var frame = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = StreetTheme.Round(frame, 8 * k);
        using (var fill = new SolidBrush(StreetTheme.Field)) g.FillPath(fill, path);
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);

        using var nameFont = new Font("Segoe UI", 14f * k, GraphicsUnit.Pixel);
        using var headerFont = new Font(StreetTheme.CondensedFamily, 11 * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var actionFont = new Font("Segoe UI", 12.67f * k, GraphicsUnit.Pixel);

        if (rows.Count == 0)
            TextRenderer.DrawText(g, EmptyText, nameFont, new Rectangle((int)(16 * k), (int)(14 * k), Width - (int)(32 * k), Height), StreetTheme.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        int y = -scroll;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int h = RowHeight(row);
            var bounds = new Rectangle(0, y, Width, h);
            y += h;
            if (bounds.Bottom < 0 || bounds.Top > Height) continue;

            if (row.Kind == PersonKind.Header)
            {
                using var yellow = new SolidBrush(StreetTheme.Yellow);
                StreetTheme.DrawSpaced(g, row.Name, headerFont, yellow, new PointF(16 * k, bounds.Y + 12 * k), 2.2f * k);
                continue;
            }

            if (i == hover)
                using (var lit = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillRectangle(lit, bounds);

            float dot = 10 * k;
            var dotRect = new RectangleF(16 * k, bounds.Y + (h - dot) / 2, dot, dot);
            bool lit2 = row.Kind == PersonKind.Online || row.IsOnline;
            using (var dotBrush = new SolidBrush(lit2 ? Color.FromArgb(86, 214, 120) : Color.FromArgb(96, 100, 104)))
                g.FillEllipse(dotBrush, dotRect);

            string suffix = row.Kind == PersonKind.Pending ? "  ·  request sent" : "";
            TextRenderer.DrawText(g, row.Name + suffix, nameFont, new Rectangle((int)(36 * k), bounds.Y, Width - (int)(220 * k), h),
                row.Kind == PersonKind.Pending || (row.Kind == PersonKind.Friend && !row.IsOnline) ? StreetTheme.Muted : StreetTheme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            foreach (var action in Actions(row, bounds))
            {
                var rect = action.Rect;
                using var chip = StreetTheme.Round(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), 6 * k);
                using (var back = new SolidBrush(action.Primary ? StreetTheme.Yellow : Color.FromArgb(34, 255, 255, 255))) g.FillPath(back, chip);
                TextRenderer.DrawText(g, action.Text, actionFont, rect, action.Primary ? StreetTheme.YellowDark : StreetTheme.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        g.Restore(state);
        using var edge = new Pen(Color.FromArgb(60, 255, 255, 255));
        g.DrawPath(edge, path);
    }
}

// Friends, requests and who is online. Same frame, fonts and colours as the setup and the online patch.
internal sealed class FriendsForm : Form, ISceneHost, IUiScaled
{
    readonly UiScaler scaler;
    public float UiScale => scaler.Scale;

    const float DesignHeight = 610f;
    new const int Left = 48;
    const int SheetX = 480, SheetY = 32, SheetW = 422, SheetH = 546, Pad = 24;
    const int ContentX = SheetX + Pad, ContentW = SheetW - 2 * Pad;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5), DefaultRequestHeaders = { { "User-Agent", "ReStreetLauncher/" + LauncherForm.LauncherVersion } } };

    readonly string server, me, key;
    readonly TextBox addBox = new();
    readonly SlantButton addButton = new();
    readonly SlantButton copyButton = new();
    readonly SlantButton closeButton = new();
    readonly PeopleList list = new();
    readonly ThemedLabel meLabel = new();
    readonly ThemedLabel connection = new();
    readonly ThemedLabel message = new();
    readonly System.Windows.Forms.Timer poll = new() { Interval = 4000 };

    Bitmap? scene;
    Size sceneSize;
    bool busy;
    int pingMs = -1;
    string lastError = "";
    JsonElement? lastSnapshot;

    public Bitmap SceneBitmap { get { BuildScene(); return scene!; } }

    internal FriendsForm(string server, string me, string key)
    {
        this.server = server;
        this.me = me;
        this.key = key;
        Text = "ReStreet - Friends";
        using (var icon = typeof(FriendsForm).Assembly.GetManifestResourceStream("fifastreet.ico")!)
            Icon = new Icon(icon);
        ClientSize = new Size(930, 610);
        scaler = new UiScaler(this, 930, 610);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = StreetTheme.Asphalt;
        ForeColor = StreetTheme.Ink;
        Font = StreetTheme.Body(10);
        HandleCreated += (_, _) => StreetTheme.ApplyDarkTitleBar(Handle);

        meLabel.SetBounds(Left, 264, 384, 28);
        meLabel.Font = StreetTheme.Body(13f, FontStyle.Bold);
        meLabel.ForeColor = StreetTheme.Ink;
        meLabel.Text = "Playing as " + me;
        Controls.Add(meLabel);
        connection.SetBounds(Left, 296, 384, 24);
        connection.Font = StreetTheme.Body(10);
        connection.ForeColor = StreetTheme.Muted;
        connection.Text = "Connecting…";
        Controls.Add(connection);
        Controls.Add(new ThemedLabel
        {
            Text = "Add a friend by name, or pick someone who is online. To invite a friend to a match, open the friends menu inside the game.",
            Bounds = new Rectangle(Left, 336, 384, 80),
            Font = StreetTheme.Body(10),
            ForeColor = StreetTheme.Muted
        });

        Controls.Add(new ThemedLabel
        {
            Text = "Friends", Bounds = new Rectangle(ContentX, SheetY + Pad, ContentW, 32),
            Font = StreetTheme.Condensed(20f, FontStyle.Bold), ForeColor = StreetTheme.Ink
        });

        const int addW = 96, gap = 8;
        addBox.Font = StreetTheme.Body(10);
        addBox.MaxLength = 16;
        addBox.PlaceholderText = "Friend's online name";
        addBox.AccessibleName = "Friend's online name";
        Controls.Add(new FieldFrame(addBox) { Bounds = new Rectangle(ContentX, 96, ContentW - addW - gap, 40) });
        addButton.Kind = SlantKind.Glass;
        addButton.Text = "Add";
        addButton.Font = StreetTheme.Body(10);
        addButton.Bounds = new Rectangle(ContentX + ContentW - addW, 96, addW, 40);
        addButton.Click += async (_, _) => await AddTyped();
        AcceptButton = addButton;
        Controls.Add(addButton);

        message.SetBounds(ContentX, 144, ContentW, 20);
        message.Font = StreetTheme.Body(9f);
        message.ForeColor = StreetTheme.Muted;
        Controls.Add(message);

        list.Bounds = new Rectangle(ContentX, 172, ContentW, 304);
        list.EmptyText = "Nobody here yet. Add a friend by name, or wait for other players to come online.";
        list.ActionClicked += async (action, name) => await Act(action, name);
        Controls.Add(list);

        copyButton.Kind = SlantKind.Text;
        copyButton.Align = StringAlignment.Near;
        copyButton.Text = "Copy connection report";
        copyButton.Font = StreetTheme.Body(10);
        copyButton.Bounds = new Rectangle(ContentX, 522, 210, 32);
        copyButton.Click += async (_, _) => await CopyReport();
        Controls.Add(copyButton);
        closeButton.Kind = SlantKind.Text;
        closeButton.Align = StringAlignment.Far;
        closeButton.Text = "Close";
        closeButton.Font = StreetTheme.Body(10);
        closeButton.Bounds = new Rectangle(ContentX + ContentW - 150, 522, 150, 32);
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);
        CancelButton = closeButton;

        poll.Tick += async (_, _) => await Refresh();
        Shown += async (_, _) => { poll.Start(); await Refresh(); };
        FormClosed += (_, _) => poll.Dispose();
        scaler.Capture();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        BuildScene();
        using (var floor = new SolidBrush(StreetTheme.Asphalt)) e.Graphics.FillRectangle(floor, ClientRectangle);
        e.Graphics.DrawImageUnscaled(scene!, 0, 0);
    }

    void BuildScene()
    {
        float s = ClientSize.Width / (float)StreetTheme.DesignWidth;
        var size = new Size(ClientSize.Width, (int)Math.Ceiling(DesignHeight * s));
        if (scene != null && sceneSize == size) return;
        scene?.Dispose();
        sceneSize = size;
        using var backdrop = StreetTheme.RenderBackdrop(size, StreetTheme.DesignWidth, DesignHeight);
        using var blurred = StreetTheme.Blur(backdrop, 10);
        scene = new Bitmap(backdrop);
        using var g = Graphics.FromImage(scene);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(s, s);

        StreetTheme.DrawGlass(g, blurred, s, new RectangleF(SheetX, SheetY, SheetW, SheetH), 16);

        using (var small = new Font(StreetTheme.CondensedFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var yellow = new SolidBrush(StreetTheme.Yellow))
            StreetTheme.DrawSpaced(g, "FIFA STREET 2012 RECOMPILED", small, yellow, new PointF(Left, 56), 3.2f);
        StreetTheme.DrawWordmark(g, "ReStreet", new PointF(Left, 88), 118, StreetTheme.Ink);
        var tag = new RectangleF(Left, 192, 236, 32);
        using (var bar = new SolidBrush(StreetTheme.Yellow)) g.FillPolygon(bar, StreetTheme.Slant(tag, 10));
        using (var tagFont = new Font(StreetTheme.CondensedFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var dark = new SolidBrush(StreetTheme.YellowDark))
            StreetTheme.DrawSpaced(g, "FRIENDS · ONLINE BETA", tagFont, dark, new PointF(Left + 12, 198), 2.6f);

        using var name = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        StreetTheme.DrawLabelValue(g, "Ported by:", "SamuelitoDaVila", name, StreetTheme.Ink, StreetTheme.Ink, Left, 534);
        StreetTheme.DrawLabelValue(g, "Contributions:", "Emran_Ahm3d", name, StreetTheme.Muted, StreetTheme.Muted, Left, 557);
    }

    // Sample rows for the --preview-friends screenshot.
    internal void PreviewRows()
    {
        poll.Stop();
        connection.ForeColor = StreetTheme.Muted;
        connection.Text = "Connected · 42 ms to the server";
        list.SetRows(new[]
        {
            new PersonRow(PersonKind.Header, "FRIEND REQUESTS"),
            new PersonRow(PersonKind.Request, "vicente"),
            new PersonRow(PersonKind.Header, "FRIENDS · 1 ONLINE"),
            new PersonRow(PersonKind.Friend, "Nelly", true),
            new PersonRow(PersonKind.Friend, "Baco", false),
            new PersonRow(PersonKind.Pending, "Darix"),
            new PersonRow(PersonKind.Header, "ONLINE NOW"),
            new PersonRow(PersonKind.Online, "Cabura"),
        });
    }

    string Url(string path) => "http://" + server + ":29812" + path;

    async Task<JsonElement?> Post(string action, string other)
    {
        var body = JsonSerializer.Serialize(new { me, key, action, other, xuid = "0", xsinfo = "" });
        try
        {
            var watch = Stopwatch.StartNew();
            using var response = await Http.PostAsync(Url("/v1/friends"), new StringContent(body, Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();
            pingMs = (int)watch.ElapsedMilliseconds;
            using var doc = JsonDocument.Parse(text);
            if (!response.IsSuccessStatusCode)
            {
                string error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                lastError = error is "bad_key" or "missing_key"
                    ? "This name belongs to another player. Run the online patch and choose a different name."
                    : "The server refused the request (" + (int)response.StatusCode + ").";
                return null;
            }
            lastError = "";
            return doc.RootElement.Clone();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            lastError = "Cannot reach the server. Check your internet connection.";
            return null;
        }
    }

    static IEnumerable<string> Names(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var item in array.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) yield return item.GetString() ?? "";
    }

    async Task Refresh(string action = "list", string other = "")
    {
        if (busy) return;
        busy = true;
        try
        {
            var snapshot = await Post(action, other);
            if (snapshot == null)
            {
                connection.ForeColor = Color.FromArgb(255, 120, 110);
                connection.Text = lastError;
                return;
            }
            lastSnapshot = snapshot;
            var root = snapshot.Value;
            connection.ForeColor = StreetTheme.Muted;
            connection.Text = "Connected · " + pingMs + " ms to the server";

            var rows = new List<PersonRow>();
            var requests = Names(root, "requests").ToList();
            if (requests.Count > 0)
            {
                rows.Add(new PersonRow(PersonKind.Header, "FRIEND REQUESTS"));
                rows.AddRange(requests.Select(n => new PersonRow(PersonKind.Request, n)));
            }
            var friends = new List<(string Name, bool Online)>();
            if (root.TryGetProperty("friends", out var friendArray) && friendArray.ValueKind == JsonValueKind.Array)
                foreach (var f in friendArray.EnumerateArray())
                    friends.Add((f.GetProperty("name").GetString() ?? "", f.TryGetProperty("online", out var o) && o.ValueKind == JsonValueKind.True));
            var pending = Names(root, "pending").ToList();
            if (friends.Count > 0 || pending.Count > 0)
            {
                int online = friends.Count(f => f.Online);
                rows.Add(new PersonRow(PersonKind.Header, "FRIENDS · " + online + " ONLINE"));
                rows.AddRange(friends.OrderByDescending(f => f.Online).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new PersonRow(PersonKind.Friend, f.Name, f.Online)));
                rows.AddRange(pending.Select(n => new PersonRow(PersonKind.Pending, n)));
            }
            var players = Names(root, "players").ToList();
            if (players.Count > 0)
            {
                rows.Add(new PersonRow(PersonKind.Header, "ONLINE NOW"));
                rows.AddRange(players.Select(n => new PersonRow(PersonKind.Online, n)));
            }
            list.SetRows(rows);
        }
        finally { busy = false; }
    }

    async Task Act(string action, string name)
    {
        while (busy) await Task.Delay(50);
        message.ForeColor = StreetTheme.Muted;
        message.Text = "";
        await Refresh(action, name);
        if (lastSnapshot?.TryGetProperty("result", out var result) == true && result.ValueKind == JsonValueKind.String)
            message.Text = result.GetString() switch
            {
                "requested" => "Request sent to " + name + ".",
                "friends" => "You and " + name + " are now friends.",
                "already" => name + " is already your friend.",
                "self" => "That is your own name.",
                "expired" => "That invite is no longer valid.",
                "unknown" => "No player called " + name + " has the online patch yet.",
                _ => ""
            };
    }

    async Task AddTyped()
    {
        string name = addBox.Text.Trim();
        if (name.Length < 3) { message.ForeColor = Color.FromArgb(255, 120, 110); message.Text = "Type the friend's online name first."; return; }
        await Act("add", name);
        addBox.Clear();
    }

    async Task CopyReport()
    {
        copyButton.Enabled = false;
        var report = new StringBuilder();
        report.AppendLine("ReStreet online report");
        report.AppendLine("Name: " + me);
        report.AppendLine("Server: " + server);
        report.AppendLine("Time (UTC): " + DateTime.UtcNow.ToString("u"));
        report.AppendLine("Windows: " + Environment.OSVersion.VersionString);
        try
        {
            var watch = Stopwatch.StartNew();
            using var health = await Http.GetAsync(Url("/v1/health"));
            report.AppendLine("HTTP /v1/health: " + (int)health.StatusCode + " in " + watch.ElapsedMilliseconds + " ms");
        }
        catch (Exception error) { report.AppendLine("HTTP /v1/health: failed (" + error.GetType().Name + ")"); }
        foreach (int port in new[] { 29812, 29813 })
        {
            try
            {
                var watch = Stopwatch.StartNew();
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(3000);
                await client.ConnectAsync(server, port, timeout.Token);
                report.AppendLine("TCP " + port + ": open in " + watch.ElapsedMilliseconds + " ms");
            }
            catch (Exception) { report.AppendLine("TCP " + port + ": blocked or unreachable"); }
        }
        report.AppendLine("Friends request: " + (lastError.Length == 0 ? "ok" : lastError));
        try { Clipboard.SetText(report.ToString()); message.ForeColor = StreetTheme.Muted; message.Text = "Report copied. Paste it in the Discord beta-feedback channel."; }
        catch (System.Runtime.InteropServices.ExternalException) { message.Text = "Could not copy to the clipboard."; }
        copyButton.Enabled = true;
    }
}
