param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Escolha uma pasta de destino nova.' }
New-Item -ItemType Directory -Path $Destination | Out-Null

# Only authoring files and public tools belong in the installer package.
foreach ($file in @('BuildFifaStreet.ps1', 'BuildIntegrity.ps1', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $Destination
}
$template = Join-Path $Destination 'recomp-template'
New-Item -ItemType Directory -Path $template | Out-Null
foreach ($file in @('CMakeLists.txt', 'CMakePresets.json', 'fifastreet_manifest.toml')) {
    Copy-Item -LiteralPath (Join-Path $root "recomp-template\$file") -Destination $template
}
foreach ($folder in @('config', 'src')) {
    New-Item -ItemType Directory -Path (Join-Path $template $folder) | Out-Null
}
foreach ($toml in @('fifadllzf_overrides.toml', 'footballcompeng_overrides.toml')) {
    Copy-Item -LiteralPath (Join-Path $root "recomp-template\config\$toml") -Destination (Join-Path $template 'config')
}
foreach ($file in @('main.cpp', 'fifastreet_app.h', 'stubs.cpp')) {
    Copy-Item -LiteralPath (Join-Path $root "recomp-template\src\$file") -Destination (Join-Path $template 'src')
}
# Do not bundle the game artwork used by the development resource file.
[IO.File]::WriteAllText((Join-Path $template 'src\fifastreet.rc'), '// No bundled game artwork.')
New-Item -ItemType Directory -Path (Join-Path $template 'generated') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'recomp-template\generated\rexglue.cmake') -Destination (Join-Path $template 'generated')
New-Item -ItemType Directory -Path (Join-Path $Destination 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\extract-xiso.exe') -Destination (Join-Path $Destination 'tools')
New-Item -ItemType Directory -Path (Join-Path $Destination 'payload\Game') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'payload\Game\fifastreet.toml') -Destination (Join-Path $Destination 'payload\Game')

dotnet publish (Join-Path $root 'FifaStreetSetupTool\FifaStreetSetupTool.csproj') -c Release --self-contained false -o (Join-Path $Destination 'FifaStreetSetupTool')
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar SetupTool: $LASTEXITCODE" }
$forbidden = Get-ChildItem -LiteralPath $Destination -Recurse -File | Where-Object {
    $_.Name -match '\.(iso|xex|big|bh)$|\.xex\.dll$|^fifastreet\.exe$|^fifastreet_fifadllzf_xex\.dll$|^fifastreet_recomp' }
if ($forbidden) { throw 'O pacote contem ficheiros privados ou recompilados.' }
Write-Host "Installer criado: $Destination"
