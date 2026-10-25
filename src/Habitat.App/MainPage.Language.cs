namespace Habitat;

public sealed partial class MainPage
{
    private void ApplyInterfaceLanguage()
    {
        UiText.SetLanguage(_preferences.InterfaceLanguage);
        Language = UiText.Culture.Name;
        UiLanguage.Refresh();
        RebuildDestinationRail();
        TasksStatus.Text = UiLanguage.Value(TasksStatus, "Text", () => UiText.LocalizeMessage(TasksStatus.Text));
        RenderTasks();
        RenderGmail();
        RenderMarkets();
        RenderUpdateNotice();
        RenderCodexScheduleNotice();
        UpdateDestinationPresentation();
        _conversationView.RefreshLanguage();
        CodexChat.RefreshLanguage();
        ((Features.Destinations.VSCodeSection)_destinationSections[Services.ComposerDestination.VSCode]).RefreshLanguage();
        ((Features.Translation.TranslateSection)_destinationSections[Services.ComposerDestination.Translate]).RefreshLanguage();
        if (_weatherSnapshot is { } snapshot)
        {
            // The forecast and source location are application-owned fields.
            WeatherLocation.Text = snapshot.DisplayLocation;
            WeatherTemperature.Text = snapshot.Temperature.ToString("0", UiText.Culture) + "°C";
            WeatherOutlook.Text = $"{UiText.LocalizeMessage(snapshot.Outlook)} · {snapshot.PrecipitationChance.ToString(UiText.Culture)}% · 6h";
            PaintWeather();
        }
        UpdateWritingLayout();
    }
}
