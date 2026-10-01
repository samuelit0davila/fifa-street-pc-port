# Building from source

The usual player workflow uses the release installer and a personal ISO. These instructions describe development and packaging. They do not include game data or generated guest code.

## Developer prerequisites

- Windows x64, Git and the .NET 8 SDK with Windows desktop support.
- Microsoft C++ Build Tools and Windows SDK, available through a developer terminal.
- LLVM/Clang, CMake and Ninja. The development package uses LLVM 23 resource files; set `-ClangResourceVersion` for a different installed version only after checking compatibility.
- The ReXGlue checkout and dependencies, including the project patch.
- A locally built or obtained `extract-xiso.exe` from [XboxDev/extract-xiso](https://github.com/XboxDev/extract-xiso), placed in `Installer/tools/`.
- Your own supported game ISO or extracted `GameData` for game builds.

The generalized packaging script has been syntax-checked. Its portable path changes have not been validated by a fresh SDK build on another PC.

## Prepare ReXGlue

From the repository root:

```powershell
git clone --recursive https://github.com/rexglue/rexglue-sdk.git ReXGlue
git -C ReXGlue checkout c94f5ebdcb3c9d1a460ca48e04f9758448f8d518
git -C ReXGlue submodule update --init --recursive
git -C ReXGlue apply --check ../patches/rexglue-local-changes.patch
git -C ReXGlue apply ../patches/rexglue-local-changes.patch
```

This commit identifies the development checkout. The patch records project-specific changes, including graphics compatibility, draw scaling, diagnostics and window restoration. Follow [ReXGlue's build documentation](https://github.com/rexglue/rexglue-sdk/wiki/Getting-Started) for its toolchain/dependency prerequisites. Do not assume a newer SDK is compatible with the recorded patch.

Build and install the base SDK in a configured Windows C++ developer terminal:

```powershell
cmake -S ReXGlue --preset win-amd64
cmake --build ReXGlue/out/build/win-amd64 --config Release --parallel 2
cmake --install ReXGlue/out/build/win-amd64 --config Release
```

Build the separate Vulkan renderer/runtime used by the package:

```powershell
cmake -S ReXGlue -B ReXGlue/out/build/win-amd64-vulkan -G "Ninja Multi-Config" `
  -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ `
  -DREXGLUE_USE_VULKAN=ON -DREXGLUE_USE_D3D12=OFF `
  -DREXGLUE_BUILD_TESTS=OFF -DREXGLUE_ENABLE_PERF_COUNTERS=ON `
  -DREXGLUE_OUTPUT_DIRECTORY="$PWD/ReXGlue/out/win-amd64-vulkan"
cmake --build ReXGlue/out/build/win-amd64-vulkan --config Release --parallel 2
```

The packager uses the installed base SDK layout and code generator, then replaces the runtime/GPU libraries and their import libraries with the Vulkan build. Keep the DLLs and `.lib` files from the same build together.

## Build the launcher

```powershell
dotnet publish Launcher/FifaStreetLauncher/FifaStreetLauncher.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/launcher
```

## Package the installer

The source images and icon are tracked. Compiled SDK tools, `extract-xiso.exe`, compiler binaries and `bundle.zip` remain local and excluded from Git.

```powershell
./Installer/BuildSetup.ps1 `
  -Output "$PWD/dist/FifaStreetSetup-Windows-x64.exe" `
  -LLVMRoot "C:/Program Files/LLVM" `
  -CMakeRoot "C:/Program Files/CMake" `
  -NinjaPath "C:/Tools/ninja.exe" `
  -ClangResourceVersion "23"
```

The script creates the public-tool bundle, publishes the launcher and publishes the self-contained installer. It rejects game archive/executable extensions in the bundle. Review exact-version third-party notices before distributing a public release.

The installer embeds `credit-patch.json.gz` and the managed LZX decoder. The recipe is the small difference needed for the two credit menus; it is not a copy of either menu archive. No original game font or modified BIG archive is included.

## Compile from extracted game data

```powershell
$env:REXSDK_DIR = "$PWD/ReXGlue"
./Installer/BuildFifaStreet.ps1 `
  -GameData "C:/Private/FIFA Street/GameData" `
  -Output "C:/Private/FIFA Street/Game" -Jobs 2
```

This builds the game binaries from local data. The graphical installer additionally applies the menu credits before recompilation. Do not commit the extracted files or generated guest source.

## Verify before publishing

Use [STATUS.md](STATUS.md) as the validation record. Check the full ISO-to-install workflow, menu credits, requested internal resolution, window restoration, controller/audio/save behavior and shortcut creation. Preserve build logs when a check fails.

Upload the installer as a Release asset; commit source, documentation, screenshots and notices. See [GitHub presentation](GITHUB-PRESENTATION.md).
