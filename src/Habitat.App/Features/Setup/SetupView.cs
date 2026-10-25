using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Setup;

internal sealed class SetupView : Grid
{
    internal TextBlock ChromeStatus { get; } = Text("");
    internal TextBlock ConnectionStatus { get; } = Text("");
    internal TextBlock ChatStatus { get; } = Text("");
    internal TextBlock ActionStatus { get; } = Text("");
    internal Button GetChrome { get; } = Action(UiText.Get("GetGoogleChrome"));
    internal Button InstallExtension { get; } = Action(UiText.Get("OpenChromeWebStore"));
    internal Button OpenExtensionFolder { get; } = Action(UiText.Get("OpenExtensionFolder"));
    internal Button OpenExtensions { get; } = Action(UiText.Get("OpenChromeExtensions"));
    internal Button OpenChat { get; } = Action(UiText.Get("OpenChatGPT"));
    internal Button OpenVSCodeFolder { get; } = Action(UiText.Get("OpenVSCodeExtensionFolder"));
    internal Button SetupGmail { get; } = Action(UiText.Get("SetUpGmail"));
    internal Button ReadGuide { get; } = Action(UiText.Get("ReadSetupGuide"));

    internal SetupView(InstallationFiles files)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ScrollEdgeFade.Enable(scroll);
        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(0, 0, 12, 8) };
        panel.Children.Add(Text(UiText.Get("ConnectTheToolsYouWantToUseTasks")));
        panel.Children.Add(Card("1. Google Chrome", ChromeStatus, GetChrome));
        panel.Children.Add(Card(UiText.Get("ChromeExtensionStep"), Text(UiText.Get("InstallHABITATBrowserBridgeThenChooseEnableConnection")),
            InstallExtension, ConnectionStatus,
            Text(UiText.Get("IfTheStoreListingIsNotAvailableYet")),
            PathText(files.ChromeExtensionDirectory), OpenExtensionFolder, OpenExtensions));
        panel.Children.Add(Card("3. ChatGPT", Text(UiText.Get("SignInToChatGPTInChromeAndOpen")), ChatStatus, OpenChat));
        panel.Children.Add(Card(UiText.Get("OptionalIntegrations"), Text(UiText.Get("VSCodeOpenTheFolderBelowThenUse")),
            PathText(files.VSCodeExtensionDirectory), OpenVSCodeFolder,
            Text(UiText.Get("GmailConnectYourOwnGoogleDesktopOAuthClient")), SetupGmail,
            Text(UiText.Get("CodexInstallTheCodexDesktopAppAndSign"))));
        panel.Children.Add(ReadGuide);
        panel.Children.Add(ActionStatus);
        OpenExtensionFolder.IsEnabled = files.ChromeExtensionDirectory is not null;
        OpenVSCodeFolder.IsEnabled = files.VSCodeExtensionDirectory is not null;
        scroll.Content = panel;
        Children.Add(scroll);
    }

    internal void Refresh(bool chromeInstalled, bool search, bool youtube, bool translation, bool chat)
    {
        ChromeStatus.Text = UiLanguage.Value(ChromeStatus, "Text", () => chromeInstalled ? UiText.Get("GoogleChromeIsInstalled") : UiText.Get("GoogleChromeWasNotFoundInstallItThen"));
        GetChrome.Visibility = chromeInstalled ? Visibility.Collapsed : Visibility.Visible;
        InstallExtension.IsEnabled = OpenExtensions.IsEnabled = OpenChat.IsEnabled = chromeInstalled;
        ConnectionStatus.Text = UiLanguage.Value(ConnectionStatus, "Text", () => UiText.Format("GoogleYouTubeTranslate", State(search), State(youtube), State(translation)));
        ChatStatus.Text = UiLanguage.Value(ChatStatus, "Text", () => chat ? UiText.Get("ChatGPTConnected") : UiText.Get("WaitingForALinkedChatGPTConversation"));
    }

    private static string State(bool connected) => connected ? UiText.Get("Connected") : UiText.Get("Waiting");
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static Button Action(string label) => new() { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBox PathText(string? path) => UiLanguage.Watch(new TextBox() { Text = path ?? UiText.Get("BundledExtensionNotFoundReinstallHABITATFromGitHub"), IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap, Header = UiText.Get("IncludedExtensionFolder") }, ("Text", () => (object?)(path ?? UiText.Get("BundledExtensionNotFoundReinstallHABITATFromGitHub"))), ("Header", () => (object?)(UiText.Get("IncludedExtensionFolder"))));
    private static StackPanel Card(string title, params UIElement[] controls)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
}
