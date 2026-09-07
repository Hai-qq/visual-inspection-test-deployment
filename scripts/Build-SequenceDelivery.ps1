param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\FAN-A01-Receiver-Package-20260829'),
    [string]$FanModelPath,
    [string]$FanImagePath
)

$ErrorActionPreference = 'Stop'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

function Resolve-RequiredAsset {
    param(
        [string]$ConfiguredPath,
        [string[]]$Candidates,
        [string]$DisplayName
    )

    $paths = @()
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        $paths += $ConfiguredPath
    }
    $paths += $Candidates
    foreach ($path in $paths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            return [System.IO.Path]::GetFullPath($path)
        }
    }

    throw "$DisplayName was not found. Pass its exact path to Build-SequenceDelivery.ps1."
}

$assetRoot = Join-Path $env:LOCALAPPDATA 'VisualInspectionTestDeployment\bundled-fan'
$FanModelPath = Resolve-RequiredAsset `
    -ConfiguredPath $FanModelPath `
    -Candidates @((Join-Path $assetRoot 'models\fan.onnx')) `
    -DisplayName 'Fan ONNX model'
$FanImagePath = Resolve-RequiredAsset `
    -ConfiguredPath $FanImagePath `
    -Candidates @((Join-Path $assetRoot 'input\IMG_1533.JPG')) `
    -DisplayName 'Fan demonstration image'

$expectedModelHash = '6e30134336323f21a2125bc36b590126b9ac3d2b34ea06ef041f6bdedcb078d7'
$expectedImageHash = '1aae7ead89bf4f0b956e48ec35924dfa2bed08208950a4108481cfb46a4487dd'
if ((Get-FileHash -LiteralPath $FanModelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedModelHash) {
    throw 'Fan ONNX model SHA-256 mismatch.'
}
if ((Get-FileHash -LiteralPath $FanImagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedImageHash) {
    throw 'Fan demonstration image SHA-256 mismatch.'
}

$sequencePath = Join-Path $resolvedOutput 'FAN-A01.sequence.json'
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$stagingDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot `
    ("VisualInspection-FAN-A01-Delivery-{0}" -f [Guid]::NewGuid().ToString('N'))
))
if (-not $stagingDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $stagingDirectory.Equals($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary staging directory escaped the system temporary root: $stagingDirectory"
}
$stagedModelPath = Join-Path $stagingDirectory 'FAN-A01.onnx'
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try {
    Copy-Item -LiteralPath $FanModelPath -Destination $stagedModelPath
    dotnet run --no-restore --project (Join-Path $PSScriptRoot '..\src\VisualInspection.App\VisualInspection.App.csproj') -- `
        --export-sample-sequence $sequencePath $stagedModelPath $FanImagePath
    if ($LASTEXITCODE -ne 0) {
        throw "Representative sequence export failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

$guideCandidates = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\docs') -File -Filter 'Sequence*.md')
if ($guideCandidates.Count -ne 1) {
    throw "Expected exactly one Sequence delivery guide, found $($guideCandidates.Count)."
}
$guidePath = Join-Path $resolvedOutput 'FAN-A01-Deployment-Reference.zh-CN.md'
Copy-Item -LiteralPath $guideCandidates[0].FullName -Destination $guidePath -Force

$inputDirectory = Join-Path $resolvedOutput 'input'
$sampleImagePath = Join-Path $inputDirectory 'IMG_1533.JPG'
New-Item -ItemType Directory -Path $inputDirectory -Force | Out-Null
Copy-Item -LiteralPath $FanImagePath -Destination $sampleImagePath -Force

$requiredFiles = @(
    $sequencePath,
    (Join-Path $resolvedOutput 'FAN-A01.onnx'),
    $guidePath,
    $sampleImagePath
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Delivery file is missing: $requiredFile"
    }
}

dotnet run --no-build --no-restore --project (Join-Path $PSScriptRoot '..\src\VisualInspection.App\VisualInspection.App.csproj') -- `
    --verify-portable-sequence $sequencePath
if ($LASTEXITCODE -ne 0) {
    throw "Operator-compatible sequence verification failed with exit code $LASTEXITCODE."
}

$outputPrefix = $resolvedOutput
if (-not $outputPrefix.EndsWith('\', [StringComparison]::Ordinal)) {
    $outputPrefix += '\'
}
$hashLines = foreach ($requiredFile in $requiredFiles) {
    $resolvedRequiredFile = [System.IO.Path]::GetFullPath($requiredFile)
    if (-not $resolvedRequiredFile.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Delivery file escaped the output directory: $resolvedRequiredFile"
    }
    $hash = Get-FileHash -LiteralPath $requiredFile -Algorithm SHA256
    $relativePath = $resolvedRequiredFile.Substring($outputPrefix.Length).Replace('\', '/')
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $relativePath
}
$hashLines | Set-Content -LiteralPath (Join-Path $resolvedOutput 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Sequence delivery generated: $resolvedOutput"
