using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.NextUi;

/// <summary>
/// The redesigned Controller page (Settings › Controller, Views/NextUi/ControllerPageView): an option page with the
/// port, what is plugged into it, the device, the button icons and the confirm button, Test rumble, Set up in the game,
/// the two dead zones; then the buttons (A, then the one wanted) and "Reset the buttons"; on the right the GameCube
/// controller drawn as the game reads it, its buttons lit. Every row works the classic page's own view model
/// (<see cref="ControllerViewModel"/>): the game's own files, the controller helper.
/// </summary>
public sealed partial class ControllerPageViewModel : ViewModelBase, IDisposable
{
    private readonly ControllerViewModel _pad;
    private readonly SettingsViewModel _settings;
    private readonly List<OptionRow> _rows = [];

    public ControllerPageViewModel(ControllerViewModel pad, SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(pad);
        ArgumentNullException.ThrowIfNull(settings);
        (_pad, _settings) = (pad, settings);
        var c = pad;
        Groups =
        [
            Group("", "Controller",
                new ChoiceRow("Port", () => ControllerViewModel.PortNames, () => c.PortIndex, i => c.PortIndex = i, () => c.Enabled),
                new ChoiceRow("Input", () => ControllerViewModel.KindNames, () => c.KindIndex, i => c.KindIndex = i, () => c.Enabled && !c.OtherKind,
                    () => c.OtherKind ? "Set in the game to something this page does not offer." : ""),
                new ChoiceRow("Device", () => [.. c.Devices.Select(d => d.Label)], () => c.SelectedDevice is { } d ? c.Devices.IndexOf(d) : -1,
                    i => c.SelectedDevice = c.Devices[i], () => c.Enabled && c.IsController, () => c.DeviceMissing ? "Not plugged in." : ""),
                new ChoiceRow("Button icons", () => SettingsViewModel.ButtonIconsChoices, () => settings.ButtonIconsIndex, i => settings.ButtonIconsIndex = i),
                new ChoiceRow("Confirm button", () => SettingsViewModel.ConfirmButtonChoices, () => settings.ConfirmButtonIndex, i => settings.ConfirmButtonIndex = i),
                new ActionRow("Test rumble", () => "", () => c.RumbleCommand.Execute(null), () => c.CanRumble),
                new ActionRow("Set up in the game", () => "", () => c.SetUpInGameCommand.Execute(null), () => c.CanSetUpInGame, () => c.SetUpInGameNote),
                new NumberRow("Stick dead zone", () => c.StickDeadZone, v => c.StickDeadZone = v, 0, 50, 5, "%", () => c.Enabled && c.IsController),
                new NumberRow("C-Stick dead zone", () => c.CStickDeadZone, v => c.CStickDeadZone = v, 0, 50, 5, "%", () => c.Enabled && c.IsController)),
            Group("Buttons", "Buttons",
            [
                .. c.Rows.Select(row => new ActionRow(row.Label, () => row.Shown, () => c.SetCommand.Execute(row), () => c.Enabled && c.IsController)
                {
                    Help = $"Press A, then the button you want for {row.Label}.",
                }),
                new ActionRow("Reset the buttons", () => "", () => c.ResetLayoutCommand.Execute(null), () => c.CanResetLayout),
            ]),
        ];
        Focused = Groups[0].Items[0];
        pad.PropertyChanged += OnChanged;
        settings.PropertyChanged += OnChanged;
        pad.Devices.CollectionChanged += OnDevices;
        foreach (var row in pad.Rows)
        {
            row.PropertyChanged += OnChanged;
        }
    }

    public ControllerViewModel Pad => _pad;

    /// <summary>The port's rows, then its buttons.</summary>
    public IReadOnlyList<OptionGroup> Groups { get; }

    /// <summary>The row the focus is on (the help line's).</summary>
    [ObservableProperty]
    public partial OptionItem? Focused { get; set; }

    public void Dispose()
    {
        _pad.PropertyChanged -= OnChanged;
        _settings.PropertyChanged -= OnChanged;
        _pad.Devices.CollectionChanged -= OnDevices;
        foreach (var row in _pad.Rows)
        {
            row.PropertyChanged -= OnChanged;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => ShowAll();

    private void OnDevices(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ShowAll();

    private void ShowAll()
    {
        foreach (var row in _rows)
        {
            row.Show();
        }
    }

    private OptionGroup Group(string heading, string eyebrow, params OptionRow[] rows)
    {
        _rows.AddRange(rows);
        foreach (var row in rows)
        {
            row.Show();
        }
        return new OptionGroup(heading, [.. rows.Select(r => new OptionItem(r, eyebrow))]);
    }
}
