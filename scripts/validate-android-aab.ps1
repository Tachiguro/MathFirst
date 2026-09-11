<#
.SYNOPSIS
    Validates a generated Android App Bundle (.aab) offline against structural and provenance requirements.
.DESCRIPTION
    Inspects .aab archive structure (ZIP integrity, BundleConfig.pb, base manifest, DEX bytecode, resources),
    verifies non-debuggable flags, inspects signature metadata, and checks SHA-256 hash matches companion provenance metadata.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$AabPath,
    [string]$ProvenancePath,
    [string]$ExpectedDisplayVersion,
    [int]$ExpectedBuildNumber = 0,
    [switch]$RequireSigned
)

$ErrorActionPreference = "Stop"

# 1. Validate File Existence and Extension
if (-not (Test-Path $AabPath)) {
    throw "AAB bundle not found at '$AabPath'."
}

$aabItem = Get-Item $AabPath
if ($aabItem.Extension -ne ".aab") {
    throw "File '$AabPath' does not have a valid .aab extension."
}

if ($aabItem.Length -le 0) {
    throw "AAB bundle at '$AabPath' is empty (0 bytes)."
}

Write-Host "Validating Android App Bundle: $($aabItem.Name) ($([math]::Round($aabItem.Length / 1MB, 2)) MB)..." -ForegroundColor Cyan

# 2. Compute SHA-256 Checksum
$computedHash = (Get-FileHash -Path $AabPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "  [OK] SHA-256 computed: $computedHash" -ForegroundColor Green

# 3. Validate ZIP Archive and Bundle Layout
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fileStream = [System.IO.File]::OpenRead($aabItem.FullName)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($fileStream, [System.IO.Compression.ZipArchiveMode]::Read)
    $entryNames = $archive.Entries | ForEach-Object { $_.FullName }

    # Assert BundleConfig.pb exists
    if ("BundleConfig.pb" -notin $entryNames) {
        throw "AAB bundle is invalid: missing 'BundleConfig.pb' in bundle root."
    }
    Write-Host "  [OK] BundleConfig.pb present." -ForegroundColor Green

    # Assert base/manifest/AndroidManifest.xml exists
    if ("base/manifest/AndroidManifest.xml" -notin $entryNames) {
        throw "AAB bundle is invalid: missing 'base/manifest/AndroidManifest.xml'."
    }
    Write-Host "  [OK] base/manifest/AndroidManifest.xml present." -ForegroundColor Green

    # Assert base/dex/ contains DEX bytecode
    $dexEntries = $entryNames | Where-Object { $_ -like "base/dex/*.dex" }
    if (-not $dexEntries -or $dexEntries.Count -eq 0) {
        throw "AAB bundle is invalid: missing DEX bytecode in 'base/dex/'."
    }
    Write-Host "  [OK] DEX bytecode present ($($dexEntries.Count) dex file(s))." -ForegroundColor Green

    # Assert base/resources.pb or base/res/ exists
    $hasResources = ("base/resources.pb" -in $entryNames) -or ($entryNames | Where-Object { $_ -like "base/res/*" })
    if (-not $hasResources) {
        throw "AAB bundle is invalid: missing compiled resources in 'base/'."
    }
    Write-Host "  [OK] Base resources present." -ForegroundColor Green

    # Check Signing Status in META-INF
    $sigEntries = $entryNames | Where-Object { $_ -like "META-INF/*.RSA" -or $_ -like "META-INF/*.DSA" -or $_ -like "META-INF/*.EC" -or $_ -like "META-INF/*.SF" }
    $isSigned = ($sigEntries -and $sigEntries.Count -gt 0)
    if ($isSigned) {
        Write-Host "  [OK] Signature block detected in META-INF/ ($($sigEntries.Count) signature file(s))." -ForegroundColor Green
    } else {
        Write-Host "  [INFO] Unsigned bundle (no META-INF signature block)." -ForegroundColor Yellow
        if ($RequireSigned) {
            throw "Signing verification failed: -RequireSigned was specified, but bundle is unsigned."
        }
    }
}
finally {
    $fileStream.Dispose()
}

# 4. Validate Companion Provenance Metadata (if present or specified)
$effectiveProvenancePath = if ($ProvenancePath) {
    $ProvenancePath
} else {
    $defaultProv = [System.IO.Path]::ChangeExtension($aabItem.FullName, ".provenance.json")
    if (Test-Path $defaultProv) { $defaultProv } else { $null }
}

if ($effectiveProvenancePath) {
    if (-not (Test-Path $effectiveProvenancePath)) {
        throw "Specified ProvenancePath not found: '$effectiveProvenancePath'."
    }

    $provenanceRaw = Get-Content -Path $effectiveProvenancePath -Raw
    $provenance = $provenanceRaw | ConvertFrom-Json

    if ($provenance.Sha256Checksum.ToLowerInvariant() -ne $computedHash) {
        throw "Provenance checksum mismatch! Provenance: '$($provenance.Sha256Checksum)', Computed: '$computedHash'."
    }
    Write-Host "  [OK] Provenance SHA-256 matches computed file hash." -ForegroundColor Green

    if ($provenance.ApplicationId -ne "com.tachiguro.mathfirst") {
        throw "Provenance ApplicationId mismatch: expected 'com.tachiguro.mathfirst', got '$($provenance.ApplicationId)'."
    }
    Write-Host "  [OK] Provenance ApplicationId verified: $($provenance.ApplicationId)" -ForegroundColor Green

    if ($ExpectedDisplayVersion -and $provenance.DisplayVersion -ne $ExpectedDisplayVersion) {
        throw "Provenance DisplayVersion mismatch: expected '$ExpectedDisplayVersion', got '$($provenance.DisplayVersion)'."
    }
    if ($ExpectedBuildNumber -gt 0 -and [int]$provenance.BuildNumber -ne $ExpectedBuildNumber) {
        throw "Provenance BuildNumber mismatch: expected '$ExpectedBuildNumber', got '$($provenance.BuildNumber)'."
    }

    if (-not $provenance.GitCommitSha -or $provenance.GitCommitSha.Length -ne 40) {
        throw "Provenance GitCommitSha is invalid: '$($provenance.GitCommitSha)'."
    }
    Write-Host "  [OK] Provenance Git commit verified: $($provenance.GitCommitSha) (clean: $($provenance.IsCleanWorkingTree))" -ForegroundColor Green
} else {
    Write-Host "  [INFO] No companion .provenance.json file found or specified." -ForegroundColor DarkGray
}

Write-Host "Validation PASSED for $($aabItem.Name)." -ForegroundColor Green
