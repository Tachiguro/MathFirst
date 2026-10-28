namespace MathFirst.Application.Practice;

/// <summary>
/// Narrow typed interface allowing non-combat pages such as Settings to request
/// a Cyber Defense tutorial replay without depending on combat or session state types.
/// </summary>
public interface ICyberDefenseTutorialReplayRequests
{
    /// <summary>
    /// Records a pending request to replay the Cyber Defense spotlight tutorial.
    /// </summary>
    void RequestTutorialReplay();
}
