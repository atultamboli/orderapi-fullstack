using Anthropic.SDK;
using Anthropic.SDK.Messaging;

// LEARNING NOTE — this is the Anthropic (Claude) version of CrmAiService.
// The concepts are identical: system prompt, user message, complete or stream.
// The class names differ because it's a different SDK, but the pattern is the same.
// Compare side-by-side with CrmAiService.cs to see what changes between providers.

public interface IClaudeAiService
{
    Task<string> SummariseCaseAsync(string caseDetails);
    IAsyncEnumerable<string> StreamSummariseCaseAsync(string caseDetails);
}

public class ClaudeAiService : IClaudeAiService
{
    private readonly AnthropicClient _client;

    public ClaudeAiService(AnthropicClient client)
    {
        _client = client;
    }

    public async Task<string> SummariseCaseAsync(string caseDetails)
    {
        // SDK v5 change: Model is now a plain string, not AnthropicModels.XYZ.
        // System is now List<SystemMessage>, not a plain string.
        var request = new MessageParameters
        {
            Model = "claude-3-5-haiku-20241022",
            MaxTokens = 1024,
            System = new List<SystemMessage>
            {
                new SystemMessage("You are a CRM assistant. Summarise the case in 2 sentences. Be factual.")
            },
            Messages = new List<Message>
            {
                new(RoleType.User, caseDetails)
            }
        };

        var response = await _client.Messages.GetClaudeMessageAsync(request);
        return response.Content[0].ToString() ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamSummariseCaseAsync(string caseDetails)
    {
        var request = new MessageParameters
        {
            Model = "claude-3-5-haiku-20241022",
            MaxTokens = 1024,
            System = new List<SystemMessage>
            {
                new SystemMessage("You are a CRM assistant. Summarise the case. Be factual.")
            },
            Messages = new List<Message>
            {
                new(RoleType.User, caseDetails)
            }
        };

        await foreach (var chunk in _client.Messages.StreamClaudeMessageAsync(request))
        {
            if (chunk.Delta?.Text != null)
                yield return chunk.Delta.Text;
        }
    }
}
