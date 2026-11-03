using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>
/// One mod's card: its name and picture, its switch, and only what the player cannot know and must: a tag when part
/// of it is not used online ("Single player") or never ("Code not used"), each with its reasons when pointed at.
/// The picture is the mod's own preview, else the disc's picture of the first course it touches.
/// </summary>
public sealed partial class ModCard : ObservableObject
{
    private readonly ModsViewModel _page;

    public ModCard(ModsViewModel page, InstalledMod mod, int at, int count)
    {
        ArgumentNullException.ThrowIfNull(mod);
        _page = page;
        Mod = mod;
        (IsFirst, IsLast) = (at == 0, at == count - 1);
        _on = mod.On;
        var files = mod.Manifest.Files;
        var offline = files.Where(f => f.Class != ModFileClass.Code && !ModOverlay.UsedOnline(f)).ToList();
        SinglePlayerWhy = string.Join("\n", offline.Select(Reason).Distinct(StringComparer.Ordinal));
        CodeWhy = files.Any(f => f.Class == ModFileClass.Code) ? ModGuard.CodeWhy : "";
        CanMakePack = files.Any(f => f.Class == ModFileClass.CarData && !f.Online);
        Initial = mod.Manifest.Name.Length > 0 ? mod.Manifest.Name[..1].ToUpperInvariant() : "M";
    }

    /// <summary>A file's reason in a few words, without the tag's own "single player only".</summary>
    private static string Reason(ModFile file) => (file.Why ?? "Single player").Replace(": single player only", "", StringComparison.Ordinal);

    public InstalledMod Mod { get; }

    public string Id => Mod.Manifest.Id;

    public string Name => Mod.Manifest.Name;

    /// <summary>The name's first letter, where there is no picture.</summary>
    public string Initial { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    public partial Bitmap? Picture { get; set; }

    public bool HasPicture => Picture is not null;

    private bool _on;

    /// <summary>The switch: saved at once, used from the next game start.</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (SetProperty(ref _on, value))
            {
                _page.Switch(this, value);
            }
        }
    }

    public bool IsFirst { get; }

    public bool IsLast { get; }

    public bool CanMoveUp => !IsFirst;

    public bool CanMoveDown => !IsLast;

    /// <summary>Why part of the mod is single player only, a line a reason; empty: all of it is used online.</summary>
    public string SinglePlayerWhy { get; }

    public bool SinglePlayer => SinglePlayerWhy.Length > 0;

    public string CodeWhy { get; }

    public bool CodeNotUsed => CodeWhy.Length > 0;

    public bool HasTags => SinglePlayer || CodeNotUsed;

    /// <summary>The mod's car data changes the race: it can be made a stat pack.</summary>
    public bool CanMakePack { get; }

    [ObservableProperty]
    public partial string? PackFile { get; set; }

    [ObservableProperty]
    public partial string? PackWhy { get; set; }

    [ObservableProperty]
    public partial bool Removing { get; set; }

    [RelayCommand]
    private void MoveUp() => _page.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _page.Move(this, 1);

    [RelayCommand]
    private void AskRemove() => Removing = !Removing;

    [RelayCommand]
    private Task Remove() => _page.RemoveAsync(this);

    [RelayCommand]
    private Task MakePack() => _page.MakePackAsync(this);

    [RelayCommand]
    private void ShowPack() => _page.Reveal(PackFile);
}

/// <summary>One folder of a mod the player may take, as the chooser lists it.</summary>
public sealed partial class ModRootChoice(ModRoot root) : ObservableObject
{
    public ModRoot Root { get; } = root;

    public string Label => Root.Label;

    public int Files => Root.Files;

    [ObservableProperty]
    public partial bool On { get; set; } = root.OnByDefault;
}

/// <summary>
/// The Mods page: the player's mods as cards in their order (a later one's files win), each with its switch, up and
/// down, and remove; and the import (a .zip, a .7z or a folder): where the mod has several folders a short list to
/// choose from, then the unpacking with its bar and Cancel. Every read and write runs off the window's thread.
/// </summary>
public sealed partial class ModsViewModel : ViewModelBase
{
    private readonly AppState _state;
    private Action? _stop;   // the import's Cancel
    private ModSurvey? _survey;
    private IReadOnlyDictionary<string, string>? _disc;
    private readonly Dictionary<string, Bitmap?> _pictures = new(StringComparer.Ordinal);

    public ModsViewModel(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        state.ModsChanged += () => OnUi(Refresh);
        Refresh();
    }

    public ObservableCollection<ModCard> Cards { get; } = [];

    public bool HasCards => Cards.Count > 0;

    public bool NoCards => Cards.Count == 0 && !Choosing && !Importing;

    public ObservableCollection<ModRootChoice> Roots { get; } = [];

