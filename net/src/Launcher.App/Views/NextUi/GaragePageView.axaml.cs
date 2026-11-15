using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Garage (its data: the classic Garage; its page: the shell's <see cref="GaragePageViewModel"/>,
/// found once the view is in the window). The strip's focus puts a machine on the stage; the page shown and left tells
/// the page, which puts back what SELECT stood on. A step to the machine's page and back takes the focus with it.</summary>
public sealed partial class GaragePageView : UserControl, IPadLanding, Controls.NextUi.IDeckPage
{
    public static readonly DirectProperty<GaragePageView, GaragePageViewModel?> PageProperty =
        AvaloniaProperty.RegisterDirect<GaragePageView, GaragePageViewModel?>(nameof(Page), v => v.Page);

    private GaragePageViewModel? _page;

    /// <summary>On the machine's page, the first of these the focus can take.</summary>
    private static readonly string[] DetailLandings = ["Use", "Setting", "Colours", "Star"];

    public GaragePageView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<ItemsControl>("Tiles") is { } tiles)
        {
            tiles.AddHandler(GotFocusEvent, OnTileFocus);
        }
    }

    /// <summary>The shell's Garage page: what this view shows and works.</summary>
    public GaragePageViewModel? Page
    {
        get => _page;
        private set
        {
            if (_page is not null)
            {
                _page.PropertyChanged -= OnPageChanged;
            }
            SetAndRaise(PageProperty, ref _page, value);
            if (value is not null)
            {
                value.PropertyChanged += OnPageChanged;
            }
        }
    }

    /// <summary>Where the focus lands when the page shows: the machine on the stage in the grid; on the machine's page
    /// "Use this machine" (else the first thing to do there: the machine is raced already); in the builder, its first
    /// row.</summary>
    public Control? Landing()
    {
        if (Page is { Building: true })
        {
            return this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "BuilderRows" && c.IsEffectivelyVisible)
                ?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible);
        }
        if (Page is { Detail: true })
        {
            return DetailLandings.Select(name => this.FindControl<Control>(name))
                .FirstOrDefault(c => c is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true });
        }
        var staged = Page?.Garage.StagedTile();
        return this.FindControl<ItemsControl>("Tiles")?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => staged is not null && ReferenceEquals(b.DataContext, staged));
    }

    /// <summary>A step to the machine's page or back: the focus to where the step lands (unless the press that took
    /// the step sent it on already).</summary>
    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GaragePageViewModel.Detail))
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (IsEffectivelyVisible && (focused is null || !focused.IsEffectivelyVisible || !this.IsVisualAncestorOf(focused))
                && Landing() is { } to)
            {
                to.Focus(NavigationMethod.Directional);
                to.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Page = this.FindAncestorOfType<NextShell>()?.DataContext is MainViewModel main ? main.Shell.GaragePage : null;
        Page?.Shown();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (IsVisible)
        {
            Page?.Left();   // (a page the shell's deck hid was left then)
        }
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The shell's deck shows the page again, or hides it: shown and left, as above.</summary>
    public void Showing(bool shown)
    {
        if (shown)
        {
            Page?.Shown();
        }
        else
        {
            Page?.Left();
        }
    }

    private void OnTileFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Button { DataContext: { } tile })
        {
            Page?.Focus(tile);
        }
    }
}
