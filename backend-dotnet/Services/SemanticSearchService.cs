using OrderApi.Models;

namespace OrderApi.Services;

// LEARNING NOTE — why Singleton?
// This service holds the in-memory index (all the stored vectors).
// If it were Scoped, a new instance would be created per request —
// meaning the index would be empty on every request.
// Singleton = one instance for the lifetime of the app = index persists.
public class SemanticSearchService : ISemanticSearchService
{
    private readonly IEmbeddingService _embeddings;

    // The index — each entry is one order line item + its vector
    private readonly List<IndexedItem> _index = new();

    public bool IsIndexed { get; private set; }

    public SemanticSearchService(IEmbeddingService embeddings)
    {
        _embeddings = embeddings;
    }

    public async Task IndexOrdersAsync(IEnumerable<Order> orders)
    {
        _index.Clear();

        // For each order line item, we build a descriptive text and embed it.
        // The richer the text, the better the search results.
        // "X-Wing Starfighter - Star Wars - $99.99" gives the model
        // enough context to understand what this item IS.
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
            {
                var document = $"{line.Name} - {line.ProductGroup} - ${line.Price} " +
                               $"- Order {order.OrderNumber} - {order.CustomerName}";

                var vector = await _embeddings.GetEmbeddingAsync(document);

                _index.Add(new IndexedItem(
                    ProductName:   line.Name,
                    OrderNumber:   order.OrderNumber,
                    CustomerName:  order.CustomerName,
                    ProductGroup:  line.ProductGroup,
                    Price:         line.Price,
                    Vector:        vector
                ));
            }
        }

        IsIndexed = true;
    }

    public async Task<IEnumerable<SemanticSearchResult>> SearchAsync(string query, int topN = 3)
    {
        // Convert the search query to a vector
        var queryVector = await _embeddings.GetEmbeddingAsync(query);

        // Compare the query vector against every stored vector
        // Order by similarity score descending, take the top N
        return _index
            .Select(item => new SemanticSearchResult(
                ProductName:   item.ProductName,
                OrderNumber:   item.OrderNumber,
                CustomerName:  item.CustomerName,
                ProductGroup:  item.ProductGroup,
                Price:         item.Price,
                Score:         CosineSimilarity(queryVector, item.Vector)
            ))
            .OrderByDescending(r => r.Score)
            .Take(topN);
    }

    // LEARNING NOTE — Cosine Similarity
    // Measures how similar two vectors are.
    // Score = 1.0  → identical meaning
    // Score = 0.0  → completely unrelated
    // Score = -1.0 → opposite meaning
    //
    // The formula: dot product of A and B, divided by (length of A × length of B)
    // Don't worry about memorising this — just know what it measures.
    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot  = a.Zip(b, (x, y) => x * y).Sum();
        var magA = MathF.Sqrt(a.Sum(x => x * x));
        var magB = MathF.Sqrt(b.Sum(x => x * x));
        return dot / (magA * magB);
    }

    // Private record — just to hold one indexed item cleanly
    private record IndexedItem(
        string  ProductName,
        string  OrderNumber,
        string  CustomerName,
        string  ProductGroup,
        decimal Price,
        float[] Vector
    );
}
