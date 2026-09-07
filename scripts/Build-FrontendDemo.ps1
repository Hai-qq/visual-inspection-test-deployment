[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$FanModelPath,
    [string]$FanImagePath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $workspace 'VisualInspection.sln'
$appProject = Join-Path $workspace 'src\VisualInspection.App\VisualInspection.App.csproj'
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$releaseDate = Get-Date -Format 'yyyyMMdd'

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

    throw "$DisplayName was not found. Pass its exact path to the packaging script."
}

function Assert-LoginStartup {
    param([string]$ExecutablePath)

    $loginTitlePrefix = [string]::Concat([char]0x767B, [char]0x5F55)
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) {
                throw "Default startup exited before showing login (exit code $($process.ExitCode))."
            }
            if ($process.MainWindowTitle.StartsWith($loginTitlePrefix, [StringComparison]::Ordinal)) {
                return
            }
        } while ([DateTime]::UtcNow -lt $deadline)

        throw "Default startup did not show the login window. Current title: $($process.MainWindowTitle)"
    }
    finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            if (-not $process.CloseMainWindow()) {
                Stop-Process -Id $process.Id
            }
            elseif (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id
            }
        }
    }
}

$desktopRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$modelSearchRoot = Join-Path $desktopRoot 'code'
$modelCandidate = if (Test-Path -LiteralPath $modelSearchRoot -PathType Container) {
    Get-ChildItem -LiteralPath $modelSearchRoot -Recurse -File -Filter 'fan.onnx' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
$imageCandidate = if (Test-Path -LiteralPath $modelSearchRoot -PathType Container) {
    Get-ChildItem -LiteralPath $modelSearchRoot -Recurse -File -Filter 'IMG_1533.JPG' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
$FanModelPath = Resolve-RequiredAsset `
    -ConfiguredPath $FanModelPath `
    -Candidates @(
        $modelCandidate
    ) `
    -DisplayName 'Fan ONNX model'
$FanImagePath = Resolve-RequiredAsset `
    -ConfiguredPath $FanImagePath `
    -Candidates @(
        $imageCandidate,
        (Join-Path $workspace '..\Fan\OK\IMG_1533.JPG')
    ) `
    -DisplayName 'Fan demonstration image'
$fanModelHash = (Get-FileHash -LiteralPath $FanModelPath -Algorithm SHA256).Hash.ToLowerInvariant()
$fanImageHash = (Get-FileHash -LiteralPath $FanImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedFanModelHash = '6e30134336323f21a2125bc36b590126b9ac3d2b34ea06ef041f6bdedcb078d7'
$expectedFanImageHash = '1aae7ead89bf4f0b956e48ec35924dfa2bed08208950a4108481cfb46a4487dd'
if ($fanModelHash -ne $expectedFanModelHash) {
    throw "Fan ONNX model SHA-256 mismatch: $fanModelHash"
}
if ($fanImageHash -ne $expectedFanImageHash) {
    throw "Fan demonstration image SHA-256 mismatch: $fanImageHash"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot "frontend-demo-v0.2-$releaseDate-win-x64"
}

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactsPrefix = "$artifactsRoot$([System.IO.Path]::DirectorySeparatorChar)"
if (-not $outputPath.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of $artifactsRoot"
}

$executableName = 'VisualInspection.FrontendDemo.exe'
$executablePath = Join-Path $outputPath $executableName
if ((Test-Path -LiteralPath $executablePath) -and -not $Force) {
    throw "Artifact already exists: $executablePath. Re-run with -Force to replace only the generated delivery files."
}

$stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) "VisualInspection-FrontendDemo-$PID-$([Guid]::NewGuid().ToString('N'))"
$stagingPath = [System.IO.Path]::GetFullPath($stagingPath)
$stagingExecutable = Join-Path $stagingPath $executableName

try {
    dotnet test $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    dotnet format $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }

    dotnet restore $appProject --runtime win-x64
    $restoreExitCode = $LASTEXITCODE
    if ($restoreExitCode -ne 0) {
        Write-Warning 'win-x64 restore failed through the configured proxy; retrying this restore with proxy environment variables temporarily removed.'
        $proxyVariables = @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy')
        $proxyBackup = @{}
        foreach ($proxyVariable in $proxyVariables) {
            $proxyItem = Get-Item -LiteralPath "Env:$proxyVariable" -ErrorAction SilentlyContinue
            if ($null -ne $proxyItem) {
                $proxyBackup[$proxyVariable] = $proxyItem.Value
                Remove-Item -LiteralPath "Env:$proxyVariable"
            }
        }

        try {
            dotnet restore $appProject --runtime win-x64 --force-evaluate
            $restoreExitCode = $LASTEXITCODE
        }
        finally {
            foreach ($proxyVariable in $proxyVariables) {
                Remove-Item -LiteralPath "Env:$proxyVariable" -ErrorAction SilentlyContinue
                if ($proxyBackup.ContainsKey($proxyVariable)) {
                    Set-Item -LiteralPath "Env:$proxyVariable" -Value $proxyBackup[$proxyVariable]
                }
            }
        }
    }
    if ($restoreExitCode -ne 0) { throw 'win-x64 restore failed.' }

    dotnet publish $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $stagingPath `
        -p:FrontendDemo=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        "-p:FrontendDemoFanModelPath=$FanModelPath" `
        "-p:FrontendDemoFanImagePath=$FanImagePath"
    if ($LASTEXITCODE -ne 0) { throw 'Frontend demo publish failed.' }
    if (-not (Test-Path -LiteralPath $stagingExecutable -PathType Leaf)) {
        throw "Published executable is missing: $stagingExecutable"
    }

    $stagingSmoke = Start-Process -FilePath $stagingExecutable -ArgumentList '--ui-construction-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($stagingSmoke.ExitCode -ne 0) {
        throw "Staging UI construction smoke failed with exit code $($stagingSmoke.ExitCode)."
    }

    $stagingAcceptance = Start-Process -FilePath $stagingExecutable -ArgumentList '--acceptance-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($stagingAcceptance.ExitCode -ne 0) {
        throw "Staging Fan acceptance smoke failed with exit code $($stagingAcceptance.ExitCode)."
    }

    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    Copy-Item -LiteralPath $stagingExecutable -Destination $executablePath -Force

    $finalSmoke = Start-Process -FilePath $executablePath -ArgumentList '--ui-construction-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($finalSmoke.ExitCode -ne 0) {
        throw "Final single-file UI construction smoke failed with exit code $($finalSmoke.ExitCode)."
    }

    $finalAcceptance = Start-Process -FilePath $executablePath -ArgumentList '--acceptance-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($finalAcceptance.ExitCode -ne 0) {
        throw "Final Fan acceptance smoke failed with exit code $($finalAcceptance.ExitCode)."
    }
    $acceptanceReceiptPath = Join-Path $env:LOCALAPPDATA 'VisualInspectionTestDeployment\acceptance-smoke-result.json'
    $acceptanceReceipt = Get-Content -LiteralPath $acceptanceReceiptPath -Raw | ConvertFrom-Json
    $fanItemReceipts = @($acceptanceReceipt.itemResults)
    if ($acceptanceReceipt.verdict -ne 'pass' -or
        [System.IO.Path]::GetFileName($acceptanceReceipt.frameOrigin) -ne 'IMG_1533.JPG' -or
        -not $acceptanceReceipt.provider.StartsWith('ONNX Runtime CPU', [StringComparison]::Ordinal) -or
        $fanItemReceipts.Count -ne 1 -or
        @($fanItemReceipts[0].measured -split ';').Count -ne 6) {
        throw 'Final Fan acceptance receipt did not confirm one six-rule test step on IMG_1533.JPG with the real ONNX Runtime CPU provider.'
    }

    Assert-LoginStartup -ExecutablePath $executablePath

    $hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $baseCommit = (git -C $workspace rev-parse HEAD).Trim()
    $sourceState = if (git -C $workspace status --porcelain) { 'working-tree-with-uncommitted-changes' } else { 'clean-working-tree' }
    $builtAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')

    $receipt = @"
