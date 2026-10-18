using System.Text.RegularExpressions;

namespace Habitat.Services;

internal static partial class GoogleMapsDestination
{
    public static Uri SearchUri(string text)
    {
        var route = text.Trim();
        var intent = RouteIntent().Match(route);
        if (intent.Success) route = route[intent.Length..];

        var places = FromRoute().Match(route);
        if (!places.Success) places = BetweenRoute().Match(route);
        // A bare place search can contain "a" or "to"; require route intent
        // before treating those words alone as a separator.
        if (!places.Success && intent.Success) places = DirectRoute().Match(route);
        if (places.Success)
        {
            var origin = places.Groups["origin"].Value.Trim();
            var destination = places.Groups["destination"].Value.Trim();
            return new Uri("https://www.google.com/maps/dir/?api=1&origin=" + Uri.EscapeDataString(origin) +
                "&destination=" + Uri.EscapeDataString(destination));
        }

        return new Uri("https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(text));
    }

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;

    [GeneratedRegex(@"\A(?:(?:calcular\s+(?:la\s+)?)?distancia|ruta|trayecto|recorrido|indicaciones|c[oó]mo\s+(?:llegar|ir)|distance|route|directions)\b\s*:?\s*", Options)]
    private static partial Regex RouteIntent();

    [GeneratedRegex(@"\A(?:de|desde|from)\s+(?<origin>\S.*?)\s+(?:a|hasta|to)\s+(?<destination>\S.*)\z", Options)]
    private static partial Regex FromRoute();

    [GeneratedRegex(@"\A(?:entre|between)\s+(?<origin>\S.*?)\s+(?:y|and)\s+(?<destination>\S.*)\z", Options)]
    private static partial Regex BetweenRoute();

    [GeneratedRegex(@"\A(?!(?:de|desde|from|entre|between)\b)(?<origin>\S.*?)\s+(?:a|hasta|to)\s+(?<destination>\S.*)\z", Options)]
    private static partial Regex DirectRoute();
}
