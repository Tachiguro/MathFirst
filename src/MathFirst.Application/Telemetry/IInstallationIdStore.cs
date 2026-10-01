namespace MathFirst.Application.Telemetry;

public interface IInstallationIdStore
{
    string? Get();
    void Set(string value);
    void Clear();
}
