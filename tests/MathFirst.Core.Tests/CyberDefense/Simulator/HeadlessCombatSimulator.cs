namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;
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
}
