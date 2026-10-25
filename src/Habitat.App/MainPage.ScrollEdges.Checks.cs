#if HABITAT_UI_CHECKS
using System.Runtime.InteropServices;
using Habitat.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckScrollEdgesUiAsync(Window window, string logPath)
    {
        var surface = (Grid)Content;
        var text = new TextBlock { Text = "Keep literal text · Español ☕", IsTextSelectionEnabled = true };
        var button = new Button { Content = "Native action", Width = 120,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, "Synthetic native action");
        var content = new Grid { Height = 1000, Background = new SolidColorBrush(Microsoft.UI.Colors.Blue) };
        content.Children.Add(text); content.Children.Add(button);
        var scroll = ScrollEdgeFade.Enable(new ScrollViewer { Content = content, Width = 300, Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        ScrollEdgeFade.Enable(scroll); // Repeated setup must not duplicate rendering or event handlers.
        var host = new Grid { Width = 320, Height = 180, Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(scroll);
        surface.Children.Add(host);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var theme = RequestedTheme;
        var language = UiText.Language;
        SetFadeCheckWindowPos(handle, new(-1), 0, 0, 0, 0, 0x13);
        try
        {
            await WaitForFadeLayoutAsync();
            if (!ScrollEdgeFade.IsActive(scroll) || scroll.ScrollableHeight < 500 ||
                ElementCompositionPreview.GetElementChildVisual(scroll) is not SpriteVisual { IsHitTestVisible: false })
                throw new InvalidOperationException("Overflowing scroll content must have one noninteractive composition mask.");

            foreach (var currentTheme in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (var currentLanguage in new[] { "en", "es" })
            {
                RequestedTheme = currentTheme;
                UiText.SetLanguage(currentLanguage);
                scroll.ChangeView(null, 0, null, true);
                await WaitForFadeLayoutAsync();
                AssertFadePixels(scroll, topFaded: false, bottomFaded: true);
                scroll.ChangeView(null, scroll.ScrollableHeight / 2, null, true);
                await WaitForFadeLayoutAsync();
                AssertFadePixels(scroll, topFaded: true, bottomFaded: true);
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await WaitForFadeLayoutAsync();
                AssertFadePixels(scroll, topFaded: true, bottomFaded: false);
                if (!ReferenceEquals(scroll.Content, content) || !text.IsTextSelectionEnabled ||
                    text.Text != "Keep literal text · Español ☕" || !button.Focus(FocusState.Programmatic) ||
                    AutomationProperties.GetName(button) != "Synthetic native action")
                    throw new InvalidOperationException("The fade must preserve the original text, native input and accessibility.");
            }
            scroll.Height = 100;
            await WaitForFadeLayoutAsync();
            scroll.ChangeView(null, scroll.ScrollableHeight / 2, null, true);
            await WaitForFadeLayoutAsync();
            AssertFadePixels(scroll, topFaded: true, bottomFaded: true);
            content.Height = 40;
            await WaitForFadeLayoutAsync();
            if (ScrollEdgeFade.IsActive(scroll) || ElementCompositionPreview.GetElementChildVisual(scroll) is not null)
                throw new InvalidOperationException("Content that fits must restore native rendering and release the mask.");
            content.Height = 1000;
            await WaitForFadeLayoutAsync();
            var presenter = FadeDescendants<ScrollContentPresenter>(scroll).First();
            var source = ElementCompositionPreview.GetElementVisual(presenter);
            if (!ScrollEdgeFade.IsActive(scroll) || source.Opacity != 0)
                throw new InvalidOperationException("Growing content must recreate the fade without drawing a second unmasked copy.");
            host.Children.Remove(scroll);
            await Task.Delay(60);
            if (ScrollEdgeFade.IsActive(scroll) || source.Opacity != 1)
                throw new InvalidOperationException("Unloading must release composition surfaces and restore the source opacity.");
            host.Children.Add(scroll);
            await WaitForFadeLayoutAsync();
            if (!ScrollEdgeFade.IsActive(scroll)) throw new InvalidOperationException("Returning to a destination must restore its fade.");
            if (!ScrollEdgeFade.IsEnabled(ConversationScroll) || !ScrollEdgeFade.IsEnabled(_taskArea))
                throw new InvalidOperationException("OpenAI and the task panel must use the shared scroll treatment.");
            foreach (var destination in new[] { ComposerDestination.Codex, ComposerDestination.Google, ComposerDestination.YouTube,
                ComposerDestination.Translate, ComposerDestination.VSCode })
            {
                var view = _destinationSections[destination].View;
                var scrolls = (view is ScrollViewer own ? new[] { own } : FadeDescendants<ScrollViewer>(view)).ToArray();
                if (scrolls.Length == 0 || scrolls.Any(item => !ScrollEdgeFade.IsEnabled(item)))
                    throw new InvalidOperationException("Every destination scroll viewport must use the same fade: " + destination);
            }
            File.AppendAllText(logPath, "PASS: rendered scroll pixels reveal the actual background over 20px at hidden edges only, in both themes and languages; resizing, native input, accessibility, destination coverage and unload/reload preserve the original content\n");
        }
        finally
        {
            surface.Children.Remove(host);
            SetFadeCheckWindowPos(handle, new(-2), 0, 0, 0, 0, 0x13);
            RequestedTheme = theme;
            UiText.SetLanguage(language);
        }

        async Task WaitForFadeLayoutAsync()
        {
            UpdateLayout();
            await Task.Delay(100);
        }

        void AssertFadePixels(ScrollViewer viewer, bool topFaded, bool bottomFaded)
        {
            var viewport = FadeDescendants<ScrollContentPresenter>(viewer).First();
            var origin = viewport.TransformToVisual(this).TransformPoint(new(0, 0));
            var client = new FadeCheckPoint();
            if (!FadeClientToScreen(handle, ref client)) throw new InvalidOperationException("Cannot resolve the synthetic window position.");
            var context = FadeGetDC(0);
            try
            {
                var scale = XamlRoot.RasterizationScale;
                uint Pixel(double y) => FadeGetPixel(context, client.X + (int)Math.Round((origin.X + 6) * scale),
                    client.Y + (int)Math.Round((origin.Y + y) * scale));
                var top = Pixel(1);
                var middle = Pixel(viewport.ActualHeight / 2);
                var bottom = Pixel(viewport.ActualHeight - 2);
                var topInner = Pixel(ScrollEdgeFade.Length + 1);
                var bottomInner = Pixel(viewport.ActualHeight - ScrollEdgeFade.Length - 1);
                bool Correct(uint pixel, bool faded) => pixel != uint.MaxValue && (faded
                    ? (pixel & 255) > 180 && ((pixel >> 16) & 255) < 80
                    : (pixel & 255) < 20 && ((pixel >> 16) & 255) > 230);
                if (!Correct(top, topFaded) || !Correct(bottom, bottomFaded) || !Correct(middle, false) ||
                    !Correct(topInner, false) || !Correct(bottomInner, false))
                    throw new InvalidOperationException($"Rendered edge colors are wrong: top={top:X8}, center={middle:X8}, bottom={bottom:X8}; expected fades {topFaded}/{bottomFaded}.");
            }
            finally { FadeReleaseDC(0, context); }
        }
    }

    private static IEnumerable<T> FadeDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result) yield return result;
            foreach (var descendant in FadeDescendants<T>(child)) yield return descendant;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FadeCheckPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetDC")]
    private static extern nint FadeGetDC(nint window);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static extern int FadeReleaseDC(nint window, nint context);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FadeClientToScreen(nint window, ref FadeCheckPoint point);
    [DllImport("gdi32.dll", EntryPoint = "GetPixel")]
    private static extern uint FadeGetPixel(nint context, int x, int y);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFadeCheckWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
#endif
