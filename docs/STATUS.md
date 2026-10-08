# Validation and known limitations

Last updated: **8 October 2026**, for **ReStreet v1.0** (offline only). This page separates what was tested from what was not.

## What was tested for v1.0

All of this was tested by the developer on one Windows 11 PC with an AMD Radeon RX 9070 XT, at 100% display scale unless stated.

| Check | Result |
|---|---|
| Clean installation from the original ISO with the final installer | Passed |
| Update over an existing installation that has a save | Passed, save kept |
| Direct3D 12: gameplay | Passed |
| Vulkan: gameplay | Passed |
| Windows display scale 125% and 150% (launcher and installer) | Passed |
| Exit shortcut (START + B) and FPS counter (Home) | Passed |
| Small screens: launcher shrinks to fit a reduced work area | Checked in a simulated 700 px high work area |

The game binaries (main module, game module and World Tour module) are the same as in v0.4.1. World Tour, Practice, Direct3D 12 and Vulkan were validated on earlier releases; this release changes the launcher, installer, overlays and performance options.

## Graphics backends

The package contains two validated backend folders, `Game/Backends/D3D12` and `Game/Backends/Vulkan`. The launcher copies the selected backend's `rexruntime.dll` and `rexgpu-xenos.dll` beside `fifastreet.exe` before launch. Direct3D 12 is the default.

### SHA-256 of the backend files in v1.0

| Backend | `rexruntime.dll` | `rexgpu-xenos.dll` |
|---|---|---|
| Direct3D 12 | `941DA42DFBA505F7F011ED81A6C7F6FD31EA2B3ED717810EF8C3E6EF89B56030` | `CC583314A05B11E49910CE0600E69EDD8B51B4C84D63641B7088916BB95551A2` |
| Vulkan | `E4DD721D6D080705B8685E14F8437583CCE7DE2A35298BEAE04B4A91372E5E0F` | `B53E030B6C41EC017F97D53F657C9B8850BB5BEDFE2A0A034EE4871F182DC688` |

The installer checks its payload against the hashes in its own manifest before installing.

## Performance options

- **Profiles:** Balanced (the launcher defaults), Quality (2x internal resolution, FXAA Extreme, MSAA), Performance (native resolution, no post-processing, Fast readback, occlusion queries off) and Custom. They only set options the launcher already has.
- **Readback Resolve:** Full is the validated mode and the default. The Performance profile uses Fast, which gives more FPS under heavy GPU load but has not been validated on other hardware. Some can cause visible graphical corruption and is not used by any profile.
- **Shader cache:** a small set of pre-built shaders ships in `Game/shader-seed` and is copied into the player's cache on the first run. It never replaces files the player already has.

## Resolution model

Output resolution and internal rendering scale are independent. Internal resolution is a whole-number multiple of the game's 1280x720 base: 1x is 1280x720, 2x is 2560x1440, 3x is 3840x2160 and 4x is 5120x2880. Higher settings are available for testing and are not a performance guarantee.

## Not tested

- Any GPU other than the AMD Radeon RX 9070 XT (an RX 6700 was used on earlier releases). NVIDIA and Intel graphics have not been tested.
- Weak or old PCs. The system requirements in the README are estimates.
- The Performance profile on a PC where the GPU is the limit.
- Other ISO regions or revisions: the installer accepts only builds that match the three verified module hashes.
- Long World Tour careers, all venues, all controller types and long play sessions.
- Windows 10.

## Known limitations

- No universal hardware, driver or frame-rate guarantee. 3x/4x internal rendering and 4K output are not guaranteed 60 FPS modes.
- No online play in v1.0.
- The window title of the game itself still reads "FIFA Street PC"; the launcher and installer use the ReStreet name.
- The exit prompt only opens with the START + B (START + Circle) shortcut, not from the main menu.
- The installer expands its details panel by 180 px, which does not fit on screens 768 px high or less.
- Free-space requirements depend on extraction and temporary files.

## Development method

SamuelitoDaVila directed and tested the project with AI assistance from ChatGPT, OpenAI Codex and Anthropic's Claude (Claude Code). The statements above separate what was actually tested from what still needs broader coverage.
