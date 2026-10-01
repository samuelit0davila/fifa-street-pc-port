param(
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$LLVMRoot = 'C:\Program Files\LLVM',
    [string]$CMakeRoot = 'C:\Program Files\CMake',
    [string]$NinjaPath = '',
    [string]$ClangResourceVersion = '23'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (!$NinjaPath) { $NinjaPath = (Get-Command ninja.exe -ErrorAction Stop).Source }
$projectRoot = Split-Path $root -Parent
$stage = Join-Path $env:TEMP ('FifaStreetPackage_' + [guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($name in @('BuildFifaStreet.ps1', 'README.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage }
$template = Join-Path $stage 'recomp-template'
foreach ($name in @('', 'config', 'src', 'generated')) { New-Item -ItemType Directory -Path (Join-Path $template $name) -Force | Out-Null }
foreach ($name in @('CMakeLists.txt','CMakePresets.json','fifastreet_manifest.toml','config\fifadllzf_overrides.toml','src\main.cpp','src\fifastreet_app.h','src\stubs.cpp','generated\rexglue.cmake')) {
    Copy-Item -LiteralPath (Join-Path $root "recomp-template\$name") -Destination (Join-Path $template $name)
}
Copy-Item -LiteralPath (Join-Path $root 'recomp-template\src\fifastreet.ico') -Destination (Join-Path $template 'src\fifastreet.ico')
[IO.File]::WriteAllText((Join-Path $template 'src\fifastreet.rc'), '1 ICON "fifastreet.ico"', $utf8)
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\extract-xiso.exe') -Destination (Join-Path $stage 'tools')
New-Item -ItemType Directory -Path (Join-Path $stage 'payload\Game') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'payload\Game\fifastreet.toml') -Destination (Join-Path $stage 'payload\Game')
# Launcher artwork is tracked with the launcher source.
dotnet publish (Join-Path $projectRoot 'Launcher\FifaStreetLauncher\FifaStreetLauncher.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $stage 'payload')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o launcher.' }
Get-ChildItem -LiteralPath (Join-Path $stage 'payload') -Filter '*.pdb' | Remove-Item

# Use the installed public SDK layout, with the current runtime and code generator.
Copy-Item -LiteralPath (Join-Path $projectRoot 'ReXGlue\out\install\win-amd64') -Destination (Join-Path $stage 'sdk') -Recurse
$current = Join-Path $projectRoot 'ReXGlue\out\win-amd64'
if (Test-Path -LiteralPath (Join-Path $current 'Release\rexglue.exe')) {
    $current = Join-Path $current 'Release'
}
foreach ($name in @('rexglue.exe','rexruntime.dll','rexgpu-xenos.dll','TracyClient.dll')) { Copy-Item -LiteralPath (Join-Path $current $name) -Destination (Join-Path $stage 'sdk\bin') -Force }
Get-ChildItem -LiteralPath $current -Filter '*.lib' | Copy-Item -Destination (Join-Path $stage 'sdk\lib') -Force
# Keep the Vulkan runtime and import libraries together. The D3D12 build
# does not contain the renderer validated with this game's resolution fixes.
$vulkan = Join-Path $projectRoot 'ReXGlue\out\win-amd64-vulkan\Release'
foreach ($name in @('rexruntime.dll','rexgpu-xenos.dll')) {
    Copy-Item -LiteralPath (Join-Path $vulkan $name) -Destination (Join-Path $stage 'sdk\bin') -Force
}
foreach ($name in @('rexruntime.lib','rexgpu-xenos.lib')) {
    Copy-Item -LiteralPath (Join-Path $vulkan $name) -Destination (Join-Path $stage 'sdk\lib') -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'ReXGlue\include\rex') -Destination (Join-Path $stage 'sdk\include') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $stage 'sdk\bin') -Filter 'rexglue.before*' | Remove-Item
New-Item -ItemType Directory -Path (Join-Path $stage 'compiler\bin') -Force | Out-Null
foreach ($name in @('clang.exe','lld-link.exe','llvm-rc.exe','llvm-mt.exe')) { Copy-Item -LiteralPath (Join-Path $LLVMRoot "bin\$name") -Destination (Join-Path $stage 'compiler\bin') }
New-Item -ItemType Directory -Path (Join-Path $stage 'compiler\lib\clang') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $LLVMRoot "lib\clang\$ClangResourceVersion") -Destination (Join-Path $stage 'compiler\lib\clang') -Recurse
foreach ($name in @('cmake.exe','cmcldeps.exe')) { Copy-Item -LiteralPath (Join-Path $CMakeRoot "bin\$name") -Destination (Join-Path $stage 'compiler\bin') }
Copy-Item -LiteralPath (Join-Path $CMakeRoot 'share') -Destination (Join-Path $stage 'compiler\share') -Recurse
Copy-Item -LiteralPath $NinjaPath -Destination (Join-Path $stage 'compiler\bin')
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'FifaStreetSetupTool\ThirdParty\MonoGame-LICENSE.txt') -Destination (Join-Path $stage 'licenses\MonoGame-LZX-MS-PL.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'ReXGlue\LICENSE') -Destination (Join-Path $stage 'licenses\ReXGlue.txt')
Copy-Item -LiteralPath (Join-Path $CMakeRoot 'doc\cmake\LICENSE.rst') -Destination (Join-Path $stage 'licenses\CMake.rst')
$thirdPartyRoot = Join-Path $projectRoot 'ReXGlue\thirdparty'
Get-ChildItem -LiteralPath $thirdPartyRoot -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|COPYRIGHT)(\..*)?$' } | ForEach-Object {
    $relative = $_.FullName.Substring($thirdPartyRoot.Length + 1)
    $target = Join-Path (Join-Path $stage 'licenses\ReXGlue-thirdparty') $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}

$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '\.(iso|xex|big|bh)$|\.xex\.dll$|^fifastreet\.exe$|^fifastreet_fifadllzf_xex\.dll$|^fifastreet_recomp' }
if ($forbidden) { throw 'O pacote contem ficheiros do jogo ou codigo recompilado.' }
# Installer artwork is tracked with the installer source.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = Join-Path $root 'FifaStreetSetupTool\bundle.zip'
if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $bundle, [IO.Compression.CompressionLevel]::Optimal, $false)
$publish = Join-Path $stage 'published'
dotnet publish (Join-Path $root 'FifaStreetSetupTool\FifaStreetSetupTool.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar instalador.' }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path (Split-Path $Output -Parent) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publish 'FifaStreetSetup.exe') -Destination $Output -Force
Write-Host "Instalador unico criado: $Output"
Write-Host "Pasta de verificacao: $stage"
