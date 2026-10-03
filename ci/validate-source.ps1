$ErrorActionPreference = 'Stop'
$files = Get-ChildItem Installer, ci -Filter '*.ps1' -Recurse
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors) { throw "$($file.Name): $($parseErrors.Message -join '; ')" }
}
Write-Host "PowerShell syntax verified for $($files.Count) source files."
