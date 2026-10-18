using System.Runtime.InteropServices;
using System.Text.Json;
using CodexMonitor.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace CodexMonitor_App;

public sealed partial class MainWindow : Window
{
    private readonly string _codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private readonly string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMonitor");
    private readonly TokenHistory _history;
    private readonly QuotaCache _quotaCache;
    private readonly NativeTrayIcon _tray;
    private readonly MonitorSettings _settings;
    private readonly OverlappedPresenter _presenter;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _quotaTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly BarAnimator _bars = new();
    private CodexAppServerClient? _client;
    private SpeedWindow? _speedWindow;
    private SettingsWindow? _settingsWindow;
    private PointInt32 _dragStartWindow;
    private NativePoint _dragStartCursor;
    private RectInt32 _resizeStartRect;
    private NativePoint _resizeStartCursor;
    private int _resizeHit;
    private bool _dragging;
    private bool _resizing;
    private QuotaSnapshot? _quota;
    private string? _accountKey;
    private string? _serverAccountKey;
    private string? _model;
    private bool _scanBusy;
    private bool _quotaBusy;
    private bool _quotaRefreshQueued;
    private bool _quitting;
    private string? _quotaError;

    public MainWindow()
    {
        InitializeComponent();
        DragSurface.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(DragSurface_PointerPressed), handledEventsToo: true);
        DragSurface.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(DragSurface_PointerMoved), handledEventsToo: true);
        DragSurface.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(DragSurface_PointerReleased), handledEventsToo: true);
        DragSurface.PointerEntered += DragSurface_PointerMoved;
        DragSurface.PointerCanceled += DragSurface_PointerReleased;
        _dragTimer.Tick += (_, _) => PollPointerAction();
        _settings = MonitorSettings.Load(_dataDirectory);
        _history = new TokenHistory(_codexHome, _dataDirectory);
        _quotaCache = new QuotaCache(_dataDirectory);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.IsShownInSwitchers = false;
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsResizable = false;
        _presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        UpdateBarHeights(scale);
        _presenter.PreferredMinimumWidth = (int)Math.Round(380 * scale);
        _presenter.PreferredMinimumHeight = (int)Math.Round(320 * scale);
        AppWindow.Resize(new SizeInt32((int)Math.Round(440 * scale), (int)Math.Round(334 * scale)));
        WindowChrome.ApplyRoundedBorderless(this);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange) WindowChrome.ApplyRoundedBorderless(this);
            if (args.DidPositionChange || args.DidSizeChange)
                UpdateBarHeights(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0);
        };
        Activated += (_, _) => DispatcherQueue.TryEnqueue(() => WindowChrome.ApplyRoundedBorderless(this));
        RestorePosition();

        _tray = new NativeTrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), iconPath,
            ToggleVisible, () => _ = RefreshQuotaAsync(), Exit);

        _displayTimer.Tick += (_, _) => UpdateDisplay();
        _scanTimer.Tick += (_, _) => _ = ScanHistoryAsync();
        _quotaTimer.Tick += (_, _) => _ = RefreshQuotaAsync();
        _displayTimer.Start();
        _scanTimer.Start();
        _quotaTimer.Start();
        Closed += OnClosed;
        UpdateAccount();
        UpdateDisplay();
        _ = ScanHistoryAsync();
        _ = RefreshQuotaAsync();
    }

    private async Task ScanHistoryAsync()
    {
        if (_scanBusy || _quitting) return;
        _scanBusy = true;
        try
        {
            bool newSamples = await Task.Run(() => _history.Scan(DateTimeOffset.UtcNow));
            UpdateAccount();
            UpdateDisplay();
            if (newSamples) _ = RefreshQuotaAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ModelText.Text = "Session data unavailable"; }
        finally { _scanBusy = false; }
    }

    private void UpdateAccount()
    {
        string? key = AccountIdentity.ReadKey(_codexHome);
        if (key == _accountKey) return;
        _accountKey = key;
        _quota = key is null ? null : _quotaCache.Get(key);
        _model = key is null ? null : _history.LatestModel(key);
        AccountText.Text = key is null ? "No ChatGPT account" : "Account " + key[..6];
        if (key is not null) _ = RefreshQuotaAsync();
    }

    private async Task RefreshQuotaAsync()
    {
        if (_quitting || _accountKey is null) return;
        if (_quotaBusy) { _quotaRefreshQueued = true; return; }
        _quotaBusy = true;
        string accountKey = _accountKey;
        try
        {
            if (_client is null || _serverAccountKey != accountKey)
            {
                if (_client is not null) await _client.DisposeAsync();
                _client = new CodexAppServerClient();
                _client.RateLimitsChanged += () => DispatcherQueue.TryEnqueue(() => _ = RefreshQuotaAsync());
                await _client.StartAsync();
                _serverAccountKey = accountKey;
            }
            if (!await _client.IsChatGptAccountAsync())
            {
                FiveReset.Text = "Sign in with ChatGPT";
                WeekReset.Text = "Sign in with ChatGPT";
                return;
            }
            var snapshot = await _client.ReadQuotasAsync();
            if (snapshot.FiveHours is null && snapshot.Week is null)
                throw new InvalidOperationException("Codex returned no rate-limit windows.");
            if (_accountKey != accountKey) return;
            _quota = snapshot;
            _quotaError = null;
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(AccountText, null);
            _quotaCache.Put(accountKey, snapshot);
            UpdateDisplay();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            _quotaError = e.Message;
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(AccountText, "Quota: " + _quotaError);
            try { File.AppendAllText(Path.Combine(_dataDirectory, "monitor.log"), $"{DateTimeOffset.Now:O} quota: {e.GetType().Name}: {e.Message}{Environment.NewLine}"); } catch (IOException) { }
            if (_client is not null) await _client.DisposeAsync();
            _client = null;
            _serverAccountKey = null;
            UpdateDisplay();
        }
        finally
        {
            _quotaBusy = false;
            bool refreshAgain = _quotaRefreshQueued || (_accountKey is not null && _accountKey != accountKey);
            _quotaRefreshQueued = false;
            if (!_quitting && refreshAgain)
                _ = RefreshQuotaAsync();
        }
    }

    private void UpdateDisplay()
    {
        UpdateAccount();
        var now = DateTimeOffset.UtcNow;
        SetQuota(_quota?.FiveHours, FivePercent, FiveReset, FiveBar, now, _quota?.FetchedAt);
        SetQuota(_quota?.Week, WeekPercent, WeekReset, WeekBar, now, _quota?.FetchedAt);
        _model = _accountKey is null ? null : _history.LatestModel(_accountKey);
        double? speed = _accountKey is not null && _model is not null
            ? _history.LatestTokensPerSecond(_accountKey, _model, now) : null;
        SpeedText.Text = speed is { } rate ? $"{rate:0.0} tokens/s" : "--";
        ModelText.Text = _model is null ? "" : "Model: " + _model;
        ToolTipService.SetToolTip(ModelText, _model);
        MainSpeedChart.SetData(_accountKey is not null && _model is not null
            ? _history.SpeedSeries(_accountKey, _model, TimeSpan.FromMinutes(5), now)
            : [], TimeSpan.FromMinutes(5), now);
        _speedWindow?.Update(_history, _accountKey, _model, now);
        string tip = _model is null ? "Codex Monitor" :
            $"Codex Monitor | {_model} | {(speed is { } value ? $"{value:0.0} tokens/s" : "speed unavailable")}";
        _tray.SetTooltip(tip);
    }

    private void UpdateBarHeights(double scale)
    {
        if (scale <= 0) return;
        double height = 8 / scale;
        if (Math.Abs(FiveBar.Height - height) < 0.01) return;
        FiveBar.Height = height;
        WeekBar.Height = height;
    }

    private void SetQuota(QuotaWindow? quota, TextBlock percentage, TextBlock reset,
                          QuotaBar bar, DateTimeOffset now, DateTimeOffset? fetched)
    {
        if (quota is null)
        {
            percentage.Text = "--";
            reset.Text = _quota is null ? (_quotaError is null ? "Waiting for quota data" : "Quota unavailable") : "Unavailable for this plan";
            _bars.Set(bar, 0);
            return;
        }
        if (fetched is { } lastCheck && now - lastCheck > TimeSpan.FromMinutes(2))
        {
            percentage.Text = "--";
            reset.Text = "Quota data stale · checked " + lastCheck.ToLocalTime().ToString("HH:mm");
            _bars.Set(bar, 0);
            return;
        }
        percentage.Text = $"{quota.UsedPercent:0.#}% used";
        string checkedAt = fetched is { } time ? " · checked " + time.ToLocalTime().ToString("HH:mm") : "";
        string cached = _quotaError is null ? "" : " · cached";
        reset.Text = (quota.ResetsAt is { } at ? "Resets in " + FormatRemaining(at - now) : "Reset time unavailable")
                     + checkedAt + cached;
        ToolTipService.SetToolTip(percentage, $"{quota.UsedPercent:0.#}% used · {100 - quota.UsedPercent:0.#}% remaining{checkedAt}{cached}");
        _bars.Set(bar, quota.UsedPercent);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "soon";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }

    private void SpeedRegion_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_speedWindow is not null) { _speedWindow.Close(); _speedWindow = null; return; }
        _settingsWindow?.Close();
        _speedWindow = new SpeedWindow(_settings.AlwaysOnTop);
        _speedWindow.Closed += (_, _) => _speedWindow = null;
        _speedWindow.ShowNear(AppWindow);
        _speedWindow.Update(_history, _accountKey, _model, DateTimeOffset.UtcNow);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _speedWindow?.Close();
        _settingsWindow = new SettingsWindow(_settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        var anchor = GetSettingsAnchor();
        _settingsWindow.ShowNear(anchor.Point, anchor.Width, anchor.Height);
    }

    private (PointInt32 Point, int Width, int Height) GetSettingsAnchor()
    {
        var location = SettingsButton.TransformToVisual(DragSurface)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        return (new PointInt32(AppWindow.Position.X + (int)Math.Round(location.X * scale),
                               AppWindow.Position.Y + (int)Math.Round(location.Y * scale)),
                (int)Math.Round(SettingsButton.ActualWidth * scale),
                (int)Math.Round(SettingsButton.ActualHeight * scale));
    }

    private void PositionSettingsNearButton()
    {
        if (_settingsWindow is null) return;
        var anchor = GetSettingsAnchor();
        _settingsWindow.PositionNear(anchor.Point, anchor.Width, anchor.Height);
    }

    private void ApplySettings()
    {
        _settings.Save(_dataDirectory);
        _presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        WindowChrome.ApplyRoundedBorderless(this);
        _speedWindow?.SetTopmost(_settings.AlwaysOnTop);
        _settingsWindow?.SetTopmost(_settings.AlwaysOnTop);
        UpdateDisplay();
    }

    private void DragSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(DragSurface).Properties.IsLeftButtonPressed) return;
        int resizeHit = GetResizeHit(e);
        if (resizeHit != 0)
        {
            if (!GetCursorPos(out _resizeStartCursor)) return;
            _resizeStartRect = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y,
                                             AppWindow.Size.Width, AppWindow.Size.Height);
            _resizeHit = resizeHit;
            _resizing = true;
            DragSurface.CapturePointer(e.Pointer);
            _dragTimer.Start();
            e.Handled = true;
            return;
        }
        var speedPoint = e.GetCurrentPoint(SpeedRegion).Position;
        if (speedPoint.X >= 0 && speedPoint.X < SpeedRegion.ActualWidth &&
            speedPoint.Y >= 0 && speedPoint.Y < SpeedRegion.ActualHeight) return;
        var settingsPoint = e.GetCurrentPoint(SettingsButton).Position;
        if (settingsPoint.X >= 0 && settingsPoint.X < SettingsButton.ActualWidth &&
            settingsPoint.Y >= 0 && settingsPoint.Y < SettingsButton.ActualHeight) return;
        if (!GetCursorPos(out _dragStartCursor)) return;
        _dragStartWindow = AppWindow.Position;
        _dragging = true;
        DragSurface.CapturePointer(e.Pointer);
        _dragTimer.Start();
        e.Handled = true;
    }

    private void DragSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizing)
        {
            if ((GetAsyncKeyState(0x01) & 0x8000) == 0 || !GetCursorPos(out var resizeCursor))
                EndResize();
            else ResizeWithPointer(resizeCursor);
            e.Handled = true;
            return;
        }
        if (!_dragging)
        {
            int hit = GetResizeHit(e);
            InputSystemCursorShape shape = hit switch
            {
                10 or 11 => InputSystemCursorShape.SizeWestEast,
                12 or 15 => InputSystemCursorShape.SizeNorthSouth,
                13 or 17 => InputSystemCursorShape.SizeNorthwestSoutheast,
                14 or 16 => InputSystemCursorShape.SizeNortheastSouthwest,
                _ => InputSystemCursorShape.Arrow
            };
            DragSurface.SetPointerCursor(shape);
            return;
        }
        if ((GetAsyncKeyState(0x01) & 0x8000) == 0 || !GetCursorPos(out var cursor))
        {
            EndDrag();
            return;
        }
        MoveWithPointer(cursor);
        e.Handled = true;
    }

    private void DragSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize();
        EndDrag();
    }

    private int GetResizeHit(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DragSurface).Position;
        double width = DragSurface.ActualWidth, height = DragSurface.ActualHeight;
        const double edge = 8, corner = 18;
        bool left = point.X >= 0 && point.X < edge;
        bool right = point.X <= width && point.X > width - edge;
        bool top = point.Y >= 0 && point.Y < edge;
        bool bottom = point.Y <= height && point.Y > height - edge;
        if (point.X < corner && point.Y < corner) return 13; // HTTOPLEFT
        if (point.X > width - corner && point.Y < corner) return 14; // HTTOPRIGHT
        if (point.X < corner && point.Y > height - corner) return 16; // HTBOTTOMLEFT
        if (point.X > width - corner && point.Y > height - corner) return 17; // HTBOTTOMRIGHT
        if (left) return 10; // HTLEFT
        if (right) return 11; // HTRIGHT
        if (top) return 12; // HTTOP
        if (bottom) return 15; // HTBOTTOM
        return 0;
    }

    private void PollPointerAction()
    {
        if (_resizing)
        {
            if ((GetAsyncKeyState(0x01) & 0x8000) == 0 || !GetCursorPos(out var resizeCursor))
                EndResize();
            else ResizeWithPointer(resizeCursor);
            return;
        }
        if (!_dragging) return;
        if ((GetAsyncKeyState(0x01) & 0x8000) == 0 || !GetCursorPos(out var cursor))
        {
            EndDrag();
            return;
        }
        MoveWithPointer(cursor);
    }

    private void ResizeWithPointer(NativePoint cursor)
    {
        int dx = cursor.X - _resizeStartCursor.X, dy = cursor.Y - _resizeStartCursor.Y;
        int minWidth = _presenter.PreferredMinimumWidth ?? 380;
        int minHeight = _presenter.PreferredMinimumHeight ?? 320;
        int left = _resizeStartRect.X, top = _resizeStartRect.Y;
        int right = left + _resizeStartRect.Width, bottom = top + _resizeStartRect.Height;
        if (_resizeHit is 10 or 13 or 16) left = Math.Min(left + dx, right - minWidth);
        if (_resizeHit is 11 or 14 or 17) right = Math.Max(right + dx, left + minWidth);
        if (_resizeHit is 12 or 13 or 14) top = Math.Min(top + dy, bottom - minHeight);
        if (_resizeHit is 15 or 16 or 17) bottom = Math.Max(bottom + dy, top + minHeight);
        var target = new RectInt32(left, top, right - left, bottom - top);
        if (AppWindow.Position.X != target.X || AppWindow.Position.Y != target.Y ||
            AppWindow.Size.Width != target.Width || AppWindow.Size.Height != target.Height)
            AppWindow.MoveAndResize(target);
    }

    private void EndResize()
    {
        if (!_resizing) return;
        if (GetCursorPos(out var cursor)) ResizeWithPointer(cursor);
        _resizing = false;
        _dragTimer.Stop();
        DragSurface.ReleasePointerCaptures();
        _speedWindow?.PositionNear(AppWindow);
        PositionSettingsNearButton();
        SavePosition();
    }

    private void MoveWithPointer(NativePoint cursor)
    {
        int x = _dragStartWindow.X + cursor.X - _dragStartCursor.X;
        int y = _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y;
        if (AppWindow.Position.X != x || AppWindow.Position.Y != y)
            AppWindow.Move(new PointInt32(x, y));
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        MoveWithPointerOnce();
        _dragging = false;
        _dragTimer.Stop();
        DragSurface.ReleasePointerCaptures();
        SavePosition();
    }

    private void MoveWithPointerOnce()
    {
        if (!GetCursorPos(out var cursor)) return;
        MoveWithPointer(cursor);
        _speedWindow?.PositionNear(AppWindow);
        PositionSettingsNearButton();
    }

    private void ToggleVisible()
    {
        if (AppWindow.IsVisible)
        {
            _speedWindow?.Close();
            _settingsWindow?.Close();
            AppWindow.Hide();
        }
        else { AppWindow.Show(); Activate(); }
    }

    private void Exit()
    {
        if (_quitting) return;
        _quitting = true;
        SavePosition();
        Close();
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _quitting = true;
        _displayTimer.Stop(); _scanTimer.Stop(); _quotaTimer.Stop(); _dragTimer.Stop();
        SavePosition();
        _speedWindow?.Close();
        _settingsWindow?.Close();
        _bars.Dispose();
        _tray.Dispose();
        if (_client is not null) await _client.DisposeAsync();
    }

    private void RestorePosition()
    {
        try
        {
            string path = Path.Combine(_dataDirectory, "window-position.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            int x = root.GetProperty("x").GetInt32(), y = root.GetProperty("y").GetInt32();
            var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(Math.Clamp(x, area.X, area.X + area.Width - 100),
                                           Math.Clamp(y, area.Y, area.Y + area.Height - 100)));
            if (root.TryGetProperty("widthDip", out var widthValue) &&
                root.TryGetProperty("heightDip", out var heightValue) &&
                widthValue.TryGetDouble(out double widthDip) &&
                heightValue.TryGetDouble(out double heightDip) &&
                double.IsFinite(widthDip) && double.IsFinite(heightDip))
            {
                double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
                int width = Math.Clamp((int)Math.Round(widthDip * scale),
                                       _presenter.PreferredMinimumWidth ?? 380,
                                       Math.Max(area.Width, _presenter.PreferredMinimumWidth ?? 380));
                int height = Math.Clamp((int)Math.Round(heightDip * scale),
                                        _presenter.PreferredMinimumHeight ?? 320,
                                        Math.Max(area.Height, _presenter.PreferredMinimumHeight ?? 320));
                AppWindow.Resize(new SizeInt32(width, height));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            File.WriteAllText(Path.Combine(_dataDirectory, "window-position.json"),
                              JsonSerializer.Serialize(new {
                                  x = AppWindow.Position.X, y = AppWindow.Position.Y,
                                  widthDip = AppWindow.Size.Width / scale,
                                  heightDip = AppWindow.Size.Height / scale
                              }));
        }
        catch (IOException) { }
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
}
