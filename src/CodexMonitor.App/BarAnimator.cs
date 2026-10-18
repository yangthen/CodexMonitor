using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexMonitor_App;

/// <summary>Animates new quota/speed values over 350 ms at display refresh cadence.</summary>
public sealed class BarAnimator : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Dictionary<QuotaBar, (double Target, double Current)> _bars = new();

    public BarAnimator()
    {
        _timer.Tick += (_, _) => Tick();
    }

    public void Set(QuotaBar bar, double value)
    {
        value = Math.Clamp(value, 0, 100);
        var current = _bars.TryGetValue(bar, out var existing) ? existing.Current : bar.Value;
        _bars[bar] = (value, current);
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Tick()
    {
        bool moving = false;
        foreach (var bar in _bars.Keys.ToArray())
        {
            var item = _bars[bar];
            double next = item.Current + (item.Target - item.Current) * 0.20;
            if (Math.Abs(item.Target - next) < 0.05) next = item.Target;
            else moving = true;
            bar.Value = next;
            _bars[bar] = (item.Target, next);
        }
        if (!moving) _timer.Stop();
    }

    public void Dispose() => _timer.Stop();
}
