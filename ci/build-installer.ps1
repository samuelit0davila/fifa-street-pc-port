$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$inputs = Join-Path $PWD '.ci-inputs'
New-Item -ItemType Directory -Force -Path $inputs | Out-Null
$archive = Join-Path $inputs 'inputs.zip'
$inputManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-inputs.json') -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot '../Installer/BuildIntegrity.ps1')
if ($inputManifest.version -notmatch '^v[0-9A-Za-z.-]+$' -or $inputManifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid build input integrity manifest.' }
$inputManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-inputs.json') -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot '../Installer/BuildIntegrity.ps1')
if ($inputManifest.version -notmatch '^v[0-9A-Za-z.-]+$' -or $inputManifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid build input integrity manifest.' }
$url = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/validated-build-inputs/$($inputManifest.version)/inputs.zip"
Invoke-WebRequest -Uri $url -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -OutFile $archive
if ((Get-InputSha256 $archive) -ne $inputManifest.sha256) { throw 'Validated build input checksum mismatch.' }
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
    $backendManifest = Get-Content Installer/backend-manifest.json -Raw | ConvertFrom-Json
    $directory = Join-Path $PWD ("ReXGlue/" + $backendManifest.backends.$backend.source)
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
"$(Get-InputSha256 'dist/FifaStreet-Setup.exe')  FifaStreet-Setup.exe" |
    Set-Content -Encoding ascii 'dist/FifaStreet-Setup.exe.sha256'
$packageUrl = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/packages/generic/installer-builds/$env:CI_COMMIT_SHA"
foreach ($file in @('FifaStreet-Setup.exe', 'FifaStreet-Setup.exe.sha256')) {
    Invoke-RestMethod -Method Put -Uri "$packageUrl/$file" -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } -InFile (Join-Path $PWD "dist/$file") -ContentType 'application/octet-stream' | Out-Null
}
