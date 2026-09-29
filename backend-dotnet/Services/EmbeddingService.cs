using OpenAI;
using OpenAI.Embeddings;

namespace OrderApi.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public EmbeddingService(OpenAIClient openAiClient)
    {
        // text-embedding-3-small — OpenAI's embedding model
        // Converts text → 1536 numbers (a vector)
        // Cheap and fast — good for learning
        _client = openAiClient.GetEmbeddingClient("text-embedding-3-small");
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var result = await _client.GenerateEmbeddingAsync(text);

        // ToFloats() gives us the 1536 numbers as a ReadOnlyMemory<float>
        // .ToArray() converts it to a plain float[] we can store and compare
        return result.Value.ToFloats().ToArray();
    }
}
