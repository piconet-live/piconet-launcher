using CommunityToolkit.Mvvm.ComponentModel;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The Garage's machine setting in a lobby (the lobby's rule is a range, e.g. 80-100): the
// slider moves inside the range its server keeps the setting in, which the server clamps each racer's own into. The
// player's own setting is kept for a race that allows it: the slider shows it brought into the range, and only a
// move of the slider changes it.
public sealed partial class GarageViewModel
{
    /// <summary>The range the lobby's server keeps every racer's setting in (Quick Play: 100 alone); out of a lobby,
    /// 0 to 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingFixed), nameof(SettingFree), nameof(SettingLimited), nameof(SettingLimit),
        nameof(SettingLeast), nameof(SettingMost), nameof(ShownSetting), nameof(SettingText))]
    public partial SettingRange SettingRange { get; set; } = SettingRange.Free;

    /// <summary>One value: the slider shows it and cannot be moved.</summary>
    public bool SettingFixed => SettingRange.IsFixed;

    /// <summary>The slider can be moved (inside <see cref="SettingLeast"/> to <see cref="SettingMost"/>).</summary>
    public bool SettingFree => !SettingRange.IsFixed;

    /// <summary>The range is narrower than 0 to 100: the note beside the slider says it.</summary>
    public bool SettingLimited => !SettingRange.IsFree;

    /// <summary>The note beside the slider: "Fixed", or the range ("80-100").</summary>
    public string SettingLimit => SettingRange.IsFixed ? "Fixed" : SettingRange.Text;

    /// <summary>The slider's left end.</summary>
    public int SettingLeast => SettingRange.Low;

    /// <summary>The slider's right end.</summary>
    public int SettingMost => SettingRange.High;

    /// <summary>The slider's value: the player's own brought into the range. Moved, it is the player's own; a value
    /// the slider sets when its ends move under it (the one shown already) changes nothing.</summary>
    public int ShownSetting
    {
        get => SettingRange.Clamp(Setting);
        set
        {
            if (!SettingRange.IsFixed && value != ShownSetting)
            {
                Setting = SettingRange.Clamp(value);
            }
        }
    }
}
