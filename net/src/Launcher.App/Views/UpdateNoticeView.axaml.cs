using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Launcher.App.Views;

/// <summary>The launcher's update at the left column's foot (UpdateNoticeView.axaml), on <see cref="ViewModels.UpdateViewModel"/>.</summary>
public sealed partial class UpdateNoticeView : UserControl
{
    public UpdateNoticeView() => AvaloniaXamlLoader.Load(this);
}
