namespace MathFirst.Domain.CyberDefense;

using System;
using System.Collections.Generic;

/// <summary>
/// Stable domain identifiers for MathFirst Cyber Defense offline achievements.
/// Defined for MF-CYBER-009 Slice 4A persistence foundation.
/// Trigger evaluation and unlock semantics are deferred to Slice 4B.
/// </summary>
public static class CyberDefenseAchievementIds
{
    public const string Sector10 = "sector_10";
    public const string BossSlayer = "boss_slayer";
    public const string FirstUpgrade = "first_upgrade";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Sector10,
        BossSlayer,
        FirstUpgrade
    };

    public static bool IsKnown(string achievementId)
    {
        if (string.IsNullOrWhiteSpace(achievementId))
        {
            return false;
        }

        return All.Contains(achievementId);
    }
}
