namespace OrderApi.Services;

// LEARNING NOTE — what does this service do?
// It wraps the OpenAI embedding API.
// You give it a string → it returns a float[] (a vector of 1536 numbers).
// That vector represents the *meaning* of the text mathematically.
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}
