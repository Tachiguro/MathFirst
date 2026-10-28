namespace MathFirst.Application.Persistence;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Read-only contract for retrieving unlocked offline achievements.
/// </summary>
public interface ICyberDefenseAchievementReader
{
    Task<IReadOnlyList<CyberDefenseAchievementRecord>> GetUnlockedAchievementsAsync(
        CancellationToken cancellationToken = default);
}
