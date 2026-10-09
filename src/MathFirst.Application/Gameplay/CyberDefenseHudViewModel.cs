namespace MathFirst.Application.Gameplay;

using System;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable, presentation-oriented Application ViewModel projecting authoritative Cyber Defense
/// run state and persisted combat transitions to the UI HUD without exposing mutable combat authority.
/// </summary>
public sealed class CyberDefenseHudViewModel : IEquatable<CyberDefenseHudViewModel>
{
    private static readonly (string Id, string NameKey, string DescKey, string AssetPath)[] NormalOpponentProfiles =
    [
        ("glitch-drone", "CyberDefense_Opponent_GlitchDrone", "CyberDefense_Opponent_GlitchDrone_Desc", "images/cyber-defense/glitch-drone.svg"),
        ("data-leech", "CyberDefense_Opponent_DataLeech", "CyberDefense_Opponent_DataLeech_Desc", "images/cyber-defense/data-leech.svg"),
        ("firewall-breaker", "CyberDefense_Opponent_FirewallBreaker", "CyberDefense_Opponent_FirewallBreaker_Desc", "images/cyber-defense/firewall-breaker.svg"),
        ("virus-core", "CyberDefense_Opponent_VirusCore", "CyberDefense_Opponent_VirusCore_Desc", "images/cyber-defense/virus-core.svg"),
        ("signal-phantom", "CyberDefense_Opponent_SignalPhantom", "CyberDefense_Opponent_SignalPhantom_Desc", "images/cyber-defense/signal-phantom.svg"),
        ("quantum-bug", "CyberDefense_Opponent_QuantumBug", "CyberDefense_Opponent_QuantumBug_Desc", "images/cyber-defense/quantum-bug.svg"),
        ("trojan-wasp", "CyberDefense_Opponent_TrojanWasp", "CyberDefense_Opponent_TrojanWasp_Desc", "images/cyber-defense/trojan-wasp.svg"),
        ("crystal-malware", "CyberDefense_Opponent_CrystalMalware", "CyberDefense_Opponent_CrystalMalware_Desc", "images/cyber-defense/crystal-malware.svg")
    ];

    public int SectorNumber { get; }
    public int OpponentIndex { get; }
    public int NormalOpponentCount { get; }
    public int TotalOpponentCount { get; }
    public int WaveNumber => OpponentIndex + 1;
    public OpponentKind OpponentKind { get; }
    public bool IsBoss { get; }
    public bool IsSectorBoss { get; }
    public int OpponentCurrentHp { get; }
    public int OpponentMaxHp { get; }
    public int OpponentHpPercentage { get; }
    public int PlayerCurrentHp { get; }
    public int PlayerMaxHp { get; }
    public int PlayerHpPercentage { get; }
    public string OpponentNameKey { get; }
    public string OpponentDescKey { get; }
    public string OpponentAssetPath { get; }
    public string OpponentScaleClass { get; }
    public CyberDefenseFeedbackKind LastFeedback { get; }
    public int LastAppliedOpponentDamage { get; }
    public int LastAppliedPlayerDamage { get; }
    public int LastAppliedHealing { get; }
    public bool IsOpponentDefeated { get; }
    public bool IsGameOver { get; }
    public long FeedbackRevision { get; }
    public CyberDefenseTerminalRunSnapshot? TerminalSnapshot { get; }

