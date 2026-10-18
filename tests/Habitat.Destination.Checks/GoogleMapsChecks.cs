using Habitat.Services;

internal static class GoogleMapsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var routes = new (string Text, string Origin, string Destination)[]
        {
            ("distancia de Madrid a Barcelona", "Madrid", "Barcelona"),
            ("distancia Madrid a Barcelona", "Madrid", "Barcelona"),
            ("distancia entre Palma de Mallorca y Sóller", "Palma de Mallorca", "Sóller"),
            ("ruta de Santiago de Compostela a A Coruña", "Santiago de Compostela", "A Coruña"),
            ("trayecto desde Sevilla hasta Cádiz", "Sevilla", "Cádiz"),
            ("recorrido Madrid a Toledo", "Madrid", "Toledo"),
            ("cómo llegar de Valencia a Alicante", "Valencia", "Alicante"),
            ("como llegar de Valencia a Alicante", "Valencia", "Alicante"),
            ("cómo ir desde Bilbao hasta San Sebastián", "Bilbao", "San Sebastián"),
            ("indicaciones de Granada a Málaga", "Granada", "Málaga"),
            ("calcular la distancia de Madrid a Segovia", "Madrid", "Segovia"),
            ("de Madrid a Barcelona", "Madrid", "Barcelona"),
            ("desde Palma hasta Sóller", "Palma", "Sóller"),
            ("entre Madrid y Barcelona", "Madrid", "Barcelona"),
            ("distance from London to Oxford", "London", "Oxford"),
            ("distance between London and Oxford", "London", "Oxford"),
            ("route London to Oxford", "London", "Oxford"),
            ("directions from London to Oxford", "London", "Oxford"),
            ("from London to Oxford", "London", "Oxford"),
            ("  DISTANCIA:\tde  Palma de Mallorca  a\n  Sóller  ", "Palma de Mallorca", "Sóller"),
            ("distancia de 39.5696,2.6502 a 39.7662,2.7152", "39.5696,2.6502", "39.7662,2.7152"),
            ("ruta de Café & Bar #1, Palma a Hôtel + 雪? / &destination=other", "Café & Bar #1, Palma", "Hôtel + 雪? / &destination=other")
        };
        foreach (var (text, origin, destination) in routes)
        {
            var uri = ComposerDestinations.SearchUri(ComposerDestination.GoogleMaps, text)!;
            check(uri.AbsoluteUri == "https://www.google.com/maps/dir/?api=1&origin=" + Uri.EscapeDataString(origin) +
                "&destination=" + Uri.EscapeDataString(destination) && uri.Fragment == "",
                $"Google Maps opens directions with both exact places: {text.Trim().Replace('\n', ' ')}");
        }

        foreach (var text in new[]
        {
            "Plaza de España, Palma", "A Coruña", "Pan a mano, Madrid", "Road to Hana",
            "Ruta de la Plata", "restaurantes entre Madrid y Toledo", "distancia", "distancia de Madrid a",
            "distancia de a Barcelona", "ruta entre Madrid y", "de Madrid", "cómo llegar a Madrid",
            "  café & cats+dogs #1? 雪 ☕\nnext / https://example.com  "
        })
        {
            check(ComposerDestinations.SearchUri(ComposerDestination.GoogleMaps, text)!.AbsoluteUri ==
                "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(text),
                $"Google Maps preserves place searches and incomplete routes: {text.Trim().Replace('\n', ' ')}");
        }

        const string routeQuery = "distancia de Madrid a Barcelona";
        check(ComposerDestinations.SearchUri(ComposerDestination.Google, routeQuery)!.AbsoluteUri ==
            "https://www.google.com/search?q=" + Uri.EscapeDataString(routeQuery) &&
            ComposerDestinations.SearchUri(ComposerDestination.YouTube, routeQuery)!.AbsoluteUri ==
            "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(routeQuery),
            "Route recognition only affects the Google Maps destination");
        check(ComposerDestinations.Placeholder(ComposerDestination.GoogleMaps) == "Search places or get directions…",
            "Google Maps hints at place searches and directions in English");
    }
}
