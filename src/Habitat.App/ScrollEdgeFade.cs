using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Habitat;

// Fade only the scroll viewport. Its native controls still own input, selection,
// accessibility and scrollbars; the composition mask reveals the real backdrop.
internal static class ScrollEdgeFade
{
    internal const float Length = 20;
    private sealed class Owner : DependencyObject { }
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(object), typeof(Owner), new PropertyMetadata(null));

    internal static ScrollViewer Enable(ScrollViewer scroll)
    {
        if (scroll.GetValue(StateProperty) is not State) scroll.SetValue(StateProperty, new State(scroll));
        return scroll;
    }

    internal static bool IsEnabled(ScrollViewer scroll) => scroll.GetValue(StateProperty) is State;
    internal static bool IsActive(ScrollViewer scroll) => scroll.GetValue(StateProperty) is State { Active: true };

    private sealed class State
    {
        private readonly ScrollViewer _scroll;
        private ScrollContentPresenter? _presenter;
        private Visual? _source;
        private CompositionVisualSurface? _surface;
        private CompositionSurfaceBrush? _content;
        private CompositionLinearGradientBrush? _gradient;
        private CompositionMaskBrush? _mask;
        private SpriteVisual? _display;
        private readonly List<CompositionColorGradientStop> _stops = [];
        private float _opacity;
        private bool _unavailable;
        private (float Width, float Height, float X, float Y, float Top, float Bottom)? _last;
        public bool Active => _display is not null;

        public State(ScrollViewer scroll)
        {
            _scroll = scroll;
            scroll.Loaded += Loaded;
            scroll.Unloaded += Unloaded;
            scroll.RegisterPropertyChangedCallback(Control.TemplateProperty, (_, _) => { Release(); _presenter = null; });
            if (scroll.IsLoaded) Loaded(scroll, new RoutedEventArgs());
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            _scroll.LayoutUpdated -= LayoutUpdated;
            _scroll.LayoutUpdated += LayoutUpdated;
            _scroll.ViewChanged -= ViewChanged;
            _scroll.ViewChanged += ViewChanged;
            Update();
        }

        private void Unloaded(object sender, RoutedEventArgs args)
        {
            _scroll.LayoutUpdated -= LayoutUpdated;
            _scroll.ViewChanged -= ViewChanged;
            Release();
            _presenter = null;
            _unavailable = false;
        }

        private void LayoutUpdated(object? sender, object args) => Update();
        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Update();

        private void Update()
        {
            if (_unavailable || !_scroll.IsLoaded) return;
            if (_scroll.ScrollableHeight <= 0.5 || _scroll.ViewportHeight <= 0 || _scroll.ViewportWidth <= 0)
            {
                Release();
                return;
            }
            _presenter ??= FindPresenter(_scroll);
            if (_presenter is null || _presenter.ActualWidth <= 0 || _presenter.ActualHeight <= 0) return;
            var origin = _presenter.TransformToVisual(_scroll).TransformPoint(new(0, 0));
            var height = (float)_presenter.ActualHeight;
            var length = Math.Min(Length, height / 2);
            var top = (float)Math.Clamp(_scroll.VerticalOffset / length, 0, 1);
            var bottom = (float)Math.Clamp((_scroll.ScrollableHeight - _scroll.VerticalOffset) / length, 0, 1);
            var state = ((float)_presenter.ActualWidth, height, (float)origin.X, (float)origin.Y, top, bottom);
            if (_last == state) return;
            try
            {
                if (!Active) Create();
                _last = state;
                _surface!.SourceSize = new(state.Item1, height);
                _display!.Size = new(state.Item1, height);
                _display.Offset = new((float)origin.X, (float)origin.Y, 0);
                _stops[0].Color = Alpha(1 - top);
                _stops[1].Offset = length / height;
                _stops[2].Offset = 1 - length / height;
                _stops[3].Color = Alpha(1 - bottom);
            }
            catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException)
            {
                // Leave native content visible if composition is unavailable.
                Release();
                _unavailable = true;
                System.Diagnostics.Trace.TraceError("Scroll edge composition failed: {0}", exception);
            }
        }

        private void Create()
        {
            _source = ElementCompositionPreview.GetElementVisual(_presenter!);
            _opacity = _source.Opacity;
            var compositor = _source.Compositor;
            _surface = compositor.CreateVisualSurface();
            _surface.SourceVisual = _source;
            _content = compositor.CreateSurfaceBrush(_surface);
            _content.Stretch = CompositionStretch.None;
            _gradient = compositor.CreateLinearGradientBrush();
            _gradient.StartPoint = Vector2.Zero;
            _gradient.EndPoint = Vector2.UnitY;
            foreach (var offset in new[] { 0f, 0.1f, 0.9f, 1f })
            {
                var stop = compositor.CreateColorGradientStop(offset, Alpha(1));
                _stops.Add(stop);
                _gradient.ColorStops.Add(stop);
            }
            _mask = compositor.CreateMaskBrush();
            _mask.Source = _content;
            _mask.Mask = _gradient;
            _display = compositor.CreateSpriteVisual();
            _display.Brush = _mask;
            _display.Opacity = _opacity;
            _display.IsHitTestVisible = false;
            ElementCompositionPreview.SetElementChildVisual(_scroll, _display);
            // A visual surface captures the source subtree before the root's
            // opacity, as used by WinUI Gallery's OpacityMaskView control.
            _source.Opacity = 0;
        }

        private void Release()
        {
            _last = null;
            if (_source is not null) _source.Opacity = _opacity;
            if (_display is not null) ElementCompositionPreview.SetElementChildVisual(_scroll, null);
            if (_surface is not null) _surface.SourceVisual = null;
            _display?.Dispose(); _display = null;
            _mask?.Dispose(); _mask = null;
            _content?.Dispose(); _content = null;
            _surface?.Dispose(); _surface = null;
            _gradient?.Dispose(); _gradient = null;
            foreach (var stop in _stops) stop.Dispose();
            _stops.Clear();
            _source = null;
        }

        private static Color Alpha(float value) => Color.FromArgb((byte)Math.Round(value * 255), 255, 255, 255);
        private static ScrollContentPresenter? FindPresenter(DependencyObject root)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is ScrollContentPresenter presenter) return presenter;
                if (child is not ScrollViewer && FindPresenter(child) is { } nested) return nested;
            }
            return null;
        }
    }
}
