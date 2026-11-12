using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The chat sidebar (ChatSidebarViewModel). The view's own part: its left edge dragged, the width following
/// the pointer (held to its least and most), kept when the button is let go.</summary>
public sealed partial class ChatSidebar : UserControl
{
    private readonly Border _grip;
    private double _from;        // where the drag began, in the parent's points (the window's at 100%)
    private double _fromWidth;   // the width then
    private bool _dragging;

    public ChatSidebar()
    {
        AvaloniaXamlLoader.Load(this);
        _grip = this.FindControl<Border>("Grip")!;
        _grip.PointerPressed += OnGripPressed;
        _grip.PointerMoved += OnGripMoved;
        _grip.PointerReleased += OnGripReleased;
        _grip.PointerCaptureLost += (_, _) => EndDrag();
    }

    /// <summary>The parent the drag is measured in: it does not move as the sidebar's width changes (the sidebar
    /// stands at its right end).</summary>
    private Visual? Frame => Parent as Visual ?? this;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ChatSidebarViewModel chat || !e.GetCurrentPoint(_grip).Properties.IsLeftButtonPressed)
        {
            return;
        }
        (_from, _fromWidth, _dragging) = (e.GetPosition(Frame).X, chat.ShownWidth, true);
        _grip.Classes.Set("dragging", true);
        e.Pointer.Capture(_grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging && DataContext is ChatSidebarViewModel chat)
        {
            // the edge is on the left: further left is wider
            chat.Drag(_fromWidth + (_from - e.GetPosition(Frame).X));
        }
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        _grip.Classes.Set("dragging", false);
        (DataContext as ChatSidebarViewModel)?.KeepWidth();
    }
}
