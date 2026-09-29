using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using OrderApi.Models;
using OrderApi.Models.Dtos;
using OrderApi.Services;

public interface ICrmAiService
{
	Task<string> SummariseCaseAsync(string caseDetails);
	Task<string> ExtractTicketDataAsync(string ticketText);
	IAsyncEnumerable<string> StreamSummariseCaseAsync(string caseDetails);
	Task<OrderAnalysisDto> AnalyseOrderAsync(string orderText);  // returns typed object, not raw string
	Task<string> AskAsync(string question, IOrderRepository orders); // NEW — function calling
}

public class CrmAiService : ICrmAiService
{
	private readonly ChatClient _chatClient;

	public CrmAiService(OpenAIClient openAiClient) // can do everything,The whole restaurant
	{
		// gpt-4o-mini = cheap + fast, good for dev/learning
		_chatClient = openAiClient.GetChatClient("gpt-4o-mini"); //A method on OpenAIClient. The full OpenAI SDK can do many things — images, audio, embeddings, transcription.
																 //GetChatClient says "I only want the chat feature" and locks it to a specific model.
	}

	public async Task<string> SummariseCaseAsync(string caseDetails)
	{
		// This is the SYSTEM PROMPT pattern from Part 2
		var messages = new List<ChatMessage> //ChatMessage = The base class for all message types. A conversation is just a List<ChatMessage>.
											 //You never create ChatMessage directly — you always use one of its subtypes:
		{
			new SystemChatMessage(           //Sets the AI's role and rules for the whole conversation. OpenAI reads this first before anything else. The AI cannot ignore it (unlike user messages which can be manipulated via prompt injection).
				"You are a CRM assistant. Summarise the case in 2 sentences. Be factual."),// --> this is the system prompt, which sets the AI's role and rules for the whole conversation. OpenAI reads this first before anything else. The AI cannot ignore it (unlike user messages which can be manipulated via prompt injection).
			new UserChatMessage(caseDetails) //User Message: Represents what the user/caller sends. In your case, the case details or ticket text coming from the API request. 
		};

		var response = await _chatClient.CompleteChatAsync(messages); //→ one ChatCompletion, Wait for full meal, serve everything at once.
		return response.Value.Content[0].Text;;
	}

	public async Task<string> ExtractTicketDataAsync(string ticketText)
	{
		// This is the STRUCTURED JSON OUTPUT pattern from Part 2
		var messages = new List<ChatMessage>
		{
			new SystemChatMessage(
				"Extract data. Return ONLY JSON: {customerName, priority, summary}"),
			new UserChatMessage(ticketText)
		};

		var response = await _chatClient.CompleteChatAsync(messages);
		return response.Value.Content[0].Text;;
	}

