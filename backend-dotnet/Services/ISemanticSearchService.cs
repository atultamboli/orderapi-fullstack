using OrderApi.Models;

namespace OrderApi.Services;

public record SemanticSearchResult(
    string ProductName,
    string OrderNumber,
    string CustomerName,
    string ProductGroup,
    decimal Price,
    float Score        // cosine similarity — 1.0 = perfect match, 0.0 = unrelated
);

public interface ISemanticSearchService
{
    Task IndexOrdersAsync(IEnumerable<Order> orders);
    Task<IEnumerable<SemanticSearchResult>> SearchAsync(string query, int topN = 3);
    bool IsIndexed { get; }
}
