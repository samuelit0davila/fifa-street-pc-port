# ReStreet - FIFA Street 2012 Recompiled v1.1

Smoother graphics and VSync, direct play, the ReStreet logo, and a setup and launcher that fit any screen. This version is offline only.

## New in v1.1

- **ReStreet logo.** The main menu and the Press Start screen now show the ReStreet logo. It is applied by a small patch in the new `Mods` folder next to `Game` and `GameData`, which the game applies while reading its files. Your game files are not modified: delete the files in `Mods` to get the original logo back.
- **Improved "Some" graphics readback, now the default.** The Some mode was reworked to remove the graphical garbage it used to cause on some courts (floor reflections, flickering floor lines) while keeping its speed. It is the default in the Balanced and Quality profiles, and occlusion queries are off by default there. Pick "Full (compatible)" in the Compatibility tab if you prefer the old behaviour. Settings you already saved are kept.
- **VSync works smoothly.** The old VSync made the game run at only a few FPS on some PCs. VSync now keeps the game locked to your refresh rate with no tearing. Turning VSync off still gives an unlocked frame rate.
- **Direct play.** Start the game without opening the launcher window: run `FifaStreetLauncher.exe --play`, or click the new **Shortcut** button in the launcher to create a desktop shortcut that does exactly that. It uses the settings you last chose in the launcher and closes by itself after the game exits.

## Fixed

- Setup and launcher layout on any resolution or Windows scale. On some screens (for example a 4K TV set to 1080p with Windows scale at 150%) the controls could be smaller than the window, overlap the title and be cut off.
- The window follows you when it is moved to a monitor with a different scale.

## Unchanged

The game files are the same as in v0.4.1, so World Tour, saves and your launcher settings keep working. Saves live in `Documents\fifastreet` and are not touched by the installer.

## How to install

1. Download `FifaStreetSetup.exe` and check its SHA-256 against the value on the release page.
2. Run it, choose your own FIFA Street (2012) Xbox 360 ISO and an empty folder, and press **INSTALL ReStreet**.
3. Press **OPEN LAUNCHER**, then **PLAY**.

To update, select the folder that contains `Game` and `GameData`. No ISO is needed.

## Requirements (estimates)

Windows 10 or 11 (64-bit), a CPU with SSSE3, a Direct3D 12 or Vulkan GPU (about GTX 900 / RX 400 or newer), 8 GB RAM and 12 GB of free disk space. On a weaker PC use the Performance profile. See [STATUS.md](STATUS.md) for what was and was not tested.

## Notes

- Performance depends on your hardware and drivers; the profiles are starting points, not guarantees.
- The installer includes generated game binaries. The project's MIT licence does not establish the right to redistribute them, and no legal clearance is claimed.
- The Mods logo patch is derived from the game's own art.
- This is an unofficial community project, with no affiliation to EA, Microsoft or Xbox.
