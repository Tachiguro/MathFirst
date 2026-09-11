<#
.SYNOPSIS
    Validates a generated Android App Bundle (.aab) offline using MathFirst.ReleaseTool.
.DESCRIPTION
    Thin orchestration wrapper delegating authoritative structural, manifest, DEX,
    signature, signer certificate, and provenance validation to MathFirst.ReleaseTool.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$AabPath,

    [Parameter(Mandatory=$true)]
    [string]$ProvenancePath,

    [Parameter(Mandatory=$true)]
    [string]$ExpectedCommitSha,

    [Parameter(Mandatory=$true)]
    [ValidateSet("SourceCandidate", "Distributable")]
    [string]$Profile,

    [string]$ExpectedDisplayVersion,

    [int]$ExpectedBuildNumber = 0,

    [string]$ExpectedSignerCertificateSha256,

    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$resolvedRepoRoot = if ($RepositoryRoot) {
    [System.IO.Path]::GetFullPath($RepositoryRoot)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory ".."))
}

$releaseToolProject = Join-Path $resolvedRepoRoot "tools\MathFirst.ReleaseTool\MathFirst.ReleaseTool.csproj"
if (-not (Test-Path $releaseToolProject)) {
    throw "ReleaseTool project was not found at '$releaseToolProject'."
}

$toolArgs = @(
    "run",
    "--project", $releaseToolProject,
    "--no-build",
    "-c", "Release",
    "--",
    "android-validate",
    "--aab-path", [System.IO.Path]::GetFullPath($AabPath),
    "--provenance-path", [System.IO.Path]::GetFullPath($ProvenancePath),
    "--expected-commit-sha", $ExpectedCommitSha,
    "--profile", $Profile
)

if ($ExpectedDisplayVersion) {
    $toolArgs += @("--display-version", $ExpectedDisplayVersion)
}

if ($ExpectedBuildNumber -gt 0) {
    $toolArgs += @("--build-number", $ExpectedBuildNumber.ToString())
}

if ($ExpectedSignerCertificateSha256) {
    $toolArgs += @("--expected-signer-certificate-sha256", $ExpectedSignerCertificateSha256)
}

if ($RepositoryRoot) {
    $toolArgs += @("--repository-root", $resolvedRepoRoot)
}

& dotnet $toolArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
