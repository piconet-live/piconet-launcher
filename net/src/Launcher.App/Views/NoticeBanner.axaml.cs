using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>The banner of a server notice. A notice's band (the tier that goes by itself) shows its time running
/// out: a thin bar along its foot that empties, evenly, over <see cref="NoticesViewModel.BannerTime"/>, and starts
/// again full for the next notice.</summary>
public sealed partial class NoticeBanner : UserControl
{
    private readonly Border? _timer;
    private NoticesViewModel? _notices;

    public NoticeBanner()
    {
        AvaloniaXamlLoader.Load(this);
        _timer = this.FindControl<Border>("Timer");
        DataContextChanged += (_, _) => Watch();
    }

    private void Watch()
    {
        if (_notices is not null)
        {
            _notices.PropertyChanged -= OnChanged;
        }
        _notices = DataContext as NoticesViewModel;
        if (_notices is not null)
        {
            _notices.PropertyChanged += OnChanged;
        }
        Run();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoticesViewModel.Banner) or nameof(NoticesViewModel.BannerShown))
        {
            Run();
        }
    }

    private void Run()
    {
        if (_timer is not { } timer)
        {
            return;
        }
        var timed = _notices is { BannerShown: true, Banner.Level: "notice" };
        timer.IsVisible = timed;
        // full at once, then empty over the banner's time, evenly
        timer.Transitions = null;
        timer.RenderTransform = TransformOperations.Parse("scaleX(1)");
        if (!timed)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            timer.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = NoticesViewModel.BannerTime }];
            timer.RenderTransform = TransformOperations.Parse("scaleX(0)");
        }, DispatcherPriority.Render);
    }
}
