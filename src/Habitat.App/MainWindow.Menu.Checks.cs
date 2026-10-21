#if HABITAT_UI_CHECKS
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace Habitat;

public sealed partial class MainWindow
{
    internal void StartWindowMenuChecks()
    {
        var started = false;
        _windowRoot.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var log = Path.Combine(AppContext.BaseDirectory, "window-menu-checks.log");
            File.WriteAllText(log, "Checking real window chrome with isolated data; no mouse injection.\n");
            try
            {
                if (!_services.Paths.IsIsolated) throw new InvalidOperationException("Window checks require isolated data.");
                foreach (var width in new[] { 600, 1080 })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 720));
                    await Task.Delay(180);
                    CheckMenuHitTargets(log);
                }
                var presenter = (OverlappedPresenter)AppWindow.Presenter;
                presenter.Maximize();
                await Task.Delay(180);
                CheckMenuHitTargets(log);
                presenter.Restore();
                await Task.Delay(180);
                CheckMenuHitTargets(log);
                for (var cycle = 0; cycle < 30; cycle++)
                {
                    File.AppendAllText(log, $"Window lifecycle cycle {cycle}: minimize/restore/resize.\n");
                    MenuSendMessage(Handle, 0x001C /* WM_ACTIVATEAPP */, 0, 0);
                    presenter.Minimize();
                    await Task.Delay(30);
                    presenter.Restore();
                    MenuSendMessage(Handle, 0x001C /* WM_ACTIVATEAPP */, 1, 0);
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(cycle % 2 == 0 ? 600 : 1080, 720));
                    await Task.Delay(30);
                    CheckMenuHitTargets(log);
                }
                // Keep the page's normal periodic updates running while idle.
                // Every continuation must return to the UI dispatcher promptly.
                var heartbeat = System.Diagnostics.Stopwatch.StartNew();
                for (var tick = 0; tick < 60; tick++)
                {
                    var before = heartbeat.Elapsed;
                    await Task.Delay(100);
                    if (heartbeat.Elapsed - before > TimeSpan.FromSeconds(2))
                        throw new InvalidOperationException("The idle UI dispatcher stopped responding.");
                }
                File.AppendAllText(log, "PASS: 30 minimize/restore/activation cycles and idle timer updates keep the UI responsive.\n");
                await _page.CheckWindowMenuActionsAsync(log);
                File.AppendAllText(log, "PASS: window menu checks completed.\n");
                await _page.StopAsync();
                await _services.DisposeAsync();
                _closing = true;
                _closeReady = true;
                Close();
                Application.Current.Exit();
            }
            catch (Exception exception)
            {
                File.AppendAllText(log, "FAIL: " + exception + "\n");
                Environment.Exit(1);
            }
        };
        Activate();
    }

    private void CheckMenuHitTargets(string log)
    {
        var button = _page.WindowMenuButton;
        var scale = _windowRoot.XamlRoot.RasterizationScale;
        var bounds = button.TransformToVisual(_windowRoot).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        // Ask Windows how it routes input at the actual on-screen control, rather
        // than merely checking that a Click handler or flyout was assigned.
        foreach (var fraction in new[] { 0.1, 0.5, 0.9 })
        {
            var point = new NativePoint
            {
                X = (int)Math.Round((bounds.X + bounds.Width * fraction) * scale),
                Y = (int)Math.Round((bounds.Y + bounds.Height / 2) * scale)
            };
            if (!ClientToScreen(Handle, ref point)) throw new InvalidOperationException("Cannot resolve menu position.");
            var packed = (nint)((point.Y & 0xffff) << 16 | (point.X & 0xffff));
            var hit = MenuSendMessage(Handle, 0x0084 /* WM_NCHITTEST */, 0, packed);
            if (hit != 1 /* HTCLIENT */)
                throw new InvalidOperationException($"Menu input is intercepted by window chrome: hit={hit}, width={AppWindow.Size.Width}, scale={scale}.");
        }
        File.AppendAllText(log, $"PASS: menu receives client input across its width at window width {AppWindow.Size.Width}, scale {scale}.\n");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint MenuSendMessage(nint window, uint message, nuint wParam, nint lParam);
}
#endif
