# FIFA Street Recompiled — Experimental Windows Installer

**PORTED BY: SAMUELITODAVILA**  
**Developed with AI assistance from ChatGPT and OpenAI Codex.**

An experimental Windows x64 port of FIFA Street (2012), powered by ReXGlue. Select your own Xbox 360 ISO and an installation folder; the installer extracts your files, applies the project credits and recompiles the game locally.

## Included

- English graphical installer and launcher with Messi artwork and FIFA Street icons.
- Local generation of `fifastreet.exe` and `fifastreet_fifadllzf_xex.dll`.
- Public extraction, SDK and compiler tools, with a self-contained .NET runtime.
- Vulkan graphics/runtime libraries from the working development build.
- Output/window resolution linked to the internal integer render scale; internal size displayed in the launcher.
- `PORTED BY: SAMUELITODAVILA` on the start screen and main menu, applied locally from a small difference recipe.
- SDL minimize/restore correction included in the runtime.
- Progress, logs, cancellation and an optional desktop shortcut.

## Evidence

Real developer captures show the start screen, credited main menu and indoor gameplay. The credit patch was verified against the working game, and the developer confirmed window restoration after the SDL fix. The same corrected runtime is included in this installer.

An earlier complete installer run reached success in approximately **22 minutes and 12 seconds**. That measurement predates the latest fixes. The gameplay screenshot's **78.3 recent FPS** is a single sample, not a promised or sustained frame rate.

## Install

1. Open the release installer and select your own supported FIFA Street ISO.
2. Choose a new or empty installation folder and click **INSTALL FIFA STREET**.
3. Wait for extraction, credit patching and local recompilation.
4. After success, optionally select **Create desktop shortcut**, then **OPEN LAUNCHER**.

## Current limits

Mark the first public release **Pre-release**. The latest complete ISO installation test, clean-PC prerequisite setup, full game-mode coverage, save/load, audio/controller coverage and a supported ISO/hardware list remain pending. Integer render scaling means intermediate output resolutions use a larger internal image, resized for display.

Cancelled or failed installs can leave incomplete files. Include `installation.log`, game/build logs and reproduction steps in bug reports. Do not attach game files.

No ISO, original XEX, extracted game archive or generated guest code is included. Project-authored code uses MIT; third-party components retain their licenses. See the README and license notices.
