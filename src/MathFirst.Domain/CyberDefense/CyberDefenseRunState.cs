namespace MathFirst.Domain.CyberDefense;

public sealed class CyberDefenseRunState : IEquatable<CyberDefenseRunState>
{
    public int Sector { get; }
    public int OpponentIndex { get; }
    public int PlayerCurrentHp { get; }
    public OpponentState CurrentOpponent { get; }

    public CyberDefenseRunState(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentIndex);

        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        if (opponentIndex > bossIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentIndex),
                opponentIndex,
                $"Opponent index cannot exceed boss index {bossIndex} for sector {sector}.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(playerCurrentHp, 1);
        if (playerCurrentHp > CyberDefenseCombatPolicy.PlayerMaxHp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCurrentHp),
                playerCurrentHp,
                $"Player current HP cannot exceed max HP ({CyberDefenseCombatPolicy.PlayerMaxHp}).");
        }

        ArgumentNullException.ThrowIfNull(currentOpponent);

        OpponentKind expectedKind = CyberDefenseScalingPolicy.GetOpponentKind(sector, opponentIndex);
        if (currentOpponent.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Current opponent kind '{currentOpponent.Kind}' does not match expected kind '{expectedKind}' for sector {sector}, index {opponentIndex}.",
                nameof(currentOpponent));
        }

        int expectedMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
        if (currentOpponent.MaxHp != expectedMaxHp)
        {
            throw new ArgumentException(
                $"Current opponent max HP '{currentOpponent.MaxHp}' does not match expected max HP '{expectedMaxHp}' for sector {sector}, index {opponentIndex}.",
                nameof(currentOpponent));
        }

        Sector = sector;
        OpponentIndex = opponentIndex;
        PlayerCurrentHp = playerCurrentHp;
        CurrentOpponent = currentOpponent;
    }

    public static CyberDefenseRunState InitialRun()
    {
        var initialOpponent = new OpponentState(
            OpponentKind.Normal,
            CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0),
            CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0));

        return new CyberDefenseRunState(
            1,
            0,
            CyberDefenseCombatPolicy.PlayerMaxHp,
            initialOpponent);
    }

    public static CyberDefenseRunState CreateActive(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent)
    {
        return new CyberDefenseRunState(
            sector,
            opponentIndex,
            playerCurrentHp,
            currentOpponent);
    }

    public static CyberDefenseRunState Rehydrate(
        int sector,
        int opponentIndex,
        int opponentCurrentHp,
        int playerCurrentHp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentIndex);

        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        if (opponentIndex > bossIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentIndex),
                opponentIndex,
                $"Opponent index cannot exceed boss index {bossIndex} for sector {sector}.");
        }

        OpponentKind kind = CyberDefenseScalingPolicy.GetOpponentKind(sector, opponentIndex);
        int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);

        ArgumentOutOfRangeException.ThrowIfLessThan(opponentCurrentHp, 1);
        if (opponentCurrentHp > maxHp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentCurrentHp),
                opponentCurrentHp,
                $"Opponent current HP ({opponentCurrentHp}) cannot exceed max HP ({maxHp}).");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(playerCurrentHp, 1);
        if (playerCurrentHp > CyberDefenseCombatPolicy.PlayerMaxHp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCurrentHp),
                playerCurrentHp,
                $"Player current HP ({playerCurrentHp}) cannot exceed max HP ({CyberDefenseCombatPolicy.PlayerMaxHp}).");
        }

        var opponent = new OpponentState(kind, maxHp, opponentCurrentHp);

        return new CyberDefenseRunState(
            sector,
            opponentIndex,
            playerCurrentHp,
            opponent);
    }

    public bool Equals(CyberDefenseRunState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sector == other.Sector &&
               OpponentIndex == other.OpponentIndex &&
               PlayerCurrentHp == other.PlayerCurrentHp &&
               Equals(CurrentOpponent, other.CurrentOpponent);
    }

    public override bool Equals(object? obj) => Equals(obj as CyberDefenseRunState);

    public override int GetHashCode() =>
        HashCode.Combine(Sector, OpponentIndex, PlayerCurrentHp, CurrentOpponent);

    public static bool operator ==(CyberDefenseRunState? left, CyberDefenseRunState? right) => Equals(left, right);

    public static bool operator !=(CyberDefenseRunState? left, CyberDefenseRunState? right) => !Equals(left, right);
}
