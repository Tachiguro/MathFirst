namespace MathFirst.Application.Telemetry;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface ITelemetryShareService
{
    Task<string> PrepareShareFileAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task DispatchSystemShareAsync(
        string filePath,
        string title);

    void PurgeShareCache();
}
