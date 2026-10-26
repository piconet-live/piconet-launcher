using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The left column's one notice (ColumnNoticeView.axaml), on <see cref="ViewModels.PlayControlViewModel"/>.</summary>
public sealed partial class ColumnNoticeView : UserControl
{
    public ColumnNoticeView() => AvaloniaXamlLoader.Load(this);
}
