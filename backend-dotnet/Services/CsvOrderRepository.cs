using System.Globalization;
using OrderApi.Models;
using OrderApi.Models.Dtos;

namespace OrderApi.Services;

/// <summary>
/// Loads orders from the pipe-delimited export files supplied with this
/// exercise. No documentation was provided for the format, so the notes
/// below capture what was reverse-engineered from the sample files.
///
/// Format notes:
///   - Each row is wrapped in pipes, markdown-table style, e.g.
///     "|17890|0001|...|". A plain Split('|') produces an empty string
///     at each end of the array; <see cref="SplitRow"/> trims those off.
///   - Each file represents one order. Every row in the file repeats the
///     same OrderNumber / OrderDate / CustomerNumber / CustomerName
///     ("header" fields), while OrderLineNumber / ProductNumber /
///     Quantity / Name / Description / Price / ProductGroup vary per
///     row. We rebuild the Order -> OrderLine hierarchy from that
///     repetition rather than trusting the file/order relationship,
///     since the file name is not guaranteed to map to a single order.
///   - Description can legitimately be empty ("||" with nothing between)
///     for plain products — that's valid data, not a parsing error.
///   - ProductNumber has no consistent format across orders
///     ("123451324A", "4000-AAA", "A145"), so it's always treated as an
///     opaque string.
///
/// Data-quality stance: a malformed row (wrong column count, or a value
/// that won't parse as the expected type) is logged and skipped rather
/// than crashing the whole load. One bad row in a 10,000-line file
/// shouldn't take down the API.
/// </summary>
public class CsvOrderRepository : IOrderRepository
{
    private const int ExpectedColumnCount = 11;

    private readonly Dictionary<string, Order> _ordersByNumber;
    private readonly ILogger<CsvOrderRepository> _logger;

    public CsvOrderRepository(string dataFolderPath, ILogger<CsvOrderRepository> logger)
    {
        _logger = logger;
        _ordersByNumber = LoadOrders(dataFolderPath);
    }

    public IEnumerable<Order> GetAll() => _ordersByNumber.Values;

    public Order? GetByOrderNumber(string orderNumber) =>
        _ordersByNumber.TryGetValue(orderNumber, out var order) ? order : null;

    // The CSV store is read-only — writes and aggregation run in-memory as a fallback.
    public Task CreateAsync(Order order, CancellationToken ct = default) =>
        throw new NotSupportedException("CsvOrderRepository is read-only.");

    public Task<bool> UpdateCustomerNameAsync(string orderNumber, string newName, CancellationToken ct = default) =>
        throw new NotSupportedException("CsvOrderRepository is read-only.");

    public Task<bool> DeleteAsync(string orderNumber, CancellationToken ct = default) =>
        throw new NotSupportedException("CsvOrderRepository is read-only.");

    public Task<IEnumerable<CustomerSpendDto>> GetSpendByCustomerAsync(CancellationToken ct = default)
    {
        // In-memory aggregation — same logic as the MongoDB pipeline, but in LINQ.
        var result = _ordersByNumber.Values
            .GroupBy(o => o.CustomerNumber)
            .Select(g => new CustomerSpendDto(
                CustomerNumber: g.Key,
                CustomerName:   g.First().CustomerName,
                OrderCount:     g.Count(),
                TotalSpend:     g.SelectMany(o => o.Lines).Sum(l => l.LineTotal)))
            .OrderByDescending(r => r.TotalSpend);

        return Task.FromResult<IEnumerable<CustomerSpendDto>>(result);
    }

    private Dictionary<string, Order> LoadOrders(string dataFolderPath)
    {
        var orders = new Dictionary<string, Order>();

        if (!Directory.Exists(dataFolderPath))
        {
            _logger.LogWarning("Data folder '{Folder}' does not exist. Starting with no orders.", dataFolderPath);
            return orders;
        }

        // Pick up any number of files dropped into the folder, rather than
        // hardcoding Order1.txt/Order2.txt/Order3.txt — new orders arrive
        // as new files, not as edits to a fixed list in code.
        var files = Directory.GetFiles(dataFolderPath, "*.txt");

        foreach (var file in files)
        {
            ParseFile(file, orders);
        }

        _logger.LogInformation("Loaded {Count} orders from {FileCount} file(s).", orders.Count, files.Length);
        return orders;
    }

    private void ParseFile(string filePath, Dictionary<string, Order> orders)
    {
        var lines = File.ReadAllLines(filePath);

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var columns = SplitRow(rawLine);

            // Skip the header row by content (column 0 == "OrderNumber"),
            // not by line number — a stray or missing header shouldn't
            // break parsing.
            if (columns.Length > 0 && columns[0] == "OrderNumber")
            {
                continue;
            }

            if (columns.Length != ExpectedColumnCount)
            {
                _logger.LogWarning(
                    "Skipping malformed row in {File}: expected {Expected} columns, found {Actual}. Row: \"{Row}\"",
                    Path.GetFileName(filePath), ExpectedColumnCount, columns.Length, rawLine);
                continue;
            }

            if (!TryParseRow(columns, out var orderNumber, out var newOrderHeader, out var line))
            {
                _logger.LogWarning("Skipping unparseable row in {File}: \"{Row}\"", Path.GetFileName(filePath), rawLine);
                continue;
            }

            if (!orders.TryGetValue(orderNumber, out var order))
            {
                order = newOrderHeader;
                orders[orderNumber] = order;
            }

            order.AddLine(line);
        }
    }

    /// <summary>Splits a "|a|b|c|"-style row into its column values,
    /// discarding the empty strings produced by the leading/trailing pipe.</summary>
    private static string[] SplitRow(string rawLine)
    {
        var parts = rawLine.Split('|');
        return parts.Skip(1).Take(parts.Length - 2).ToArray();
    }

    private bool TryParseRow(string[] c, out string orderNumber, out Order orderHeader, out OrderLine line)
    {
        orderNumber = c[0].Trim();
        var orderLineNumber = c[1].Trim();
        var productNumber = c[2].Trim();
        var quantityRaw = c[3].Trim();
        var name = c[4].Trim();
        var description = c[5].Trim();
        var priceRaw = c[6].Trim();
        var productGroup = c[7].Trim();
        var orderDateRaw = c[8].Trim();
        var customerName = c[9].Trim();
        var customerNumber = c[10].Trim();

        orderHeader = null!;
        line = null!;

        if (string.IsNullOrEmpty(orderNumber))
        {
            return false;
        }

        if (!int.TryParse(quantityRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
        {
            return false;
        }

        if (!decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(orderDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var orderDate))
        {
            return false;
        }

        // Note: OrderNumber/CustomerNumber are kept as strings, not parsed
        // as int. They're identifiers (the source data even zero-pads
        // OrderLineNumber, e.g. "0001"), not quantities — there's no
        // arithmetic to do on them, and parsing would silently lose
        // leading zeros if any order ever has them.
        orderHeader = new Order(orderNumber, orderDate, customerNumber, customerName);
        line = new OrderLine(
            orderLineNumber,
            productNumber,
            name,
            string.IsNullOrEmpty(description) ? null : description,
            quantity,
            price,
            productGroup);

        return true;
    }
}
