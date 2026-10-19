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
    internal Button GetChrome { get; } = Action("Get Google Chrome");
    internal Button InstallExtension { get; } = Action("Open Chrome Web Store");
    internal Button OpenExtensionFolder { get; } = Action("Open extension folder");
    internal Button OpenExtensions { get; } = Action("Open Chrome extensions");
    internal Button OpenChat { get; } = Action("Open ChatGPT");
    internal Button OpenVSCodeFolder { get; } = Action("Open VS Code extension folder");
    internal Button SetupGmail { get; } = Action("Set up Gmail…");
    internal Button ReadGuide { get; } = Action("Read setup guide");

    internal SetupView(InstallationFiles files)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(0, 0, 12, 8) };
        panel.Children.Add(Text("Connect the tools you want to use. Tasks and local tools work immediately; each optional integration stays on this PC."));
        panel.Children.Add(Card("1. Google Chrome", ChromeStatus, GetChrome));
        panel.Children.Add(Card("2. Chrome extension", Text("Install HABITAT · browser bridge, then choose Enable connection on its welcome page. Keep HABITAT and Chrome open. Chrome asks you to approve the installation."),
            InstallExtension, ConnectionStatus,
            Text("If the store listing is not available yet, open Chrome extensions, enable Developer mode and choose Load unpacked. Select the folder below, then open the extension and choose Connect automatically."),
            PathText(files.ChromeExtensionDirectory), OpenExtensionFolder, OpenExtensions));
        panel.Children.Add(Card("3. ChatGPT", Text("Sign in to ChatGPT in Chrome and open the conversation you want HABITAT to use. Google, YouTube and Translate do not require a ChatGPT account."), ChatStatus, OpenChat));
        panel.Children.Add(Card("Optional integrations", Text("VS Code: open the folder below, then use Extensions > … > Install from VSIX in VS Code. Select habitat-terminal and reload open VS Code windows. Install VS Code itself first if needed."),
            PathText(files.VSCodeExtensionDirectory), OpenVSCodeFolder,
            Text("Gmail: connect your own Google Desktop OAuth client and authorize your account. This step is optional."), SetupGmail,
            Text("Codex: install the Codex desktop app and sign in separately. Notepad, Terminal, Run and Tasks need no browser extension.")));
        panel.Children.Add(ReadGuide);
        panel.Children.Add(ActionStatus);
        OpenExtensionFolder.IsEnabled = files.ChromeExtensionDirectory is not null;
        OpenVSCodeFolder.IsEnabled = files.VSCodeExtensionDirectory is not null;
        scroll.Content = panel;
        Children.Add(scroll);
    }

    internal void Refresh(bool chromeInstalled, bool search, bool youtube, bool translation, bool chat)
    {
        ChromeStatus.Text = chromeInstalled ? "Google Chrome is installed." : "Google Chrome was not found. Install it, then return here.";
        GetChrome.Visibility = chromeInstalled ? Visibility.Collapsed : Visibility.Visible;
        InstallExtension.IsEnabled = OpenExtensions.IsEnabled = OpenChat.IsEnabled = chromeInstalled;
        ConnectionStatus.Text = $"Google: {State(search)} · YouTube: {State(youtube)} · Translate: {State(translation)}";
        ChatStatus.Text = chat ? "ChatGPT connected." : "Waiting for a linked ChatGPT conversation.";
    }

    private static string State(bool connected) => connected ? "Connected" : "Waiting";
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static Button Action(string label) => new() { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBox PathText(string? path) => new() { Text = path ?? "Bundled extension not found. Reinstall HABITAT from GitHub Releases.", IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap, Header = "Included extension folder" };
    private static StackPanel Card(string title, params UIElement[] controls)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
}
