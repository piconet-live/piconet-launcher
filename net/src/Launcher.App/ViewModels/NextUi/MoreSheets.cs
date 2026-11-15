using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>A panel that belongs to one page (a replay's to Replays, a mod's to Mods): the shell closes it when another
/// page shows.</summary>
public interface IOwnedSheet
{
    /// <summary>The page the panel belongs to.</summary>
    ViewModelBase Owner { get; }
}

/// <summary>When a replay was recorded with its day where its row's group says it ("Today, 7:00 PM").</summary>
public static class ReplayDay
{
    public static string Of(ReplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (order, title) = ReplayDates.Group(item.Listing.Recorded, DateTimeOffset.Now);
        return order <= 1 ? $"{title}, {item.When}" : item.When;
    }
}

/// <summary>A fact of a panel: a name and its value ("Course", "Mute City · Big Blue").</summary>
public sealed record SheetFact(string Name, string Value);

/// <summary>
/// A replay's panel (a row of Replays pressed): the course, the result, the racer and the machine, the mode, when; Play
/// (in the game), Results (every racer's, read from the file the first time), the mode file (its name, code,
/// settings and text, when the race had a mode the game plays), Show in folder, Delete (after a question). What it
/// does is the classic page's (<see cref="ReplaysViewModel"/>).
/// </summary>
public sealed partial class ReplaySheetViewModel : ViewModelBase, IOwnedSheet, IDisposable
{
    private readonly ReplaysViewModel _replays;
    private readonly ShellViewModel _shell;

    public ReplaySheetViewModel(ReplaysViewModel replays, ReplayItem item, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(replays);
        ArgumentNullException.ThrowIfNull(item);
        (_replays, _shell, Item) = (replays, shell, item);
        Facts =
        [
            new("Course", item.Venue.Length > 0 ? $"{item.Venue} · {item.Track}" : item.Track),
            new(item.IsPractice ? "Time" : "Result", item.IsPractice ? item.Time : string.Join(" ", new[] { item.Place, item.Of }.Where(w => w.Length > 0))),
            new("Racer", item.RacerMachine.Length > 0 ? $"{item.RacerName} · {item.RacerMachine}" : item.RacerName),
            new("Race", item.Under),
            new("When", ReplayDay.Of(item)),
        ];
        When = ReplayDay.Of(item);
        replays.PropertyChanged += OnReplays;
    }

    public ViewModelBase Owner => _replays;

    public ReplayItem Item { get; }

    /// <summary>When it was recorded, with its day.</summary>
    public string When { get; }

    public IReadOnlyList<SheetFact> Facts { get; }

    /// <summary>Every racer's result shows (a race with results).</summary>
    [ObservableProperty]
    public partial bool ShowingResults { get; private set; }

    /// <summary>The mode file shows.</summary>
    [ObservableProperty]
    public partial bool ShowingMode { get; private set; }

    /// <summary>The page's line (a play that did not start, a file not shown).</summary>
    public string Notice => _replays.Notice;

    public IReadOnlyList<ReplayResultLine> Results => [.. Item.StatsLeft, .. Item.StatsRight];

    [RelayCommand]
    private void Play()
    {
        if (_replays.PlayCommand.CanExecute(Item.Path))
        {
            _replays.PlayCommand.Execute(Item.Path);
        }
    }

    [RelayCommand]
    private void ToggleResults()
    {
        ShowingResults = Item.HasStats && !ShowingResults;
        ShowingMode = false;
        OnPropertyChanged(nameof(Results));
    }

    [RelayCommand]
    private void ToggleMode()
    {
        ShowingMode = Item.ModeFile is not null && !ShowingMode;
        ShowingResults = false;
    }

    [RelayCommand]
    private void Reveal() => _replays.RevealCommand.Execute(Item);

    /// <summary>"Delete": asked first.</summary>
    [RelayCommand]
    private void AskDelete() => _shell.Ask("Delete this replay?", "It is removed from your replay folder for good.", "Delete", () =>
    {
        _replays.DeleteCommand.Execute(Item);
        if (ReferenceEquals(_shell.Sheet, this))
        {
            _shell.Sheet = null;
        }
    });

    public void Dispose() => _replays.PropertyChanged -= OnReplays;

    private void OnReplays(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReplaysViewModel.Notice))
        {
            OnPropertyChanged(nameof(Notice));
        }
    }
}

/// <summary>
/// A mod's panel (a row of Mods pressed): its place in the order, what of it is not used online and why, its switch,
/// Move up and Move down, its stat pack (made from its machine stats, then shown), its parts as a tree to tick (and its
/// own text files to read), Remove (after a question). What it does is the classic page's card's
/// (<see cref="ModCard"/>).
/// </summary>
public sealed partial class ModSheetViewModel : ViewModelBase, IOwnedSheet, IDisposable
{
    private readonly ModsViewModel _mods;
    private readonly ShellViewModel _shell;

