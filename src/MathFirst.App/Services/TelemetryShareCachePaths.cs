namespace MathFirst.App.Services;

using System.IO;
using Microsoft.Maui.Storage;

internal static class TelemetryShareCachePaths
{
    public const string DirectoryName = "telemetry-share";

    public static string DirectoryPath => Path.Combine(FileSystem.CacheDirectory, DirectoryName);
}
