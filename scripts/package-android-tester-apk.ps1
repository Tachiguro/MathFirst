<#
.SYNOPSIS
    Invokes the repository-native MathFirst Android tester APK release packaging tool.
.DESCRIPTION
    Thin orchestration wrapper delegating authoritative Tester APK packaging,
    identity projection, validation, evidence generation, and atomic promotion to MathFirst.ReleaseTool.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommitSha,

    [AllowEmptyString()]
    [string]$DisplayVersion,

    [AllowEmptyString()]
    [string]$BuildNumber
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "tools/MathFirst.ReleaseTool/MathFirst.ReleaseTool.csproj"

$toolArguments = [System.Collections.Generic.List[string]]::new()
$toolArguments.Add("run")
$toolArguments.Add("--project")
$toolArguments.Add($toolProject)
$toolArguments.Add("-c")
$toolArguments.Add("Release")
$toolArguments.Add("--")
$toolArguments.Add("android-package")
$toolArguments.Add("--profile")
$toolArguments.Add("Tester")
$toolArguments.Add("--expected-commit-sha")
$toolArguments.Add($ExpectedCommitSha)

$optionalArguments = [ordered]@{
    DisplayVersion = "--display-version"
    BuildNumber    = "--build-number"
}

foreach ($parameterName in $optionalArguments.Keys) {
    if ($PSBoundParameters.ContainsKey($parameterName) -and -not [string]::IsNullOrEmpty([string]$PSBoundParameters[$parameterName])) {
        $toolArguments.Add($optionalArguments[$parameterName])
        $toolArguments.Add([string]$PSBoundParameters[$parameterName])
    }
}

& dotnet @toolArguments
exit $LASTEXITCODE
