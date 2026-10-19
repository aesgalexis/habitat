using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Destinations;

internal sealed class VSCodeRunView
{
    private readonly TextBlock _summary = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _progress = new() { Width = 20, FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock _preview = new() { FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12,
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _output = new() { FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12,
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _truncated = new() { Text = "Output truncated", FontSize = 11, Opacity = 0.7, Visibility = Visibility.Collapsed };
    private readonly StackPanel _links = new() { Spacing = 2 };
    private readonly Button _details;
    private readonly Func<Uri, Task<bool>> _openLink;
    private readonly StackPanel _expanded = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    public StackPanel View { get; } = new() { Spacing = 6, Margin = new Thickness(0, 8, 0, 8) };
    public VSCodeEndpoint Endpoint { get; }
    public VSCodeExecution Snapshot { get; private set; }
    public bool NeedsPolling => Snapshot.EndedAt is null;
    internal string SummaryText => _summary.Text;
    internal string PreviewText => _preview.Text;
    internal string OutputText => _output.Text;
    internal bool IsAnimating => Snapshot.IsRunning;
    internal bool IsExpanded => _expanded.Visibility == Visibility.Visible;
    internal void ToggleOutput()
    {
        var expand = !IsExpanded;
        _details.Content = expand ? "Hide output" : "Show output";
        _expanded.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        _preview.Visibility = expand || _preview.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public VSCodeRunView(VSCodeProject project, string command, string? id, Func<Uri, Task<bool>>? openLink = null)
    {
        _openLink = openLink ?? (_ => Task.FromResult(false));
        Endpoint = project.Endpoint;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Snapshot = new VSCodeExecution(id ?? "", command, project.Folder.Path, "", id is null ? "unknown" : "starting",
            now, id is null ? now : null, null, 0, "", false);
        View.Children.Add(new TextBlock { Text = command, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        View.Children.Add(new TextBlock { Text = project.Folder.Path, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        status.Children.Add(_progress); status.Children.Add(_summary);
        View.Children.Add(status);
        View.Children.Add(_preview);
        View.Children.Add(_links);
        _expanded.Children.Add(_truncated);
        _expanded.Children.Add(new ScrollViewer { Content = _output, MaxHeight = 240,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        // Use the same native toggle pattern as Tasks: Expander's template
        // crashes in this application's bundled WinUI runtime.
        _details = new Button { Content = "Show output", HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed };
        _details.Click += (_, _) => ToggleOutput();
        View.Children.Add(_details);
        View.Children.Add(_expanded);
        AutomationProperties.SetName(_output, "Command output");
        AutomationProperties.SetName(_preview, "Recent command output");
        Update(Snapshot);
    }

    public void Update(VSCodeExecution snapshot)
    {
        Snapshot = snapshot;
        _summary.Text = snapshot.Summary;
        _progress.Text = new string('·', (int)(snapshot.ElapsedMs / 1000 % 3) + 1);
        _progress.Visibility = snapshot.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        _preview.Text = snapshot.Preview;
        _preview.Visibility = IsExpanded || _preview.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _output.Text = snapshot.Output;
        _details.Visibility = snapshot.Output.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _truncated.Visibility = snapshot.Truncated ? Visibility.Visible : Visibility.Collapsed;
        _links.Children.Clear();
        foreach (var uri in snapshot.Links)
        {
            var link = new HyperlinkButton { Content = uri.AbsoluteUri, Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += async (_, _) =>
            {
                try
                {
                    if (!await _openLink(uri)) _summary.Text = "Could not open link. Check that Google Chrome is installed, then retry.";
                }
                catch (Exception) { _summary.Text = "Could not open link. Check that Google Chrome is installed, then retry."; }
            };
            AutomationProperties.SetName(link, "Open " + uri.AbsoluteUri);
            _links.Children.Add(link);
        }
    }

    public void Disconnected() => Update(Snapshot with { State = "unknown" });
}
