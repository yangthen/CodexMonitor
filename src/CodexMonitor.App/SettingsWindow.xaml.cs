using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CodexMonitor_App;

public sealed partial class SettingsWindow : Window
{
    private readonly MonitorSettings _settings;
    private readonly Action _settingsChanged;
    private readonly OverlappedPresenter _presenter;

    public SettingsWindow(MonitorSettings settings, Action settingsChanged)
    {
        InitializeComponent();
        AppWindow.IsShownInSwitchers = false;
        _settings = settings;
        _settingsChanged = settingsChanged;
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsResizable = false;
        _presenter.IsAlwaysOnTop = settings.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(370 * scale), (int)Math.Round(184 * scale)));
        WindowChrome.ApplyRoundedBorderless(this);
        AppWindow.Changed += (_, args) => { if (args.DidSizeChange) WindowChrome.ApplyRoundedBorderless(this); };
        Activated += (_, _) => DispatcherQueue.TryEnqueue(() => WindowChrome.ApplyRoundedBorderless(this));

        AlwaysOnTopSwitch.IsOn = settings.AlwaysOnTop;
        AlwaysOnTopSwitch.Toggled += AlwaysOnTopSwitch_Toggled;
        RefreshIntervalBox.Value = settings.RefreshIntervalSeconds;
        RefreshIntervalBox.ValueChanged += RefreshIntervalBox_ValueChanged;
    }

    public void SetTopmost(bool value)
    {
        _presenter.IsAlwaysOnTop = value;
        WindowChrome.ApplyRoundedBorderless(this);
    }

    public void ShowNear(PointInt32 anchor, int anchorWidth, int anchorHeight)
    {
        PositionNear(anchor, anchorWidth, anchorHeight);
        Activate();
    }

    public void PositionNear(PointInt32 anchor, int anchorWidth, int anchorHeight)
    {
        var area = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Primary).WorkArea;
        int x = Math.Clamp(anchor.X + anchorWidth - AppWindow.Size.Width,
                           area.X, area.X + area.Width - AppWindow.Size.Width);
        int below = anchor.Y + anchorHeight + 6;
        int above = anchor.Y - AppWindow.Size.Height - 6;
        int y = below + AppWindow.Size.Height <= area.Y + area.Height ? below : above;
        AppWindow.Move(new PointInt32(x,
            Math.Clamp(y, area.Y, area.Y + area.Height - AppWindow.Size.Height)));
    }

    private void AlwaysOnTopSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = AlwaysOnTopSwitch.IsOn;
        _settingsChanged();
    }

    private void RefreshIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (!double.IsFinite(e.NewValue)) return;
        int seconds = (int)Math.Round(Math.Clamp(e.NewValue, 1, 99999));
        if (RefreshIntervalBox.Value != seconds) RefreshIntervalBox.Value = seconds;
        if (_settings.RefreshIntervalSeconds == seconds) return;
        _settings.RefreshIntervalSeconds = seconds;
        _settingsChanged();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
