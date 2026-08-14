[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\artifacts\acceptance-zh-CN\VisualInspection.App.exe')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = Resolve-Path -LiteralPath $ExecutablePath
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList '--acceptance-smoke' -Wait -PassThru -WindowStyle Hidden
$receiptPath = Join-Path $env:LOCALAPPDATA 'VisualInspectionTestDeployment\acceptance-smoke-result.json'

if (Test-Path -LiteralPath $receiptPath) {
    Get-Content -LiteralPath $receiptPath -Raw
}
else {
    Write-Error "Acceptance receipt was not created: $receiptPath"
}

if ($process.ExitCode -ne 0) {
    throw "Acceptance smoke failed with exit code $($process.ExitCode)."
}

Write-Host "Acceptance smoke passed. Receipt: $receiptPath"
