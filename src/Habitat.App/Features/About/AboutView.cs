using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.About;

internal sealed class AboutView : Grid
{
    internal string AppVersion { get; } = typeof(AboutView).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutView).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
    internal TextBlock ActionStatus { get; } = Text("");
    internal Button CopyDetails { get; } = new() { Content = "Copy app details", HorizontalAlignment = HorizontalAlignment.Left };
    internal string AppDetails { get; }
    internal List<HyperlinkButton> Links { get; } = new();

    internal AboutView()
    {
        var platform = $"Windows · {RuntimeInformation.ProcessArchitecture} · {RuntimeInformation.FrameworkDescription} · WinUI 3";
        AppDetails = $"HABITAT {AppVersion}{Environment.NewLine}{platform}{Environment.NewLine}{RuntimeInformation.OSDescription}";
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 0, 12, 8) };
        var identity = new StackPanel { Spacing = 6 };
        identity.Children.Add(new TextBlock { Text = "HABITAT", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        identity.Children.Add(Text($"Version {AppVersion}"));
        identity.Children.Add(Text("A quiet place for your ideas, tasks and everyday tools."));
        identity.Children.Add(Text(platform));
        panel.Children.Add(identity);
        panel.Children.Add(Section("Created by aesgalexis", Text("© 2026 aesgalexis · MIT License. Free and open source.")));

        var project = new StackPanel { Spacing = 2 };
        AddLink(project, "Project on GitHub", "https://github.com/aesgalexis/habitat");
        AddLink(project, "Releases and updates", "https://github.com/aesgalexis/habitat/releases/latest");
        AddLink(project, "Report an issue or suggest a feature", "https://github.com/aesgalexis/habitat/issues");
        AddLink(project, "Setup guide", "https://github.com/aesgalexis/habitat/blob/main/docs/GETTING_STARTED.md");
        AddLink(project, "MIT License", "https://github.com/aesgalexis/habitat/blob/main/LICENSE");
        panel.Children.Add(project);

        var privacy = new StackPanel { Spacing = 8 };
        privacy.Children.Add(Text("Tasks and preferences are stored on this PC. Connected services process the content you submit under their own policies. Weather uses Open-Meteo; update checks contact GitHub."));
        AddLink(privacy, "Browser bridge privacy policy", "https://github.com/aesgalexis/habitat/blob/main/docs/EXTENSION_PRIVACY.md");
        panel.Children.Add(Section("Data and privacy", privacy));
        var credits = new StackPanel { Spacing = 8 };
        credits.Children.Add(Text("Built with .NET and Windows App SDK. HABITAT is an independent project; third-party names and trademarks belong to their respective owners."));
        AddLink(credits, "Weather data by Open-Meteo", "https://open-meteo.com/");
        panel.Children.Add(Section("Acknowledgements", credits));
        panel.Children.Add(CopyDetails);
        AutomationProperties.SetLiveSetting(ActionStatus, AutomationLiveSetting.Polite);
        panel.Children.Add(ActionStatus);
        Children.Add(new ScrollViewer
        {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
    }

    private void AddLink(StackPanel panel, string label, string address)
    {
        // Links use HABITAT's browser launcher, including its isolated test adapter.
        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Tag = new Uri(address),
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4, 0, 4)
        };
        AutomationProperties.SetName(link, label);
        Links.Add(link);
        panel.Children.Add(link);
    }

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    private static StackPanel Section(string title, UIElement content)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(content);
        return panel;
    }
}
