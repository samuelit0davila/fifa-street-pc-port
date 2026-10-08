using System;
using System.Windows.Forms;

namespace FifaStreetLauncher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--preview")
        {
            using var form = new LauncherForm();
            form.Show();
            if (args.Length > 2)
            {
                var method = typeof(LauncherForm).GetMethod(args[2] == "advanced" ? "ToggleAdvanced" : "ToggleCompatibility", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method!.Invoke(form, null);
            }
            Application.DoEvents();
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
            bitmap.Save(args[1]);
            return;
        }
        Application.Run(new LauncherForm());
    }
}