Artifact=$executableName
BuiltAt=$builtAt
Platform=win-x64
Deployment=self-contained-single-file
DefaultEntry=Login
DemoFlow=Login -> Operator -> V2 design -> Operator
FanImage=IMG_1533.JPG
FanRuntime=real ONNX Runtime CPU
FanTestSteps=1
FanRulesInStep=6
FanModelSHA256=$fanModelHash
FanImageSHA256=$fanImageHash
AssemblyVersion=0.6.0
FrontendBaseline=approved frontend v0.2 through Pass 17
BaseCommit=$baseCommit
SourceState=$sourceState
Tests=pass
Format=pass
UIConstructionSmoke=pass
FanAcceptanceSmoke=pass
StartupLoginSmoke=pass
SHA256=$hash
"@
    $stagingReceipt = Join-Path $stagingPath 'build-receipt.txt'
    [System.IO.File]::WriteAllText(
        $stagingReceipt,
        $receipt,
        [System.Text.UTF8Encoding]::new($false))
    if ((Get-Item -LiteralPath $stagingReceipt).Length -eq 0) {
        throw 'Generated build receipt is empty.'
    }

    $readmeTemplate = Join-Path $PSScriptRoot 'FrontendDemo-README.txt'
    if (-not (Test-Path -LiteralPath $readmeTemplate -PathType Leaf)) {
        throw "Frontend demo README template is missing: $readmeTemplate"
    }
    Copy-Item -LiteralPath $readmeTemplate -Destination (Join-Path $outputPath 'README-FrontendDemo.txt') -Force
    Copy-Item -LiteralPath $stagingReceipt -Destination (Join-Path $outputPath 'build-receipt.txt') -Force

    Write-Host "Frontend demo executable: $executablePath"
    Write-Host "SHA256: $hash"
}
finally {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $stagingLeaf = [System.IO.Path]::GetFileName($stagingPath)
    if ((Test-Path -LiteralPath $stagingPath) -and
        $stagingPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $stagingLeaf.StartsWith('VisualInspection-FrontendDemo-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}
