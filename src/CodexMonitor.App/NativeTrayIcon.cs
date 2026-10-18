using System.Runtime.InteropServices;

namespace CodexMonitor_App;

/// <summary>Win32 notification-area icon; keeps the WinUI project free of WinForms XAML build targets.</summary>
public sealed class NativeTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001; // WM_APP + 1
    private const uint IconId = 1;
    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly bool _ownsIcon;
    private readonly SubclassProc _subclass;
    private readonly Action _toggle;
    private readonly Action _refresh;
    private readonly Action _exit;
    private readonly uint _taskbarCreated;
    private string _tooltip = "Codex Monitor";
    private bool _disposed;

    public NativeTrayIcon(nint hwnd, string iconPath, Action toggle, Action refresh, Action exit)
    {
        _hwnd = hwnd;
        _toggle = toggle;
        _refresh = refresh;
        _exit = exit;
        _icon = File.Exists(iconPath) ? LoadImage(nint.Zero, iconPath, 1, 0, 0, 0x10 | 0x40) : nint.Zero;
        _ownsIcon = _icon != nint.Zero;
        if (_icon == nint.Zero) _icon = LoadIcon(nint.Zero, (nint)32512);
        _subclass = WindowProc;
        if (!SetWindowSubclass(hwnd, _subclass, 1, 0)) throw new InvalidOperationException("Cannot attach tray callback.");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Add();
    }

    public void SetTooltip(string text)
    {
        if (_disposed || _tooltip == text) return;
        _tooltip = text.Length > 120 ? text[..120] : text;
        var data = CreateData();
        Shell_NotifyIcon(1, ref data); // NIM_MODIFY
    }

    private void Add()
    {
        var add = CreateData();
        Shell_NotifyIcon(0, ref add); // NIM_ADD
        var data = CreateData();
        data.Version = 4;
        Shell_NotifyIcon(4, ref data); // NIM_SETVERSION
    }

    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Hwnd = _hwnd,
        Id = IconId,
        Flags = 0x1 | 0x2 | 0x4, // NIF_MESSAGE | NIF_ICON | NIF_TIP
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = _tooltip,
        Info = "",
        InfoTitle = ""
    };

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated) Add();
        if (message == CallbackMessage)
        {
            uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage is 0x0203 or 0x0400) _toggle(); // double click / NIN_SELECT
            else if (mouseMessage is 0x0205 or 0x007B) ShowMenu(); // right click / context menu
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, 1, "Show / Hide");
            AppendMenu(menu, 0, 2, "Refresh now");
            AppendMenu(menu, 0x800, 0, null); // MF_SEPARATOR
            AppendMenu(menu, 0, 3, "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            uint command = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, _hwnd, nint.Zero);
            PostMessage(_hwnd, 0, 0, 0);
            if (command == 1) _toggle();
            else if (command == 2) _refresh();
            else if (command == 3) _exit();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = CreateData();
        Shell_NotifyIcon(2, ref data); // NIM_DELETE
        RemoveWindowSubclass(_hwnd, _subclass, 1);
        if (_ownsIcon) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Hwnd;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    private delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, uint id, string? label);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
