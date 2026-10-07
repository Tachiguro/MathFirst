namespace MathFirst.App.Services;

using System.IO;
using MathFirst.Application.Telemetry;

public sealed class MauiTelemetryShareCacheCleaner : ITelemetryShareCacheCleaner
{
    public void PurgeShareCache()
    {
        var directory = TelemetryShareCachePaths.DirectoryPath;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
