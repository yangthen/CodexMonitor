using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace CodexMonitor_App;

internal static class WindowChrome
{
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcPaint = 0x0085;
    private const uint WmNcDestroy = 0x0082;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly WindowSubclassProcedure SubclassProcedure = WindowProcedure;
    private static readonly HashSet<nint> Installed = [];

    public static void ApplyRoundedBorderless(Window window)
    {
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int)); // DWMWA_BORDER_COLOR
        int rounded = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        // Keep DWM's antialiased corners, while extending WinUI's client area
        // over the native band that otherwise flashes white during resizing.
        if (Installed.Add(handle))
        {
            if (SetWindowSubclass(handle, SubclassProcedure, 1, 0))
                SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpFrameChanged);
            else
                Installed.Remove(handle);
        }
    }

    private static nint WindowProcedure(nint handle, uint message, nuint wParam, nint lParam,
                                         nuint subclassId, nuint referenceData)
    {
        if (message == WmNcCalcSize || message == WmNcPaint) return 0;
        nint result = DefSubclassProc(handle, message, wParam, lParam);
        if (message == WmNcDestroy)
        {
            RemoveWindowSubclass(handle, SubclassProcedure, subclassId);
            Installed.Remove(handle);
        }
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowSubclassProcedure(nint handle, uint message, nuint wParam, nint lParam,
                                                    nuint subclassId, nuint referenceData);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, WindowSubclassProcedure callback, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint hwnd, WindowSubclassProcedure callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
