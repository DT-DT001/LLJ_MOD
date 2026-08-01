param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localAppData = Join-Path $projectRoot '.appdata'
New-Item -ItemType Directory -Force (Join-Path $localAppData 'NuGet') | Out-Null
$env:APPDATA = $localAppData
$env:DOTNET_CLI_HOME = $localAppData
$env:NUGET_PACKAGES = Join-Path $localAppData 'packages'

dotnet restore (Join-Path $projectRoot 'src\LLJDeviceLink\LLJDeviceLink.csproj') --configfile (Join-Path $projectRoot 'NuGet.Config')
if (-not $SkipTests) {
    dotnet restore (Join-Path $projectRoot 'tests\ProtocolSelfTest\ProtocolSelfTest.csproj') --configfile (Join-Path $projectRoot 'NuGet.Config')
    dotnet run --no-restore --project (Join-Path $projectRoot 'tests\ProtocolSelfTest\ProtocolSelfTest.csproj')
}
dotnet build (Join-Path $projectRoot 'src\LLJDeviceLink\LLJDeviceLink.csproj') -c Release --no-restore

$build = Join-Path $projectRoot 'src\LLJDeviceLink\bin\Release\net6.0-windows10.0.19041.0'
$obsoleteCoyoteWaveforms = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\obsolete-coyote-waveforms.txt') -Encoding UTF8 |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$coyoteSourceDirectory = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'waveforms') -Directory |
    Where-Object { @(Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.pulse').Count -gt 0 } |
    Select-Object -First 1
if ($null -eq $coyoteSourceDirectory) {
    throw 'Coyote PULSE waveform directory was not found.'
}
foreach ($rootName in @('mod_out', 'Game')) {
    $root = Join-Path $projectRoot $rootName
    $plugins = Join-Path $root 'BepInEx\plugins\LLJDeviceLink'
    New-Item -ItemType Directory -Force $plugins | Out-Null
    Copy-Item -LiteralPath (Join-Path $build 'LLJDeviceLink.dll'), (Join-Path $build 'Microsoft.Windows.SDK.NET.dll'), (Join-Path $build 'WinRT.Runtime.dll') -Destination $plugins -Force
    $waveforms = Join-Path $root 'waveforms'
    New-Item -ItemType Directory -Force $waveforms | Out-Null
    $coyoteWaveforms = Join-Path $waveforms $coyoteSourceDirectory.Name
    foreach ($obsolete in $obsoleteCoyoteWaveforms) {
        $obsoletePath = Join-Path $coyoteWaveforms $obsolete
        if (Test-Path -LiteralPath $obsoletePath) {
            Remove-Item -LiteralPath $obsoletePath -Force
        }
    }
    Copy-Item -Path (Join-Path $projectRoot 'waveforms\*') -Destination $waveforms -Recurse -Force
}

Write-Host 'Build and deployment complete.'
