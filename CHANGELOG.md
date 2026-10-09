# ReStreet - FIFA Street 2012 Recompiled: changelog

## v1.1

Smoother graphics and VSync, direct play, and a setup and launcher that fit any screen.

### New
- **New logo: ReStreet.** The main menu and the Press Start screen now show the ReStreet logo. It is applied by a small patch in the new `Mods` folder next to `Game` and `GameData`, which the game applies while reading its files; your game files are not modified. Delete the file in `Mods` to get the original logo back.
- **Improved "Some" graphics readback, now the default.** The Some readback mode was reworked to remove the graphical garbage it used to cause on some courts (for example floor reflections and flickering floor lines) while keeping its performance. It is now the default in the Balanced and Quality profiles. Occlusion queries are off by default in those profiles. If you prefer the old behaviour, pick Full (compatible) in the Compatibility tab. Settings you already saved are kept.
- **VSync now works smoothly.** The old VSync made the game run at only a few FPS on some PCs (every wait for the graphics card cost two screen refreshes). VSync now keeps the game locked to your refresh rate and shows each frame without blocking the graphics queue, with no tearing. Turning VSync off still gives an unlocked frame rate.
- **Direct play.** Start the game without opening the launcher window: run `FifaStreetLauncher.exe --play`, or click the new **Shortcut** button in the launcher to create a desktop shortcut that does exactly that. It uses the settings you last chose in the launcher and closes by itself after the game exits.

### Fixed
- **Setup and launcher layout on any resolution or Windows scale.** On some screens (for example a 4K TV set to 1080p with Windows scale at 150%) the controls could end up smaller than the window, overlapping the title and cut off. The window, controls and text are now laid out from one scale that follows your monitor's DPI and always fits the screen, including 100% to 200% scale and small laptop screens.
- The window now follows you when it is moved to a monitor with a different scale.

## v1.0

First stable release. This version is offline only: it has no online play.

**The project is now called ReStreet - FIFA Street 2012 Recompiled.** The launcher, the setup and the desktop shortcut use the new name. Your install folder, saves and settings are not affected.

### New
- **Performance profiles in the launcher.** Choose Balanced (the default), Quality for strong PCs, or Performance for weaker PCs. Performance uses native resolution, no post-processing, a faster graphics readback and no occlusion queries. If you change any single option by hand, the profile switches to Custom.
- **Shader cache included.** A set of pre-built shaders now ships with the game and is copied on the first run, so the first matches have fewer slow moments. Shaders you already have are never overwritten.
- **FPS counter.** Press Home to show or hide a small FPS number in the top-left corner. It scales with the game's resolution and changes colour: green from 55 FPS, yellow from 30, red below. It replaces the old statistics panel.
- **Exit shortcut.** Hold START and press B (Xbox layout) or START and Circle (PlayStation layout) on any screen to open "EXIT THE GAME?". A confirms, B cancels, and the game then closes cleanly. Available in English, German, French, Spanish, Italian and Portuguese.

### Unchanged
- The game files are the same as in v0.4.1, so World Tour, saves and your launcher settings keep working. Saves live in `Documents\fifastreet` and are not touched by the installer.

### Notes
- Performance still depends on your hardware and drivers. The profiles are starting points, not guarantees.
- The project's MIT licence does not establish rights to redistribute game-derived binaries; no legal clearance is claimed.

## v0.4.1 Experimental

Same precompiled game binaries as v0.4.0; the launcher and installer changed.


- Launcher: new Readback Resolve option (Full, Some, Fast, None) in the Compatibility panel. Full remains the default because it is the validated mode; the other modes are not validated and Fast previously caused graphical corruption.
- Lighter first-run defaults for better performance: 1x internal scale, FXAA, MSAA off and Memory Page State off. Existing launcher settings are not migrated; they keep their saved values.
- Frame stats logging is now optional ("Frame stats log") and off by default.
- The status bar warns when 3x or higher internal resolution is selected.
- Launcher validates the selected resolution and monitor, and falls back to defaults when settings cannot be read.
- Installer keeps an existing `fifastreet.toml` when installing over a previous copy, and reports an ISO with several `default.xex` files as unsupported.

## v0.4.0 Experimental

- Faster installation using bundled precompiled binaries for the verified game build; no Visual Studio, compiler download or local game compilation in this installer.
- Existing-installation updates: select the folder containing Game and GameData. Saves, extracted game data, launcher preferences and existing runtime configuration are preserved.
- Update integrity checks, backups of replaced files and restoration on handled errors or cancellation. Power-loss recovery is not automatic.
- Clear installation/update instructions and corrected folder browsing when the default installation path does not exist.
- Launcher language selection: English, Spanish, French, German and Italian. All five were tested by Samuel.
- First-run defaults: Direct3D 12, fullscreen, 2x internal scale, FXAA Extreme, native 2x MSAA, VSync off and D3D12 VRR/tearing on. Resolution and refresh rate follow the selected primary display; existing preferences are retained.
- Rendering, synchronization and frame-pacing corrections for the tested Direct3D 12 and Vulkan configurations, retaining Full Readback for compatibility. No universal FPS guarantee or claim of unlocked game simulation.
- World Tour FootballCompEng function-boundary override; local code-generation, copy-routine validation and Samuel's World Tour test passed.
- Contributor fixes for Windows patch line endings, module-path lookup and diagnostics, flexible data discovery and resource compilation. The latest source improvements to data-discovery error handling require a future main-executable rebuild; they are not claimed as changes in the bundled main executable.
- Credits to Emran_Ahm3d in the documentation, installer and launcher.

Limitations: a separate clean Windows installation/update test has not been confirmed. The project's MIT licence does not establish rights to redistribute game-derived binaries; no legal clearance is claimed. Applicable bundled notices are included.
