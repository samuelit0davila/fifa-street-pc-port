namespace FifaStreetLauncher;

// The game keeps its saves in Documents\fifastreet (the Windows Documents folder, wherever it points). When OneDrive
// backs up Documents it can roll the save files back or hold them as online-only, and players lost their World Tour
// careers. Warn once, in the player's language, with the fix.
public partial class LauncherForm
{
    private static bool DocumentsInOneDrive()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) return false;
        foreach (string name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? root = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(root) && docs.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return docs.Contains("\\OneDrive", StringComparison.OrdinalIgnoreCase);
    }

    private void WarnOneDriveOnce()
    {
        try
        {
            string marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FifaStreetOnline", "onedrive-warned.txt");
            if (File.Exists(marker) || !DocumentsInOneDrive()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fifastreet");
            string lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            string text = lang switch
            {
                "pt" => "Sua pasta Documentos está no OneDrive. O jogo salva os saves em:\n" + docs +
                        "\n\nO OneDrive pode apagar ou trocar esses arquivos e fazer você perder o World Tour.\n\n" +
                        "Para evitar isso: no OneDrive, Configurações > Sincronizar e fazer backup > Gerenciar backup, " +
                        "desative a pasta Documentos. Antes, copie a pasta fifastreet para outro lugar por segurança.",
                "es" => "Tu carpeta Documentos está en OneDrive. El juego guarda las partidas en:\n" + docs +
                        "\n\nOneDrive puede borrar o cambiar estos archivos y hacerte perder el World Tour.\n\n" +
                        "Para evitarlo: en OneDrive, Configuración > Sincronización y copia de seguridad > Administrar copia de seguridad, " +
                        "desactiva la carpeta Documentos. Antes, copia la carpeta fifastreet a otro sitio por seguridad.",
                _ => "Your Documents folder is in OneDrive. The game keeps its saves in:\n" + docs +
                     "\n\nOneDrive can remove or swap these files and you can lose your World Tour career.\n\n" +
                     "To avoid it: in OneDrive, Settings > Sync and backup > Manage back up, turn off Documents. " +
                     "First copy the fifastreet folder somewhere else to be safe.",
            };
            MessageBox.Show(this, text, "ReStreet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No warning is better than a launcher that fails to open.
        }
    }
}
