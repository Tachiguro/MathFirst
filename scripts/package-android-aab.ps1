<#
.SYNOPSIS
    Invokes the repository-native MathFirst Android release packaging tool.
#>
[CmdletBinding()]
param(
    [ValidateSet("SourceCandidate", "Distributable")]
    [string]$Profile = "SourceCandidate",

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommitSha,

    [AllowEmptyString()]
    [string]$DisplayVersion,

    [AllowEmptyString()]
    [string]$BuildNumber,

    [string]$KeystorePath,
    [string]$KeyAlias,
    [string]$StorePasswordFile,
    [string]$KeyPasswordFile,
    [string]$ExpectedSignerCertificateSha256
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "tools/MathFirst.ReleaseTool/MathFirst.ReleaseTool.csproj"

$toolArguments = [System.Collections.Generic.List[string]]::new()
$toolArguments.Add("run")
$toolArguments.Add("--project")
$toolArguments.Add($toolProject)
$toolArguments.Add("--")
$toolArguments.Add("android-package")
$toolArguments.Add("--profile")
$toolArguments.Add($Profile)
$toolArguments.Add("--expected-commit-sha")
$toolArguments.Add($ExpectedCommitSha)

$optionalArguments = [ordered]@{
    DisplayVersion                    = "--display-version"
    BuildNumber                       = "--build-number"
    KeystorePath                      = "--keystore-path"
    KeyAlias                          = "--key-alias"
    StorePasswordFile                 = "--store-password-file"
    KeyPasswordFile                   = "--key-password-file"
    ExpectedSignerCertificateSha256  = "--expected-signer-certificate-sha256"
}

foreach ($parameterName in $optionalArguments.Keys) {
    if ($PSBoundParameters.ContainsKey($parameterName)) {
        $toolArguments.Add($optionalArguments[$parameterName])
        $toolArguments.Add([string]$PSBoundParameters[$parameterName])
    }
}

& dotnet @toolArguments
exit $LASTEXITCODE
