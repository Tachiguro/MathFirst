namespace MathFirst.Application.Telemetry;

public interface IInstallationIdProvider
{
    string GetOrCreateInstallationId();
    void ClearInstallationId();
}
