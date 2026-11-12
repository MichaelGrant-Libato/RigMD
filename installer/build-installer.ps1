$ErrorActionPreference = "Stop"

$RepoRoot =
    Split-Path -Parent $PSScriptRoot
$ReleaseVersion = (Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "frontend\release.json") | ConvertFrom-Json).version
if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version." }
foreach ($versionFile in @("backend-dotnet\RigMD.Api\RigMD.Api.csproj", "backend-dotnet\RigMD.Desktop\RigMD.Desktop.csproj")) {
    [xml]$project = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $versionFile)
    if ($project.Project.PropertyGroup.Version -ne $ReleaseVersion) { throw "Release version mismatch: $versionFile" }
}
if ((Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "frontend\package.json") | ConvertFrom-Json).version -ne $ReleaseVersion -or
    (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "RigMD-Setup.iss")) -notmatch ('#define MyAppVersion "' + [regex]::Escape($ReleaseVersion) + '"')) {
    throw "Frontend or installer release version mismatch."
}

$DesktopProject =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Desktop\RigMD.Desktop.csproj"

$ApiProject =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Api\RigMD.Api.csproj"

$DesktopPublishDirectory =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Desktop\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

$ApiPublishDirectory =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Api\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

$FrontendDirectory =
    Join-Path `
        $RepoRoot `
        "frontend"

function Get-SourceSnapshot {
    $paths = @(& git -c core.quotePath=false -C $RepoRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw "Source provenance could not be read." }
    $rootPrefix = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
    $files = @($paths | Where-Object { $_ -notmatch '(^|/)(artifacts|bin|obj|node_modules)/|^installer/output/' } | Sort-Object -Unique | ForEach-Object {
        $absolute = [IO.Path]::GetFullPath((Join-Path $RepoRoot $_))
        if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Source path is outside the repository." }
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
        }
    })
    if ($files.Count -eq 0) { throw "Source provenance is empty." }
    $canonical = ($files | ForEach-Object { $_.path + "`t" + $_.sha256 }) -join "`n"
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '') }
    finally { $algorithm.Dispose() }
    [pscustomobject]@{ Hash=$hash; Files=$files }
}

$InstallerScript =
    Join-Path `
        $PSScriptRoot `
        "RigMD-Setup.iss"

