namespace OrderApi.Models;

/// <summary>
/// A customer order, made up of one or more <see cref="OrderLine"/> items.
///
/// The source data is a flat, denormalized export — every row repeats the
/// OrderNumber/OrderDate/CustomerNumber/CustomerName "header" fields. This
/// class represents the order AFTER that repetition has been collapsed
/// back into a proper header/line structure.
/// </summary>
public class Order
{
    public string OrderNumber { get; }
    public DateOnly OrderDate { get; }
    public string CustomerNumber { get; }
    public string CustomerName { get; }

    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines;

    public Order(string orderNumber, DateOnly orderDate, string customerNumber, string customerName)
    {
        OrderNumber = orderNumber;
        OrderDate = orderDate;
        CustomerNumber = customerNumber;
        CustomerName = customerName;
    }

    public void AddLine(OrderLine line) => _lines.Add(line);
}