    public ModSheetViewModel(ModsViewModel mods, ModCard card, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(card);
        (_mods, _shell, Card) = (mods, shell, card);
        card.PropertyChanged += OnCard;
        mods.Cards.CollectionChanged += OnCards;
        if (card.HasTree && !card.TreeOpen)
        {
            card.ToggleTreeCommand.Execute(null);   // (the tree made, and open in the panel)
        }
    }

    public ViewModelBase Owner => _mods;

    public ModCard Card { get; }

    /// <summary>"MOD · 2 OF 5".</summary>
    public string Place => $"Mod · {Math.Max(1, _mods.Cards.IndexOf(Card) + 1)} of {_mods.Cards.Count}";

    /// <summary>The switch's word.</summary>
    public string OnText => Card.On ? "On" : "Off";

    /// <summary>The switch pressed.</summary>
    [RelayCommand]
    private void Toggle() => Card.On = !Card.On;

    /// <summary>"Remove": asked first; the panel closes with it.</summary>
    [RelayCommand]
    private void AskRemove() => _shell.Ask("Remove this mod for good?", "Its files are deleted. You can import it again later.", "Remove", () =>
    {
        Card.RemoveCommand.Execute(null);
        if (ReferenceEquals(_shell.Sheet, this))
        {
            _shell.Sheet = null;
        }
    });

    public void Dispose()
    {
        Card.PropertyChanged -= OnCard;
        _mods.Cards.CollectionChanged -= OnCards;
    }

    private void OnCard(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModCard.On))
        {
            OnPropertyChanged(nameof(OnText));
        }
        OnPropertyChanged(nameof(Place));
    }

    private void OnCards(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(Place));
}

/// <summary>A licence in the Licences panel: what it is for, under which licence, and its text.</summary>
public sealed record LicenceEntry(string Title, string Line, string Text);

/// <summary>
/// More › About › Licences: the launcher's own licence, and the parts of others it holds with theirs (the controller
/// pictures, the fonts), their texts as they ship inside the launcher.
/// </summary>
public sealed class LicencesSheetViewModel(AboutViewModel about) : ViewModelBase, IOwnedSheet
{
    public ViewModelBase Owner { get; } = about;

    public IReadOnlyList<LicenceEntry> Entries { get; } =
    [
        new("Piconet launcher", "GNU General Public License, version 3",
            "The launcher is free software: you can share it and change it under the terms of the GNU General Public License, "
            + "version 3, as published by the Free Software Foundation. Its full text comes with the launcher's source."),
        new("Controller pictures", "m-overlay · MIT License · Copyright (c) 2020 Bkacjios", Asset("avares://Launcher.App/Assets/PadOverlay/LICENSE.txt")),
        new("Fonts", "Saira · SIL Open Font License 1.1", Asset("avares://Launcher.App/Assets/Fonts/OFL.txt")),
    ];

    /// <summary>A text shipped inside the launcher; "" where it cannot be read.</summary>
    private static string Asset(string uri)
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(uri));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or InvalidOperationException or UriFormatException)
        {
            return "";
        }
    }
}

/// <summary>
/// The profile's "Change name": the name typed, as the game will show it ("In races it reads …"), the room left, the
/// characters the game cannot draw; Save keeps it (as the classic name menu does: <see cref="AccountViewModel"/>).
/// </summary>
public sealed partial class NameSheetViewModel : ViewModelBase, IOwnedSheet, IDisposable
{
    private readonly ShellViewModel _shell;

    public NameSheetViewModel(AccountViewModel account, ShellViewModel shell, ViewModelBase owner)
    {
        ArgumentNullException.ThrowIfNull(account);
        (Account, _shell, Owner) = (account, shell, owner);
        account.EditName();
        account.PropertyChanged += OnAccount;
    }

    public AccountViewModel Account { get; }

    public ViewModelBase Owner { get; }

    /// <summary>"In races it reads ANA".</summary>
    public string Reads => $"In races it reads {Account.DraftShown}";

    /// <summary>Under the box: the room left, or which characters are not shown.</summary>
    public string Under => Account.HasLost ? $"Not shown: {Account.DraftLost}" : Account.DraftLeft;

    [RelayCommand]
    private void Save()
    {
        if (Account.SaveName())
        {
            _shell.Sheet = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _shell.Sheet = null;

    public void Dispose() => Account.PropertyChanged -= OnAccount;

    private void OnAccount(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.Draft))
        {
            OnPropertyChanged(nameof(Reads));
            OnPropertyChanged(nameof(Under));
        }
    }
}
