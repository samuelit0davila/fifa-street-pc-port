$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inputs = Join-Path $PWD '.ci-inputs'
New-Item -ItemType Directory -Force -Path $inputs | Out-Null
$archive = Join-Path $inputs 'inputs.zip'
$url = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/validated-build-inputs/v0.3.0/inputs.zip"
Invoke-WebRequest -Uri $url -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -OutFile $archive
$expected = 'f754c863c879008d3e31b8cb83b3f4070ec65455ea8a718b2625b2f195507b83'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'Validated build input checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $inputs
# Restore the unchanged public SDK and toolchain; no game code or data is included.
New-Item -ItemType Directory -Force -Path ReXGlue/out/install, ReXGlue/out/win-amd64, ReXGlue/include, ReXGlue/thirdparty, Installer/tools | Out-Null
Copy-Item -LiteralPath (Join-Path $inputs 'sdk') -Destination ReXGlue/out/install/win-amd64 -Recurse
Copy-Item -Path (Join-Path $inputs 'sdk/bin/*') -Destination ReXGlue/out/win-amd64
Copy-Item -Path (Join-Path $inputs 'sdk/lib/*.lib') -Destination ReXGlue/out/win-amd64
Copy-Item -LiteralPath (Join-Path $inputs 'sdk/include/rex') -Destination ReXGlue/include -Recurse
Copy-Item -LiteralPath (Join-Path $inputs 'licenses/ReXGlue.txt') -Destination ReXGlue/LICENSE
Copy-Item -Path (Join-Path $inputs 'licenses/ReXGlue-thirdparty/*') -Destination ReXGlue/thirdparty -Recurse
Copy-Item -LiteralPath (Join-Path $inputs 'tools/extract-xiso.exe') -Destination Installer/tools/extract-xiso.exe
foreach ($backend in @('D3D12', 'Vulkan')) {
    $directory = Join-Path $PWD "ReXGlue/out/win-amd64-$($backend.ToLower())/Release"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Copy-Item -Path (Join-Path $inputs "sdk/backends/$backend/*") -Destination $directory
}
foreach ($name in @('rexruntime.lib', 'rexgpu-xenos.lib')) {
    Copy-Item -LiteralPath (Join-Path $inputs "sdk/lib/$name") -Destination ReXGlue/out/win-amd64-vulkan/Release
}
$compiler = Join-Path $inputs 'compiler'
New-Item -ItemType Directory -Force -Path (Join-Path $compiler 'doc/cmake') | Out-Null
Copy-Item -LiteralPath (Join-Path $inputs 'licenses/CMake.rst') -Destination (Join-Path $compiler 'doc/cmake/LICENSE.rst')
$env:PATH = "$(Join-Path $compiler 'bin');$env:PATH"
New-Item -ItemType Directory -Force -Path dist | Out-Null
./Installer/BuildSetup.ps1 -Output "$PWD/dist/FifaStreet-Setup.exe" -LLVMRoot $compiler -CMakeRoot $compiler -NinjaPath (Join-Path $compiler 'bin/ninja.exe') -ClangResourceVersion '23'
if (-not (Test-Path -LiteralPath 'dist/FifaStreet-Setup.exe')) { throw 'Installer was not created.' }
Get-FileHash -LiteralPath 'dist/FifaStreet-Setup.exe' -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLower())  FifaStreet-Setup.exe" } |
    Set-Content -Encoding ascii 'dist/FifaStreet-Setup.exe.sha256'
$packageUrl = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/installer-builds/$env:CI_COMMIT_SHA"
foreach ($file in @('FifaStreet-Setup.exe', 'FifaStreet-Setup.exe.sha256')) {
    Invoke-RestMethod -Method Put -Uri "$packageUrl/$file" -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -InFile (Join-Path $PWD "dist/$file") -ContentType 'application/octet-stream' | Out-Null
}
