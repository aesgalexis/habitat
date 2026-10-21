using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed class MarketsPreferencesView : StackPanel
{
    private readonly List<string> _symbols;
    private readonly StackPanel _favorites = new() { Spacing = 6 };
    internal TextBox SymbolInput { get; } = new() { MaxLength = 32 };
    internal Button AddButton { get; } = new();
    internal ComboBox Pinned { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public MarketsSettings Settings => new() { Symbols = _symbols.ToArray(), PinnedSymbol = (Pinned.SelectedItem as ComboBoxItem)?.Tag as string };

    public MarketsPreferencesView(MarketsSettings saved)
    {
        saved = saved.Normalize();
        _symbols = saved.Symbols.ToList();
        Spacing = 12;
        SymbolInput.Header = UiText.Get("MarketsSymbol");
        SymbolInput.PlaceholderText = UiText.Get("MarketsSymbolExample");
        UiLanguage.SetName(SymbolInput, () => UiText.Get("MarketsSymbol"));
        AddButton.Content = UiText.Get("MarketsAdd");
        AddButton.Click += (_, _) => Add();
        SymbolInput.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Add(); } };
        Pinned.Header = UiText.Get("MarketsSidebarSelection");
        UiLanguage.SetName(Pinned, () => UiText.Get("MarketsSidebarSelection"));
        Children.Add(_favorites);
        Children.Add(SymbolInput);
        Children.Add(AddButton);
        Children.Add(_status);
        Children.Add(Pinned);
        Children.Add(new TextBlock { Text = UiText.Format("MarketsFavoritesHelp", MarketsSettings.MaximumSymbols), TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
        Rebuild(saved.PinnedSymbol);
    }

    private void Add()
    {
        var symbol = MarketsSettings.NormalizeSymbol(SymbolInput.Text);
        if (symbol is null) { _status.Text = UiText.Get("MarketsInvalidSymbol"); return; }
        if (_symbols.Contains(symbol)) { _status.Text = UiText.Get("MarketsDuplicateSymbol"); return; }
        if (_symbols.Count >= MarketsSettings.MaximumSymbols) { _status.Text = UiText.Format("MarketsLimit", MarketsSettings.MaximumSymbols); return; }
        var pinned = Settings.PinnedSymbol;
        _symbols.Add(symbol);
        SymbolInput.Text = "";
        _status.Text = "";
        Rebuild(pinned);
        SymbolInput.Focus(FocusState.Keyboard);
    }

    private void Rebuild(string? pinned)
    {
        _favorites.Children.Clear();
        foreach (var symbol in _symbols)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = symbol, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 } };
            UiLanguage.SetName(remove, () => UiText.Format("MarketsRemove", symbol));
            remove.Click += (_, _) => { var selected = Settings.PinnedSymbol; _symbols.Remove(symbol); _status.Text = ""; Rebuild(selected); SymbolInput.Focus(FocusState.Keyboard); };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            _favorites.Children.Add(row);
        }
        Pinned.Items.Clear();
        Pinned.Items.Add(new ComboBoxItem { Content = UiText.Get("MarketsRotate") });
        foreach (var symbol in _symbols) Pinned.Items.Add(new ComboBoxItem { Content = symbol, Tag = symbol });
        Pinned.SelectedItem = Pinned.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == pinned) ?? Pinned.Items[0];
    }
}
