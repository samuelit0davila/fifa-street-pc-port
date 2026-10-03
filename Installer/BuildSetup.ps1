param(
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$LLVMRoot = 'C:\Program Files\LLVM',
    [string]$CMakeRoot = 'C:\Program Files\CMake',
    [string]$NinjaPath = (Get-Command ninja.exe -ErrorAction Stop).Source,
    [string]$ReXGlueRoot,
    [string]$BackendManifest,
    [string]$ClangResourceVersion
)
$ErrorActionPreference = 'Stop'
if (-not $ClangResourceVersion) {
    $ClangResourceVersion = (Get-ChildItem -LiteralPath (Join-Path $LLVMRoot 'lib\clang') -Directory |
        Sort-Object { if ($_.Name.Contains('.')) { [version]$_.Name } else { [version]($_.Name + '.0') } } -Descending | Select-Object -First 1).Name
}
if (-not $ClangResourceVersion) { throw 'Clang resource directory was not found.' }
$root = $PSScriptRoot
$projectRoot = Split-Path $root -Parent
. (Join-Path $root "BuildIntegrity.ps1")
if (!$ReXGlueRoot) { $ReXGlueRoot = Join-Path $projectRoot "ReXGlue" }
if (!$BackendManifest) { $BackendManifest = Join-Path $root "backend-manifest.json" }
$backendInputs = Get-Content -LiteralPath $BackendManifest -Raw | ConvertFrom-Json
$stage = Join-Path $env:TEMP ('FifaStreetPackage_' + [guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in @('BuildFifaStreet.ps1', 'BuildIntegrity.ps1', 'README.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage }
$template = Join-Path $stage 'recomp-template'
foreach ($name in @('', 'config', 'src', 'generated')) { New-Item -ItemType Directory -Path (Join-Path $template $name) -Force | Out-Null }
foreach ($name in @('CMakeLists.txt','CMakePresets.json','fifastreet_manifest.toml','config\fifadllzf_overrides.toml','config\footballcompeng_overrides.toml','src\main.cpp','src\fifastreet_app.h','src\stubs.cpp','generated\rexglue.cmake')) {
    Copy-Item -LiteralPath (Join-Path $root "recomp-template\$name") -Destination (Join-Path $template $name)
}
Copy-Item -LiteralPath (Join-Path $root 'recomp-template\src\fifastreet.ico') -Destination (Join-Path $template 'src\fifastreet.ico')
[IO.File]::WriteAllText((Join-Path $template 'src\fifastreet.rc'), '1 ICON "fifastreet.ico"', $utf8)
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\extract-xiso.exe') -Destination (Join-Path $stage 'tools')
New-Item -ItemType Directory -Path (Join-Path $stage 'payload\Game') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'payload\Game\fifastreet.toml') -Destination (Join-Path $stage 'payload\Game')
dotnet publish (Join-Path $projectRoot 'Launcher\FifaStreetLauncher\FifaStreetLauncher.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RuntimeFrameworkVersion=8.0.31 -o (Join-Path $stage 'payload')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o launcher.' }
Get-ChildItem -LiteralPath (Join-Path $stage 'payload') -Filter '*.pdb' | Remove-Item

# Use the installed public SDK layout, with the current runtime and code generator.
Copy-Item -LiteralPath (Join-Path $ReXGlueRoot 'out\install\win-amd64') -Destination (Join-Path $stage 'sdk') -Recurse
$current = Join-Path $ReXGlueRoot 'out\win-amd64'
$backendRuntime = Join-Path $ReXGlueRoot $backendInputs.backends.Vulkan.source
foreach ($name in @('rexglue.exe','rexruntime.dll','rexgpu-xenos.dll','TracyClient.dll')) { Copy-Item -LiteralPath (Join-Path $current $name) -Destination (Join-Path $stage 'sdk\bin') -Force }
Get-ChildItem -LiteralPath $current -Filter '*.lib' | Copy-Item -Destination (Join-Path $stage 'sdk\lib') -Force
foreach ($name in @('rexruntime.dll','rexgpu-xenos.dll')) { Copy-Item -LiteralPath (Join-Path $backendRuntime $name) -Destination (Join-Path $stage 'sdk\bin') -Force }
# Package both graphics backends validated with FIFA Street.
$backendRoot = Join-Path $stage 'sdk\backends'
$d3d12Package = Join-Path $backendRoot 'D3D12'
$vulkanPackage = Join-Path $backendRoot 'Vulkan'

New-Item -ItemType Directory -Path $d3d12Package -Force | Out-Null
New-Item -ItemType Directory -Path $vulkanPackage -Force | Out-Null

$vulkan = Join-Path $ReXGlueRoot $backendInputs.backends.Vulkan.source
$expectedBackends = @{}
foreach ($backendName in @('D3D12', 'Vulkan')) {
    $inputBackend = $backendInputs.backends.$backendName
    if (!$inputBackend -or $inputBackend.runtimeHash -notmatch '^[a-fA-F0-9]{64}$' -or $inputBackend.gpuHash -notmatch '^[a-fA-F0-9]{64}$') { throw "Invalid backend integrity manifest: $backendName" }
    $expectedBackends[$backendName] = @{
        Source = Join-Path $ReXGlueRoot $inputBackend.source
        Destination = Join-Path $backendRoot $backendName
        RuntimeHash = $inputBackend.runtimeHash
        GpuHash = $inputBackend.gpuHash
    }
}

foreach ($backendName in @('D3D12', 'Vulkan')) {
    $backend = $expectedBackends[$backendName]

    foreach ($name in @('rexruntime.dll', 'rexgpu-xenos.dll')) {
        $source = Join-Path $backend.Source $name

        if (!(Test-Path -LiteralPath $source)) {
            throw "$backendName backend is missing: $source"
        }

        Copy-Item -LiteralPath $source -Destination $backend.Destination -Force
    }

    $runtimeFile = Join-Path $backend.Destination 'rexruntime.dll'
    $gpuFile = Join-Path $backend.Destination 'rexgpu-xenos.dll'

    $runtimeHash = (Get-InputSha256 $runtimeFile)
    $gpuHash = (Get-InputSha256 $gpuFile)

    if ($runtimeHash -ne $backend.RuntimeHash) {
        throw "$backendName rexruntime.dll hash mismatch. Expected $($backend.RuntimeHash), got $runtimeHash"
    }

    if ($gpuHash -ne $backend.GpuHash) {
        throw "$backendName rexgpu-xenos.dll hash mismatch. Expected $($backend.GpuHash), got $gpuHash"
    }
}

# Keep the Vulkan import libraries in the SDK used to compile the game.
# The runtime DLLs themselves are installed from sdk\backends.
foreach ($name in @('rexruntime.lib','rexgpu-xenos.lib')) {
    Copy-Item -LiteralPath (Join-Path $vulkan $name) -Destination (Join-Path $stage 'sdk\lib') -Force
}
Copy-Item -LiteralPath (Join-Path $ReXGlueRoot 'include\rex') -Destination (Join-Path $stage 'sdk\include') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $stage 'sdk\bin') -Filter 'rexglue.before*' | Remove-Item
New-Item -ItemType Directory -Path (Join-Path $stage 'compiler\bin') -Force | Out-Null
foreach ($name in @('clang.exe','clang++.exe','lld-link.exe','llvm-rc.exe','llvm-mt.exe')) { Copy-Item -LiteralPath (Join-Path $LLVMRoot "bin\$name") -Destination (Join-Path $stage 'compiler\bin') }
New-Item -ItemType Directory -Path (Join-Path $stage 'compiler\lib\clang') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $LLVMRoot "lib\clang\$ClangResourceVersion") -Destination (Join-Path $stage 'compiler\lib\clang') -Recurse
foreach ($name in @('cmake.exe','cmcldeps.exe')) { Copy-Item -LiteralPath (Join-Path $CMakeRoot "bin\$name") -Destination (Join-Path $stage 'compiler\bin') }
Copy-Item -LiteralPath (Join-Path $CMakeRoot 'share') -Destination (Join-Path $stage 'compiler\share') -Recurse
Copy-Item -LiteralPath $NinjaPath -Destination (Join-Path $stage 'compiler\bin')
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\bundled-tools') -Destination (Join-Path $stage 'licenses\bundled-tools') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'FifaStreetSetupTool\ThirdParty\MonoGame-LICENSE.txt') -Destination (Join-Path $stage 'licenses\MonoGame-LZX-MS-PL.txt')
Copy-Item -LiteralPath (Join-Path $ReXGlueRoot 'LICENSE') -Destination (Join-Path $stage 'licenses\ReXGlue.txt')
Copy-Item -LiteralPath (Join-Path $CMakeRoot 'doc\cmake\LICENSE.rst') -Destination (Join-Path $stage 'licenses\CMake.rst')
$thirdPartyRoot = Join-Path $ReXGlueRoot 'thirdparty'
Get-ChildItem -LiteralPath $thirdPartyRoot -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|COPYRIGHT)(\..*)?$' } | ForEach-Object {
    $relative = $_.FullName.Substring($thirdPartyRoot.Length + 1)
    $target = Join-Path (Join-Path $stage 'licenses\ReXGlue-thirdparty') $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}

$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '\.(iso|xex|big|bh)$|\.xex\.dll$|^fifastreet\.exe$|^fifastreet_.*_xex\.dll$|^fifastreet_recomp' }
if ($forbidden) { throw 'O pacote contem ficheiros do jogo ou codigo recompilado.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = Join-Path $root 'FifaStreetSetupTool\bundle.zip'
if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $bundle, [IO.Compression.CompressionLevel]::Optimal, $false)
$publish = Join-Path $stage 'published'
dotnet publish (Join-Path $root 'FifaStreetSetupTool\FifaStreetSetupTool.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RuntimeFrameworkVersion=8.0.31 -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar instalador.' }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path (Split-Path $Output -Parent) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publish 'FifaStreetSetup.exe') -Destination $Output -Force
Write-Host "Instalador unico criado: $Output"
Write-Host "Pasta de verificacao: $stage"