# Isolate compilation from a running development app. Published inputs retain the installer’s expected layout.
$PackageBuildRoot = Join-Path $RepoRoot ("artifacts\package-build\" + $ReleaseVersion + "-" + [guid]::NewGuid().ToString("N") + "\")
$DesktopPublishDirectory = Join-Path $PackageBuildRoot "Desktop\win-x64\publish"
$ApiPublishDirectory = Join-Path $PackageBuildRoot "Api\win-x64\publish"

$InnoCompiler = $null
$CandidateCompilerPaths = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

foreach ($candidate in $CandidateCompilerPaths)
{
    if ($candidate -and (Test-Path $candidate))
    {
        $InnoCompiler = $candidate
        break
    }
}

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD Combined Installer Build"
Write-Host "======================================"
Write-Host ""

Write-Host "[1/7] Checking required tools..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
{
    throw ".NET SDK was not found in PATH."
}

if (-not $InnoCompiler -or -not (Test-Path $InnoCompiler))
{
    throw "Inno Setup compiler (ISCC.exe) was not found. Please install Inno Setup 6 or 7."
}

Write-Host "Inno Setup compiler found: $InnoCompiler"
Write-Host "Required tools found."
Write-Host ""

Write-Host "[2/7] Building React frontend..."

Push-Location $FrontendDirectory
try
{
    $env:VITE_PUBLIC_DOWNLOAD_SITE = "false"
    $env:VITE_SUPABASE_URL = ""
    $env:VITE_SUPABASE_PUBLISHABLE_KEY = ""
    $env:VITE_SUPABASE_ANON_KEY = ""
    & cmd.exe /c "npm run build"
    if ($LASTEXITCODE -ne 0)
    {
        throw "React frontend build failed."
    }
}
finally
{
    Pop-Location
}

Write-Host "React frontend build completed."
$SourceSnapshot = Get-SourceSnapshot
Write-Host ""

Write-Host "[3/7] Checking fresh publish output..."

foreach ($directory in @(
    $DesktopPublishDirectory,
    $ApiPublishDirectory))
{
    $resolvedPublishPath = [System.IO.Path]::GetFullPath($directory)
    $expectedBuildRoot = [System.IO.Path]::GetFullPath($PackageBuildRoot).TrimEnd('\') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPublishPath.StartsWith($expectedBuildRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedPublishPath.EndsWith("\win-x64\publish", [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Unexpected publish cleanup path: $resolvedPublishPath"
    }
    if (Test-Path $directory)
    {
        throw "The fresh staging directory already exists: $resolvedPublishPath"
    }
}

Write-Host "New isolated publish directories selected; previous builds are retained."
Write-Host ""

Write-Host "[4/7] Publishing self-contained RigMD Desktop..."

& dotnet publish `
    $DesktopProject `
    -c Release `
    -r win-x64 `
    --output $DesktopPublishDirectory `
    --artifacts-path (Join-Path $PackageBuildRoot "artifacts") `
    -p:PublishReadyToRun=true `
    --self-contained true

if ($LASTEXITCODE -ne 0)
{
    throw "RigMD Desktop publish failed."
}

$DesktopExecutable =
    Join-Path `
        $DesktopPublishDirectory `
        "RigMD.Desktop.exe"

if (-not (Test-Path $DesktopExecutable))
{
    throw "Published RigMD.Desktop.exe was not found."
}

Write-Host "RigMD Desktop publish completed."
Write-Host ""

Write-Host "[5/7] Publishing self-contained RigMD API..."

& dotnet publish `
    $ApiProject `
    -c Release `
    -r win-x64 `
    --output $ApiPublishDirectory `
    --artifacts-path (Join-Path $PackageBuildRoot "artifacts") `
    -p:PublishReadyToRun=true `
    --self-contained true

if ($LASTEXITCODE -ne 0)
{
    throw "RigMD API publish failed."
}

$ApiExecutable =
    Join-Path `
        $ApiPublishDirectory `
        "RigMD.Api.exe"

$ApiDeps =
    Join-Path `
        $ApiPublishDirectory `
        "RigMD.Api.deps.json"

$ApiRuntimeConfig =
    Join-Path `
        $ApiPublishDirectory `
        "RigMD.Api.runtimeconfig.json"

$ApiFrontendIndex =
    Join-Path `
        $ApiPublishDirectory `
        "wwwroot\index.html"

if (-not (Test-Path $ApiExecutable))
{
    throw "Published RigMD.Api.exe was not found."
}

if (-not (Test-Path $ApiDeps))
{
    throw "RigMD.Api.deps.json was not found."
}

if (-not (Test-Path $ApiRuntimeConfig))
{
    throw "RigMD.Api.runtimeconfig.json was not found."
}

if (-not (Test-Path $ApiFrontendIndex))
{
    throw "Published API frontend wwwroot\index.html was not found."
}

Write-Host "RigMD API publish completed."
Write-Host ""

Write-Host "[6/7] Scrubbing sensitive config files & verifying installer inputs..."

$SensitivePatterns = @(
    "appsettings.Development.json",
    "appsettings.Development.example.json",
    "appsettings.Local.json",
    "appsettings.*.local.json",
    "secrets.json",
    ".env",
    ".env.*",
    "*.db",
    "*.db-shm",
    "*.db-wal",
    "cloud-account.bin",
    "*.user",
    "*.pdb",
    "*.sys",
    "LibreHardwareMonitorLib.dll"
)

foreach ($pubDir in @($DesktopPublishDirectory, $ApiPublishDirectory))
{
    foreach ($pattern in $SensitivePatterns)
    {
        Get-ChildItem -Path $pubDir -Filter $pattern -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    $pubAppSettings = Join-Path $pubDir "appsettings.json"
    if (Test-Path $pubAppSettings)
    {
        $sanitizedConfig = [ordered]@{
            Logging = [ordered]@{
                LogLevel = [ordered]@{
                    Default = "Information"
                    "Microsoft.AspNetCore" = "Warning"
                }
            }
            AllowedHosts = "localhost;127.0.0.1;[::1]"
            Gemini = [ordered]@{
                ApiKey = ""
                ProxyUrl = "https://www.rig-md.com/api/gemini"
            }
            DATABASE_URL = ""
        }
        $sanitizedConfig | ConvertTo-Json -Depth 5 | Set-Content -Path $pubAppSettings -Encoding UTF8
    }
}

Write-Host "Desktop:"
Write-Host $DesktopExecutable

Write-Host "API:"
Write-Host $ApiExecutable

Write-Host "API deps:"
Write-Host $ApiDeps

Write-Host "Frontend:"
Write-Host $ApiFrontendIndex

Write-Host ""

Write-Host "[7/7] Compiling RigMD installer..."

if ((Get-SourceSnapshot).Hash -ne $SourceSnapshot.Hash) {
    throw "Source files changed during publishing. Build a fresh candidate before distributing it."
}

& $InnoCompiler `
    "/DDesktopPublishDirectory=$DesktopPublishDirectory" `
    "/DApiPublishDirectory=$ApiPublishDirectory" `
    $InstallerScript

if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup compilation failed."
}

$InstallerOutput = Join-Path $PSScriptRoot "output\RigMD-Setup.exe"

if (-not $InstallerOutput -or -not (Test-Path $InstallerOutput))
{
    throw "RigMD setup executable was not created in output directory."
}

$VersionedInstallerOutput = Join-Path $PSScriptRoot "output\RigMD-Setup-v$ReleaseVersion.exe"
Copy-Item -Path $InstallerOutput -Destination $VersionedInstallerOutput -Force

$InstallerHash = (Get-FileHash -Path $InstallerOutput -Algorithm SHA256).Hash
$InstallerSizeMb = [Math]::Round((Get-Item $InstallerOutput).Length / 1MB, 2)
$sourceRevision = (& git -C $RepoRoot rev-parse HEAD 2>$null)
$sourceDirty = $null
if ($LASTEXITCODE -eq 0) {
    $gitStatus = @(& git -C $RepoRoot status --porcelain 2>$null)
    if ($LASTEXITCODE -eq 0) { $sourceDirty = $gitStatus.Count -gt 0 }
} else { $sourceRevision = $null }
$releaseManifest = [ordered]@{ version=$ReleaseVersion; builtAtUtc=[DateTimeOffset]::UtcNow.ToString("O"); sha256=$InstallerHash; bytes=(Get-Item $InstallerOutput).Length; sourceRevision=$sourceRevision; sourceDirty=$sourceDirty; sourceTreeSha256=$SourceSnapshot.Hash; signatureStatus=(Get-AuthenticodeSignature -LiteralPath $InstallerOutput).Status.ToString() }
$SourceSnapshot.Files | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "output\source-files.json") -Encoding UTF8
$releaseManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot "output\release-manifest.json") -Encoding UTF8

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD v$ReleaseVersion installer build succeeded."
Write-Host "======================================"
Write-Host ""
Write-Host "Output:"
Write-Host "  - $InstallerOutput"
Write-Host "  - $VersionedInstallerOutput"
Write-Host "Size:   $InstallerSizeMb MB"
Write-Host "SHA256: $InstallerHash"
Write-Host ""
