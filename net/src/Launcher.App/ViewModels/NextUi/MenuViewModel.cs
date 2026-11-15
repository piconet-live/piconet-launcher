namespace Launcher.App.ViewModels.NextUi;

/// <summary>A row of a <see cref="MenuViewModel"/>: its name, one line of what is behind it, and the page it
/// opens when pressed.</summary>
public sealed record MenuRow(string Label, string Line, Action Open);

/// <summary>A page of the redesigned interface that is a short list of places to go into (More: Mods, Stats; until
/// Settings has its one page, its four pages): each a row with its name and one line.</summary>
public sealed class MenuViewModel : ViewModelBase
{
    public MenuViewModel(string title, IReadOnlyList<MenuRow> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<MenuRow> Rows { get; }
}
