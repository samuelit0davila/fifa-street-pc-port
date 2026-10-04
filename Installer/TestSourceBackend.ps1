$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallSourceBackend.ps1')
. (Join-Path $PSScriptRoot 'BuildIntegrity.ps1')
$testRoot = Join-Path $env:TEMP ('FifaSourceBackendTest_' + [guid]::NewGuid().ToString('N'))
$runtime = Join-Path $testRoot 'runtime'
$output = Join-Path $testRoot 'output'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $runtime 'rexruntime.dll'), 'local runtime')
$rejected = $false
try { Install-SourceBackend -RuntimeDirectory $runtime -Output $output } catch { $rejected = $true }
if (!$rejected -or (Test-Path $output)) { throw 'An incomplete backend must fail before installation.' }
foreach ($name in @('rexgpu-xenos.dll', 'TracyClient.dll')) {
    [IO.File]::WriteAllText((Join-Path $runtime $name), $name)
}
Install-SourceBackend -RuntimeDirectory $runtime -Output $output
foreach ($name in @('rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll')) {
    foreach ($destination in @($output, (Join-Path $output 'Backends\D3D12'))) {
        if ((Get-InputSha256 (Join-Path $runtime $name)) -ne (Get-InputSha256 (Join-Path $destination $name))) {
            throw "Source backend copy differs: $name"
        }
    }
}
Write-Host "PASS: missing-pair rejection, paired backend and runtime dependency installation. Test files: $testRoot"
Install-SourceBackend -RuntimeDirectory $runtime -Output $output -GraphicsApi Vulkan
if (!(Test-Path (Join-Path $output 'Backends\Vulkan\rexgpu-xenos.dll')) -or
    !(Test-Path (Join-Path $output 'Backends\D3D12\rexgpu-xenos.dll'))) {
    throw 'Installing Vulkan must preserve the Direct3D 12 backend.'
}
Write-Host 'PASS: separate graphics backend directories.'
