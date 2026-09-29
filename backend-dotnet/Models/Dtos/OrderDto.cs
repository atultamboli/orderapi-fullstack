namespace OrderApi.Models.Dtos;

/// <summary>Full representation of an order, including all of its lines —
/// returned for GET /api/orders/{orderNumber}.</summary>
public record OrderDto(
    string OrderNumber,
    DateOnly OrderDate,
    string CustomerNumber,
    string CustomerName,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto FromOrder(Order order) => new(
        order.OrderNumber,
        order.OrderDate,
        order.CustomerNumber,
        order.CustomerName,
        order.Lines.Select(OrderLineDto.FromOrderLine).ToList());
}
