using Listenarr.Api.Services.Search;

namespace Listenarr.Api.Services.Search.Filters;

/// <summary>
/// Filters out results that look like physical products or have seller-like authors.
/// </summary>
public class ProductLikeTitleFilter : ISearchResultFilter
{
    public string FilterReason => "product_like_filtered";

    public bool ShouldFilter(SearchResult result)
    {
        // If this result was enriched by a metadata source (Amazon/Audible/Audible/Audnexus/OpenLibrary),
        // prefer the enriched metadata and do not treat it as a product-like false positive.
        if (result.IsEnriched && !string.IsNullOrWhiteSpace(result.MetadataSource))
        {
            return false;
        }

        return SearchValidation.IsProductLikeTitle(result.Title) || SearchValidation.IsSellerArtist(result.Artist);
    }
}
