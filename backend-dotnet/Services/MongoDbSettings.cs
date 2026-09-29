namespace OrderApi.Services;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "OrderDb";
    public string CollectionName { get; set; } = "orders";
}