	public async IAsyncEnumerable<string> StreamSummariseCaseAsync(string caseDetails)
	{
		var messages = new List<ChatMessage>
		{
			new SystemChatMessage(
				"You are a CRM assistant. Summarise the case. Be factual."),
			new UserChatMessage(caseDetails)
		};

		// CompleteChatStreamingAsync sends tokens as they arrive
		await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages)) // The streaming version of CompleteChatAsync. Returns IAsyncEnumerable<StreamingChatCompletionUpdate> instead of Task<ChatCompletion>.
		{																			   // Serve each dish as it's ready
			foreach (var part in update.ContentUpdate)
			{
				if (!string.IsNullOrEmpty(part.Text))
					yield return part.Text;  // sends each token immediately
			}
		}
	}

	// LEARNING NOTE — PROMPT ENGINEERING
	// Compare this prompt with ExtractTicketDataAsync above.
	// The old prompt was: "Extract data. Return ONLY JSON: {customerName, priority, summary}"
	// That was written for support tickets — "priority" makes no sense for an order.
	//
	// A well-designed prompt has three things:
	//   1. ROLE     — tells GPT who it is ("You are an order analyst")
	//   2. RULES    — specific instructions ("Return ONLY valid JSON, no markdown")
	//   3. SCHEMA   — exact shape of the output with field names and what each means
	//
	// The more specific you are, the more reliable the output.
	// Vague prompt → GPT guesses → inconsistent output → bugs in your code
	// Specific prompt → GPT follows rules → consistent output → reliable parsing
	public async Task<OrderAnalysisDto> AnalyseOrderAsync(string orderText)
	{
		var messages = new List<ChatMessage>
		{
			new SystemChatMessage("""
				You are an order analyst for an e-commerce platform.
				Analyse the order and return ONLY valid JSON — no markdown, no explanation.

				Return exactly this shape:
				{
				  "customerName": "string — the customer's full name",
				  "totalValue": number — the total order value as a number,
				  "itemCount": number — how many line items are in the order,
				  "dominantCategory": "string — the product group that appears most",
				  "oneLineSummary": "string — one sentence describing the order",
				  "flag": "none" | "high-value" | "bulk-order" | "mixed-categories"
				}

				Rules for flag:
				- high-value: totalValue > 50
				- bulk-order: any single item has quantity > 5
				- mixed-categories: more than one distinct product group
				- none: none of the above apply
				"""),
			new UserChatMessage(orderText)
		};

		var response = await _chatClient.CompleteChatAsync(messages);
		var json     = response.Value.Content[0].Text;

		// LEARNING NOTE — JsonSerializer.Deserialize<T>
		// GPT returned a JSON string. We now parse it into our C# record.
		//
		// JsonSerializerOptions.PropertyNameCaseInsensitive = true means
		// "totalValue" in JSON maps to TotalValue in C# even if the case differs.
		// Without this, a mismatch silently gives you 0 or null — hard to debug.
		//
		// If GPT returns something that isn't valid JSON (rare but possible),
		// Deserialize throws an exception. The try/catch in the controller
		// catches that and returns a clean 503 to the caller.
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		return JsonSerializer.Deserialize<OrderAnalysisDto>(json, options)
			?? throw new InvalidOperationException("AI returned an empty response.");
	}

	// LEARNING NOTE — FUNCTION CALLING
	// Step 1: we define tools (functions GPT is allowed to call)
	// Step 2: we send the user's question + tools to GPT
	// Step 3: GPT responds with FinishReason.ToolCalls — "I need to call a function"
	// Step 4: we execute the function GPT requested, add the result to the conversation
	// Step 5: we send the conversation back to GPT
	// Step 6: GPT now has the data it needed → gives a final answer
	//
	// WHY pass IOrderRepository as a parameter and not inject it in constructor?
	// CrmAiService is an AI service — its job is talking to OpenAI.
	// Data access is a separate concern. Passing the repo as a method parameter
	// keeps the service focused. The controller (which already has both) passes it in.
	public async Task<string> AskAsync(string question, IOrderRepository orders)
	{
		// ── STEP 1: Define the tools GPT can call ────────────────────────────
		// Each tool has:
		//   name        — what GPT uses to refer to it
		//   description — GPT reads this to decide WHEN to use the tool
		//   parameters  — JSON Schema describing the arguments GPT must provide

		var summaryTool = ChatTool.CreateFunctionTool(
			functionName: "get_order_summary",
			functionDescription: "Gets header info for an order: customer name, date, total value, item count.",
			functionParameters: BinaryData.FromString("""
				{
					"type": "object",
					"properties": {
						"orderNumber": {
							"type": "string",
							"description": "The order number to look up"
						}
					},
					"required": ["orderNumber"]
				}
				""")
		);

		var itemsTool = ChatTool.CreateFunctionTool(
			functionName: "get_order_items",
			functionDescription: "Gets the line items of an order: product names, quantities, prices, and groups.",
			functionParameters: BinaryData.FromString("""
				{
					"type": "object",
					"properties": {
						"orderNumber": {
							"type": "string",
							"description": "The order number to look up"
						}
					},
					"required": ["orderNumber"]
				}
				""")
		);

		var chatOptions = new ChatCompletionOptions();
		chatOptions.Tools.Add(summaryTool);
		chatOptions.Tools.Add(itemsTool);

		// ── STEP 2: First call to GPT ─────────────────────────────────────────
		var messages = new List<ChatMessage>
		{
			new SystemChatMessage(
				"You are an order assistant. Use the available tools to look up order data and answer questions accurately. Only use data returned by tools — never guess."),
			new UserChatMessage(question)
		};

		var response = await _chatClient.CompleteChatAsync(messages, chatOptions);

		// ── STEP 3 & 4: Tool call loop ────────────────────────────────────────
		// GPT may call multiple tools (e.g. user asks about two orders).
		// We keep looping until GPT stops asking for tool calls.
		while (response.Value.FinishReason == ChatFinishReason.ToolCalls)
		{
			// Add GPT's tool request to the conversation history
			// (GPT needs to see what it asked for, to make sense of the results)
			messages.Add(new AssistantChatMessage(response.Value));

			foreach (var toolCall in response.Value.ToolCalls)
			{
				// Parse the arguments GPT provided
				var args        = JsonSerializer.Deserialize<JsonElement>(toolCall.FunctionArguments);
				var orderNumber = args.GetProperty("orderNumber").GetString()!;
				var order       = orders.GetByOrderNumber(orderNumber);

				// Execute the function GPT requested, build a result string
				string toolResult;

				if (order is null)
				{
					toolResult = $"Order {orderNumber} was not found.";
				}
				else if (toolCall.FunctionName == "get_order_summary")
				{
					toolResult = JsonSerializer.Serialize(new
					{
						orderNumber  = order.OrderNumber,
						customerName = order.CustomerName,
						orderDate    = order.OrderDate.ToString(),
						itemCount    = order.Lines.Count,
						totalValue   = order.Lines.Sum(l => l.Price * l.Quantity)
					});
				}
				else // get_order_items
				{
					toolResult = JsonSerializer.Serialize(
						order.Lines.Select(l => new
						{
							name         = l.Name,
							quantity     = l.Quantity,
							price        = l.Price,
							productGroup = l.ProductGroup
						})
					);
				}

				// ── STEP 5: Return the function result to GPT ─────────────────
				// ToolChatMessage links back to the specific tool call via Id
				// so GPT knows which result belongs to which call
				messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
			}

			// Send the updated conversation back to GPT
			response = await _chatClient.CompleteChatAsync(messages, chatOptions);
		}

		// ── STEP 6: GPT has all the data it needs — final answer ─────────────
		return response.Value.Content[0].Text;
	}
}

