[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $workspace 'VisualInspection.sln'
$appProject = Join-Path $workspace 'src\VisualInspection.App\VisualInspection.App.csproj'
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$releaseDate = Get-Date -Format 'yyyyMMdd'

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
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Frontend demo publish failed.' }
    if (-not (Test-Path -LiteralPath $stagingExecutable -PathType Leaf)) {
        throw "Published executable is missing: $stagingExecutable"
    }

    $stagingSmoke = Start-Process -FilePath $stagingExecutable -ArgumentList '--ui-construction-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($stagingSmoke.ExitCode -ne 0) {
        throw "Staging UI construction smoke failed with exit code $($stagingSmoke.ExitCode)."
    }

    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    Copy-Item -LiteralPath $stagingExecutable -Destination $executablePath -Force

    $finalSmoke = Start-Process -FilePath $executablePath -ArgumentList '--ui-construction-smoke' -Wait -PassThru -WindowStyle Hidden
    if ($finalSmoke.ExitCode -ne 0) {
        throw "Final single-file UI construction smoke failed with exit code $($finalSmoke.ExitCode)."
    }

    $hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $baseCommit = (git -C $workspace rev-parse HEAD).Trim()
    $sourceState = if (git -C $workspace status --porcelain) { 'working-tree-with-uncommitted-changes' } else { 'clean-working-tree' }
    $builtAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')

    $instructions = @"
Visual Inspection Test Deployment - 前端演示版 v0.2

双击 $executableName，直接进入已确认的 V2 五步前端界面。
该 EXE 为 Windows x64 自包含单文件，不要求另行安装 .NET 8 Desktop Runtime。

边界：此包用于前端界面演示，不代表视频读取、相机/PLC/IO 接入、图像分割/姿态推理、自定义函数执行或 V2 生产运行链已经完成。
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $outputPath 'README-前端演示.txt'),
        $instructions,
        [System.Text.UTF8Encoding]::new($true))

    $receipt = @"
Artifact=$executableName
BuiltAt=$builtAt
Platform=win-x64
Deployment=self-contained-single-file
DefaultEntry=V2 frontend demo
AssemblyVersion=0.6.0
FrontendBaseline=approved frontend v0.2 plus Pass 13-15
BaseCommit=$baseCommit
SourceState=$sourceState
Tests=pass
Format=pass
UIConstructionSmoke=pass
SHA256=$hash
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $outputPath 'build-receipt.txt'),
        $receipt,
        [System.Text.UTF8Encoding]::new($false))

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
