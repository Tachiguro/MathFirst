namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
using System.Threading;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Authoritative headless combat simulator engine core.
/// Delegates single-step combat turn transitions directly to the production <see cref="CyberDefenseStateMachine"/>.
/// </summary>
public static class HeadlessCombatSimulator
{
    /// <summary>
    /// Executes a single authoritative combat turn transition.
    /// </summary>
    /// <param name="run">The current valid run state.</param>
    /// <param name="isCorrect">Whether the answer was mathematically correct.</param>
    /// <param name="effectiveAttackDamage">The effective attack damage value.</param>
    /// <returns>The authoritative combat transition result.</returns>
    public static CyberDefenseCombatTransitionResult Step(
        CyberDefenseRunState run,
        bool isCorrect,
        int effectiveAttackDamage)
    {
        return CyberDefenseStateMachine.ApplyAttempt(run, isCorrect, effectiveAttackDamage);
    }

    /// <summary>
    /// Alias for <see cref="Step"/> aligning directly with domain state machine terminology.
    /// </summary>
    /// <param name="run">The current valid run state.</param>
    /// <param name="isCorrect">Whether the answer was mathematically correct.</param>
    /// <param name="effectiveAttackDamage">The effective attack damage value.</param>
    /// <returns>The authoritative combat transition result.</returns>
    public static CyberDefenseCombatTransitionResult ApplyAttempt(
        CyberDefenseRunState run,
        bool isCorrect,
        int effectiveAttackDamage)
    {
        return Step(run, isCorrect, effectiveAttackDamage);
    }

    /// <summary>
    /// Executes a bounded, deterministic multi-turn combat simulation.
    /// </summary>
    /// <param name="config">The validated simulation configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The deterministic simulation execution result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public static SimulationExecutionResult Run(
        SimulationRunConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var clock = new VirtualSimulationClock();
        var sampler = new SyntheticAttemptSampler(config.Profile, config.MasterSeed);
        CyberDefenseRunState currentState = config.StartingState;
        CyberDefenseTerminalRunSnapshot? lastTerminalSnapshot = null;
        long totalExecutedTurns = 0;
        int totalGameOvers = 0;

        // Precedence 1: Honor already-requested cancellation before first turn.
        if (cancellationToken.IsCancellationRequested)
        {
            return new SimulationExecutionResult(
                currentState,
                SimulationTerminationReason.Cancelled,
                totalExecutedTurns,
                totalGameOvers,
                clock.ElapsedMilliseconds,
                lastTerminalSnapshot);
        }

        // Precedence 2: If starting state already satisfies target sector, stop with zero turns.
        if (config.TargetSector.HasValue && currentState.Sector >= config.TargetSector.Value)
        {
            return new SimulationExecutionResult(
                currentState,
                SimulationTerminationReason.TargetSectorReached,
                totalExecutedTurns,
                totalGameOvers,
                clock.ElapsedMilliseconds,
                lastTerminalSnapshot);
        }

        // Main execution loop: bounded strictly by MaxTurns.
        while (totalExecutedTurns < config.MaxTurns)
        {
            // Precedence 3: Honor requested cancellation before every subsequent turn.
            if (cancellationToken.IsCancellationRequested)
            {
                return new SimulationExecutionResult(
                    currentState,
                    SimulationTerminationReason.Cancelled,
                    totalExecutedTurns,
                    totalGameOvers,
                    clock.ElapsedMilliseconds,
                    lastTerminalSnapshot);
            }

            // Step 6 & 7: Repeatedly sample one synthetic attempt and pass to authoritative transition.
            SyntheticAttemptResult attempt = sampler.SampleAttempt();
            CyberDefenseCombatTransitionResult transition = Step(
                currentState,
                attempt.IsCorrect,
                config.Profile.EffectiveAttackDamage);

            // Step 9 & 10: Advance virtual clock and increment executed turns.
            clock.Advance(attempt.SyntheticLatencyMilliseconds);
            totalExecutedTurns++;

            // Step 11: If GameOver, increment counter and retain authoritative terminal snapshot.
            if (transition.IsGameOver)
            {
                totalGameOvers++;
                lastTerminalSnapshot = transition.TerminalSnapshot;
            }

            // Step 12: Assign current state from authoritative transition NextState.
            currentState = transition.NextState;

            // Optional turn notification for test hooks.
            config.OnTurnCompleted?.Invoke(transition);

            // Precedence 6: If maximum game-over count is reached, stop with MaxGameOversReached.
            if (config.MaxGameOvers.HasValue && totalGameOvers >= config.MaxGameOvers.Value)
            {
                return new SimulationExecutionResult(
                    currentState,
                    SimulationTerminationReason.MaxGameOversReached,
                    totalExecutedTurns,
                    totalGameOvers,
                    clock.ElapsedMilliseconds,
                    lastTerminalSnapshot);
            }

            // Precedence 7: If target sector is reached, stop with TargetSectorReached.
            if (config.TargetSector.HasValue && currentState.Sector >= config.TargetSector.Value)
            {
                return new SimulationExecutionResult(
                    currentState,
                    SimulationTerminationReason.TargetSectorReached,
                    totalExecutedTurns,
                    totalGameOvers,
                    clock.ElapsedMilliseconds,
                    lastTerminalSnapshot);
            }

            // Precedence 8: If maximum turn count is reached, stop with MaxTurnsReached.
            if (totalExecutedTurns >= config.MaxTurns)
            {
                return new SimulationExecutionResult(
                    currentState,
                    SimulationTerminationReason.MaxTurnsReached,
                    totalExecutedTurns,
                    totalGameOvers,
                    clock.ElapsedMilliseconds,
                    lastTerminalSnapshot);
            }
        }

        return new SimulationExecutionResult(
            currentState,
            SimulationTerminationReason.MaxTurnsReached,
            totalExecutedTurns,
            totalGameOvers,
            clock.ElapsedMilliseconds,
            lastTerminalSnapshot);
    }
}
