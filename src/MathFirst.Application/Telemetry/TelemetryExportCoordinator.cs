namespace MathFirst.Application.Telemetry;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;

public sealed class TelemetryExportCoordinator
{
    private const string ShareTitle = "MathFirst Telemetry Export";

    private readonly ILearnerStore _learnerStore;
    private readonly IInstallationIdProvider _installationIdProvider;
    private readonly IAppBuildInfo _appBuildInfo;
    private readonly ITelemetryJsonSerializer _jsonSerializer;
    private readonly ITelemetryShareService _shareService;
    private readonly TimeProvider _timeProvider;

    public TelemetryExportCoordinator(
        ILearnerStore learnerStore,
        IInstallationIdProvider installationIdProvider,
        IAppBuildInfo appBuildInfo,
        ITelemetryJsonSerializer jsonSerializer,
        ITelemetryShareService shareService,
        TimeProvider timeProvider)
    {
        _learnerStore = learnerStore ?? throw new ArgumentNullException(nameof(learnerStore));
        _installationIdProvider = installationIdProvider ?? throw new ArgumentNullException(nameof(installationIdProvider));
        _appBuildInfo = appBuildInfo ?? throw new ArgumentNullException(nameof(appBuildInfo));
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        _shareService = shareService ?? throw new ArgumentNullException(nameof(shareService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<TelemetryExportResult> ExportAndShareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var attempts = await _learnerStore.LoadCompleteAttemptTelemetryAsync(cancellationToken).ConfigureAwait(false);
            var installationId = _installationIdProvider.GetOrCreateInstallationId();
            var now = _timeProvider.GetUtcNow();

            using var memoryStream = new MemoryStream();
            await _jsonSerializer.SerializeAsync(
                memoryStream,
                installationId,
                _appBuildInfo.DisplayVersion,
                _appBuildInfo.BuildClassification,
                now,
                attempts,
                cancellationToken).ConfigureAwait(false);

            memoryStream.Position = 0;

            var timestamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"mathfirst-telemetry-{timestamp}-{Guid.NewGuid():N}.json";

            var filePath = await _shareService.PrepareShareFileAsync(
                fileName,
                memoryStream,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            await _shareService.DispatchSystemShareAsync(
                filePath,
                ShareTitle).ConfigureAwait(false);

            return TelemetryExportResult.Succeeded(filePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TelemetryExportResult.Failed(ex.Message);
        }
    }
}
