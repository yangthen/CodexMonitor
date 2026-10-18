using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexMonitor.Core;

public sealed record QuotaWindow(double UsedPercent, DateTimeOffset? ResetsAt, int? WindowMinutes)
{
    public static QuotaWindow? FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        double percent = element.TryGetProperty("usedPercent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
        int? minutes = element.TryGetProperty("windowDurationMins", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : null;
        DateTimeOffset? reset = element.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null;
        return new(Math.Clamp(percent, 0, 100), reset, minutes);
    }
}

public sealed record QuotaSnapshot(QuotaWindow? FiveHours, QuotaWindow? Week, DateTimeOffset FetchedAt);

public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement result, DateTimeOffset now)
    {
        // A quota bucket has primary and secondary windows. Their durations identify the
        // display rows; bucket names are not guaranteed to stay stable between plans.
        var candidates = new List<QuotaWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            if (buckets.TryGetProperty("codex", out var codex)) AddWindows(codex, candidates);
        }
        if (candidates.Count == 0 && result.TryGetProperty("rateLimits", out var legacy)) AddWindows(legacy, candidates);

        QuotaWindow? five = candidates.Where(x => x.WindowMinutes is >= 240 and <= 420)
            .OrderBy(x => Math.Abs(x.WindowMinutes!.Value - 300)).FirstOrDefault();
        QuotaWindow? week = candidates.Where(x => x.WindowMinutes is >= 7200 and <= 11520)
            .OrderBy(x => Math.Abs(x.WindowMinutes!.Value - 10080)).FirstOrDefault();
        return new(five, week, now);
    }

    private static void AddWindows(JsonElement bucket, List<QuotaWindow> target)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return;
        foreach (var name in new[] { "primary", "secondary" })
            if (bucket.TryGetProperty(name, out var value) && QuotaWindow.FromJson(value) is { } window)
                target.Add(window);
    }
}

public static class AccountIdentity
{
    public static string? ReadKey(string codexHome)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(codexHome, "auth.json")));
            var root = document.RootElement;
            if (!root.TryGetProperty("tokens", out var tokens) ||
                !tokens.TryGetProperty("account_id", out var id) || id.ValueKind != JsonValueKind.String)
                return null;
            return Hash(id.GetString()!);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
}

public sealed record TokenSample(DateTimeOffset From, DateTimeOffset At, string AccountKey, string Model, long Tokens);

public sealed record SpeedPoint(DateTimeOffset At, double TokensPerSecond);

public sealed class SessionCheckpoint
{
    public long Offset { get; set; }
    public long LastOutputTotal { get; set; }
    public DateTimeOffset? LastCountAt { get; set; }
    public DateTimeOffset? LastTaskStartedAt { get; set; }
    public string? AccountKey { get; set; }
    public string? Model { get; set; }
}

public sealed class HistoryState
{
    public int Version { get; set; }
    public Dictionary<string, SessionCheckpoint> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TokenSample> Samples { get; set; } = [];
    public Dictionary<string, ModelActivity> LatestModels { get; set; } = new();
}

public sealed record ModelActivity(string Model, DateTimeOffset At);
