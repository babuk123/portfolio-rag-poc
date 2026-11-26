namespace PortfolioRag.Api.Services;

/// <summary>
/// RAG ingestion pipeline: PDF bytes -> page text -> chunks -> embeddings -> Azure AI Search.
/// In production this usually runs in an Azure Function triggered by a new blob (BlobTrigger or Event Grid).
/// </summary>
public class IngestionService(OpenAIService openAi, SearchService search, ILogger<IngestionService> log)
{
    public async Task<int> IndexPdfAsync(byte[] pdf, string blobName, string documentName, string docType,
        string customerId, CancellationToken ct)
    {
        var pages = TextChunker.ExtractPages(pdf);
        var chunks = TextChunker.Chunk(pages);
        if (chunks.Count == 0) return 0;

        var vectors = await openAi.EmbedBatchAsync(chunks.Select(c => c.Text).ToList(), ct);

        var docs = chunks.Select((c, i) => new ChunkDocument
        {
            // Key must be URL-safe: letters, digits, dash, underscore, equals.
            Id = $"{SafeKey(blobName)}_p{c.PageNumber}_c{c.ChunkIndex}",
            Content = c.Text,
            ContentVector = vectors[i],
            CustomerId = customerId,
            DocType = docType,
            DocumentName = documentName,
            BlobName = blobName,
            PageNumber = c.PageNumber,
            CreatedUtc = DateTimeOffset.UtcNow
        }).ToList();

        await search.UploadAsync(docs, ct);
        log.LogInformation("Indexed {Count} chunks from {Blob}", docs.Count, blobName);
        return docs.Count;
    }

    private static string SafeKey(string s) =>
        new(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
}
