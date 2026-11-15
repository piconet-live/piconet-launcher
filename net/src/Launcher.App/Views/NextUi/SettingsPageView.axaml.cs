using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;

namespace Launcher.App.Views.NextUi;

/// <summary>The redesigned Settings (its data: the classic page's view model; its rows: the shell's
/// <see cref="SettingsPageViewModel"/>, found once the view is in the window). The view's own part: the system's
/// pickers (the disc image, the replay folder, an older copy's folder), and the launcher's tone at the volume set.</summary>
public sealed partial class SettingsPageView : UserControl, IPadLanding
{
    public static readonly DirectProperty<SettingsPageView, SettingsPageViewModel?> PageProperty =
        AvaloniaProperty.RegisterDirect<SettingsPageView, SettingsPageViewModel?>(nameof(Page), v => v.Page);

    private SettingsPageViewModel? _page;
    private SettingsViewModel? _settings;

    public SettingsPageView() => AvaloniaXamlLoader.Load(this);

    /// <summary>The shell's Settings page: the rows this view shows.</summary>
    public SettingsPageViewModel? Page
    {
        get => _page;
        private set => SetAndRaise(PageProperty, ref _page, value);
    }

    /// <summary>The pad lands on the first row of the group open; among the groups, on the one open last (else the
    /// first).</summary>
    public Control? Landing()
    {
        if (Page is { IsOpen: true })
        {
            return this.FindControl<OptionList>("Rows")?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible);
        }
        var groups = this.FindControl<ItemsControl>("Categories")?.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList() ?? [];
        return groups.FirstOrDefault(b => Page?.LastOpen is { } last && ReferenceEquals(b.DataContext, last)) ?? groups.FirstOrDefault();
    }

    /// <summary>A group opened or closed: the focus moves with it, onto the group's first row, or back onto the group
    /// it came from (the pad's, the keyboard's and the mouse's alike: the step changed under them).</summary>
    /// <summary>The focus (the pointer's or the pad's) on a group: the window on the right says what is in it.</summary>
    private void OnCategoryFocus(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.NextUi.SettingsCategory category } && Page is { } page)
        {
            page.On(category);
        }
    }

    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.Open) && IsEffectivelyVisible)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Landing()?.Focus(Avalonia.Input.NavigationMethod.Directional), Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Page = this.FindAncestorOfType<NextShell>()?.DataContext is MainViewModel main ? main.Shell.SettingsPage : null;
        if (Page is { } page)
        {
            page.Picking += OnPicking;
            page.PropertyChanged += OnPageChanged;
        }
        _settings = DataContext as SettingsViewModel;
        if (_settings is not null)
        {
            _settings.SoundSet += Hear;
            _settings.KindSet += HearKind;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Page is { } page)
        {
            page.Picking -= OnPicking;
            page.PropertyChanged -= OnPageChanged;
        }
        if (_settings is not null)
        {
            _settings.SoundSet -= Hear;
            _settings.KindSet -= HearKind;
            _settings = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The launcher's volume was set: one of its tones at that volume, as on the classic page.</summary>
    private void Hear()
    {
        if (_settings is { } settings)
        {
            Attention.Sound(Launcher.Core.LobbyMoment.CourseVote, settings.Sounds);
        }
    }

    /// <summary>A kind's volume set: a sound of that kind at it (the menu's confirm, the course vote's call, the
    /// draw's chime).</summary>
    private void HearKind(Launcher.Core.SoundKind kind)
    {
        if (_settings is { } settings)
        {
            Attention.Sound(kind switch
            {
                Launcher.Core.SoundKind.Menu => Launcher.Core.LauncherSound.Confirm,
                Launcher.Core.SoundKind.Notice => Launcher.Core.LauncherSound.CourseVote,
                _ => Launcher.Core.LauncherSound.CourseDrawn,
            }, settings.Sounds);
        }
    }

    private async void OnPicking(SettingsPick pick)
    {
        var path = pick switch
        {
            SettingsPick.Disc => await DiscPicker.PickAsync(this).ConfigureAwait(true),
            SettingsPick.ReplayFolder => await FolderPicker.PickAsync(this, "The folder for your replays").ConfigureAwait(true),
            _ => await FolderPicker.PickAsync(this, FolderPicker.OlderCopyTitle).ConfigureAwait(true),
        };
        if (path is not null && Page is { } page)
        {
            await page.PickedAsync(pick, path).ConfigureAwait(true);
        }
    }
}
