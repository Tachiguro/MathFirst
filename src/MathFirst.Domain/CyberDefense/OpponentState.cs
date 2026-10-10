namespace MathFirst.Domain.CyberDefense;

public sealed class OpponentState : IEquatable<OpponentState>
{
    public OpponentKind Kind { get; }
    public int MaxHp { get; }
    public int CurrentHp { get; }
    public int ExecutedAttacks { get; }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp)
        : this(kind, maxHp, currentHp, 0)
    {
    }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp, int executedAttacks)
    {
        if (!Enum.IsDefined(typeof(OpponentKind), kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Undefined opponent kind: {kind}.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxHp, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentHp, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(executedAttacks);

        if (currentHp > maxHp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentHp),
                currentHp,
                $"Current HP ({currentHp}) cannot exceed Max HP ({maxHp}).");
        }

        Kind = kind;
        MaxHp = maxHp;
        CurrentHp = currentHp;
        ExecutedAttacks = executedAttacks;
    }

    public bool Equals(OpponentState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind &&
               MaxHp == other.MaxHp &&
               CurrentHp == other.CurrentHp &&
               ExecutedAttacks == other.ExecutedAttacks;
    }

    public override bool Equals(object? obj) => Equals(obj as OpponentState);

    public override int GetHashCode() => HashCode.Combine(Kind, MaxHp, CurrentHp, ExecutedAttacks);

    public static bool operator ==(OpponentState? left, OpponentState? right) => Equals(left, right);

    public static bool operator !=(OpponentState? left, OpponentState? right) => !Equals(left, right);
}
