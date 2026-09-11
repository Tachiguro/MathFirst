namespace MathFirst.ReleaseTool;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

public sealed record SignerInfo(
    string CertificateSha256,
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsAndroidDebug);

public sealed class JarSignatureInspector(
    IProcessRunner processRunner,
    IValidationToolLocator toolLocator)
{
    private static readonly Regex PemCertPattern = new(
        "-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public void VerifySignature(string aabPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aabPath);
        if (!File.Exists(aabPath))
        {
            throw new ReleaseToolException($"AAB file not found for signature verification: '{aabPath}'.");
        }

        var jarsigner = toolLocator.ResolveJarsigner();
        var invocation = new ProcessInvocation(
            jarsigner,
            ["-verify", "-strict", aabPath],
            Path.GetDirectoryName(aabPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new ReleaseToolException($"Cryptographic signature verification failed (exit code {result.ExitCode}): {error.Trim()}");
        }

        var output = result.StandardOutput;
        if (output.Contains("jar is unsigned.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("Cryptographic signature verification failed: the bundle is unsigned.");
        }

        if (!output.Contains("jar verified.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("Cryptographic signature verification failed: jarsigner did not report 'jar verified.'");
        }
    }

    public IReadOnlyList<SignerInfo> ExtractSigners(string aabPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aabPath);
        if (!File.Exists(aabPath))
        {
            throw new ReleaseToolException($"AAB file not found for certificate extraction: '{aabPath}'.");
        }

        var keytool = toolLocator.ResolveKeytool();
        var invocation = new ProcessInvocation(
            keytool,
            ["-printcert", "-jarfile", aabPath, "-rfc"],
            Path.GetDirectoryName(aabPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        result.EnsureSuccess("keytool -printcert");

        var signers = new List<SignerInfo>();
        var matches = PemCertPattern.Matches(result.StandardOutput);
        foreach (Match match in matches)
        {
            var pemBlock = match.Value;
            try
            {
                using var cert = X509Certificate2.CreateFromPem(pemBlock);
                var sha256 = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();
                var subject = cert.Subject;
                var issuer = cert.Issuer;
                var notBefore = cert.NotBefore.ToUniversalTime();
                var notAfter = cert.NotAfter.ToUniversalTime();
                var isDebug = subject.Contains("Android Debug", StringComparison.OrdinalIgnoreCase) ||
                              issuer.Contains("Android Debug", StringComparison.OrdinalIgnoreCase) ||
                              subject.Contains("CN=Android Debug", StringComparison.OrdinalIgnoreCase);

                signers.Add(new SignerInfo(sha256, subject, issuer, notBefore, notAfter, isDebug));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                throw new ReleaseToolException($"Failed to parse signer X.509 certificate from keytool output: {exception.Message}");
            }
        }

        return signers;
    }
}
