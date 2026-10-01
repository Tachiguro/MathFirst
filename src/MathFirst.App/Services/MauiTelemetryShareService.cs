namespace MathFirst.App.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Telemetry;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

public sealed class MauiTelemetryShareService : ITelemetryShareService
{
    private const string TelemetryShareDirectoryName = "telemetry-share";

    public async Task<string> PrepareShareFileAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.Combine(FileSystem.CacheDirectory, TelemetryShareDirectoryName);
        Directory.CreateDirectory(directory);

        var safeFileName = Path.GetFileName(fileName);
        var filePath = Path.Combine(directory, safeFileName);

        using (var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }

        return filePath;
    }

    public async Task DispatchSystemShareAsync(
        string filePath,
        string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath)
        }).ConfigureAwait(false);
    }

    public void PurgeShareCache()
    {
        var directory = Path.Combine(FileSystem.CacheDirectory, TelemetryShareDirectoryName);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
