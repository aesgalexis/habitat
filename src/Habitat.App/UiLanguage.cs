using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

// Registrations belong to their controls, so closed dialogs and retired results
// can be collected. Only explicitly localized properties are registered.
internal static class UiLanguage
{
    private sealed class Registration(Func<object?> read, Action<object?> write, Func<object?> value, object? initial)
    {
        private object? _last = initial;
        private Func<object?> _value = value;
        public void Reset(Func<object?> next, object? current) { _value = next; _last = current; }
        public void Refresh()
        {
            // A result, draft or other later assignment owns the property now.
            if (!Equals(read(), _last)) return;
            var next = _value();
            write(next);
            _last = next;
        }
    }

    // A native WinUI collection can retain a control after its original managed
    // wrapper is collected. Keep the registrations on the native control itself.
    private sealed class PropertyOwner : DependencyObject { }
    private static readonly DependencyProperty RegistrationsProperty = DependencyProperty.RegisterAttached(
        "LocalizedProperties", typeof(object), typeof(PropertyOwner), new PropertyMetadata(null));
    private static Dictionary<object, Registration> Owners(object target)
    {
        var control = (DependencyObject)target;
        if (control.GetValue(RegistrationsProperty) is Dictionary<object, Registration> properties) return properties;
        properties = new();
        control.SetValue(RegistrationsProperty, properties);
        return properties;
    }
    private static readonly List<WeakReference<Registration>> Registrations = [];

    public static T Watch<T>(T control, params (string Property, Func<object?> Value)[] properties) where T : class
    {
        foreach (var (property, value) in properties) Register(control, property, value, ReadProperty(control, property));
        return control;
    }

    public static T Value<T>(object control, string property, Func<T> value)
    {
        var result = value();
        Register(control, property, () => value(), result);
        return result;
    }

    private static PropertyInfo Property(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Unknown localized property: {name}");
    private static object? ReadProperty(object target, string name) => Property(target, name).GetValue(target);

    private static void Register(object target, string name, Func<object?> value, object? initial)
    {
        var owner = Owners(target);
        if (owner.TryGetValue(name, out var existing)) { existing.Reset(value, initial); return; }
        var property = Property(target, name);
        var registration = new Registration(() => property.GetValue(target), v => property.SetValue(target, v), value, initial);
        owner[name] = registration;
        Registrations.Add(new(registration));
        if (Registrations.Count % 256 == 0) Registrations.RemoveAll(reference => !reference.TryGetTarget(out _));
    }

    private static void Attached(DependencyObject target, DependencyProperty property, Func<string> value)
    {
        target.SetValue(property, value());
        var owner = Owners(target);
        if (owner.TryGetValue(property, out var existing)) { existing.Reset(() => value(), target.GetValue(property)); return; }
        var registration = new Registration(() => target.GetValue(property), v => target.SetValue(property, v), () => value(), target.GetValue(property));
        owner[property] = registration;
        Registrations.Add(new(registration));
    }

    public static void SetName(DependencyObject target, Func<string> value) => Attached(target, AutomationProperties.NameProperty, value);
    public static void SetHelpText(DependencyObject target, Func<string> value) => Attached(target, AutomationProperties.HelpTextProperty, value);
    public static void SetItemStatus(DependencyObject target, Func<string> value) => Attached(target, AutomationProperties.ItemStatusProperty, value);
    public static void SetToolTip(DependencyObject target, Func<string> value) => Attached(target, ToolTipService.ToolTipProperty, value);

    public static void Message(TextBlock target, string message) =>
        target.Text = Value(target, nameof(TextBlock.Text), () => UiText.LocalizeMessage(message));

    public static void Refresh()
    {
        foreach (var reference in Registrations.ToArray())
            if (reference.TryGetTarget(out var registration)) registration.Refresh();
        Registrations.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
}
