$ErrorActionPreference = "Stop"

$RepoRoot =
    Split-Path -Parent $PSScriptRoot

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
        "backend-dotnet\RigMD.Desktop\bin\Release\net10.0-windows\win-x64\publish"

$ApiPublishDirectory =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Api\bin\Release\net10.0-windows\win-x64\publish"

$FrontendDirectory =
    Join-Path `
        $RepoRoot `
        "frontend"

$InstallerScript =
    Join-Path `
        $PSScriptRoot `
        "RigMD-Setup.iss"

$InnoCompiler = $null
$CandidateCompilerPaths = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
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
    throw "Inno Setup compiler (ISCC.exe) was not found. Please install Inno Setup 6."
}

Write-Host "Inno Setup compiler found: $InnoCompiler"
Write-Host "Required tools found."
Write-Host ""

Write-Host "[2/7] Building React frontend..."

Push-Location $FrontendDirectory
try
{
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
Write-Host ""

Write-Host "[3/7] Cleaning previous publish output..."

foreach ($directory in @(
    $DesktopPublishDirectory,
    $ApiPublishDirectory))
{
    if (Test-Path $directory)
    {
        Remove-Item `
            $directory `
            -Recurse `
            -Force
    }
}

Write-Host "Previous publish output removed."
Write-Host ""

Write-Host "[4/7] Publishing self-contained RigMD Desktop..."

& dotnet publish `
    $DesktopProject `
    -c Release `
    -r win-x64 `
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
    "*.pdb"
)

foreach ($pubDir in @($DesktopPublishDirectory, $ApiPublishDirectory))
{
    foreach ($pattern in $SensitivePatterns)
    {
        Get-ChildItem -Path $pubDir -Filter $pattern -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
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

& $InnoCompiler `
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

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD installer build succeeded."
Write-Host "======================================"
Write-Host ""
Write-Host "Output:"
Write-Host $InstallerOutput
Write-Host ""
