param(
    [Parameter(Mandatory = $true)]
    [string]$GameData,

    [Parameter(Mandatory = $true)]
    [string]$Output,

    [string]$Workspace,

    [ValidateRange(1, 64)]
    [int]$Jobs = 2
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

function Invoke-BuildTool {
    param([string]$Tool, [string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    $nativeLog = $logPath + '.native'
    $writer = [IO.StreamWriter]::new($nativeLog, $true, [Text.UTF8Encoding]::new($false))
    try {
        $writer.WriteLine("$Tool " + ($Arguments -join ' '))
        & $Tool @Arguments 2>&1 | ForEach-Object {
            $line = if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception.Message } else { [string]$_ }
            $writer.WriteLine($line)
            if ($line -match '^\[\d+|^\s*(start|phase|done)\s|FAILED:|fatal error:|error:|^Failed:|Codegen summary:|^Done in') {
                Write-Host $line
            }
        }
        $exitCode = $LASTEXITCODE
    }
    finally { $writer.Dispose() }
    if ($exitCode -ne 0) { throw "$Tool failed with code $exitCode. Full log: $nativeLog" }
}

$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$template = Join-Path $installerRoot "recomp-template"
$rexGlueSource = if ($env:REXSDK_DIR) { [IO.Path]::GetFullPath($env:REXSDK_DIR) } else {
    "C:\Users\Samuel M\Desktop\FIFASTREET - 2012\ReXGlue"
}
$bundledSdk = Join-Path $installerRoot 'sdk'
$bundledTools = Join-Path $installerRoot 'compiler'
$useBundledSdk = Test-Path -LiteralPath (Join-Path $bundledSdk 'bin\rexglue.exe')
$llvmBin = if (Test-Path -LiteralPath $bundledTools) { Join-Path $bundledTools 'bin' } else { "C:\Program Files\LLVM\bin" }

$clang = Join-Path $llvmBin "clang.exe"
$clangxx = Join-Path $llvmBin "clang++.exe"
if (!(Test-Path $clang)) { throw "clang.exe not found at $clang" }
if (!(Test-Path $clangxx)) { throw "clang++.exe not found at $clangxx" }

$env:PATH = "$llvmBin;$env:PATH"
if ($useBundledSdk) { $env:PATH = "$(Join-Path $bundledSdk 'bin');$env:PATH" }

$shortTempRoot = if ($env:FIFA_BUILD_ROOT) { $env:FIFA_BUILD_ROOT } else { "C:\FSTRB" }
if (!(Test-Path $shortTempRoot)) { New-Item -ItemType Directory -Path $shortTempRoot -Force | Out-Null }

$resume = ![string]::IsNullOrWhiteSpace($Workspace)
$workRoot = if ($resume) { [IO.Path]::GetFullPath($Workspace) } else {
    Join-Path $shortTempRoot ([guid]::NewGuid().ToString("N").Substring(0, 8))
}
$workProject = Join-Path $workRoot "FifaStreetRex"
if ($resume -and !(Test-Path -LiteralPath (Join-Path $workProject 'CMakeLists.txt'))) { throw "Invalid build workspace: $workRoot" }
$GameData = [IO.Path]::GetFullPath($GameData)
$Output = [IO.Path]::GetFullPath($Output)

Write-Host ""
Write-Host "======================================"
Write-Host " FIFA Street PC - Recomp Build"
Write-Host "======================================"
Write-Host ""

if (!(Test-Path $GameData)) { throw "GameData does not exist: $GameData" }

$defaultXex = Join-Path $GameData "default.xex"
$fifaDll = Join-Path $GameData "fifadllzf.xex.dll"
$footballCompDll = Join-Path $GameData "dlc\dlc_FootballCompEng\dlc\FootballCompEng\FootballCompEngzf.xex.dll"

if (!(Test-Path $defaultXex)) { throw "default.xex was not found." }
if (!(Test-Path $fifaDll)) { throw "fifadllzf.xex.dll was not found." }
if (!(Test-Path $footballCompDll)) { throw "FootballCompEngzf.xex.dll was not found. This module is required for World Tour." }
if (!(Test-Path $template)) { throw "recomp-template was not found: $template" }
if (!$useBundledSdk -and !(Test-Path $rexGlueSource)) { throw "ReXGlue source was not found: $rexGlueSource" }

Write-Host "[1/6] Preparing temporary workspace..."
New-Item -ItemType Directory -Path $workProject -Force | Out-Null
Get-ChildItem -LiteralPath $template | Where-Object { $_.Name -ne 'fifastreet_manifest.toml' } |
    Copy-Item -Destination $workProject -Recurse -Force

$logPath = Join-Path $workRoot 'build.log'
Write-Host "Workspace: $workRoot"
Write-Host "Log: $logPath"
Write-Host "Full diagnostics: $logPath.native"
Start-Transcript -Path $logPath -Append | Out-Null
trap {
    Write-Host $_.ToString()
    Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
    throw $_
}

Write-Host "[2/6] Preparing manifest..."
$manifestPath = Join-Path $workProject "fifastreet_manifest.toml"
$gameDataUnix = $GameData.Replace("\", "/").Replace('"', '\"')

$manifest = @"
[project]
name = "fifastreet"
sdk_version = "0.10.0"
game_root = "$gameDataUnix"

[entrypoint]
file_path = "$gameDataUnix/default.xex"
out_directory_path = "generated/default"
includes = []

[[modules]]
guest_path = "game:\\fifadllzf.xex.dll"
file_path = "$gameDataUnix/fifadllzf.xex.dll"
out_directory_path = "generated/fifadllzf_xex"
includes = ["config/fifadllzf_overrides.toml"]

[[modules]]
guest_path = "game:\\FootballCompEngzf.xex.dll"
file_path = "$gameDataUnix/dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll"
out_directory_path = "generated/FootballCompEngzf_xex"
includes = []
"@

$manifest = $manifest.Replace("`r`n", "`n")
$manifestChanged = !$resume -or [IO.File]::ReadAllText($manifestPath).Replace("`r`n", "`n").Trim() -ne $manifest.Trim()
if ($manifestChanged) { [System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false)) }

Write-Host ""
Write-Host "Manifest:"
Get-Content $manifestPath
Write-Host ""

Write-Host "[3/6] Cleaning previous generated code..."
$generatedDefault = Join-Path $workProject "generated\default"
$generatedDll = Join-Path $workProject "generated\fifadllzf_xex"
$generatedFootballComp = Join-Path $workProject "generated\FootballCompEngzf_xex"

foreach ($generated in @($generatedDefault,$generatedDll,$generatedFootballComp)) {
    if (!$resume -and (Test-Path $generated)) {
        if (![IO.Path]::GetFullPath($generated).StartsWith($workProject + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe generated path' }
        Remove-Item $generated -Recurse -Force
    }
}

Write-Host "[4/6] Configuring build..."
Push-Location $workProject
try {
    $codegen = if ($useBundledSdk) { Join-Path $bundledSdk 'bin\rexglue.exe' } else { Join-Path $rexGlueSource 'out\win-amd64\rexglue.exe' }
    if (!(Test-Path -LiteralPath $codegen)) { throw "ReXGlue code generator not found: $codegen" }
    if ($manifestChanged -or !(Test-Path -LiteralPath (Join-Path $generatedDefault 'sources.cmake')) -or
        !(Test-Path -LiteralPath (Join-Path $generatedDll 'sources.cmake')) -or
        !(Test-Path -LiteralPath (Join-Path $generatedFootballComp 'sources.cmake'))) {
        Invoke-BuildTool $codegen @('--log-file', (Join-Path $workRoot 'codegen.log'), 'codegen', $manifestPath)
    }

    $sdkArguments = if ($useBundledSdk) { @('-DREXSDK_DIR=', "-DCMAKE_PREFIX_PATH=$bundledSdk") } else { @("-DREXSDK_DIR=$rexGlueSource") }
    Invoke-BuildTool 'cmake' (@('--preset', 'win-amd64-release') + $sdkArguments + @(
        "-DCMAKE_C_COMPILER=$clang",
        "-DCMAKE_CXX_COMPILER=$clangxx", '-DCMAKE_C_FLAGS=-mssse3', '-DCMAKE_CXX_FLAGS=-mssse3')
    )

    Write-Host ""
    Write-Host "[5/6] Compiling FIFA Street..."
    Write-Host ""
    Invoke-BuildTool 'cmake' @('--build', '--preset', 'win-amd64-release', '--parallel', "$Jobs")
}
finally { Pop-Location }

$buildFolder = Join-Path $workProject "out\build\win-amd64-release"
$exe = Join-Path $buildFolder "fifastreet.exe"
$dll = Join-Path $buildFolder "fifastreet_fifadllzf_xex.dll"
$footballCompOut = Join-Path $buildFolder "fifastreet_FootballCompEngzf_xex.dll"

if (!(Test-Path $exe)) { throw "The build finished but fifastreet.exe was not found." }
if (!(Test-Path $dll)) { throw "The build finished but fifastreet_fifadllzf_xex.dll was not found." }
if (!(Test-Path $footballCompOut)) { throw "The build finished but fifastreet_FootballCompEngzf_xex.dll was not found." }

Write-Host ""
Write-Host "[6/6] Installing game binaries..."
New-Item -ItemType Directory -Path $Output -Force | Out-Null
Copy-Item $exe $Output -Force

Get-ChildItem -LiteralPath $buildFolder -Filter '*.dll' -File |
    Where-Object { $_.Name -notin @('rexruntime.dll', 'rexgpu-xenos.dll') } |
    Copy-Item -Destination $Output -Force

$validatedRuntimeRoot = Join-Path $bundledSdk 'bin'
$validatedRuntime = Join-Path $validatedRuntimeRoot 'rexruntime.dll'
$validatedGpu = Join-Path $validatedRuntimeRoot 'rexgpu-xenos.dll'
if (!(Test-Path -LiteralPath $validatedRuntime)) { throw "Validated rexruntime.dll was not found: $validatedRuntime" }
if (!(Test-Path -LiteralPath $validatedGpu)) { throw "Validated rexgpu-xenos.dll was not found: $validatedGpu" }

Copy-Item $validatedRuntime (Join-Path $Output 'rexruntime.dll') -Force
Copy-Item $validatedGpu (Join-Path $Output 'rexgpu-xenos.dll') -Force

Write-Host ""
Write-Host "======================================"
Write-Host " BUILD COMPLETE"
Write-Host "======================================"
Write-Host ""
Write-Host "Created:"
Write-Host "  $(Join-Path $Output 'fifastreet.exe')"
Write-Host "  $(Join-Path $Output 'fifastreet_fifadllzf_xex.dll')"
Write-Host "  $(Join-Path $Output 'fifastreet_FootballCompEngzf_xex.dll')"
Write-Host ""
Write-Host "Temporary workspace:"
Write-Host $workRoot
Stop-Transcript | Out-Null
