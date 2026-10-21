<div align="center">

<img src="docs/images/project-icon.png" alt="ReStreet" width="64">

# ReStreet — FIFA Street (2012) on PC

**FIFA Street (2012) recompiled for Windows, with online play. Bring your own game.**

### Ported by: SamuelitoDaVila

[**Download**](https://github.com/samuelit0davila/fifa-street-pc-port/releases/latest) · [Changelog](CHANGELOG.md) · [Known issues](docs/STATUS.md) · [Report a bug](https://github.com/samuelit0davila/fifa-street-pc-port/issues) · [Build from source](docs/BUILDING.md)

[![Website](https://img.shields.io/badge/Website-ReStreet-1f6feb?style=for-the-badge&logo=googlechrome&logoColor=white)](https://samuelit0davila.github.io/fifa-street-pc-port/)
[![Discord](https://img.shields.io/badge/Discord-Join%20the%20server-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/CqjYMGKyy2)
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Z3B428N88S)

[![Latest release](https://img.shields.io/github/v/release/samuelit0davila/fifa-street-pc-port?include_prereleases&label=latest&style=flat-square)](https://github.com/samuelit0davila/fifa-street-pc-port/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/samuelit0davila/fifa-street-pc-port/total?style=flat-square)](https://github.com/samuelit0davila/fifa-street-pc-port/releases)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)

</div>

https://github.com/user-attachments/assets/eb97ba0b-2c44-4b54-a93f-65220b0c3d40

## What is this

ReStreet is a Windows PC version of FIFA Street (2012), made by recompiling the Xbox 360 game. It comes with a one-file installer, a launcher, and free online play on community servers (Europe and South America).

You need your own copy of the game: an **Xbox 360 ISO of FIFA Street (2012)**. The ISO and the game files are not included.

**Current version: 2.0.6 Online Beta.** Trailer and screenshots on the [website](https://samuelit0davila.github.io/fifa-street-pc-port/).

## What's new in 2.0

- **New graphics engine**, based on AnDr3zF's "Deck" port: sharper image and much higher frame rates (about 400 FPS in a match on the test PC). Direct3D 12 and Vulkan.
- **Graphics options:** FSR 1, internal resolution up to 4x, aspect ratio, anisotropic filtering, ambient occlusion (SSAO / HBAO / GTAO), MSAA, FXAA and an FPS limit.
- **Launcher social page:** who is online, friends and requests, chat, and **game rooms** that bring both players to the same lobby.
- **In-game friends Guide:** press **F6** (or the Xbox button on a controller) to see your friends and invite them. Accept an invite with A / Space, decline with B / Shift.
- **Brazilian Portuguese** translation by Eze_.
- **Automatic updates:** the launcher finds new versions, checks them and installs them, keeping your name, server, settings and World Tour progress.

**Latest fixes (2.0.4 – 2.0.6):** online sign-in, crashes entering or during a match, ultrawide screens, older CPUs without AVX2, World Tour saves (OneDrive, "corrupt save", progress from 2.0.0 / 2.0.1), My Squad faces, Portuguese (PT-BR) in game, and achievements on Windows user names with accents.

Full history: [CHANGELOG.md](CHANGELOG.md).

## Download and install

**New install**

1. Download **ReStreet-2.0.6-Setup.exe** from the [latest release](https://github.com/samuelit0davila/fifa-street-pc-port/releases/latest). Optionally check it against the `.sha256` file next to it.
2. Run it, choose your FIFA Street ISO, a folder, your online name and your server (Europe or South America).
3. Open the launcher from the desktop shortcut and press **PLAY**.

**Already have ReStreet?**

- On **1.5.1 or newer**: open the launcher and press **Update**. That's it.
- On an older version: run the setup and pick your ReStreet folder. No ISO is needed; saves, settings and your online name are kept.

**Step-by-step guide (PDF, with pictures):** [English](docs/guides/ReStreet-Guide-EN.pdf) · [Português](docs/guides/ReStreet-Guide-PT.pdf) · [Español](docs/guides/ReStreet-Guide-ES.pdf) — install, update, launcher settings, friends and fixing problems.

## System requirements

Estimates from testing, not guarantees.

| | |
|---|---|
| System | Windows 10 or 11, 64-bit (Linux: see below) |
| CPU | x86-64; a quad-core is safest |
| GPU | Direct3D 12 or Vulkan with up-to-date drivers |
| RAM | 8 GB |
| Disk | about 12 GB free for a new install |
| Game | Your own FIFA Street (2012) Xbox 360 ISO |

On a weaker PC, open **Graphics** in the launcher: use **1x** internal resolution and turn off ambient occlusion and MSAA.

## Linux (beta)

ReStreet runs on Linux through Proton, with a native Linux launcher. Tested on CachyOS with an AMD GPU; online play on Linux is not fully tested yet.

1. Install `umu-launcher`, `webkit2gtk-4.1` and a Proton build (Proton-CachyOS or GE-Proton recommended). On Arch / CachyOS:

   ```bash
   sudo pacman -S umu-launcher webkit2gtk-4.1
   ```

2. Install ReStreet from your ISO. The setup's normal window does not work under Proton, so use its install mode instead (your ISO, the folder to install to, your online name):

   ```bash
   umu-run ReStreet-2.0.7-Setup.exe --iso-install-test 'Z:\home\you\FIFASTREET.iso' 'Z:\home\you\Games\ReStreet' YourName
   ```

   `Z:` is your Linux `/`, so `Z:\home\you\...` is `/home/you/...`.
3. Download **ReStreet-Linux-launcher.tar.gz** from the [latest release](https://github.com/samuelit0davila/fifa-street-pc-port/releases/latest) and extract it into your ReStreet folder, next to `Binaries` and `Game`.
4. Run `Linux/restreet-launcher` and press **PLAY**. Press **Shortcut** in the launcher to add ReStreet to your applications menu.

On Linux the launcher cannot update itself yet: to update, download the new launcher and run the new setup.

## Online play

- Free open beta on two servers: **Europe** and **South America**. Players, friends and leaderboards are per server.
- Online modes, friend invites, launcher rooms and the News Feed work. Chat is in the launcher.
- Your World Tour team is shown to other players on your server.

## Known limits (2.0)

- No mouse for skill moves yet (the launcher hides that option on 2.0).
- Vulkan can have small stutters, and on AMD cards it can crash in the driver; use Direct3D 12 there.
- The game renders internally at 1280x720 and is scaled to your screen.
- START + B also shows the game's own "leave this session?" question behind the launcher's; close it with A.
- A launcher room does not pull a player out of another lobby; leave that lobby first.
- Two copies of the game on the same PC can lose the connection after an invite (different PCs are fine).

More details and what was tested: [docs/STATUS.md](docs/STATUS.md).

## Reporting a problem

[Open an issue](https://github.com/samuelit0davila/fifa-street-pc-port/issues) or ask on [Discord](https://discord.gg/CqjYMGKyy2) with:

- your ReStreet version, Windows version, CPU, GPU and RAM;
- the graphics API (Direct3D 12 or Vulkan) and your launcher settings;
- what happened and how to repeat it;
- the logs: **Open logs** in the launcher (`Binaries\Logs\fifastreet.log` and `launcher_settings.txt`), plus `Binaries\crash_motivo.txt` if the game crashed.

Please do not upload ISOs or game files.

## Credits and licence

- **SamuelitoDaVila** — port, online services, launcher, installer, 2.0 integration and testing.
- **AnDr3zF** — the "Deck" port that the 2.0 graphics engine is built on.
- **Eze_** — Brazilian Portuguese translation.
- **Emran_Ahm3d** — Windows build fixes, game-data discovery, FootballCompEng function boundaries, and the Discord Rich Presence integration.
- Developed with assistance from ChatGPT, OpenAI Codex and Anthropic's Claude (Claude Code), directed and tested by SamuelitoDaVila.

Thanks to [ReXGlue](https://github.com/rexglue/rexglue-sdk), [Xenia](https://github.com/xenia-project/xenia), [extract-xiso](https://github.com/XboxDev/extract-xiso), LLVM, CMake, Ninja, .NET and MonoGame. Country data: IP to country by [DB-IP.com](https://db-ip.com) (CC BY 4.0). See [CREDITS.md](CREDITS.md) and the [third-party notices](licenses/README.md).

My original installer, launcher and scripts use the [MIT License](LICENSE). Third-party components keep their own licences. The installer includes generated game binaries; the MIT licence does not establish the right to redistribute them, and no legal clearance is claimed. FIFA Street, its game content, artwork and trademarks belong to their respective owners.

This is an unofficial community project, with no affiliation to EA, Microsoft or Xbox.
