using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexMonitor.Core;

/// <summary>Small read-only JSON-RPC client for a private Codex app-server process.</summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Process? _process;
    private Task? _readerTask;
    private int _nextId;

    public event Action? RateLimitsChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process is { HasExited: false }) return;
        var start = new ProcessStartInfo(LocateCodex(), "app-server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        _process = Process.Start(start) ?? throw new InvalidOperationException("Codex app-server did not start.");
        _readerTask = Task.Run(() => ReadLoopAsync(_process));
        _ = Task.Run(async () => { try { await _process.StandardError.ReadToEndAsync(); } catch { } });
        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex_monitor", title = "Codex Monitor", version = "0.1.0" }
        }, cancellationToken);
        await SendAsync(new { method = "initialized", @params = new { } }, cancellationToken);
    }

    private static string LocateCodex()
    {
        string? configured = Environment.GetEnvironmentVariable("CODEX_MONITOR_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        string appBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(appBin))
        {
            string? candidate = Directory.EnumerateFiles(appBin, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (candidate is not null) return candidate;
        }
        return "codex";
    }

    public async Task<bool> IsChatGptAccountAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync("account/read", new { refreshToken = false }, cancellationToken);
        if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object) return false;
        return account.TryGetProperty("type", out var type) && type.GetString() == "chatgpt";
    }

    public async Task<QuotaSnapshot> ReadQuotasAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync("account/rateLimits/read", null, cancellationToken);
        return QuotaParser.Parse(result, DateTimeOffset.UtcNow);
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await SendAsync(new { method, id, @params = parameters }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_process is null || _process.HasExited) throw new IOException("Codex app-server has stopped.");
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally { _sendLock.Release(); }
    }

    private async Task ReadLoopAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number &&
                        _pending.TryGetValue(id.GetInt32(), out var pending))
                    {
                        if (root.TryGetProperty("error", out var error))
                            pending.TrySetException(new InvalidOperationException(error.ToString()));
                        else if (root.TryGetProperty("result", out var result)) pending.TrySetResult(result.Clone());
                    }
                    else if (root.TryGetProperty("method", out var method) &&
                             method.GetString() == "account/rateLimits/updated") RateLimitsChanged?.Invoke();
                }
                catch (JsonException) { /* ignore non-protocol diagnostic output */ }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Codex app-server disconnected."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        if (_readerTask is not null) await _readerTask;
        _process.Dispose();
        _sendLock.Dispose();
    }
}
