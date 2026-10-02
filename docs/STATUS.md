# Validation and known limitations

Last updated: **2 October 2026**. The project remains experimental, but the current installer pipeline has now completed a clean end-to-end validation.

## Confirmed evidence

| Check | Result | Scope |
|---|---|---|
| Current full ISO installation | **Passed** | ISO extraction, credit patch, local recompilation, packaging and first launch |
| Main game executable | **Passed** | `fifastreet.exe` produced by the installer |
| Main guest DLL | **Passed** | `fifastreet_fifadllzf_xex.dll` produced by the installer |
| World Tour competition DLL | **Passed** | `fifastreet_FootballCompEngzf_xex.dll` produced from `FootballCompEngzf.xex.dll` |
| World Tour | **Passed in tested flow** | Bronze/Silver/Gold progression no longer returns to the menu at the previous failure point |
| Practice | **Passed** | Launched successfully from the clean installation |
| Credits in the game | **Passed** | Start-screen and main-menu credit patch present |
| Vulkan runtime packaging | **Passed** | Installer pins the runtime/GPU pair validated with FIFA Street |
| Resolve readback | **Passed in current configuration** | `readback_resolve = "full"` is the validated configuration |
| Launcher | **Passed** | Clean installation launched successfully through the included launcher |
| Game-file exclusion | **Passed** | Public package excludes ISO, original XEX, BIG/BH archives and generated game binaries |

## World Tour correction

World Tour loads an additional competition-engine module from the game's DLC tree:

`dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll`

Earlier builds recompiled the main executable and `fifadllzf.xex.dll` but did not provide a recompiled host module for this dynamically loaded guest DLL. The current pipeline detects that module, includes it in the ReXGlue manifest, generates its guest source and builds:

`fifastreet_FootballCompEngzf_xex.dll`

The `fifadllzf`-specific stubs are intentionally not attached to this independent module.

## Current clean-install validation

The current setup was tested through the complete player-facing path:

1. Select an original FIFA Street Xbox 360 ISO.
2. Extract the game data.
3. Apply the start-screen and main-menu credit patch.
4. Recompile the main executable.
5. Recompile `fifadllzf.xex.dll`.
6. Recompile `FootballCompEngzf.xex.dll`.
7. Install the validated Vulkan runtime/GPU pair.
8. Start the dedicated launcher.
9. Launch the game.
10. Test menus, Practice and World Tour.

That workflow completed successfully on the developer's test system.

## Renderer/runtime configuration

The release packaging script verifies the validated runtime files before creating the installer:

- `rexruntime.dll` SHA-256: `115722CC5905D07FC6A6212B47692FFE6405DD1BA9F87CDA97F8DA2C25E4767A`
- `rexgpu-xenos.dll` SHA-256: `71FA61D82FF6134F1F407D682ACEBAC01F2B3B2DCEFCC34151A0C082B746BDD2`

The current known-good game configuration uses full resolve readback.

## Remaining validation

- Test more World Tour events and a longer career progression.
- Exercise prerequisite installation on additional clean Windows systems.
- Verify more complete matches, venues, audio, controller mappings, saves and repeated launches.
- Validate individual graphics controls and ultrawide output.
- Record supported ISO regions/revisions. The credit recipe rejects unknown menu hashes.
- Continue hardware/driver testing beyond the developer's current system.

## Known limitations

- No universal hardware/driver or frame-rate guarantee.
- No verified online-play support.
- The renderer uses integer multiples of the game's 1280×720 base for internal scaling.
- Some SDK controls may not apply to the Vulkan backend or may need title-specific testing.
- Generated recompilation warnings remain and should continue to be investigated.
- Cancellation/failure may leave incomplete diagnostic workspaces.
- Free-space requirements depend on extraction, compilation and temporary packaging.

## Development method

SAMUELITODAVILA directed and tested the project with AI assistance from ChatGPT and OpenAI Codex. The validation statements above distinguish what was actually tested from areas that still need broader coverage.
