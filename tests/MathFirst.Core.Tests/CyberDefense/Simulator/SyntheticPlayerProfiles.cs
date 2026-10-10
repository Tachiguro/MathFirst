namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System.Collections.Generic;

/// <summary>
/// Canonical synthetic player profiles A through E defined by the Cyber Defense GDD.
/// All profiles use baseline effective attack damage of 1.
/// Upgrade preferences are descriptive metadata only and cannot be executed in Slice 3.
/// </summary>
public static class SyntheticPlayerProfiles
{
    public const string ProfileAId = "ProfileA_Perfect";
    public const string ProfileBId = "ProfileB_Expert";
    public const string ProfileCId = "ProfileC_Average";
    public const string ProfileDId = "ProfileD_Learner";
    public const string ProfileEId = "ProfileE_Beginner";

    /// <summary>
    /// Profile A — Perfect: 100% accuracy, fixed 1,200 ms latency, no upgrades.
    /// Purpose: Future proof of survival through Sector 1000 without an unavoidable defeat.
    /// </summary>
    public static SyntheticPlayerProfile ProfileA_Perfect { get; } = new(
        id: ProfileAId,
        name: "Profile A (Perfect)",
        accuracyProbability: 1.00,
        minSyntheticLatencyMilliseconds: 1200,
        maxSyntheticLatencyMilliseconds: 1200,
        effectiveAttackDamage: 1,
        upgradePreference: SyntheticUpgradePreference.None,
        isUpgradeStrategyExecutable: false);

    /// <summary>
    /// Profile B — Expert: approximately 98% accuracy, 1,000–2,000 ms latency, attack-focused.
    /// Purpose: Future advanced-sector and boss scaling tests.
    /// </summary>
    public static SyntheticPlayerProfile ProfileB_Expert { get; } = new(
        id: ProfileBId,
        name: "Profile B (Expert)",
        accuracyProbability: 0.98,
        minSyntheticLatencyMilliseconds: 1000,
        maxSyntheticLatencyMilliseconds: 2000,
        effectiveAttackDamage: 1,
        upgradePreference: SyntheticUpgradePreference.AttackFocused,
        isUpgradeStrategyExecutable: false);

    /// <summary>
    /// Profile C — Average: approximately 85% accuracy, 2,000–4,500 ms latency, balanced.
    /// Purpose: Future typical progression and pacing tests.
    /// </summary>
    public static SyntheticPlayerProfile ProfileC_Average { get; } = new(
        id: ProfileCId,
        name: "Profile C (Average)",
        accuracyProbability: 0.85,
        minSyntheticLatencyMilliseconds: 2000,
        maxSyntheticLatencyMilliseconds: 4500,
        effectiveAttackDamage: 1,
        upgradePreference: SyntheticUpgradePreference.Balanced,
        isUpgradeStrategyExecutable: false);

    /// <summary>
    /// Profile D — Learner: approximately 70% accuracy, 3,000–6,000 ms latency, defensive.
    /// Purpose: Future error recovery and shield tests.
    /// </summary>
    public static SyntheticPlayerProfile ProfileD_Learner { get; } = new(
        id: ProfileDId,
        name: "Profile D (Learner)",
        accuracyProbability: 0.70,
        minSyntheticLatencyMilliseconds: 3000,
        maxSyntheticLatencyMilliseconds: 6000,
        effectiveAttackDamage: 1,
        upgradePreference: SyntheticUpgradePreference.Defensive,
        isUpgradeStrategyExecutable: false);

    /// <summary>
    /// Profile E — Beginner: approximately 50% accuracy, 4,000–8,000 ms latency, unupgraded baseline.
    /// Purpose: Future early-game survival and assistance tests.
    /// </summary>
    public static SyntheticPlayerProfile ProfileE_Beginner { get; } = new(
        id: ProfileEId,
        name: "Profile E (Beginner)",
        accuracyProbability: 0.50,
        minSyntheticLatencyMilliseconds: 4000,
        maxSyntheticLatencyMilliseconds: 8000,
        effectiveAttackDamage: 1,
        upgradePreference: SyntheticUpgradePreference.UnupgradedBaseline,
        isUpgradeStrategyExecutable: false);

    public static SyntheticPlayerProfile ProfileA => ProfileA_Perfect;
    public static SyntheticPlayerProfile ProfileB => ProfileB_Expert;
    public static SyntheticPlayerProfile ProfileC => ProfileC_Average;
    public static SyntheticPlayerProfile ProfileD => ProfileD_Learner;
    public static SyntheticPlayerProfile ProfileE => ProfileE_Beginner;

    public static IReadOnlyList<SyntheticPlayerProfile> All { get; } = new[]
    {
        ProfileA_Perfect,
        ProfileB_Expert,
        ProfileC_Average,
        ProfileD_Learner,
        ProfileE_Beginner
    };
}
