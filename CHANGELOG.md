# Changelog

## Unreleased (next release)

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
