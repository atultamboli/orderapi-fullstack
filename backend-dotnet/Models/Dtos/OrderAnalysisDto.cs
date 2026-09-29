using System.Text.Json.Serialization;

namespace OrderApi.Models.Dtos;

// LEARNING NOTE — why does this record exist?
// GPT returns a JSON string. We could forward that raw string to the caller,
// but then the caller has to parse it themselves — messy and error-prone.
//
// Instead we deserialize GPT's JSON into THIS record in our service,
// and return the typed object. Our API response is clean and strongly typed.
//
// The field names here MUST match what the prompt tells GPT to return.
// If the prompt says "totalValue" and this says "TotalValue" with no attribute,
// deserialization silently gives you 0 or null — a common bug.
//
// [JsonPropertyName] maps the C# property name to the exact JSON key GPT returns.

public record OrderAnalysisDto(
    [property: JsonPropertyName("customerName")]    string CustomerName,
    [property: JsonPropertyName("totalValue")]      decimal TotalValue,
    [property: JsonPropertyName("itemCount")]       int ItemCount,
    [property: JsonPropertyName("dominantCategory")]string DominantCategory,
    [property: JsonPropertyName("oneLineSummary")]  string OneLineSummary,
    [property: JsonPropertyName("flag")]            string Flag
);
