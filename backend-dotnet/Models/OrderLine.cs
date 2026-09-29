namespace OrderApi.Models;

/// <summary>
/// A single line item within an <see cref="Order"/>.
///
/// ProductNumber is intentionally a string, never parsed as numeric —
/// the source data mixes formats across orders (e.g. "123451324A",
/// "4000-AAA", "A145"), so it's an opaque identifier, not a number.
///
/// Description is nullable: plain, undecorated products (e.g. a single
/// red 2x2 brick) legitimately have no description in the source data —
/// that's valid data, not a parsing error.
/// </summary>
public class OrderLine
{
    public string OrderLineNumber { get; }
    public string ProductNumber { get; }
    public string Name { get; }
    public string? Description { get; }
    public int Quantity { get; }
    public decimal Price { get; }
    public string ProductGroup { get; }

    public OrderLine(
        string orderLineNumber,
        string productNumber,
        string name,
        string? description,
        int quantity,
        decimal price,
        string productGroup)
    {
        OrderLineNumber = orderLineNumber;
        ProductNumber = productNumber;
        Name = name;
        Description = description;
        Quantity = quantity;
        Price = price;
        ProductGroup = productGroup;
    }

    /// <summary>Quantity x Price, computed rather than stored — it should
    /// never drift out of sync with its inputs.</summary>
    public decimal LineTotal => Price * Quantity;
}
