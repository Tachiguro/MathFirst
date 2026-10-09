namespace MathFirst.Domain.CyberDefense;

public sealed class OpponentState : IEquatable<OpponentState>
{
    public OpponentKind Kind { get; }
    public int MaxHp { get; }
    public int CurrentHp { get; }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp)
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
    }

    public bool Equals(OpponentState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind && MaxHp == other.MaxHp && CurrentHp == other.CurrentHp;
    }

    public override bool Equals(object? obj) => Equals(obj as OpponentState);

    public override int GetHashCode() => HashCode.Combine(Kind, MaxHp, CurrentHp);

    public static bool operator ==(OpponentState? left, OpponentState? right) => Equals(left, right);

    public static bool operator !=(OpponentState? left, OpponentState? right) => !Equals(left, right);
}
