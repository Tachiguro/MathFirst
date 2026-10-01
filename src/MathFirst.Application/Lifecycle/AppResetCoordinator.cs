namespace MathFirst.Application.Lifecycle;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Telemetry;

public sealed class AppResetCoordinator : IAppResetCoordinator
{
    private readonly TrainingSession _session;
    private readonly IPreferenceStore _preferenceStore;
    private readonly IInstallationIdProvider _installationIdProvider;
    private readonly ITelemetryShareService _shareService;

    public AppResetCoordinator(
        TrainingSession session,
        IPreferenceStore preferenceStore,
        IInstallationIdProvider installationIdProvider,
        ITelemetryShareService shareService)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        _installationIdProvider = installationIdProvider ?? throw new ArgumentNullException(nameof(installationIdProvider));
        _shareService = shareService ?? throw new ArgumentNullException(nameof(shareService));
    }

    public async Task ExecuteFullResetAsync(CancellationToken cancellationToken = default)
    {
        _session.PauseItemTiming();

        await _session.ResetLearningProgressAsync(
            cancellationToken,
            startTiming: false)
            .ConfigureAwait(false);

        _preferenceStore.ResetAllPreferences();

        _installationIdProvider.ClearInstallationId();

        try
        {
            _shareService.PurgeShareCache();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
