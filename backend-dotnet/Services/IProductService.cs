using OrderApi.Models;

namespace OrderApi.Services;

/// <summary>
/// Abstracts product data operations — same pattern as IOrderRepository.
/// Program.cs depends on this interface, not on any concrete implementation.
/// </summary>
public interface IProductService
{
    Product? GetById(int id);
    IEnumerable<Product> GetAll();
    void Add(Product product);
}