//What is ChatClient?
//It's a class from the OpenAI SDK specifically for chat-based models (like gpt-4o, gpt-4o-mini).
//OpenAIClient is the main entry point — it connects to OpenAI. But it can do many things: images, audio, embeddings, etc.
//GetChatClient("gpt-4o-mini") says "give me the part that handles chat specifically."
// OpenAIClient = the full OpenAI connection
//// ChatClient   = just the chat part of it
//_chatClient = openAiClient.GetChatClient("gpt-4o-mini");
//Think of it like a TV remote — OpenAIClient is the remote, ChatClient is the button for one specific channel.

//Why private readonly ChatClient _chatClient?

//private — only this class uses it, nothing outside needs it
//readonly — set once in the constructor, never changed again. This is a safety guarantee — you're saying "this will always point to the same ChatClient for the life of this object"
//_chatClient — the underscore prefix is a C# convention for private fields (you'll see this everywhere in professional code)


//Why not just create it inside each method?
// BAD — creates a new ChatClient on every call
//public async Task<string> SummariseCaseAsync(string caseDetails)
//{
//	var chatClient = openAiClient.GetChatClient("gpt-4o-mini"); // wasteful
//	...
//}
//Creating it once in the constructor and reusing it is more efficient. Same reason we use DI — create once, reuse many times.