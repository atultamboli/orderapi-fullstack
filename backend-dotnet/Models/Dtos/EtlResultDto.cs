namespace OrderApi.Models.Dtos;

/// <summary>
/// Returned by POST /api/orders/upload after the ETL pipeline completes.
/// Tells the caller exactly what happened: what was inserted, what was
/// skipped (already existed), and which rows had errors.
/// </summary>
public record EtlResultDto(
    string FileName,
    int    OrdersInserted,
    int    OrdersSkipped,    // duplicate _id — already in MongoDB
    int    RowParseErrors,   // rows that couldn't be parsed from the file
    IReadOnlyList<string> ErrorDetails);
