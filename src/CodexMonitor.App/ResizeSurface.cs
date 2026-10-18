using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace CodexMonitor_App;

public sealed class ResizeSurface : Grid
{
    private static readonly Dictionary<InputSystemCursorShape, InputCursor> Cursors = new();

    public void SetPointerCursor(InputSystemCursorShape shape)
    {
        if (!Cursors.TryGetValue(shape, out var cursor))
            Cursors[shape] = cursor = InputSystemCursor.Create(shape);
        ProtectedCursor = cursor;
    }
}
