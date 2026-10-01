<div align="center">

<img src="docs/images/project-icon.png" alt="FIFA Street" width="64">

# FIFA Street Recompiled

**My experimental Windows PC port of FIFA Street (2012), built with ReXGlue.**

### PORTED BY: SAMUELITODAVILA

[Downloads](https://github.com/samuelitodavila/fifa-street-pc/releases) · [Report a bug](https://github.com/samuelitodavila/fifa-street-pc/issues) · [Build from source](docs/BUILDING.md)

<img src="docs/images/start-screen.png" alt="FIFA Street running on PC" width="1000">

</div>

## About

I'm bringing FIFA Street (2012) to Windows PC using ReXGlue. I've built a graphical installer and a dedicated launcher with display, graphics and compatibility settings.

The installer uses your own Xbox 360 ISO to extract the game files, apply my menu credits and compile the game locally. I don't include an ISO, original game data or generated game binaries in this repository or installer.

## Download and install

My first experimental release is now available. Download **FifaStreet-Setup.exe** from the [release page](https://github.com/samuelitodavila/fifa-street-pc/releases/tag/v0.1.0-experimental).

**You'll need:** Windows x64, your own Xbox 360 FIFA Street ISO, a Vulkan-capable GPU and at least 12 GB free on the destination drive, plus extra space for temporary and build files. An Internet connection may be needed to install Microsoft C++ build components.

1. Run **FifaStreet-Setup.exe**.
2. Select your ISO and a new or empty installation folder.
3. Click **INSTALL FIFA STREET** and wait for extraction and local compilation.
4. Once complete, click **OPEN LAUNCHER**, choose your settings and press **PLAY**.

The installer includes its .NET runtime, SDK and compiler tools. My earlier installation test took around **22 minutes**, but your installation time may vary.

## Screenshots

<img src="docs/images/installer.png" alt="My FIFA Street installer" width="900">

<img src="docs/images/launcher.png" alt="My FIFA Street PC launcher" width="1000">

<img src="docs/images/gameplay.png" alt="FIFA Street gameplay on PC" width="1000">

## Current status

I've tested an earlier complete installation and captured the start screen, menus and indoor gameplay on PC. I've also corrected Vulkan packaging, internal render scaling and window restoration.

This is still experimental. I haven't completed a full installation test of the latest installer or verified every game mode, audio, controllers and saves. See [known issues and validation](docs/STATUS.md).

If you encounter a problem, [open an issue](https://github.com/samuelitodavila/fifa-street-pc/issues) with your hardware, Windows and driver versions, settings and relevant logs. Please don't upload ISOs or game files.

## Credits and licence

I developed this project with assistance from **ChatGPT and OpenAI Codex**, and directed and tested the work myself.

Thanks to [ReXGlue](https://github.com/rexglue/rexglue-sdk), [Xenia](https://github.com/xenia-project/xenia), [extract-xiso](https://github.com/XboxDev/extract-xiso), LLVM, CMake, Ninja, .NET and MonoGame. See the [third-party notices](licenses/README.md).

My original installer, launcher and scripts use the [MIT License](LICENSE). Third-party components keep their own licences. FIFA Street, its game content, artwork and trademarks belong to their respective owners.

This is my unofficial community project, with no affiliation to EA, Microsoft or Xbox.
