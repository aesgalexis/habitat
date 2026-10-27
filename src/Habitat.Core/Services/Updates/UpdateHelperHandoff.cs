using System.Diagnostics;
using System.Text;

namespace Habitat.Services;

internal static class UpdateHelperHandoff
{
    internal static async Task StartAsync(ProcessStartInfo start, string ready, UpdateDiagnosticLog diagnostics,
        CancellationToken token, TimeSpan? preparationTimeout = null)
    {
        var output = new Capture();
        var error = new Capture();
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        using var helper = new Process { StartInfo = start };
        helper.OutputDataReceived += (_, value) => output.Add(value.Data);
        helper.ErrorDataReceived += (_, value) => error.Add(value.Data);
        diagnostics.Record("helper-start");
        try
        {
            if (!helper.Start()) throw new IOException("The update helper could not start.");
            helper.BeginOutputReadLine(); helper.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(preparationTimeout ?? TimeSpan.FromSeconds(45));
            while (!File.Exists(ready))
            {
                if (helper.HasExited)
                {
                    // Drain asynchronous output events after process exit.
                    helper.WaitForExit();
                    throw new IOException("The update helper could not prepare the installation. Update diagnostics were saved.");
                }
                await Task.Delay(100, timeout.Token);
            }
            token.ThrowIfCancellationRequested();
            diagnostics.Record("helper-ready", output: output.Text, error: error.Text);
        }
        catch (Exception exception)
        {
            int? exitCode = null;
            try
            {
                if (!helper.HasExited) helper.Kill();
                await helper.WaitForExitAsync();
                helper.WaitForExit();
                exitCode = helper.ExitCode;
            }
            catch (InvalidOperationException) { } // Start itself may have failed.
            diagnostics.Record("helper-preparation", exception, output.Text, error.Text, exitCode);
            throw;
        }
    }

    private sealed class Capture
    {
        private readonly object _gate = new();
        private readonly StringBuilder _text = new();
        internal void Add(string? value)
        {
            if (value is null) return;
            lock (_gate)
            {
                // Redact before retaining output in memory or on disk.
                _text.AppendLine(UpdateDiagnosticLog.Redact(value));
                if (_text.Length > 8192) _text.Remove(0, _text.Length - 8192);
            }
        }
        internal string Text { get { lock (_gate) return _text.ToString(); } }
    }
}
