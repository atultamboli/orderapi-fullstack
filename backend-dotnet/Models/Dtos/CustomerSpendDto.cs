namespace OrderApi.Models.Dtos;

/// <summary>Result of the aggregation pipeline — total spend per customer.</summary>
public record CustomerSpendDto(
    string CustomerNumber,
    string CustomerName,
    int    OrderCount,
    decimal TotalSpend);
