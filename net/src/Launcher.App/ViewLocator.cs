using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.App;

/// <summary>Each screen's view for its view model: the one place that pairs them, so a new design swaps views here.</summary>
public sealed class ViewLocator : IDataTemplate
{
    /// <summary>Each page's view, made once and kept for as long as its view model: a page opened again is the same
    /// controls (no page's making on the window's thread at each visit). A view that still stands somewhere else (one
    /// view model shown in two places) gets a second, not kept.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Control> Made = [];

    public Control? Build(object? param)
    {
        if (param is null)
        {
            return Make(param);
        }
        if (Made.TryGetValue(param, out var view))
        {
            return view.Parent is null ? view : Make(param);
        }
        view = Make(param);
        if (view is not null)
        {
            Made.AddOrUpdate(param, view);
        }
        return view;
    }

    /// <summary>The views of these pages made now, before the window shows: the first open of a page does not make it
    /// on the window's thread in front of the player.</summary>
    public void Prebuild(IEnumerable<object> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        foreach (var page in pages)
        {
            Build(page);
        }
    }

    private static Control? Make(object? param) => param switch
    {
        HomeViewModel => new HomeView(),
        GarageViewModel => new GarageView(),
        LobbyViewModel => new LobbyView(),
        LanViewModel => new LanView(),
        PracticeViewModel => new PracticeView(),
        CustomLobbiesViewModel => new CustomLobbiesView(),
        SettingsViewModel => new SettingsView(),
        ControllerViewModel => new ControllerView(),
        ReplaysViewModel => new ReplaysView(),
        StatsViewModel => new StatsView(),
        DiscViewModel => new DiscView(),
        _ => new TextBlock { Text = param?.GetType().Name ?? "" },
    };

    public bool Match(object? data) => data is ViewModelBase;
}
