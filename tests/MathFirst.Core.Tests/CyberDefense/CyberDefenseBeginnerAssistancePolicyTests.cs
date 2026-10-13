using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseBeginnerAssistancePolicyTests
{
    [Fact]
    public void Constants_MatchOwnerApprovedDecisionAndGddTrigger()
    {
        Assert.Equal(3, CyberDefenseBeginnerAssistancePolicy.RequiredConsecutiveEarlyDefeats);
        // DEC-01: exactly one temporary Emergency Shield.
        Assert.Equal(1, CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(50, false, false)]
    public void IsQualifyingEarlyDefeat_RequiresSector1WithoutBoss(int sector, bool bossReached, bool expected)
    {
        Assert.Equal(expected, CyberDefenseBeginnerAssistancePolicy.IsQualifyingEarlyDefeat(sector, bossReached));
    }

    [Fact]
    public void OneEarlyDefeat_DoesNotActivate()
    {
        int count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(0, 1, false);

        Assert.Equal(1, count);
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(count, false, 0, false));
    }

    [Fact]
    public void TwoEarlyDefeats_DoNotActivate()
    {
        int count = 0;
        for (int i = 0; i < 2; i++)
        {
            count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(count, 1, false);
        }

        Assert.Equal(2, count);
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(count, false, 0, false));
    }

    [Fact]
    public void ThreeConsecutiveEarlyDefeats_BecomeEligible()
    {
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(count, 1, false);
        }

        Assert.Equal(3, count);
        Assert.True(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(count, false, 0, false));
    }

    [Fact]
    public void ReachingSector1Boss_ResetsCounter()
    {
        Assert.Equal(0, CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(2));
        Assert.Equal(0, CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(3));
        Assert.Equal(0, CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(0));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(7, true)]
    public void NonQualifyingGameOver_ResetsCounter(int sector, bool bossReached)
    {
        Assert.Equal(0, CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(2, sector, bossReached));
    }

    [Fact]
    public void InterruptedStreak_RequiresThreeFreshConsecutiveDefeats()
    {
        int count = 0;
        count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(count, 1, false);
        count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(count, 1, false);
        count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(count);
        count = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(count, 1, false);

        Assert.Equal(1, count);
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(count, false, 0, false));
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(100, 3)]
    [InlineData(int.MaxValue, 3)]
    public void Counter_SaturatesAtRequiredThreshold_WithoutOverflow(int current, int expected)
    {
        Assert.Equal(
            expected,
            CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(current, 1, false));
    }

    [Fact]
    public void Eligibility_RejectedAfterReachingSector1Boss()
    {
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(3, true, 0, false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Eligibility_RejectedWithPermanentFirewallRank(int firewallRank)
    {
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(3, false, firewallRank, false));
    }

    [Fact]
    public void Eligibility_RejectedWhileAlreadyActive()
    {
        Assert.False(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(3, false, 0, true));
    }

    [Fact]
    public void Eligibility_AcceptsCountAboveThreshold()
    {
        Assert.True(CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(int.MaxValue, false, 0, false));
    }

    [Theory]
    [InlineData(true, true, 0, true)]   // Sector 1 cleared
    [InlineData(true, false, 1, true)]  // Firewall Rank 1 purchased
    [InlineData(true, true, 1, true)]
    [InlineData(true, false, 3, true)]
    [InlineData(true, false, 0, false)] // Still in Sector 1, no Firewall
    [InlineData(false, true, 1, false)] // Not active: nothing to deactivate
    [InlineData(false, false, 0, false)]
    public void ShouldDeactivate_OnSector1ClearOrFirewallRank1(
        bool isActive,
        bool sector1Cleared,
        int firewallRank,
        bool expected)
    {
        Assert.Equal(
            expected,
            CyberDefenseBeginnerAssistancePolicy.ShouldDeactivate(isActive, sector1Cleared, firewallRank));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void IsQualifyingEarlyDefeat_RejectsInvalidSector(int sector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseBeginnerAssistancePolicy.IsQualifyingEarlyDefeat(sector, false));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    public void GetConsecutiveEarlyDefeatsAfterGameOver_RejectsInvalidArguments(int current, int sector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(current, sector, false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetConsecutiveEarlyDefeatsAfterSector1BossReached_RejectsNegativeCount(int current)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(current));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 4)]
    [InlineData(3, int.MaxValue)]
    public void IsEligibleForActivation_RejectsInvalidArguments(int count, int firewallRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(count, false, firewallRank, false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ShouldDeactivate_RejectsInvalidFirewallRank(int firewallRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseBeginnerAssistancePolicy.ShouldDeactivate(true, false, firewallRank));
    }

    [Fact]
    public void PublicContract_DependsOnlyOnExplicitGameStatePrimitives()
    {
        // No learner records, arithmetic performance, FSRS data, age, or grade may influence eligibility:
        // every public input is an explicit Cyber Defense game-state integer or flag.
        MethodInfo[] methods = typeof(CyberDefenseBeginnerAssistancePolicy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.All(method.GetParameters(), p =>
                Assert.True(
                    p.ParameterType == typeof(int) || p.ParameterType == typeof(bool),
                    $"{method.Name}.{p.Name} has disallowed type {p.ParameterType}."));

            string[] forbidden = ["age", "grade", "fsrs", "learner", "accuracy", "latency", "fact", "profile"];
            Assert.All(method.GetParameters(), p =>
                Assert.DoesNotContain(forbidden, f => p.Name!.Contains(f, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Fact]
    public void PublicContract_ExposesNoXpOrSkillPointGeneration()
    {
        MemberInfo[] members = typeof(CyberDefenseBeginnerAssistancePolicy)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.All(members, m =>
        {
            Assert.DoesNotContain("Xp", m.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SkillPoint", m.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Reward", m.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Policy_HasNoMutableStaticState()
    {
        FieldInfo[] fields = typeof(CyberDefenseBeginnerAssistancePolicy).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        Assert.All(fields, f => Assert.True(f.IsLiteral, $"Field {f.Name} must be a compile-time constant."));
    }
}
