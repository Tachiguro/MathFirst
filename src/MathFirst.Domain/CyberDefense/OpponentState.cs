namespace MathFirst.Domain.CyberDefense;

public sealed class OpponentState : IEquatable<OpponentState>
{
    public OpponentKind Kind { get; }
    public int MaxHp { get; }
    public int CurrentHp { get; }
    public int ExecutedAttacks { get; }
    public OpponentEncounterProvenance Provenance { get; }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp)
        : this(kind, maxHp, currentHp, 0, OpponentEncounterProvenance.Authoritative)
    {
    }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp, int executedAttacks)
        : this(kind, maxHp, currentHp, executedAttacks, OpponentEncounterProvenance.Authoritative)
    {
    }

    public OpponentState(OpponentKind kind, int maxHp, int currentHp, int executedAttacks, OpponentEncounterProvenance provenance)
    {
        if (!Enum.IsDefined(typeof(OpponentKind), kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"Undefined opponent kind: {kind}.");
        }

        if (!Enum.IsDefined(typeof(OpponentEncounterProvenance), provenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provenance),
                provenance,
                $"Undefined encounter provenance: {provenance}.");
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
        Provenance = provenance;
    }

    public bool Equals(OpponentState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind &&
               MaxHp == other.MaxHp &&
               CurrentHp == other.CurrentHp &&
               ExecutedAttacks == other.ExecutedAttacks &&
               Provenance == other.Provenance;
    }

    public override bool Equals(object? obj) => Equals(obj as OpponentState);

    public override int GetHashCode() => HashCode.Combine(Kind, MaxHp, CurrentHp, ExecutedAttacks, Provenance);

    public static bool operator ==(OpponentState? left, OpponentState? right) => Equals(left, right);

    public static bool operator !=(OpponentState? left, OpponentState? right) => !Equals(left, right);
}
