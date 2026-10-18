using System.Text;
using System.Text.Json;

namespace CodexMonitor.Core;

/// <summary>Reads only account, model and token-count fields from Codex rollout JSONL files.</summary>
public sealed class TokenHistory
{
    private readonly string _sessionsDirectory;
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private readonly object _gate = new();
    private HistoryState _state;
    private bool _newSamples;

    public TokenHistory(string codexHome, string dataDirectory)
    {
        _sessionsDirectory = Path.Combine(codexHome, "sessions");
        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(dataDirectory, "token-history.json");
        try { _state = JsonSerializer.Deserialize<HistoryState>(File.ReadAllText(_statePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { _state = new(); }
        // Version 4 retains seven days and uses task start times to exclude idle gaps.
        if (_state.Version < 4) _state = new() { Version = 4 };
    }

    public string? LatestModel(string accountKey)
    {
        lock (_gate) return _state.LatestModels.TryGetValue(accountKey, out var item) ? item.Model : null;
    }

    /// <summary>
    /// Rate observed between the two most recent output-token checkpoints.
    /// Keep the observation steady between scans; once it is old, the live rate is unknown.
    /// </summary>
    public double? LatestTokensPerSecond(string accountKey, string model, DateTimeOffset now)
    {
        lock (_gate)
        {
            var sample = _state.Samples
                .Where(s => s.AccountKey == accountKey && s.Model == model && s.At <= now)
                .MaxBy(s => s.At);
            if (sample is null || now - sample.At > TimeSpan.FromSeconds(30)) return null;
            double seconds = (sample.At - sample.From).TotalSeconds;
            return seconds > 0 ? sample.Tokens / Math.Max(seconds, 1) : null;
        }
    }

    public double TokensPerSecond(string accountKey, string model, TimeSpan period, DateTimeOffset now)
    {
        if (period.TotalSeconds <= 0) return 0;
        var since = now - period;
        lock (_gate) return _state.Samples.Where(s => s.AccountKey == accountKey && s.Model == model && s.At > since && s.From < now)
            .Sum(s =>
            {
                double duration = (s.At - s.From).TotalSeconds;
                if (duration <= 0) return 0;
                var end = s.At < now ? s.At : now;
                var start = s.From > since ? s.From : since;
                double overlap = (end - start).TotalSeconds;
                return s.Tokens * Math.Clamp(overlap / duration, 0, 1);
            }) / period.TotalSeconds;
    }

    public IReadOnlyList<SpeedPoint> SpeedSeries(string accountKey, string model, TimeSpan period,
                                                 DateTimeOffset now, int maxPoints = 160)
    {
        if (period <= TimeSpan.Zero || maxPoints <= 0) return [];
        var since = now - period;
        lock (_gate)
        {
            var buckets = new (long Tokens, double Seconds, DateTimeOffset At)[maxPoints];
            foreach (var sample in _state.Samples)
            {
                if (sample.AccountKey != accountKey || sample.Model != model ||
                    sample.At < since || sample.At > now) continue;
                double elapsed = (sample.At - sample.From).TotalSeconds;
                if (elapsed <= 0) continue;
                int index = Math.Clamp((int)((sample.At - since).TotalSeconds / period.TotalSeconds * maxPoints),
                                       0, maxPoints - 1);
                buckets[index].Tokens += sample.Tokens;
                buckets[index].Seconds += Math.Max(elapsed, 1);
                if (sample.At > buckets[index].At) buckets[index].At = sample.At;
            }
            var result = new List<SpeedPoint>();
            foreach (var bucket in buckets)
                if (bucket.Seconds > 0)
                    result.Add(new SpeedPoint(bucket.At, bucket.Tokens / bucket.Seconds));
            return result;
        }
    }

    public bool Scan(DateTimeOffset now)
    {
        lock (_gate)
        {
            _newSamples = false;
            ScanCore(now);
            return _newSamples;
        }
    }

    private void ScanCore(DateTimeOffset now)
    {
        if (!Directory.Exists(_sessionsDirectory)) return;
        var cutoff = now.AddDays(-7);
        bool dirty = false;
        foreach (var path in Directory.EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime && !_state.Files.ContainsKey(path)) continue;
                if (!_state.Files.TryGetValue(path, out var checkpoint))
                {
                    _state.Files[path] = checkpoint = new SessionCheckpoint();
                    dirty = true;
                }
                if (info.Length < checkpoint.Offset)
                {
                    checkpoint.Offset = 0;
                    checkpoint.LastOutputTotal = 0;
                    checkpoint.LastCountAt = null;
                    checkpoint.LastTaskStartedAt = null;
                    dirty = true;
                }
                if (info.Length == checkpoint.Offset) continue;
                long previousOffset = checkpoint.Offset;
                ScanFile(path, checkpoint, cutoff);
                if (checkpoint.Offset != previousOffset) dirty = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* file rotated */ }
        }
        if (_state.Samples.RemoveAll(s => s.At < cutoff) > 0) dirty = true;
        foreach (var stale in _state.Files.Keys.Where(p => !File.Exists(p) ||
                                                           File.GetLastWriteTimeUtc(p) < cutoff.UtcDateTime).ToArray())
        {
            _state.Files.Remove(stale);
            dirty = true;
        }
        if (dirty) Save();
    }

    private void ScanFile(string path, SessionCheckpoint checkpoint, DateTimeOffset cutoff)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(checkpoint.Offset, SeekOrigin.Begin);
        var buffer = new byte[64 * 1024];
        using var lineBuffer = new MemoryStream();
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            long chunkStart = stream.Position - count;
            int start = 0;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] != (byte)'\n') continue;
                lineBuffer.Write(buffer, start, i - start);
                ProcessLine(lineBuffer.GetBuffer().AsSpan(0, (int)lineBuffer.Length), checkpoint, cutoff);
                lineBuffer.SetLength(0);
                checkpoint.Offset = chunkStart + i + 1;
                start = i + 1;
            }
            lineBuffer.Write(buffer, start, count - start);
            // Stop on an incomplete or unexpectedly enormous line. The next scan retries it.
            if (lineBuffer.Length > 32 * 1024 * 1024) break;
        }
    }

    private void ProcessLine(ReadOnlySpan<byte> bytes, SessionCheckpoint checkpoint, DateTimeOffset cutoff)
    {
        // Rollouts also contain prompts and tool output. Never persist or log them.
        if (bytes.IndexOf("session_meta"u8) < 0 && bytes.IndexOf("turn_context"u8) < 0 &&
            bytes.IndexOf("token_count"u8) < 0 && bytes.IndexOf("thread_settings_applied"u8) < 0 &&
            bytes.IndexOf("task_started"u8) < 0 && bytes.IndexOf("task_complete"u8) < 0 &&
            bytes.IndexOf("turn_aborted"u8) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray());
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var kind) || !root.TryGetProperty("payload", out var payload)) return;
            string? type = kind.GetString();
            if (type == "session_meta")
            {
                if (payload.TryGetProperty("creator_account_id", out var id) && id.ValueKind == JsonValueKind.String)
                    checkpoint.AccountKey = AccountIdentity.Hash(id.GetString()!);
                return;
            }
            if (type == "turn_context")
            {
                if (payload.TryGetProperty("model", out var modelValue) && modelValue.ValueKind == JsonValueKind.String)
                {
                    checkpoint.Model = modelValue.GetString();
                    if (checkpoint.Model!.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) &&
                        checkpoint.AccountKey is { } key && root.TryGetProperty("timestamp", out var at) &&
                        DateTimeOffset.TryParse(at.GetString(), out var date) &&
                        (!_state.LatestModels.TryGetValue(key, out var latest) || date > latest.At))
                        _state.LatestModels[key] = new(checkpoint.Model!, date);
                }
                return;
            }
            if (type == "event_msg" && payload.TryGetProperty("type", out var taskType))
            {
                string? taskEvent = taskType.GetString();
                if (taskEvent == "task_started")
                {
                    if (root.TryGetProperty("timestamp", out var startedAt) &&
                        DateTimeOffset.TryParse(startedAt.GetString(), out var date))
                        checkpoint.LastTaskStartedAt = date;
                    return;
                }
                if (taskEvent is "task_complete" or "turn_aborted")
                {
                    checkpoint.LastTaskStartedAt = null;
                    return;
                }
            }
            if (type == "event_msg" && payload.TryGetProperty("type", out var settingsType) &&
                settingsType.GetString() == "thread_settings_applied" &&
                payload.TryGetProperty("thread_settings", out var settings) &&
                settings.TryGetProperty("model", out var selectedModel) && selectedModel.ValueKind == JsonValueKind.String)
            {
                checkpoint.Model = selectedModel.GetString();
                if (checkpoint.Model!.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) &&
                    checkpoint.AccountKey is { } key && root.TryGetProperty("timestamp", out var at) &&
                    DateTimeOffset.TryParse(at.GetString(), out var date) &&
                    (!_state.LatestModels.TryGetValue(key, out var latest) || date > latest.At))
                    _state.LatestModels[key] = new(checkpoint.Model!, date);
                return;
            }
            if (type != "event_msg" || !payload.TryGetProperty("type", out var eventType) ||
                eventType.GetString() != "token_count" || checkpoint.AccountKey is null || checkpoint.Model is null) return;
            if (!payload.TryGetProperty("info", out var usage) || usage.ValueKind != JsonValueKind.Object ||
                !usage.TryGetProperty("total_token_usage", out var totalUsage) || totalUsage.ValueKind != JsonValueKind.Object ||
                !totalUsage.TryGetProperty("output_tokens", out var totalValue) || totalValue.ValueKind != JsonValueKind.Number ||
                !root.TryGetProperty("timestamp", out var timeValue) ||
                !DateTimeOffset.TryParse(timeValue.GetString(), out var timestamp)) return;
            long total = totalValue.GetInt64();
            var previousAt = checkpoint.LastCountAt;
            long delta = previousAt is null
                ? usage.TryGetProperty("last_token_usage", out var lastUsage) &&
                  lastUsage.ValueKind == JsonValueKind.Object &&
                  lastUsage.TryGetProperty("output_tokens", out var lastOutput) &&
                  lastOutput.ValueKind == JsonValueKind.Number ? lastOutput.GetInt64() : 0
                : total - checkpoint.LastOutputTotal;
            checkpoint.LastOutputTotal = total;
            checkpoint.LastCountAt = timestamp;
            // The first count can use the per-response usage if a task start is known.
            // After an idle gap, the task start excludes waiting time from the rate.
            DateTimeOffset? from = previousAt;
            if (checkpoint.LastTaskStartedAt is { } taskStart && (from is null || taskStart > from))
                from = taskStart;
            if (from is null || timestamp <= from || delta <= 0 || timestamp < cutoff) return;
            _state.Samples.Add(new(from.Value, timestamp, checkpoint.AccountKey, checkpoint.Model, delta));
            _newSamples = true;
        }
        catch (JsonException) { /* partial or unknown record */ }
    }

    private void Save()
    {
        var temp = _statePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, _jsonOptions), Encoding.UTF8);
        File.Move(temp, _statePath, true);
    }
}
