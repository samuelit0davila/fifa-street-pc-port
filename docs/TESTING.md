# Test procedure

The recorded results are in [STATUS.md](STATUS.md). These steps describe checks for a new build; unchecked steps must not be reported as passed.

## Installer

1. Use a personal supported Xbox 360 ISO and a new, empty installation folder.
2. Record installer version, start/end times, Windows version, CPU, GPU/driver, free space and ISO region/revision.
3. Confirm extraction, menu-credit patching and compilation finish successfully.
4. Confirm `Game/fifastreet.exe` and `Game/fifastreet_fifadllzf_xex.dll` exist, with runtime libraries.
5. Test the optional desktop shortcut and **OPEN LAUNCHER**.
6. Repeat separately on a clean Windows machine to test missing C++ prerequisites.

For cancellation, use a disposable new destination, cancel during extraction/build, and confirm the active child process stops. Partial installation files can remain. Do not reuse that destination as a completed game.

## Credits and content checks

The local patch test was run on copies of the original `data1.big` and `data1.bh`, using the packaged installer's `--credit-test <GameDataFolder> <LogPath>` developer entry point. Wait for the process to exit before inspecting the log.

Verification compared all outer archive entries with the original. Only `data/ui/game/screens/bootflow/03_pressstart.big` and `data/ui/game/screens/menus/mainmenu.big` changed. Both replacement payloads matched the working game byte for byte, and each companion-index offset/length matched. Running the patch again reported that the credits were already applied.

For a new recipe or ISO revision, repeat those checks with local private files. Do not commit the archives, font, generated code or private test fixtures.

Audit the distributable bundle separately for game ISOs, original XEX files, BIG/BH archives and generated game executables. The presentation images and credit difference recipe are intentional source assets, with their license scope documented.

## Launcher and game

1. Confirm the start screen and main menu display **PORTED BY: SAMUELITODAVILA**.
2. Choose 1280×720, 1920×1080, 2560×1440 and 3840×2160 where the display supports them. Record the internal size shown by the launcher and the runtime's actual render-scale behavior.
3. Test both fullscreen and windowed mode. Minimize/restore repeatedly from the menu and during play; the game should repaint and remain responsive.
4. Play a complete match. Check controls, sound, transitions and results.
5. Save, close the game, relaunch and load the saved state.
6. Test additional modes/venues and controllers individually. Record unsupported options and reproduction steps.

## Performance reporting

Use a repeatable scene and settings, identify hardware/driver, report resolution and internal scale, and distinguish a cold shader cache from a warm cache. A screenshot of recent FPS is useful evidence of that moment, not a sustained benchmark.

The supplied gameplay capture shows **78.3 recent FPS**. No general performance guarantee is inferred from it.
