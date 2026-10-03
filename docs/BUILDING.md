# Building from source

The usual player workflow uses the release installer and a personal ISO. These instructions describe development and packaging. They do not include game data or generated guest code.

## Developer prerequisites

- Windows x64, Git and the .NET 8 SDK with Windows desktop support.
- Microsoft C++ Build Tools and Windows SDK, available through a developer terminal.
- LLVM/Clang, CMake and Ninja.
- The ReXGlue checkout and dependencies, including the project patch.
- A locally built or obtained `extract-xiso.exe` from [XboxDev/extract-xiso](https://github.com/XboxDev/extract-xiso), placed in `Installer/tools/`.
- Your own supported game ISO or extracted `GameData` for game builds.

## Prepare ReXGlue

From the repository root:

    git clone --recursive https://github.com/rexglue/rexglue-sdk.git ReXGlue
    git -C ReXGlue checkout c94f5ebdcb3c9d1a460ca48e04f9758448f8d518
    git -C ReXGlue submodule update --init --recursive
    git -C ReXGlue apply --check ../patches/rexglue-local-changes.patch
    git -C ReXGlue apply ../patches/rexglue-local-changes.patch

The recorded project patch has been checked and applied successfully against this ReXGlue base. It contains project-specific code-generation, graphics, memory, runtime, scaling, diagnostics and window/input compatibility changes.

Do not assume a newer ReXGlue revision is compatible with the patch.

## Build the base SDK

Build and install the base SDK in a configured Windows C++ developer terminal:

    cmake -S ReXGlue --preset win-amd64
    cmake --build ReXGlue/out/build/win-amd64 --config Release --parallel 2
    cmake --install ReXGlue/out/build/win-amd64 --config Release

## Build the Direct3D 12 backend

The v0.3.0 package contains a separate validated Direct3D 12 runtime/GPU pair.

Use the project's configured Clang/Ninja ReXGlue build with Direct3D 12 enabled and Vulkan disabled. Keep `rexruntime.dll` and `rexgpu-xenos.dll` from the same build together.

The release packaging script expects the validated Direct3D 12 pair under the project's D3D12 backend output directory and verifies its SHA-256 hashes before packaging.

Validated release hashes:

- `rexruntime.dll`: `790B6B1B13765249160E53DCE6F28AF0F03B66D5D1AEDB1054949C50B13F04E8`
- `rexgpu-xenos.dll`: `461A88F1D27C605106453B0F3C92BD72BD4BC166DCDC0BBF01E825BCEFB8418D`

## Build the Vulkan backend

Build the separate Vulkan renderer/runtime using the project's Clang/Ninja configuration. Keep the runtime and GPU DLL from the same build together.

The release packaging script expects the validated Vulkan pair under the project's Vulkan backend output directory and verifies its SHA-256 hashes before packaging.

Validated release hashes:

- `rexruntime.dll`: `115722CC5905D07FC6A6212B47692FFE6405DD1BA9F87CDA97F8DA2C25E4767A`
- `rexgpu-xenos.dll`: `71FA61D82FF6134F1F407D682ACEBAC01F2B3B2DCEFCC34151A0C082B746BDD2`

The runtime and GPU DLL from each backend must remain paired.

## Build the launcher

    dotnet publish Launcher/FifaStreetLauncher/FifaStreetLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/launcher

The launcher expects installed backend pairs under:

    Game/Backends/D3D12/
    Game/Backends/Vulkan/

It activates the selected pair beside `fifastreet.exe` before starting the game.

## Package the installer

The source images and icon are tracked. Compiled SDK tools, `extract-xiso.exe`, compiler binaries and `bundle.zip` remain local and excluded from Git.

    ./Installer/BuildSetup.ps1 -Output "$PWD/dist/FifaStreetSetup.exe"

The packaging script creates the public-tool bundle, publishes the launcher and publishes the self-contained installer.

The v0.3.0 packaging pipeline also:

- includes the validated Direct3D 12 and Vulkan backend pairs;
- verifies all four backend DLL hashes;
- keeps the backend pairs in separate package directories;
- installs Direct3D 12 as the initial active backend;
- rejects game archive/executable extensions from the public bundle.

Review exact-version third-party notices before distributing a public release.

The installer embeds `credit-patch.json.gz` and the managed LZX decoder. The recipe is the small difference needed for the two credit menus; it is not a copy of either menu archive. No original game font or modified BIG archive is included.

## Compile from extracted game data

Set `REXSDK_DIR` to the prepared ReXGlue SDK and run `Installer/BuildFifaStreet.ps1` with your private extracted `GameData` and an output directory.

The build pipeline produces:

    fifastreet.exe
    fifastreet_fifadllzf_xex.dll
    fifastreet_FootballCompEngzf_xex.dll

`FootballCompEngzf.xex.dll` is read from the game's DLC tree and recompiled as an independent guest module for World Tour.

The graphical installer additionally applies the menu credits before recompilation. Do not commit extracted files or generated guest source.

## Verify before publishing

Use [STATUS.md](STATUS.md) as the validation record.

For a release, verify:

- complete ISO-to-install workflow;
- start-screen/main-menu credits;
- all three recompiled outputs;
- Direct3D 12 launch;
- Vulkan launch;
- backend switching;
- output/internal-resolution independence;
- World Tour;
- public-package game-file exclusion;
- ReXGlue patch application against the recorded base.

Upload the installer as a Release asset; commit source, documentation, screenshots and notices. See [GitLab build and migration instructions](GITLAB.md).
