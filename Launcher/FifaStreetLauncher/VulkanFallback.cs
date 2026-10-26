namespace FifaStreetLauncher;

// Vulkan on some AMD drivers (RX 580, Ryzen integrated graphics) kills the game while the first match or menu loads,
// and Direct3D 12 works on the same PCs. Players did not know: when the game crashes in its first minutes with
// Vulkan, offer to switch. Only asks; nothing changes without a yes.
public partial class LauncherForm
{
    private bool vulkanFallbackAsked;  // once per launcher run

    private static bool IsCrashExitCode(int code)
    {
        uint c = unchecked((uint)code);
        return c >= 0xC0000000 || c == 0xE06D7363;  // NTSTATUS errors (access violation, ...) and C++ exceptions
    }

    private void SuggestD3D12AfterVulkanCrash(string graphicsApi, int exitCode, TimeSpan ran)
    {
        if (graphicsApi != "Vulkan" || !IsCrashExitCode(exitCode) || ran > TimeSpan.FromMinutes(3) ||
            vulkanFallbackAsked || closeAfterGame || graphicsApiBox.SelectedIndex != 1)
            return;
        vulkanFallbackAsked = true;
        string lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        string text = lang switch
        {
            "pt" => "O ReStreet fechou logo no início usando Vulkan. Em algumas placas AMD o Vulkan fecha o jogo e o " +
                    "Direct3D 12 funciona.\n\nMudar para Direct3D 12?",
            "es" => "ReStreet se cerró al poco de empezar con Vulkan. En algunas gráficas AMD Vulkan cierra el juego y " +
                    "Direct3D 12 funciona.\n\n¿Cambiar a Direct3D 12?",
            _ => "ReStreet closed soon after starting with Vulkan. On some AMD graphics Vulkan closes the game and " +
                 "Direct3D 12 works.\n\nSwitch to Direct3D 12?",
        };
        if (MessageBox.Show(this, text, "ReStreet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        graphicsApiBox.SelectedIndex = 0;
        UpdateGraphicsApiState();
        SaveSettings();
    }
}
