namespace MathFirst.Application.Lifecycle;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;


public sealed class AppResetCoordinator : IAppResetCoordinator
{
    private readonly TrainingSession _session;
    private readonly IPreferenceStore _preferenceStore;
    private readonly IInstallationIdProvider _installationIdProvider;
    private readonly ITelemetryShareCacheCleaner _cacheCleaner;
    private readonly CyberDefenseSessionState _cyberDefenseSessionState;
    private readonly IGameplayStore? _gameplayStore;
    private readonly MathFirst.Application.Gameplay.ICyberDefenseCombatCoordinator? _combatCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppResetCoordinator(
        TrainingSession session,
        IPreferenceStore preferenceStore,
        IInstallationIdProvider installationIdProvider,
        ITelemetryShareCacheCleaner cacheCleaner,
        CyberDefenseSessionState cyberDefenseSessionState,
        IGameplayStore? gameplayStore = null,
        MathFirst.Application.Gameplay.ICyberDefenseCombatCoordinator? combatCoordinator = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        _installationIdProvider = installationIdProvider ?? throw new ArgumentNullException(nameof(installationIdProvider));
        _cacheCleaner = cacheCleaner ?? throw new ArgumentNullException(nameof(cacheCleaner));
        _cyberDefenseSessionState = cyberDefenseSessionState ?? throw new ArgumentNullException(nameof(cyberDefenseSessionState));
        _gameplayStore = gameplayStore;
        _combatCoordinator = combatCoordinator;
    }

    public async Task ExecuteFullResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _session.PauseItemTiming();

            GameplayResetIntentRecord? resetIntent = null;
            if (_gameplayStore != null)
            {
                await _gameplayStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
                resetIntent = await _gameplayStore.BeginOrGetResetIntentAsync(cancellationToken).ConfigureAwait(false);
            }

            await _session.ResetLearningProgressAsync(
                cancellationToken,
                startTiming: false)
                .ConfigureAwait(false);

            if (_gameplayStore != null && resetIntent != null)
            {
                await _gameplayStore.ResetGameplayStateAsync(
                    resetIntent.TargetEpoch,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            _preferenceStore.ResetAllPreferences();

            _combatCoordinator?.InvalidateState();
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

            if (_gameplayStore != null && resetIntent != null)
            {
                await _gameplayStore.ClearResetIntentAsync(
                    resetIntent.TargetEpoch,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReconcileStartupResetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _gameplayStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var resetIntent = await _gameplayStore.GetResetIntentAsync(cancellationToken).ConfigureAwait(false);
            if (!resetIntent.IsPending)
            {
                return false;
            }

            _session.PauseItemTiming();

            await _session.ResetLearningProgressAsync(
                cancellationToken,
                startTiming: false)
                .ConfigureAwait(false);

            await _gameplayStore.ResetGameplayStateAsync(
                resetIntent.TargetEpoch,
                cancellationToken)
                .ConfigureAwait(false);

            _preferenceStore.ResetAllPreferences();

            _combatCoordinator?.InvalidateState();
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

            await _gameplayStore.ClearResetIntentAsync(
                resetIntent.TargetEpoch,
                cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
