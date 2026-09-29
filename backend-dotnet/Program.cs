using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OrderApi.Services;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// LEARNING NOTE — PORT
// Cloud hosts like Render tell your app which port to listen on through the
// PORT environment variable. Locally it's not set, so launchSettings.json wins.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Configuration.AddUserSecrets<Program>(optional: true); //What builder.Configuration.AddUserSecrets<Program>(optional: true) does.
                                                               //It explicitly tells the configuration system: "load user secrets for this project, regardless of environment."
builder.Services.AddMemoryCache(); // stores key→value pairs in RAM for the lifetime of the app
builder.Services.AddControllers();// this is a DI container

// If you scaffolded this with `dotnet new webapi`, Swagger/Swashbuckle is
// already wired up in the generated template — keep it. If you started
// from this file directly, add the Swashbuckle.AspNetCore package and
// uncomment the two lines below.
builder.Services.AddEndpointsApiExplorer(); // this is a middleware
builder.Services.AddSwaggerGen();

// ------------------------------------------------------------------
// Storage: flip "Storage:UseMongoDb" in appsettings.json to switch
// between the flat-file CSV reader and a real MongoDB database.
// The controller depends only on IOrderRepository — it never changes.
// ------------------------------------------------------------------
var useMongoDb = builder.Configuration.GetValue<bool>("Storage:UseMongoDb");

if (useMongoDb)
{
    // Bind the MongoDB settings section to a typed POCO.
    var mongoSettings = builder.Configuration
        .GetSection("Storage:MongoDb")
        .Get<MongoDbSettings>() ?? new MongoDbSettings();

    builder.Services.AddSingleton(mongoSettings);
    builder.Services.AddSingleton<MongoOrderRepository>();
    builder.Services.AddSingleton<IOrderRepository>(sp => sp.GetRequiredService<MongoOrderRepository>());
}
else
{
    // Original flat-file implementation — parse once at startup, serve from memory.
    builder.Services.AddSingleton<IOrderRepository>(sp =>
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        var dataFolder = Path.Combine(env.ContentRootPath, "Data");
        var logger = sp.GetRequiredService<ILogger<CsvOrderRepository>>();
        return new CsvOrderRepository(dataFolder, logger);
    });
}

// LEARNING NOTE — OPTIONAL AI
// Before: a missing key crashed the whole app at startup.
// Now: the app starts anyway, and only /api/ai/* returns 503 (see the
// UseWhen block below). The placeholder key is never actually sent to OpenAI.
var openAiKey = builder.Configuration["OpenAI:ApiKey"];
var aiEnabled = !string.IsNullOrWhiteSpace(openAiKey);

builder.Services.AddSingleton(new OpenAIClient(aiEnabled ? openAiKey! : "not-configured"));  // Service Registration (or Dependency Registration) in the Dependency Injection Container (DI Container).

builder.Services.AddScoped<ICrmAiService, CrmAiService>();  // Service Registration (or Dependency Registration) in the Dependency Injection Container (DI Container).
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();   // Singleton — one instance, reused across all requests
builder.Services.AddSingleton<ISemanticSearchService, SemanticSearchService>(); // Singleton — holds the in-memory index

builder.Services.AddScoped<IProductService, ProductService>(); // Service Registration (or Dependency Registration) in the Dependency Injection Container (DI Container).
builder.Services.AddScoped<OrderEtlService>(); // ETL pipeline for POST /api/orders/upload
//                         ↑               ↑
//                    "when asked for"   "give this one"

//Why interface and not directly ProductService?
//csharp// BAD — tightly coupled, can't swap implementation
//builder.Services.AddScoped<ProductService>();

//// GOOD — loosely coupled, swap anytime
//builder.Services.AddScoped<IProductService, ProductService>();
//With the interface approach, tomorrow if you want to switch to CachedProductService:
//csharp// Change ONE line in Program.cs — nothing else changes
//builder.Services.AddScoped<IProductService, CachedProductService>();
//Controller doesn't change, nothing else changes — just swap the registration.

// LEARNING NOTE — CORS
// Browsers block a page on one address (e.g. myapp.vercel.app) from calling an
// API on another (orderapi.onrender.com) unless the API says "I allow that origin".
// Origins come from config, so in the cloud you set Cors__AllowedOrigins__0,
// Cors__AllowedOrigins__1, ... as environment variables.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// LEARNING NOTE — RATE LIMITING
// Every /api/ai call costs real money. Once the API is public, anyone can call it,
// so the "ai" policy caps total AI calls per hour. Extra calls get 429 Too Many Requests.
var aiRequestsPerHour = builder.Configuration.GetValue("Ai:RequestsPerHour", 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("ai", limiter =>
    {
        limiter.PermitLimit = aiRequestsPerHour;
        limiter.Window = TimeSpan.FromHours(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();

// Seed MongoDB from CSV data on first startup (no-op if already seeded).
if (useMongoDb)
{
    var mongoRepo  = app.Services.GetRequiredService<MongoOrderRepository>();
    var csvRepo    = app.Services.GetRequiredService<IOrderRepository>();  // resolves MongoOrderRepository (same singleton)
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();

    // We need a CsvOrderRepository specifically for seeding — resolve it manually.
    var env        = app.Services.GetRequiredService<IWebHostEnvironment>();
    var csvLogger  = app.Services.GetRequiredService<ILogger<CsvOrderRepository>>();
    var csvSeedRepo = new CsvOrderRepository(Path.Combine(env.ContentRootPath, "Data"), csvLogger);

    OrderSeeder.SeedIfEmpty(mongoRepo, csvSeedRepo, seedLogger);
}

app.UseSwagger(); //middleware
app.UseSwaggerUI();

app.UseCors();
app.UseRateLimiter();

// No OpenAI key → answer every /api/ai request with a clean 503 instead of an error.
if (!aiEnabled)
{
    app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/ai"), branch =>
        branch.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return ctx.Response.WriteAsJsonAsync(new { message = "AI features are disabled: no OpenAI API key configured." });
        }));
}

app.UseAuthorization();
app.MapControllers();

// Health check — Render pings this to know the app is alive.
app.MapGet("/health", () => Results.Ok(new { status = "ok", aiEnabled }));

app.Run();

// all below 2 make a middleware pipeline
//app.UseSwagger(); // this is a middleware
//app.UseSwaggerUI(); // this is a middleware
