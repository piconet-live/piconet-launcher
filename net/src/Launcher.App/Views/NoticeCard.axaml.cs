using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

/// <summary>A server notice, drawn the same in the chat, the banner and the list: the card takes its tier's class
/// from its item.</summary>
public sealed partial class NoticeCard : UserControl
{
    private static readonly string[] Tiers = ["info", "notice", "warning", "urgent"];

    public NoticeCard()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Mark();
    }

    /// <summary>The card's tier, by its class (one of four; an unknown word is info).</summary>
    public string Tier => this.FindControl<Border>("Card") is { } card ? Tiers.FirstOrDefault(card.Classes.Contains) ?? "info" : "info";

    private void Mark()
    {
        if (this.FindControl<Border>("Card") is not { } card)
        {
            return;
        }
        card.Classes.RemoveAll(Tiers);
        card.Classes.Add(DataContext is NoticeItem { Level: var level } && Tiers.Contains(level) ? level : "info");
    }
}
