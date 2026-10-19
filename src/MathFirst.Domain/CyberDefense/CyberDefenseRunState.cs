namespace MathFirst.Domain.CyberDefense;

public sealed class CyberDefenseRunState : IEquatable<CyberDefenseRunState>
{
    public int Sector { get; }
    public int OpponentIndex { get; }
    public int PlayerCurrentHp { get; }
    public OpponentState CurrentOpponent { get; }
    public int ShieldCapacity { get; }
    public int ActiveShields { get; }
    public int ShieldRechargeUnits { get; }

    public CyberDefenseRunState(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent)
        : this(sector, opponentIndex, playerCurrentHp, currentOpponent, 0, 0, 0)
    {
    }

    public CyberDefenseRunState(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent,
        int shieldCapacity,
        int activeShields,
        int shieldRechargeUnits)
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

        ArgumentOutOfRangeException.ThrowIfNegative(shieldCapacity);
        if (shieldCapacity > CyberDefenseFirewallPolicy.MaxShieldCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shieldCapacity),
                shieldCapacity,
                $"Shield capacity cannot exceed max capacity {CyberDefenseFirewallPolicy.MaxShieldCapacity}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(activeShields);
        if (activeShields > shieldCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeShields),
                activeShields,
                $"Active shields ({activeShields}) cannot exceed shield capacity ({shieldCapacity}).");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(shieldRechargeUnits);
        if (activeShields == shieldCapacity && shieldRechargeUnits != 0)
        {
            throw new ArgumentException(
                $"Shield recharge units must be 0 when active shields ({activeShields}) equal capacity ({shieldCapacity}).",
                nameof(shieldRechargeUnits));
        }

        if (shieldCapacity > 0 && activeShields < shieldCapacity)
        {
            if (shieldRechargeUnits >= CyberDefenseFirewallPolicy.RechargeThresholdUnits)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shieldRechargeUnits),
                    shieldRechargeUnits,
                    $"Shield recharge units ({shieldRechargeUnits}) must be less than threshold ({CyberDefenseFirewallPolicy.RechargeThresholdUnits}).");
            }
        }

        Sector = sector;
        OpponentIndex = opponentIndex;
        PlayerCurrentHp = playerCurrentHp;
        CurrentOpponent = currentOpponent;
        ShieldCapacity = shieldCapacity;
        ActiveShields = activeShields;
        ShieldRechargeUnits = shieldRechargeUnits;
    }

    public static CyberDefenseRunState InitialRun() => InitialRun(shieldCapacity: 0);

    public static CyberDefenseRunState InitialRun(int shieldCapacity)
    {
        var initialOpponent = new OpponentState(
            OpponentKind.Normal,
            CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0),
            CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0));

        return new CyberDefenseRunState(
            1,
            0,
            CyberDefenseCombatPolicy.PlayerMaxHp,
            initialOpponent,
            shieldCapacity,
            activeShields: shieldCapacity,
            shieldRechargeUnits: 0);
    }

    public static CyberDefenseRunState CreateActive(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent)
    {
        return CreateActive(sector, opponentIndex, playerCurrentHp, currentOpponent, 0, 0, 0);
    }

    public static CyberDefenseRunState CreateActive(
        int sector,
        int opponentIndex,
        int playerCurrentHp,
        OpponentState currentOpponent,
        int shieldCapacity,
        int activeShields,
        int shieldRechargeUnits)
    {
        return new CyberDefenseRunState(
            sector,
            opponentIndex,
            playerCurrentHp,
            currentOpponent,
            shieldCapacity,
            activeShields,
            shieldRechargeUnits);
    }

    public static CyberDefenseRunState Rehydrate(
        int sector,
        int opponentIndex,
        int opponentCurrentHp,
        int playerCurrentHp)
    {
        return Rehydrate(sector, opponentIndex, opponentCurrentHp, playerCurrentHp, 0);
    }

    public static CyberDefenseRunState Rehydrate(
        int sector,
        int opponentIndex,
        int opponentCurrentHp,
        int playerCurrentHp,
        int opponentExecutedAttacks)
    {
        return Rehydrate(sector, opponentIndex, opponentCurrentHp, playerCurrentHp, opponentExecutedAttacks, OpponentEncounterProvenance.Authoritative);
    }

    public static CyberDefenseRunState Rehydrate(
        int sector,
        int opponentIndex,
        int opponentCurrentHp,
        int playerCurrentHp,
        int opponentExecutedAttacks,
        OpponentEncounterProvenance provenance)
    {
        return Rehydrate(
            sector,
            opponentIndex,
            opponentCurrentHp,
            playerCurrentHp,
            opponentExecutedAttacks,
            provenance,
            shieldCapacity: 0,
            activeShields: 0,
            shieldRechargeUnits: 0);
    }

    public static CyberDefenseRunState Rehydrate(
        int sector,
        int opponentIndex,
        int opponentCurrentHp,
        int playerCurrentHp,
        int opponentExecutedAttacks,
        OpponentEncounterProvenance provenance,
        int shieldCapacity,
        int activeShields,
        int shieldRechargeUnits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentExecutedAttacks);

        if (!Enum.IsDefined(typeof(OpponentEncounterProvenance), provenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provenance),
                provenance,
                $"Undefined encounter provenance: {provenance}.");
        }

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

        var opponent = new OpponentState(kind, maxHp, opponentCurrentHp, opponentExecutedAttacks, provenance);

        return new CyberDefenseRunState(
            sector,
            opponentIndex,
            playerCurrentHp,
            opponent,
            shieldCapacity,
            activeShields,
            shieldRechargeUnits);
    }

    public bool Equals(CyberDefenseRunState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sector == other.Sector &&
               OpponentIndex == other.OpponentIndex &&
               PlayerCurrentHp == other.PlayerCurrentHp &&
               Equals(CurrentOpponent, other.CurrentOpponent) &&
               ShieldCapacity == other.ShieldCapacity &&
               ActiveShields == other.ActiveShields &&
               ShieldRechargeUnits == other.ShieldRechargeUnits;
    }

    public override bool Equals(object? obj) => Equals(obj as CyberDefenseRunState);

    public override int GetHashCode() =>
        HashCode.Combine(Sector, OpponentIndex, PlayerCurrentHp, CurrentOpponent, ShieldCapacity, ActiveShields, ShieldRechargeUnits);

    public static bool operator ==(CyberDefenseRunState? left, CyberDefenseRunState? right) => Equals(left, right);

    public static bool operator !=(CyberDefenseRunState? left, CyberDefenseRunState? right) => !Equals(left, right);
}
