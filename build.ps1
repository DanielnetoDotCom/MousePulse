<#
.SYNOPSIS
    Runs the unit tests and publishes MousePulse as a self-contained, single-file win-x64 executable.
.EXAMPLE
    .\build.ps1                # test + publish to .\dist\MousePulse.exe
    .\build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Configuration = 'Release',
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults.
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot 'dist' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Find-Dotnet {
    $candidates = @()
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $candidates += (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
    $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    foreach ($c in $candidates | Select-Object -Unique) {
        if ((Test-Path $c) -and (& $c --list-sdks | Where-Object { $_ -match '^10\.' })) { return $c }
    }
    throw 'The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0'
}

$dotnet = Find-Dotnet
Write-Host "Using $dotnet"

if (-not $SkipTests) {
    & $dotnet test (Join-Path $PSScriptRoot 'tests\MousePulse.Tests\MousePulse.Tests.csproj') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
}

& $dotnet publish (Join-Path $PSScriptRoot 'src\MousePulse\MousePulse.csproj') -c $Configuration -r win-x64 -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $OutputDir 'MousePulse.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Published: $exe ($sizeMb MB)"
Write-Host "SHA-256:   $hash"
