using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Habitat;

public sealed partial class MainPage
{
    private ControlTemplate? _widgetButtonTemplate;

    private void UseStableWidgetButton(Button button)
    {
        // The native template animates the background brush when switching
        // between hover, press and normal. Keep widget clicks visually steady.
        _widgetButtonTemplate ??= (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                <ContentPresenter x:Name="Presenter" Content="{TemplateBinding Content}"
                    ContentTemplate="{TemplateBinding ContentTemplate}"
                    Background="{TemplateBinding Background}" Foreground="{TemplateBinding Foreground}"
                    BorderThickness="{TemplateBinding BorderThickness}" BorderBrush="{TemplateBinding BorderBrush}"
                    CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}"
                    HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}">
                    <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup x:Name="CommonStates">
                            <VisualState x:Name="Normal" />
                            <VisualState x:Name="PointerOver">
                                <VisualState.Setters>
                                    <Setter Target="Presenter.Background" Value="{ThemeResource ButtonBackgroundPointerOver}" />
                                </VisualState.Setters>
                            </VisualState>
                            <VisualState x:Name="Pressed">
                                <VisualState.Setters>
                                    <Setter Target="Presenter.Background" Value="{ThemeResource ButtonBackgroundPointerOver}" />
                                </VisualState.Setters>
                            </VisualState>
                            <VisualState x:Name="Disabled">
                                <VisualState.Setters>
                                    <Setter Target="Presenter.Foreground" Value="{ThemeResource ButtonForegroundDisabled}" />
                                </VisualState.Setters>
                            </VisualState>
                        </VisualStateGroup>
                    </VisualStateManager.VisualStateGroups>
                </ContentPresenter>
            </ControlTemplate>
            """);
        if (!ReferenceEquals(button.Template, _widgetButtonTemplate))
            button.Template = _widgetButtonTemplate;
    }
}
