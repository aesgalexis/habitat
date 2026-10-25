using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Habitat;

public sealed partial class MainPage
{
    private bool _aboutDialogOpen;

    private async Task ShowAboutAsync()
    {
        if (HasAppearanceOverlay || _activeDialog is not null || _operations.IsStopping) return;

        var assembly = typeof(App).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? "Unknown";
        var author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "aesgalexis";
        var content = new StackPanel { Spacing = 16 };
        var name = new TextBlock { Text = "HABITAT", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        content.Children.Add(name);
        content.Children.Add(new TextBlock
        {
            Text = "A native Windows workspace for your conversations, tools and everyday tasks.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.72
        });
        var details = new StackPanel { Spacing = 6 };
        foreach (var text in new[]
        {
            $"Version {version}",
            $"Developed by {author}",
            $"Windows · {RuntimeInformation.ProcessArchitecture}",
            $"Built with WinUI 3 and .NET {Environment.Version.Major}"
        })
            details.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        content.Children.Add(details);

        var links = new StackPanel { Spacing = 8 };
        foreach (var (label, url) in new[]
        {
            ("GitHub repository", "https://github.com/aesgalexis/habitat"),
            ("Release notes", "https://github.com/aesgalexis/habitat/releases"),
            ("MIT license", "https://github.com/aesgalexis/habitat/blob/main/LICENSE")
        })
        {
            var link = new HyperlinkButton
            {
                Content = label, NavigateUri = new Uri(url),
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0)
            };
            AutomationProperties.SetName(link, label);
            links.Children.Add(link);
        }
        content.Children.Add(links);
        content.Children.Add(new TextBlock { Text = "© 2026 aesgalexis", FontSize = 12, Opacity = 0.72 });

        var scroll = new ScrollViewer
        {
            Content = content,
            Width = Math.Max(240, Math.Min(360, XamlRoot.Size.Width - 96)),
            MaxHeight = Math.Max(120, Math.Min(460, XamlRoot.Size.Height - 180)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "About HABITAT", Content = scroll,
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close
        };
        StyleDialog(dialog);
        _aboutDialogOpen = true;
        try { await ShowDialogAsync(dialog); }
        finally { _aboutDialogOpen = false; FocusComposer(); }
    }
}
