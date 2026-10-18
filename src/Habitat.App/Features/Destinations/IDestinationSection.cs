using Habitat.Services;
using Microsoft.UI.Xaml;

namespace Habitat.Features.Destinations;

internal interface IDestinationSection : IDisposable
{
    ComposerDestination Destination { get; }
    FrameworkElement View { get; }
    bool IsBusy { get; }
    bool HasContent { get; }
    event Action? StateChanged;
    Task SubmitAsync(string text);
    // Cancels immediately, then drains callbacks before shared services close.
    Task StopAsync();
}
