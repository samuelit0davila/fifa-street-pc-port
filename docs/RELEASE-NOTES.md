# FIFA Street Recompiled v0.3.0 Experimental

v0.3.0 is a major experimental update to the FIFA Street PC recompilation project. It introduces a new multi-backend launcher and installer, Direct3D 12 support alongside Vulkan, independent output/internal resolution controls, updated ReXGlue compatibility changes and the previously completed World Tour correction.

## Highlights

- Added **Direct3D 12** as a validated graphics backend.
- Kept **Vulkan** as a validated graphics backend.
- Added graphics-API selection directly to the launcher.
- Added automatic switching of the validated ReXGlue runtime/GPU DLL pair.
- Reworked the launcher UI.
- Separated **Output Resolution** from **Internal Resolution**.
- Added internal rendering scales from **1x to 4x**.
- Added common output modes through **3840x2160**, including 3200x1800.
- Full resolve readback is fixed to the validated configuration.
- Updated the installer to package and verify both graphics backends.
- Updated the ReXGlue project patch and verified that it applies cleanly against the recorded `c94f5eb` base.
- Retained the World Tour `FootballCompEngzf` correction.

## Graphics backends

The release package now contains separate:

- `Game/Backends/D3D12`
- `Game/Backends/Vulkan`

backend directories.

The launcher copies the selected backend's `rexruntime.dll` and `rexgpu-xenos.dll` into the active game directory before launch.

Direct3D 12 is the default graphics API. GPU-adapter selection is available for D3D12.

Vulkan uses automatic device selection.

Both backends were launched successfully from the final clean multi-backend installation.

## Resolution changes

Output resolution no longer determines the internal render scale.

Available internal scales are:

- **1x** — 1280x720
- **2x** — 2560x1440
- **3x** — 3840x2160
- **4x** — 5120x2880

Common output modes exposed by the launcher include:

- 1280x720
- 1920x1080
- 2560x1440
- 3200x1800
- 3840x2160

2560x1440 output with 2x internal rendering was specifically tested during backend validation.

Higher internal scales and 4K output are available for testing, but this release does not claim that every combination maintains 60 FPS.

## World Tour

The installer continues to discover and recompile:

`dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll`

into:

`fifastreet_FootballCompEngzf_xex.dll`

This addresses the previous World Tour crash/return-to-menu during Bronze/Silver/Gold progression.

World Tour was retested successfully on the final clean v0.3.0 multi-backend installation.

## Installer changes

The installer now packages validated Direct3D 12 and Vulkan ReXGlue backend pairs and verifies their SHA-256 hashes.

It still performs the complete local workflow from the user's own Xbox 360 ISO:

1. Extract the ISO.
2. Apply the start-screen and main-menu credits.
3. Recompile the main executable.
4. Recompile `fifadllzf.xex.dll`.
5. Recompile `FootballCompEngzf.xex.dll`.
6. Install both graphics backends.
7. Install the launcher.

No ISO, original game data, XEX files, BIG/BH archives or generated game binaries are distributed with the project.

## Validation

The final v0.3.0 release-candidate installer was tested through a clean installation.

Confirmed in that installation:

- successful ISO extraction and recompilation;
- launcher startup;
- Direct3D 12 launch;
- Vulkan launch;
- automatic backend DLL switching;
- 2560x1440 output with 2x internal rendering;
- Practice;
- World Tour.

The project remains experimental. Broader GPU/driver, controller, audio, save, venue, resolution and long-session testing is still welcome.
