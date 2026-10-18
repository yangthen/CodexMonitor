using System.Text.Json;

namespace CodexMonitor.Core;

public sealed class QuotaCache
{
    private readonly string _path;
    private readonly Dictionary<string, QuotaSnapshot> _snapshots;

    public QuotaCache(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "quota-cache.json");
        try { _snapshots = JsonSerializer.Deserialize<Dictionary<string, QuotaSnapshot>>(File.ReadAllText(_path)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { _snapshots = new(); }
    }

    public QuotaSnapshot? Get(string accountKey) => _snapshots.GetValueOrDefault(accountKey);

    public void Put(string accountKey, QuotaSnapshot snapshot)
    {
        _snapshots[accountKey] = snapshot;
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_snapshots));
        File.Move(temp, _path, true);
    }
}
