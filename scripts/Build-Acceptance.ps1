[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution = Join-Path $workspace 'VisualInspection.sln'
$appProject = Join-Path $workspace 'src\VisualInspection.App\VisualInspection.App.csproj'
$publishDirectory = Join-Path $workspace 'artifacts\acceptance-zh-CN'
$archivePath = Join-Path $workspace 'artifacts\VisualInspection-v0.6.0-onnx-yolo-e2e-zh-CN-win-x64.zip'

dotnet test $solution --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

dotnet format $solution --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }

dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained false --output $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

& (Join-Path $PSScriptRoot 'Invoke-AcceptanceSmoke.ps1') -ExecutablePath (Join-Path $publishDirectory 'VisualInspection.App.exe')

$uiSmoke = Start-Process -FilePath (Join-Path $publishDirectory 'VisualInspection.App.exe') -ArgumentList '--ui-construction-smoke' -Wait -PassThru -WindowStyle Hidden
if ($uiSmoke.ExitCode -ne 0) {
    $uiReceipt = Join-Path $env:LOCALAPPDATA 'VisualInspectionTestDeployment\ui-construction-smoke.txt'
    if (Test-Path -LiteralPath $uiReceipt) { Get-Content -LiteralPath $uiReceipt }
    throw "UI construction smoke failed with exit code $($uiSmoke.ExitCode)."
}
Write-Host 'UI construction smoke passed.'

$startupSmoke = Start-Process -FilePath (Join-Path $publishDirectory 'VisualInspection.App.exe') -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
$startupSmoke.Refresh()
if ($startupSmoke.HasExited) {
    throw "Application startup lifetime smoke failed with exit code $($startupSmoke.ExitCode)."
}
Stop-Process -Id $startupSmoke.Id
Write-Host 'Application startup lifetime smoke passed.'

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
Write-Host "Chinese acceptance package: $archivePath"
