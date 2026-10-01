namespace MathFirst.Application.Telemetry;

public interface IAppBuildInfo
{
    string DisplayVersion { get; }
    string BuildClassification { get; }
}
