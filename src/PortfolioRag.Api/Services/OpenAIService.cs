using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;
using PortfolioRag.Api.Options;

namespace PortfolioRag.Api.Services;

/// <summary>Thin wrapper over Azure OpenAI: embeddings for search, chat completions for answers.</summary>
public class OpenAIService
{
    private readonly Lazy<AzureOpenAIClient> _client;
    private readonly AzureOpenAIOptions _o;

    public OpenAIService(IOptions<AzureOpenAIOptions> options)
    {
        _o = options.Value;
        // Created on first use, so the app still starts before Azure settings are filled in.
        // For production prefer Managed Identity: new AzureOpenAIClient(uri, new DefaultAzureCredential())
        _client = new Lazy<AzureOpenAIClient>(() =>
            new AzureOpenAIClient(new Uri(_o.Endpoint), new AzureKeyCredential(_o.ApiKey)));
    }

    private EmbeddingClient _embeddings => _client.Value.GetEmbeddingClient(_o.EmbeddingDeployment);
    private ChatClient _chat => _client.Value.GetChatClient(_o.ChatDeployment);

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        OpenAIEmbedding result = await _embeddings.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.ToFloats().ToArray();
    }

    /// <summary>Batch call: one request for many chunks is faster and cheaper than one per chunk.</summary>
    public async Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(16))
        {
            OpenAIEmbeddingCollection result = await _embeddings.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
            vectors.AddRange(result.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()));
        }
        return vectors;
    }

    public async Task<string> CompleteAsync(IEnumerable<ChatMessage> messages, CancellationToken ct)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = 0f,              // factual answers: no creativity
            MaxOutputTokenCount = 700
        };
        ChatCompletion completion = await _chat.CompleteChatAsync(messages, options, ct);
        return completion.Content.Count > 0 ? completion.Content[0].Text : "";
    }
}
