using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FifaStreetLauncher.Linux;

// Screen sizes, refresh rates and monitors from xrandr (X11 and XWayland). Without it, the usual 16:9 sizes.
internal static partial class Displays
{
    private static readonly string[] CommonSizes = { "3840x2160", "2560x1440", "1920x1080", "1600x900", "1366x768", "1280x720" };

    [GeneratedRegex(@"^(\S+) connected( primary)?")]
    private static partial Regex OutputLine();

    [GeneratedRegex(@"^\s+(\d+x\d+)\s+(.*)$")]
    private static partial Regex ModeLine();

    public static (string[] Sizes, string[] Rates, string[] Monitors) Detect()
    {
        var sizes = new List<string>();
        var rates = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        var monitors = new List<string>();
        string current = "";
        int currentRate = 0;
        try
        {
            using var xrandr = Process.Start(new ProcessStartInfo("xrandr", "--current") { RedirectStandardOutput = true, UseShellExecute = false });
            string text = xrandr!.StandardOutput.ReadToEnd();
            xrandr.WaitForExit(3000);
            bool first = true;
            foreach (string line in text.Split('\n'))
            {
                var o = OutputLine().Match(line);
                if (o.Success)
                {
                    monitors.Add($"{monitors.Count}: {o.Groups[1].Value}{(o.Groups[2].Success ? " • Primary" : "")}");
                    first = monitors.Count == 1 || o.Groups[2].Success;
                    continue;
                }
                var m = ModeLine().Match(line);
                if (!m.Success || !first) continue;
                if (!sizes.Contains(m.Groups[1].Value)) sizes.Add(m.Groups[1].Value);
                foreach (string r in m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!double.TryParse(r.TrimEnd('*', '+'), NumberStyles.Float, CultureInfo.InvariantCulture, out double hz)) continue;
                    rates.Add((int)Math.Round(hz));
                    if (r.Contains('*')) { current = m.Groups[1].Value; currentRate = (int)Math.Round(hz); }
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex.Message);  // no xrandr
        }
        // The game wants 16:9-ish sizes it can scale; keep the screen's own size first.
        var list = sizes.Where(s => s == current || CommonSizes.Contains(s)).ToList();
        if (list.Count == 0) list.AddRange(CommonSizes);
        if (current.Length > 0) { list.Remove(current); list.Insert(0, current); }
        if (rates.Count == 0) foreach (int r in new[] { 144, 120, 60 }) rates.Add(r);
        var rateList = rates.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToList();
        if (currentRate > 0) { rateList.Remove(currentRate.ToString(CultureInfo.InvariantCulture)); rateList.Insert(0, currentRate.ToString(CultureInfo.InvariantCulture)); }
        if (monitors.Count == 0) monitors.Add("0: Primary");
        return (list.ToArray(), rateList.ToArray(), monitors.ToArray());
    }
}
