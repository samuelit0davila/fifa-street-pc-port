# Validation and known limitations

Last updated: **2 October 2026**. The project remains experimental. v0.3.0 has completed a clean end-to-end validation of the new multi-backend installer and launcher.

## Confirmed evidence

| Check | Result | Scope |
|---|---|---|
| Current full ISO installation | **Passed** | ISO extraction, credit patch, local recompilation, multi-backend packaging and first launch |
| Main game executable | **Passed** | `fifastreet.exe` produced by the installer |
| Main guest DLL | **Passed** | `fifastreet_fifadllzf_xex.dll` produced by the installer |
| World Tour competition DLL | **Passed** | `fifastreet_FootballCompEngzf_xex.dll` produced from `FootballCompEngzf.xex.dll` |
| World Tour | **Passed in tested flow** | Previous Bronze/Silver/Gold failure corrected; World Tour retested successfully on the final clean installation |
| Practice | **Passed** | Launched successfully from a clean installation |
| Credits in the game | **Passed** | Start-screen and main-menu `PORTED BY: SAMUELITODAVILA` credit present |
| Direct3D 12 backend | **Passed** | Packaged backend launched successfully from the final clean installation |
| Vulkan backend | **Passed** | Packaged backend launched successfully from the final clean installation |
| Backend switching | **Passed** | Launcher selected the requested backend and activated the corresponding validated runtime/GPU DLL pair |
| Resolve readback | **Passed in current configuration** | Full resolve readback is the launcher default |
| Output/internal resolution separation | **Passed** | 2560x1440 output with 1x and 2x internal settings exercised independently |
| Launcher | **Passed** | Graphics API selection, backend activation and launch tested |
| ReXGlue project patch | **Passed** | Patch `--check` and application succeeded against clean ReXGlue `c94f5eb` |
| Game-file exclusion | **Passed** | Public package excludes ISO, original XEX, BIG/BH archives and generated game binaries |

## World Tour correction

World Tour loads an additional competition-engine module from the game's DLC tree:

`dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll`

Earlier builds recompiled the main executable and `fifadllzf.xex.dll` but did not provide a recompiled host module for this dynamically loaded guest DLL.

The current pipeline detects the module, includes it in the ReXGlue manifest, generates its guest source and builds:

`fifastreet_FootballCompEngzf_xex.dll`

The `fifadllzf`-specific stubs are intentionally not attached to this independent module.

## Current clean-install validation

The v0.3.0 setup was tested through the complete player-facing path:

1. Select an original FIFA Street Xbox 360 ISO.
2. Extract the game data.
3. Apply the start-screen and main-menu credit patch.
4. Recompile the main executable.
5. Recompile `fifadllzf.xex.dll`.
6. Recompile `FootballCompEngzf.xex.dll`.
7. Install both validated graphics backends.
8. Start the dedicated launcher.
9. Launch and test Direct3D 12.
10. Switch to Vulkan and launch again.
11. Test gameplay and World Tour.

That workflow completed successfully on the developer's test system.

## Graphics backends

The release package contains separate validated backend directories:

`Game/Backends/D3D12`

`Game/Backends/Vulkan`

The launcher copies the selected backend's `rexruntime.dll` and `rexgpu-xenos.dll` beside `fifastreet.exe` before launch.

Direct3D 12 is the default backend. Vulkan uses automatic Vulkan-device selection.

### Direct3D 12 validated files

- `rexruntime.dll` SHA-256: `790B6B1B13765249160E53DCE6F28AF0F03B66D5D1AEDB1054949C50B13F04E8`
- `rexgpu-xenos.dll` SHA-256: `461A88F1D27C605106453B0F3C92BD72BD4BC166DCDC0BBF01E825BCEFB8418D`

### Vulkan validated files

- `rexruntime.dll` SHA-256: `115722CC5905D07FC6A6212B47692FFE6405DD1BA9F87CDA97F8DA2C25E4767A`
- `rexgpu-xenos.dll` SHA-256: `71FA61D82FF6134F1F407D682ACEBAC01F2B3B2DCEFCC34151A0C082B746BDD2`

The installer verifies these hashes before packaging/installing the backends.

## Renderer configuration

Full resolve readback is the validated configuration and the launcher default. It can be changed in the launcher (Compatibility > Readback Resolve); other modes are not validated.

For Direct3D 12, the validated configuration uses ReXGlue's automatic render-target-path selection.

For Vulkan, the validated configuration uses the `fbo` render-target path and automatic Vulkan device selection.

Fast readback was rejected during development because it produced visible graphical corruption.

## Resolution model

Output resolution and internal rendering scale are independent.

The launcher exposes common output modes through 3840x2160, including 3200x1800.

Internal resolution is based on the game's 1280x720 render base:

- 1x — 1280x720
- 2x — 2560x1440
- 3x — 3840x2160
- 4x — 5120x2880

2560x1440 output with 2x internal rendering was specifically tested with the current backends. Higher combinations remain available for testing but are not a performance guarantee.

## Remaining validation

- Test more World Tour events and longer career progression.
- Exercise prerequisite installation on additional clean Windows systems.
- Verify more complete matches, venues, audio, controller mappings, saves and repeated launches.
- Validate additional graphics-control combinations and ultrawide output.
- Expand Direct3D 12 and Vulkan testing across more GPU vendors and driver versions.
- Record supported ISO regions/revisions. The credit recipe rejects unknown menu hashes.
- Continue performance testing across output/internal-resolution combinations.

## Known limitations

- No universal hardware, driver or frame-rate guarantee.
- 3x/4x internal rendering and 4K output are not guaranteed 60 FPS modes.
- No verified online-play support.
- Internal scaling uses integer multiples of the game's 1280x720 base.
- Some advanced SDK controls remain title/backend-specific and require further validation.
- Generated recompilation warnings remain and should continue to be investigated.
- Cancellation/failure may leave incomplete diagnostic workspaces.
- Free-space requirements depend on extraction, compilation and temporary packaging.

## Development method

SAMUELITODAVILA directed and tested the project with AI assistance from ChatGPT and OpenAI Codex. The validation statements above distinguish what was actually tested from areas that still need broader coverage.
