param([switch]$IncludeNativeJunctionTest, [switch]$SkipNativeJunctionTest)
$ErrorActionPreference = 'Stop'
if ($IncludeNativeJunctionTest -and $SkipNativeJunctionTest) { throw 'Choose either inclusion or explicit exclusion, not both.' }
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -Raw -LiteralPath (Join-Path $repo 'frontend\release.json') | ConvertFrom-Json).version
$artifactRoot = Join-Path $repo ('artifacts\validation\' + [guid]::NewGuid().ToString('N'))
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, [scriptblock]$work) {
    & $work
    $code = $LASTEXITCODE
    $results.Add([ordered]@{ check=$name; exitCode=$code; passed=($code -eq 0) })
    if ($code -ne 0) { throw "$name failed. See the preceding output." }
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
try {
    Push-Location (Join-Path $repo 'frontend')
    try {
        Check 'Frontend tests and release-version consistency' { & npm.cmd test }
        Check 'TypeScript' { & .\node_modules\.bin\tsc.cmd --noEmit -p tsconfig.json }
        Check 'ESLint' { & npm.cmd run lint }
        Check 'Frontend production build' { & npm.cmd run build }
    } finally { Pop-Location }
    Push-Location (Join-Path $repo 'backend-dotnet')
    try {
        if (-not $SkipNativeJunctionTest) {
            Check 'Backend tests including native junction setup' { & dotnet test RigMD.Tests\RigMD.Tests.csproj -c Release --artifacts-path $artifactRoot }
        } else {
            Check 'Backend tests excluding environment-blocked junction setup' { & dotnet test RigMD.Tests\RigMD.Tests.csproj -c Release --artifacts-path $artifactRoot --filter 'FullyQualifiedName!~RootAndAncestorJunctionsAreRejectedAndChildJunctionsAreSkipped' }
        }
        Check 'Desktop Release build' { & dotnet build RigMD.Desktop\RigMD.Desktop.csproj -c Release --artifacts-path $artifactRoot }
    } finally { Pop-Location }
} finally {
    [ordered]@{ version=$version; dateUtc=[DateTimeOffset]::UtcNow.ToString('O'); nativeJunctionIncluded=(-not [bool]$SkipNativeJunctionTest); checks=$results.ToArray(); installedAcceptanceComplete=$false } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactRoot 'validation-results.json') -Encoding UTF8
    Write-Host "Validation evidence: $artifactRoot"
}
