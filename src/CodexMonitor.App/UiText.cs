using System.Globalization;

namespace CodexMonitor_App;

internal static class UiText
{
    // CurrentUICulture follows the Windows user's UI language, not the regional number format.
    public static bool IsChinese { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
        .Equals("zh", StringComparison.OrdinalIgnoreCase);

    private static string Pick(string english, string chinese) => IsChinese ? chinese : english;

    public static string Settings => Pick("Settings", "设置");
    public static string Close => Pick("Close", "关闭");
    public static string AlwaysOnTop => Pick("Always on top", "始终保持在最顶层");
    public static string RefreshFrequency => Pick("Refresh interval", "刷新频率");
    public static string Details => Pick("Details  ›", "详情  ›");
    public static string FiveHours => Pick("5 hours", "5小时");
    public static string Week => Pick("Week", "每周");
    public static string TokenSpeed => Pick("Token speed", "Token 速度");
    public static string NoActiveModel => Pick("No active model", "暂无活动模型");
    public static string NoSpeedData => Pick("No speed data", "暂无速度数据");
    public static string Now => Pick("Now", "现在");
    public static string NoChatGptAccount => Pick("No ChatGPT account", "未登录 ChatGPT 账户");
    public static string SessionDataUnavailable => Pick("Session data unavailable", "会话数据不可用");
    public static string SignInWithChatGpt => Pick("Sign in with ChatGPT", "请登录 ChatGPT");
    public static string WaitingForQuota => Pick("Waiting for quota data", "正在等待配额数据");
    public static string QuotaUnavailable => Pick("Quota unavailable", "配额数据不可用");
    public static string UnavailableForPlan => Pick("Unavailable for this plan", "当前套餐无此配额");
    public static string QuotaDataStale => Pick("Quota data stale", "配额数据已过期");
    public static string ResetTimeUnavailable => Pick("Reset time unavailable", "重置时间不可用");
    public static string SpeedUnavailable => Pick("speed unavailable", "速度不可用");
    public static string QuotaFetchFailed => Pick("Quota fetch failed", "配额获取失败");
    public static string ShowHide => Pick("Show / Hide", "显示 / 隐藏");
    public static string RefreshNow => Pick("Refresh now", "立即刷新");
    public static string Exit => Pick("Exit", "退出");
    public static string CachedSuffix => Pick(" · cached", " · 缓存数据");

    public static string Account(string key) => Pick("Account ", "账户 ") + key;
    public static string Model(string model) => Pick("Model: ", "模型：") + model;
    public static string PercentUsed(double percent) => IsChinese
        ? $"已用 {percent:0.#}%" : $"{percent:0.#}% used";
    public static string QuotaTooltip(double percent, bool cached) => IsChinese
        ? $"已用 {percent:0.#}% · 剩余 {100 - percent:0.#}%{(cached ? CachedSuffix : "")}"
        : $"{percent:0.#}% used · {100 - percent:0.#}% remaining{(cached ? CachedSuffix : "")}";

    public static string ResetTime(TimeSpan? remaining)
    {
        if (remaining is null) return ResetTimeUnavailable;
        if (remaining <= TimeSpan.Zero) return Pick("Resets soon", "即将重置");

        TimeSpan value = remaining.Value;
        string duration = value.TotalDays >= 1
            ? IsChinese ? $"{(int)value.TotalDays}天{value.Hours}小时" : $"{(int)value.TotalDays}d {value.Hours}h"
            : value.TotalHours >= 1
                ? IsChinese ? $"{(int)value.TotalHours}小时{value.Minutes}分" : $"{(int)value.TotalHours}h {value.Minutes}m"
                : IsChinese ? $"{Math.Max(1, (int)Math.Ceiling(value.TotalMinutes))}分钟"
                            : $"{Math.Max(1, (int)Math.Ceiling(value.TotalMinutes))}m";
        return IsChinese ? duration + "后重置" : "Resets in " + duration;
    }

    public static string Period(TimeSpan period) => period.TotalDays switch
    {
        >= 7 => Pick("7 days", "7天"),
        >= 3 => Pick("3 days", "3天"),
        >= 1 => Pick("24 hours", "24小时"),
        _ when period.TotalHours >= 1 => Pick("1 hour", "1小时"),
        _ when period.TotalMinutes >= 15 => Pick("15 min", "15分钟"),
        _ => Pick("5 min", "5分钟")
    };

    public static string Ago(TimeSpan period) => IsChinese ? Period(period) + "前" : Period(period) + " ago";
}
