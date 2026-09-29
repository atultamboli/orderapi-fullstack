using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderApi.Models;
using OrderApi.Models.Dtos;

namespace OrderApi.Services;

/// <summary>
/// MongoDB implementation of IOrderRepository.
///
/// Patterns used:
///   - Embedded documents: OrderLines live inside the Order document (no JOINs).
///   - _id = OrderNumber string: O(1) point lookups via the primary index.
///   - Async throughout: never blocks the thread pool under load.
///   - Aggregation pipeline for analytics (GetSpendByCustomerAsync).
///   - Indexes created once at startup in EnsureIndexes().
/// </summary>
public class MongoOrderRepository : IOrderRepository
{
    // -----------------------------------------------------------------------
    // Private nested POCOs — BSON shape stays inside this class
    // -----------------------------------------------------------------------

    private class OrderDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = default!;

        [BsonElement("orderDate")]
        public string OrderDate { get; set; } = default!;      // yyyy-MM-dd

        [BsonElement("customerNumber")]
        public string CustomerNumber { get; set; } = default!;

        [BsonElement("customerName")]
        public string CustomerName { get; set; } = default!;

        [BsonElement("lines")]
        public List<OrderLineDocument> Lines { get; set; } = new();
    }

    private class OrderLineDocument
    {
        [BsonElement("orderLineNumber")]
        public string OrderLineNumber { get; set; } = default!;

        [BsonElement("productNumber")]
        public string ProductNumber { get; set; } = default!;

        [BsonElement("name")]
        public string Name { get; set; } = default!;

        [BsonElement("description")]
        public string? Description { get; set; }

        [BsonElement("quantity")]
        public int Quantity { get; set; }

        [BsonElement("price")]
        public decimal Price { get; set; }

        [BsonElement("productGroup")]
        public string ProductGroup { get; set; } = default!;
    }

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly IMongoCollection<OrderDocument> _collection;

    // -----------------------------------------------------------------------
    // Constructor — connect, create indexes once
    // -----------------------------------------------------------------------

    public MongoOrderRepository(MongoDbSettings settings)
    {
        // MongoClient manages a connection pool — it is registered as a
        // singleton so we don't recreate it here. The collection reference
        // is cheap to hold.
        var client = new MongoClient(settings.ConnectionString);
        var db = client.GetDatabase(settings.DatabaseName);
        _collection = db.GetCollection<OrderDocument>(settings.CollectionName);

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var ib = Builders<OrderDocument>.IndexKeys;

        // Compound index: common query — "all orders for customer X, newest first"
        _collection.Indexes.CreateOne(new CreateIndexModel<OrderDocument>(
            ib.Ascending(o => o.CustomerNumber)
              .Descending(o => o.OrderDate),
            new CreateIndexOptions { Name = "ix_customer_date", Background = true }));

        // Index on the nested array — "find orders containing product Y"
        _collection.Indexes.CreateOne(new CreateIndexModel<OrderDocument>(
            ib.Ascending("lines.productNumber"),
            new CreateIndexOptions { Name = "ix_product", Background = true }));
    }

    // -----------------------------------------------------------------------
    // Reads (sync — keeps backward-compat with IOrderRepository callers)
    // -----------------------------------------------------------------------

    public IEnumerable<Order> GetAll()
    {
        var docs = _collection.Find(FilterDefinition<OrderDocument>.Empty).ToList();
        return docs.Select(ToOrder);
    }

    public Order? GetByOrderNumber(string orderNumber)
    {
        var doc = _collection.Find(d => d.Id == orderNumber).FirstOrDefault();
        return doc is null ? null : ToOrder(doc);
    }

    // -----------------------------------------------------------------------
    // Writes (async — use these from controllers for real throughput)
    // -----------------------------------------------------------------------

    public async Task CreateAsync(Order order, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(ToDocument(order), cancellationToken: ct);
    }

    public async Task<bool> UpdateCustomerNameAsync(
        string orderNumber, string newName, CancellationToken ct = default)
    {
        // UpdateOneAsync patches ONLY the customerName field — no full replace.
        // This is how orgs do partial updates: touch only what changed.
        var result = await _collection.UpdateOneAsync(
            filter: o => o.Id == orderNumber,
            update: Builders<OrderDocument>.Update.Set(o => o.CustomerName, newName),
            cancellationToken: ct);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string orderNumber, CancellationToken ct = default)
    {
        var result = await _collection.DeleteOneAsync(
            o => o.Id == orderNumber, cancellationToken: ct);

        return result.DeletedCount > 0;
    }

    // -----------------------------------------------------------------------
    // Analytics — Aggregation Pipeline
    // -----------------------------------------------------------------------

    public async Task<IEnumerable<CustomerSpendDto>> GetSpendByCustomerAsync(
        CancellationToken ct = default)
    {
        // Pipeline:
        //   Stage 1 ($unwind)  — explode the "lines" array: one doc per line item
        //   Stage 2 ($group)   — group by customerNumber, SUM(price * quantity)
        //   Stage 3 ($sort)    — highest spender first
        //
        // This runs entirely in MongoDB — no data transferred until the final
        // result set, which is exactly one small row per customer.

        var pipeline = new[]
        {
            new BsonDocument("$unwind", "$lines"),

            new BsonDocument("$group", new BsonDocument
            {
                { "_id",          "$customerNumber" },
                { "customerName", new BsonDocument("$first", "$customerName") },
                { "orderCount",   new BsonDocument("$sum",   1) },
                { "totalSpend",   new BsonDocument("$sum",
                    new BsonDocument("$multiply",
                        new BsonArray { "$lines.price", "$lines.quantity" })) }
            }),

            new BsonDocument("$sort", new BsonDocument("totalSpend", -1))
        };

        var stageDefs = pipeline
            .Select(b => new BsonDocumentPipelineStageDefinition<OrderDocument, BsonDocument>(b))
            .ToList<IPipelineStageDefinition>();

        var pipelineDef = new BsonDocumentStagePipelineDefinition<OrderDocument, BsonDocument>(pipeline);

        var results = await _collection
            .Aggregate(pipelineDef)
            .ToListAsync(ct);

        return results.Select(b => new CustomerSpendDto(
            CustomerNumber: b["_id"].AsString,
            CustomerName:   b["customerName"].AsString,
            OrderCount:     b["orderCount"].AsInt32,
            TotalSpend:     b["totalSpend"].ToDecimal()));
    }

    // -----------------------------------------------------------------------
    // Seeding helpers (called by OrderSeeder at startup)
    // -----------------------------------------------------------------------

    internal void InsertMany(IEnumerable<Order> orders) =>
        _collection.InsertMany(orders.Select(ToDocument));

    internal bool HasData() =>
        _collection.CountDocuments(FilterDefinition<OrderDocument>.Empty) > 0;

    // -----------------------------------------------------------------------
    // Mapping helpers — domain model ↔ MongoDB document
    // -----------------------------------------------------------------------

    private static Order ToOrder(OrderDocument doc)
    {
        var order = new Order(
            doc.Id,
            DateOnly.ParseExact(doc.OrderDate, "yyyy-MM-dd"),
            doc.CustomerNumber,
            doc.CustomerName);

        foreach (var l in doc.Lines)
            order.AddLine(new OrderLine(
                l.OrderLineNumber, l.ProductNumber, l.Name,
                l.Description, l.Quantity, l.Price, l.ProductGroup));

        return order;
    }

    private static OrderDocument ToDocument(Order order) => new()
    {
        Id             = order.OrderNumber,
        OrderDate      = order.OrderDate.ToString("yyyy-MM-dd"),
        CustomerNumber = order.CustomerNumber,
        CustomerName   = order.CustomerName,
        Lines = order.Lines.Select(l => new OrderLineDocument
        {
            OrderLineNumber = l.OrderLineNumber,
            ProductNumber   = l.ProductNumber,
            Name            = l.Name,
            Description     = l.Description,
            Quantity        = l.Quantity,
            Price           = l.Price,
            ProductGroup    = l.ProductGroup,
        }).ToList()
    };
}