    public CyberDefenseHudViewModel(
        int sectorNumber,
        int opponentIndex,
        int normalOpponentCount,
        int totalOpponentCount,
        OpponentKind opponentKind,
        bool isBoss,
        bool isSectorBoss,
        int opponentCurrentHp,
        int opponentMaxHp,
        int opponentHpPercentage,
        int playerCurrentHp,
        int playerMaxHp,
        int playerHpPercentage,
        string opponentNameKey,
        string opponentDescKey,
        string opponentAssetPath,
        string opponentScaleClass,
        CyberDefenseFeedbackKind lastFeedback,
        int lastAppliedOpponentDamage,
        int lastAppliedPlayerDamage,
        int lastAppliedHealing,
        bool isOpponentDefeated,
        bool isGameOver,
        long feedbackRevision,
        CyberDefenseTerminalRunSnapshot? terminalSnapshot)
    {
        SectorNumber = sectorNumber;
        OpponentIndex = opponentIndex;
        NormalOpponentCount = normalOpponentCount;
        TotalOpponentCount = totalOpponentCount;
        OpponentKind = opponentKind;
        IsBoss = isBoss;
        IsSectorBoss = isSectorBoss;
        OpponentCurrentHp = opponentCurrentHp;
        OpponentMaxHp = opponentMaxHp;
        OpponentHpPercentage = opponentHpPercentage;
        PlayerCurrentHp = playerCurrentHp;
        PlayerMaxHp = playerMaxHp;
        PlayerHpPercentage = playerHpPercentage;
        OpponentNameKey = opponentNameKey ?? string.Empty;
        OpponentDescKey = opponentDescKey ?? string.Empty;
        OpponentAssetPath = opponentAssetPath ?? string.Empty;
        OpponentScaleClass = opponentScaleClass ?? string.Empty;
        LastFeedback = lastFeedback;
        LastAppliedOpponentDamage = lastAppliedOpponentDamage;
        LastAppliedPlayerDamage = lastAppliedPlayerDamage;
        LastAppliedHealing = lastAppliedHealing;
        IsOpponentDefeated = isOpponentDefeated;
        IsGameOver = isGameOver;
        FeedbackRevision = feedbackRevision;
        TerminalSnapshot = terminalSnapshot;
    }

    public static CyberDefenseHudViewModel Initial() =>
        FromRunState(CyberDefenseRunState.InitialRun(), latestReceipt: null, feedbackRevision: 0);

    public static CyberDefenseHudViewModel FromRunState(
        CyberDefenseRunState runState,
        CyberDefenseReceiptRecord? latestReceipt = null,
        long feedbackRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(runState);

        int sector = runState.Sector;
        int opponentIndex = runState.OpponentIndex;
        int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
        var opponentKind = runState.CurrentOpponent.Kind;
        bool isBoss = opponentKind == OpponentKind.Boss;
        bool isSectorBoss = isBoss;

        int opponentCurrentHp = runState.CurrentOpponent.CurrentHp;
        int opponentMaxHp = runState.CurrentOpponent.MaxHp;
        int opponentHpPercentage = opponentMaxHp > 0
            ? (int)Math.Round((double)Math.Clamp(opponentCurrentHp, 0, opponentMaxHp) / opponentMaxHp * 100)
            : 0;

        int playerCurrentHp = runState.PlayerCurrentHp;
        int playerMaxHp = CyberDefenseCombatPolicy.PlayerMaxHp;
        int playerHpPercentage = playerMaxHp > 0
            ? (int)Math.Round((double)Math.Clamp(playerCurrentHp, 0, playerMaxHp) / playerMaxHp * 100)
            : 0;

        string opponentNameKey;
        string opponentDescKey;
        string opponentAssetPath;
        string opponentScaleClass;

        if (isBoss)
        {
            opponentNameKey = "CyberDefense_Opponent_NexusOverlord";
            opponentDescKey = "CyberDefense_Opponent_NexusOverlord_Desc";
            opponentAssetPath = "images/cyber-defense/nexus-overlord.svg";
            opponentScaleClass = CyberDefenseOpponentScalePolicy.SectorBossClass;
        }
        else
        {
            var profile = NormalOpponentProfiles[Math.Abs(opponentIndex) % NormalOpponentProfiles.Length];
            opponentNameKey = profile.NameKey;
            opponentDescKey = profile.DescKey;
            opponentAssetPath = profile.AssetPath;
            opponentScaleClass = opponentIndex switch
            {
                <= 1 => CyberDefenseOpponentScalePolicy.SmallClass,
                <= 3 => CyberDefenseOpponentScalePolicy.MediumSmallClass,
                <= 5 => CyberDefenseOpponentScalePolicy.MediumClass,
                _ => CyberDefenseOpponentScalePolicy.LargeClass
            };
        }

        CyberDefenseFeedbackKind lastFeedback = CyberDefenseFeedbackKind.None;
        int lastAppliedOpponentDamage = 0;
        int lastAppliedPlayerDamage = 0;
        int lastAppliedHealing = 0;
        bool isOpponentDefeated = false;
        bool isGameOver = false;
        CyberDefenseTerminalRunSnapshot? terminalSnapshot = null;

        if (latestReceipt != null &&
            latestReceipt.ReceiptKind == CyberDefenseReceiptKind.Applied &&
            latestReceipt.TransitionResult is { } tr)
        {
            lastAppliedOpponentDamage = tr.AppliedOpponentDamage;
            lastAppliedPlayerDamage = tr.AppliedPlayerDamage;
            lastAppliedHealing = tr.AppliedHealing;
            isOpponentDefeated = tr.IsOpponentDefeated;
            isGameOver = tr.IsGameOver;
            terminalSnapshot = tr.TerminalSnapshot;

            if (tr.AppliedOpponentDamage > 0)
            {
                lastFeedback = CyberDefenseFeedbackKind.Hit;
            }
        }

        return new CyberDefenseHudViewModel(
            sectorNumber: sector,
            opponentIndex: opponentIndex,
            normalOpponentCount: normalCount,
            totalOpponentCount: totalCount,
            opponentKind: opponentKind,
            isBoss: isBoss,
            isSectorBoss: isSectorBoss,
            opponentCurrentHp: opponentCurrentHp,
            opponentMaxHp: opponentMaxHp,
            opponentHpPercentage: opponentHpPercentage,
            playerCurrentHp: playerCurrentHp,
            playerMaxHp: playerMaxHp,
            playerHpPercentage: playerHpPercentage,
            opponentNameKey: opponentNameKey,
            opponentDescKey: opponentDescKey,
            opponentAssetPath: opponentAssetPath,
            opponentScaleClass: opponentScaleClass,
            lastFeedback: lastFeedback,
            lastAppliedOpponentDamage: lastAppliedOpponentDamage,
            lastAppliedPlayerDamage: lastAppliedPlayerDamage,
            lastAppliedHealing: lastAppliedHealing,
            isOpponentDefeated: isOpponentDefeated,
            isGameOver: isGameOver,
            feedbackRevision: feedbackRevision,
            terminalSnapshot: terminalSnapshot);
    }

