using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Controls;

/// <summary>The course picker: its view model is a <see cref="ViewModels.CoursePickerModel"/> (one course, or a
/// pool), given as its data context.</summary>
public sealed partial class CoursePicker : UserControl
{
    public CoursePicker() => AvaloniaXamlLoader.Load(this);
}