    /// <summary>The chooser shows: the mod has several folders.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoCards), nameof(Idle))]
    public partial bool Choosing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoCards), nameof(Idle))]
    public partial bool Importing { get; set; }

    public bool Idle => !Choosing && !Importing;

    /// <summary>The mod being chosen or imported, by name.</summary>
    [ObservableProperty]
    public partial string ImportName { get; set; } = "";

    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>What went wrong with the last import, in a few words; empty: nothing.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    /// <summary>The cards as the list is now (an import, a switch, the order, a removal).</summary>
    public void Refresh()
    {
        var mods = _state.Mods ?? [];
        var cards = mods.Select((m, i) => new ModCard(this, m, i, mods.Count)).ToList();
        Cards.Clear();
        foreach (var card in cards)
        {
            Cards.Add(card);
            LoadPicture(card);
        }
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(NoCards));
    }

    /// <summary>A card's picture, decoded on another thread the first time (kept for the run).</summary>
    private void LoadPicture(ModCard card)
    {
        if (_pictures.TryGetValue(card.Id, out var known))
        {
            card.Picture = known;
            return;
        }
        var mod = card.Mod;
        _ = Task.Run(() =>
        {
            Bitmap? picture = null;
            try
            {
                if (mod.Picture is { } file)
                {
                    using var stream = File.OpenRead(file);
                    picture = Bitmap.DecodeToWidth(stream, 480);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                          or NotSupportedException)
            {
                picture = null;
            }
            picture ??= mod.Manifest.Courses.Select(Theme.GameSkin.Venue).FirstOrDefault(p => p is not null);
            OnUi(() =>
            {
                _pictures[mod.Manifest.Id] = picture;
                foreach (var c in Cards.Where(c => c.Id == mod.Manifest.Id))
                {
                    c.Picture = picture;
                }
            });
        });
    }

    internal void Switch(ModCard card, bool on) => _state.SetModOn(card.Id, on);

    internal void Move(ModCard card, int by) => _state.MoveMod(card.Id, by);

    internal Task RemoveAsync(ModCard card) => _state.RemoveModAsync(card.Id);

    internal async Task MakePackAsync(ModCard card)
    {
        var (pack, why) = await _state.StatPackOfModAsync(card.Mod).ConfigureAwait(true);
        (card.PackFile, card.PackWhy) = (pack, why);
    }

    internal void Reveal(string? file)
    {
        if (file is not null && !Replays.TryReveal(file))
        {
            Notice = "File could not be shown";
        }
    }

    /// <summary>A mod picked (a file or a folder): its first look, then the chooser or the import straight away.</summary>
    public async Task PickedAsync(string source)
    {
        if (!Idle)
        {
            return;
        }
        Notice = "";
        ImportName = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        Importing = true;
        Fraction = 0;
        var (survey, refusal, disc) = await _state.SurveyModAsync(source).ConfigureAwait(true);
        Importing = false;
        if (survey is null)
        {
            Notice = refusal ?? "Not a mod";
            return;
        }
        (_survey, _disc) = (survey, disc);
        ImportName = survey.Name;
        if (survey.AsksChoice)
        {
            Roots.Clear();
            foreach (var root in survey.Roots)
            {
                Roots.Add(new ModRootChoice(root));
            }
            Choosing = true;
            return;
        }
        await ImportAsync([survey.Roots[0].Path]).ConfigureAwait(true);
    }

    /// <summary>The chooser's IMPORT: the folders ticked, in the mod's own order.</summary>
    [RelayCommand]
    private async Task ImportChosen()
    {
        var chosen = Roots.Where(r => r.On).Select(r => r.Root.Path).ToList();
        if (chosen.Count == 0)
        {
            return;
        }
        Choosing = false;
        await ImportAsync(chosen).ConfigureAwait(true);
    }

    private async Task ImportAsync(IReadOnlyList<string> chosen)
    {
        if (_survey is not { } survey || _disc is not { } disc)
        {
            return;
        }
        using var cancel = new CancellationTokenSource();
        _stop = cancel.Cancel;
        Importing = true;
        Fraction = 0;
        var progress = new Progress<ModProgress>(p => Fraction = p.Total == 0 ? 0 : (double)p.Done / p.Total);
        try
        {
            var refusal = await _state.ImportModAsync(survey, chosen, disc, progress, cancel.Token).ConfigureAwait(true);
            Notice = refusal ?? "";
        }
        catch (OperationCanceledException)
        {
            Notice = "";
        }
        finally
        {
            Importing = false;
            _stop = null;
            _survey = null;
        }
    }

    /// <summary>Cancel: the chooser closed, or the import stopped (nothing of it is kept).</summary>
    [RelayCommand]
    private void CancelImport()
    {
        if (Choosing)
        {
            Choosing = false;
            _survey = null;
            return;
        }
        _stop?.Invoke();
    }
}
