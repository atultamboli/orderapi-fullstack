using Microsoft.AspNetCore.Mvc;
using OrderApi.Models;
using OrderApi.Models.Dtos;
using OrderApi.Services;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _repository;

    public OrdersController(IOrderRepository repository)
    {
        _repository = repository;
    }

    // ── Reads ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/orders — browse all orders as lightweight summaries.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<OrderSummaryDto>> GetAll()
    {
        var summaries = _repository.GetAll().Select(OrderSummaryDto.FromOrder);
        return Ok(summaries);
    }

    /// <summary>GET /api/orders/{orderNumber} — full order with line detail.</summary>
    [HttpGet("{orderNumber}")]
    public ActionResult<OrderDto> GetByOrderNumber(string orderNumber)
    {
        var order = _repository.GetByOrderNumber(orderNumber);
        return order is null
            ? NotFound(new { message = $"Order '{orderNumber}' was not found." })
            : Ok(OrderDto.FromOrder(order));
    }

    // ── Analytics (MongoDB aggregation pipeline) ──────────────────────────

    /// <summary>GET /api/orders/analytics/spend-by-customer
    /// — total spend per customer, highest first.
    /// Uses a MongoDB aggregation pipeline (or in-memory LINQ for CSV mode).</summary>
    [HttpGet("analytics/spend-by-customer")]
    public async Task<ActionResult<IEnumerable<CustomerSpendDto>>> GetSpendByCustomer(
        CancellationToken ct)
    {
        var result = await _repository.GetSpendByCustomerAsync(ct);
        return Ok(result);
    }

    // ── Writes (MongoDB only — CSV repo returns 501 Not Implemented) ──────

    /// <summary>PATCH /api/orders/{orderNumber}/customer-name
    /// — update a customer name without replacing the whole document.
    /// Demonstrates MongoDB's $set partial-update operator.</summary>
    [HttpPatch("{orderNumber}/customer-name")]
    public async Task<IActionResult> UpdateCustomerName(
        string orderNumber, [FromBody] string newName, CancellationToken ct)
    {
        try
        {
            var updated = await _repository.UpdateCustomerNameAsync(orderNumber, newName, ct);
            return updated
                ? NoContent()
                : NotFound(new { message = $"Order '{orderNumber}' was not found." });
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    /// <summary>DELETE /api/orders/{orderNumber}
    /// — remove an order document from MongoDB.</summary>
    [HttpDelete("{orderNumber}")]
    public async Task<IActionResult> Delete(string orderNumber, CancellationToken ct)
    {
        try
        {
            var deleted = await _repository.DeleteAsync(orderNumber, ct);
            return deleted
                ? NoContent()
                : NotFound(new { message = $"Order '{orderNumber}' was not found." });
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }

    // ── ETL Upload ────────────────────────────────────────────────────────

    /// <summary>POST /api/orders/upload
    /// — upload a pipe-delimited .txt file and run the full ETL pipeline.
    ///
    /// The pipeline: Extract rows from file → Transform (collapse flat rows
    /// into Order + Lines hierarchy) → Load into MongoDB (skips duplicates).
    ///
    /// Returns an EtlResultDto showing exactly what was inserted, skipped,
    /// and errored — so the caller has full visibility into what happened.</summary>
    [HttpPost("upload")]
    public async Task<ActionResult<EtlResultDto>> Upload(
        IFormFile file,
        [FromServices] OrderEtlService etlService,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .txt order files are accepted." });

        try
        {
            var result = await etlService.RunAsync(file, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}
