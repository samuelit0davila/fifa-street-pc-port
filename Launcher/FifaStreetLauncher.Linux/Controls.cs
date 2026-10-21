using System.Diagnostics;
using System.Text.Json;

namespace FifaStreetLauncher.Linux;

// Controls page: play with the keyboard (and mouse) instead of a controller. The runtime's keyboard mode presents the
// keys as an Xbox controller (cvars mnk_mode, mnk_mouse, mnk_sensitivity, keybind_*), so the game needs no change.
// Saved in controls.json (shared with the Windows launcher); passed to the game as REX_* variables on PLAY.
internal sealed partial class Launcher
{
    public sealed class ControlsSettings
    {
        public bool Keyboard { get; set; }
        public bool MouseTricks { get; set; } = true;  // the default layout is keyboard + mouse
        public double MouseSensitivity { get; set; } = 1.0;
        // Controller button (runtime cvar suffix, e.g. "a", "lstick_up") -> keys, "Space,Semicolon" style.
        public Dictionary<string, string> Keys { get; set; } = new(DefaultKeys);
    }

    // Layouts made for FIFA Street (the game's own tips): LT held = Street Ball Control (and jockey), RT = sprint /
    // panna, LB held = flair pass or shot, RB = juggle, R3 = stand on the ball, right stick = skill moves, Y = manual
    // pass. The left hand always has WASD, Shift sprint, Space ball control, Q flair, E juggle; Enter / Backspace / Esc
    // work in the menus.
    // Keyboard + mouse: the mouse moves the right stick (skills) and its buttons pass / shoot / lob.
    public static readonly IReadOnlyDictionary<string, string> MouseKeys = new Dictionary<string, string>
    {
        ["lstick_up"] = "W", ["lstick_down"] = "S", ["lstick_left"] = "A", ["lstick_right"] = "D", ["lstick_press"] = "C",
        ["rstick_up"] = "Up", ["rstick_down"] = "Down", ["rstick_left"] = "Left", ["rstick_right"] = "Right", ["rstick_press"] = "F",
        ["a"] = "LMB,Return", ["b"] = "RMB,Backspace", ["x"] = "MMB", ["y"] = "R",
        ["left_trigger"] = "Space", ["right_trigger"] = "Shift", ["left_shoulder"] = "Q", ["right_shoulder"] = "E",
        ["dpad_up"] = "1", ["dpad_down"] = "2", ["dpad_left"] = "3", ["dpad_right"] = "4",
        ["start"] = "Escape", ["back"] = "Tab",
    };
    // Keyboard only: the right hand passes / shoots on J K L I and does skills on the arrows.
    public static readonly IReadOnlyDictionary<string, string> KeyboardKeys = new Dictionary<string, string>(MouseKeys)
    {
        ["rstick_press"] = "O", ["a"] = "J,Return", ["b"] = "K,Backspace", ["x"] = "L", ["y"] = "I",
    };
    public static readonly IReadOnlyDictionary<string, string> DefaultKeys = MouseKeys;

    private ControlsSettings? controls;
    private string ControlsPath => Path.Combine(root, "controls.json");

    private ControlsSettings InputControls()
    {
        if (controls != null) return controls;
        try
        {
            if (File.Exists(ControlsPath)) controls = JsonSerializer.Deserialize<ControlsSettings>(File.ReadAllText(ControlsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine(ex.Message);
        }
        controls ??= new ControlsSettings();
        // Buttons added in a later launcher keep their default key.
        foreach (var (button, keys) in DefaultKeys) controls.Keys.TryAdd(button, keys);
        return controls;
    }

    // From the page: the whole controls object. Only known buttons are kept; the page already refuses the runtime's
    // own overlay keys and repeated keys.
    private void SetControls(JsonElement value)
    {
        var current = InputControls();
        if (value.TryGetProperty("keyboard", out var k) && k.ValueKind is JsonValueKind.True or JsonValueKind.False) current.Keyboard = k.GetBoolean();
        if (value.TryGetProperty("mouseTricks", out var mt) && mt.ValueKind is JsonValueKind.True or JsonValueKind.False) current.MouseTricks = mt.GetBoolean();
        if (value.TryGetProperty("sensitivity", out var s) && s.ValueKind == JsonValueKind.Number) current.MouseSensitivity = Math.Clamp(s.GetDouble(), 0.1, 5.0);
        if (value.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
            foreach (var p in keys.EnumerateObject())
                if (DefaultKeys.ContainsKey(p.Name) && p.Value.ValueKind == JsonValueKind.String)
                    current.Keys[p.Name] = p.Value.GetString()!.Length <= 64 ? p.Value.GetString()! : DefaultKeys[p.Name];
        if (value.TryGetProperty("preset", out var pr) && pr.ValueKind == JsonValueKind.String)
        {
            bool mouse = pr.GetString() == "mouse";
            current.Keys = new(mouse ? MouseKeys : KeyboardKeys);
            current.MouseTricks = mouse;
        }
        try
        {
            File.WriteAllText(ControlsPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save the controls: " + ex.Message, false);
        }
    }

    private object ControlsState()
    {
        var c = InputControls();
        return new { keyboard = c.Keyboard, mouseTricks = c.MouseTricks, sensitivity = c.MouseSensitivity, keys = c.Keys };
    }

    private void ApplyControls(IDictionary<string, string?> env)
    {
        var c = InputControls();
        env["REX_MNK_MODE"] = c.Keyboard ? "true" : "false";
        env["REX_MNK_MOUSE"] = c.Keyboard && c.MouseTricks ? "true" : "false";
        env["REX_MNK_SENSITIVITY"] = c.MouseSensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (button, keys) in c.Keys)
            if (DefaultKeys.ContainsKey(button)) env["REX_KEYBIND_" + button.ToUpperInvariant()] = keys;
    }
}
