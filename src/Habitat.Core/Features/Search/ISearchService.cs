namespace Habitat.Features.Search;

internal sealed record SearchResult(string Title, string Url, string Snippet);
internal sealed record SearchPage(IReadOnlyList<SearchResult> Results, int? NextStart = null);

internal interface ISearchService
{
    Task<SearchPage> SearchAsync(string query, int start, CancellationToken cancellationToken);
    void Reset();
}
