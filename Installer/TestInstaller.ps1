param([Parameter(Mandatory = $true)][string]$Package)
$ErrorActionPreference = 'Stop'
foreach ($script in @('BuildFifaStreet.ps1', 'PackageInstaller.ps1')) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $script), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw ($errors.Message -join "`n") }
}
foreach ($required in @('BuildFifaStreet.ps1', 'recomp-template\generated\rexglue.cmake',
    'tools\extract-xiso.exe', 'FifaStreetSetupTool\FifaStreetSetupTool.exe', 'payload\Game\fifastreet.toml')) {
    if (!(Test-Path -LiteralPath (Join-Path $Package $required))) { throw "Missing package file: $required" }
}
$privateFiles = Get-ChildItem -LiteralPath $Package -Recurse -File | Where-Object {
    $_.Name -match '\.(iso|xex|big|bh|ico)$|\.xex\.dll$|^fifastreet\.exe$|^fifastreet_fifadllzf_xex\.dll$|^fifastreet_recomp' }
if ($privateFiles) { throw 'Private game files found in package' }
& (Join-Path $Package 'FifaStreetSetupTool\FifaStreetSetupTool.exe') (Join-Path $Package 'missing.iso') (Join-Path $Package 'unused install path')
if ($LASTEXITCODE -ne 3) { throw 'Missing ISO validation failed' }
Write-Host 'Installer checks passed.'

# Verify the actual native-command wrapper under Windows PowerShell 5.1:
# stderr warnings must not fail a successful tool, nonzero exit codes must fail.
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'BuildFifaStreet.ps1'), [ref]$tokens, [ref]$errors)
$wrapper = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-BuildTool'
}, $true)
if (!$wrapper) { throw 'Native build wrapper not found' }
. ([scriptblock]::Create($wrapper.Extent.Text))
$logPath = Join-Path $env:TEMP ('FifaStreetWrapperTest_' + [guid]::NewGuid().ToString('N') + '.log')
Invoke-BuildTool 'cmd.exe' @('/c', 'echo Test diagnostic 1>&2 & exit /b 0')
$failedAsExpected = $false
try { Invoke-BuildTool 'cmd.exe' @('/c', 'exit /b 7') }
catch { $failedAsExpected = $_.Exception.Message -match 'codigo 7' }
if (!$failedAsExpected) { throw 'Nonzero tool exit code was not preserved' }
if ([IO.File]::ReadAllText($logPath + '.native') -notmatch 'Test diagnostic') { throw 'Native stderr was not logged' }
Remove-Item -LiteralPath ($logPath + '.native') -Force
Write-Host 'Native build exit-code checks passed.'
