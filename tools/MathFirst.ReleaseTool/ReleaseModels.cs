namespace MathFirst.ReleaseTool;

public enum ReleaseProfile
{
    SourceCandidate,
    Distributable
}

public enum ArtifactValidationStatus
{
    NotValidated,
    ValidatorApproved
}

public sealed record VersionOverrides(string? DisplayVersion, string? BuildNumber);

public sealed record SigningInputs(
    string KeystorePath,
    string KeyAlias,
    string StorePasswordFile,
    string KeyPasswordFile,
    string ExpectedSignerCertificateSha256);

public sealed record PackageRequest(
    ReleaseProfile Profile,
    string ExpectedCommitSha,
    VersionOverrides VersionOverrides,
    SigningInputs? SigningInputs);

public sealed record RepositorySnapshot(
    string RepositoryRoot,
    string HeadSha,
    string Branch,
    bool TrackedWorkingTreeClean,
    bool IndexClean,
    IReadOnlyList<string> UntrackedFiles,
    string LocalMainSha,
    string OriginMainSha);

public sealed record EvaluatedProjectMetadata(
    string ApplicationTitle,
    string ApplicationId,
    string ApplicationDisplayVersion,
    string ApplicationVersion,
    string TargetFramework,
    string MinimumSdk,
    string TargetSdk,
    string AndroidNetSdkVersion);

public sealed record ValidatedBuildMetadata(
    string ApplicationTitle,
    string ApplicationId,
    string DisplayVersion,
    int BuildNumber,
    string TargetFramework,
    string MinimumSdk,
    string TargetSdk);

public sealed record ProcessInvocation(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record ArtifactProvenance(
    int SchemaVersion,
    ProvenanceArtifact Artifact,
    ProvenanceApplication Application,
    ProvenanceSource Source,
    ProvenanceBuild Build,
    ProvenanceSigning Signing,
    DateTimeOffset GeneratedAtUtc);

public sealed record ProvenanceArtifact(string FileName, string Classification, long SizeBytes, string Sha256);

public sealed record ProvenanceApplication(string Id, string DisplayVersion, int BuildNumber);

public sealed record ProvenanceSource(
    string ExpectedCommitSha,
    string CommitSha,
    string Ref,
    string Classification,
    bool WorkingTreeClean);

public sealed record ProvenanceBuild(
    string Configuration,
    string TargetFramework,
    string MinSdk,
    string TargetSdk,
    VersionOverrides RequestedOverrides,
    IReadOnlyDictionary<string, string> ToolVersions);

public sealed record ProvenanceSigning(
    string State,
    string? ExpectedCertificateSha256,
    string? CertificateSha256);

public interface IProcessRunner
{
    ProcessResult Run(ProcessInvocation invocation);
}

public sealed class ReleaseToolException(string message) : InvalidOperationException(message);

public static class ReleaseConstants
{
    public const string SourceCandidateBranch = "feat/mf-rel-001-android-aab-packaging";
    public const string TargetFramework = "net10.0-android36.0";
    public const string Configuration = "Release";
}
