namespace MathFirst.Application.Telemetry;

public sealed record TelemetryExportResult(
    bool Success,
    string? FilePath = null,
    string? ErrorMessage = null)
{
    public static TelemetryExportResult Succeeded(string filePath) =>
        new(true, filePath, null);

    public static TelemetryExportResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