    public static CyberDefenseHudViewModel FromEncounterState(CyberDefenseEncounterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int normalMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(state.SectorNumber, state.EnemyIndex);
        var opponentKind = state.IsBoss ? OpponentKind.Boss : OpponentKind.Normal;
        var opponent = new OpponentState(opponentKind, normalMaxHp, Math.Clamp(state.EnemyHitPoints, 1, normalMaxHp));
        var runState = CyberDefenseRunState.CreateActive(state.SectorNumber, state.EnemyIndex, CyberDefenseCombatPolicy.PlayerMaxHp, opponent);

        return FromRunState(runState, null, state.FeedbackRevision);
    }

    public bool Equals(CyberDefenseHudViewModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SectorNumber == other.SectorNumber &&
               OpponentIndex == other.OpponentIndex &&
               NormalOpponentCount == other.NormalOpponentCount &&
               TotalOpponentCount == other.TotalOpponentCount &&
               OpponentKind == other.OpponentKind &&
               IsBoss == other.IsBoss &&
               IsSectorBoss == other.IsSectorBoss &&
               OpponentCurrentHp == other.OpponentCurrentHp &&
               OpponentMaxHp == other.OpponentMaxHp &&
               OpponentHpPercentage == other.OpponentHpPercentage &&
               PlayerCurrentHp == other.PlayerCurrentHp &&
               PlayerMaxHp == other.PlayerMaxHp &&
               PlayerHpPercentage == other.PlayerHpPercentage &&
               OpponentNameKey == other.OpponentNameKey &&
               OpponentDescKey == other.OpponentDescKey &&
               OpponentAssetPath == other.OpponentAssetPath &&
               OpponentScaleClass == other.OpponentScaleClass &&
               LastFeedback == other.LastFeedback &&
               LastAppliedOpponentDamage == other.LastAppliedOpponentDamage &&
               LastAppliedPlayerDamage == other.LastAppliedPlayerDamage &&
               LastAppliedHealing == other.LastAppliedHealing &&
               IsOpponentDefeated == other.IsOpponentDefeated &&
               IsGameOver == other.IsGameOver &&
               FeedbackRevision == other.FeedbackRevision &&
               Equals(TerminalSnapshot, other.TerminalSnapshot);
    }

    public override bool Equals(object? obj) => Equals(obj as CyberDefenseHudViewModel);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SectorNumber);
        hash.Add(OpponentIndex);
        hash.Add(OpponentCurrentHp);
        hash.Add(PlayerCurrentHp);
        hash.Add(LastFeedback);
        hash.Add(FeedbackRevision);
        return hash.ToHashCode();
    }

    public static bool operator ==(CyberDefenseHudViewModel? left, CyberDefenseHudViewModel? right) => Equals(left, right);
    public static bool operator !=(CyberDefenseHudViewModel? left, CyberDefenseHudViewModel? right) => !Equals(left, right);
}
