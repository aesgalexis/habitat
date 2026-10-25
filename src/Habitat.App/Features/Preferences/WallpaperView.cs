using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Habitat.Features.Preferences;

internal sealed class WallpaperView : StackPanel
{
    private readonly WallpaperService? _service;
    private readonly Func<Task<IReadOnlyList<string>>> _pick;
    private readonly List<WallpaperImage> _imported = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _add;
    private readonly Button _remove;
    private readonly Button _restore;
    private readonly WallpaperSurface? _preview;
    private WallpaperSettings _settings;
    private bool _rebuilding;
    internal ComboBox Choices { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ComboBox Fit { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Slider Attenuation { get; } = new() { Minimum = 0, Maximum = 85, StepFrequency = 1 };
    internal WallpaperSettings Settings => _settings;
    internal Task PendingImport { get; private set; } = Task.CompletedTask;
    internal event Action? Changed;
    internal event Action? RestoreOriginal;
    internal bool IsBusy { get; private set; }
    internal event Action? BusyChanged;

    internal WallpaperView(WallpaperSettings settings, WallpaperService? service,
        Func<Task<IReadOnlyList<string>>>? pick)
    {
        _settings = settings.Normalize();
        _service = service;
        _pick = pick ?? (() => Task.FromResult<IReadOnlyList<string>>([]));
        Spacing = 12;
        Children.Add(new TextBlock { Text = UiText.Get("Wallpaper"), FontSize = 18 });
        Children.Add(Choices);
        UiLanguage.SetName(Choices, () => UiText.Get("Wallpaper"));
        if (service is not null)
        {
            _preview = new WallpaperSurface(service);
            var frame = new Grid { Height = 140 };
            frame.Children.Add(_preview);
            var original = new TextBlock { Text = UiText.Get("WallpaperOriginalDescription"), TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280 };
            frame.Children.Add(original);
            Changed += () => original.Visibility = Settings.SelectedId == WallpaperSettings.Original ? Visibility.Visible : Visibility.Collapsed;
            original.Visibility = Settings.SelectedId == WallpaperSettings.Original ? Visibility.Visible : Visibility.Collapsed;
            Children.Add(new Border { Child = frame, CornerRadius = new CornerRadius(8) });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _add = new Button { Content = UiText.Get("WallpaperAdd") };
        _remove = new Button { Content = UiText.Get("WallpaperRemove") };
        buttons.Children.Add(_add);
        buttons.Children.Add(_remove);
        Children.Add(buttons);
        Fit.Header = UiText.Get("WallpaperFit");
        UiLanguage.SetName(Fit, () => UiText.Get("WallpaperFit"));
        Fit.Items.Add(new ComboBoxItem { Tag = "fill", Content = UiText.Get("WallpaperFill") });
        Fit.Items.Add(new ComboBoxItem { Tag = "fit", Content = UiText.Get("WallpaperContain") });
        Fit.SelectedIndex = _settings.Fit == "fit" ? 1 : 0;
        Children.Add(Fit);
        var dimLabel = new TextBlock();
        void UpdateDimLabel() => dimLabel.Text = UiText.Format("WallpaperAttenuation", Attenuation.Value);
        Attenuation.Value = _settings.Attenuation;
        UpdateDimLabel();
        UiLanguage.SetName(Attenuation, () => UiText.Get("WallpaperAttenuationLabel"));
        Children.Add(dimLabel);
        Children.Add(Attenuation);
        _restore = new Button { Content = UiText.Get("WallpaperRestore") };
        Children.Add(_restore);
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Children.Add(_status);
        RebuildChoices();
        Choices.SelectionChanged += (_, _) =>
        {
            if (_rebuilding || Choices.SelectedItem is not ComboBoxItem item) return;
            _settings = _settings with { SelectedId = (string)item.Tag };
            _status.Text = "";
            NotifyChanged();
        };
        Fit.SelectionChanged += (_, _) =>
        {
            _settings = _settings with { Fit = Fit.SelectedIndex == 1 ? "fit" : "fill" };
            NotifyChanged();
        };
        Attenuation.ValueChanged += (_, _) =>
        {
            _settings = _settings with { Attenuation = Attenuation.Value };
            UpdateDimLabel();
            NotifyChanged();
        };
        _add.Click += async (_, _) => await (PendingImport = AddAsync());
        _remove.Click += (_, _) => RemoveSelected();
        _restore.Click += (_, _) => ResetOriginal();
        UpdateControls();
    }

    internal void ResetOriginal()
    {
        _settings = _settings with { SelectedId = WallpaperSettings.Original };
        RebuildChoices();
        NotifyChanged();
        RestoreOriginal?.Invoke();
    }

    internal void RemoveSelected()
    {
        if (!WallpaperSettings.IsImageId(_settings.SelectedId)) return;
        _settings = _settings with { Images = _settings.Images.Where(image => image.Id != _settings.SelectedId).ToArray(),
            SelectedId = WallpaperSettings.Original };
        RebuildChoices();
        NotifyChanged();
    }

    internal async Task AddAsync()
    {
        if (_service is null || IsBusy) return;
        IsBusy = true;
        UpdateControls();
        BusyChanged?.Invoke();
        try
        {
            var files = await _pick();
            if (files.Count == 0) return;
            _status.Text = UiText.Get("WallpaperLoading");
            var failed = false;
            foreach (var file in files)
            {
                if (_settings.Images.Length >= WallpaperSettings.MaxImages)
                {
                    _status.Text = UiText.Format("WallpaperLimit", WallpaperSettings.MaxImages);
                    return;
                }
                try
                {
                    var imported = await _service.ImportAsync(file);
                    _imported.Add(imported);
                    _settings = _settings with { Images = [.. _settings.Images, imported], SelectedId = imported.Id };
                    RebuildChoices();
                    NotifyChanged();
                }
                catch (Exception)
                {
                    failed = true;
                }
            }
            _status.Text = UiText.Get(failed ? "WallpaperImportFailed" : "WallpaperReady");
        }
        catch (Exception) { _status.Text = UiText.Get("WallpaperImportFailed"); }
        finally
        {
            IsBusy = false;
            UpdateControls();
            BusyChanged?.Invoke();
        }
    }

    internal async Task PreviewAsync(bool dark)
    {
        if (_preview is not null && !await _preview.ApplyAsync(_settings, dark, 480))
            _status.Text = UiText.Get("WallpaperUnavailable");
    }

    internal void Complete(bool saved, WallpaperSettings original)
    {
        _service?.Delete(saved ? original.Images.Concat(_imported) : _imported, saved ? _settings.Images : original.Images);
    }

    private void NotifyChanged()
    {
        UpdateControls();
        Changed?.Invoke();
    }

    private void RebuildChoices()
    {
        _rebuilding = true;
        try
        {
            Choices.Items.Clear();
            foreach (var id in WallpaperSettings.BuiltInIds)
                Choices.Items.Add(new ComboBoxItem { Tag = id, Content = UiText.Get(WallpaperSettings.Illustration(id)?.NameKey ?? (id switch
                {
                    "mist" => "WallpaperMist", "dusk" => "WallpaperDusk", "forest" => "WallpaperForest", _ => "WallpaperOriginal"
                })) });
            foreach (var image in _settings.Images)
                Choices.Items.Add(new ComboBoxItem { Tag = image.Id, Content = image.Name });
            Choices.SelectedItem = Choices.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == _settings.SelectedId);
        }
        finally { _rebuilding = false; }
    }

    private void UpdateControls()
    {
        Choices.IsEnabled = !IsBusy;
        _add.IsEnabled = !IsBusy && _settings.Images.Length < WallpaperSettings.MaxImages;
        _remove.IsEnabled = !IsBusy && WallpaperSettings.IsImageId(_settings.SelectedId);
        _restore.IsEnabled = !IsBusy;
        Fit.IsEnabled = WallpaperSettings.UsesImage(_settings.SelectedId);
        Attenuation.IsEnabled = _settings.SelectedId != WallpaperSettings.Original;
    }
}
