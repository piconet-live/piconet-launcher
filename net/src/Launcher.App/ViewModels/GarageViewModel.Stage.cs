using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;

namespace Launcher.App.ViewModels;

// The redesigned Garage's stage (Views/NextUi/GaragePageView): one machine on the stage beside the strip of the
// shelf's machines, the one the strip's focus is on. A stock machine on it is PREVIEW on that machine; a custom one is
// the builder on it, the shelf staying what it is (a marked custom machine on Favourites stays on Favourites). Nothing
// is raced until the machine on the stage is used (UseStaged); leaving the page puts back what SELECT stood on, as
// Back does in the classic page. The classic page never calls any of this.
public sealed partial class GarageViewModel
{
    /// <summary>The stage holds a custom machine (the builder's open one, <see cref="StagedBuild"/>); else the stock
    /// machine <see cref="MachineIndex"/>.</summary>
    [ObservableProperty]
    public partial bool StagedCustom { get; private set; }

    /// <summary>The custom machine on the stage; null: the stock one.</summary>
    public CustomBuild? StagedBuild => StagedCustom ? Builder.Build : null;

    /// <summary>The name of the machine on the stage.</summary>
    public string StagedName => !StagedCustom ? MachineName
        : Builder.Name.Length > 0 ? Builder.Name : RacedMachine.UnnamedCustom;

    /// <summary>What the game's machine select shows of the machine on the stage: its letters, its weight.</summary>
    public IReadOnlyList<StatFigure> StagedSummary => StagedCustom ? Builder.Summary : Summary;

    /// <summary>The machine on the stage is the one raced.</summary>
    public bool StagedInUse => StagedCustom
        ? !CustomsOff && !Builder.Unsaved && Builder.Editing is { } i && i < Builder.Saved.Count && Builder.Saved[i].Raced
        : Machines.Has(MachineIndex) && _cards[MachineIndex].Selected;

    /// <summary>The machine on the stage can be used: one the lobby offers, a custom one where the lobby's mode takes
    /// them.</summary>
    public bool StagedUsable => StagedCustom ? !CustomsOff : Offers(MachineIndex);

    /// <summary>The machine on the stage is marked with the star.</summary>
    public bool StagedFavourite => StagedCustom ? Builder.Favourite : SelectedFavourite;

    /// <summary>The star can be pressed: a stock machine, or a custom one that is saved.</summary>
    public bool StagedMarkable => !StagedCustom || Builder.CanFavourite;

    /// <summary>A saved custom machine is on the stage: its parts can be changed, and it can be removed.</summary>
    public bool StagedSaved => StagedCustom && Builder.CanFavourite;

    /// <summary>The stats are seen through a choice ("Stats: Stock", a pack's name); in a lobby its fixed pack.</summary>
    public string LensText => "Stats: " + (LensFixed ? LensFixedText : Lenses[Math.Clamp(LensIndex, 0, Lenses.Count - 1)]);

    /// <summary>There is another choice to see the stats through.</summary>
    public bool LensSteps => LensFree && Lenses.Count > 1;

    /// <summary>The stats button's tip: whose values show, and what a press does (never empty: the disc's own stats
    /// are said too).</summary>
    public string LensTip => LensFixed
        ? LensPack is null ? "The lobby races on the disc's own stats: every value shows them." : $"The lobby races on a stat pack: {PackText}. Every value shows it."
        : (LensPack is null ? "The values are the disc's own stats." : $"The values are a stat pack's: {PackText}.")
            + (LensSteps ? " Press to see them through the next stat pack." : "");

    /// <summary>Some machine is left out: a letter is chosen, or a word typed.</summary>
    public bool Filtered => !Filter.Open;

