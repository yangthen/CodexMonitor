// See https://aka.ms/new-console-template for more information
using CodexMonitor.Core;
using System.Text.Json;

if (args.Contains("--self-test"))
{
    RunSelfTest();
    return;
}

try
{
    await using var client = new CodexAppServerClient();
    await client.StartAsync();
    if (!await client.IsChatGptAccountAsync())
    {
        Console.WriteLine("Codex is not signed in with a ChatGPT account.");
        return;
    }
    var quota = await client.ReadQuotasAsync();
    Console.WriteLine($"5hours: {Format(quota.FiveHours)}");
    Console.WriteLine($"week:   {Format(quota.Week)}");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}

static string Format(QuotaWindow? window) => window is null
    ? "not returned for this account"
    : $"{window.UsedPercent:0.#}% used; resets {window.ResetsAt?.ToLocalTime():g}";

static void RunSelfTest()
{
    string root = Path.Combine(AppContext.BaseDirectory, "CodexMonitorTest-" + Guid.NewGuid().ToString("N"));
    string sessions = Path.Combine(root, "sessions");
    Directory.CreateDirectory(sessions);
    try
    {
        var now = DateTimeOffset.UtcNow;
        string[] first =
        [
            Line(now.AddSeconds(-40), "session_meta", new { creator_account_id = "account-a" }),
            Line(now.AddSeconds(-30), "turn_context", new { model = "gpt-6.1-sol" }),
            Line(now.AddSeconds(-22), "event_msg", new { type = "task_started" }),
            Line(now.AddSeconds(-20), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 50000, output_tokens = 600 }, last_token_usage = new { output_tokens = 60 } } }),
            Line(now.AddSeconds(-17), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 75000, output_tokens = 660 } } }),
            Line(now.AddSeconds(-15), "event_msg", new { type = "thread_settings_applied", thread_settings = new { model = "gpt-6-sol" } }),
            Line(now.AddSeconds(-13), "event_msg", new { type = "task_started" }),
            Line(now.AddSeconds(-10), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 150000, output_tokens = 900 } } }),
            Line(now.AddSeconds(-4), "event_msg", new { type = "task_started" }),
            Line(now.AddSeconds(-2), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 225000, output_tokens = 990 } } })
        ];
        string[] second =
        [
            Line(now.AddSeconds(-35), "session_meta", new { creator_account_id = "account-b" }),
            Line(now.AddSeconds(-25), "turn_context", new { model = "gpt-6.1-sol" }),
            Line(now.AddSeconds(-10), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 40000, output_tokens = 120 } } }),
            Line(now.AddSeconds(-5), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 90000, output_tokens = 180 } } })
        ];
        File.WriteAllLines(Path.Combine(sessions, "a.jsonl"), first);
        File.WriteAllLines(Path.Combine(sessions, "b.jsonl"), second);
        File.WriteAllLines(Path.Combine(sessions, "older.jsonl"),
        [
            Line(now.AddDays(-3).AddSeconds(-10), "session_meta", new { creator_account_id = "account-a" }),
            Line(now.AddDays(-3).AddSeconds(-9), "turn_context", new { model = "gpt-6-sol" }),
            Line(now.AddDays(-3).AddSeconds(-8), "event_msg", new { type = "task_started" }),
            Line(now.AddDays(-3), "event_msg", new { type = "token_count", info = new { total_token_usage = new { output_tokens = 100 }, last_token_usage = new { output_tokens = 100 } } })
        ]);
        var history = new TokenHistory(root, Path.Combine(root, "data"));
        Check(history.Scan(now), "new samples detected");
        Check(!history.Scan(now), "unchanged scan has no new samples");
        string a = AccountIdentity.Hash("account-a"), b = AccountIdentity.Hash("account-b");
        Check(history.LatestModel(a) == "gpt-6-sol", "model switching");
        Check(history.TokensPerSecond(a, "gpt-6.1-sol", TimeSpan.FromMinutes(15), now) == 120.0 / 900, "old model history");
        Check(history.TokensPerSecond(a, "gpt-6-sol", TimeSpan.FromMinutes(15), now) == 330.0 / 900, "new model history");
        Check(history.TokensPerSecond(b, "gpt-6.1-sol", TimeSpan.FromMinutes(15), now) == 60.0 / 900, "account partition");
        Check(history.TokensPerSecond(a, "gpt-6-sol", TimeSpan.FromSeconds(10), now) == 90.0 / 10, "recent output speed");
        Check(history.LatestTokensPerSecond(a, "gpt-6.1-sol", now.AddSeconds(-19)) == 60.0 / 2,
              "first response uses its own usage");
        Check(history.LatestTokensPerSecond(a, "gpt-6-sol", now.AddSeconds(-9)) == 240.0 / 3,
              "idle gap excluded by task start");
        Check(history.LatestTokensPerSecond(a, "gpt-6-sol", now) == 90.0 / 2, "latest checkpoint interval");
        Check(history.LatestTokensPerSecond(a, "gpt-6-sol", now.AddSeconds(27)) == 90.0 / 2,
              "speed stays steady without new samples");
        Check(history.LatestTokensPerSecond(a, "gpt-6-sol", now.AddSeconds(29)) is null,
              "stale speed becomes unknown");
        Check(history.SpeedSeries(a, "gpt-6-sol", TimeSpan.FromDays(7), now).Count >
              history.SpeedSeries(a, "gpt-6-sol", TimeSpan.FromHours(24), now).Count,
              "seven-day chart includes older samples");
        File.AppendAllLines(Path.Combine(sessions, "a.jsonl"),
        [
            Line(now.AddSeconds(1), "event_msg", new { type = "task_started" }),
            Line(now.AddSeconds(4), "event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 230000, output_tokens = 1030 } } })
        ]);
        history.Scan(now.AddSeconds(5));
        Check(Math.Abs(history.LatestTokensPerSecond(a, "gpt-6-sol", now.AddSeconds(5))!.Value - 40.0 / 3) < 0.0001,
              "new checkpoint replaces the displayed speed");
        using var quotaDoc = JsonDocument.Parse("""
            {"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":99,"windowDurationMins":300}},
                                     "codex":{"primary":{"usedPercent":76,"windowDurationMins":300},
                                              "secondary":{"usedPercent":51,"windowDurationMins":10080}}}}
            """);
        var parsed = QuotaParser.Parse(quotaDoc.RootElement, now);
        Check(parsed.FiveHours?.UsedPercent == 76 && parsed.Week?.UsedPercent == 51,
              "quota parser selects Codex bucket rather than another limit");
        Console.WriteLine("PASS: quota bucket, seven-day history, account partition, model switch, active timing, stable live rate, expiry, no double count");
    }
    finally
    {
        string full = Path.GetFullPath(root);
        if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("CodexMonitorTest-", StringComparison.Ordinal))
            Directory.Delete(full, recursive: true);
    }
}

static string Line(DateTimeOffset at, string type, object payload) =>
    JsonSerializer.Serialize(new { timestamp = at, type, payload });

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("Self-test failed: " + description);
}
