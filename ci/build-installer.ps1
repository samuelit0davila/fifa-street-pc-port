$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$env:RUNNER_TEMP = $env:TEMP
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ build tools are required.' }
& (Join-Path $vs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64
$env:PATH = "C:\Program Files\LLVM\bin;C:\Program Files\CMake\bin;$env:PATH"
$ErrorActionPreference = 'Stop'
if (-not (Get-Command cmake.exe -ErrorAction SilentlyContinue)) {
  choco install cmake --installargs 'ADD_CMAKE_TO_PATH=System' -y
}
if (-not (Get-Command ninja.exe -ErrorAction SilentlyContinue)) {
  choco install ninja -y
}
if (-not (Test-Path 'C:\Program Files\LLVM\bin\clang.exe')) {
  choco install llvm -y
}
cmake --version
ninja --version
& 'C:\Program Files\LLVM\bin\clang.exe' --version
dotnet --info


$ErrorActionPreference = 'Stop'
git -c core.autocrlf=false clone --recursive https://github.com/rexglue/rexglue-sdk.git ReXGlue
if ($LASTEXITCODE -ne 0) { throw 'ReXGlue clone failed.' }
git -C ReXGlue config core.autocrlf false
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure ReXGlue line endings.' }
git -C ReXGlue checkout c94f5ebdcb3c9d1a460ca48e04f9758448f8d518
if ($LASTEXITCODE -ne 0) { throw 'Pinned ReXGlue checkout failed.' }
git -C ReXGlue -c core.autocrlf=false submodule update --init --recursive
if ($LASTEXITCODE -ne 0) { throw 'ReXGlue submodule update failed.' }
# The project checkout can convert the patch itself to CRLF on Windows.
$patch = Join-Path $PWD 'patches/rexglue-local-changes.patch'
$patchText = [System.IO.File]::ReadAllText($patch).Replace("`r`n", "`n")
[System.IO.File]::WriteAllText($patch, $patchText, [System.Text.UTF8Encoding]::new($false))
git -C ReXGlue apply --check ../patches/rexglue-local-changes.patch
if ($LASTEXITCODE -ne 0) { throw 'ReXGlue patch validation failed.' }
git -C ReXGlue apply ../patches/rexglue-local-changes.patch
if ($LASTEXITCODE -ne 0) { throw 'ReXGlue patch application failed.' }


$ErrorActionPreference = 'Stop'
cmake -S ReXGlue --preset win-amd64
cmake --build ReXGlue/out/build/win-amd64 --config Release --parallel 2
cmake --install ReXGlue/out/build/win-amd64 --config Release


$ErrorActionPreference = 'Stop'
$out = (Resolve-Path '.').Path.Replace('\','/')

cmake -S ReXGlue -B ReXGlue/out/build/win-amd64-vulkan -G "Ninja Multi-Config" `
  -DCMAKE_C_COMPILER=clang `
  -DCMAKE_CXX_COMPILER=clang++ `
  "-DCMAKE_C_FLAGS=-mssse3" `
  "-DCMAKE_CXX_FLAGS=-mssse3" `
  -DREXGLUE_USE_VULKAN=ON `
  -DREXGLUE_USE_D3D12=OFF `
  -DREXGLUE_BUILD_TESTS=OFF `
  -DREXGLUE_ENABLE_PERF_COUNTERS=ON `
  "-DREXGLUE_OUTPUT_DIRECTORY=$out/ReXGlue/out/win-amd64-vulkan"

cmake --build ReXGlue/out/build/win-amd64-vulkan --config Release --parallel 2


$ErrorActionPreference = 'Stop'
$out = (Resolve-Path '.').Path.Replace('\','/')

cmake -S ReXGlue -B ReXGlue/out/build/win-amd64-d3d12 -G "Ninja Multi-Config" `
  -DCMAKE_C_COMPILER=clang `
  -DCMAKE_CXX_COMPILER=clang++ `
  "-DCMAKE_C_FLAGS=-mssse3" `
  "-DCMAKE_CXX_FLAGS=-mssse3" `
  -DREXGLUE_USE_VULKAN=OFF `
  -DREXGLUE_USE_D3D12=ON `
  -DREXGLUE_BUILD_TESTS=OFF `
  -DREXGLUE_ENABLE_PERF_COUNTERS=ON `
  "-DREXGLUE_OUTPUT_DIRECTORY=$out/ReXGlue/out/win-amd64-d3d12"

cmake --build ReXGlue/out/build/win-amd64-d3d12 --config Release --parallel 2


$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path Installer/tools | Out-Null
$zip = Join-Path $env:RUNNER_TEMP 'extract-xiso.zip'
$dir = Join-Path $env:RUNNER_TEMP 'extract-xiso'
Invoke-WebRequest `
  -Uri 'https://github.com/XboxDev/extract-xiso/releases/download/build-202609111233/extract-xiso-Win64_Release.zip' `
  -OutFile $zip
Expand-Archive -LiteralPath $zip -DestinationPath $dir -Force
$exe = Get-ChildItem -LiteralPath $dir -Recurse -Filter 'extract-xiso.exe' | Select-Object -First 1
if (-not $exe) { throw 'extract-xiso.exe was not found in the downloaded archive.' }
Copy-Item -LiteralPath $exe.FullName -Destination 'Installer/tools/extract-xiso.exe' -Force


# Package the exact validated runtime pairs from the migrated release.
foreach ($backend in @('D3D12', 'Vulkan')) {
    $directory = Join-Path $PWD "ReXGlue/out/win-amd64-$($backend.ToLower())/Release"
    foreach ($file in @('rexruntime.dll', 'rexgpu-xenos.dll')) {
        $url = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/validated-backends/v0.3.0/$backend-$file"
        Invoke-WebRequest -Uri $url -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -OutFile (Join-Path $directory $file)
    }
}
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path dist | Out-Null
$clangRoot = 'C:\Program Files\LLVM\lib\clang'
$clangVersion = (Get-ChildItem -LiteralPath $clangRoot -Directory |
  Sort-Object { [version]$_.Name } -Descending |
  Select-Object -First 1).Name
if (-not $clangVersion) { throw 'Unable to determine the installed Clang resource version.' }
$ninja = (Get-Command ninja.exe -ErrorAction Stop).Source
./Installer/BuildSetup.ps1 `
  -Output "$PWD/dist/FifaStreet-Setup.exe" `
  -LLVMRoot 'C:\Program Files\LLVM' `
  -CMakeRoot 'C:\Program Files\CMake' `
  -NinjaPath $ninja `
  -ClangResourceVersion $clangVersion
if (-not (Test-Path 'dist/FifaStreet-Setup.exe')) {
  throw 'FifaStreet-Setup.exe was not created.'
}
Get-FileHash 'dist/FifaStreet-Setup.exe' -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  FifaStreet-Setup.exe" } |
  Set-Content -Encoding ascii 'dist/FifaStreet-Setup.exe.sha256'


$packageUrl = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/installer-builds/$env:CI_COMMIT_SHA"
foreach ($file in @('FifaStreet-Setup.exe', 'FifaStreet-Setup.exe.sha256')) {
    Invoke-RestMethod -Method Put -Uri "$packageUrl/$file" -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -InFile (Join-Path $PWD "dist/$file") -ContentType 'application/octet-stream' | Out-Null
}
