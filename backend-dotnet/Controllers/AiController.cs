using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using OrderApi.Models.Dtos;
using OrderApi.Services;

// LEARNING NOTE — three services in one controller now
// DI handles as many constructor parameters as you need.
// Each one is registered in Program.cs and supplied automatically.

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("ai")] // caps AI calls per hour — see Program.cs
public class AiController : ControllerBase
{
    private readonly ICrmAiService _aiService;
    private readonly IOrderRepository _orders;
    private readonly IMemoryCache _cache;
    private readonly ISemanticSearchService _search;  // NEW

    public AiController(
        ICrmAiService aiService,
        IOrderRepository orders,
        IMemoryCache cache,
        ISemanticSearchService search)
    {
        _aiService = aiService;
        _orders    = orders;
        _cache     = cache;
        _search    = search;
    }

    // ── Existing endpoints (unchanged) ───────────────────────────────────────

    [HttpPost("summarise-case")]
    public async Task<IActionResult> SummariseCase([FromBody] CaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CaseDetails))
            return BadRequest("Case details required.");

        var summary = await _aiService.SummariseCaseAsync(request.CaseDetails);
        return Ok(new { summary });
    }

    [HttpGet("anything")]
    public IActionResult Anything(string orderNumber) =>
        Ok("Anything works " + orderNumber);

    [HttpPost("extract-ticket")]
    public async Task<IActionResult> ExtractTicket([FromBody] TicketRequest request)
    {
        var data = await _aiService.ExtractTicketDataAsync(request.TicketText);
        return Ok(new { data });
    }

    [HttpPost("stream-summarise")]
    public async Task StreamSummarise([FromBody] CaseRequest request)
    {
        Response.ContentType = "text/plain";
        await foreach (var token in _aiService.StreamSummariseCaseAsync(request.CaseDetails))
        {
            await Response.WriteAsync(token);
            await Response.Body.FlushAsync();
        }
    }

    // ── AI endpoints that work with real order data ───────────────────────────

    // LEARNING NOTE — CACHING PATTERN
    // The pattern is always the same three steps:
    //   1. Build a cache key — a unique string that identifies THIS result
    //   2. Check the cache — if found, return immediately (no API call)
    //   3. If not found — call the API, store the result, then return it
    //
    // Cache key for summarise-order: "summary:17890"
    // If someone asks for order 17890 again within 1 hour → instant response, $0 cost
    // After 1 hour → cache expires → next request calls OpenAI again (fresh data)
    [HttpPost("summarise-order/{orderNumber}")]
    public async Task<IActionResult> SummariseOrder(string orderNumber)
    {
        var order = _orders.GetByOrderNumber(orderNumber);
        if (order is null)
            return NotFound(new { message = $"Order '{orderNumber}' not found." });

        // Step 1 — build a unique cache key for this specific order's summary
        var cacheKey = $"summary:{orderNumber}";

        // Step 2 — check the cache first
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return Ok(new { orderNumber, summary = cached, source = "cache" });

        // Step 3 — not in cache, call OpenAI
        // LEARNING NOTE — ERROR HANDLING
        // What if OpenAI is down? Or your API key is wrong? Without try/catch,
        // the exception bubbles up and your user gets a raw 500 error with a
        // stack trace. With try/catch you return a clean, readable error message.
        try
        {
            var context = FormatOrderAsText(order);
            var summary = await _aiService.SummariseCaseAsync(context);

            // Store in cache for 1 hour — same order won't hit OpenAI again
            _cache.Set(cacheKey, summary, TimeSpan.FromHours(1));

            return Ok(new { orderNumber, summary, source = "openai" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                message = "AI service is temporarily unavailable.",
                detail  = ex.Message
            });
        }
    }

    [HttpPost("analyse-order/{orderNumber}")]
    public async Task<IActionResult> AnalyseOrder(string orderNumber)
    {
        var order = _orders.GetByOrderNumber(orderNumber);
        if (order is null)
            return NotFound(new { message = $"Order '{orderNumber}' not found." });

        var cacheKey = $"analysis:{orderNumber}";

        // LEARNING NOTE — cache stores the typed object now, not a raw string
        // TryGetValue<OrderAnalysisDto> retrieves it already deserialized.
        // No need to deserialize again on cache hit — it's already a C# object.
        if (_cache.TryGetValue(cacheKey, out OrderAnalysisDto? cached))
            return Ok(new { orderNumber, analysis = cached, source = "cache" });

        try
        {
            var context  = FormatOrderAsText(order);

            // AnalyseOrderAsync now returns OrderAnalysisDto, not a raw string.
            // The deserialization happens inside the service — controller stays clean.
            var analysis = await _aiService.AnalyseOrderAsync(context);

            _cache.Set(cacheKey, analysis, TimeSpan.FromHours(1));

            return Ok(new { orderNumber, analysis, source = "openai" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                message = "AI service is temporarily unavailable. Check that the response was valid JSON.",
                detail  = ex.Message
            });
        }
    }

    // POST /api/ai/search
    // Body: { "query": "space toys" }
    //
    // LEARNING NOTE — lazy indexing
    // We index on the first search request, not at startup.
    // IsIndexed is a property on the singleton service — so it stays true
    // across all future requests. The index is only built once.
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { message = "Query is required." });

        try
        {
            // Index all order items on first search only
            if (!_search.IsIndexed)
                await _search.IndexOrdersAsync(_orders.GetAll());

            var results = await _search.SearchAsync(request.Query, topN: 3);
            return Ok(new { query = request.Query, results });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                message = "Search service unavailable.",
                detail  = ex.Message
            });
        }
    }

    // POST /api/ai/ask
    // Body: { "question": "What's the total value of order 17890?" }
    //
    // LEARNING NOTE — this endpoint doesn't know what order the user is asking about.
    // It just passes the question to GPT. GPT reads it, decides it needs order data,
    // calls get_order_summary("17890"), gets the data, then answers.
    // The AI is doing the routing — not your code.
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Question is required." });

        try
        {
            var answer = await _aiService.AskAsync(request.Question, _orders);
            return Ok(new { question = request.Question, answer });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                message = "AI service is temporarily unavailable.",
                detail  = ex.Message
            });
        }
    }

    private static string FormatOrderAsText(OrderApi.Models.Order order)
    {
        var totalValue       = order.Lines.Sum(l => l.Price * l.Quantity);
        var lineDescriptions = order.Lines.Select(l =>
            $"- {l.Name} (qty: {l.Quantity}, price: {l.Price:C}, group: {l.ProductGroup})");

        return $"""
            Order Number: {order.OrderNumber}
            Order Date: {order.OrderDate}
            Customer: {order.CustomerName} (#{order.CustomerNumber})
            Total Lines: {order.Lines.Count}
            Total Value: {totalValue:C}

            Items:
            {string.Join("\n", lineDescriptions)}
            """;
    }
}

public record CaseRequest(string CaseDetails);
public record TicketRequest(string TicketText);
public record AskOrderRequest(string Question);
public record SearchRequest(string Query);
