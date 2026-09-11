[CmdletBinding()]
param (
    [switch]$Silent,
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "======================================"
Write-Host " RigMD Uninstaller Helper"
Write-Host "======================================"
Write-Host ""

function Find-RigMDUninstaller {
    # 1. Check registry uninstall keys
    $regPaths = @(
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    foreach ($regPath in $regPaths) {
        try {
            $items = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
            foreach ($item in $items) {
                if ($item.DisplayName -match "^RigMD" -and $item.UninstallString) {
                    $uninstaller = $item.UninstallString.Trim('"')
                    if (Test-Path $uninstaller) {
                        return $uninstaller
                    }
                }
            }
        } catch {
            # Continue checking fallback locations
        }
    }

    # 2. Check well-known default locations
    $defaultPaths = @(
        "C:\Program Files\RigMD\unins000.exe",
        "${env:ProgramFiles}\RigMD\unins000.exe",
        "${env:LOCALAPPDATA}\Programs\RigMD\unins000.exe"
    )

    foreach ($path in $defaultPaths) {
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    return $null
}

$uninstallerPath = Find-RigMDUninstaller

if (-not $uninstallerPath) {
    Write-Warning "Could not find RigMD uninstaller executable."
    Write-Warning "Checked Windows Registry and standard directories (e.g. C:\Program Files\RigMD\unins000.exe)."
    Write-Host "If RigMD is not installed, no action is needed."
    return
}

Write-Host "Found uninstaller: $uninstallerPath"

if ($DryRun) {
    Write-Host "[DryRun] RigMD uninstaller is ready at: $uninstallerPath"
    Write-Host "[DryRun] Would execute uninstaller (Silent=$Silent, Force=$Force)."
    return
}

# Stop running processes if Force is enabled
$rigProcesses = @("RigMD.Desktop", "RigMD.Api", "RigMD.Agent")
foreach ($procName in $rigProcesses) {
    $running = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($running) {
        if ($Force) {
            Write-Host "Terminating running process: $procName..."
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
        } else {
            Write-Host "Notice: $procName is currently running. The uninstaller will prompt or shut it down."
        }
    }
}

# Build arguments
$argsList = @()
if ($Silent) {
    $argsList += "/VERYSILENT"
    $argsList += "/NORESTART"
    Write-Host "Running uninstaller silently..."
    $process = Start-Process -FilePath $uninstallerPath -ArgumentList $argsList -Wait -PassThru
    if ($process.ExitCode -eq 0) {
        Write-Host "RigMD uninstallation finished successfully."
    } else {
        Write-Warning "Uninstaller exited with code $($process.ExitCode)."
    }
} else {
    Write-Host "Launching RigMD uninstaller wizard..."
    if ($argsList.Count -gt 0) {
        Start-Process -FilePath $uninstallerPath -ArgumentList $argsList
    } else {
        Start-Process -FilePath $uninstallerPath
    }
    Write-Host "Follow the prompts in the uninstaller wizard."
}
Write-Host ""
