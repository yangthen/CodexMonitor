param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\CodexMonitor.App\CodexMonitor.App.csproj'
$publishDir = Join-Path $repoRoot 'work\publish-win-x64'
$outputDir = Join-Path $repoRoot 'dist'
$installerScript = Join-Path $repoRoot 'installer\CodexMonitor.iss'

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $repoRoot 'work\inno\ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Inno Setup 7 (ISCC.exe) is required. Pass its path with -InnoCompiler.'
}

$expectedPublishDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'work\publish-win-x64'))
if ([System.IO.Path]::GetFullPath($publishDir) -ne $expectedPublishDir -or
    -not $expectedPublishDir.StartsWith([System.IO.Path]::GetFullPath($repoRoot) + [System.IO.Path]::DirectorySeparatorChar)) {
    throw 'Refusing to clean a publish folder outside this workspace.'
}
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir, $outputDir -Force | Out-Null
& dotnet clean $project -c Release -r win-x64 `
    -p:Platform=x64 `
    -p:EnableMsixTooling=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE" }

& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:EnableMsixTooling=true `
    -p:PublishTrimmed=false `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "-p:Version=$Version" `
    "-p:PublishDir=$publishDir\"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

foreach ($requiredFile in @('CodexMonitor.App.exe', 'Assets\AppIcon.ico', 'Assets\TrayIcon.ico', 'Assets\CodexKnotWhite.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $requiredFile))) {
        throw "Required release file was not published: $requiredFile"
    }
}
& $InnoCompiler "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$setup = Join-Path $outputDir "CodexMonitor-$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer was not created: $setup" }
Get-Item -LiteralPath $setup | Select-Object FullName, Length
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $outputDir 'SHA256SUMS.txt') -Encoding ascii -Value "$hash  $(Split-Path -Leaf $setup)"
Write-Output "SHA-256: $hash"
