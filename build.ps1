#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'dotnet SDK not found' }
dotnet restore CubaseInspector.slnx -p:Configuration=$Configuration
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet test tests/CubaseInspector.Tests/CubaseInspector.Tests.csproj --no-restore -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet build src/CubaseInspector.App/CubaseInspector.App/CubaseInspector.App.csproj --no-restore -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Uno Desktop build failed' }
dotnet build src/CubaseInspector.Cli/CubaseInspector.Cli.csproj --no-restore -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }
Write-Host 'All validation commands succeeded.' -ForegroundColor Green
