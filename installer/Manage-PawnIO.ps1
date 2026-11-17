[CmdletBinding()]
param(
    [ValidateSet('Status', 'Prepare', 'Install', 'Uninstall')]
    [string]$Action = 'Status',
    [string]$SetupPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SetupPath)) {
    $SetupPath = Join-Path $PSScriptRoot 'dependencies\PawnIO_setup.exe'
}
$PawnIOVersion = [version]'2.2.0.0'
$PawnIOUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
$PawnIOSha256 = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
$PawnIOSigner = 'F380DCC9F706E2756A5047B832FFE719E1BC35F5'

function Get-PawnIOStatus {
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $registry.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
        try {
            $version = if ($key) { $key.GetValue('DisplayVersion') } else { $null }
            $location = if ($key) { $key.GetValue('InstallLocation') } else { $null }
        }
        finally { if ($key) { $key.Dispose() } }
    }
    finally { $registry.Dispose() }

    $service = Get-Service -Name 'PawnIO' -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Installed = -not [string]::IsNullOrWhiteSpace($version)
        Version = $version
        InstallLocation = $location
        ServiceStatus = if ($service) { $service.Status.ToString() } else { 'Not present' }
        RequiredVersion = $PawnIOVersion.ToString()
    }
}

function Get-VerifiedPawnIOSetup {
    if (-not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) {
        $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($SetupPath))
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $download = Join-Path $directory ('PawnIO-' + [guid]::NewGuid().ToString('N') + '.download')
        try {
            Invoke-WebRequest -Uri $PawnIOUrl -OutFile $download -UseBasicParsing -TimeoutSec 60
            Test-PawnIOSetup $download
            Move-Item -LiteralPath $download -Destination $SetupPath
        }
        finally {
            if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
        }
    }
    Test-PawnIOSetup $SetupPath
    return [System.IO.Path]::GetFullPath($SetupPath)
}

function Test-PawnIOSetup([string]$Path) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($hash -ne $PawnIOSha256) {
        throw 'The PawnIO setup SHA-256 does not match the official pinned 2.2.0 release.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $PawnIOSigner) {
        throw 'The PawnIO setup signature could not be verified against the pinned official signer.'
    }
}

try {
    if ($Action -eq 'Status') {
        Get-PawnIOStatus
        exit 0
    }

    $setup = Get-VerifiedPawnIOSetup
    if ($Action -eq 'Prepare') {
        Write-Output $setup
        exit 0
    }

    $status = Get-PawnIOStatus
    if ($Action -eq 'Install' -and $status.Installed -and [version]$status.Version -ge $PawnIOVersion) {
        Write-Host "PawnIO $($status.Version) is already installed."
        $status
        exit 0
    }
    if ($Action -eq 'Uninstall') {
        if (-not $status.Installed) {
            Write-Host 'PawnIO is not registered as installed.'
            $status
            exit 0
        }
        if (Get-Process -Name 'RigMD.Desktop', 'RigMD.Api' -ErrorAction SilentlyContinue) {
            throw 'Close RigMD before removing PawnIO so it can release its sensor handles.'
        }
        Write-Warning 'PawnIO is shared with other monitoring apps. Close those apps before removal.'
    }

    # The official silent defaults install the signed edition. No unrestricted switch is passed.
    $arguments = if ($Action -eq 'Install') { '-install -silent' } else { '-uninstall -silent' }
    $start = @{
        FilePath = $setup
        ArgumentList = $arguments
        WorkingDirectory = Split-Path -Parent $setup
        Wait = $true
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $administrator = ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally { $identity.Dispose() }
    if (-not $administrator) { $start.Verb = 'RunAs' }
    $process = Start-Process @start
    if ($process.ExitCode -notin @(0, 3010)) {
        throw "PawnIO $Action failed with exit code $($process.ExitCode)."
    }
    $exitCode = $process.ExitCode
    $process.Dispose()
    $after = Get-PawnIOStatus
    if ($exitCode -eq 0) {
        if ($Action -eq 'Install' -and (-not $after.Installed -or
            [version]$after.Version -lt $PawnIOVersion -or $after.ServiceStatus -eq 'Not present')) {
            throw 'PawnIO setup completed, but the required driver installation could not be confirmed.'
        }
        if ($Action -eq 'Uninstall' -and $after.Installed) {
            throw 'PawnIO setup completed, but the driver is still registered as installed.'
        }
    }
    $after
    if ($exitCode -eq 3010) {
        Write-Host 'Restart Windows to finish the PawnIO operation.'
    }
    elseif ($Action -eq 'Install') {
        Write-Host 'Restart RigMD to open the CPU sensors with the installed driver.'
    }
    exit $exitCode
}
catch {
    Write-Error $_
    exit 1
}
