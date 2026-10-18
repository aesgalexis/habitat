namespace Habitat.Services;

internal sealed record TopWidgetLayout(bool Expanded, double WidgetWidth, double ContentInset, double TaskLeft, double TaskWidth)
{
    public static TopWidgetLayout Calculate(double contentWidth, bool tasksOpen, bool? manuallyExpanded = null)
    {
        var expanded = manuallyExpanded == true && contentWidth >= 272;
        var widgetWidth = expanded ? 240 : 40;
        var taskWidth = Math.Min(240, Math.Max(0, contentWidth - 64));
        var taskLeft = expanded ? 0 : widgetWidth + 16;
        double inset = widgetWidth + (expanded ? 24 : 16);
        if (tasksOpen && !expanded) inset = taskLeft + taskWidth + 24;
        return new(expanded, widgetWidth, Math.Min(contentWidth, inset), taskLeft, taskWidth);
    }
}