    /// <summary>The page's notices for the stage: the classic page's own changes and the builder's.</summary>
    private void HearStage()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MachineIndex) or nameof(StagedCustom) or nameof(Summary) or nameof(SelectedFavourite)
                or nameof(CustomsOff) or nameof(OfferLimited) or nameof(LensIndex) or nameof(LensFixed) or nameof(LensFixedText)
                or nameof(FilterCountText) or nameof(Query))
            {
                Restaged();
            }
        };
        Builder.PropertyChanged += (_, e) =>
        {
            if (StagedCustom && e.PropertyName is null or "" or nameof(CustomBuilderViewModel.Build) or nameof(CustomBuilderViewModel.Editing)
                or nameof(CustomBuilderViewModel.Unsaved) or nameof(CustomBuilderViewModel.Summary) or nameof(CustomBuilderViewModel.Favourite))
            {
                Restaged();
            }
        };
        Builder.SavedChanged += Restaged;
    }

    private void Restaged()
    {
        foreach (var name in (string[])[nameof(StagedBuild), nameof(StagedName), nameof(StagedSummary), nameof(StagedInUse), nameof(StagedUsable),
            nameof(StagedFavourite), nameof(StagedMarkable), nameof(StagedSaved), nameof(LensText), nameof(LensSteps), nameof(LensTip), nameof(Filtered)])
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>A tile of the strip on the stage (the focus on it): a stock machine (one the lobby does not offer is
    /// not), a custom machine. The machine raced is on the stage as SELECT stands on it; another one is a preview.</summary>
    public void Stage(object? tile)
    {
        switch (tile)
        {
            case GarageCard card when Offers(card.Id):
                if (card.Selected)
                {
                    StagedCustom = false;
                    Settle();   // (the machine raced: SELECT on it)
                    return;
                }
                Hold();
                StagedCustom = false;
                MachineIndex = card.Id;
                Phase = PreviewPhase;
                break;
            case CustomCard card:
                if (card.Raced && !CustomsOff)
                {
                    Settle();
                    StagedCustom = true;
                    Builder.OpenSavedCommand.Execute(card);
                    return;
                }
                Hold();
                StagedCustom = true;
                Builder.OpenSavedCommand.Execute(card);
                Phase = PreviewPhase;
                break;
            default:
                return;
        }
        ShowStagedTrail();
    }

    /// <summary>A new machine on the stage, for the builder: nothing is kept until it is saved.</summary>
    public void StageFresh()
    {
        Hold();
        StagedCustom = true;
        Builder.NewMachineCommand.Execute(null);
        Phase = PreviewPhase;
        ShowStagedTrail();
    }

    /// <summary>The machine on the stage made the one raced (a custom one saved first when it was changed or is new),
    /// SELECT standing on it; told to a lobby, with the confirm's sound.</summary>
    public void UseStaged()
    {
        if (!StagedUsable)
        {
            return;
        }
        _confirming = true;   // (the builder's Save on the way is this one confirm)
        try
        {
            if (StagedCustom)
            {
                Builder.UseCommand.Execute(null);
            }
            else
            {
                PickMachine(MachineIndex);
            }
        }
        finally
        {
            _confirming = false;
        }
        if (Previewing)
        {
            _held = MachineIndex;
            Phase = SelectPhase;
        }
        ShowSelected();
        Restaged();
        Confirm();
    }

    /// <summary>The page left (or a shelf without the machine): what SELECT stood on again, and the machine raced on
    /// the stage.</summary>
    public void Unstage()
    {
        Settle();
        StagedCustom = _state.Settings.Racer.Raced.IsCustom && !CustomsOff && Builder.Saved.Any(c => c.Raced);
        if (StagedCustom && Builder.Saved.FirstOrDefault(c => c.Raced) is { } raced)
        {
            Builder.OpenSavedCommand.Execute(raced);
        }
    }

    /// <summary>SELECT again, on the machine it stood on.</summary>
    private void Settle()
    {
        if (!Previewing)
        {
            return;
        }
        var held = _held;
        Phase = SelectPhase;
        if (Machines.Has(held))
        {
            MachineIndex = held;
        }
        ShowSelected();
    }

    /// <summary>The star of the machine on the stage.</summary>
    public void ToggleStagedFavourite()
    {
        if (StagedCustom)
        {
            Builder.ToggleFavouriteCommand.Execute(null);
        }
        else
        {
            ToggleFavourite(MachineIndex);
        }
        Restaged();
    }

    /// <summary>The next choice to see the stats through (round to Stock).</summary>
    public void NextLens()
    {
        if (LensSteps)
        {
            LensIndex = (LensIndex + 1) % Lenses.Count;
        }
    }

    /// <summary>Every letter chosen in Sort &amp; filter let go (the search stays).</summary>
    public void ClearLetters()
    {
        Filter = Filter with { Body = 0, Boost = 0, Grip = 0 };
        ShowLetters();
        ShowChoices();
        Restaged();
    }

    /// <summary>The trail's swatches for the machine on the stage: a custom machine's booster's, else the stock
    /// machine's.</summary>
    public void ShowStagedTrail()
    {
        if (!StagedCustom && !Machines.Has(MachineIndex))
        {
            return;
        }
        _ = Trail.ShowAsync(StagedCustom ? Theme.GameSkin.CustomTrailSwatchesAsync(Builder.Build.Booster) : Theme.GameSkin.TrailSwatchesAsync(MachineIndex));
    }

    /// <summary>The tile that stands for the machine on the stage in the strip, if the shelf holds it.</summary>
    public object? StagedTile() => StagedCustom
        ? (Builder.Editing is { } i ? Roster.OfType<CustomCard>().FirstOrDefault(c => c.Index == i) : null)
        : Roster.OfType<GarageCard>().FirstOrDefault(c => c.Id == MachineIndex);
}
