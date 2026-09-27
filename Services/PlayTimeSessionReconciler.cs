namespace SteamCNGameLauncher.Services;

public sealed record PlaySession(string PresetId, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
    string LaunchCategory, int? ProcessId = null, DateTimeOffset? ProcessStartedAt = null);

/// <summary>Joins observations of one OS process into one persisted play session.</summary>
public static class PlayTimeSessionReconciler
{
    public static void Observe(List<PlaySession> sessions, string presetId, int pid,
        DateTimeOffset processStart, DateTimeOffset observedAt, string category)
    {
        if (processStart > observedAt) processStart = observedAt;
        var index = sessions.FindLastIndex(x => x.PresetId == presetId &&
            x.ProcessId == pid && x.ProcessStartedAt == processStart);
        if (index >= 0)
        {
            sessions[index] = sessions[index] with { EndedAt = observedAt };
            return;
        }

        // Older records had no process identity. Any such intervals inside the current
        // process lifetime can be joined without counting the same minutes twice.
        var legacy = sessions.Where(x => x.PresetId == presetId && x.ProcessId is null &&
            x.StartedAt >= processStart && x.EndedAt <= observedAt).ToArray();
        if (legacy.Length > 0)
        {
            category = legacy.OrderBy(x => x.StartedAt).First().LaunchCategory;
            sessions.RemoveAll(x => legacy.Contains(x));
        }
        sessions.Add(new PlaySession(presetId, processStart, observedAt, category, pid, processStart));
    }
}
