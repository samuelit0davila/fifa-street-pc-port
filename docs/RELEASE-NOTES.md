# FIFA Street Recompiled v0.2.0 Experimental

This experimental update fixes the World Tour failure found in the previous PC build.

## Changes

- Fixed the World Tour crash/return-to-menu encountered during Bronze/Silver/Gold progression.
- Added discovery and recompilation of `FootballCompEngzf.xex.dll`, the additional competition-engine module used by World Tour.
- The installer now builds and validates `fifastreet_FootballCompEngzf_xex.dll` alongside the main executable and `fifastreet_fifadllzf_xex.dll`.
- Kept the validated Vulkan runtime/GPU pair.
- Kept `readback_resolve = "full"` as the validated resolve-readback configuration.
- Completed a clean end-to-end installation from an original Xbox 360 FIFA Street ISO.
- Confirmed launcher startup, menus, Practice and World Tour on the clean installation.

## Installation

Download **FifaStreetSetup.exe**, run it, select your own Xbox 360 FIFA Street ISO and choose a new or empty installation directory.

No ISO, original game data, XEX files, BIG/BH archives or generated game binaries are distributed with this project.

## Status

This is still an experimental PC port. Broader hardware, controller, audio, save, venue and long-session testing is still in progress.
