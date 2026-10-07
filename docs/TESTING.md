# Test procedure

The recorded results are in [STATUS.md](STATUS.md). These steps describe checks for a new build; unchecked steps must not be reported as passed.

## Installer

1. Use a personal supported Xbox 360 ISO and a new, empty installation folder.
2. Record installer version, start/end times, Windows version, CPU, GPU/driver, free space and ISO region/revision.
3. Confirm extraction, menu-credit patching and compilation finish successfully.
4. Confirm the following files exist:
   - `Game/fifastreet.exe`
   - `Game/fifastreet_fifadllzf_xex.dll`
   - `Game/fifastreet_FootballCompEngzf_xex.dll`
5. Confirm `Game/Backends/D3D12` and `Game/Backends/Vulkan` each contain `rexruntime.dll` and `rexgpu-xenos.dll`.
6. Confirm the initial root runtime/GPU pair corresponds to Direct3D 12.
7. Test the optional desktop shortcut and **OPEN LAUNCHER**.
8. Repeat separately on a clean Windows machine to test missing C++ prerequisites.

For cancellation, use a disposable new destination, cancel during extraction/build, and confirm the active child process stops. Partial installation files can remain. Do not reuse that destination as a completed game.

## Backend switching

Test both graphics APIs from the same clean installation.

### Direct3D 12

1. Select **Direct3D 12**.
2. Confirm GPU-adapter selection is enabled.
3. Press **PLAY**.
4. Confirm the game starts and renders correctly.
5. Record the active root runtime/GPU hashes if validating a release package.

### Vulkan

1. Select **Vulkan**.
2. Confirm the launcher uses automatic Vulkan device selection.
3. Press **PLAY**.
4. Confirm the game starts and renders correctly.
5. Confirm the active root runtime/GPU pair has changed to the packaged Vulkan pair.

Switch back to Direct3D 12 and repeat when validating backend switching in both directions.

## Resolution testing

Output resolution and internal rendering resolution must be tested separately.

The launcher exposes internal scales:

- 1x — 1280x720
- 2x — 2560x1440
- 3x — 3840x2160
- 4x — 5120x2880

Test output modes supported by the display, including 1280x720, 1920x1080, 2560x1440, 3200x1800 and 3840x2160 where applicable.

Changing output resolution must not implicitly change the selected internal scale.

Record both values in performance reports.

## Credits and content checks

The local patch test was run on copies of the original `data1.big` and `data1.bh`, using the packaged installer's `--credit-test <GameDataFolder> <LogPath>` developer entry point. Wait for the process to exit before inspecting the log.

Verification compared all outer archive entries with the original. Only `data/ui/game/screens/bootflow/03_pressstart.big` and `data/ui/game/screens/menus/mainmenu.big` changed. Both replacement payloads matched the working game byte for byte, and each companion-index offset/length matched. Running the patch again reported that the credits were already applied.

For a new recipe or ISO revision, repeat those checks with local private files. Do not commit the archives, font, generated code or private test fixtures.

Audit the distributable bundle separately for game ISOs, original XEX files, BIG/BH archives and generated game executables. The presentation images and credit difference recipe are intentional source assets, with their license scope documented.

## Launcher and game

1. Confirm the start screen and main menu display **PORTED BY: SAMUELITODAVILA**.
2. Confirm Direct3D 12 and Vulkan can each launch the game.
3. Confirm the Readback Resolve option is set to Full (the default) unless you are testing another mode.
4. Test fullscreen and windowed mode. Minimize/restore repeatedly from the menu and during play.
5. Play a complete match. Check controls, sound, transitions and results.
6. Enter World Tour and exercise the progression path that previously failed.
7. Save, close the game, relaunch and load the saved state.
8. Test additional modes/venues and controllers individually. Record unsupported options and reproduction steps.

## ReXGlue patch

Before a release, verify the project patch against the recorded ReXGlue base:

    git -C ReXGlue apply --check ../patches/rexglue-local-changes.patch
    git -C ReXGlue apply ../patches/rexglue-local-changes.patch

The v0.3.0 patch was checked and applied successfully against ReXGlue commit `c94f5eb`.

## Performance reporting

Use a repeatable scene and settings, identify hardware/driver and graphics API, and report output resolution and internal scale separately.

Distinguish a cold shader cache from a warm cache. A screenshot of recent FPS is useful evidence of that moment, not a sustained benchmark.

Do not infer a universal performance guarantee from a single machine. In particular, availability of 3x/4x internal scaling or 4K output does not imply that those combinations maintain 60 FPS.
