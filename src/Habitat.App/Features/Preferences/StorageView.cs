using System.Globalization;
using Habitat.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed class StorageView : StackPanel
{
    internal Button Analyze { get; } = new() { Content = "Analyze storage" };
    internal Button Clean { get; } = new() { Content = "Clean up", IsEnabled = false };
    internal TextBlock Status { get; } = new() { Text = "Analyze generated files to see how much space can be recovered.", TextWrapping = TextWrapping.Wrap };
    private readonly Func<bool, CancellationToken, Task<StorageReport>> _run;
    private readonly CancellationTokenSource _stop = new();
    internal Task Pending { get; private set; } = Task.CompletedTask;

    internal StorageView(Func<bool, CancellationToken, Task<StorageReport>> run)
    {
        _run = run;
        Spacing = 12;
        Children.Add(new TextBlock { Text = "Generated files", FontSize = 18 });
        Children.Add(new TextBlock { Text = "Keeps the active version, a verified recovery version and files in use. Files newer than seven days and folders without verifiable ownership are kept.", TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
        Children.Add(Status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Analyze); buttons.Children.Add(Clean); Children.Add(buttons);
        AutomationProperties.SetName(Analyze, "Analyze storage");
        AutomationProperties.SetName(Clean, "Clean up generated files");
        AutomationProperties.SetLiveSetting(Status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Analyze.Click += (_, _) => { Pending = RunAsync(false); };
        Clean.Click += (_, _) => { Pending = RunAsync(true); };
    }

    internal async Task RunAsync(bool clean)
    {
        Analyze.IsEnabled = Clean.IsEnabled = false;
        Status.Text = clean ? "Cleaning up generated files…" : "Analyzing generated files…";
        try
        {
            var report = await _run(clean, _stop.Token);
            Status.Text = report.Busy ? "An update, build or cleanup is in progress. Try again shortly." :
                report.Unavailable ? "Generated storage could not be verified for this installation. No files were removed." :
                clean ? $"Freed {Size(report.FreedBytes)}. Analyze again to check remaining space." :
                $"{Size(report.RecoverableBytes)} can be recovered from {report.Candidates} folders.";
            if (!report.Busy && !report.Unavailable && report.Errors.Count > 0)
                Status.Text += " Some folders were kept because they could not be verified or accessed.";
            Clean.IsEnabled = !clean && !report.Busy && !report.Unavailable && report.RecoverableBytes > 0;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception) { Status.Text = "Storage maintenance could not finish. Analyze again to retry."; }
        finally { Analyze.IsEnabled = !_stop.IsCancellationRequested; }
    }

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GiB"
        : bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " MiB"
        : bytes.ToString(CultureInfo.InvariantCulture) + " bytes";

    internal async Task StopAsync() { await _stop.CancelAsync(); await Pending; _stop.Dispose(); }
}
