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
        ?? typeof(AboutView).Assembly.GetName().Version?.ToString(3) ?? UiText.Get("Unknown");
    internal TextBlock ActionStatus { get; } = Text("");
    internal Button CopyDetails { get; } = UiLanguage.Watch(new Button() { Content = UiText.Get("CopyAppDetails"), HorizontalAlignment = HorizontalAlignment.Left }, ("Content", () => (object?)(UiText.Get("CopyAppDetails"))));
    internal string AppDetails { get; }
    internal List<HyperlinkButton> Links { get; } = new();

    internal AboutView()
    {
        var platform = $"Windows · {RuntimeInformation.ProcessArchitecture} · {RuntimeInformation.FrameworkDescription} · WinUI 3";
        AppDetails = $"HABITAT {AppVersion}{Environment.NewLine}{platform}{Environment.NewLine}{RuntimeInformation.OSDescription}";
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 0, 12, 8) };
        var identity = new StackPanel { Spacing = 6 };
        identity.Children.Add(new TextBlock { Text = "HABITAT", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        identity.Children.Add(Text(UiText.Format("VersionLabel", AppVersion)));
        identity.Children.Add(Text(UiText.Get("AboutTagline")));
        identity.Children.Add(Text(platform));
        panel.Children.Add(identity);
        panel.Children.Add(Section(UiText.Get("CreatedBy"), Text(UiText.Get("LicenseNotice"))));

        var project = new StackPanel { Spacing = 2 };
        AddLink(project, UiText.Get("ProjectOnGitHub"), "https://github.com/aesgalexis/habitat");
        AddLink(project, UiText.Get("ReleasesAndUpdates"), "https://github.com/aesgalexis/habitat/releases/latest");
        AddLink(project, UiText.Get("ReportIssue"), "https://github.com/aesgalexis/habitat/issues");
        AddLink(project, UiText.Get("SetupGuide"), "https://github.com/aesgalexis/habitat/blob/main/docs/GETTING_STARTED.md");
        AddLink(project, UiText.Get("MITLicense"), "https://github.com/aesgalexis/habitat/blob/main/LICENSE");
        panel.Children.Add(project);

        var privacy = new StackPanel { Spacing = 8 };
        privacy.Children.Add(Text(UiText.Get("PrivacyNotice")));
        AddLink(privacy, UiText.Get("BridgePrivacyPolicy"), "https://github.com/aesgalexis/habitat/blob/main/docs/EXTENSION_PRIVACY.md");
        panel.Children.Add(Section(UiText.Get("DataAndPrivacy"), privacy));
        var credits = new StackPanel { Spacing = 8 };
        credits.Children.Add(Text(UiText.Get("ThirdPartyNotice")));
        AddLink(credits, UiText.Get("WeatherCredits"), "https://open-meteo.com/");
        panel.Children.Add(Section(UiText.Get("Acknowledgements"), credits));
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
