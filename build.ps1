# Build and collect Release assemblies for JEMSts_UDS_Parser.
# Usage: .\build.ps1 [-Deploy -DeployPath <JEMSts Modules\UserLibraries path>]

[CmdletBinding()]
param(
    [switch]$Deploy,
    [string]$DeployPath
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "== Building solution (Release) ==" -ForegroundColor Cyan
dotnet build "$root\JEMSts_UDS_Parser.slnx" -c Release

if ($LASTEXITCODE -eq 0) {
    $out = Join-Path $root "artifacts"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    # Copy every first-party net48 assembly; leave external and vendor DLLs intact.
    $dlls = @(
        "ExcelDecodeCore\bin\Release\net48\Jabil.Uds.Shared.dll",
        "UdsProtocol\bin\Release\net48\Jabil.Uds.Protocol.dll",
        "UdsCore\bin\Release\net48\Jabil.Uds.DoIp.dll",
        "UdsEngine\bin\Release\net48\Jabil.Uds.Engine.dll",
        "UdsCan\bin\Release\net48\Jabil.Uds.Can.dll",
        "UdsCanZlg\bin\Release\net48\Jabil.Uds.Can.Zlg.dll",
        "UdsFunctionLibrary\bin\Release\net48\Jabil.Uds.FunctionLibrary.dll"
    )
    foreach ($dll in $dlls) {
        Copy-Item (Join-Path $root $dll) $out -Force
    }
    if ($Deploy) {
        if ([string]::IsNullOrWhiteSpace($DeployPath)) {
            throw "-Deploy requires -DeployPath pointing to the intended JEMSts Modules\UserLibraries directory."
        }
        if (-not (Test-Path $DeployPath -PathType Container)) {
            throw "Deployment directory does not exist: $DeployPath"
        }
        foreach ($dll in $dlls) {
            Copy-Item (Join-Path $out (Split-Path $dll -Leaf)) $DeployPath -Force
        }
        Write-Host "Deployed updated DLLs to: $DeployPath" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "== BUILD SUCCESS ==" -ForegroundColor Green
    Write-Host "First-party DLLs copied to: $out"
    if (-not $Deploy) {
        Write-Host "No deployment was performed. Use -Deploy -DeployPath to copy DLLs to JEMSts."
    }
    Get-ChildItem $out -Filter *.dll | Select-Object Name, Length | Format-Table -AutoSize
}
else {
    Write-Host "== BUILD FAILED ==" -ForegroundColor Red
    exit 1
}
