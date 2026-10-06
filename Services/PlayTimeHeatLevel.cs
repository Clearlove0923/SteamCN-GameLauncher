namespace SteamCNGameLauncher.Services;

/// <summary>按单日游戏时长划分热力图颜色等级。</summary>
internal enum PlayTimeHeatLevel
{
    None,
    Short,
    Medium,
    Long,
}

internal static class PlayTimeHeatLevelClassifier
{
    public static PlayTimeHeatLevel Classify(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return PlayTimeHeatLevel.None;
        if (duration < TimeSpan.FromHours(1)) return PlayTimeHeatLevel.Short;
        if (duration < TimeSpan.FromHours(3)) return PlayTimeHeatLevel.Medium;
        return PlayTimeHeatLevel.Long;
    }
}
