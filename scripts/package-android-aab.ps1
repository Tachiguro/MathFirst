<#
.SYNOPSIS
    Packages MathFirst as an Android App Bundle (.aab) with exact-candidate provenance tracking.
.DESCRIPTION
    Builds and packages the MathFirst .NET MAUI Android application in Release configuration,
    enforcing clean working-tree checks, fail-closed parameter validation, externalized signing
    protocols, deterministic artifact naming, and companion JSON provenance generation.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DisplayVersion,
    [int]$BuildNumber = 0,
    [switch]$Sign,
    [string]$KeystorePath = $env:MATHFIRST_ANDROID_KEYSTORE_PATH,
    [string]$KeyAlias = $env:MATHFIRST_ANDROID_KEY_ALIAS,
    [string]$StorePassword = $env:MATHFIRST_ANDROID_STORE_PASS,
    [string]$KeyPassword = $env:MATHFIRST_ANDROID_KEY_PASS,
    [string]$OutputDir = "artifacts/android",
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

# 1. Verify Repository Root and Project
$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    throw "Must be executed within the MathFirst Git repository."
}
Set-Location $repoRoot

$projectPath = "src/MathFirst.App/MathFirst.App.csproj"
if (-not (Test-Path $projectPath)) {
    throw "Could not locate project file at '$projectPath'."
}

# 2. Validate Configuration
if ($Configuration -notin @("Release", "Debug")) {
    throw "Invalid Configuration '$Configuration'. Must be 'Release' or 'Debug'."
}

# 3. Read and Validate Project Identity & Version Defaults
[xml]$projectXml = Get-Content -Path $projectPath
$projDisplayVersion = $projectXml.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$projBuildNumber = $projectXml.Project.PropertyGroup.ApplicationVersion | Where-Object { $_ } | Select-Object -First 1
$appTitle = $projectXml.Project.PropertyGroup.ApplicationTitle | Where-Object { $_ } | Select-Object -First 1
$appId = $projectXml.Project.PropertyGroup.ApplicationId | Where-Object { $_ } | Select-Object -First 1

$effectiveDisplayVersion = if ($DisplayVersion) { $DisplayVersion } else { $projDisplayVersion }
$effectiveBuildNumber = if ($BuildNumber -gt 0) { $BuildNumber } else { [int]$projBuildNumber }

$versionRegex = '^\d+(\.\d+)+$'
if (-not ($effectiveDisplayVersion -match $versionRegex)) {
    throw "Invalid DisplayVersion '$effectiveDisplayVersion'. Must follow semantic numeric format (e.g. '1.0' or '1.0.0')."
}
if ($effectiveBuildNumber -le 0) {
    throw "Invalid BuildNumber '$effectiveBuildNumber'. Must be a positive integer."
}

# 4. Check Git Working Tree and Provenance State
$gitStatus = git status --porcelain=v1
$isClean = [string]::IsNullOrWhiteSpace($gitStatus)
if (-not $isClean -and -not $AllowDirty) {
    throw "Working tree contains uncommitted changes. Packaging aborted to guarantee exact provenance. Use -AllowDirty for development testing."
}

$commitSha = (git rev-parse HEAD).Trim()
$shortSha = (git rev-parse --short=7 HEAD).Trim()
$branchName = (git rev-parse --abbrev-ref HEAD).Trim()

# 5. Validate Signing Credentials (if -Sign requested)
if ($Sign) {
    if (-not $KeystorePath -or -not (Test-Path $KeystorePath)) {
        throw "Signing requested (-Sign), but KeystorePath is missing or invalid: '$KeystorePath'."
    }
    if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
        throw "Signing requested (-Sign), but KeyAlias is missing."
    }
    if ([string]::IsNullOrWhiteSpace($StorePassword)) {
        throw "Signing requested (-Sign), but StorePassword is missing."
    }
    if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
        throw "Signing requested (-Sign), but KeyPassword is missing."
    }
}

# 6. Ensure Output Directory Exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# 7. Assemble dotnet publish Arguments
$publishArgs = @(
    "publish",
    $projectPath,
    "-f", "net10.0-android",
    "-c", $Configuration,
    "-p:AndroidPackageFormat=aab"
)

if ($DisplayVersion) {
    $publishArgs += "-p:ApplicationDisplayVersion=$effectiveDisplayVersion"
}
if ($BuildNumber -gt 0) {
    $publishArgs += "-p:ApplicationVersion=$effectiveBuildNumber"
}

if ($Sign) {
    $publishArgs += "-p:AndroidKeyStore=true"
    $publishArgs += "-p:AndroidSigningKeyStore=$KeystorePath"
    $publishArgs += "-p:AndroidSigningStorePass=$StorePassword"
    $publishArgs += "-p:AndroidSigningKeyAlias=$KeyAlias"
    $publishArgs += "-p:AndroidSigningKeyPass=$KeyPassword"
} else {
    $publishArgs += "-p:AndroidKeyStore=false"
}

Write-Host "Packaging Android App Bundle ($Configuration) for $appId v$effectiveDisplayVersion (b$effectiveBuildNumber)..." -ForegroundColor Cyan
Write-Host "Commit: $commitSha ($branchName)" -ForegroundColor DarkGray

# 8. Execute Publish
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# 9. Locate Generated AAB
$searchDir = "src/MathFirst.App/bin/$Configuration/net10.0-android"
$candidateAabs = Get-ChildItem -Path $searchDir -Filter "*.aab" -Recurse | Sort-Object LastWriteTime -Descending
if (-not $candidateAabs -or $candidateAabs.Count -eq 0) {
    throw "Could not locate generated .aab bundle under '$searchDir'."
}
$sourceAab = $candidateAabs[0]

# 10. Copy to Output Directory with Deterministic Name
$artifactBaseName = "MathFirst-v$effectiveDisplayVersion-b$effectiveBuildNumber-$shortSha-$Configuration"
$destinationAabPath = Join-Path $OutputDir "$artifactBaseName.aab"
Copy-Item -Path $sourceAab.FullName -Destination $destinationAabPath -Force

# 11. Compute Checksum and Generate Provenance Metadata
$fileHash = (Get-FileHash -Path $destinationAabPath -Algorithm SHA256).Hash.ToLowerInvariant()
$fileSize = (Get-Item $destinationAabPath).Length

$provenance = [ordered]@{
    ApplicationTitle   = $appTitle
    ApplicationId      = $appId
    DisplayVersion     = $effectiveDisplayVersion
    BuildNumber        = [int]$effectiveBuildNumber
    GitCommitSha       = $commitSha
    GitBranch          = $branchName
    IsCleanWorkingTree = $isClean
    BuildTimestampUtc  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    Configuration      = $Configuration
    TargetFramework    = "net10.0-android"
    IsSigned           = [bool]$Sign
    Sha256Checksum     = $fileHash
}

$provenanceJsonPath = Join-Path $OutputDir "$artifactBaseName.provenance.json"
$provenanceJson = $provenance | ConvertTo-Json -Depth 4
Set-Content -Path $provenanceJsonPath -Value $provenanceJson -Encoding utf8

Write-Host "Android App Bundle successfully generated:" -ForegroundColor Green
Write-Host "  Artifact:   $destinationAabPath ($([math]::Round($fileSize / 1MB, 2)) MB)" -ForegroundColor White
Write-Host "  Provenance: $provenanceJsonPath" -ForegroundColor White
Write-Host "  SHA-256:    $fileHash" -ForegroundColor DarkGray
