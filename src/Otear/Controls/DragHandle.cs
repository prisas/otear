using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Otear.Controls;

/// <summary>A hit-testable area that shows the "move" pointer (ProtectedCursor is only settable by a subclass).</summary>
public sealed partial class DragHandle : Grid
{
    public DragHandle()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    }
}
