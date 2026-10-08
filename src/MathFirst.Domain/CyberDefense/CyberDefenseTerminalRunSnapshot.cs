namespace MathFirst.Domain.CyberDefense;

public sealed class CyberDefenseTerminalRunSnapshot : IEquatable<CyberDefenseTerminalRunSnapshot>
{
    public int Sector { get; }
    public int OpponentIndex { get; }
    public OpponentKind Kind { get; }
    public int OpponentCurrentHp { get; }
    public int OpponentMaxHp { get; }
    public int PlayerCurrentHp { get; }

    public CyberDefenseTerminalRunSnapshot(
        int sector,
        int opponentIndex,
        OpponentKind kind,
        int opponentCurrentHp,
        int opponentMaxHp,
        int playerCurrentHp = 0)
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

        OpponentKind expectedKind = CyberDefenseScalingPolicy.GetOpponentKind(sector, opponentIndex);
        if (kind != expectedKind)
        {
            throw new ArgumentException(
                $"Opponent kind '{kind}' does not match expected kind '{expectedKind}' for sector {sector}, index {opponentIndex}.",
                nameof(kind));
        }

        int expectedMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
        if (opponentMaxHp != expectedMaxHp)
        {
            throw new ArgumentException(
                $"Opponent max HP '{opponentMaxHp}' does not match expected max HP '{expectedMaxHp}' for sector {sector}, index {opponentIndex}.",
                nameof(opponentMaxHp));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(opponentCurrentHp, 1);
        if (opponentCurrentHp > opponentMaxHp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentCurrentHp),
                opponentCurrentHp,
                $"Opponent current HP ({opponentCurrentHp}) cannot exceed max HP ({opponentMaxHp}).");
        }

        if (playerCurrentHp != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCurrentHp),
                playerCurrentHp,
                "Terminal run snapshot requires player current HP to be exactly 0.");
        }

        Sector = sector;
        OpponentIndex = opponentIndex;
        Kind = kind;
        OpponentCurrentHp = opponentCurrentHp;
        OpponentMaxHp = opponentMaxHp;
        PlayerCurrentHp = playerCurrentHp;
    }

    public bool Equals(CyberDefenseTerminalRunSnapshot? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sector == other.Sector &&
               OpponentIndex == other.OpponentIndex &&
               Kind == other.Kind &&
               OpponentCurrentHp == other.OpponentCurrentHp &&
               OpponentMaxHp == other.OpponentMaxHp &&
               PlayerCurrentHp == other.PlayerCurrentHp;
    }

    public override bool Equals(object? obj) => Equals(obj as CyberDefenseTerminalRunSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(Sector, OpponentIndex, Kind, OpponentCurrentHp, OpponentMaxHp, PlayerCurrentHp);

    public static bool operator ==(CyberDefenseTerminalRunSnapshot? left, CyberDefenseTerminalRunSnapshot? right) => Equals(left, right);

    public static bool operator !=(CyberDefenseTerminalRunSnapshot? left, CyberDefenseTerminalRunSnapshot? right) => !Equals(left, right);
}
