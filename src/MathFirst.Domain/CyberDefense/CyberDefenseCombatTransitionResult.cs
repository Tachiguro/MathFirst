namespace MathFirst.Domain.CyberDefense;

public sealed class CyberDefenseCombatTransitionResult : IEquatable<CyberDefenseCombatTransitionResult>
{
    public bool IsCorrect { get; }
    public int RequestedAttackDamage { get; }
    public int AppliedOpponentDamage { get; }
    public int ExcessOpponentDamage { get; }
    public int IncomingEnemyDamage { get; }
    public int AppliedPlayerDamage { get; }
    public int ExcessEnemyDamage { get; }
    public int PotentialHealing { get; }
    public int AppliedHealing { get; }
    public bool IsOpponentDefeated { get; }
    public bool IsSectorCompleted { get; }
    public bool IsGameOver { get; }
    public CyberDefenseRunState NextState { get; }
    public CyberDefenseTerminalRunSnapshot? TerminalSnapshot { get; }

    public CyberDefenseCombatTransitionResult(
        bool isCorrect,
        int requestedAttackDamage,
        int appliedOpponentDamage,
        int excessOpponentDamage,
        int incomingEnemyDamage,
        int appliedPlayerDamage,
        int excessEnemyDamage,
        int potentialHealing,
        int appliedHealing,
        bool isOpponentDefeated,
        bool isSectorCompleted,
        bool isGameOver,
        CyberDefenseRunState nextState,
        CyberDefenseTerminalRunSnapshot? terminalSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(nextState);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedAttackDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedOpponentDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(excessOpponentDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(incomingEnemyDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedPlayerDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(excessEnemyDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(potentialHealing);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedHealing);

        if (appliedOpponentDamage > requestedAttackDamage)
        {
            throw new ArgumentException(
                $"Applied opponent damage ({appliedOpponentDamage}) cannot exceed requested attack damage ({requestedAttackDamage}).",
                nameof(appliedOpponentDamage));
        }

        if (excessOpponentDamage != checked(requestedAttackDamage - appliedOpponentDamage))
        {
            throw new ArgumentException(
                $"Excess opponent damage ({excessOpponentDamage}) must equal requested attack damage ({requestedAttackDamage}) minus applied opponent damage ({appliedOpponentDamage}).",
                nameof(excessOpponentDamage));
        }

        if (appliedPlayerDamage > incomingEnemyDamage)
        {
            throw new ArgumentException(
                $"Applied player damage ({appliedPlayerDamage}) cannot exceed incoming enemy damage ({incomingEnemyDamage}).",
                nameof(appliedPlayerDamage));
        }

        if (excessEnemyDamage != checked(incomingEnemyDamage - appliedPlayerDamage))
        {
            throw new ArgumentException(
                $"Excess enemy damage ({excessEnemyDamage}) must equal incoming enemy damage ({incomingEnemyDamage}) minus applied player damage ({appliedPlayerDamage}).",
                nameof(excessEnemyDamage));
        }

        if (appliedHealing > potentialHealing)
        {
            throw new ArgumentException(
                $"Applied healing ({appliedHealing}) cannot exceed potential healing ({potentialHealing}).",
                nameof(appliedHealing));
        }

        if (isSectorCompleted && !isOpponentDefeated)
        {
            throw new ArgumentException(
                "Sector cannot be completed unless the opponent is defeated.",
                nameof(isSectorCompleted));
        }

        if (!isGameOver && terminalSnapshot is not null)
        {
            throw new ArgumentException(
                "Terminal snapshot must be null when game is not over.",
                nameof(terminalSnapshot));
        }

        if (isGameOver && terminalSnapshot is null)
        {
            throw new ArgumentException(
                "Terminal snapshot is required when game is over.",
                nameof(terminalSnapshot));
        }

        IsCorrect = isCorrect;
        RequestedAttackDamage = requestedAttackDamage;
        AppliedOpponentDamage = appliedOpponentDamage;
        ExcessOpponentDamage = excessOpponentDamage;
        IncomingEnemyDamage = incomingEnemyDamage;
        AppliedPlayerDamage = appliedPlayerDamage;
        ExcessEnemyDamage = excessEnemyDamage;
        PotentialHealing = potentialHealing;
        AppliedHealing = appliedHealing;
        IsOpponentDefeated = isOpponentDefeated;
        IsSectorCompleted = isSectorCompleted;
        IsGameOver = isGameOver;
        NextState = nextState;
        TerminalSnapshot = terminalSnapshot;
    }

    public bool Equals(CyberDefenseCombatTransitionResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsCorrect == other.IsCorrect &&
               RequestedAttackDamage == other.RequestedAttackDamage &&
               AppliedOpponentDamage == other.AppliedOpponentDamage &&
               ExcessOpponentDamage == other.ExcessOpponentDamage &&
               IncomingEnemyDamage == other.IncomingEnemyDamage &&
               AppliedPlayerDamage == other.AppliedPlayerDamage &&
               ExcessEnemyDamage == other.ExcessEnemyDamage &&
               PotentialHealing == other.PotentialHealing &&
               AppliedHealing == other.AppliedHealing &&
               IsOpponentDefeated == other.IsOpponentDefeated &&
               IsSectorCompleted == other.IsSectorCompleted &&
               IsGameOver == other.IsGameOver &&
               Equals(NextState, other.NextState) &&
               Equals(TerminalSnapshot, other.TerminalSnapshot);
    }

    public override bool Equals(object? obj) => Equals(obj as CyberDefenseCombatTransitionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsCorrect);
        hash.Add(RequestedAttackDamage);
        hash.Add(AppliedOpponentDamage);
        hash.Add(ExcessOpponentDamage);
        hash.Add(IncomingEnemyDamage);
        hash.Add(AppliedPlayerDamage);
        hash.Add(ExcessEnemyDamage);
        hash.Add(PotentialHealing);
        hash.Add(AppliedHealing);
        hash.Add(IsOpponentDefeated);
        hash.Add(IsSectorCompleted);
        hash.Add(IsGameOver);
        hash.Add(NextState);
        hash.Add(TerminalSnapshot);
        return hash.ToHashCode();
    }

    public static bool operator ==(CyberDefenseCombatTransitionResult? left, CyberDefenseCombatTransitionResult? right) => Equals(left, right);

    public static bool operator !=(CyberDefenseCombatTransitionResult? left, CyberDefenseCombatTransitionResult? right) => !Equals(left, right);
}
