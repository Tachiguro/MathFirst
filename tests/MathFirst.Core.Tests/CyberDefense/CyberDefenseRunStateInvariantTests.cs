using System.Reflection;
using System.Runtime.CompilerServices;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseRunStateInvariantTests
{
    // =========================================================================
    // 1. INITIAL RUN STATE
    // =========================================================================

    [Fact]
    public void InitialRun_CreatesExpectedStartingBaseline()
    {
        var run = CyberDefenseRunState.InitialRun();

        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(100, run.PlayerCurrentHp);
        Assert.NotNull(run.CurrentOpponent);
        Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);
        Assert.Equal(2, run.CurrentOpponent.MaxHp);
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void InitialRun_ReturnsIndependentInstances()
    {
        var run1 = CyberDefenseRunState.InitialRun();
        var run2 = CyberDefenseRunState.InitialRun();

        Assert.NotSame(run1, run2);
        Assert.NotSame(run1.CurrentOpponent, run2.CurrentOpponent);
        Assert.Equal(run1, run2);
    }

    // =========================================================================
    // 2. OPPONENT STATE VALIDATION
    // =========================================================================

    [Theory]
    [InlineData((OpponentKind)(-1))]
    [InlineData((OpponentKind)2)]
    [InlineData((OpponentKind)99)]
    public void OpponentState_ThrowsOnUndefinedKind(OpponentKind undefinedKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpponentState(undefinedKind, 2, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void OpponentState_ThrowsWhenMaxHpIsZeroOrNegative(int maxHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpponentState(OpponentKind.Normal, maxHp, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void OpponentState_ThrowsWhenCurrentHpIsZeroOrNegative(int currentHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpponentState(OpponentKind.Normal, 5, currentHp));
    }

    [Fact]
    public void OpponentState_ThrowsWhenCurrentHpExceedsMaxHp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpponentState(OpponentKind.Normal, 5, 6));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void OpponentState_ConstructsValidLivingOpponent(int currentHp)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 5, currentHp);

        Assert.Equal(OpponentKind.Normal, opponent.Kind);
        Assert.Equal(5, opponent.MaxHp);
        Assert.Equal(currentHp, opponent.CurrentHp);
    }

    // =========================================================================
    // 3. ACTIVE RUN STATE VALIDATION (DIRECT CTOR & CREATEACTIVE)
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void RunState_ThrowsWhenSectorIsLessThanOne(int sector)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseRunState(sector, 0, 100, opponent));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(sector, 0, 100, opponent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void RunState_ThrowsWhenOpponentIndexIsNegative(int index)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseRunState(1, index, 100, opponent));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, index, 100, opponent));
    }

    [Fact]
    public void RunState_ThrowsWhenOpponentIndexExceedsBossIndex()
    {
        // For Sector 1, boss index is 5. Index 6 must throw.
        var boss = new OpponentState(OpponentKind.Boss, 12, 12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseRunState(1, 6, 100, boss));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 6, 100, boss));
    }

    [Fact]
    public void RunState_ThrowsWhenOpponentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CyberDefenseRunState(1, 0, 100, null!));

        Assert.Throws<ArgumentNullException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, 100, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void RunState_ThrowsWhenPlayerHpIsZeroOrNegative(int playerHp)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseRunState(1, 0, playerHp, opponent));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, playerHp, opponent));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    public void RunState_ThrowsWhenPlayerHpExceedsMax(int playerHp)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseRunState(1, 0, playerHp, opponent));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.CreateActive(1, 0, playerHp, opponent));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void RunState_AllowsValidPlayerHpRange(int playerHp)
    {
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var run = new CyberDefenseRunState(1, 0, playerHp, opponent);

        Assert.Equal(playerHp, run.PlayerCurrentHp);
    }

    // =========================================================================
    // 4. CROSS-OBJECT INVARIANTS (SECTOR & OPPONENT CLASSIFICATION / STATS)
    // =========================================================================

    [Fact]
    public void RunState_ThrowsWhenOpponentKindMismatchesExpectedForIndex()
    {
        // Sector 1 index 0 expects Normal, but Boss provided
        var wrongKindOpponent = new OpponentState(OpponentKind.Boss, 2, 2);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseRunState(1, 0, 100, wrongKindOpponent));

        // Sector 1 index 5 expects Boss, but Normal provided
        var wrongKindBoss = new OpponentState(OpponentKind.Normal, 12, 12);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseRunState(1, 5, 100, wrongKindBoss));
    }

    [Fact]
    public void RunState_ThrowsWhenOpponentMaxHpMismatchesSectorPolicy()
    {
        // Sector 1 Normal MaxHp is 2. Providing MaxHp 3 must throw.
        var wrongHpOpponent = new OpponentState(OpponentKind.Normal, 3, 3);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseRunState(1, 0, 100, wrongHpOpponent));

        // Sector 1 Boss MaxHp is 12. Providing MaxHp 11 must throw.
        var wrongHpBoss = new OpponentState(OpponentKind.Boss, 11, 11);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseRunState(1, 5, 100, wrongHpBoss));
    }

    [Fact]
    public void RunState_EnforcesSector1Structure()
    {
        // Sector 1: Normal 0..4 (MaxHp 2), Boss 5 (MaxHp 12)
        int normalMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 0);
        Assert.Equal(2, normalMaxHp);

        for (int i = 0; i <= 4; i++)
        {
            var normal = new OpponentState(OpponentKind.Normal, normalMaxHp, normalMaxHp);
            var run = CyberDefenseRunState.CreateActive(1, i, 100, normal);
            Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);
            Assert.Equal(i, run.OpponentIndex);
        }

        int bossMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, 5);
        Assert.Equal(12, bossMaxHp);

        var boss = new OpponentState(OpponentKind.Boss, bossMaxHp, bossMaxHp);
        var bossRun = CyberDefenseRunState.CreateActive(1, 5, 100, boss);
        Assert.Equal(OpponentKind.Boss, bossRun.CurrentOpponent.Kind);
        Assert.Equal(5, bossRun.OpponentIndex);
    }

    [Fact]
    public void RunState_EnforcesSector4Structure()
    {
        // Sector 4: Normal 0..6 (MaxHp 2), Boss 7 (MaxHp 14)
        Assert.Equal(7, CyberDefenseScalingPolicy.GetBossIndex(4));
        Assert.Equal(2, CyberDefenseScalingPolicy.GetNormalMaxHp(4));
        Assert.Equal(14, CyberDefenseScalingPolicy.GetBossMaxHp(4));

        for (int i = 0; i <= 6; i++)
        {
            var normal = new OpponentState(OpponentKind.Normal, 2, 2);
            var run = CyberDefenseRunState.CreateActive(4, i, 100, normal);
            Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);
        }

        var boss = new OpponentState(OpponentKind.Boss, 14, 14);
        var bossRun = CyberDefenseRunState.CreateActive(4, 7, 100, boss);
        Assert.Equal(OpponentKind.Boss, bossRun.CurrentOpponent.Kind);
        Assert.Equal(14, bossRun.CurrentOpponent.MaxHp);
    }

    [Fact]
    public void RunState_EnforcesSector10Structure()
    {
        // Sector 10: Normal 0..7 (MaxHp 3), Boss 8 (MaxHp 18)
        Assert.Equal(8, CyberDefenseScalingPolicy.GetBossIndex(10));
        Assert.Equal(3, CyberDefenseScalingPolicy.GetNormalMaxHp(10));
        Assert.Equal(18, CyberDefenseScalingPolicy.GetBossMaxHp(10));

        var normal = new OpponentState(OpponentKind.Normal, 3, 3);
        var run = CyberDefenseRunState.CreateActive(10, 3, 100, normal);
        Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);

        var boss = new OpponentState(OpponentKind.Boss, 18, 18);
        var bossRun = CyberDefenseRunState.CreateActive(10, 8, 100, boss);
        Assert.Equal(OpponentKind.Boss, bossRun.CurrentOpponent.Kind);
        Assert.Equal(18, bossRun.CurrentOpponent.MaxHp);
    }

    // =========================================================================
    // 5. VALIDATED REHYDRATION
    // =========================================================================

    [Fact]
    public void Rehydrate_ReconstructsPartiallyDamagedNormalOpponent()
    {
        // Sector 1, index 0, opponent HP 1, player HP 90
        var run = CyberDefenseRunState.Rehydrate(1, 0, 1, 90);

        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(90, run.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, run.CurrentOpponent.Kind);
        Assert.Equal(2, run.CurrentOpponent.MaxHp);
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void Rehydrate_ReconstructsPartiallyDamagedBoss()
    {
        // Sector 1, index 5, opponent HP 7, player HP 80
        var run = CyberDefenseRunState.Rehydrate(1, 5, 7, 80);

        Assert.Equal(1, run.Sector);
        Assert.Equal(5, run.OpponentIndex);
        Assert.Equal(80, run.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, run.CurrentOpponent.Kind);
        Assert.Equal(12, run.CurrentOpponent.MaxHp);
        Assert.Equal(7, run.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void Rehydrate_ReconstructsFullHealthBossInSector4()
    {
        // Sector 4, boss index 7, opponent HP 14
        var run = CyberDefenseRunState.Rehydrate(4, 7, 14, 100);

        Assert.Equal(4, run.Sector);
        Assert.Equal(7, run.OpponentIndex);
        Assert.Equal(100, run.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Boss, run.CurrentOpponent.Kind);
        Assert.Equal(14, run.CurrentOpponent.MaxHp);
        Assert.Equal(14, run.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public void Rehydrate_ReconstructsRunAtLargeSector()
    {
        int sector = 1_000_000;
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        var run = CyberDefenseRunState.Rehydrate(sector, bossIndex, bossMaxHp - 5, 50);

        Assert.Equal(sector, run.Sector);
        Assert.Equal(bossIndex, run.OpponentIndex);
        Assert.Equal(OpponentKind.Boss, run.CurrentOpponent.Kind);
        Assert.Equal(bossMaxHp, run.CurrentOpponent.MaxHp);
        Assert.Equal(bossMaxHp - 5, run.CurrentOpponent.CurrentHp);
        Assert.Equal(50, run.PlayerCurrentHp);
    }

    [Fact]
    public void Rehydrate_ThrowsWhenOpponentHpExceedsMaxHp()
    {
        // Sector 4, boss index 7, max HP is 14. 15 must throw.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(4, 7, 15, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rehydrate_ThrowsWhenOpponentHpIsZeroOrNegative(int opponentHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(1, 0, opponentHp, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rehydrate_ThrowsWhenPlayerHpIsInvalid(int playerHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(1, 0, 1, playerHp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rehydrate_ThrowsWhenSectorIsInvalid(int sector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(sector, 0, 1, 100));
    }

    [Fact]
    public void Rehydrate_ThrowsWhenOpponentIndexIsOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(1, -1, 1, 100));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRunState.Rehydrate(1, 6, 1, 100));
    }

    // =========================================================================
    // 6. TERMINAL RUN SNAPSHOT
    // =========================================================================

    [Fact]
    public void TerminalRunSnapshot_ConstructsValidSnapshotWhenPlayerHpIsZero()
    {
        var snapshot = new CyberDefenseTerminalRunSnapshot(
            sector: 1,
            opponentIndex: 0,
            kind: OpponentKind.Normal,
            opponentCurrentHp: 1,
            opponentMaxHp: 2,
            playerCurrentHp: 0);

        Assert.Equal(1, snapshot.Sector);
        Assert.Equal(0, snapshot.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, snapshot.Kind);
        Assert.Equal(1, snapshot.OpponentCurrentHp);
        Assert.Equal(2, snapshot.OpponentMaxHp);
        Assert.Equal(0, snapshot.PlayerCurrentHp);
    }

    [Fact]
    public void TerminalRunSnapshot_DefaultPlayerHpParameterIsZero()
    {
        var snapshot = new CyberDefenseTerminalRunSnapshot(
            sector: 1,
            opponentIndex: 0,
            kind: OpponentKind.Normal,
            opponentCurrentHp: 2,
            opponentMaxHp: 2);

        Assert.Equal(0, snapshot.PlayerCurrentHp);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(-1)]
    public void TerminalRunSnapshot_ThrowsWhenPlayerHpIsNotZero(int playerHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 0,
                kind: OpponentKind.Normal,
                opponentCurrentHp: 1,
                opponentMaxHp: 2,
                playerCurrentHp: playerHp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TerminalRunSnapshot_ThrowsWhenSectorIsInvalid(int sector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: sector,
                opponentIndex: 0,
                kind: OpponentKind.Normal,
                opponentCurrentHp: 1,
                opponentMaxHp: 2,
                playerCurrentHp: 0));
    }

    [Fact]
    public void TerminalRunSnapshot_ThrowsWhenOpponentIndexIsOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: -1,
                kind: OpponentKind.Normal,
                opponentCurrentHp: 1,
                opponentMaxHp: 2,
                playerCurrentHp: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 6,
                kind: OpponentKind.Boss,
                opponentCurrentHp: 1,
                opponentMaxHp: 12,
                playerCurrentHp: 0));
    }

    [Fact]
    public void TerminalRunSnapshot_ThrowsWhenKindDoesNotMatchExpected()
    {
        // Sector 1 index 0 must be Normal, not Boss
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 0,
                kind: OpponentKind.Boss,
                opponentCurrentHp: 1,
                opponentMaxHp: 2,
                playerCurrentHp: 0));

        // Sector 1 index 5 must be Boss, not Normal
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 5,
                kind: OpponentKind.Normal,
                opponentCurrentHp: 1,
                opponentMaxHp: 12,
                playerCurrentHp: 0));
    }

    [Fact]
    public void TerminalRunSnapshot_ThrowsWhenMaxHpDoesNotMatchScalingPolicy()
    {
        // Sector 1 Normal MaxHp is 2, not 3
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 0,
                kind: OpponentKind.Normal,
                opponentCurrentHp: 1,
                opponentMaxHp: 3,
                playerCurrentHp: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void TerminalRunSnapshot_ThrowsWhenCurrentHpIsOutOfRange(int currentHp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseTerminalRunSnapshot(
                sector: 1,
                opponentIndex: 0,
                kind: OpponentKind.Normal,
                opponentCurrentHp: currentHp,
                opponentMaxHp: 2,
                playerCurrentHp: 0));
    }

    // =========================================================================
    // 7. IMMUTABILITY & SHAPE INVARIANTS
    // =========================================================================

    [Theory]
    [InlineData(typeof(OpponentState))]
    [InlineData(typeof(CyberDefenseRunState))]
    [InlineData(typeof(CyberDefenseTerminalRunSnapshot))]
    public void StateTypes_AreSealedClasses(Type type)
    {
        Assert.True(type.IsClass);
        Assert.True(type.IsSealed);
        Assert.False(type.IsAbstract);
    }

    [Theory]
    [InlineData(typeof(OpponentState))]
    [InlineData(typeof(CyberDefenseRunState))]
    [InlineData(typeof(CyberDefenseTerminalRunSnapshot))]
    public void StateTypes_HaveNoPublicSettersOrInitAccessors(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);

        foreach (var prop in properties)
        {
            Assert.True(prop.CanRead, $"Property {prop.Name} on {type.Name} should be readable.");

            var setMethod = prop.SetMethod;
            if (setMethod != null)
            {
                Assert.False(setMethod.IsPublic,
                    $"Property {prop.Name} on {type.Name} has a public setter.");

                // Verify it's not an init-only accessor either
                bool isInit = setMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit));
                Assert.False(isInit,
                    $"Property {prop.Name} on {type.Name} has an init accessor.");
            }
        }
    }

    // =========================================================================
    // 8. VALUE EQUALITY & HASH CODES
    // =========================================================================

    [Fact]
    public void OpponentState_ImplementsValueEqualityAndConsistentHashCodes()
    {
        var a = new OpponentState(OpponentKind.Normal, 5, 3);
        var b = new OpponentState(OpponentKind.Normal, 5, 3);
        var c = new OpponentState(OpponentKind.Boss, 12, 12);
        var d = new OpponentState(OpponentKind.Normal, 5, 2);

        Assert.Equal(a, b);
        Assert.True(a.Equals((object)b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, c);
        Assert.False(a == c);
        Assert.True(a != c);

        Assert.NotEqual(a, d);
        Assert.False(a == d);
        Assert.True(a != d);

        Assert.False(a.Equals(null));
        Assert.False(a == null);
        Assert.False(null == a);
        Assert.True((OpponentState?)null == (OpponentState?)null);
    }

    [Fact]
    public void CyberDefenseRunState_ImplementsValueEqualityAndConsistentHashCodes()
    {
        var run1 = new CyberDefenseRunState(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2));
        var run2 = new CyberDefenseRunState(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2));
        var diffSector = new CyberDefenseRunState(2, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2));
        var diffPlayerHp = new CyberDefenseRunState(1, 0, 95, new OpponentState(OpponentKind.Normal, 2, 2));
        var diffOpponentHp = new CyberDefenseRunState(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 1));

        Assert.Equal(run1, run2);
        Assert.True(run1.Equals((object)run2));
        Assert.True(run1 == run2);
        Assert.False(run1 != run2);
        Assert.Equal(run1.GetHashCode(), run2.GetHashCode());

        Assert.NotEqual(run1, diffSector);
        Assert.NotEqual(run1, diffPlayerHp);
        Assert.NotEqual(run1, diffOpponentHp);

        Assert.False(run1.Equals(null));
        Assert.False(run1 == null);
        Assert.False(null == run1);
        Assert.True((CyberDefenseRunState?)null == (CyberDefenseRunState?)null);
    }

    [Fact]
    public void TerminalRunSnapshot_ImplementsValueEqualityAndConsistentHashCodes()
    {
        var s1 = new CyberDefenseTerminalRunSnapshot(1, 0, OpponentKind.Normal, 2, 2, 0);
        var s2 = new CyberDefenseTerminalRunSnapshot(1, 0, OpponentKind.Normal, 2, 2, 0);
        var sDiffSector = new CyberDefenseTerminalRunSnapshot(2, 0, OpponentKind.Normal, 2, 2, 0);
        var sDiffOpponentHp = new CyberDefenseTerminalRunSnapshot(1, 0, OpponentKind.Normal, 1, 2, 0);

        Assert.Equal(s1, s2);
        Assert.True(s1.Equals((object)s2));
        Assert.True(s1 == s2);
        Assert.False(s1 != s2);
        Assert.Equal(s1.GetHashCode(), s2.GetHashCode());

        Assert.NotEqual(s1, sDiffSector);
        Assert.NotEqual(s1, sDiffOpponentHp);

        Assert.False(s1.Equals(null));
        Assert.False(s1 == null);
        Assert.False(null == s1);
        Assert.True((CyberDefenseTerminalRunSnapshot?)null == (CyberDefenseTerminalRunSnapshot?)null);
    }
}
