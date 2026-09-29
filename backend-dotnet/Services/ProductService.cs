using OrderApi.Models;

namespace OrderApi.Services;

/// <summary>
/// In-memory product store — same idea as CsvOrderRepository but simpler.
/// Data lives only while the app is running (no file or database yet).
///
/// LEARNING NOTE — why AddScoped in Program.cs?
///   AddScoped = "create one instance per HTTP request, share it within that request"
///   This is safe here because _products is a shared list owned by the singleton
///   backing store — ProductService just wraps it. In a real app you'd use a database.
/// </summary>
public class ProductService : IProductService
{
    // Static so all scoped instances in the same process share the same list.
    // Fine for learning; in production you'd use a database instead.
    private static readonly List<Product> _products = new();
    private static int _nextId = 1;

    public Product? GetById(int id) =>
        _products.FirstOrDefault(p => p.Id == id);

    public IEnumerable<Product> GetAll() => _products.AsReadOnly();

    public void Add(Product product)
    {
        product.Id = _nextId++;
        _products.Add(product);
    }
}
