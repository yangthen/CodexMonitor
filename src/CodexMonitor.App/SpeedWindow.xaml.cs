using CodexMonitor.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace CodexMonitor_App;

public sealed partial class SpeedWindow : Window
{
    private readonly OverlappedPresenter _presenter;
    private readonly Button[] _rangeButtons;
    private TimeSpan _period = TimeSpan.FromMinutes(15);
    private TokenHistory? _history;
    private string? _accountKey;
    private string? _model;
    private DateTimeOffset _now;

    public SpeedWindow(bool alwaysOnTop)
    {
        InitializeComponent();
        _rangeButtons = [Range15m, Range1h, Range24h, Range3d, Range7d];
        UpdateRangeButtons();
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsResizable = false;
        _presenter.IsAlwaysOnTop = alwaysOnTop;
        AppWindow.SetPresenter(_presenter);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(490 * scale), (int)Math.Round(350 * scale)));
        WindowChrome.ApplyRoundedBorderless(this);
        AppWindow.Changed += (_, args) => { if (args.DidSizeChange) WindowChrome.ApplyRoundedBorderless(this); };
        Activated += (_, _) => DispatcherQueue.TryEnqueue(() => WindowChrome.ApplyRoundedBorderless(this));
    }

    public void SetTopmost(bool value)
    {
        _presenter.IsAlwaysOnTop = value;
        WindowChrome.ApplyRoundedBorderless(this);
    }

    public void ShowNear(AppWindow parent)
    {
        PositionNear(parent);
        Activate();
    }

    public void PositionNear(AppWindow parent)
    {
        var area = DisplayArea.GetFromWindowId(parent.Id, DisplayAreaFallback.Primary).WorkArea;
        int x = Math.Clamp(parent.Position.X + parent.Size.Width - AppWindow.Size.Width,
                           area.X, area.X + area.Width - AppWindow.Size.Width);
        int below = parent.Position.Y + parent.Size.Height + 6;
        int above = parent.Position.Y - AppWindow.Size.Height - 6;
        int y = below + AppWindow.Size.Height <= area.Y + area.Height ? below : above;
        y = Math.Clamp(y, area.Y, area.Y + area.Height - AppWindow.Size.Height);
        AppWindow.Move(new PointInt32(x, y));
    }

    public void Update(TokenHistory history, string? accountKey, string? model, DateTimeOffset now)
    {
        _history = history;
        _accountKey = accountKey;
        _model = model;
        _now = now;
        ModelName.Text = model ?? "No active model";
        RenderChart();
    }

    private void RangeButton_Click(object sender, RoutedEventArgs e)
    {
        _period = (sender as Button)?.Tag?.ToString() switch
        {
            "1h" => TimeSpan.FromHours(1),
            "24h" => TimeSpan.FromHours(24),
            "3d" => TimeSpan.FromDays(3),
            "7d" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromMinutes(15)
        };
        UpdateRangeButtons();
        RenderChart();
    }

    private void UpdateRangeButtons()
    {
        foreach (var button in _rangeButtons)
        {
            bool selected = button.Tag?.ToString() switch
            {
                "1h" => _period == TimeSpan.FromHours(1),
                "24h" => _period == TimeSpan.FromHours(24),
                "3d" => _period == TimeSpan.FromDays(3),
                "7d" => _period == TimeSpan.FromDays(7),
                _ => _period == TimeSpan.FromMinutes(15)
            };
            button.Background = new SolidColorBrush(selected ? Color.FromArgb(255, 29, 69, 80) : Color.FromArgb(0, 0, 0, 0));
            button.Foreground = new SolidColorBrush(selected ? Color.FromArgb(255, 54, 215, 234) : Color.FromArgb(255, 175, 191, 200));
            button.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 54, 215, 234) : Color.FromArgb(255, 54, 67, 78));
            button.BorderThickness = new Thickness(1);
        }
    }

    private void RenderChart()
    {
        var points = _history is not null && _accountKey is not null && _model is not null
            ? _history.SpeedSeries(_accountKey, _model, _period, _now)
            : [];
        DetailChart.SetData(points, _period, _now);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
