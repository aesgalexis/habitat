using Habitat.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.UI;
using WinRT;

namespace Habitat;

public sealed partial class MainWindow : Window, IWindowHost
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private readonly AppServices _services;
    private bool _closing;
    private bool _closeReady;
    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    private readonly MainPage _page;
    private readonly Grid _windowRoot = new();
    private readonly Grid _titleSurface = new();
    private readonly TextBlock _titleText = new() { Text = "HABITAT", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _titleIcon = new() { Width = 6.4, Height = 6.4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Fill = new SolidColorBrush(Color.FromArgb(255, 0x89, 0x95, 0xFF)) };
    private readonly ColumnDefinition _captionInset = new() { Width = new GridLength(138) };
    private Windows.Graphics.RectInt32? _menuInputRect;
    private bool _captionLayoutPending;
    private readonly DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration? _backdropConfiguration;
    private string _statusIcon = string.Empty;
    public bool IsActive { get; private set; }
    public void CloseForUpdate() => Close();

    internal MainWindow(AppServices services)
    {
        _services = services;
        Title = "HABITAT";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HabitatDot.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        _page = new MainPage(services, this);
        BuildWindowChrome();
        if (DesktopAcrylicController.IsSupported())
        {
            _backdropConfiguration = new SystemBackdropConfiguration { IsInputActive = true };
            _acrylic = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_backdropConfiguration);
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
        _page.RestoreWindowPlacement(AppWindow);
        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => { _closing = true; _acrylic?.Dispose(); };
    }

    private void BuildWindowChrome()
    {
        _windowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _titleSurface.Height = 32;
        _titleSurface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleSurface.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleSurface.ColumnDefinitions.Add(_captionInset);

        var dragArea = new Grid { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Padding = new Thickness(12, 0, 12, 0), ColumnSpacing = 8 };
        dragArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dragArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconSlot = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        iconSlot.Children.Add(_titleIcon);
        dragArea.Children.Add(iconSlot);
        Grid.SetColumn(_titleText, 1);
        dragArea.Children.Add(_titleText);
        _titleSurface.Children.Add(dragArea);
        Grid.SetColumn(_page.WindowMenuButton, 1);
        _titleSurface.Children.Add(_page.WindowMenuButton);
        _windowRoot.Children.Add(_titleSurface);
        Grid.SetRow(_page, 1);
        _windowRoot.Children.Add(_page);
        Content = _windowRoot;
        _page.RegisterDestinationWheel(_windowRoot);

        ExtendsContentIntoTitleBar = true;
        // Keep dragging separate from the menu's explicit native input region.
        SetTitleBar(dragArea);
        _titleSurface.Loaded += (_, _) => QueueCaptionLayout();
        _titleSurface.SizeChanged += (_, _) => QueueCaptionLayout();
        _titleSurface.LayoutUpdated += (_, _) => UpdateCaptionInputRegion();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPresenterChange) QueueCaptionLayout();
        };
        _windowRoot.Loaded += (_, _) =>
        {
            if (_windowRoot.XamlRoot is { } root) root.Changed += (_, _) => QueueCaptionLayout();
        };
    }

    internal void ActivateComposer(string? text)
    {
        if (_closing) return;
        if (IsIconic(Handle)) ShowWindowAsync(Handle, 9 /* SW_RESTORE */);
        Activate();
        _ = SetForegroundWindow(Handle);
        _page.ReceiveActivation(text);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);

    private void QueueCaptionLayout()
    {
        if (_closing || _captionLayoutPending) return;
        _captionLayoutPending = DispatcherQueue.TryEnqueue(() =>
        {
            _captionLayoutPending = false;
            UpdateCaptionLayout();
        });
    }

    private void UpdateCaptionLayout()
    {
        if (_closing) return;
        var scale = _windowRoot.XamlRoot?.RasterizationScale ?? 1;
        if (!double.IsFinite(scale) || scale <= 0) return;
        // Insets are physical pixels and change with DPI and window state.
        // Windows reports a negative inset while minimizing (observed: -14).
        // Passing it to GridLength throws through AppWindow.Changed and causes
        // the native windowing layer to fail-fast instead of recovering.
        var inset = Math.Max(0, AppWindow.TitleBar.RightInset) / scale;
        var height = AppWindow.TitleBar.Height > 0 ? AppWindow.TitleBar.Height / scale : 32;
        if (_captionInset.Width.Value != inset) _captionInset.Width = new GridLength(inset);
        if (_titleSurface.Height != height) _titleSurface.Height = height;
    }

    private void UpdateCaptionInputRegion()
    {
        var button = _page.WindowMenuButton;
        if (_closing || _windowRoot.XamlRoot is not { } root || button.ActualWidth <= 0 || button.ActualHeight <= 0) return;
        // Native regions use physical pixels. Read the final arranged bounds so
        // resizing, maximizing and DPI changes never leave the menu in a drag region.
        var bounds = button.TransformToVisual(_windowRoot).TransformBounds(
            new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
        var scale = root.RasterizationScale;
        var left = (int)Math.Floor(bounds.X * scale);
        var top = (int)Math.Floor(bounds.Y * scale);
        var rect = new Windows.Graphics.RectInt32(left, top,
            (int)Math.Ceiling(bounds.Right * scale) - left, (int)Math.Ceiling(bounds.Bottom * scale) - top);
        if (_menuInputRect is { } previous && previous.Equals(rect)) return;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
        _menuInputRect = rect;
    }

    public void SetBackgroundAppearance(double percent, bool dark)
    {
        var opacity = (float)Math.Clamp(percent / 100d, 0, 1);
        var tint = dark
            ? Color.FromArgb(0xFF, 0x10, 0x16, 0x23)
            : Color.FromArgb(0xFF, 0xF2, 0xF6, 0xFB);
        var titleBackground = dark
            ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var titleForeground = dark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        var hover = dark
            ? Color.FromArgb(0xFF, 0x28, 0x28, 0x28)
            : Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8);
        var pressed = dark
            ? Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)
            : Color.FromArgb(0xFF, 0xD5, 0xD5, 0xD5);

        var titleBar = AppWindow.TitleBar;
        _windowRoot.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _titleSurface.Background = new SolidColorBrush(titleBackground);
        _titleText.Foreground = new SolidColorBrush(titleForeground);
        titleBar.BackgroundColor = titleBackground;
        titleBar.InactiveBackgroundColor = titleBackground;
        titleBar.ForegroundColor = titleForeground;
        titleBar.InactiveForegroundColor = titleForeground;
        titleBar.ButtonBackgroundColor = titleBackground;
        titleBar.ButtonInactiveBackgroundColor = titleBackground;
        titleBar.ButtonForegroundColor = titleForeground;
        titleBar.ButtonInactiveForegroundColor = titleForeground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = titleForeground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = titleForeground;

        if (_acrylic is not null && _backdropConfiguration is not null)
        {
            _backdropConfiguration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            _acrylic.TintColor = tint;
            _acrylic.FallbackColor = _acrylic.TintColor;
            _acrylic.TintOpacity = opacity;
            _acrylic.LuminosityOpacity = opacity;
        }
        _page.SetBackgroundOpacity(percent);
    }

    public void SetStatusPresentation(string text, string status)
    {
        var title = string.IsNullOrWhiteSpace(text) ? "HABITAT" : $"HABITAT - {text}";
        if (Title != title) Title = title;
        if (_titleText.Text != title) _titleText.Text = title;
        var normalized = status switch
        {
            "Success" => "Success",
            "Warning" => "Warning",
            "Danger" => "Danger",
            _ => "Active"
        };
        if (_statusIcon == normalized)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", $"Status{normalized}.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
            _statusIcon = normalized;
        }
        _titleIcon.Fill = new SolidColorBrush(normalized switch
        {
            "Success" => Color.FromArgb(255, 0x55, 0xC7, 0x9C),
            "Warning" => Color.FromArgb(255, 0xE2, 0xA0, 0x6B),
            "Danger" => Color.FromArgb(255, 0xED, 0x71, 0x81),
            _ => Color.FromArgb(255, 0x89, 0x95, 0xFF)
        });
    }

    public bool DetectBackdropIsDark(bool current)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (IsIconic(window) || !GetClientRect(window, out var client)) return current;
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin)) return current;
        var scale = _page.XamlRoot?.RasterizationScale ?? 1;
        var points = BackdropThemeDetector.SamplePoints(client.Right, client.Bottom, scale);

        // Read the visible acrylic itself. Pixels outside HABITAT may belong to
        // a different background, especially with maximized or overlapping windows.
        if (_page.HasAppearanceOverlay) return current;

        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualRight = virtualLeft + GetSystemMetrics(SmCxVirtualScreen);
        var virtualBottom = virtualTop + GetSystemMetrics(SmCyVirtualScreen);
        var luminances = new List<double>();
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return current;
        }

        try
        {
            foreach (var point in points)
            {
                var location = new NativePoint { X = origin.X + point.X, Y = origin.Y + point.Y };
                if (location.X < virtualLeft || location.X >= virtualRight || location.Y < virtualTop || location.Y >= virtualBottom)
                {
                    continue;
                }
                // Covered areas and flyouts must not decide the backdrop theme.
                if (GetAncestor(WindowFromPoint(location), 2 /* GA_ROOT */) != window)
                {
                    continue;
                }

                var pixel = GetPixel(screen, location.X, location.Y);
                if (pixel == 0xFFFFFFFF)
                {
                    continue;
                }

                var red = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var blue = (pixel >> 16) & 0xFF;
                luminances.Add((0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        return BackdropThemeDetector.IsDark(luminances, current);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_closing) return;
        IsActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (IsActive)
        {
            _page.OnWindowActivated();
            _page.RefreshHybridAppearance(force: true);
            await Task.Delay(80);
            if (!_closing && IsActive) _page.FocusComposer();
        }
    }

    private async void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeReady) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            _page.SaveWindowPlacement(AppWindow);
            try { await _page.StopAsync(); }
            finally { await _services.DisposeAsync(); }
        }
        catch (Exception exception) { _services.ReportError("Close application", exception); }
        finally { _closeReady = true; Close(); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
