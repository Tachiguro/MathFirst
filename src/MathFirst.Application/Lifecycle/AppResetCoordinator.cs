namespace MathFirst.Application.Lifecycle;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;

public sealed class AppResetCoordinator : IAppResetCoordinator
{
    private readonly TrainingSession _session;
    private readonly IPreferenceStore _preferenceStore;
    private readonly IInstallationIdProvider _installationIdProvider;
    private readonly ITelemetryShareCacheCleaner _cacheCleaner;
    private readonly CyberDefenseSessionState _cyberDefenseSessionState;

    public AppResetCoordinator(
        TrainingSession session,
        IPreferenceStore preferenceStore,
        IInstallationIdProvider installationIdProvider,
        ITelemetryShareCacheCleaner cacheCleaner,
        CyberDefenseSessionState cyberDefenseSessionState)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        _installationIdProvider = installationIdProvider ?? throw new ArgumentNullException(nameof(installationIdProvider));
        _cacheCleaner = cacheCleaner ?? throw new ArgumentNullException(nameof(cacheCleaner));
        _cyberDefenseSessionState = cyberDefenseSessionState ?? throw new ArgumentNullException(nameof(cyberDefenseSessionState));
    }

    public async Task ExecuteFullResetAsync(CancellationToken cancellationToken = default)
    {
        _session.PauseItemTiming();

        await _session.ResetLearningProgressAsync(
            cancellationToken,
            startTiming: false)
            .ConfigureAwait(false);

        _preferenceStore.ResetAllPreferences();

        _cyberDefenseSessionState.ClearEncounter();

        _installationIdProvider.ClearInstallationId();

        try
        {
            _cacheCleaner.PurgeShareCache();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
