using System.Globalization;
using MongoDB.Driver;
using OrderApi.Models;
using OrderApi.Models.Dtos;

namespace OrderApi.Services;

/// <summary>
/// Orchestrates the full ETL pipeline for an uploaded order file.
///
///   Extract   — reads the uploaded .txt stream, parses pipe-delimited rows
///   Transform — collapses flat/repeated rows into Order + OrderLine hierarchy
///   Load      — inserts into MongoDB; skips duplicates instead of crashing
///   Report    — returns EtlResultDto so the caller knows exactly what happened
///
/// This is the same data shape your files already use, so any new .txt file
/// following the same format can be uploaded and loaded without touching code.
/// </summary>
public class OrderEtlService
{
    private const int ExpectedColumnCount = 11;

    private readonly IOrderRepository _repo;
    private readonly ILogger<OrderEtlService> _logger;

    public OrderEtlService(IOrderRepository repo, ILogger<OrderEtlService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<EtlResultDto> RunAsync(IFormFile file, CancellationToken ct)
    {
        // ── EXTRACT + TRANSFORM ───────────────────────────────────────────
        // Parse the uploaded stream into Order objects, collecting any row
        // errors rather than aborting the whole pipeline on one bad row.
        var (orders, parseErrors) = ExtractAndTransform(file);

        _logger.LogInformation(
            "[ETL] Extract+Transform complete for '{File}': {Orders} orders, {Errors} parse error(s).",
            file.FileName, orders.Count, parseErrors.Count);

        // ── LOAD ──────────────────────────────────────────────────────────
        int inserted = 0;
        int skipped  = 0;
        var loadErrors = new List<string>();

        foreach (var order in orders)
        {
            try
            {
                await _repo.CreateAsync(order, ct);
                inserted++;
            }
            catch (MongoWriteException ex)
                when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Order already exists — skip silently, don't overwrite existing data.
                skipped++;
                _logger.LogInformation("[ETL] Order '{Id}' already in MongoDB — skipped.", order.OrderNumber);
            }
            catch (NotSupportedException ex)
            {
                // Repository is read-only (CSV mode) — fail fast.
                throw new InvalidOperationException(
                    "ETL upload requires MongoDB mode. Set Storage:UseMongoDb=true in appsettings.json.", ex);
            }
            catch (Exception ex)
            {
                loadErrors.Add($"Order {order.OrderNumber}: {ex.Message}");
                _logger.LogWarning("[ETL] Failed to insert order '{Id}': {Err}", order.OrderNumber, ex.Message);
            }
        }

        var allErrors = parseErrors.Concat(loadErrors).ToList();

        _logger.LogInformation(
            "[ETL] Load complete for '{File}': {Ins} inserted, {Skip} skipped, {Err} error(s).",
            file.FileName, inserted, skipped, allErrors.Count);

        return new EtlResultDto(file.FileName, inserted, skipped, parseErrors.Count, allErrors);
    }

    // ── EXTRACT + TRANSFORM ───────────────────────────────────────────────
    // Reads the file stream line by line. Collapses repeated order header
    // fields (OrderNumber/Date/Customer) into one Order with many lines.
    // Bad rows are collected into `errors` instead of crashing the pipeline.

    private (List<Order> orders, List<string> errors) ExtractAndTransform(IFormFile file)
    {
        var orders = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        int lineNumber = 0;

        using var reader = new StreamReader(file.OpenReadStream());
        string? rawLine;

        while ((rawLine = reader.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var columns = SplitRow(rawLine);

            // Skip header row by content, not by line number
            if (columns.Length > 0 && columns[0] == "OrderNumber")
                continue;

            if (columns.Length != ExpectedColumnCount)
            {
                errors.Add($"Line {lineNumber}: expected {ExpectedColumnCount} columns, found {columns.Length}.");
                continue;
            }

            if (!TryParseRow(columns, out var orderNumber, out var orderHeader, out var line))
            {
                errors.Add($"Line {lineNumber}: could not parse values (bad date, quantity, or price).");
                continue;
            }

            // First time we see this OrderNumber → create the Order header
            // Subsequent rows for the same OrderNumber → just add the line
            if (!orders.TryGetValue(orderNumber, out var order))
            {
                order = orderHeader;
                orders[orderNumber] = order;
            }

            order.AddLine(line);
        }

        return (orders.Values.ToList(), errors);
    }

    // Strips the leading and trailing empty strings produced by "|a|b|c|".Split('|')
    private static string[] SplitRow(string rawLine)
    {
        var parts = rawLine.Split('|');
        return parts.Skip(1).Take(parts.Length - 2).ToArray();
    }

    private static bool TryParseRow(
        string[] c,
        out string orderNumber,
        out Order orderHeader,
        out OrderLine line)
    {
        orderNumber = c[0].Trim();
        var orderLineNumber = c[1].Trim();
        var productNumber   = c[2].Trim();
        var quantityRaw     = c[3].Trim();
        var name            = c[4].Trim();
        var description     = c[5].Trim();
        var priceRaw        = c[6].Trim();
        var productGroup    = c[7].Trim();
        var orderDateRaw    = c[8].Trim();
        var customerName    = c[9].Trim();
        var customerNumber  = c[10].Trim();

        orderHeader = null!;
        line        = null!;

        if (string.IsNullOrEmpty(orderNumber)) return false;

        if (!int.TryParse(quantityRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
            return false;

        if (!decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            return false;

        if (!DateOnly.TryParseExact(orderDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var orderDate))
            return false;

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
