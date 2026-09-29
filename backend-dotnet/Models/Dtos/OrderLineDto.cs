namespace OrderApi.Models.Dtos;

public record OrderLineDto(
    string OrderLineNumber,
    string ProductNumber,
    string Name,
    string? Description,
    int Quantity,
    decimal Price,
    string ProductGroup,
    decimal LineTotal)
{
    public static OrderLineDto FromOrderLine(OrderLine line) => new(
        line.OrderLineNumber,
        line.ProductNumber,
        line.Name,
        line.Description,
        line.Quantity,
        line.Price,
        line.ProductGroup,
        line.LineTotal);
}
