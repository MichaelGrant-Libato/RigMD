[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot 'dependencies'
New-Item -ItemType Directory -Path $directory -Force | Out-Null

# Keep matching upstream source available with the binary distribution.
# PawnPP is the submodule pinned by PawnIO 2.2.0; the other Pawn sources
# and build files are already present in the driver archive.
$sources = @(
    @{
        File = 'LibreHardwareMonitor-0.9.6-source.zip'
        Repository = 'LibreHardwareMonitor/LibreHardwareMonitor'
        Revision = '3d331e3370efb858411f19511373eff65a218701'
        Sha256 = '45DDAED7D4A8C74C5DC6613B3308E590B4B5301E595C36268F76E30E68879696'
    },
    @{
        File = 'PawnIO-2.2.0-source.zip'
        Repository = 'namazso/PawnIO'
        Revision = '5cdf470831fdfff3f7f1d06363ca6b230f3bf35a'
        Sha256 = '73AC1A94D48F74552B0E9ACFBC7714E12BEF3A2E91B75F27F863F878B7546B19'
    },
    @{
        File = 'PawnPP-source.zip'
        Repository = 'namazso/PawnPP'
        Revision = 'e64e4c37b2d8ba0d8ee57205faf8183aee12c438'
        Sha256 = '8A97A62DB80459618BF3EAC2C1C8CF65B91FB3BC17E2A226E4B3B1A1E4D3BF6C'
    },
    @{
        File = 'PawnIO-Modules-0.1.6-source.zip'
        Repository = 'namazso/PawnIO.Modules'
        Revision = '4aa792beb020a14c8261072e9786d2dfb38489d9'
        Sha256 = 'CBD0BECD1058881704F278A0C5467F61C2A7B07383E3447AAFD72078F427C468'
    }
)

foreach ($source in $sources) {
    $destination = Join-Path $directory $source.File
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        $download = Join-Path $directory ([guid]::NewGuid().ToString('N') + '.download')
        try {
            $uri = 'https://api.github.com/repos/' + $source.Repository + '/zipball/' + $source.Revision
            Invoke-WebRequest -Uri $uri -OutFile $download -UseBasicParsing -TimeoutSec 60
            if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $source.Sha256) {
                throw "Source archive verification failed: $($source.File)"
            }
            Move-Item -LiteralPath $download -Destination $destination
        }
        finally {
            if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
        }
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $source.Sha256) {
        throw "Source archive verification failed: $($source.File)"
    }
    Write-Output $destination
}
