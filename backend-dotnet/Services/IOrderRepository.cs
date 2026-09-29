using OrderApi.Models;
using OrderApi.Models.Dtos;

namespace OrderApi.Services;

/// <summary>
/// Abstracts away where order data comes from. Controllers depend only on
/// this interface — the storage mechanism can change without touching the
/// API layer (Dependency Inversion Principle).
/// </summary>
public interface IOrderRepository
{
    // ── Reads ────────────────────────────────────────────────────────────────
    IEnumerable<Order> GetAll();

    /// <returns>The matching order, or null if no order has this number.</returns>
    Order? GetByOrderNumber(string orderNumber);

    // ── Writes (MongoDB implementation uses these; CSV impl throws NotSupported) ──
    Task CreateAsync(Order order, CancellationToken ct = default);
    Task<bool> UpdateCustomerNameAsync(string orderNumber, string newName, CancellationToken ct = default);
    Task<bool> DeleteAsync(string orderNumber, CancellationToken ct = default);

    // ── Analytics ────────────────────────────────────────────────────────────
    /// <summary>Total spend per customer, sorted descending. MongoDB uses an
    /// aggregation pipeline; the CSV impl does it in-memory with LINQ.</summary>
    Task<IEnumerable<CustomerSpendDto>> GetSpendByCustomerAsync(CancellationToken ct = default);
}
