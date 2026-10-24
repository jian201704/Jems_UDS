# Build script for JEMSts_UDS_Parser
# Usage: PowerShell -ExecutionPolicy Bypass -File build.ps1

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
    $deploy = "D:\Projects\ZEEKR\Software\JEMSts_8.55.00x64\Modules\UserLibraries"
    if (Test-Path $deploy) {
        foreach ($dll in $dlls) {
            Copy-Item (Join-Path $out (Split-Path $dll -Leaf)) $deploy -Force
        }
        Write-Host "Deployed updated DLLs to: $deploy" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "== BUILD SUCCESS ==" -ForegroundColor Green
    Write-Host "First-party DLLs copied to artifacts; deploy them to (JEMSts probing already includes this dir):"
    Write-Host "  D:\Projects\ZEEKR\Software\JEMSts_8.55.00x64\Modules\UserLibraries\" -ForegroundColor Yellow
    Get-ChildItem $out -Filter *.dll | Select-Object Name, Length | Format-Table -AutoSize
}
else {
    Write-Host "== BUILD FAILED ==" -ForegroundColor Red
    exit 1
}
