$ErrorActionPreference = "Stop"

$RepoRoot =
    Split-Path -Parent $PSScriptRoot

$AgentProject =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Agent\RigMD.Agent.csproj"

$PublishDirectory =
    Join-Path `
        $RepoRoot `
        "backend-dotnet\RigMD.Agent\bin\Release\net10.0-windows\win-x64\publish"

$InstallerScript =
    Join-Path `
        $PSScriptRoot `
        "RigMD-Setup.iss"

$InnoCompiler =
    Join-Path `
        $env:LOCALAPPDATA `
        "Programs\Inno Setup 6\ISCC.exe"

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD Installer Build"
Write-Host "======================================"
Write-Host ""

Write-Host "[1/4] Checking required tools..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
{
    throw ".NET SDK was not found in PATH."
}

if (-not (Test-Path $InnoCompiler))
{
    throw "Inno Setup compiler was not found at: $InnoCompiler"
}

Write-Host "Required tools found."
Write-Host ""

Write-Host "[2/4] Cleaning previous Agent publish..."

if (Test-Path $PublishDirectory)
{
    Remove-Item `
        $PublishDirectory `
        -Recurse `
        -Force
}

Write-Host "Previous publish output removed."
Write-Host ""

Write-Host "[3/4] Publishing self-contained RigMD Agent..."

& dotnet publish `
    $AgentProject `
    -c Release `
    -r win-x64 `
    --self-contained true

if ($LASTEXITCODE -ne 0)
{
    throw "RigMD Agent publish failed."
}

$AgentExecutable =
    Join-Path `
        $PublishDirectory `
        "RigMD.Agent.exe"

$RuntimeConfig =
    Join-Path `
        $PublishDirectory `
        "RigMD.Agent.runtimeconfig.json"

if (-not (Test-Path $AgentExecutable))
{
    throw "Published RigMD.Agent.exe was not found."
}

if (-not (Test-Path $RuntimeConfig))
{
    throw "Agent runtime configuration was not found."
}

Write-Host ""
Write-Host "Self-contained Agent publish completed."
Write-Host ""

Write-Host "[4/4] Compiling RigMD installer..."

& $InnoCompiler `
    $InstallerScript

if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup compilation failed."
}

$InstallerOutput =
    Join-Path `
        $PSScriptRoot `
        "output\RigMD-Setup.exe"

if (-not (Test-Path $InstallerOutput))
{
    throw "RigMD-Setup.exe was not created."
}

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD installer build succeeded."
Write-Host "======================================"
Write-Host ""
Write-Host "Output:"
Write-Host $InstallerOutput
Write-Host ""