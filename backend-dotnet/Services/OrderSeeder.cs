namespace OrderApi.Services;

/// <summary>
/// Seeds the MongoDB collection from the CSV flat files on first startup.
///
/// Why a seeder instead of always reading from CSV?
/// - In production you'd import data once (or via an ETL job), not on every restart.
/// - This lets you later add orders directly to MongoDB (e.g. via a POST endpoint)
///   without worrying about the flat files.
/// - The idempotency check (HasData) means restarting the app never duplicates data.
/// </summary>
public static class OrderSeeder
{
    public static void SeedIfEmpty(
        MongoOrderRepository mongoRepo,
        IOrderRepository csvRepo,
        ILogger logger)
    {
        if (mongoRepo.HasData())
        {
            logger.LogInformation("MongoDB collection already contains data — skipping seed.");
            return;
        }

        var orders = csvRepo.GetAll().ToList();
        if (orders.Count == 0)
        {
            logger.LogWarning("No CSV orders found to seed into MongoDB.");
            return;
        }

        mongoRepo.InsertMany(orders);
        logger.LogInformation("Seeded {Count} order(s) into MongoDB from CSV files.", orders.Count);
    }
}
