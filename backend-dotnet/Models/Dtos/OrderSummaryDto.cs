namespace OrderApi.Models.Dtos;

/// <summary>
/// Lightweight projection of an <see cref="Order"/> for list views.
/// Deliberately omits line-item detail — a consumer browsing orders
/// doesn't need every line just to see what's there.
/// </summary>
public record OrderSummaryDto(
    string OrderNumber,
    DateOnly OrderDate,
    string CustomerNumber,
    string CustomerName,
    int LineCount,
    decimal OrderTotal)
{
    public static OrderSummaryDto FromOrder(Order order) => new(
        order.OrderNumber,
        order.OrderDate,
        order.CustomerNumber,
        order.CustomerName,
        order.Lines.Count,
        order.Lines.Sum(l => l.LineTotal));
}
