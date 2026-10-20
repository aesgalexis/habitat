#if HABITAT_UI_CHECKS
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckUpdatesUiAsync(string logPath)
    {
        if (UpdateButton.Visibility != Visibility.Collapsed || UpdateButton.Parent != Content)
            throw new InvalidOperationException("Updates must stay hidden until a newer release is found.");
        var original = _services.Updates;
        var theme = RequestedTheme;
        using var http = new HttpClient(new UpdateCheckHandler());
        using var updates = new AppUpdateService(http, new Version(0, 10, 0));
        _services.Updates = updates;
        try
        {
            await updates.CheckIfDueAsync(DateTimeOffset.UtcNow);
            RenderUpdateNotice();
            foreach (var currentTheme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = currentTheme;
                PaintSurface();
                foreach (var expanded in new[] { false, true })
                {
                    _sidebarManualExpanded = expanded;
                    UpdateWritingLayout();
                    UpdateLayout();
                    if (UpdateButton.Visibility != Visibility.Visible || UpdateButton.ActualWidth != 40 || UpdateButton.ActualHeight != 40 ||
                        UpdateButton.VerticalAlignment != VerticalAlignment.Bottom ||
                        AutomationProperties.GetName(UpdateButton) != "Update HABITAT to 0.11.0" ||
                        ToolTipService.GetToolTip(UpdateButton) is not string tip || !tip.Contains("same installer format (EXE or ZIP)") ||
                        _widgets.RowDefinitions.Count != 3)
                        throw new InvalidOperationException("The accessible update icon must stay at the bottom in both sidebar states and themes.");
                    var origin = UpdateButton.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
                    var sidebar = _widgets.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
                    if (Math.Abs(origin.X - sidebar.X) > 1 || origin.Y <= sidebar.Y + _widgets.ActualHeight)
                        throw new InvalidOperationException("The update icon overlaps the sidebar widgets or is misaligned.");
                    SetTasksOpen(true);
                    UpdateLayout();
                    var taskTop = _taskArea.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point()).Y;
                    if (taskTop + _taskArea.MaxHeight > origin.Y)
                        throw new InvalidOperationException("The task drawer must reserve space above the update icon.");
                    SetTasksOpen(false);
                }
            }
            await OpenUpdateAsync(); // Isolated launcher returns false and never opens a real browser.
            if (!UpdateButton.IsEnabled || UpdateButton.Visibility != Visibility.Visible ||
                ToolTipService.GetToolTip(UpdateButton) is not string error || !error.Contains("Could not open Chrome") ||
                AutomationProperties.GetItemStatus(UpdateButton) != "Could not open download. Retry.")
                throw new InvalidOperationException("A failed browser launch must retain the update and expose an English retry action.");
            File.AppendAllText(logPath, "PASS: automatic update notice, bottom sidebar icon in both themes and widths, task clearance, accessible version and browser retry; synthetic releases only\n");
        }
        finally
        {
            _services.Updates = original;
            RequestedTheme = theme;
            _sidebarManualExpanded = null;
            SetTasksOpen(false);
            RenderUpdateNotice();
            PaintSurface();
        }
    }

    private sealed class UpdateCheckHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v0.11.0","draft":false,"prerelease":false,"assets":[{"name":"HABITAT-0.11.0-x64-setup.exe","state":"uploaded","size":1024}]}""")
            });
    }
}
#endif
