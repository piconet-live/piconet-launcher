using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>A course picker's mode: one course (Practice), or a pool of them (a custom lobby's vote).</summary>
public enum PickMode
{
    One,
    Pool,
}

/// <summary>A course's card: its venue picture from the disc, the venue, the track; picked or not.</summary>
public sealed partial class CourseCard(int course, string cup) : ObservableObject
{
    public int Course { get; } = course;

    public string Cup { get; } = cup;

    public string Venue { get; } = ReplayWords.Course(Courses.Name(course)).Venue;

    public string Track { get; } = ReplayWords.Course(Courses.Name(course)).Track;

    public string Full { get; } = Courses.Name(course);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checked))]
    public partial bool Picked { get; set; }

    /// <summary>In a pool, a picked card shows its check.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checked))]
    public partial bool InPool { get; set; }

    public bool Checked => InPool && Picked;
}

/// <summary>A cup's chip over the cards: "All" or a cup's name.</summary>
public sealed partial class CupChip(string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>A cup has its gem beside its name; "All" has none.</summary>
    public bool HasGem => Label != CoursePickerModel.AllCups;

    [ObservableProperty]
    public partial bool On { get; set; }
}

/// <summary>
/// The course picker (Controls/CoursePicker): every course as a card, the cups as chips that show one cup's cards or
/// all of them. In <see cref="PickMode.One"/> a press picks that course and no other; in <see cref="PickMode.Pool"/>
/// a press puts the course in the pool or takes it out, with All and None and the pool's count.
/// </summary>
public sealed partial class CoursePickerModel : ObservableObject
{
    public const string AllCups = "All";

    public CoursePickerModel(PickMode mode, IEnumerable<int> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        Mode = mode;
        var set = picked.ToHashSet();
        Cards = [.. CourseCups.All.Select(c => new CourseCard(c.Course, c.Cup) { InPool = mode == PickMode.Pool })];
        var first = true;
        foreach (var card in Cards)
        {
            card.Picked = set.Contains(card.Course) && (mode == PickMode.Pool || first);
            first &= !card.Picked;
        }
        if (mode == PickMode.One && !Cards.Any(c => c.Picked))
        {
            Cards[0].Picked = true;
        }
        Chips = [.. new[] { AllCups }.Concat(CourseCups.Cups).Select(label => new CupChip(label) { On = label == AllCups })];
        Shown = [.. Cards];
    }

    public PickMode Mode { get; }

    public bool IsPool => Mode == PickMode.Pool;

    public IReadOnlyList<CourseCard> Cards { get; }

    public IReadOnlyList<CupChip> Chips { get; }

    /// <summary>The cards of the cup chosen.</summary>
    public ObservableCollection<CourseCard> Shown { get; }

    /// <summary>The cup whose cards show, or <see cref="AllCups"/>.</summary>
    [ObservableProperty]
    public partial string Cup { get; set; } = AllCups;

    /// <summary>The course picked (the first in the pool's order in a pool).</summary>
    public int Picked => Cards.FirstOrDefault(c => c.Picked)?.Course ?? 0;

    /// <summary>The course picked (one course: the hero over the picker shows it); null when none is.</summary>
    public CourseCard? PickedCard => Cards.FirstOrDefault(c => c.Picked);

    /// <summary>The pool, in the game's order.</summary>
    public IReadOnlyList<int> Pool => [.. Cards.Where(c => c.Picked).Select(c => c.Course)];

    /// <summary>"26 / 30".</summary>
    public string PoolCount => $"{Cards.Count(c => c.Picked)} / {Cards.Count}";

    /// <summary>A course was picked, or the pool changed.</summary>
    public event Action? Changed;

    partial void OnCupChanged(string value)
    {
        foreach (var chip in Chips)
        {
            chip.On = chip.Label == value;
        }
        Shown.Clear();
        foreach (var card in Cards.Where(c => value == AllCups || c.Cup == value))
        {
            Shown.Add(card);
        }
    }

    [RelayCommand]
    private void ShowCup(CupChip chip) => Cup = chip.Label;

    [RelayCommand]
    public void Pick(CourseCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (Mode == PickMode.Pool)
        {
            card.Picked = !card.Picked;
        }
        else
        {
            foreach (var other in Cards)
            {
                other.Picked = other == card;
            }
        }
        Said();
    }

    [RelayCommand]
    private void PickAll() => SetPool(true);

    [RelayCommand]
    private void PickNone() => SetPool(false);

    private void SetPool(bool on)
    {
        if (Mode != PickMode.Pool)
        {
            return;
        }
        foreach (var card in Cards)
        {
            card.Picked = on;
        }
        Said();
    }

    private void Said()
    {
        OnPropertyChanged(nameof(Picked));
        OnPropertyChanged(nameof(PickedCard));
        OnPropertyChanged(nameof(Pool));
        OnPropertyChanged(nameof(PoolCount));
        Changed?.Invoke();
    }
}
